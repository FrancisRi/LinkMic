using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace LinkMic.Windows;

internal static class NativeWindowCheck
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint first, uint count, [Out] byte[] buffer, ref BitmapInfo info, uint usage);

    internal static void Invoke(Button button)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    internal static Task RequestCloseAsync(MainWindow window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        const uint wmClose = 0x0010;
        if (!PostMessage(window.Handle, wmClose, IntPtr.Zero, IntPtr.Zero))
            throw new Exception("Could not request a native window close.");
        return closed.Task;
    }

    public static async Task RunAsync(MainWindow window, string directory)
    {
        var frames = new List<object>();
        var hwnd = window.Handle;
        var appWindow = window.AppWindow;
        var presenter = (OverlappedPresenter)appWindow.Presenter;
        var originalSize = appWindow.Size;
        long style = GetWindowLongPtr(hwnd, -16).ToInt64();
        const long nativeFrame = 0x00C00000 | 0x00040000 | 0x00010000 | 0x00020000 | 0x00080000;
        bool layered = (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x00080000) != 0;
        if ((style & nativeFrame) != nativeFrame || layered || !presenter.HasTitleBar || window.ExtendsContentIntoTitleBar)
            throw new Exception("Native caption, resizing, minimize/maximize buttons or composition were lost.");
        if (window.SystemBackdrop is not MicaBackdrop)
            throw new Exception("The main window has no WinUI Mica backdrop.");

        foreach (string mode in new[] { "Light", "Dark" })
        {
            window.SetAppearance(mode, persist: false);
            await WaitForCompositionAsync(window);
            int darkResult = DwmGetWindowAttribute(hwnd, 20, out int dark, sizeof(int));
            if (AppWindowTitleBar.IsCustomizationSupported() && !window.Theme.HighContrast
                && (darkResult != 0 || dark != (mode == "Dark" ? 1 : 0)))
                throw new Exception($"Native caption theme did not follow {mode}.");
            bool captured = await CaptureAsync(hwnd, Path.Combine(directory, $"windows11-{mode.ToLowerInvariant()}-window.png"));
            frames.Add(new { mode, nativeDarkCaption = dark, darkCaptionHResult = darkResult, nativeFrameCaptured = captured });
        }

        var navigation = (NavigationView)window.Content;
        var start = window.FindControl<Button>("StartButton");
        foreach (var (name, title) in new[] { ("SettingsNav", "设置"), ("GuideNav", "使用指南"), ("LogsNav", "运行日志"), ("ConnectNav", "连接与音频") })
        {
            navigation.SelectedItem = window.FindControl<NavigationViewItem>(name);
            await App.WaitForLayoutAsync(window);
            if (window.FindControl<TextBlock>("PageTitle").Text != title || start.Visibility != Visibility.Visible)
                throw new Exception("Navigation lost its heading or persistent session controls.");
        }

        presenter.Maximize();
        await App.WaitForLayoutAsync(window);
        if (presenter.State != OverlappedPresenterState.Maximized) throw new Exception("Native maximization failed.");
        presenter.Restore();
        if (presenter.State != OverlappedPresenterState.Restored) throw new Exception("Native restore failed.");
        presenter.Minimize();
        await App.WaitForLayoutAsync(window);
        if (presenter.State != OverlappedPresenterState.Minimized) throw new Exception("Native minimization failed.");
        presenter.Restore();
        await App.WaitForLayoutAsync(window);
        if (presenter.State != OverlappedPresenterState.Restored) throw new Exception("Native restore failed after minimizing.");

        if (presenter.PreferredMinimumWidth is not int minWidth || presenter.PreferredMinimumHeight is not int minHeight)
            throw new Exception("Native window minimum size is missing.");
        appWindow.Resize(new SizeInt32(minWidth, minHeight));
        await WaitForCompositionAsync(window);
        var bounds = start.TransformToVisual(navigation).TransformBounds(new Rect(0, 0, start.ActualWidth, start.ActualHeight));
        if (bounds.Bottom > navigation.ActualHeight || bounds.Right > navigation.ActualWidth || start.ActualHeight == 0 || start.Visibility != Visibility.Visible)
            throw new Exception("Session controls are clipped at the minimum window size.");
        await CaptureAsync(hwnd, Path.Combine(directory, "windows11-compact-window.png"));
        appWindow.Resize(originalSize);
        window.SetAppearance("System", persist: false);
        await File.WriteAllTextAsync(Path.Combine(directory, "native-window-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, nativeCaptionButtons = true, nativeResizeFrame = true, layeredTransparency = layered,
            micaConfigured = true, micaSupported = MicaController.IsSupported(),
            maximizeRestorePassed = true, minimizeRestorePassed = true, minimumSizeControlsVisible = true, navigationPassed = true, frames,
            snapFlyoutManuallyTested = false, physicalMonitorDpiSwitchTested = false,
            highContrastPhysicallyTested = window.Theme.HighContrast,
            note = "WinUI sets the system backdrop through composition. PrintWindow may omit the wallpaper tint."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task WaitForCompositionAsync(Window window)
    {
        await App.WaitForLayoutAsync(window);
        // Allow the native window theme and backdrop animations to settle before capturing.
        await Task.Delay(350);
        DwmFlush();
    }

    private static async Task<bool> CaptureAsync(IntPtr hwnd, string path)
    {
        if (!GetWindowRect(hwnd, out var rect)) return false;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        IntPtr screen = GetWindowDC(hwnd), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero) return false;
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, width, height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) return false;
            previous = SelectObject(memory, bitmap);
            if (!PrintWindow(hwnd, memory, 2)) return false;
            SelectObject(memory, previous);
            previous = IntPtr.Zero;
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };
            byte[] pixels = new byte[checked(width * height * 4)];
            if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0) != height) return false;
            await App.SavePngAsync(path, pixels, width, height);
            return true;
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
