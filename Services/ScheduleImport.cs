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
    private static readonly Regex RangeRx = new(@"(\d{1,2})\s*[:hg.]\s*(\d{2})\s*(?:-|–|—|~|đến|to)\s*(\d{1,2})\s*[:hg.]\s*(\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OneTimeRx = new(@"^\s*(\d{1,2})\s*(?:[:hg.]\s*(\d{2}))?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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

    private static (TimeSpan, TimeSpan)? Range(string text)
    {
        var m = RangeRx.Match(text);
        if (!m.Success) return null;
        int h1 = int.Parse(m.Groups[1].Value), m1 = int.Parse(m.Groups[2].Value);
        int h2 = int.Parse(m.Groups[3].Value), m2 = int.Parse(m.Groups[4].Value);
        if (h1 > 23 || h2 > 23 || m1 > 59 || m2 > 59) return null;
        var a = new TimeSpan(h1, m1, 0);
        var b = new TimeSpan(h2, m2, 0);
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

        // Find the header row
        int header = -1;
        int cDay = -1, cStart = -1, cEnd = -1, cSubject = -1, cRoom = -1, cTime = -1, cDate = -1;
        for (int r = 0; r < Math.Min(rows.Count, 8) && header < 0; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                var h = rows[r][c].Trim().ToLowerInvariant();
                if (h.Length == 0) continue;
                if (cDay < 0 && Regex.IsMatch(h, @"^(thứ|thu|day|weekday|ngày trong tuần)")) cDay = c;
                else if (cStart < 0 && Regex.IsMatch(h, @"(bắt đầu|bat dau|start|^từ|^tu$|giờ vào|from)")) cStart = c;
                else if (cEnd < 0 && Regex.IsMatch(h, @"(kết thúc|ket thuc|end|^đến|^den$|giờ ra|^to$)")) cEnd = c;
                else if (cSubject < 0 && Regex.IsMatch(h, @"(môn|mon hoc|subject|course|học phần|lớp|class)")) cSubject = c;
                else if (cRoom < 0 && Regex.IsMatch(h, @"(phòng|phong|room|địa điểm|location)")) cRoom = c;
                else if (cTime < 0 && Regex.IsMatch(h, @"^(giờ|gio|time|thời gian|ca|slot|tiết)")) cTime = c;
                else if (cDate < 0 && Regex.IsMatch(h, @"^(ngày|ngay|date)")) cDate = c;
            }
            if (cSubject >= 0 && (cDay >= 0 || cDate >= 0) && (cStart >= 0 || cTime >= 0)) header = r;
            else cDay = cStart = cEnd = cSubject = cRoom = cTime = cDate = -1;
        }
        if (header < 0) return FromText(string.Join("\n", rows.Select(r => string.Join("\t", r))), origin);

        foreach (var row in rows.Skip(header + 1))
        {
            string Cell(int c) => c >= 0 && c < row.Length ? row[c].Trim() : "";
            var subject = Cell(cSubject);
            if (subject.Length == 0) continue;

            TimeSpan? start = Time(Cell(cStart)), end = Time(Cell(cEnd));
            if (Range(Cell(cTime)) is { } rg) (start, end) = (rg.Item1, rg.Item2);
            else if (Range(Cell(cStart)) is { } rg2) (start, end) = (rg2.Item1, rg2.Item2);
            else if (start == null && Regex.Match(Cell(cTime), @"\d+") is { Success: true } sm && int.TryParse(sm.Value, out int slot) && slot is >= 1 and < 7)
                (start, end) = (FptSlots[slot].Start, FptSlots[slot].End);
            if (start == null) continue;
            end ??= start.Value + TimeSpan.FromMinutes(90);

            DateTime? date = null;
            if (cDate >= 0 && DayMonthRx.Match(Cell(cDate)) is { Success: true } dm)
            {
                int d = int.Parse(dm.Groups[1].Value), mo = int.Parse(dm.Groups[2].Value);
                int y = dm.Groups[3].Success ? int.Parse(dm.Groups[3].Value) : DateTime.Today.Year;
                if (y < 100) y += 2000;
                if (mo is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, mo)) date = new DateTime(y, mo, d);
            }
            int weekday = date is { } dd ? Iso(dd.DayOfWeek) : Weekday(Cell(cDay));
            if (weekday == 0) continue;

            result.Add(new ClassSlot
            {
                Weekday = weekday, Date = date, Start = start.Value, End = end.Value,
                Subject = subject, Room = NullIfEmpty(Cell(cRoom)), Origin = origin
            });
        }
        return result;
    }

    private static List<ClassSlot> FromGrid(List<string[]> rows, Dictionary<int, int> dayOfColumn, string origin)
    {
        var result = new List<ClassSlot>();
        foreach (var row in rows)
        {
            // The first cell may say the slot: "Slot 2", "Ca 2", "Tiết 3", or a time range
            string first = row.Length > 0 ? row[0] : "";
            (TimeSpan, TimeSpan)? rowTime = Range(first);
            if (rowTime == null && Regex.Match(first, @"(?:slot|ca|tiết|tiet)\s*(\d)", RegexOptions.IgnoreCase) is { Success: true } sm
                && int.Parse(sm.Groups[1].Value) is >= 1 and < 7 and var slot)
                rowTime = (FptSlots[slot].Start, FptSlots[slot].End);

            foreach (var (col, day) in dayOfColumn)
            {
                if (col >= row.Length) continue;
                foreach (var found in FromCell(row[col], rowTime))
                {
                    found.Weekday = day;
                    found.Origin = origin;
                    result.Add(found);
                }
            }
        }
        return Dedupe(result);
    }

    /// <summary>One cell of a timetable: "PRF192- at DE-C201 (Not yet) (7:30-9:50)".</summary>
    private static IEnumerable<ClassSlot> FromCell(string cell, (TimeSpan, TimeSpan)? rowTime)
    {
        cell = cell.Trim();
        if (cell.Length < 3 || cell == "-") yield break;
        var codes = CodeRx.Matches(cell);
        if (codes.Count == 0)
        {
            // A table someone typed: the subject's own name, time from the row
            if (rowTime is { } t && !Regex.IsMatch(cell, @"^[\s\-–]*$"))
                yield return new ClassSlot { Subject = cell.Split('\n')[0].Trim(), Start = t.Item1, End = t.Item2, Room = Room(cell) };
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
