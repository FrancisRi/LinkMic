using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using LinkMic.Core;

namespace LinkMic.Windows;
public partial class MainWindow : Window
{
    private Receiver? receiver; private Task? running;
    private readonly Func<Receiver> receiverFactory;
    private readonly ThemeController theme;
    private bool themeReady;
    private long displayedSeconds = -1;
    public ThemeController Theme => theme;
    public MainWindow() : this(() => new Receiver()) { }
    internal MainWindow(Func<Receiver> receiverFactory)
    {
        this.receiverFactory = receiverFactory;
        InitializeComponent(); theme = new ThemeController(this);
        ThemeChoice.SelectedIndex = theme.Preference == "Light" ? 1 : theme.Preference == "Dark" ? 2 : 0;
        themeReady = true; Closed += (_, _) => theme.Dispose();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        MinHeight = Math.Min(MinHeight, Height); MinWidth = Math.Min(MinWidth, Width);
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
        Closing += OnClosing;
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
    private void Navigate(int page)
    {
        if (ConnectPage is null || PageTitle is null || SettingsPage is null) return;
        ConnectPage.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        GuidePage.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page switch { 1 => "使用指南", 2 => "运行日志", 3 => "设置", _ => "连接与音频" };
        PageSubtitle.Text = page switch { 1 => "从连接手机，到在会议软件中使用。", 2 => "查看最近的连接状态与设备信息。", 3 => "个性化 LinkMic 的外观。", _ => "通过蓝牙或 Wi-Fi，让手机成为电脑的麦克风。" };
        var navigation = page switch { 1 => GuideNav, 2 => LogsNav, 3 => SettingsNav, _ => ConnectNav };
        if (navigation.IsChecked != true) navigation.IsChecked = true;
        PageScroll.ScrollToTop();
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
    private void ShowConnect(object sender, RoutedEventArgs e) => Navigate(0);
    private void ShowGuide(object sender, RoutedEventArgs e) => Navigate(1);
    private void ShowLogs(object sender, RoutedEventArgs e) => Navigate(2);
    private void ShowSettings(object sender, RoutedEventArgs e) => Navigate(3);
    private void RefreshPairingState()
    {
        try { PairedDevicesInfo.Text = $"已记住 {DeviceTrustStore.Count} 台手机；下次连接自动校验身份。"; }
        catch (Exception error) { PairedDevicesInfo.Text = "配对记录不可读取，可忘记设备后重新配对。"; Diagnostics.Write("device-trust-read", error); }
    }
    private void ForgetDevices(object sender, RoutedEventArgs e)
    {
        if (receiver is not null) return;
        if (MessageBox.Show(this, "忘记后，这些手机下次连接需要重新输入配对码。", "忘记已配对设备", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
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
        if (!System.IO.Directory.Exists(path)) { Navigate(1); SetStatus("请使用完整 Windows ZIP；VB-CABLE 官方地址：https://vb-audio.com/Cable/"); return; }
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
        if (receiver is not null) return;
        if (Outputs.SelectedItem is not OutputDevice output) { SetStatus("请选择音频输出设备。"); return; }
        bool wifi = WifiMode.IsChecked == true;
        if (!wifi && BluetoothAddress.Text.Replace(":", "").Replace("-", "").Length != 12) { SetStatus("请先选择已配对手机或填写蓝牙地址。"); return; }
        var current = receiverFactory(); receiver = current; receiver.Gain = Gain.Value; receiver.Muted = Muted.IsChecked == true;
        receiver.NoiseReductionEnabled = NoiseReduction.IsEnabled && NoiseReduction.IsChecked == true;
        receiver.AudioProcessingError += message => Dispatcher.InvokeAsync(() =>
        {
            if (receiver != current) return;
            NoiseReduction.IsChecked = false; NoiseReductionHint.Text = message; Log(message);
        });
        displayedSeconds = -1; AudioStats.Text = "48 kHz · 单声道 · 等待连接";
        receiver.Status += message => Dispatcher.InvokeAsync(() => { if (receiver != current) return; SetStatus(message); if (message.StartsWith("已连接")) RefreshPairingState(); });
        receiver.Stats += stats => Dispatcher.InvokeAsync(() =>
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
        try { running = Task.Run(() => current.RunAsync(wifi, address, code, output)); await running; SetStatus("手机已断开。"); }
        catch (OperationCanceledException) { SetStatus("已停止接收。"); }
        catch (Exception) when (current.IsStopping) { SetStatus("已停止接收。"); }
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
        StateContainer.SetResourceReference(Border.BackgroundProperty, connected ? "SystemFillColorSuccessBackgroundBrush" : "SubtleFillColorSecondaryBrush");
        StateBadge.SetResourceReference(TextBlock.ForegroundProperty, connected ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
    }
    private void SetStatus(string message) { StatusText.Text = message; bool connected = message.StartsWith("已连接"); SetBadge(connected, connected ? "●  已连接" : receiver is null ? "●  未连接" : "●  会话中"); Log(message); }
    private void Log(string message) { if (LogText is null) return; if (LogText.Text.Length > 16000) LogText.Text = LogText.Text[^8000..]; LogText.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\n"); LogText.ScrollToEnd(); }
    private bool closing;
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing || running is null || running.IsCompleted) return;
        e.Cancel = true; receiver?.RequestStop(); try { await running; } catch (Exception) { }
        // Cancellation can finish synchronously; leave the original Closing event
        // before calling Close again, otherwise WPF rejects a reentrant close.
        closing = true; _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
