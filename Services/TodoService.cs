namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using DynamicIslandNotifier.Core;

/// <summary>A to-do for today. Due is a time of day, or null.</summary>
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public TimeSpan? Due { get; set; }
    public bool Done { get; set; }
    public DateTime Day { get; set; } = DateTime.Today;    // the day it belongs to
    public bool FromEarlier { get; set; }                  // carried over from a previous day

    // Reminder bookkeeping (so each one fires once)
    public bool WarnedSoon { get; set; }
    public bool WarnedLate { get; set; }
}

/// <summary>
/// Today's to-do list, kept in %LOCALAPPDATA%\DynamicIslandNotifier\todos.json,
/// and the reminders that keep asking until the day is planned and done:
/// - no to-dos yet today: every 30 minutes ("Hôm nay bạn sẽ làm những gì?")
/// - to-dos left: every 60 minutes, plus 15 minutes before a due time and when it's overdue.
/// Unfinished to-dos move to the next day.
/// </summary>
public sealed class TodoService
{
    public static readonly TimeSpan EmptyEvery   = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PendingEvery = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan SoonBefore   = TimeSpan.FromMinutes(15);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "todos.json");

    private readonly object _gate = new();
    private readonly List<TodoItem> _items;
    private readonly Timer _timer;
    private DateTime _nextNag;         // next "empty" / "pending" reminder
    private DateTime _today;

    /// <summary>The list changed. Raised on any thread.</summary>
    public event Action? Changed;

    /// <summary>A reminder to show. Raised on a background thread.</summary>
    public event Action<NotificationData>? Remind;

    /// <summary>What clicking a reminder does (set by the app: open the to-do tab).</summary>
    public Action? OpenTodo { get; set; }

    public TodoService()
    {
        _items = Load();
        _today = DateTime.Today;
        RollOver();

        // First nudge a minute after start-up (e.g. right after turning the PC on).
        _nextNag = DateTime.Now.AddMinutes(1);
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    // ── List ──────────────────────────────────────────────────────────
    /// <summary>Today's to-dos: unfinished first (by due time), then finished.</summary>
    public IReadOnlyList<TodoItem> Today()
    {
        lock (_gate)
        {
            return _items.Where(i => i.Day == DateTime.Today)
                         .OrderBy(i => i.Done)
                         .ThenBy(i => i.Due ?? TimeSpan.MaxValue)
                         .ToList();
        }
    }

    public (int Done, int Total) Progress()
    {
        var today = Today();
        return (today.Count(i => i.Done), today.Count);
    }

    /// <summary>
    /// Adds a to-do. A time in the text sets its due time:
    /// "Nộp lab OSG 17:00", "họp nhóm 9h30", "gym 18h".
    /// </summary>
    public void Add(string input)
    {
        var (text, due) = Parse(input);
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_gate)
        {
            bool wasEmpty = !_items.Any(i => i.Day == DateTime.Today);
            _items.Add(new TodoItem { Text = text, Due = due, Day = DateTime.Today });
            Save();
            // The day is planned: next reminder is the hourly one.
            if (wasEmpty) _nextNag = DateTime.Now + PendingEvery;
        }
        Changed?.Invoke();
    }

    public void Toggle(Guid id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;
            item.Done = !item.Done;
            Save();
        }
        Changed?.Invoke();
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => i.Id == id);
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Splits "text 17:00" into text and due time.</summary>
    public static (string Text, TimeSpan? Due) Parse(string input)
    {
        input = input.Trim();
        var m = Regex.Match(input, @"(?:^|\s)(?:lúc\s+)?([01]?\d|2[0-3])(?:[:h]([0-5]\d)?|h)(?=\s|$)", RegexOptions.IgnoreCase);
        if (!m.Success) return (input, null);

        int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var text = (input[..m.Index] + " " + input[(m.Index + m.Length)..]).Trim();
        text = Regex.Replace(text, @"\s{2,}", " ");
        return (text.Length > 0 ? text : input, new TimeSpan(h, min, 0));
    }

    // ── Reminders ─────────────────────────────────────────────────────
    private void Tick()
    {
        try
        {
            var now = DateTime.Now;
            if (now.Date != _today)
            {
                _today = now.Date;
                RollOver();
                _nextNag = now;   // new day: ask right away
                Changed?.Invoke();
            }

            List<TodoItem> today;
            lock (_gate) today = _items.Where(i => i.Day == now.Date).ToList();
            var pending = today.Where(i => !i.Done).ToList();

            // Due times: 15 minutes before, and once overdue
            bool dirty = false;
            foreach (var item in pending.Where(i => i.Due != null))
            {
                var due = now.Date + item.Due!.Value;
                if (!item.WarnedLate && now >= due)
                {
                    item.WarnedLate = item.WarnedSoon = dirty = true;
                    Remind?.Invoke(Make("Quá hạn rồi!", $"{item.Text} · hạn {Format(item.Due.Value)}", urgent: true));
                }
                else if (!item.WarnedSoon && now >= due - SoonBefore)
                {
                    item.WarnedSoon = dirty = true;
                    var left = (int)Math.Ceiling((due - now).TotalMinutes);
                    Remind?.Invoke(Make($"Còn {left} phút", $"{item.Text} · hạn {Format(item.Due.Value)}", urgent: true));
                }
            }
            if (dirty) lock (_gate) Save();

            // The regular nudge
            if (now < _nextNag) return;

            if (today.Count == 0)
            {
                Remind?.Invoke(Make("Hôm nay bạn sẽ làm những gì?",
                                    "Chưa có việc nào cho hôm nay · bấm để thêm việc cần làm"));
                _nextNag = now + EmptyEvery;
            }
            else if (pending.Count > 0)
            {
                var names = string.Join(", ", pending.Take(3).Select(i => i.Text)) + (pending.Count > 3 ? "…" : "");
                Remind?.Invoke(Make($"Còn {pending.Count}/{today.Count} việc chưa xong", names));
                _nextNag = now + PendingEvery;
            }
            else
            {
                _nextNag = now + PendingEvery;   // all done: nothing to say
            }
        }
        catch (Exception ex)
        {
            DebugLog.Note($"TodoService: {ex.Message}");
        }
    }

    private NotificationData Make(string title, string body, bool urgent = false) => new()
    {
        AppName    = "Việc cần làm",
        Title      = title,
        Body       = body,
        Glyph      = "",                       // checkbox
        GlyphColor = urgent ? "#FF9F0A" : "#0A84FF",
        SourceKey  = null,                           // reminders can't be muted (you asked to be nagged)
        OnClick    = OpenTodo
    };

    private static string Format(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    // ── Days ──────────────────────────────────────────────────────────
    /// <summary>Unfinished to-dos from earlier days move to today; old finished ones are dropped.</summary>
    private void RollOver()
    {
        lock (_gate)
        {
            var today = DateTime.Today;
            bool changed = false;
            foreach (var item in _items.Where(i => i.Day < today && !i.Done))
            {
                item.Day = today;
                item.FromEarlier = true;
                item.WarnedSoon = item.WarnedLate = false;
                changed = true;
            }
            changed |= _items.RemoveAll(i => i.Day < today.AddDays(-7)) > 0;   // keep a week of done items
            if (changed) Save();
        }
    }

    // ── Storage ───────────────────────────────────────────────────────
    private static List<TodoItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch { }
    }
}
