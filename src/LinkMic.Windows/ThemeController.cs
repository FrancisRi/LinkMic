using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace LinkMic.Windows;

// Native Fluent resources own materials, contrast themes and system accent colors.
public sealed class ThemeController : IDisposable
{
    private readonly Window window;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkMic", "theme.txt");
    public string Preference { get; private set; } = "System";
    public string EffectiveMode { get; private set; } = "Light";
    public ThemeController(Window window)
    {
        this.window = window;
        try { if (File.Exists(settingsPath)) Preference = Normalize(File.ReadAllText(settingsPath).Trim()); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        SystemEvents.UserPreferenceChanged += OnSystemPreference;
        SystemParameters.StaticPropertyChanged += OnSystemParameters;
        Apply();
    }
    public void SetPreference(string preference, bool persist = true)
    {
        Preference = Normalize(preference); Apply();
        if (!persist) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, Preference); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static string Normalize(string value) => value is "Light" or "Dark" ? value : "System";
    private void OnSystemPreference(object sender, UserPreferenceChangedEventArgs e) => Refresh();
    private void OnSystemParameters(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void Refresh() { if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.InvokeAsync(Apply); }
    private static bool SystemIsDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch (System.Security.SecurityException) { return false; }
    }
    private void Apply()
    {
        bool dark = Preference == "Dark" || Preference == "System" && SystemIsDark();
        EffectiveMode = SystemParameters.HighContrast ? "HighContrast" : dark ? "Dark" : "Light";
#pragma warning disable WPF0001 // Isolate WPF's experimental code-access opt-in.
        Application.Current.ThemeMode = Preference == "Dark" ? ThemeMode.Dark : Preference == "Light" ? ThemeMode.Light : ThemeMode.System;
        window.ThemeMode = Application.Current.ThemeMode;
#pragma warning restore WPF0001
    }
    public void Dispose() { SystemEvents.UserPreferenceChanged -= OnSystemPreference; SystemParameters.StaticPropertyChanged -= OnSystemParameters; }
}
