namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

/// <summary>
/// Deadlines ("Hạn chót"): type something like "Nộp báo cáo PRO 15/11 17h"
/// and it's kept with its due time. Reminds you a day before, an hour before
/// and when it's due; old ones disappear two days after.
/// </summary>
public sealed class DeadlineService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>A deadline and how soon: "24h", "1h" or "due".</summary>
    public event Action<Deadline, string>? Remind;

    /// <summary>About once an hour (8:00–22:00), the nearest deadlines, so none are forgotten.</summary>
    public event Action<IReadOnlyList<Deadline>>? Summary;
    private int _lastSummaryHour = -1;
    private DateTime _lastReminder;
    public event Action? Changed;

    /// <summary>Once a day (from 7:00): exams in the next 14 days, nearest first.</summary>
    public event Action<IReadOnlyList<Deadline>>? ExamNotice;

    public DeadlineService()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();   // first check after 30 s, once the app is listening
    }

    public IReadOnlyList<Deadline> Items => AppSettings.Current.Deadlines.OrderBy(d => d.Due).ToList();

    /// <summary>The nearest deadline that isn't past yet.</summary>
    public Deadline? Next => AppSettings.Current.Deadlines.Where(d => d.Due > DateTime.Now).OrderBy(d => d.Due).FirstOrDefault();

    public Deadline? Add(string text, out string? problem)
    {
        var parsed = Parse(text, DateTime.Now);
        if (parsed == null)
        {
            problem = "Thêm ngày nhé, vd: 15/11 17h · mai 9h · thứ 6";
            return null;
        }
        var (title, due) = parsed.Value;
        if (title.Length == 0) { problem = "Thêm tên công việc nhé"; return null; }
        var d = new Deadline { Title = title, Due = due, Exam = IsExamTitle(title) };
        // Already within a window: don't fire that reminder at once
        var left = due - DateTime.Now;
        d.Warned24h = left <= TimeSpan.FromHours(24);
        d.Warned1h  = left <= TimeSpan.FromHours(1);
        d.WarnedDue = left <= TimeSpan.Zero;
        AppSettings.Current.Deadlines.Add(d);
        AppSettings.Current.Save();
        problem = null;
        Changed?.Invoke();
        return d;
    }

    /// <summary>Changes a deadline (name and time) from text like when adding one.</summary>
    public bool Update(Guid id, string text, out string? problem)
    {
        var d = AppSettings.Current.Deadlines.FirstOrDefault(x => x.Id == id);
        var parsed = Parse(text, DateTime.Now);
        if (d == null) { problem = "Hạn chót này không còn nữa"; return false; }
        if (parsed == null) { problem = "Thêm ngày nhé, vd: 15/11 17h · mai 9h · thứ 6"; return false; }
        if (parsed.Value.Title.Length == 0) { problem = "Thêm tên công việc nhé"; return false; }
        d.Title = parsed.Value.Title;
        d.Exam  = IsExamTitle(d.Title);
        if (d.Due != parsed.Value.Due)
        {
            d.Due = parsed.Value.Due;
            var left = d.Due - DateTime.Now;
            d.Warned24h = left <= TimeSpan.FromHours(24);
            d.Warned1h  = left <= TimeSpan.FromHours(1);
            d.WarnedDue = left <= TimeSpan.Zero;
        }
        AppSettings.Current.Save();
        problem = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The text that puts a deadline back in the box for editing: "Nộp báo cáo 15/11/2026 17:00".</summary>
    public static string EditText(Deadline d) => $"{d.Title} {d.Due:dd/MM/yyyy} {d.Due:HH:mm}";

    /// <summary>"Thi PE PRO192 20/11 7h30", "Kiểm tra giữa kỳ MAD 18/11": counted down by days, not hours.</summary>
    internal static bool IsExamTitle(string title) =>
        Regex.IsMatch(title, @"^(thi|kiểm tra|ktra|exam|pe|fe)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Exams not over yet, nearest first.</summary>
    public IReadOnlyList<Deadline> Exams =>
        AppSettings.Current.Deadlines.Where(d => d.Exam && d.Due > DateTime.Now.AddHours(-3)).OrderBy(d => d.Due).ToList();

    /// <summary>Something to worry about: a deadline within the hour, or an exam today.</summary>
    public bool Pressing
    {
        get
        {
            var now = DateTime.Now;
            return AppSettings.Current.Deadlines.Any(d =>
                (d.Due > now && d.Due - now <= TimeSpan.FromHours(1)) ||
                (d.Exam && d.Due.Date == now.Date && d.Due > now.AddHours(-3)));
        }
    }

    /// <summary>"còn 6 ngày", "ngày mai", "hôm nay 7:30" for an exam.</summary>
    public static string DaysLeft(DateTime due, DateTime now)
    {
        int days = (due.Date - now.Date).Days;
        return days switch
        {
            < 0  => "đã thi",
            0    => due > now ? $"hôm nay {due:HH:mm}" : "đang thi",
            1    => "ngày mai",
            _    => $"còn {days} ngày"
        };
    }

    // ── Quick notes (no time: kept as a note) ─────────────────────────
    public IReadOnlyList<QuickNote> Notes => AppSettings.Current.Notes.OrderByDescending(n => n.Time).ToList();

    public void AddNote(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        AppSettings.Current.Notes.Add(new QuickNote { Text = text });
        AppSettings.Current.Save();
        Changed?.Invoke();
    }

    public void UpdateNote(Guid id, string text)
    {
        var n = AppSettings.Current.Notes.FirstOrDefault(x => x.Id == id);
        if (n == null || text.Trim().Length == 0) return;
        n.Text = text.Trim();
        AppSettings.Current.Save();
        Changed?.Invoke();
    }

    public void RemoveNote(Guid id)
    {
        AppSettings.Current.Notes.RemoveAll(n => n.Id == id);
        AppSettings.Current.Save();
        Changed?.Invoke();
    }

    /// <summary>The exams brought in from one linked sheet take the place of the ones it gave before.</summary>
    public void ReplaceFromSource(string source, IEnumerable<Deadline> exams)
    {
        var old = AppSettings.Current.Deadlines.Where(d => d.Source == source).ToList();
        AppSettings.Current.Deadlines.RemoveAll(d => d.Source == source);
        var now = DateTime.Now;
        foreach (var d in exams)
        {
            d.Source = source;
            d.Exam = true;
            // The same exam read again keeps what was already said about it; a new one
            // that's already close isn't announced as if it were news
            if (old.FirstOrDefault(o => o.Title == d.Title && o.Due == d.Due) is { } same)
            {
                d.Id = same.Id; d.Warned24h = same.Warned24h; d.Warned1h = same.Warned1h; d.WarnedDue = same.WarnedDue;
            }
            else
            {
                d.Warned24h = d.Due <= now.AddHours(24);
                d.Warned1h = d.Due <= now.AddHours(1);
                d.WarnedDue = d.Due <= now;
            }
            AppSettings.Current.Deadlines.Add(d);
        }
        AppSettings.Current.Save();
        Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        AppSettings.Current.Deadlines.RemoveAll(d => d.Id == id);
        AppSettings.Current.Save();
        Changed?.Invoke();
    }

    private void Check()
    {
        var now = DateTime.Now;
        bool dirty = false;
        foreach (var d in AppSettings.Current.Deadlines.ToList())
        {
            var left = d.Due - now;
            if (left < TimeSpan.FromDays(-2))
            {
                AppSettings.Current.Deadlines.Remove(d);
                dirty = true;
                continue;
            }
            string? kind = null;
            if (!d.WarnedDue && left <= TimeSpan.Zero) { kind = "due"; d.WarnedDue = d.Warned1h = d.Warned24h = true; }
            else if (!d.Warned1h && left <= TimeSpan.FromHours(1)) { kind = "1h"; d.Warned1h = d.Warned24h = true; }
            else if (!d.Warned24h && left <= TimeSpan.FromHours(24)) { kind = "24h"; d.Warned24h = true; }
            if (kind != null)
            {
                dirty = true;
                _lastReminder = now;
                Remind?.Invoke(d, kind);
            }
        }

        // The hourly round-up: on the hour from 8 to 22 (15 a day), skipped when
        // a reminder just showed or there's nothing coming up.
        if (now.Hour is >= 8 and <= 22 && now.Hour != _lastSummaryHour && now.Minute < 10)
        {
            _lastSummaryHour = now.Hour;
            var next = AppSettings.Current.Deadlines.Where(d => d.Due > now.AddHours(-12)).OrderBy(d => d.Due).Take(3).ToList();
            if (next.Count > 0 && (now - _lastReminder).TotalMinutes > 15) Summary?.Invoke(next);
        }
        // Exams: once a day from 7:00, how many days are left
        if (now.Hour >= 7 && AppSettings.Current.ExamNoticeDay?.Date != now.Date)
        {
            var exams = AppSettings.Current.Deadlines
                .Where(d => d.Exam && d.Due > now && (d.Due.Date - now.Date).TotalDays <= 14)
                .OrderBy(d => d.Due).Take(2).ToList();
            AppSettings.Current.ExamNoticeDay = now.Date;
            dirty = true;
            if (exams.Count > 0) ExamNotice?.Invoke(exams);
        }
        if (dirty)
        {
            AppSettings.Current.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>"còn 2 ngày", "còn 3 giờ 20 phút", "quá hạn 1 giờ".</summary>
    public static string Left(DateTime due, DateTime now)
    {
        var t = due - now;
        bool late = t < TimeSpan.Zero;
        if (late) t = -t;
        string s = t.TotalDays >= 1 ? $"{(int)t.TotalDays} ngày{(t.Hours > 0 && t.TotalDays < 3 ? $" {t.Hours} giờ" : "")}"
                 : t.TotalHours >= 1 ? $"{(int)t.TotalHours} giờ{(t.Minutes > 0 ? $" {t.Minutes} phút" : "")}"
                 : $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))} phút";
        return late ? $"quá hạn {s}" : $"còn {s}";
    }

    // ── Reading what you typed ────────────────────────────────────────
    private static readonly Regex DateRx = new(@"(?<!\d)(\d{1,2})[/\-.](\d{1,2})(?:[/\-.](\d{2,4}))?(?!\d)", RegexOptions.Compiled);
    private static readonly Regex TimeRx = new(@"(?<![\d/])(\d{1,2})(?:\s*(?:h|g|giờ|:)\s*(\d{2})?)(?:\s*(sáng|chiều|tối|trưa|pm|am))?(?![\p{L}\d])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WordRx = new(@"(?<!\p{L})(hôm nay|nay|ngày mai|mai|ngày mốt|mốt|tuần sau|thứ\s*([2-7]|hai|ba|tư|năm|sáu|bảy)|chủ nhật|cn)(?!\p{L})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Filler = new(@"(?<!\p{L})(lúc|vào|ngày|hạn|deadline|trước)(?!\p{L})\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Words that only said when (taken out of the name): "sáng mai 9h đi cà phê" → "Đi cà phê"
    private static readonly Regex WhenWords = new(@"(?<!\p{L})(buổi\s+)?(sáng|trưa|chiều|tối|đêm|khuya)(?!\p{L})|(?<!\p{L})(lúc|vào lúc|vào|hạn chót|hạn|deadline)(?=\s|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (string Title, DateTime Due)? Parse(string text, DateTime now)
    {
        string rest = " " + text.Trim() + " ";
        DateTime? date = null;
        TimeSpan? time = null;

        var m = DateRx.Match(rest);
        if (m.Success)
        {
            int day = int.Parse(m.Groups[1].Value), month = int.Parse(m.Groups[2].Value);
            int year = now.Year;
            if (m.Groups[3].Success) { year = int.Parse(m.Groups[3].Value); if (year < 100) year += 2000; }
            if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
            {
                var d = new DateTime(year, month, day);
                if (!m.Groups[3].Success && d < now.Date.AddDays(-1)) d = d.AddYears(1);
                date = d;
                rest = rest.Remove(m.Index, m.Length).Insert(m.Index, " ");
            }
        }

        if (date == null)
        {
            var w = WordRx.Match(rest);
            if (w.Success)
            {
                string word = w.Value.ToLower(CultureInfo.GetCultureInfo("vi-VN"));
                date = word switch
                {
                    "hôm nay" or "nay" => now.Date,
                    "ngày mai" or "mai" => now.Date.AddDays(1),
                    "ngày mốt" or "mốt" => now.Date.AddDays(2),
                    "tuần sau" => now.Date.AddDays(7),
                    _ => NextWeekday(now, word)
                };
                rest = rest.Remove(w.Index, w.Length).Insert(w.Index, " ");
            }
        }

        var t = TimeRx.Match(rest);
        if (t.Success)
        {
            int h = int.Parse(t.Groups[1].Value);
            int min = t.Groups[2].Success ? int.Parse(t.Groups[2].Value) : 0;
            string part = t.Groups[3].Value.ToLower();
            if ((part is "chiều" or "tối" or "pm") && h < 12) h += 12;
            if (part == "trưa" && h < 6) h += 12;
            if (h <= 23 && min <= 59)
            {
                time = new TimeSpan(h, min, 0);
                rest = rest.Remove(t.Index, t.Length).Insert(t.Index, " ");
            }
        }

        if (date == null && time == null) return null;
        DateTime due;
        if (date == null)
        {
            due = now.Date + time!.Value;
            if (due <= now) due = due.AddDays(1);
        }
        else due = date.Value + (time ?? new TimeSpan(23, 59, 0));

        // "sáng/chiều/tối" next to the time also sets the part of the day when no time-of-day word was matched
        if (time is { } tm && tm.Hours < 12 && Regex.IsMatch(rest, @"(?<!\p{L})(chiều|tối)(?!\p{L})", RegexOptions.IgnoreCase))
        {
            time = tm.Add(TimeSpan.FromHours(12));
            due = (date ?? due.Date) + time.Value;
            if (date == null && due <= now) due = due.AddDays(1);
        }
        string title = WhenWords.Replace(rest, " ");
        title = Regex.Replace(title, @"\s+", " ").Trim(' ', ',', '-', '·', ':');
        title = Filler.Replace(title, "").Trim(' ', ',', '-', '·', ':');
        if (title.Length > 0) title = char.ToUpper(title[0], CultureInfo.GetCultureInfo("vi-VN")) + title[1..];
        return (title, due);
    }

    private static DateTime NextWeekday(DateTime now, string word)
    {
        DayOfWeek target = word switch
        {
            var s when s.StartsWith("chủ") || s == "cn" => DayOfWeek.Sunday,
            var s when s.EndsWith("2") || s.EndsWith("hai") => DayOfWeek.Monday,
            var s when s.EndsWith("3") || s.EndsWith("ba") => DayOfWeek.Tuesday,
            var s when s.EndsWith("4") || s.EndsWith("tư") => DayOfWeek.Wednesday,
            var s when s.EndsWith("5") || s.EndsWith("năm") => DayOfWeek.Thursday,
            var s when s.EndsWith("6") || s.EndsWith("sáu") => DayOfWeek.Friday,
            _ => DayOfWeek.Saturday
        };
        int add = ((int)target - (int)now.DayOfWeek + 7) % 7;
        if (add == 0) add = 7;
        return now.Date.AddDays(add);
    }
}
