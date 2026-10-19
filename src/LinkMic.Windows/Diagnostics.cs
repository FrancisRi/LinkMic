using System.Text;

namespace LinkMic.Windows;
internal static class Diagnostics
{
    private static readonly object sync = new();
    public static string DirectoryPath => Environment.GetEnvironmentVariable("LINKMIC_LOG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkMic", "logs");
    public static void Write(string operation, Exception? error = null)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "linkmic.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
                string native = error is System.ComponentModel.Win32Exception win32 ? $"Win32Error={win32.NativeErrorCode}\n" : "";
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {operation}\n{native}{(error is null ? "" : error + "\n")}", Encoding.UTF8);
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
