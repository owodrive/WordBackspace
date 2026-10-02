using Microsoft.Win32;

namespace WordBackspace;

// Reads the Windows light/dark setting and applies it to the app.
static class Theme
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static bool _dark = Read();

    public static bool IsDark => _dark;

    public static void ApplyAtStartup()
    {
        Ui.ApplyTheme(_dark);
        Logger.Info($"theme = {(_dark ? "dark" : "light")}");
    }

    // Poll the registry; returns true if the setting changed since the last
    // call (palette already applied).
    public static bool CheckForChange()
    {
        bool now = Read();
        if (now == _dark) return false;
        _dark = now;
        Ui.ApplyTheme(now);
        Logger.Info($"theme changed to {(now ? "dark" : "light")}");
        return true;
    }

    private static bool Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            int light = key?.GetValue("AppsUseLightTheme") as int? ?? 1;
            return light == 0;
        }
        catch
        {
            return false;
        }
    }
}