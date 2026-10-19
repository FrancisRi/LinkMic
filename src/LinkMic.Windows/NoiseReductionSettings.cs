using System.IO;
using System.Text.Json;

namespace LinkMic.Windows;
internal static class NoiseReductionSettings
{
    private sealed record Settings(bool RnNoiseEnabled = false);
    private static string PathName => Path.Combine(Environment.GetEnvironmentVariable("LINKMIC_STATE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkMic"), "audio-settings.json");
    public static bool Load()
    {
        try { return File.Exists(PathName) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName))?.RnNoiseEnabled == true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { Diagnostics.Write("audio-settings-read", error); return false; }
    }
    public static void Save(bool enabled)
    {
        string path = PathName; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(enabled))); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
