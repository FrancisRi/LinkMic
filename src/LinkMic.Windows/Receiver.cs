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
    private Stream? pairedStream;
    private readonly TaskCompletionSource capabilitySent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int stopping;
    public event Action<string>? Status; public event Action<AudioStats>? Stats;
    public event Action<string>? AudioProcessingError;
    private int noiseReduction;
    public bool NoiseReductionEnabled { get => Volatile.Read(ref noiseReduction) != 0; set => Volatile.Write(ref noiseReduction, value ? 1 : 0); }
    public double Gain { get; set; } = 1; public bool Muted { get; set; }
    public bool IsStopping => Volatile.Read(ref stopping) != 0;

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
                    catch (InvalidDataException error) when (Volatile.Read(ref pairedStream) is null) { Status?.Invoke(error.Message + " 等待重新连接。"); tcp.Dispose(); await Task.Delay(1000, token); }
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
        finally { Volatile.Write(ref pairedStream, null); CloseTransport(); finished.TrySetResult(); }
    }

    private async Task ReceiveAsync(Stream stream, string code, IAudioOutput sink, CancellationToken token)
    {
        (uint Session, string Name) pairing;
        try { pairing = await Pairing.AcceptAsync(stream, code, DeviceTrustStore.Open(), token); }
        catch (EndOfStreamException) { return; }
        Volatile.Write(ref pairedStream, stream);
        await stream.WriteAsync(new[] { (byte)SessionControl.Ready }, token);
        capabilitySent.TrySetResult();
        token.ThrowIfCancellationRequested();
        Status?.Invoke($"已连接 · {pairing.Name}");
        var connected = Stopwatch.StartNew();
        var buffer = new PlayoutBuffer(); long frames = 0;
        using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
        var read = Task.Run(async () =>
        {
            bool peerSupportsDisconnect = false;
            try
            {
                while (true)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(session.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    (AudioFrame? frame, SessionControl control) packet;
                    try { packet = await AudioProtocol.ReadSessionPacketAsync(stream, pairing.Session, deadline.Token); }
                    catch (OperationCanceledException) when (!session.IsCancellationRequested) { throw new TimeoutException("接收音频超时。"); }
                    switch (packet.control)
                    {
                        case SessionControl.Ready: peerSupportsDisconnect = true; break;
                        case SessionControl.Disconnect when peerSupportsDisconnect: return;
                        case SessionControl.Disconnect: throw new InvalidDataException("未协商断开信号。");
                        case SessionControl.EndOfStream:
                            if (peerSupportsDisconnect && !IsStopping) throw new IOException("连接意外中断。");
                            return; // A legacy phone only signals orderly closure with EOF.
                        default: buffer.Push(packet.frame!); Interlocked.Increment(ref frames); break;
                    }
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
                if (IsStopping) continue;
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
        catch (OperationCanceledException) when (session.IsCancellationRequested && !token.IsCancellationRequested) { }
        finally { await session.CancelAsync(); stream.Dispose(); try { await read; } catch (OperationCanceledException) { } }
    }
    public void RequestStop()
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0) return;
        Diagnostics.Write("session-stop-requested");
        var stream = Volatile.Read(ref pairedStream);
        if (stream is not null) { _ = Task.Run(() => DisconnectAsync(stream)); return; }
        CancelAndClose();
    }

    private async Task DisconnectAsync(Stream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        try
        {
            await capabilitySent.Task.WaitAsync(deadline.Token);
            await stream.WriteAsync(new[] { (byte)SessionControl.Disconnect }, deadline.Token).AsTask().WaitAsync(deadline.Token);
            // The peer closes once it has read the message. Bound the wait for old or stalled peers.
            await finished.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Diagnostics.Write("disconnect-signal", error); }
        finally { CancelAndClose(); }
    }

    private void CancelAndClose()
    {
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
