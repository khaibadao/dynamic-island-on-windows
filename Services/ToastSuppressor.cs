namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using Microsoft.Win32;

/// <summary>
/// While the app runs, Windows' own pop-up banner is hidden for apps whose
/// notifications show on the island (so you don't get both). Every change is
/// written down and undone when the app quits, when Windows signs out, or
/// from the tray menu; so nothing stays changed after you stop using it.
/// Notifications still land in Windows' notification center as normal.
/// </summary>
public sealed class ToastSuppressor
{
    private const string GlobalKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string AppKey    = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings\";

    /// <summary>Hide Windows' banner for apps that show on the island (tray menu).</summary>
    public static bool Enabled
    {
        get => AppSettings.Current.HideWindowsBanners;
        set { AppSettings.Current.HideWindowsBanners = value; AppSettings.Current.Save(); }
    }

    /// <summary>
    /// Versions before 1.8 could leave ALL Windows notifications switched off
    /// after a crash. This switches them back on, but only once ever, so a
    /// choice you make yourself in Windows settings is left alone afterwards.
    /// </summary>
    public void RepairOldVersionsOnce()
    {
        var s = AppSettings.Current;
        if (s.GlobalToastsRepaired) return;
        try { Registry.SetValue(GlobalKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 1, RegistryValueKind.DWord); } catch { }
        s.GlobalToastsRepaired = true;
        s.Save();
    }

    public void SuppressApp(string appUserModelId)
    {
        if (!Enabled || string.IsNullOrEmpty(appUserModelId)) return;
        var changed = AppSettings.Current.SuppressedBanners;
        if (changed.ContainsKey(appUserModelId)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey + appUserModelId);
            // What it was before, so it can be put back exactly. A 0 left by an older
            // version of this app (which never put it back) is treated as "not set".
            var before = key.GetValue("ShowBanner") as int?;
            changed[appUserModelId] = before == 0 ? null : before;
            AppSettings.Current.Save();   // written down before changing anything
            key.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>Puts every app's banner setting back the way it was.</summary>
    public void RestoreAll()
    {
        var changed = AppSettings.Current.SuppressedBanners;
        if (changed.Count == 0) return;
        foreach (var (aumid, before) in new List<KeyValuePair<string, int?>>(changed))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppKey + aumid, writable: true);
                if (key != null)
                {
                    if (before is int v) key.SetValue("ShowBanner", v, RegistryValueKind.DWord);
                    else key.DeleteValue("ShowBanner", throwOnMissingValue: false);
                }
                changed.Remove(aumid);
            }
            catch { }
        }
        AppSettings.Current.Save();
        CrashLog.Write("Windows notification banners restored");
    }
}
