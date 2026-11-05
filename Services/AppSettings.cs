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

    /// <summary>Lyrics pane open next to the player.</summary>
    public bool LyricsOpen { get; set; }

    /// <summary>Deep focus: nudge when a distracting app / site comes up while a tree grows.</summary>
    public bool DeepFocus { get; set; } = true;
    public List<string> DeepFocusList { get; set; } = new(Services.DeepFocus.DefaultList);

    /// <summary>Hạn chót: deadlines you count down to.</summary>
    public List<Deadline> Deadlines { get; set; } = new();

    /// <summary>Colour wash behind the panel: "full", "light" (still picture) or "off".</summary>
    public string Ambient { get; set; } = "full";

    /// <summary>Mây strolls across the notch now and then.</summary>
    public bool MayWalks { get; set; } = true;

    /// <summary>Step aside while a video, game or F11 page fills the screen.</summary>
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>Where the floating tree timer was dragged to (null = bottom-right corner).</summary>
    public double? FocusWidgetLeft { get; set; }
    public double? FocusWidgetTop  { get; set; }

    /// <summary>Do Not Disturb: notifications don't drop down (they still go to the history).</summary>
    public bool DoNotDisturb { get; set; }

    /// <summary>True once "start with Windows" was set up on first run (the user may have turned it off since).</summary>
    public bool AutoStartConfigured { get; set; }

    // ── Changes to Windows settings, always undone ──
    /// <summary>Apps whose Windows banner is hidden while the app runs, with the value it had before (null: not set).</summary>
    public Dictionary<string, int?> SuppressedBanners { get; set; } = new();
    public bool HideWindowsBanners { get; set; } = true;
    public bool GlobalToastsRepaired { get; set; }

    // ── Privacy switches (Cài đặt & quyền riêng tư) ──
    /// <summary>Read the title of the window in front (deep focus, presenting).</summary>
    public bool ReadWindowTitles { get; set; } = true;
    /// <summary>Listen to what the speakers play, for the sound bars.</summary>
    public bool ListenToAudio { get; set; } = true;
    /// <summary>Website logos for notifications, from Google's icon service.</summary>
    public bool WebIcons { get; set; } = true;
    /// <summary>Look lyrics up on NetEase too (LRCLIB is always used).</summary>
    public bool NetEaseLyrics { get; set; } = true;
    /// <summary>Deadline reminders show even with Do Not Disturb on.</summary>
    public bool DeadlinesBypassDnd { get; set; } = true;

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

/// <summary>A deadline to count down to; reminders already sent are remembered.</summary>
public sealed class Deadline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTime Due { get; set; }
    public bool Warned24h { get; set; }
    public bool Warned1h { get; set; }
    public bool WarnedDue { get; set; }
}
