using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LinkMic.Core;

namespace LinkMic.Windows;
internal static class DisconnectCheck
{
    public static async Task RunAsync(string directory)
    {
        var results = new List<object>();
        var unhandled = new List<Exception>();
        void CaptureError(object sender, DispatcherUnhandledExceptionEventArgs e) { unhandled.Add(e.Exception); e.Handled = true; }
        Application.Current.DispatcherUnhandledException += CaptureError;
        try
        {
            // Use the actual WPF buttons and async handlers, actual Receiver and
            // loopback sockets; replace only the hardware audio sink.
            foreach (string stage in new[] { "waiting", "pairing", "playing", "playing-cable", "peer-close", "window-close" }.Concat(Enumerable.Repeat("window-close", 9)))
            {
                var sink = new CountingOutput();
                var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var window = new MainWindow(() =>
                {
                    var receiver = new Receiver(selected =>
                    {
                        if (stage == "playing-cable" && (!selected.Cable || selected.Driver || selected.Id != 41))
                            throw new Exception("VB-CABLE selection was not passed to the playback output factory.");
                        return sink;
                    }, IPAddress.Loopback);
                    receiver.Status += status => { if (status.StartsWith("等待")) listening.TrySetResult(); };
                    return receiver;
                });
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                ((RadioButton)window.FindName("WifiMode")).IsChecked = true;
                if (stage == "playing-cable")
                {
                    var outputs = (ComboBox)window.FindName("Outputs");
                    outputs.ItemsSource = new[] { WaveOutput.DescribeDevice(41, "CABLE Input (VB-Audio Virtual C") };
                    outputs.SelectedIndex = 0;
                }
                ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var client = new TcpClient();
                if (stage is "pairing" or "playing" or "playing-cable" or "peer-close")
                {
                    await client.ConnectAsync(IPAddress.Loopback, AudioProtocol.Port);
                    if (stage != "pairing")
                    {
                        var stream = client.GetStream();
                        string code = ((TextBox)window.FindName("PairCode")).Text;
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"LMIC/1 {code} RegressionPhone\n"));
                        string reply = await Pairing.ReadLineAsync(stream, default);
                        uint session = uint.Parse(reply.Split(' ')[1]);
                        for (uint sequence = 0; sequence < 8; sequence++)
                            await stream.WriteAsync(AudioProtocol.Encode(new(sequence, sequence * 20000, session, new short[960])));
                        await Task.Delay(120);
                        if (sink.Writes == 0) throw new Exception("Receiver never entered playout.");
                        if (stage == "playing-cable" && (!((TextBlock)window.FindName("OutputHint")).Text.Contains("CABLE Output")
                            || ((Button)window.FindName("OutputsRefreshButton")).IsEnabled))
                            throw new Exception("VB-CABLE active UI did not show the recording endpoint or lock device refresh.");
                        if (stage == "playing-cable")
                        {
                            ((RadioButton)window.FindName("SettingsNav")).IsChecked = true;
                            window.SetAppearance("Dark", persist: false);
                            if (((TextBlock)window.FindName("PageTitle")).Text != "设置"
                                || ((Button)window.FindName("RegenerateCodeButton")).IsEnabled
                                || ((Button)window.FindName("StopButton")).Visibility != Visibility.Visible)
                                throw new Exception("Navigation during reception lost the stop control or unlocked pairing changes.");
                            ((RadioButton)window.FindName("ConnectNav")).IsChecked = true;
                        }
                    }
                }
                Task completed = window.SessionCompletion;
                if (stage == "peer-close") client.Dispose();
                else if (stage == "window-close") window.Close();
                else
                {
                    var stop = (Button)window.FindName("StopButton");
                    stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                try { await completed.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) when (completed.IsCompleted) { /* The UI handler owns the expected cancellation/network exception. */ }
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (window.SessionActive || sink.Disposals != 1 || unhandled.Count != 0)
                    throw new Exception($"UI disconnect failed at {stage}: active={window.SessionActive}, disposals={sink.Disposals}, dispatcherErrors={unhandled.Count}");
                if (stage != "window-close") window.Close();
                results.Add(new { stage, passed = true, outputDisposedExactlyOnce = true, playoutWrites = sink.Writes });
            }
            var unusedSink = new CountingOutput();
            using var stoppedBeforeStart = new Receiver(_ => unusedSink, IPAddress.Loopback);
            stoppedBeforeStart.RequestStop(); stoppedBeforeStart.RequestStop();
            try { await stoppedBeforeStart.RunAsync(true, "", "123456", new("test", -1)); throw new Exception("Cancelled session started."); }
            catch (OperationCanceledException) { }
            if (unusedSink.Disposals != 0) throw new Exception("An output was created after cancellation.");
            results.Add(new { stage = "stop-before-start", passed = true });
            if (!VirtualMicOutput.Exists())
            {
                var window = new MainWindow();
                ((RadioButton)window.FindName("WifiMode")).IsChecked = true;
                var outputs = (ComboBox)window.FindName("Outputs");
                // Uninstalled experimental drivers are hidden in the normal device list.
                outputs.ItemsSource = new[] { new OutputDevice("Missing experimental driver", 0, Driver: true) };
                outputs.SelectedIndex = 0;
                ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Task completed = window.SessionCompletion;
                try { await completed.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) when (completed.IsCompleted) { }
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (window.SessionActive || !((TextBlock)window.FindName("StatusText")).Text.Contains("无法打开 LinkMic") || unhandled.Count != 0)
                    throw new Exception("Missing-driver startup did not report a recoverable UI error.");
                window.Close();
                results.Add(new { stage = "missing-driver-startup", passed = true });
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "ui-disconnect-results.json"), JsonSerializer.Serialize(new
            {
                passed = true, cases = results, unhandledDispatcherExceptions = unhandled.Count,
                physicalBluetooth = false, actualWpfButtons = true, actualReceiver = true, hardwareAudioSink = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Application.Current.DispatcherUnhandledException -= CaptureError; }
    }
    private sealed class CountingOutput : IAudioOutput
    {
        public int Writes, Disposals;
        public void Write(byte[] pcm) => Interlocked.Increment(ref Writes);
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
