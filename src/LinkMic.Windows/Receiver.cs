using System.Net;
using System.Net.Sockets;
using InTheHand.Net;
using InTheHand.Net.Sockets;
using LinkMic.Core;
using System.Diagnostics;

namespace LinkMic.Windows;

public sealed record PairedPhone(string Display, string Address);
public sealed record AudioStats(long Frames, int Buffered, long Dropped, long Underruns, double Level, TimeSpan ConnectedFor = default);

public sealed class Receiver : IDisposable
{
    private readonly Func<OutputDevice, IAudioOutput>? outputFactory;
    private readonly IPAddress listenAddress;
    public Receiver(Func<OutputDevice, IAudioOutput>? outputFactory = null, IPAddress? listenAddress = null) { this.outputFactory = outputFactory; this.listenAddress = listenAddress ?? IPAddress.Any; }
    private readonly CancellationTokenSource stop = new();
    private TcpListener? listener; private TcpClient? tcp; private DisposeOnce<BluetoothClient>? bluetooth;
    private int stopping;
    public event Action<string>? Status; public event Action<AudioStats>? Stats;
    public event Action<string>? AudioProcessingError;
    private int noiseReduction;
    public bool NoiseReductionEnabled { get => Volatile.Read(ref noiseReduction) != 0; set => Volatile.Write(ref noiseReduction, value ? 1 : 0); }
    public double Gain { get; set; } = 1; public bool Muted { get; set; }
    public bool IsStopping => stop.IsCancellationRequested;

    public static List<PairedPhone> PairedDevices()
    {
        using var client = new BluetoothClient();
        return client.PairedDevices.Select(phone => new PairedPhone(phone.DeviceName, phone.DeviceAddress.ToString())).ToList();
    }

    public async Task RunAsync(bool wifi, string address, string code, OutputDevice output)
    {
        var token = stop.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            using IAudioOutput sink = outputFactory?.Invoke(output) ?? (output.Driver ? new VirtualMicOutput() : new WaveOutput(output));
            Diagnostics.Write($"session-start transport={(wifi ? "wifi" : "bluetooth")} output={output.Name}");
            Stream stream;
            if (wifi)
            {
                listener = new TcpListener(listenAddress, AudioProtocol.Port); listener.Start(2);
                Status?.Invoke("等待手机连接 · TCP 42791");
                while (true)
                {
                    tcp = await listener.AcceptTcpClientAsync(token); tcp.NoDelay = true;
                    try { stream = tcp.GetStream(); await ReceiveAsync(stream, code, sink, token); return; }
                    catch (InvalidDataException error) { Status?.Invoke(error.Message + " 等待重新连接。"); tcp.Dispose(); await Task.Delay(1000, token); }
                }
            }
            using var ownedBluetooth = new DisposeOnce<BluetoothClient>(new BluetoothClient { Authenticate = true, Encrypt = true });
            bluetooth = ownedBluetooth;
            var client = ownedBluetooth.Value;
            using var cancel = token.Register(ownedBluetooth.Dispose);
            Status?.Invoke("正在连接蓝牙手机…");
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); connectTimeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var abort = connectTimeout.Token.Register(ownedBluetooth.Dispose);
            await Task.Run(() => client.Connect(BluetoothAddress.Parse(address.Replace(":", "").Replace("-", "")), AudioProtocol.BluetoothService), token).WaitAsync(connectTimeout.Token);
            connectTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            stream = client.GetStream(); await ReceiveAsync(stream, code, sink, token);
        }
        finally { CloseTransport(); }
    }

    private async Task ReceiveAsync(Stream stream, string code, IAudioOutput sink, CancellationToken token)
    {
        var pairing = await Pairing.AcceptAsync(stream, code, DeviceTrustStore.Open(), token); Status?.Invoke($"已连接 · {pairing.Name}");
        var connected = Stopwatch.StartNew();
        var buffer = new PlayoutBuffer(); long frames = 0;
        using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
        var read = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(session.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    buffer.Push(await AudioProtocol.ReadAsync(stream, pairing.Session, deadline.Token)); Interlocked.Increment(ref frames);
                }
            }
            finally { await session.CancelAsync(); }
        }, session.Token);
        try
        {
            using var denoiser = new RnNoiseProcessor();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20)); int ticks = 0;
            while (await timer.WaitForNextTickAsync(session.Token))
            {
                var samples = buffer.Take();
                try { denoiser.Process(samples, NoiseReductionEnabled); }
                catch (Exception error) when (RnNoiseProcessor.IsLibraryError(error))
                {
                    NoiseReductionEnabled = false; denoiser.Reset(); Diagnostics.Write("rnnoise-bypass", error);
                    AudioProcessingError?.Invoke("RNNoise 暂不可用，已关闭降噪并继续传输原始声音。");
                }
                double gain = Muted ? 0 : Math.Clamp(Gain, 0, 2), energy = 0;
                for (int i = 0; i < samples.Length; i++) { samples[i] = (short)Math.Clamp(samples[i] * gain, short.MinValue, short.MaxValue); energy += (double)samples[i] * samples[i]; }
                sink.Write(AudioProtocol.ToPcm(samples));
                if (++ticks % 5 == 0) Stats?.Invoke(new(Interlocked.Read(ref frames), buffer.Count, buffer.Dropped, buffer.Underruns, Math.Min(1, Math.Sqrt(energy / samples.Length) / 12000), connected.Elapsed));
            }
        }
        finally { await session.CancelAsync(); stream.Dispose(); try { await read; } catch (OperationCanceledException) { } }
    }
    public void RequestStop()
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0) return;
        Diagnostics.Write("session-stop-requested");
        Cleanup(stop.Cancel, "cancel");
        CloseTransport();
    }
    private void CloseTransport()
    {
        Cleanup(() => listener?.Stop(), "tcp-listener");
        Cleanup(() => tcp?.Dispose(), "tcp-client");
        Cleanup(() => bluetooth?.Dispose(), "bluetooth-client");
    }
    private static void Cleanup(Action close, string operation)
    {
        try { close(); } catch (Exception error) { Diagnostics.Write("cleanup-" + operation, error); }
    }
    public void Dispose() => RequestStop();
}
