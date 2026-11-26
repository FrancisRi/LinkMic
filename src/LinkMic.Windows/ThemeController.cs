using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace LinkMic.Windows;

public sealed class ThemeController
{
    private readonly FrameworkElement root;
    private readonly AppWindowTitleBar titleBar;
    private readonly AccessibilitySettings accessibility = new();
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkMic", "theme.txt");

    public string Preference { get; private set; } = "System";
    public bool HighContrast => accessibility.HighContrast;
    public string EffectiveMode => HighContrast ? "HighContrast" : root.ActualTheme.ToString();

    public ThemeController(FrameworkElement root, AppWindowTitleBar titleBar)
    {
        this.root = root;
        this.titleBar = titleBar;
        try { if (File.Exists(settingsPath)) Preference = Normalize(File.ReadAllText(settingsPath).Trim()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Apply();
    }

    public void SetPreference(string preference, bool persist = true)
    {
        Preference = Normalize(preference);
        Apply();
        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, Preference);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Normalize(string value) => value is "Light" or "Dark" ? value : "System";

    private void Apply()
    {
        root.RequestedTheme = Preference switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            titleBar.PreferredTheme = Preference switch
            {
                "Dark" => TitleBarTheme.Dark,
                "Light" => TitleBarTheme.Light,
                _ => TitleBarTheme.UseDefaultAppMode
            };
        }
    }
}
