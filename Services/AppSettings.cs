namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// Small settings file kept between runs, at
/// %LOCALAPPDATA%\DynamicIslandNotifier\settings.json.
/// </summary>
public sealed class AppSettings
{
    public bool MusicEnabled { get; set; } = true;

    /// <summary>The notch bar at the top centre of the screen.</summary>
    public bool NotchEnabled { get; set; } = true;

    // ── Focus (tree planting) ─────────────────────────────────────────
    /// <summary>Start of the focus session in progress, so it survives a restart.</summary>
    public DateTime? FocusStartUtc { get; set; }
    public int FocusMinutes { get; set; }
    public string? FocusKind { get; set; }
    public int LastFocusMinutes { get; set; } = 25;
    public string? LastTreeKind { get; set; }

    /// <summary>The notch tab shown last: "music", "focus" or "notifications".</summary>
    public string LastNotchTab { get; set; } = "music";

    /// <summary>True once "start with Windows" was set up on first run (the user may have turned it off since).</summary>
    public bool AutoStartConfigured { get; set; }

    /// <summary>Muted senders: source key → name to show in the menu.</summary>
    public Dictionary<string, string> MutedSources { get; set; } = new();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "settings.json");

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
