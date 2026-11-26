using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LinkMic.Windows;
internal static class ThemeCheck
{
    public static async Task RunAsync(MainWindow window, string directory)
    {
        var results = new List<object>();
        foreach (string mode in new[] { "Light", "Dark", "Light" })
        {
            window.SetAppearance(mode, persist: false);
            await App.RenderPreviewAsync(window, Path.Combine(directory, $"winui3-{mode.ToLowerInvariant()}.png"));
            var code = window.FindControl<TextBox>("PairCode");
            double contrast = Contrast(((SolidColorBrush)code.Foreground).Color, ((SolidColorBrush)code.Background).Color);
            if (contrast < 4.5 || !window.Theme.HighContrast && window.Theme.EffectiveMode != mode)
                throw new Exception($"Theme {mode} not applied, or unreadable pairing field: contrast {contrast:F2}");
            results.Add(new { requestedMode = mode, effectiveMode = window.Theme.EffectiveMode, pairingCodeContrast = contrast });
        }
        await App.RenderPreviewAsync(window, Path.Combine(directory, "winui3-compact.png"), 950, 700);
        await NativeWindowCheck.RunAsync(window, directory);
        await IntegrationCheck.RunAsync(directory, frame =>
        {
            if (frame == 20) window.SetAppearance("Dark", persist: false);
            if (frame == 40) window.SetAppearance("Light", persist: false);
            if (frame == 60) window.SetAppearance("System", persist: false);
        });
        await File.WriteAllTextAsync(Path.Combine(directory, "theme-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, runtimeTransitions = results, systemModeResolved = window.Theme.EffectiveMode,
            audioSessionSurvivedThemeChanges = true, compactLayoutRendered = true,
            physicalDpiSwitchTested = false, windowsSettingsChangeTested = false, highContrastTested = window.Theme.HighContrast
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static double Contrast(Color a, Color b)
    {
        // Fluent text brushes use alpha. Measure the displayed text against its
        // opaque pairing-field background, rather than ignoring that alpha.
        double alpha = a.A / 255d;
        a = Color.FromArgb(255, (byte)Math.Round(a.R * alpha + b.R * (1 - alpha)),
            (byte)Math.Round(a.G * alpha + b.G * (1 - alpha)), (byte)Math.Round(a.B * alpha + b.B * (1 - alpha)));
        static double Luminance(Color c)
        {
            static double Channel(byte value) { double n = value / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
