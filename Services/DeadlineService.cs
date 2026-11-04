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
    public event Action? Changed;

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
        var d = new Deadline { Title = title, Due = due };
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
                Remind?.Invoke(d, kind);
            }
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

        string title = Regex.Replace(rest, @"\s+", " ").Trim(' ', ',', '-', '·', ':');
        title = Filler.Replace(title, "").Trim(' ', ',', '-', '·', ':');
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
