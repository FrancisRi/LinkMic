using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LinkMic.Core;

try { await RunAsync(args); }
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

static async Task RunAsync(string[] args)
{
string directory = args[0];
var results = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); }
void Pass(string label) { results.Add(label); Console.WriteLine("PASS " + label); }
async Task Reject(Func<Task> action, string label)
{
    try { await action(); } catch (InvalidDataException) { Pass(label); return; } catch (EndOfStreamException) { Pass(label); return; }
    throw new Exception("Expected rejection: " + label);
}
var packetBytes = File.ReadAllBytes(Path.Combine(directory, "android-frames.bin"));
var reference = File.ReadAllBytes(Path.Combine(directory, "reference.pcm"));
using var fragmented = new FragmentedStream(packetBytes);
using var decoded = new MemoryStream();
double errorEnergy = 0, signalEnergy = 0;
for (int i = 0; i < 160; i++)
{
    var frame = await AudioProtocol.ReadAsync(fragmented, 987654321, default);
    Check(frame.Sequence == i && frame.TimestampMicros == (ulong)i * 20000, "Cross-language frame fields");
    var pcm = AudioProtocol.ToPcm(frame.Samples); decoded.Write(pcm);
    for (int j = 0; j < frame.Samples.Length; j++)
    {
        var sample = BinaryPrimitives.ReadInt16LittleEndian(reference.AsSpan(i * 1920 + j * 2));
        errorEnergy += Math.Pow(frame.Samples[j] - sample, 2); signalEnergy += (double)sample * sample;
    }
}
double relativeError = Math.Sqrt(errorEnergy / signalEnergy);
Check(relativeError < .06, $"ADPCM voice-band distortion {relativeError}");
Pass($"Android Java → .NET: 160 packets, fragmented reads, normalized RMS error {relativeError:P2}");
var valid = AudioProtocol.Encode(new(0, 0, 987654321, new short[960]));
var huge = (byte[])valid.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(24), uint.MaxValue);
await Reject(async () => await AudioProtocol.ReadAsync(new MemoryStream(huge), 987654321, default), "Reject oversized length before allocation");
await Reject(async () => await AudioProtocol.ReadAsync(new MemoryStream(valid), 123, default), "Reject stale session");
await Reject(async () => await AudioProtocol.ReadAsync(new MemoryStream(valid[..^1]), 987654321, default), "Reject truncated frame");
var invalid = (byte[])valid.Clone(); invalid[34] = 255;
await Reject(async () => await AudioProtocol.ReadAsync(new MemoryStream(invalid), 987654321, default), "Reject invalid ADPCM state");
var buffer = new PlayoutBuffer();
foreach (uint sequence in new uint[] { uint.MaxValue - 1, uint.MaxValue, 0, 1, 2, 3 }) buffer.Push(new(sequence, 0, 1, new short[960]));
Check(buffer.Count == 5 && buffer.Dropped == 1, "Queue bounded across sequence wrap");
buffer.Push(new(3, 0, 1, new short[960])); Check(buffer.Count == 5 && buffer.Dropped == 2, "Duplicate rejected");
for (int i = 0; i < 7; i++) Check(buffer.Take().Length == 960, "Silence on underrun");
Pass("Bounded playout, sequence wrap, duplicate rejection and underrun silence");

using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var server = Task.Run(async () =>
{
    using var client = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = client.GetStream();
    var identity = await Pairing.AcceptAsync(stream, "123456", timeout.Token);
    Check(identity.Name == "TestPhone", "Phone identity");
    for (int i = 0; i < 25; i++) { var frame = await AudioProtocol.ReadAsync(stream, identity.Session, timeout.Token); Check(frame.Sequence == i, "TCP frame order"); }
}, timeout.Token);
using (var client = new TcpClient())
{
    await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); using var stream = client.GetStream();
    await stream.WriteAsync("LMIC/1 123456 TestPhone\n"u8.ToArray(), timeout.Token);
    string reply = await Pairing.ReadLineAsync(stream, timeout.Token); uint session = uint.Parse(reply.Split(' ')[1]);
    for (uint i = 0; i < 25; i++) await stream.WriteAsync(AudioProtocol.Encode(new(i, i * 20000, session, new short[960])), timeout.Token);
    await server;
}
Pass("Real loopback TCP pairing and 25 audio frames");
await RememberedPairingTests.RunAsync(directory);
Pass("Java/.NET remembered pairing: enrollment, PIN-free reconnect, restart, revocation, replay and tampering");
using (var stream = new MemoryStream())
{
    stream.Write(Encoding.ASCII.GetBytes("LMIC/1 000000 Intruder\n")); stream.Position = 0;
    await Reject(async () => await Pairing.AcceptAsync(stream, "123456", default), "Reject wrong pairing code");
}
var pcmBytes = decoded.ToArray();
using (var wav = new BinaryWriter(File.Create(Path.Combine(directory, "android-to-windows.wav"))))
{
    wav.Write("RIFF"u8); wav.Write(36 + pcmBytes.Length); wav.Write("WAVEfmt "u8); wav.Write(16); wav.Write((short)1); wav.Write((short)1); wav.Write(48000); wav.Write(96000); wav.Write((short)2); wav.Write((short)16); wav.Write("data"u8); wav.Write(pcmBytes.Length); wav.Write(pcmBytes);
}
File.WriteAllText(Path.Combine(directory, "test-results.json"), JsonSerializer.Serialize(new { passed = results.Count, results, normalizedRmsError = relativeError, hardwareBluetoothTested = false, androidMicrophoneTested = false, virtualDriverTested = false }, new JsonSerializerOptions { WriteIndented = true }));
}

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
}
