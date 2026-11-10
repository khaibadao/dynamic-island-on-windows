namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Reads a timetable from what people have at hand: a table (Google Sheet,
/// .csv, .xlsx), a calendar file (.ics), the text copied from FAP's weekly
/// timetable, or the words read out of a photo of a timetable. Nothing here
/// touches the network or the disk except the file it's given.
/// </summary>
public static class ScheduleImport
{
    // "PRF192", "OSG202", "MAD101", "SWP391", "PRO192c"
    private static readonly Regex CodeRx = new(@"(?<![A-Za-z0-9])([A-Z]{2,4}\d{3}[a-zA-Z]?)(?![A-Za-z0-9])", RegexOptions.Compiled);
    // "7:30-9:50", "07h30 – 09h50", "7:30 đến 9:50"
    // "7:30-9:50", "07h30 – 09h50", "7h-7h45", "13h - 14h", "22h30-0h", "7:30 đến 9:50"
    private static readonly Regex RangeRx = new(@"(?<!\d)(\d{1,2})\s*(?:[:.]\s*(\d{2})|[hg]\s*(\d{2})?)\s*(?:-|–|—|~|đến|to)\s*(\d{1,2})\s*(?:[:.]\s*(\d{2})|[hg]\s*(\d{2})?)(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OneTimeRx = new(@"^\s*(\d{1,2})\s*(?:[:hg.]\s*(\d{2})?)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // "at DE-C201", "P.304", "Phòng 304", "room 201", "BE-305"
    private static readonly Regex RoomRx = new(@"(?:\bat\s+|\bph(?:ò|o)ng\s*|\broom\s*|\bP\.\s?)([A-Za-z0-9][A-Za-z0-9.\-]*\d[A-Za-z0-9.\-]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DayMonthRx = new(@"(?<!\d)(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?(?!\d)", RegexOptions.Compiled);

    /// <summary>FPT's usual slots, for a timetable that only says "Slot 3".</summary>
    private static readonly (TimeSpan Start, TimeSpan End)[] FptSlots =
    {
        (new(7, 0, 0),  new(7, 0, 0)),     // slot 0: unused placeholder
        (new(7, 30, 0), new(9, 50, 0)),
        (new(10, 0, 0), new(12, 20, 0)),
        (new(12, 50, 0), new(15, 10, 0)),
        (new(15, 20, 0), new(17, 40, 0)),
        (new(17, 50, 0), new(20, 10, 0)),
        (new(20, 20, 0), new(22, 40, 0)),
    };

    // ── Weekday words ─────────────────────────────────────────────────
    /// <summary>Monday = 1 … Sunday = 7 from "Thứ 2", "T2", "2", "Mon", "Monday", "CN", "Chủ nhật"; 0 if not a day.</summary>
    internal static int Weekday(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace(".", "");
        t = Regex.Replace(t, @"\s+", " ");
        if (t.Length == 0) return 0;
        if (t is "cn" or "chủ nhật" or "chu nhat" or "sun" or "sunday" or "su") return 7;
        string[] en = { "mon", "tue", "wed", "thu", "fri", "sat" };
        string[] enLong = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };
        for (int i = 0; i < 6; i++) if (t == en[i] || t == enLong[i]) return i + 1;
        string[] vi = { "hai", "ba", "tư", "năm", "sáu", "bảy" };
        var m = Regex.Match(t, @"^(?:thứ|thu|t)\s*([2-7]|hai|ba|tư|tu|năm|nam|sáu|sau|bảy|bay)$");
        string? n = m.Success ? m.Groups[1].Value : Regex.IsMatch(t, @"^[2-7]$") ? t : null;
        if (n == null) return 0;
        if (int.TryParse(n, out int k)) return k - 1;
        n = n switch { "tu" => "tư", "nam" => "năm", "sau" => "sáu", "bay" => "bảy", _ => n };
        int idx = Array.IndexOf(vi, n);
        return idx >= 0 ? idx + 1 : 0;
    }

    private static TimeSpan? Time(string text)
    {
        var m = OneTimeRx.Match(text);
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return h is >= 0 and < 24 && min < 60 ? new TimeSpan(h, min, 0) : null;
    }

    private static (TimeSpan, TimeSpan)? Range(string text) => RangeAt(text, out _);

    private static (TimeSpan, TimeSpan)? RangeAt(string text, out Match match)
    {
        match = RangeRx.Match(text);
        if (!match.Success) return null;
        static int Min(Match m, int a, int b) => m.Groups[a].Success ? int.Parse(m.Groups[a].Value) : m.Groups[b].Success ? int.Parse(m.Groups[b].Value) : 0;
        int h1 = int.Parse(match.Groups[1].Value), m1 = Min(match, 2, 3);
        int h2 = int.Parse(match.Groups[4].Value), m2 = Min(match, 5, 6);
        if (h1 > 23 || h2 > 24 || m1 > 59 || m2 > 59) return null;
        var a = new TimeSpan(h1, m1, 0);
        var b = new TimeSpan(h2, m2, 0);
        if (b <= a && h2 == 0) b += TimeSpan.FromDays(1);   // "22h30-0h": until midnight
        return b > a ? (a, b) : null;
    }

    // ── A table: Google Sheet / .csv / .xlsx ──────────────────────────
    /// <summary>
    /// Rows with a header: Thứ · Bắt đầu · Kết thúc · Môn · Phòng (any order, Vietnamese or
    /// English; "Giờ" with "7:30-9:50" works too, and "Ngày" for a single date).
    /// A grid (days across the top, slots down the side) is read as well.
    /// </summary>
    public static List<ClassSlot> FromTable(IReadOnlyList<string[]> rows, string origin)
    {
        var result = new List<ClassSlot>();
        if (rows.Count == 0) return result;

        // Grid layout: a row with at least 5 weekday names
        for (int r = 0; r < Math.Min(rows.Count, 6); r++)
        {
            var dayCols = rows[r].Select((c, i) => (Day: Weekday(c), Col: i)).Where(x => x.Day > 0).ToList();
            if (dayCols.Count >= 5) return FromGrid(rows.Skip(r + 1).ToList(), dayCols.ToDictionary(x => x.Col, x => x.Day), origin);
        }

        // Grid by date: a row with at least 3 dates across ("18/11", "Thứ 3 19/11"…)
        for (int r = 0; r < Math.Min(rows.Count, 6); r++)
        {
            var dateCols = rows[r].Select((c, i) => (Date: c.Trim().Length <= 20 ? DateIn(c) : null, Col: i)).Where(x => x.Date != null).ToList();
            if (dateCols.Count >= 3)
            {
                var dates = dateCols.ToDictionary(x => x.Col, x => x.Date!.Value);
                return FromGrid(rows.Skip(r + 1).ToList(), dates.ToDictionary(kv => kv.Key, kv => Iso(kv.Value.DayOfWeek)), origin, dates);
            }
        }

        // Find the header row
        int header = -1;
        int cDay = -1, cStart = -1, cEnd = -1, cSubject = -1, cRoom = -1, cTime = -1, cDate = -1, cDone = -1;
        for (int r = 0; r < Math.Min(rows.Count, 8) && header < 0; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                var h = rows[r][c].Trim().ToLowerInvariant();
                if (h.Length == 0) continue;
                if (cDate < 0 && Regex.IsMatch(h, @"^(ngày|ngay|date|hạn|han|deadline|due)")) cDate = c;
                else if (cDay < 0 && Regex.IsMatch(h, @"^(thứ|thu|day|weekday|ngày trong tuần)")) cDay = c;
                else if (cStart < 0 && Regex.IsMatch(h, @"(bắt đầu|bat dau|start|^từ|^tu$|giờ vào|from)")) cStart = c;
                else if (cEnd < 0 && Regex.IsMatch(h, @"(kết thúc|ket thuc|end|^đến|^den$|giờ ra|^to$)")) cEnd = c;
                else if (cSubject < 0 && Regex.IsMatch(h, @"(môn|mon hoc|subject|course|học phần|lớp|class|công việc|cong viec|^việc|nội dung|task|^tên|title|sự kiện|event|bài tập|assignment)")) cSubject = c;
                else if (cDone < 0 && Regex.IsMatch(h, @"(tiến độ|tien do|trạng thái|trang thai|status|check|xong|done)")) cDone = c;
                else if (cRoom < 0 && Regex.IsMatch(h, @"(phòng|phong|room|địa điểm|location)")) cRoom = c;
                else if (cTime < 0 && Regex.IsMatch(h, @"^(giờ|gio|time|thời gian|ca|slot|tiết)")) cTime = c;
            }
            // A timetable (day + time), or a list of things by date (the time may be missing)
            if (cSubject >= 0 && ((cDay >= 0 || cDate >= 0) && (cStart >= 0 || cTime >= 0) || cDate >= 0)) header = r;
            else cDay = cStart = cEnd = cSubject = cRoom = cTime = cDate = cDone = -1;
        }
        if (header < 0)
        {
            // No header: rows that hold a date and a time ("18/11 | 7h30 | Thi Toán | P.201")
            var dated = FromDatedRows(rows, origin);
            return dated.Count > 0 ? dated : FromText(string.Join("\n", rows.Select(r => string.Join("\t", r))), origin);
        }

        foreach (var row in rows.Skip(header + 1))
        {
            string Cell(int c) => c >= 0 && c < row.Length ? row[c].Trim() : "";
            var subject = Cell(cSubject);
            if (subject.Length == 0) continue;
            if (cDone >= 0 && IsDone(Cell(cDone))) continue;   // ticked off / "Hoàn thành"

            TimeSpan? start = Time(Cell(cStart)), end = Time(Cell(cEnd));
            if (Range(Cell(cTime)) is { } rg) (start, end) = (rg.Item1, rg.Item2);
            else if (Range(Cell(cStart)) is { } rg2) (start, end) = (rg2.Item1, rg2.Item2);
            else if (start == null && Regex.Match(Cell(cTime), @"\d+") is { Success: true } sm && int.TryParse(sm.Value, out int slot) && slot is >= 1 and < 7)
                (start, end) = (FptSlots[slot].Start, FptSlots[slot].End);
            DateTime? date = cDate >= 0 ? DateIn(Cell(cDate)) : null;
            bool allDay = start == null;
            if (allDay && date == null) continue;   // neither a time nor a date
            start ??= new TimeSpan(7, 0, 0);
            end ??= start.Value + TimeSpan.FromMinutes(allDay ? 60 : 90);

            int weekday = date is { } dd ? Iso(dd.DayOfWeek) : Weekday(Cell(cDay));
            if (weekday == 0) continue;

            result.Add(new ClassSlot
            {
                Weekday = weekday, Date = date, Start = start.Value, End = end.Value,
                Subject = subject, Room = NullIfEmpty(Cell(cRoom)), Origin = origin, AllDay = allDay
            });
        }
        return result;
    }

    private static List<ClassSlot> FromGrid(List<string[]> rows, Dictionary<int, int> dayOfColumn, string origin, Dictionary<int, DateTime>? dateOfColumn = null)
    {
        var result = new List<ClassSlot>();
        int firstDay = dayOfColumn.Keys.Min();
        // The cells left of the days say when: "Slot 2", "Ca 2", "7h-7h45", or just "6h45"
        string Side(string[] row) => string.Join(" ", row.Take(Math.Min(firstDay, row.Length)));
        var starts = rows.Select(r => Range(Side(r))?.Item1 ?? Time(LastTime(Side(r)))).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            string first = Side(row);
            (TimeSpan, TimeSpan)? rowTime = Range(first);
            if (rowTime == null && Regex.Match(first, @"(?:slot|ca|tiết|tiet)\s*(\d)", RegexOptions.IgnoreCase) is { Success: true } sm
                && int.Parse(sm.Groups[1].Value) is >= 1 and < 7 and var slot)
                rowTime = (FptSlots[slot].Start, FptSlots[slot].End);
            if (rowTime == null && starts[r] is { } single)
            {
                // A start only: until the next row that has a time (or 45 minutes)
                var next = starts.Skip(r + 1).FirstOrDefault(t => t != null && t > single);
                rowTime = (single, next ?? single + TimeSpan.FromMinutes(45));
            }

            foreach (var (col, day) in dayOfColumn)
            {
                if (col >= row.Length) continue;
                foreach (var found in FromCell(row[col], rowTime))
                {
                    found.Weekday = day;
                    if (dateOfColumn != null && dateOfColumn.TryGetValue(col, out var on)) found.Date = on;
                    found.Origin = origin;
                    result.Add(found);
                }
            }
        }
        return Merge(Dedupe(result));
    }

    /// <summary>
    /// The date in a cell: "18/11", "18/11/2026", "Thứ 3, 18-11", "2026-11-18". Without a
    /// year, the one that puts it nearest today (so January is next year when it's December).
    /// </summary>
    internal static DateTime? DateIn(string cell)
    {
        var iso = Regex.Match(cell, @"(?<!\d)(\d{4})-(\d{1,2})-(\d{1,2})(?!\d)");
        int d, mo, y;
        if (iso.Success) { y = int.Parse(iso.Groups[1].Value); mo = int.Parse(iso.Groups[2].Value); d = int.Parse(iso.Groups[3].Value); }
        else
        {
            var m = Regex.Match(cell, @"(?<![\d:h])(\d{1,2})[/\-.](\d{1,2})(?:[/\-.](\d{2,4}))?(?![\d:])");
            if (!m.Success) return null;
            d = int.Parse(m.Groups[1].Value); mo = int.Parse(m.Groups[2].Value);
            if (m.Groups[3].Success) { y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000; }
            else
            {
                var today = DateTime.Today;
                y = today.Year;
                if (mo is >= 1 and <= 12)
                {
                    if (new DateTime(y, mo, 1) < today.AddMonths(-6)) y++;
                    else if (new DateTime(y, mo, 1) > today.AddMonths(7)) y--;
                }
            }
        }
        if (mo is < 1 or > 12 || y is < 2000 or > 2100 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return null;
        return new DateTime(y, mo, d);
    }

    /// <summary>Rows without a header that each hold a date, a time and a name (an exam list, a plan).</summary>
    private static List<ClassSlot> FromDatedRows(IReadOnlyList<string[]> rows, string origin)
    {
        var result = new List<ClassSlot>();
        foreach (var row in rows)
        {
            var cells = row.Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (cells.Count == 0) continue;
            var dateCell = cells.FirstOrDefault(c => DateIn(c) != null);
            if (dateCell == null) continue;
            var date = DateIn(dateCell)!.Value;
            var line = string.Join("  ", cells.Where(c => c != dateCell));
            // A date with a time in the same cell ("18/11 7h30") leaves the time
            var rest = Regex.Replace(dateCell, @"(?<![\d:h])\d{1,2}[/\-.]\d{1,2}(?:[/\-.]\d{2,4})?(?![\d:])", "");
            rest = Regex.Replace(rest, @"^\s*(thứ\s*\S+|chủ nhật|cn)[,\s]*", "", RegexOptions.IgnoreCase).Trim(' ', ',', '-');
            var slot = FromLine((rest.Length > 0 ? rest + " " : "") + line);
            if (slot == null)
            {
                // No time written: a whole-day item at 7:00 (exams usually say their time, plans may not)
                var name = Regex.Replace(line, @"\s+", " ").Trim();
                if (name.Length == 0 || NotAClass.IsMatch(name)) continue;
                slot = new ClassSlot { Subject = name, Start = new TimeSpan(7, 0, 0), End = new TimeSpan(8, 0, 0), AllDay = true };
            }
            slot.Date = date;
            slot.Weekday = Iso(date.DayOfWeek);
            slot.Origin = origin;
            result.Add(slot);
        }
        // Only a real list: a couple of lines, not one stray date in a timetable
        return result.Count >= 2 || (result.Count == 1 && rows.Count <= 3) ? Dedupe(result) : new();
    }

    private static readonly Regex ExamRx = new(@"(?<!\p{L})(thi|kiểm tra|ktra|exam|final|midterm|giữa kỳ|cuối kỳ|bảo vệ|pe|fe)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>An exam by its name: "Thi Toán", "PE PRO192", "Kiểm tra giữa kỳ".</summary>
    public static bool LooksLikeExam(string subject) => ExamRx.IsMatch(subject);

    /// <summary>"Hoàn thành", "Xong", "Done", a ticked box (TRUE), "✓" (but not "Chưa hoàn thành").</summary>
    internal static bool IsDone(string cell)
    {
        var t = cell.Trim().ToLowerInvariant();
        if (t.Length == 0 || Regex.IsMatch(t, @"chưa|chua|not|đang|dang|in progress|todo")) return false;
        return Regex.IsMatch(t, @"^(✓|✔|☑|x|true|yes|có|done|xong|đã xong|hoàn thành|hoan thanh|completed|finished|100\s*%)$");
    }

    /// <summary>The sheet is a list of deadlines ("DEADLINE", "Hạn chót", "Hạn nộp").</summary>
    public static bool IsDeadlineSheet(IReadOnlyList<string[]> rows) =>
        rows.Take(3).SelectMany(r => r).Any(c => Regex.IsMatch(c, @"dea?l?d?line|dealdine|hạn chót|han chot|hạn nộp|due date", RegexOptions.IgnoreCase));

    /// <summary>The sheet says it's an exam schedule ("LỊCH THI HỌC KỲ I").</summary>
    public static bool IsExamSheet(IReadOnlyList<string[]> rows) =>
        rows.Take(3).SelectMany(r => r).Any(c => Regex.IsMatch(c, @"lịch\s*thi|exam\s*schedule|kiểm tra", RegexOptions.IgnoreCase));

    /// <summary>"THỜI KHÓA BIỂU NĂM HỌC 2026-2027", or the first words in the sheet.</summary>
    public static string TitleOf(IReadOnlyList<string[]> rows)
    {
        var first = rows.SelectMany(r => r).Select(c => c.Trim()).FirstOrDefault(c => c.Length > 0) ?? "Google Sheet";
        return first.Length > 48 ? first[..47] + "…" : first;
    }

    /// <summary>"SÁNG 6h45" → "6h45": the last thing in the side cells that looks like a time.</summary>
    private static string LastTime(string side) =>
        Regex.Matches(side, @"\d{1,2}\s*(?:[:.]\s*\d{2}|[hg]\s*(?:\d{2})?)", RegexOptions.IgnoreCase).LastOrDefault()?.Value ?? "";

    /// <summary>Two periods of the same subject back to back (a short break between) become one.</summary>
    private static List<ClassSlot> Merge(List<ClassSlot> list)
    {
        var merged = new List<ClassSlot>();
        foreach (var s in list.OrderBy(x => x.Date ?? DateTime.MinValue).ThenBy(x => x.Weekday).ThenBy(x => x.Start))
        {
            var last = merged.LastOrDefault();
            if (last != null && last.Weekday == s.Weekday && last.Date == s.Date &&
                string.Equals(last.Subject, s.Subject, StringComparison.OrdinalIgnoreCase) &&
                s.Start >= last.Start && s.Start - last.End <= TimeSpan.FromMinutes(20))
            {
                if (s.End > last.End) last.End = s.End;
                last.Room ??= s.Room;
                continue;
            }
            merged.Add(s);
        }
        return merged;
    }

    /// <summary>Things written in a timetable that aren't classes.</summary>
    private static readonly Regex NotAClass = new(@"^(giải lao|giai lao|nghỉ|nghi|nghỉ trưa|ra chơi|ra choi|dậy|day|vào lớp|vao lop|ăn trưa|ăn sáng|ăn tối|break|lunch|x|-+|–)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>One cell of a timetable: "PRF192- at DE-C201 (Not yet) (7:30-9:50)".</summary>
    private static IEnumerable<ClassSlot> FromCell(string cell, (TimeSpan, TimeSpan)? rowTime)
    {
        cell = cell.Trim();
        if (cell.Length < 2 || cell == "-") yield break;   // "Lý" is a subject
        var codes = CodeRx.Matches(cell);
        if (codes.Count == 0)
        {
            // A table someone typed: the subject's own name; a time in the cell ("17h30-19h: Toán")
            // wins over the row's
            var own = RangeAt(cell, out var at);
            string name = own != null ? cell.Remove(at.Index, at.Length) : cell;
            name = Regex.Replace(name.Split('\n')[0], @"^[\s:\-–,.]+|[\s:\-–,]+$", "").Trim();
            var when = own ?? rowTime;
            if (when is { } t && name.Length > 0 && !NotAClass.IsMatch(name))
                yield return new ClassSlot { Subject = name, Start = t.Item1, End = t.Item2, Room = Room(cell) };
            yield break;
        }
        for (int i = 0; i < codes.Count; i++)
        {
            int from = codes[i].Index, to = i + 1 < codes.Count ? codes[i + 1].Index : cell.Length;
            var piece = cell[from..to];
            var range = Range(piece) ?? rowTime;
            if (range == null) continue;
            yield return new ClassSlot { Subject = codes[i].Groups[1].Value, Start = range.Value.Item1, End = range.Value.Item2, Room = Room(piece) };
        }
    }

    private static string? Room(string text)
    {
        var m = RoomRx.Match(text);
        if (m.Success) return m.Groups[1].Value.TrimEnd('.', '-');
        var fpt = Regex.Match(text, @"(?<![A-Za-z0-9])([A-Z]{1,2}-?[A-Z]?\d{3})(?![A-Za-z0-9])");   // "DE-C201", "BE305"
        return fpt.Success && !CodeRx.IsMatch(fpt.Value) ? fpt.Groups[1].Value : null;
    }

    // ── FAP's weekly timetable (copied with Ctrl + A, Ctrl + C) ───────
    /// <summary>
    /// The text copied from FAP's "Weekly timetable" page, or any text with
    /// weekday columns (tab separated). Rows start with "Slot N"; a cell can
    /// run over several lines. The week shown is kept as a weekly timetable.
    /// </summary>
    public static List<ClassSlot> FromText(string text, string origin)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');

        // The header with the days (MON TUE … or Thứ 2 … CN), split on tabs
        int headerLine = -1;
        Dictionary<int, int>? dayOfColumn = null;
        for (int i = 0; i < lines.Length && headerLine < 0; i++)
        {
            var cells = lines[i].Split('\t');
            var days = cells.Select((c, k) => (Day: Weekday(c), Col: k)).Where(x => x.Day > 0).ToList();
            if (days.Count >= 5) { headerLine = i; dayOfColumn = days.ToDictionary(x => x.Col, x => x.Day); }
        }

        if (dayOfColumn != null)
        {
            // Some pages put "YEAR 2026 | MON TUE …" in one row, then the dates; day columns
            // count from the left, so make the first day column line up with the slot rows' cells.
            var rows = new List<string[]>();
            var current = new StringBuilder();
            void Flush()
            {
                if (current.Length > 0) rows.Add(current.ToString().Split('\t'));
                current.Clear();
            }
            for (int i = headerLine + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (Regex.IsMatch(line, @"^\s*(slot|ca|tiết|tiet)\s*\d", RegexOptions.IgnoreCase)) Flush();
                if (current.Length > 0) current.Append('\n');
                current.Append(line);
            }
            Flush();

            // In the slot rows the first cell is "Slot N", then one cell per day.
            int firstDayCol = dayOfColumn.Keys.Min();
            int offset = 1 - firstDayCol;   // header "YEAR\tMON…" → MON at 1; header "MON…" → MON at 0
            var shifted = dayOfColumn.ToDictionary(kv => kv.Key + offset, kv => kv.Value);
            var fromGrid = FromGrid(rows.Where(r => r.Length > 1 && Regex.IsMatch(r[0], @"(slot|ca|tiết|tiet)\s*\d", RegexOptions.IgnoreCase)).ToList(), shifted, origin);
            if (fromGrid.Count > 0) return fromGrid;
        }

        // No columns (e.g. pasted without tabs): read subject + time pairs in order,
        // taking the day from the nearest "Thứ …"/"MON" word or a dd/MM date before it.
        var result = new List<ClassSlot>();
        int day = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var firstWord = Regex.Match(line, @"^(thứ\s*\S+|chủ nhật|cn|mon|tue|wed|thu|fri|sat|sun)\w*", RegexOptions.IgnoreCase);
            if (firstWord.Success && Weekday(firstWord.Value) is > 0 and var d) day = d;
            else if (DayMonthRx.Match(line) is { Success: true } dm && line.Length <= 12)
            {
                int dd = int.Parse(dm.Groups[1].Value), mo = int.Parse(dm.Groups[2].Value);
                if (mo is >= 1 and <= 12 && dd is >= 1 and <= 31)
                    try { day = Iso(new DateTime(DateTime.Today.Year, mo, Math.Min(dd, DateTime.DaysInMonth(DateTime.Today.Year, mo))).DayOfWeek); } catch { }
            }
            if (day == 0) continue;
            foreach (var found in FromCell(line, null)) { found.Weekday = day; found.Origin = origin; result.Add(found); }
        }
        return Dedupe(result);
    }

    /// <summary>
    /// One line typed by hand: "Họp nhóm SWP 15h-16h30 phòng 304", "Gym 18h" (an hour long),
    /// "15:00 - 16:30 Họp nhóm". The day comes from where it was typed.
    /// </summary>
    public static ClassSlot? FromLine(string text)
    {
        text = Regex.Replace(text, @"https?://\S+|www\.\S+", " ").Trim();   // digits inside a link aren't a time
        if (text.Length == 0) return null;
        (TimeSpan, TimeSpan)? when = RangeAt(text, out var at);
        string rest;
        if (when != null) rest = text.Remove(at.Index, at.Length);
        else
        {
            var one = Regex.Match(text, @"(?<!\d)(\d{1,2})\s*(?:[:.]\s*(\d{2})|[hg]\s*(\d{2})?)(?!\d)", RegexOptions.IgnoreCase);
            if (!one.Success) return null;
            int h = int.Parse(one.Groups[1].Value);
            int m = one.Groups[2].Success ? int.Parse(one.Groups[2].Value) : one.Groups[3].Success ? int.Parse(one.Groups[3].Value) : 0;
            if (h > 23 || m > 59) return null;
            var start = new TimeSpan(h, m, 0);
            when = (start, start + TimeSpan.FromHours(1));
            rest = text.Remove(one.Index, one.Length);
        }
        string? room = Room(rest);
        if (room != null) rest = Regex.Replace(rest, @"(?:\bat\s+|\bph(?:ò|o)ng\s*|\broom\s*|\bP\.\s?)" + Regex.Escape(room), "", RegexOptions.IgnoreCase);
        string subject = Regex.Replace(rest, @"\s+", " ").Trim(' ', ':', '-', '–', ',', '.', '·');
        if (subject.Length == 0) return null;
        return new ClassSlot { Subject = subject, Start = when.Value.Item1, End = when.Value.Item2, Room = room };
    }

    // ── Words read from a photo (with where each word was) ────────────
    public sealed record Word(string Text, double X, double Y, double Width, double Height);

    /// <summary>
    /// A photo of a timetable: the weekday words across the top give the columns;
    /// every other word joins the column it sits under, read top to bottom.
    /// </summary>
    public static List<ClassSlot> FromWords(IReadOnlyList<Word> words, string origin)
    {
        // Day headers: one word ("MON", "T2", "CN") or two ("Thứ 2")
        var heads = new List<(int Day, double X, double Y)>();
        for (int i = 0; i < words.Count; i++)
        {
            int d = Weekday(words[i].Text);
            if (d == 0 && i + 1 < words.Count && Math.Abs(words[i + 1].Y - words[i].Y) < words[i].Height)
                d = Weekday(words[i].Text + " " + words[i + 1].Text);
            if (d > 0 && heads.All(h => h.Day != d)) heads.Add((d, words[i].X + words[i].Width / 2, words[i].Y));
        }
        if (heads.Count < 3)
            return FromText(string.Join("\n", Lines(words)), origin);

        double headerY = heads.Max(h => h.Y);
        var columns = heads.OrderBy(h => h.X).ToList();
        var byColumn = columns.ToDictionary(c => c.Day, _ => new List<Word>());
        var left = new List<Word>();   // the slot / time column at the far left
        foreach (var w in words.Where(w => w.Y > headerY + 2))
        {
            double cx = w.X + w.Width / 2;
            var nearest = columns.OrderBy(c => Math.Abs(c.X - cx)).First();
            double gap = columns.Count > 1 ? (columns[^1].X - columns[0].X) / (columns.Count - 1) : 100;
            if (cx < columns[0].X - gap * 0.6) left.Add(w);
            else byColumn[nearest.Day].Add(w);
        }

        var result = new List<ClassSlot>();
        foreach (var (day, list) in byColumn)
        {
            var text = string.Join("\n", Lines(list));
            foreach (var found in FromCell(text, null)) { found.Weekday = day; found.Origin = origin; result.Add(found); }
        }
        return Dedupe(result);
    }

    /// <summary>Words joined into lines, top to bottom, left to right.</summary>
    private static IEnumerable<string> Lines(IEnumerable<Word> words)
    {
        var sorted = words.OrderBy(w => w.Y).ToList();
        var line = new List<Word>();
        foreach (var w in sorted)
        {
            if (line.Count > 0 && Math.Abs(w.Y - line[0].Y) > line[0].Height * 0.6)
            {
                yield return string.Join(" ", line.OrderBy(x => x.X).Select(x => x.Text));
                line.Clear();
            }
            line.Add(w);
        }
        if (line.Count > 0) yield return string.Join(" ", line.OrderBy(x => x.X).Select(x => x.Text));
    }

    // ── Calendar file (.ics, e.g. exported from Google Calendar) ──────
    public static List<ClassSlot> FromIcs(string text, string origin)
    {
        var result = new List<ClassSlot>();
        // Unfold continued lines
        text = Regex.Replace(text.Replace("\r\n", "\n"), @"\n[ \t]", "");
        foreach (Match ev in Regex.Matches(text, @"BEGIN:VEVENT(.*?)END:VEVENT", RegexOptions.Singleline))
        {
            string body = ev.Groups[1].Value;
            string? Field(string name) => Regex.Match(body, $@"^{name}(?:;[^:\n]*)?:(.*)$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
            var start = IcsTime(Field("DTSTART"));
            var end = IcsTime(Field("DTEND"));
            var summary = Field("SUMMARY")?.Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ");
            if (start == null || string.IsNullOrWhiteSpace(summary)) continue;
            end ??= start.Value.AddMinutes(90);
            if (end.Value.Date != start.Value.Date || end <= start) continue;   // all-day or multi-day events aren't classes
            bool weekly = Field("RRULE")?.Contains("FREQ=WEEKLY", StringComparison.OrdinalIgnoreCase) == true;
            result.Add(new ClassSlot
            {
                Weekday = Iso(start.Value.DayOfWeek), Date = weekly ? null : start.Value.Date,
                Start = start.Value.TimeOfDay, End = end.Value.TimeOfDay, Subject = summary!,
                Room = NullIfEmpty(Field("LOCATION")?.Replace("\\,", ",")), Origin = origin
            });
        }
        // Keep the coming weeks only: a whole year of one-off events is too much
        var from = DateTime.Today.AddDays(-1);
        return Dedupe(result.Where(s => s.Date == null || (s.Date >= from && s.Date <= from.AddDays(120))).ToList());
    }

    private static DateTime? IcsTime(string? value)
    {
        if (value == null) return null;
        bool utc = value.EndsWith('Z');
        var v = value.TrimEnd('Z');
        if (!DateTime.TryParseExact(v, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;   // a date without a time: all-day
        return utc ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime() : t;
    }

    // ── .csv and .xlsx into rows ──────────────────────────────────────
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        // Excel in Vietnamese often saves with ';'
        char sep = text.Split('\n').FirstOrDefault() is { } first && first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : ',';
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep || ch == '\t') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n') { row.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(row.ToArray()); row.Clear(); }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().TrimEnd('\r')); rows.Add(row.ToArray()); }
        return rows;
    }

    /// <summary>The first sheet of an .xlsx file as rows of text (dates and times as Excel shows them).</summary>
    public static List<string[]> ParseXlsx(Stream file)
    {
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            using var s = ss.Open();
            shared = XDocument.Load(s).Root!.Elements(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToList();
        }
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet"));
        if (sheetEntry == null) return new();
        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);
        var rows = new List<string[]>();
        foreach (var r in sheet.Descendants(ns + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var c in r.Elements(ns + "c"))
            {
                var reference = (string?)c.Attribute("r") ?? "";
                int col = 0;
                foreach (var ch in reference.TakeWhile(char.IsLetter)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                var type = (string?)c.Attribute("t");
                var v = c.Element(ns + "v")?.Value ?? string.Concat(c.Descendants(ns + "t").Select(t => t.Value));
                string text = type == "s" && int.TryParse(v, out int si2) && si2 < shared.Count ? shared[si2]
                            : type is null or "n" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double num) ? ExcelNumber(num)
                            : v;
                cells[Math.Max(0, col - 1)] = text;
            }
            if (cells.Count == 0) continue;
            var arr = new string[cells.Keys.Max() + 1];
            for (int i = 0; i < arr.Length; i++) arr[i] = cells.TryGetValue(i, out var t) ? t : "";
            rows.Add(arr);
        }
        return rows;
    }

    /// <summary>Excel keeps times as parts of a day (0.3125 = 7:30) and dates as days since 1900.</summary>
    private static string ExcelNumber(double n)
    {
        if (n > 0 && n < 1) { var t = TimeSpan.FromDays(n); return $"{(int)t.TotalHours}:{t.Minutes:00}"; }
        if (n > 30000 && n < 80000 && Math.Abs(n - Math.Round(n)) < 1e-9) return DateTime.FromOADate(n).ToString("dd/MM/yyyy");
        if (n > 30000 && n < 80000) { var d = DateTime.FromOADate(n); return d.ToString("dd/MM/yyyy HH:mm"); }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>docs.google.com/spreadsheets/d/ID/edit#gid=N → the link that downloads it as .csv.</summary>
    public static string? SheetCsvUrl(string link)
    {
        var m = Regex.Match(link.Trim(), @"docs\.google\.com/spreadsheets/d/([A-Za-z0-9_\-]+)");
        if (!m.Success) return null;
        var gid = Regex.Match(link, @"[#&?]gid=(\d+)");
        return $"https://docs.google.com/spreadsheets/d/{m.Groups[1].Value}/export?format=csv" + (gid.Success ? $"&gid={gid.Groups[1].Value}" : "");
    }

    // ── Helpers ───────────────────────────────────────────────────────
    internal static int Iso(DayOfWeek d) => d == DayOfWeek.Sunday ? 7 : (int)d;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<ClassSlot> Dedupe(List<ClassSlot> list) =>
        list.GroupBy(s => (s.Weekday, s.Date, s.Start, s.Subject.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(s => s.Date ?? DateTime.MinValue).ThenBy(s => s.Weekday).ThenBy(s => s.Start)
            .ToList();
}
