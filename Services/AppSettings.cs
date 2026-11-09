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
    /// <summary>Quick notes (Ctrl + Alt + N, or a line without a time).</summary>
    public List<QuickNote> Notes { get; set; } = new();
    /// <summary>The day the morning exam countdown was last shown.</summary>
    public DateTime? ExamNoticeDay { get; set; }
    /// <summary>The Sunday the week's summary was last shown.</summary>
    public DateTime? WeeklySummaryDay { get; set; }

    // ── Pomodoro (in tree planting) ──
    /// <summary>"free" (pick the minutes) or "pomodoro" (study / rest rounds).</summary>
    public string FocusMode { get; set; } = "free";
    public string PomoPlan { get; set; } = "short";
    public int PomoCustomStudy { get; set; } = 30;
    public int PomoCustomBreak { get; set; } = 5;
    public bool PomoActive { get; set; }
    public int PomoRound { get; set; } = 1;
    public int PomoStudy { get; set; } = 25;
    public int PomoBreak { get; set; } = 5;
    public DateTime? PomoBreakUntilUtc { get; set; }
    public bool PomoLongBreak { get; set; }
    public int PomoBreakMinutes { get; set; }
    public string? PomoKind { get; set; }

    // ── Lịch học ──
    /// <summary>Classes, each a weekly slot (or one date).</summary>
    public List<ClassSlot> Schedule { get; set; } = new();
    /// <summary>A Google Sheet link the schedule is read from (refreshed every few hours).</summary>
    public string? ScheduleSheetUrl { get; set; }
    public DateTime? ScheduleSyncedUtc { get; set; }
    /// <summary>Remind 15 minutes before each class; the evening line about tomorrow.</summary>
    public bool ScheduleReminders { get; set; } = true;
    public DateTime? TomorrowNoticeDay { get; set; }

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
    /// <summary>Show Mây the cat (on the pill and in the panel). Off: she's gone and costs nothing.</summary>
    public bool ShowCat { get; set; } = true;
    /// <summary>Apps that have shown on the island before: their banner is hidden again as soon as the app starts.</summary>
    public List<string> BannerApps { get; set; } = new();
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
    /// <summary>Stop the drifting notes when Windows' "Animation effects" is off (off by default).</summary>
    public bool RespectWindowsAnimations { get; set; }
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

    /// <summary>Held while saving (and by code that changes settings off the UI thread).</summary>
    public static readonly object SaveLock = new();

    public void Save()
    {
        lock (SaveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temp = FilePath + ".tmp";   // write fully, then swap: never a half-written file
                File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch { }
        }
    }
}

/// <summary>One class: a weekday (or a single date), start and end time, subject and room.</summary>
public sealed class ClassSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Monday = 1 … Sunday = 7 (ignored when <see cref="Date"/> is set).</summary>
    public int Weekday { get; set; }
    /// <summary>A single day (FAP's weekly page, a calendar file); null = every week.</summary>
    public DateTime? Date { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Subject { get; set; } = "";
    public string? Room { get; set; }
    /// <summary>Where it came from: "sheet", "file", "fap", "photo", "typed".</summary>
    public string? Origin { get; set; }
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
    /// <summary>An exam ("Thi …"): counted down in days, a notice each morning.</summary>
    public bool Exam { get; set; }
}

/// <summary>A line typed without a time: kept as a note.</summary>
public sealed class QuickNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
}
