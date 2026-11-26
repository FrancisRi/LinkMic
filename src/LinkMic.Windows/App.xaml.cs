using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace LinkMic.Windows;

public partial class App : Application
{
    private MainWindow? mainWindow;

    public App()
    {
        UnhandledException += (_, error) => Diagnostics.Write("dispatcher-unhandled", error.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, error) => Diagnostics.Write("process-unhandled", error.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, error) => Diagnostics.Write("task-unobserved", error.Exception);
        try { InitializeComponent(); }
        catch (Exception error) { Diagnostics.Write("xaml-initialize", error); throw; }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length >= 3 && await RunCheckAsync(args[1], args[2], args.ElementAtOrDefault(3))) return;

        mainWindow = new MainWindow();
        mainWindow.Activate();
    }

    private async Task<bool> RunCheckAsync(string command, string path, string? mode)
    {
        string? errorFile = command switch
        {
            "--rnnoise-check" => "rnnoise-error.txt",
            "--pairing-store-check" => "device-trust-store-error.txt",
            "--output-check" => "output-routing-error.txt",
            "--disconnect-check" => "disconnect-error.txt",
            "--integration-check" => "windows-receiver-error.txt",
            "--theme-check" => "theme-error.txt",
            "--rnnoise-availability" or "--audio-devices" or "--bluetooth-check" or "--render-preview" => "",
            _ => null
        };
        if (errorFile is null) return false;

        try
        {
            switch (command)
            {
                case "--rnnoise-check":
                    await RnNoiseCheck.RunAsync(path);
                    break;
                case "--rnnoise-availability":
                    bool available = RnNoiseProcessor.IsAvailable(out string reason);
                    var probe = new MainWindow();
                    File.WriteAllText(path, JsonSerializer.Serialize(new { available, uiEnabled = probe.FindControl<CheckBox>("NoiseReduction").IsEnabled, reason }));
                    break;
                case "--pairing-store-check":
                    DeviceTrustStoreCheck.Run(path);
                    break;
                case "--output-check":
                    OutputRoutingCheck.Run(path);
                    break;
                case "--audio-devices":
                    File.WriteAllText(path, JsonSerializer.Serialize(WaveOutput.Devices(), new JsonSerializerOptions { WriteIndented = true }));
                    break;
                case "--disconnect-check":
                    // Keep the WinUI dispatcher alive across the test windows' native closes.
                    mainWindow = new MainWindow();
                    await DisconnectCheck.RunAsync(path);
                    break;
                case "--bluetooth-check":
                    await CheckBluetoothAsync(path);
                    break;
                case "--integration-check":
                    await IntegrationCheck.RunAsync(path);
                    break;
                case "--theme-check":
                    mainWindow = new MainWindow();
                    mainWindow.Activate();
                    await ThemeCheck.RunAsync(mainWindow, path);
                    break;
                case "--render-preview":
                    mainWindow = new MainWindow();
                    mainWindow.SetAppearance(mode == "dark" ? "Dark" : "Light", persist: false);
                    mainWindow.Activate();
                    await RenderPreviewAsync(mainWindow, path);
                    break;
            }
            Environment.ExitCode = 0;
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            Diagnostics.Write(command, error);
            if (errorFile.Length != 0) File.WriteAllText(Path.Combine(path, errorFile), error.ToString());
        }
        Exit();
        return true;
    }

    private static async Task CheckBluetoothAsync(string path)
    {
        object result;
        try
        {
            var devices = await Task.Run(Receiver.PairedDevices);
            result = new { available = true, pairedDeviceCount = devices.Count, connectionTested = false };
        }
        catch (Exception error) { result = new { available = false, error = error.Message, connectionTested = false }; }
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static Task WaitForLayoutAsync(Window window)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            window.Content.UpdateLayout();
            ready.SetResult();
        })) throw new InvalidOperationException("The UI dispatcher is shutting down.");
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task RenderPreviewAsync(MainWindow window, string path, int width = 1120, int height = 800)
    {
        var root = (Control)window.Content;
        var originalSize = window.AppWindow.Size;
        var originalBackground = root.Background;
        try
        {
            window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * window.Scale), (int)Math.Round(height * window.Scale)));
            await WaitForLayoutAsync(window);

            // RenderTargetBitmap captures XAML. Use a solid canvas for the preview
            // because the system backdrop is composed outside the XAML tree.
            Color background = window.Theme.HighContrast ? new UISettings().GetColorValue(UIColorType.Background)
                : root.ActualTheme == ElementTheme.Dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
            root.Background = new SolidColorBrush(background);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
            await SavePngAsync(path, pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        finally
        {
            root.Background = originalBackground;
            window.AppWindow.Resize(originalSize);
        }
    }

    internal static async Task SavePngAsync(string path, byte[] pixels, int width, int height)
    {
        using var output = File.Create(path);
        using IRandomAccessStream stream = output.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
