using System.Text.Json;

namespace LinkMic.Windows;
internal static class OutputRoutingCheck
{
    public static void Run(string directory)
    {
        var cases = new List<string>();
        void Check(bool passed, string name) { if (!passed) throw new Exception(name); cases.Add(name); }
        var cable = WaveOutput.DescribeDevice(4, "CABLE Input (VB-Audio Virtual C");
        Check(cable.Cable && !cable.Driver && cable.Id == 4, "truncated base cable selects standard playback API");
        Check(WaveOutput.DescribeDevice(1, "cable input (vb-audio virtual c").Cable, "case-insensitive base cable");
        Check(!WaveOutput.DescribeDevice(1, "CABLE Output (VB-Audio Virtual").Cable, "capture endpoint never classified as render cable");
        Check(!WaveOutput.DescribeDevice(1, "CABLE In 16 Ch (VB-Audio Virtual").Cable, "16-channel endpoint not selected automatically");
        Check(!WaveOutput.DescribeDevice(1, "CABLE Input (Other Vendor)").Cable, "unrelated names not classified as VB-CABLE");
        var speaker = WaveOutput.DescribeDevice(0, "Speakers (Test)");
        var devices = WaveOutput.OrderDevices([speaker, cable], false);
        Check(devices[0] == cable && devices[0].Hint.Contains("CABLE Output"), "cable preferred and correct recording endpoint explained");
        Check(!devices.Any(output => output.Driver), "missing experimental driver hidden");
        devices = WaveOutput.OrderDevices([speaker], false);
        Check(devices[0].Id == -1 && !devices[0].Cable, "monitoring remains explicit without installed cable");
        devices = WaveOutput.OrderDevices([speaker, cable], true);
        Check(devices[0] == cable && devices[^1].Driver, "installed experimental driver optional after cable");
        foreach (int id in new[] { -1, int.MaxValue })
        {
            bool rejected = false;
            try { using var unexpected = new WaveOutput(new("Removed cable", id, Cable: true)); }
            catch (IOException) { rejected = true; }
            Check(rejected, $"missing cable {id} cannot fall back to default speakers");
        }
        foreach (var actual in WaveOutput.Devices().Where(output => !output.Driver && output.Id >= 0))
        {
            bool rejected = false;
            try { using var unexpected = new WaveOutput(actual with { DeviceName = "__stale_device_name__" }); }
            catch (IOException) { rejected = true; }
            Check(rejected, $"changed device {actual.Id} rejected before audio open");
        }
        File.WriteAllText(Path.Combine(directory, "output-routing-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, cases, hardwareAudioOpened = false, driverInstalled = false,
            physicalVbCableTested = false, actualDevices = WaveOutput.Devices()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
