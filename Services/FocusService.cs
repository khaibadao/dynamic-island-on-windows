namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DynamicIslandNotifier.Core;

/// <summary>One finished focus session: a tree that grew (Success) or died.</summary>
public sealed record FocusSession(DateTime StartUtc, int Minutes, bool Success, string? Kind = null)
{
    public TreeKind TreeKind => TreeCatalog.Parse(Kind);
}

/// <summary>
/// Forest-style focus timer: plant a tree for N minutes; it grows while the
/// timer runs and only dies if you give up. Finished sessions are kept in
/// %LOCALAPPDATA%\DynamicIslandNotifier\forest.json. A session in progress
/// survives closing the app or restarting the PC.
/// </summary>
public sealed class FocusService
{
    public static readonly int[] Presets = { 10, 25, 45, 60, 90 };
    public const int MinMinutes = 5, MaxMinutes = 180;   // the ruler's range

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "forest.json");

    private readonly object _gate = new();
    private readonly List<FocusSession> _log;
    private readonly Timer _timer;

    public DateTime? StartUtc { get; private set; }
    public int Minutes { get; private set; }
    public TreeKind CurrentKind { get; private set; } = TreeKind.Oak;
    public bool IsRunning => StartUtc != null;

    /// <summary>Started, finished or given up. Raised on a background thread.</summary>
    public event Action? Changed;

    /// <summary>A session ended (tree grown or dead). Raised on a background thread.</summary>
    public event Action<FocusSession>? Finished;

    public FocusService()
    {
        _log = Load();

        // Pick up a session that was running when the app last closed.
        var s = AppSettings.Current;
        if (s.FocusStartUtc is { } start && s.FocusMinutes > 0)
        {
            StartUtc    = start;
            Minutes     = s.FocusMinutes;
            CurrentKind = TreeCatalog.Parse(s.FocusKind);
        }

        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public TimeSpan Total => TimeSpan.FromMinutes(Minutes);

    public TimeSpan Remaining
    {
        get
        {
            if (StartUtc is not { } start) return TimeSpan.Zero;
            var left = start + Total - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>0 when just planted, 1 when fully grown.</summary>
    public double Progress
    {
        get
        {
            if (StartUtc is not { } start || Minutes <= 0) return 0;
            return Math.Clamp((DateTime.UtcNow - start).TotalSeconds / Total.TotalSeconds, 0, 1);
        }
    }

    /// <summary>Whether this kind of tree has been unlocked (enough hours of focus).</summary>
    public bool IsUnlocked(TreeKind kind) => FocusedHours >= TreeCatalog.Get(kind).UnlockHours;

    /// <summary>Hours of focus in trees that grew to the end (given-up ones don't count).</summary>
    public double FocusedHours
    {
        get { lock (_gate) return _log.Where(s => s.Success).Sum(s => s.Minutes) / 60.0; }
    }

    public void Start(int minutes, TreeKind kind)
    {
        if (minutes <= 0) return;
        if (!IsUnlocked(kind)) kind = TreeKind.Oak;
        lock (_gate)
        {
            if (IsRunning) return;
            StartUtc    = DateTime.UtcNow;
            Minutes     = minutes;
            CurrentKind = kind;
            Persist();
        }
        Changed?.Invoke();
    }

    public void GiveUp() => Finish(success: false);

    private void Tick()
    {
        if (StartUtc is { } start && DateTime.UtcNow >= start + Total)
            Finish(success: true);
    }

    private void Finish(bool success)
    {
        FocusSession session;
        lock (_gate)
        {
            if (StartUtc is not { } start) return;
            session = new FocusSession(start, Minutes, success, CurrentKind.ToString());
            _log.Add(session);
            StartUtc = null;
            Minutes  = 0;
            Persist();
            Save();
        }
        Changed?.Invoke();
        Finished?.Invoke(session);
    }

    // ── Stats ─────────────────────────────────────────────────────────
    /// <summary>Sessions started today (local time), oldest first.</summary>
    /// <summary>Sessions started between two local times, oldest first.</summary>
    public IReadOnlyList<FocusSession> Between(DateTime fromLocal, DateTime toLocal)
    {
        lock (_gate)
            return _log.Where(s => { var t = s.StartUtc.ToLocalTime(); return t >= fromLocal && t < toLocal; }).ToList();
    }

    public IReadOnlyList<FocusSession> Today()
    {
        lock (_gate)
            return _log.Where(s => s.StartUtc.ToLocalTime().Date == DateTime.Today).ToList();
    }

    /// <summary>Minutes of grown trees per day, for the last <paramref name="days"/> days (oldest first, today last).</summary>
    public IReadOnlyList<(DateTime Day, int Minutes, int Trees)> LastDays(int days)
    {
        lock (_gate)
        {
            var today = DateTime.Today;
            return Enumerable.Range(0, days)
                .Select(i => today.AddDays(i - days + 1))
                .Select(day =>
                {
                    var grown = _log.Where(s => s.Success && s.StartUtc.ToLocalTime().Date == day).ToList();
                    return (day, grown.Sum(s => s.Minutes), grown.Count);
                })
                .ToList();
        }
    }

    /// <summary>
    /// Days in a row with at least one grown tree, ending today (or yesterday,
    /// so the streak isn't shown as broken before you've planted today).
    /// </summary>
    public int Streak()
    {
        lock (_gate)
        {
            var days = _log.Where(s => s.Success).Select(s => s.StartUtc.ToLocalTime().Date).ToHashSet();
            var day = days.Contains(DateTime.Today) ? DateTime.Today : DateTime.Today.AddDays(-1);
            int streak = 0;
            while (days.Contains(day)) { streak++; day = day.AddDays(-1); }
            return streak;
        }
    }

    public int TotalTrees
    {
        get { lock (_gate) return _log.Count(s => s.Success); }
    }

    // ── Storage ───────────────────────────────────────────────────────
    private void Persist()
    {
        AppSettings.Current.FocusStartUtc = StartUtc;
        AppSettings.Current.FocusMinutes  = Minutes;
        AppSettings.Current.FocusKind     = StartUtc != null ? CurrentKind.ToString() : null;
        AppSettings.Current.Save();
    }

    private static List<FocusSession> Load()
    {
        try
        {
            if (File.Exists(LogPath))
                return JsonSerializer.Deserialize<List<FocusSession>>(File.ReadAllText(LogPath)) ?? new();
        }
        catch { }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, JsonSerializer.Serialize(_log));
        }
        catch { }
    }
}
