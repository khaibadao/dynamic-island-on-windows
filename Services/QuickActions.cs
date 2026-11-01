namespace DynamicIslandNotifier.Services;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

/// <summary>
/// The notch's quick buttons: microphone on/off, Do Not Disturb (pauses
/// the island's notifications), screenshot, lock the PC.
/// </summary>
public static class QuickActions
{
    // ── Do Not Disturb ────────────────────────────────────────────────
    /// <summary>
    /// While on, notifications don't drop down from the top (they still go to
    /// the history). Only affects this app; Windows' own settings are untouched.
    /// </summary>
    public static bool DoNotDisturb
    {
        get => AppSettings.Current.DoNotDisturb;
        set
        {
            AppSettings.Current.DoNotDisturb = value;
            AppSettings.Current.Save();
        }
    }

    // ── Microphone ────────────────────────────────────────────────────
    /// <summary>Whether the default microphone is muted; null if there's no microphone.</summary>
    public static bool? IsMicMuted()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return mic.AudioEndpointVolume.Mute;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Mutes / unmutes the default microphone. Returns the new state, or null if there's none.</summary>
    public static bool? ToggleMic()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            mic.AudioEndpointVolume.Mute = !mic.AudioEndpointVolume.Mute;
            return mic.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            DebugLog.Note($"ToggleMic: {ex.Message}");
            return null;
        }
    }

    // ── Screenshot / lock ─────────────────────────────────────────────
    /// <summary>Opens Windows' snipping overlay (same as Win + Shift + S).</summary>
    public static void Screenshot()
    {
        try { Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
        catch (Exception ex) { DebugLog.Note($"Screenshot: {ex.Message}"); }
    }

    [DllImport("user32.dll")] private static extern bool LockWorkStation();

    public static void Lock() => LockWorkStation();
}
