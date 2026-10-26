namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using Microsoft.Win32;

/// <summary>
/// Finds the .exe of the browser that sent a notification, so a click on the
/// island can be handed back to it.
/// </summary>
public static class BrowserLocator
{
    public static string? FindExe(string? appUserModelId)
    {
        if (string.IsNullOrEmpty(appUserModelId)) return null;

        string? exe =
            Has(appUserModelId, "Edge")    ? "msedge.exe"  :
            Has(appUserModelId, "Chrome")  ? "chrome.exe"  :
            Has(appUserModelId, "Brave")   ? "brave.exe"   :
            Has(appUserModelId, "Vivaldi") ? "vivaldi.exe" :
            Has(appUserModelId, "Opera")   ? "opera.exe"   :
            Has(appUserModelId, "Coccoc")  ? "browser.exe" : null;
        if (exe == null) return null;

        // Installed browsers register themselves under "App Paths".
        foreach (var hive in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
        {
            var path = Registry.GetValue(
                $@"{hive}\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}", "", null) as string;
            path = path?.Trim('"');
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        }

        // Usual install location for Edge.
        if (exe == "msedge.exe")
        {
            var edge = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(edge)) return edge;
        }
        return null;
    }

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);
}
