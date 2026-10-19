using InTheHand.Net.Sockets;
using System.Text.Json;
using LinkMic.Windows;
using System.Buffers.Binary;

var results = new List<object>();
foreach (string scenario in args.Contains("--abi-only") ? Array.Empty<string>() : new[] { "repeated-dispose", "linked-cancel-callbacks" })
{
    try
    {
        var client = new DisposeOnce<BluetoothClient>(new BluetoothClient());
        if (scenario == "repeated-dispose") { client.Dispose(); client.Dispose(); client.Dispose(); }
        else
        {
            using var stop = new CancellationTokenSource();
            using var cancel = stop.Token.Register(() => client.Dispose());
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            using var abort = connect.Token.Register(() => client.Dispose());
            stop.Cancel(); client.Dispose();
        }
        results.Add(new { scenario, passed = true });
    }
    catch (Exception error) { results.Add(new { scenario, passed = false, error = error.ToString() }); Environment.ExitCode = 1; }
}
try
{
    var info = new byte[32];
    uint[] fields = [32, 1, 48000, 1, 16, 1920, 9600, 1];
    for (int i = 0; i < fields.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(i * 4), fields[i]);
    VirtualMicOutput.ValidateDriverInfo(info, 32);
    foreach (int offset in new[] { 0, 4, 8, 12, 16, 20, 24, 28 })
    {
        var invalid = (byte[])info.Clone(); invalid.AsSpan(offset, 4).Clear();
        try { VirtualMicOutput.ValidateDriverInfo(invalid, 32); throw new Exception("Invalid ABI field accepted: " + offset); }
        catch (NotSupportedException) { }
    }
    try { VirtualMicOutput.ValidateDriverInfo(info, 31); throw new Exception("Truncated ABI accepted"); }
    catch (InvalidDataException) { }
    results.Add(new { scenario = "driver-abi-validation", passed = true });
}
catch (Exception error) { results.Add(new { scenario = "driver-abi-validation", passed = false, error = error.ToString() }); Environment.ExitCode = 1; }
string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.Length != 0 && !args[0].StartsWith("--")) File.WriteAllText(args[0], json);
