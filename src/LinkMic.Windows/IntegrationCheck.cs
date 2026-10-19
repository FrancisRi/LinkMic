using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LinkMic.Core;

namespace LinkMic.Windows;
internal static class IntegrationCheck
{
    public static async Task RunAsync(string directory, Action<int>? onFrame = null)
    {
        var sink = new CaptureOutput(); using var receiver = new Receiver(_ => sink, IPAddress.Loopback);
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new List<string>(); AudioStats? latest = null;
        receiver.Status += value => { lock (statuses) statuses.Add(value); if (value.StartsWith("等待")) listening.TrySetResult(); };
        receiver.Stats += value => latest = value;
        var running = Task.Run(() => receiver.RunAsync(true, "", "123456", new("integration sink", -1)));
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, AudioProtocol.Port); client.NoDelay = true;
        using var stream = client.GetStream(); await stream.WriteAsync("LMIC/1 123456 AndroidFixture\n"u8.ToArray());
        string reply = await Pairing.ReadLineAsync(stream, default); uint session = uint.Parse(reply.Split(' ')[1]);
        byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(directory, "android-frames.bin"));
        const int packetSize = 516;
        for (int i = 0; i < 65; i++)
        {
            onFrame?.Invoke(i);
            var packet = fixture.AsSpan(i * packetSize, packetSize).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), session);
            await stream.WriteAsync(packet); await Task.Delay(20);
        }
        receiver.Muted = true; await Task.Delay(200);
        receiver.Dispose();
        try { await running.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) when (receiver.IsStopping && running.IsCompleted) { }
        if (latest is null || latest.Frames != 65 || sink.Writes < 50 || sink.NonSilentWrites < 35 || sink.LastPeak != 0 || !sink.Disposed)
            throw new Exception($"Receiver check failed: frames={latest?.Frames}, writes={sink.Writes}, sound={sink.NonSilentWrites}, lastPeak={sink.LastPeak}, disposed={sink.Disposed}");
        var result = new { passed = true, source = "actual Android Java encoder packets", receiver = "production Windows Receiver", frames = latest.Frames, writes = sink.Writes, nonSilentWrites = sink.NonSilentWrites, mutedFinalOutput = sink.LastPeak == 0, cancellationCompleted = running.IsCompleted, outputDisposed = sink.Disposed, hardwareAudioOutput = false, hardwareBluetooth = false, statusMessages = statuses };
        await File.WriteAllTextAsync(Path.Combine(directory, "windows-receiver-test.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class CaptureOutput : IAudioOutput
    {
        public int Writes, NonSilentWrites, LastPeak; public bool Disposed;
        public void Write(byte[] pcm)
        {
            Writes++; int peak = 0; for (int i = 0; i < pcm.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i))));
            LastPeak = peak; if (peak > 1000) NonSilentWrites++;
        }
        public void Dispose() => Disposed = true;
    }
}
