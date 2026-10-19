using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LinkMic.Windows;
internal static class NativeWindowCheck
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    public static async Task RunAsync(MainWindow window, string directory)
    {
        var frames = new List<object>();
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = GetWindowLongPtr(hwnd, -16).ToInt64();
        const long expected = 0x00C00000 | 0x00040000 | 0x00010000 | 0x00020000;
        if ((style & expected) != expected || window.AllowsTransparency || window.WindowStyle != WindowStyle.SingleBorderWindow)
            throw new Exception("Native caption, resizing, minimize/maximize buttons or composition were lost.");
        window.ShowActivated = false;
        window.Show();
        try
        {
            foreach (string mode in new[] { "Light", "Dark" })
            {
                window.SetAppearance(mode, persist: false);
                await WaitForCompositionAsync(window);
                int backdropResult = DwmGetWindowAttribute(hwnd, 38, out int backdrop, sizeof(int));
                int darkResult = DwmGetWindowAttribute(hwnd, 20, out int dark, sizeof(int));
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemParameters.HighContrast && (backdropResult != 0 || backdrop != 2))
                    throw new Exception($"Native Mica was not configured: HRESULT={backdropResult}, backdrop={backdrop}.");
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast && (darkResult != 0 || dark != (mode == "Dark" ? 1 : 0)))
                    throw new Exception($"Native caption theme did not follow {mode}.");
                // Windows animates the Mica theme transition after its attributes
                // have changed. Allow that system animation to settle for the image.
                await Task.Delay(350);
                await WaitForCompositionAsync(window);
                bool captured = Capture(hwnd, Path.Combine(directory, $"windows11-{mode.ToLowerInvariant()}-window.png"));
                frames.Add(new { mode, dwmBackdrop = backdrop, backdropHResult = backdropResult, nativeDarkCaption = dark, darkCaptionHResult = darkResult, nativeFrameCaptured = captured });
            }
            foreach (var (name, title) in new[] { ("SettingsNav", "设置"), ("GuideNav", "使用指南"), ("LogsNav", "运行日志"), ("ConnectNav", "连接与音频") })
            {
                ((RadioButton)window.FindName(name)).IsChecked = true;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (((TextBlock)window.FindName("PageTitle")).Text != title || !((Button)window.FindName("StartButton")).IsVisible)
                    throw new Exception("Navigation lost its heading or persistent session controls.");
            }
            window.WindowState = WindowState.Maximized;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (window.WindowState != WindowState.Maximized) throw new Exception("Native maximization failed.");
            window.WindowState = WindowState.Normal;
            window.Width = 880; window.Height = 640;
            await WaitForCompositionAsync(window);
            var start = (Button)window.FindName("StartButton");
            var content = (FrameworkElement)window.Content;
            var bounds = start.TransformToAncestor(content).TransformBounds(new System.Windows.Rect(start.RenderSize));
            if (bounds.Bottom > content.ActualHeight || bounds.Right > content.ActualWidth || !start.IsVisible)
                throw new Exception("Session controls are clipped at the minimum window size.");
            Capture(hwnd, Path.Combine(directory, "windows11-compact-window.png"));
            window.Width = 1080; window.Height = 800;
            window.SetAppearance("System", persist: false);
            await File.WriteAllTextAsync(Path.Combine(directory, "native-window-results.json"), JsonSerializer.Serialize(new
            {
                passed = true, nativeCaptionButtons = true, nativeResizeFrame = true, layeredTransparency = false,
                maximizeRestorePassed = true, minimumSizeControlsVisible = true, navigationPassed = true, frames,
            snapFlyoutManuallyTested = false, physicalMonitorDpiSwitchTested = false,
                highContrastPhysicallyTested = SystemParameters.HighContrast,
                note = "DWM attributes verify configuration; PrintWindow may use a neutral backdrop and does not prove wallpaper tint or Snap flyout rendering."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Hide(); }
    }
    private static async Task WaitForCompositionAsync(Window window)
    {
        // Dispatcher-idle alone does not wait for WPF's render thread or DWM.
        // Wait for render frames so captures cannot contain the previous theme.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int frames = 0;
        void Rendered(object? sender, EventArgs args) { if (++frames >= 3) ready.TrySetResult(); }
        CompositionTarget.Rendering += Rendered;
        try
        {
            window.UpdateLayout(); window.InvalidateVisual();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            DwmFlush();
        }
        finally { CompositionTarget.Rendering -= Rendered; }
    }
    private static bool Capture(IntPtr hwnd, string path)
    {
        if (!GetWindowRect(hwnd, out var rect)) return false;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        IntPtr screen = GetWindowDC(hwnd), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero) return false;
            memory = CreateCompatibleDC(screen); bitmap = CreateCompatibleBitmap(screen, width, height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) return false;
            previous = SelectObject(memory, bitmap);
            if (!PrintWindow(hwnd, memory, 2)) return false;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var file = File.Create(path); encoder.Save(file); return true;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(hwnd, screen);
        }
    }
}
