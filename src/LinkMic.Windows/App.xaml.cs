using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LinkMic.Windows;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, error) => Diagnostics.Write("dispatcher-unhandled", error.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, error) => Diagnostics.Write("process-unhandled", error.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, error) => Diagnostics.Write("task-unobserved", error.Exception);
        if (e.Args.Length == 2 && e.Args[0] == "--rnnoise-check")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await RnNoiseCheck.RunAsync(e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1], "rnnoise-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--rnnoise-availability")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bool available = RnNoiseProcessor.IsAvailable(out string reason);
            var probe = new MainWindow();
            File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new { available, uiEnabled = ((System.Windows.Controls.CheckBox)probe.FindName("NoiseReduction")).IsEnabled, reason }));
            probe.Close(); Shutdown(0); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--pairing-store-check")
        {
            try { DeviceTrustStoreCheck.Run(e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1], "device-trust-store-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--output-check")
        {
            try { OutputRoutingCheck.Run(e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1], "output-routing-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--audio-devices")
        {
            try { File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(WaveOutput.Devices(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); Shutdown(0); }
            catch (Exception error) { Diagnostics.Write("audio-device-inventory", error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--disconnect-check")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await DisconnectCheck.RunAsync(e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1], "disconnect-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--bluetooth-check")
        {
            object result;
            try { var devices = await Task.Run(Receiver.PairedDevices); result = new { available = true, pairedDeviceCount = devices.Count, connectionTested = false }; }
            catch (Exception error) { result = new { available = false, error = error.Message, connectionTested = false }; }
            File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); Shutdown(); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--integration-check")
        {
            try { await IntegrationCheck.RunAsync(e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1], "windows-receiver-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        var window = new MainWindow(); MainWindow = window;
        if (e.Args.Length == 2 && e.Args[0] == "--theme-check")
        {
            try { await ThemeCheck.RunAsync(window, e.Args[1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[1], "theme-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--render-preview")
        {
            window.SetAppearance(e.Args.Length >= 3 && e.Args[2] == "dark" ? "Dark" : "Light", persist: false);
            await RenderPreviewAsync(window, e.Args[1]); Shutdown(); return;
        }
        window.Show();
    }
    internal static async Task RenderPreviewAsync(MainWindow window, string path, int width = 1120, int height = 800)
    {
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        // A content-only bitmap cannot include DWM's wallpaper material. Paint the
        // theme's opaque fallback behind it; native-window captures are separate.
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle((Brush)window.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, width, height));
        bitmap.Render(backdrop); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.GetFullPath(path)); encoder.Save(file);
    }
}
