namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;

/// <summary>
/// Reads and sets the volume of one app only (its entry in the Windows
/// Volume Mixer), e.g. Spotify, without touching the system volume.
/// </summary>
public static class AppVolume
{
    /// <summary>Volume 0..1 of the app's audio, or null if it has no audio session.</summary>
    public static float? Get(string? appId)
    {
        float? result = null;
        ForEachSession(appId, v => { result ??= v.Volume; });
        return result;
    }

    public static void Set(string? appId, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        ForEachSession(appId, v =>
        {
            v.Volume = volume;
            if (v.Mute && volume > 0) v.Mute = false;
        });
    }

    private static void ForEachSession(string? appId, Action<SimpleAudioVolume> action)
    {
        var processName = ProcessNameFor(appId);
        if (processName == null) return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device     = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                uint pid = session.GetProcessID;
                if (pid == 0) continue;

                string name;
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch { continue; }

                if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                    action(session.SimpleAudioVolume);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Note($"AppVolume: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps the media session's app id to the process that plays the audio:
    /// "Spotify.exe" -> "Spotify", "MSEdge" -> "msedge",
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" -> "Spotify".
    /// </summary>
    private static string? ProcessNameFor(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;

        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Edge"]    = "msedge",
            ["Chrome"]  = "chrome",
            ["Firefox"] = "firefox",
            ["Brave"]   = "brave",
            ["Opera"]   = "opera",
            ["Spotify"] = "Spotify",
        };
        foreach (var kv in known)
            if (appId.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;

        var bang = appId.LastIndexOf('!');
        if (bang >= 0) return appId[(bang + 1)..];

        return Path.GetFileNameWithoutExtension(appId);
    }
}
