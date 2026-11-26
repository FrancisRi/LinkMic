using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LinkMic.Core;

namespace LinkMic.Windows;
internal static class DisconnectCheck
{
    public static async Task RunAsync(string directory)
    {
        var results = new List<object>();
        var unhandled = new List<Exception>();
        var app = Application.Current;
        void CaptureError(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) { unhandled.Add(e.Exception); e.Handled = true; }
        app.UnhandledException += CaptureError;
        try
        {
            // Use the actual WinUI buttons and async handlers, actual Receiver and
            // loopback sockets; replace only the hardware audio sink.
            foreach (string stage in new[] { "waiting", "pairing", "playing", "playing-cable", "peer-close", "phone-stop", "desktop-stop", "unexpected-close", "truncated-frame", "window-close" }.Concat(Enumerable.Repeat("window-close", 9)))
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
                window.Activate();
                await App.WaitForLayoutAsync(window);
                var status = window.FindControl<TextBlock>("StatusText");
                window.FindControl<RadioButton>("WifiMode").IsChecked = true;
                if (stage == "playing-cable")
                {
                    var outputs = window.FindControl<ComboBox>("Outputs");
                    outputs.ItemsSource = new[] { WaveOutput.DescribeDevice(41, "CABLE Input (VB-Audio Virtual C") };
                    outputs.SelectedIndex = 0;
                }
                NativeWindowCheck.Invoke(window.FindControl<Button>("StartButton"));
                await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                NetworkStream? peer = null;
                if (stage != "waiting" && stage != "window-close")
                {
                    await client.ConnectAsync(IPAddress.Loopback, AudioProtocol.Port);
                    if (stage != "pairing")
                    {
                        var stream = peer = client.GetStream();
                        string code = window.FindControl<TextBox>("PairCode").Text;
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"LMIC/1 {code} RegressionPhone\n"));
                        string reply = await Pairing.ReadLineAsync(stream, timeout.Token);
                        uint session = uint.Parse(reply.Split(' ')[1]);
                        var signal = new byte[1];
                        await stream.ReadExactlyAsync(signal, timeout.Token);
                        if (signal[0] != (byte)SessionControl.Ready) throw new Exception("Desktop did not advertise disconnect support.");
                        if (stage is "phone-stop" or "desktop-stop" or "unexpected-close" or "truncated-frame")
                            await stream.WriteAsync(new[] { (byte)SessionControl.Ready }, timeout.Token);
                        for (uint sequence = 0; sequence < 8; sequence++)
                            await stream.WriteAsync(AudioProtocol.Encode(new(sequence, sequence * 20000, session, new short[960])));
                        await Task.Delay(120);
                        if (sink.Writes == 0) throw new Exception("Receiver never entered playout.");
                        if (stage == "playing-cable" && (!window.FindControl<TextBlock>("OutputHint").Text.Contains("CABLE Output")
                            || window.FindControl<Button>("OutputsRefreshButton").IsEnabled))
                            throw new Exception("VB-CABLE active UI did not show the recording endpoint or lock device refresh.");
                        if (stage == "playing-cable")
                        {
                            ((NavigationView)window.Content).SelectedItem = window.FindControl<NavigationViewItem>("SettingsNav");
                            window.SetAppearance("Dark", persist: false);
                            if (window.FindControl<TextBlock>("PageTitle").Text != "设置"
                                || window.FindControl<Button>("RegenerateCodeButton").IsEnabled
                                || window.FindControl<Button>("StopButton").Visibility != Visibility.Visible)
                                throw new Exception("Navigation during reception lost the stop control or unlocked pairing changes.");
                            ((NavigationView)window.Content).SelectedItem = window.FindControl<NavigationViewItem>("ConnectNav");
                        }
                    }
                }
                Task completed = window.SessionCompletion;
                Task? closed = null;
                if (stage is "peer-close" or "unexpected-close") client.Dispose();
                else if (stage == "phone-stop") await peer!.WriteAsync(new[] { (byte)SessionControl.Disconnect }, timeout.Token);
                else if (stage == "truncated-frame")
                {
                    await peer!.WriteAsync("LM"u8.ToArray(), timeout.Token);
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                else if (stage == "window-close") closed = NativeWindowCheck.RequestCloseAsync(window);
                else
                {
                    var stop = window.FindControl<Button>("StopButton");
                    NativeWindowCheck.Invoke(stop);
                    NativeWindowCheck.Invoke(stop);
                    if (stage == "desktop-stop")
                    {
                        var signal = new byte[1];
                        await peer!.ReadExactlyAsync(signal, timeout.Token);
                        if (signal[0] != (byte)SessionControl.Disconnect) throw new Exception("Desktop closed without sending the disconnect signal.");
                        client.Dispose();
                    }
                }
                try { await completed.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) when (completed.IsCompleted) { /* The UI handler owns the expected cancellation/network exception. */ }
                if (closed is not null) await closed.WaitAsync(TimeSpan.FromSeconds(5));
                string finalStatus = status.Text;
                if ((stage is "peer-close" or "phone-stop" or "desktop-stop") && finalStatus != "已断开连接。")
                    throw new Exception("Graceful close was shown as: " + finalStatus);
                if ((stage is "unexpected-close" or "truncated-frame") && !finalStatus.StartsWith("连接结束："))
                    throw new Exception("An unexpected connection loss was shown as: " + finalStatus);
                if (window.SessionActive || sink.Disposals != 1 || unhandled.Count != 0)
                    throw new Exception($"UI disconnect failed at {stage}: active={window.SessionActive}, disposals={sink.Disposals}, dispatcherErrors={unhandled.Count}");
                if (stage != "window-close") window.Close();
                results.Add(new { stage, passed = true, outputDisposedExactlyOnce = true, playoutWrites = sink.Writes, finalStatus });
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
                window.Activate();
                await App.WaitForLayoutAsync(window);
                window.FindControl<RadioButton>("WifiMode").IsChecked = true;
                var outputs = window.FindControl<ComboBox>("Outputs");
                // Uninstalled experimental drivers are hidden in the normal device list.
                outputs.ItemsSource = new[] { new OutputDevice("Missing experimental driver", 0, Driver: true) };
                outputs.SelectedIndex = 0;
                NativeWindowCheck.Invoke(window.FindControl<Button>("StartButton"));
                await App.WaitForLayoutAsync(window);
                Task completed = window.SessionCompletion;
                try { await completed.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) when (completed.IsCompleted) { }
                await App.WaitForLayoutAsync(window);
                if (window.SessionActive || !window.FindControl<TextBlock>("StatusText").Text.Contains("无法打开 LinkMic") || unhandled.Count != 0)
                    throw new Exception("Missing-driver startup did not report a recoverable UI error.");
                window.Close();
                results.Add(new { stage = "missing-driver-startup", passed = true });
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "ui-disconnect-results.json"), JsonSerializer.Serialize(new
            {
                passed = true, cases = results, unhandledDispatcherExceptions = unhandled.Count,
                physicalBluetooth = false, actualWinUiButtons = true, actualReceiver = true, hardwareAudioSink = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { app.UnhandledException -= CaptureError; }
    }
    private sealed class CountingOutput : IAudioOutput
    {
        public int Writes, Disposals;
        public void Write(byte[] pcm) => Interlocked.Increment(ref Writes);
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
