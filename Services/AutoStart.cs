namespace DynamicIslandNotifier.Services;

using System;
using Microsoft.Win32;

/// <summary>
/// "Start with Windows": adds the app to the current user's startup list
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). No admin rights
/// needed; it also shows up in Task Manager → Startup apps.
/// </summary>
public static class AutoStart
{
    private const string RunKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIslandNotifier";

    private static string? ExePath => Environment.ProcessPath;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    public static void Enable()
    {
        if (string.IsNullOrEmpty(ExePath)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, $"\"{ExePath}\"");
        }
        catch (Exception ex) { DebugLog.Note($"AutoStart enable failed: {ex.Message}"); }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { DebugLog.Note($"AutoStart disable failed: {ex.Message}"); }
    }

    /// <summary>
    /// Call at startup. Turns auto-start on the first time the app runs, and
    /// keeps the saved path current if the .exe was moved or updated.
    /// </summary>
    public static void EnsureOnStartup(AppSettings settings)
    {
        if (!settings.AutoStartConfigured)
        {
            Enable();
            settings.AutoStartConfigured = true;
            settings.Save();
        }
        else if (IsEnabled)
        {
            Enable();   // refresh the path
        }
    }
}
