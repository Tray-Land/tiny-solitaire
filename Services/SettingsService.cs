using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Storage;

namespace TinySolitaire.Services;

internal enum AppTheme { System, Light, Dark, Green }

internal static class SettingsService
{
    public static string? Error { get; private set; }
    public static bool HasLaunchedBefore
    {
        get => Get(nameof(HasLaunchedBefore), false);
        set => Set(nameof(HasLaunchedBefore), value);
    }
    public static int DrawCount
    {
        get => Get(nameof(DrawCount), 1) == 3 ? 3 : 1;
        set => Set(nameof(DrawCount), value == 3 ? 3 : 1);
    }
    public static int CardStyle
    {
        get => Math.Clamp(Get(nameof(CardStyle), 0), 0, 3);
        set => Set(nameof(CardStyle), Math.Clamp(value, 0, 3));
    }
    public static AppTheme Theme
    {
        get => (AppTheme)Math.Clamp(Get(nameof(Theme), (int)AppTheme.Green), 0, 3);
        set => Set(nameof(Theme), (int)value);
    }

    private static T Get<T>(string key, T fallback)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value) && value is T typed
                ? typed : fallback;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Debug.WriteLine($"Failed to read setting '{key}': {ex}");
            Error = "Couldn't read your preferences. Default options are in use.";
            return fallback;
        }
    }

    private static void Set(string key, object value)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
            Error = null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Debug.WriteLine($"Failed to save setting '{key}': {ex}");
            Error = "Couldn't save your preferences. Try changing the option again.";
        }
    }
}
