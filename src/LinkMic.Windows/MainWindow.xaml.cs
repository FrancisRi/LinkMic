using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LinkMic.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace LinkMic.Windows;
public partial class MainWindow : Window
{
    private Receiver? receiver; private Task? running;
    private readonly Func<Receiver> receiverFactory;
    private readonly ThemeController theme;
    private bool themeReady;
    private bool closeRequested;
    private long displayedSeconds = -1;
    public ThemeController Theme => theme;
    public MainWindow() : this(() => new Receiver()) { }
    internal MainWindow(Func<Receiver> receiverFactory)
    {
        this.receiverFactory = receiverFactory;
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        ConfigureWindow();
        theme = new ThemeController(WindowRoot, AppWindow.TitleBar);
        ThemeChoice.SelectedIndex = theme.Preference == "Light" ? 1 : theme.Preference == "Dark" ? 2 : 0;
        themeReady = true;
        WindowRoot.SelectedItem = ConnectNav;
        PairCode.Text = Pairing.NewCode();
        RefreshPairingState();
        RefreshOutputDevices();
        LocalAddresses.Text = string.Join(" / ", Dns.GetHostAddresses(Dns.GetHostName()).Where(ip => ip.AddressFamily == AddressFamily.InterNetwork));
        Gain.ValueChanged += (_, _) => { if (receiver is not null) receiver.Gain = Gain.Value; };
        Muted.Checked += (_, _) => { if (receiver is not null) receiver.Muted = true; };
        Muted.Unchecked += (_, _) => { if (receiver is not null) receiver.Muted = false; };
        NoiseReduction.IsEnabled = RnNoiseProcessor.IsAvailable(out string libraryError);
        NoiseReduction.IsChecked = NoiseReduction.IsEnabled && NoiseReductionSettings.Load();
        UpdateNoiseReductionHint();
        if (!NoiseReduction.IsEnabled) Diagnostics.Write("rnnoise-unavailable", new InvalidOperationException(libraryError));
        NoiseReduction.Checked += NoiseReductionChanged; NoiseReduction.Unchecked += NoiseReductionChanged;
        AppWindow.Closing += OnClosing;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    internal IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    internal double Scale => GetDpiForWindow(Handle) / 96d;
    internal T FindControl<T>(string name) where T : FrameworkElement =>
        WindowRoot.FindName(name) as T ?? throw new InvalidOperationException($"Control '{name}' was not found.");

    private void ConfigureWindow()
    {
        var appWindow = AppWindow;
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "LinkMic.ico"));
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        double scale = Scale;
        int width = Math.Min((int)Math.Round(1080 * scale), work.Width);
        int height = Math.Min((int)Math.Round(800 * scale), work.Height);
        appWindow.MoveAndResize(new RectInt32(area.OuterBounds.X + work.X + (work.Width - width) / 2,
            area.OuterBounds.Y + work.Y + (work.Height - height) / 2, width, height));
        UpdateMinimumSize();
        appWindow.Changed += (_, change) =>
        {
            if (change.DidPositionChange || change.DidSizeChange) UpdateMinimumSize();
        };
    }

    private void UpdateMinimumSize()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        double scale = Scale;
        int width = Math.Min((int)Math.Round(880 * scale), work.Width);
        int height = Math.Min((int)Math.Round(640 * scale), work.Height);
        if (presenter.PreferredMinimumWidth != width) presenter.PreferredMinimumWidth = width;
        if (presenter.PreferredMinimumHeight != height) presenter.PreferredMinimumHeight = height;
    }
    private void UpdateNoiseReductionHint() => NoiseReductionHint.Text = !NoiseReduction.IsEnabled
        ? "降噪组件不可用，请使用完整 Windows 更新包。"
        : NoiseReduction.IsChecked == true ? "开启 · 抑制背景噪声，可随时关闭。" : "关闭 · 保留原始声音，可在连接时切换。";
    private void NoiseReductionChanged(object sender, RoutedEventArgs e)
    {
        bool enabled = NoiseReduction.IsChecked == true;
        if (receiver is not null) receiver.NoiseReductionEnabled = enabled;
        UpdateNoiseReductionHint();
        try { NoiseReductionSettings.Save(enabled); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { Diagnostics.Write("audio-settings-save", error); Log("降噪开关已生效，但偏好未能保存。"); }
    }
    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is not NavigationViewItem item) return;
        ConnectPage.Visibility = item == ConnectNav ? Visibility.Visible : Visibility.Collapsed;
        GuidePage.Visibility = item == GuideNav ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = item == LogsNav ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = item == SettingsNav ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = (string)item.Content;
        PageSubtitle.Text = item == GuideNav ? "从连接手机，到在会议软件中使用。"
            : item == LogsNav ? "查看最近的连接状态与设备信息。"
            : item == SettingsNav ? "个性化 LinkMic 的外观。"
            : "通过蓝牙或 Wi-Fi，让手机成为电脑的麦克风。";
        PageScroll.ChangeView(null, 0, null, disableAnimation: true);
    }
    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (themeReady && ThemeChoice.SelectedItem is ComboBoxItem item && item.Tag is string preference) SetAppearance(preference);
    }
    public void SetAppearance(string preference, bool persist = true)
    {
        theme.SetPreference(preference, persist); themeReady = false;
        ThemeChoice.SelectedIndex = theme.Preference == "Light" ? 1 : theme.Preference == "Dark" ? 2 : 0;
        themeReady = true;
    }
    private void RefreshPairingState()
    {
        try { PairedDevicesInfo.Text = $"已记住 {DeviceTrustStore.Count} 台手机；下次连接自动校验身份。"; }
        catch (Exception error) { PairedDevicesInfo.Text = "配对记录不可读取，可忘记设备后重新配对。"; Diagnostics.Write("device-trust-read", error); }
    }
    private async void ForgetDevices(object sender, RoutedEventArgs e)
    {
        if (receiver is not null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            RequestedTheme = WindowRoot.RequestedTheme,
            Title = "忘记已配对设备",
            Content = "忘记后，这些手机下次连接需要重新输入配对码。",
            PrimaryButtonText = "忘记",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || receiver is not null) return;
        try { DeviceTrustStore.ForgetAll(); PairCode.Text = Pairing.NewCode(); RefreshPairingState(); SetStatus("已忘记设备，下次连接请重新配对。"); }
        catch (Exception error) { Diagnostics.Write("device-trust-reset", error); SetStatus("无法忘记设备：" + error.Message); }
    }
    private void OutputSelected(object sender, SelectionChangedEventArgs e)
    {
        if (OutputHint is not null && Outputs.SelectedItem is OutputDevice output) OutputHint.Text = output.Hint;
    }
    private void RefreshOutputs(object sender, RoutedEventArgs e) => RefreshOutputDevices();
    private void RefreshOutputDevices()
    {
        if (receiver is not null) return;
        try
        {
            var previous = Outputs.SelectedItem as OutputDevice;
            var devices = WaveOutput.Devices(); Outputs.ItemsSource = devices;
            Outputs.SelectedItem = devices.FirstOrDefault(output => previous is not null && output.Id == previous.Id && output.DeviceName == previous.DeviceName && output.Driver == previous.Driver) ?? devices[0];
            if (!devices.Any(output => output.Cable)) OutputHint.Text = "会议麦克风需要 VB-CABLE：点击“驱动文件”查看安装说明，安装重启后刷新设备。";
        }
        catch (Exception error) { Diagnostics.Write("audio-device-refresh", error); Log(error.Message); }
    }
    private void OpenDriverFiles(object sender, RoutedEventArgs e)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "drivers", "vb-cable");
        if (!System.IO.Directory.Exists(path)) { WindowRoot.SelectedItem = GuideNav; SetStatus("请使用完整 Windows ZIP，VB-CABLE 官方地址为 https://vb-audio.com/Cable/"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { Diagnostics.Write("driver-folder", error); SetStatus("无法打开驱动文件夹：" + error.Message); }
    }
    private void ModeChanged(object sender, RoutedEventArgs e) { if (WifiPanel is null) return; WifiPanel.Visibility = WifiMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; BluetoothPanel.Visibility = WifiMode.IsChecked == true ? Visibility.Collapsed : Visibility.Visible; }
    private void RegenerateCode(object sender, RoutedEventArgs e) { if (receiver is null) PairCode.Text = Pairing.NewCode(); }
    private async void RefreshBluetooth(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.IsEnabled = false;
        try { Devices.ItemsSource = await Task.Run(Receiver.PairedDevices); Devices.SelectedIndex = 0; Log("已刷新配对设备。"); }
        catch (Exception error) { SetStatus("蓝牙不可用：" + error.Message); }
        finally { button.IsEnabled = true; }
    }
    private void DeviceSelected(object sender, SelectionChangedEventArgs e) { if (Devices.SelectedItem is PairedPhone phone) BluetoothAddress.Text = phone.Address; }
    private async void StartClicked(object sender, RoutedEventArgs e)
    {
        if (receiver is not null || closeRequested) return;
        running = ReceiveAsync();
        await running;
    }

    private async Task ReceiveAsync()
    {
        if (Outputs.SelectedItem is not OutputDevice output) { SetStatus("请选择音频输出设备。"); return; }
        bool wifi = WifiMode.IsChecked == true;
        if (!wifi && BluetoothAddress.Text.Replace(":", "").Replace("-", "").Length != 12) { SetStatus("请先选择已配对手机或填写蓝牙地址。"); return; }
        var current = receiverFactory(); receiver = current; receiver.Gain = Gain.Value; receiver.Muted = Muted.IsChecked == true;
        receiver.NoiseReductionEnabled = NoiseReduction.IsEnabled && NoiseReduction.IsChecked == true;
        receiver.AudioProcessingError += message => DispatcherQueue.TryEnqueue(() =>
        {
            if (receiver != current) return;
            NoiseReduction.IsChecked = false; NoiseReductionHint.Text = message; Log(message);
        });
        displayedSeconds = -1; AudioStats.Text = "48 kHz · 单声道 · 等待连接";
        receiver.Status += message => DispatcherQueue.TryEnqueue(() => { if (receiver != current) return; SetStatus(message); if (message.StartsWith("已连接")) RefreshPairingState(); });
        receiver.Stats += stats => DispatcherQueue.TryEnqueue(() =>
        {
            if (receiver != current) return;
            Meter.Value = stats.Level;
            long seconds = (long)stats.ConnectedFor.TotalSeconds;
            if (seconds == displayedSeconds) return;
            displayedSeconds = seconds;
            AudioStats.Text = $"已连接 {seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00} · 48 kHz";
        });
        SetBusy(true); OutputHint.Text = output.Hint;
        string address = BluetoothAddress.Text, code = PairCode.Text;
        try { await Task.Run(() => current.RunAsync(wifi, address, code, output)); SetStatus("已断开连接。"); }
        catch (Exception) when (current.IsStopping) { SetStatus("已断开连接。"); }
        catch (OperationCanceledException error) { Diagnostics.Write("session-timeout", error); SetStatus("连接超时。"); }
        catch (Exception error) { Diagnostics.Write("session-error", error); SetStatus("连接结束：" + error.Message); }
        finally { current.Dispose(); if (receiver == current) receiver = null; SetBusy(false); Meter.Value = 0; }
    }
    private void StopClicked(object sender, RoutedEventArgs e) => receiver?.RequestStop();
    internal Task SessionCompletion => running ?? Task.CompletedTask;
    internal bool SessionActive => receiver is not null;
    private void SetBusy(bool value) { StartButton.IsEnabled = !value; StopButton.IsEnabled = value; Outputs.IsEnabled = !value; OutputsRefreshButton.IsEnabled = !value; BluetoothMode.IsEnabled = WifiMode.IsEnabled = !value; PairCode.IsEnabled = !value; RegenerateCodeButton.IsEnabled = !value; ForgetDevicesButton.IsEnabled = !value; if (!value) SetBadge(false, "●  未连接"); }
    private void SetBadge(bool connected, string text)
    {
        StateBadge.Text = text;
        StateContainer.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ConnectedBadge.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetStatus(string message) { StatusText.Text = message; bool connected = message.StartsWith("已连接"); SetBadge(connected, connected ? "●  已连接" : receiver is null ? "●  未连接" : "●  会话中"); Log(message); }
    private void Log(string message)
    {
        string log = LogText.Text;
        if (log.Length > 16000) log = log[^8000..];
        LogText.Text = $"{log}{DateTime.Now:HH:mm:ss}  {message}\r\n";
        LogText.Select(LogText.Text.Length, 0);
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (running is null || running.IsCompleted) return;
        e.Cancel = true;
        if (closeRequested) return;
        closeRequested = true;
        receiver?.RequestStop();
        // Let the current Closing event return before closing the window again.
        try { await running; }
        finally { DispatcherQueue.TryEnqueue(Close); }
    }
}
