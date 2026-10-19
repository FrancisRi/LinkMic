using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LinkMic.Core;

namespace LinkMic.Windows;
internal static class RnNoiseCheck
{
    public static async Task RunAsync(string directory)
    {
        string? previous = Environment.GetEnvironmentVariable("LINKMIC_STATE_DIR");
        string stateDirectory = Path.Combine(directory, "rnnoise-state-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("LINKMIC_STATE_DIR", stateDirectory);
        try
        {
            if (!RnNoiseProcessor.IsAvailable(out string reason)) throw new Exception("RNNoise unavailable: " + reason);
            if (NoiseReductionSettings.Load()) throw new Exception("New installations must start with denoising off.");
            NoiseReductionSettings.Save(true); if (!NoiseReductionSettings.Load()) throw new Exception("Enabled preference not saved.");
            NoiseReductionSettings.Save(false); if (NoiseReductionSettings.Load()) throw new Exception("Disabled preference not saved.");
            var random = new Random(4810); double dryEnergy = 0, wetEnergy = 0; long measuredSamples = 0;
            var timings = new List<double>();
            using (var processor = new RnNoiseProcessor())
            {
                for (int frame = 0; frame < 250; frame++)
                {
                    short[] samples = Noise(random); short[] original = (short[])samples.Clone();
                    long started = Stopwatch.GetTimestamp(); processor.Process(samples, true);
                    if (frame >= 25)
                    {
                        timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        for (int i = 0; i < samples.Length; i++) { dryEnergy += (double)original[i] * original[i]; wetEnergy += (double)samples[i] * samples[i]; measuredSamples++; }
                    }
                }
                for (int repeat = 0; repeat < 50; repeat++)
                {
                    short[] samples = Noise(random), original = (short[])samples.Clone(); processor.Process(samples, false);
                    if (!samples.SequenceEqual(original)) throw new Exception("Disabled processing changed PCM.");
                    processor.Process(samples, true);
                }
                processor.Reset(); processor.Reset();
            }
            double ratio = Math.Sqrt(wetEnergy / dryEnergy);
            if (ratio >= 0.65) throw new Exception($"Synthetic noise was not suppressed: RMS ratio {ratio:F3}.");
            using (var a = new RnNoiseProcessor())
            using (var b = new RnNoiseProcessor())
            {
                for (int frame = 0; frame < 15; frame++)
                {
                    short[] input = Noise(random), copy = (short[])input.Clone(); a.Process(input, true); b.Process(copy, true);
                    if (!input.SequenceEqual(copy)) throw new Exception("Native session states are not independent.");
                }
                a.Reset(); short[] resumed = Noise(random), fresh = (short[])resumed.Clone();
                using var clean = new RnNoiseProcessor(); a.Process(resumed, true); clean.Process(fresh, true);
                if (!resumed.SequenceEqual(fresh)) throw new Exception("Denoiser reset retained old session audio.");
            }
            timings.Sort(); double p95 = timings[(int)(timings.Count * .95)];
            if (p95 >= 20) throw new Exception($"Native DSP exceeds the 20 ms frame budget: P95 {p95:F2} ms.");
            var session = await CheckUiSessionAsync(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "rnnoise-results.json"), JsonSerializer.Serialize(new
            {
                passed = true, nativeVersion = "RNNoise v0.2", officialModel = "0b50c45", sampleRate = 48000, nativeBlockSamples = 480,
                disabledPcmBitExact = true, resetAndIndependentSessions = true, repeatedOnOff = true, persistedPreference = true,
                syntheticNoiseInputRms = Math.Sqrt(dryEnergy / measuredSamples), syntheticNoiseOutputRms = Math.Sqrt(wetEnergy / measuredSamples),
                syntheticNoiseReductionDb = -20 * Math.Log10(ratio), processingP95MsPer20ms = p95, processingAverageMsPer20ms = timings.Average(),
                uiSession = session, physicalBluetoothTested = false, humanSpeechQualityTested = false, actualUserSettingsModified = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Delete(Path.Combine(directory, "rnnoise-error.txt"));
        }
        finally { Environment.SetEnvironmentVariable("LINKMIC_STATE_DIR", previous); }
    }
    private static short[] Noise(Random random) => Enumerable.Range(0, 960).Select(_ => (short)random.Next(-7000, 7001)).ToArray();
    private static async Task<object> CheckUiSessionAsync(string directory)
    {
        var sink = new CaptureOutput(); Receiver? actual = null;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new MainWindow(() =>
        {
            actual = new Receiver(_ => sink, IPAddress.Loopback);
            actual.Status += value => { if (value.StartsWith("等待")) ready.TrySetResult(); };
            return actual;
        });
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        try
        {
            var toggle = (CheckBox)window.FindName("NoiseReduction");
            if (!toggle.IsEnabled || toggle.IsChecked == true) throw new Exception("RNNoise UI is not available/off by default.");
            toggle.IsChecked = true;
            var reopened = new MainWindow();
            if (((CheckBox)reopened.FindName("NoiseReduction")).IsChecked != true) throw new Exception("UI preference did not survive reopening.");
            reopened.Close(); toggle.IsChecked = false;
            ((RadioButton)window.FindName("WifiMode")).IsChecked = true;
            ((Button)window.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, AudioProtocol.Port);
            var stream = client.GetStream();
            string code = ((TextBox)window.FindName("PairCode")).Text;
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"LMIC/1 {code} NoiseFixture\n"));
            uint session = uint.Parse((await Pairing.ReadLineAsync(stream, default)).Split(' ')[1]);
            short[] source = Noise(new Random(192)); byte[] pcm = AudioProtocol.ToPcm(source);
            for (uint frame = 0; frame < 120; frame++)
            {
                if (frame == 15 || frame == 75) toggle.IsChecked = true;
                if (frame == 45) { toggle.IsChecked = false; sink.Phase = 1; }
                if (frame == 75) ((CheckBox)window.FindName("Muted")).IsChecked = true;
                if (frame == 80) sink.Phase = 2;
                if (frame >= 95) toggle.IsChecked = frame % 2 == 0;
                if (!toggle.IsEnabled || actual!.NoiseReductionEnabled != (toggle.IsChecked == true)) throw new Exception("Live checkbox did not reach the audio worker.");
                await stream.WriteAsync(AudioProtocol.Encode(new(frame, frame * 20000, session, source), compressed: false));
                await Task.Delay(20);
            }
            ((Button)window.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try { await window.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) when (actual!.IsStopping && window.SessionCompletion.IsCompleted) { }
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (window.SessionActive || !sink.Disposed || sink.Writes < 80 || !sink.DryAfter.Any(value => value.SequenceEqual(pcm)) || sink.MutedAfter.Any(value => value.Any(sample => sample != 0)) || sink.MutedAfter.Count < 10)
                throw new Exception("Live toggle/bypass/mute/disconnect regression failed.");
            toggle.IsChecked = true; window.SetAppearance("Dark", persist: false);
            await App.RenderPreviewAsync(window, Path.Combine(directory, "rnnoise-dark.png"));
            window.SetAppearance("Light", persist: false);
            await App.RenderPreviewAsync(window, Path.Combine(directory, "rnnoise-light.png"));
            return new { passed = true, realWpfCheckbox = true, actualReceiver = true, liveToggle = true, rawAudioRestored = true, mutePreserved = true, disconnectedCleanly = true, writes = sink.Writes, hardwareAudio = false };
        }
        finally { actual?.RequestStop(); try { await window.SessionCompletion; } catch { } window.Close(); }
    }
    private sealed class CaptureOutput : IAudioOutput
    {
        public volatile int Phase;
        public int Writes; public bool Disposed;
        public readonly List<byte[]> DryAfter = [], MutedAfter = [];
        public void Write(byte[] pcm) { Writes++; if (Phase == 1) DryAfter.Add((byte[])pcm.Clone()); if (Phase == 2) MutedAfter.Add((byte[])pcm.Clone()); }
        public void Dispose() => Disposed = true;
    }
}
