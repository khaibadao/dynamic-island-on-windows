namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;

/// <summary>
/// Lịch học: the week's classes, a reminder 15 minutes before each one, the
/// class going on now (for the pill), and in the evening a line about tomorrow.
/// The timetable comes from a Google Sheet link (re-read every 6 hours), a
/// file, or a photo of the timetable.
/// </summary>
public sealed class ScheduleService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HashSet<string> _reminded = new();

    /// <summary>A class starts in about 15 minutes. UI thread.</summary>
    public event Action<ClassSlot, DateTime>? Upcoming;
    /// <summary>20:00: tomorrow's classes (only if there are any). UI thread.</summary>
    public event Action<IReadOnlyList<ClassSlot>>? Tomorrow;
    public event Action? Changed;

    /// <summary>Where exams from a sheet go (Hạn chót, counted down by days). Set by the app.</summary>
    public DeadlineService? Deadlines { get; set; }

    public ScheduleService()
    {
        // The one sheet of earlier versions becomes the first linked sheet
        if (S.ScheduleSheetUrl is { } oldLink && S.ScheduleSources.Count == 0)
        {
            S.ScheduleSources.Add(new ScheduleSource { Key = "sheet", Url = oldLink, Name = "Google Sheet", SyncedUtc = S.ScheduleSyncedUtc });
            S.ScheduleSheetUrl = null;
            S.Save();
        }
        // One-day sessions typed in by hand go away a week after their day
        if (S.Schedule.RemoveAll(c => c.Origin == "typed" && c.Date is { } d && d < DateTime.Today.AddDays(-7)) > 0) S.Save();
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private static AppSettings S => AppSettings.Current;

    public bool HasAny => S.Schedule.Count > 0;

    /// <summary>The classes on a day, by time.</summary>
    public static IReadOnlyList<ClassSlot> On(DateTime day)
    {
        int wd = ScheduleImport.Iso(day.DayOfWeek);
        return S.Schedule.Where(c => c.Date is { } d ? d.Date == day.Date : c.Weekday == wd)
                         .OrderBy(c => c.Start).ToList();
    }

    /// <summary>The class going on right now, if any.</summary>
    public static ClassSlot? Now(DateTime now) =>
        On(now).FirstOrDefault(c => now.TimeOfDay >= c.Start && now.TimeOfDay < c.End);

    /// <summary>Puts in a new timetable from one source (the old one from that source goes).</summary>
    public int Replace(IReadOnlyList<ClassSlot> slots, string origin)
    {
        // A source's new timetable replaces only what that source gave before
        S.Schedule.RemoveAll(c => c.Origin == origin);
        S.Schedule.AddRange(slots);
        S.Save();
        _reminded.Clear();
        Changed?.Invoke();
        return slots.Count;
    }

    // ── By hand ───────────────────────────────────────────────────────
    /// <summary>A session typed in on the notch (kept when the sheet is read again).</summary>
    public void AddTyped(ClassSlot slot)
    {
        slot.Origin = "typed";
        S.Schedule.Add(slot);
        S.Save();
        Changed?.Invoke();
    }

    public void UpdateTyped(Guid id, ClassSlot with)
    {
        var c = S.Schedule.FirstOrDefault(x => x.Id == id);
        if (c == null) return;
        c.Subject = with.Subject; c.Start = with.Start; c.End = with.End; c.Room = with.Room;
        c.Date = with.Date; c.Weekday = with.Weekday;
        S.Save();
        Changed?.Invoke();
    }

    /// <summary>Every session with this name gets this kind (kept when the sheet is read again).</summary>
    public void SetKind(string subject, string kind)
    {
        S.ScheduleKinds[subject.Trim().ToLowerInvariant()] = kind;
        S.Save();
        Changed?.Invoke();
    }

    private static readonly string[] KindPalette = { "#FF6482", "#64D2FF", "#FFD60A", "#AC8E68", "#5E5CE6", "#FF375F", "#66D4CF" };

    /// <summary>A new kind of session; returns its key (an existing one if the name is taken).</summary>
    public static string AddKind(string name)
    {
        name = name.Trim();
        var same = S.CustomKinds.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
        if (same != null) return same.Key;
        var kind = new SessionKind { Key = "k" + Guid.NewGuid().ToString("N")[..8], Name = name, Color = KindPalette[S.CustomKinds.Count % KindPalette.Length] };
        S.CustomKinds.Add(kind);
        S.Save();
        return kind.Key;
    }

    public void Remove(Guid id)
    {
        S.Schedule.RemoveAll(c => c.Id == id);
        S.Save();
        Changed?.Invoke();
    }

    public void Clear()
    {
        S.Schedule.Clear();
        foreach (var src in S.ScheduleSources) Deadlines?.ReplaceFromSource(src.Key, Array.Empty<Deadline>());
        S.ScheduleSources.Clear();
        S.ScheduleSheetUrl = null;
        S.Save();
        Changed?.Invoke();
    }

    // ── Sources ───────────────────────────────────────────────────────
    // ── Linked Google Sheets ──────────────────────────────────────────
    /// <summary>What a sheet holds, before it's saved: so a wrong guess can be fixed.</summary>
    public sealed record SheetPreview(string Url, string Title, IReadOnlyList<ClassSlot> Weekly, IReadOnlyList<ClassSlot> Dated,
                                      IReadOnlyList<ClassSlot> Exams, IReadOnlyList<ClassSlot> Due, string Mode)
    {
        public bool Empty => Weekly.Count + Dated.Count + Exams.Count + Due.Count == 0;

        /// <summary>"70 buổi hằng tuần · 5 lịch thi (18/11 Thi Toán…) · 3 buổi theo ngày".</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (Weekly.Count > 0) parts.Add($"{Weekly.Count} buổi hằng tuần");
                if (Exams.Count > 0)
                    parts.Add($"{Exams.Count} lịch thi (" + string.Join(", ", Exams.OrderBy(e => e.Date).Take(2).Select(e => $"{e.Date:dd/MM} {e.Subject}")) + (Exams.Count > 2 ? "…" : "") + ")");
                if (Due.Count > 0)
                    parts.Add($"{Due.Count} hạn chót (" + string.Join(", ", Due.OrderBy(e => e.Date).Take(2).Select(e => $"{e.Date:dd/MM} {e.Subject}")) + (Due.Count > 2 ? "…" : "") + ")");
                if (Dated.Count > 0) parts.Add($"{Dated.Count} buổi theo ngày");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>
    /// Reads a sheet ("Anyone with the link can view") and sorts what's in it: weekly
    /// sessions, sessions on a given date, and exams (by their name, or the sheet's title
    /// "Lịch thi"). Mode: "auto", "classes" (no exams), "exams" (every dated row is one).
    /// Returns the preview, or a message saying what went wrong.
    /// </summary>
    public static async Task<(SheetPreview? Preview, string? Problem)> PreviewSheetAsync(string link, string mode = "auto")
    {
        var csv = ScheduleImport.SheetCsvUrl(link);
        if (csv == null) return (null, "Link này không phải Google Sheet. Dán link dạng docs.google.com/spreadsheets/…");
        try
        {
            using var resp = await Http.GetAsync(csv);
            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!resp.IsSuccessStatusCode || type.Contains("html"))
                return (null, "Không đọc được sheet. Mở sheet → Chia sẻ → \u201CBất kỳ ai có đường liên kết\u201D → Người xem, rồi thử lại.");
            var rows = ScheduleImport.ParseCsv(await resp.Content.ReadAsStringAsync());
            var all = ScheduleImport.FromTable(rows, "sheet");
            var preview = Sort(link.Trim(), ScheduleImport.TitleOf(rows), all, mode, ScheduleImport.IsExamSheet(rows), ScheduleImport.IsDeadlineSheet(rows));
            return preview.Empty
                ? (null, "Sheet không có buổi nào đọc được. Cần có cột giờ, và cột thứ hoặc ngày (vd: Thứ · Giờ · Môn, hoặc Ngày · Giờ · Môn thi).")
                : (preview, null);
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Schedule sheet failed: {ex.GetType().Name}");
            return (null, "Không tải được sheet, kiểm tra mạng rồi thử lại nhé.");
        }
    }

    /// <summary>
    /// Weekly sessions stay in the timetable. Dated items: exams (by name, or an exam sheet)
    /// and deadlines (no time written, or a deadline sheet) go to Hạn chót; the rest are
    /// sessions on that day. "classes" keeps everything in the timetable.
    /// </summary>
    internal static SheetPreview Sort(string url, string title, IReadOnlyList<ClassSlot> all, string mode, bool examSheet, bool deadlineSheet = false)
    {
        bool Exam(ClassSlot c) => c.Date != null && mode != "classes" && (mode == "exams" || examSheet || ScheduleImport.LooksLikeExam(c.Subject));
        bool Due(ClassSlot c) => c.Date != null && mode != "classes" && !Exam(c) && (c.AllDay || deadlineSheet);
        return new SheetPreview(url, title,
            all.Where(c => c.Date == null).ToList(),
            all.Where(c => c.Date != null && !Exam(c) && !Due(c)).ToList(),
            all.Where(Exam).ToList(),
            all.Where(Due).ToList(), mode);
    }

    /// <summary>Saves a sheet's sessions (exams go to Hạn chót) and keeps the link to re-read it.</summary>
    public string Apply(SheetPreview p)
    {
        var src = S.ScheduleSources.FirstOrDefault(x => x.Url == p.Url);
        if (src == null)
        {
            src = new ScheduleSource { Key = "sheet:" + Guid.NewGuid().ToString("N")[..8], Url = p.Url };
            S.ScheduleSources.Add(src);
        }
        src.Name = p.Title;
        src.Mode = p.Mode;
        src.SyncedUtc = DateTime.UtcNow;
        src.Classes = p.Weekly.Count + p.Dated.Count;
        src.Exams = p.Exams.Count + p.Due.Count;

        var slots = p.Weekly.Concat(p.Dated).ToList();
        foreach (var c in slots) c.Origin = src.Key;
        Replace(slots, src.Key);
        // Exams: at their time (7:00 when none is written); deadlines: end of that day
        var dues = p.Exams.Select(e => new Deadline
        {
            Title = DeadlineService.IsExamTitle(e.Subject) || ScheduleImport.LooksLikeExam(e.Subject) ? e.Subject : "Thi " + e.Subject,
            Due = e.Date!.Value + (e.AllDay ? new TimeSpan(7, 0, 0) : e.Start), Exam = true, NoTime = e.AllDay
        }).Concat(p.Due.Select(d => new Deadline
        {
            Title = d.Subject, Due = d.Date!.Value + (d.AllDay ? new TimeSpan(23, 59, 0) : d.Start), Exam = false, NoTime = d.AllDay
        })).ToList();
        Deadlines?.ReplaceFromSource(src.Key, dues);
        CrashLog.Write($"Schedule: sheet read, {slots.Count} sessions, {dues.Count} deadlines/exams");
        return $"Đã lưu: {p.Summary}" + (dues.Count > 0 ? " · lịch thi và hạn chót nằm ở tab Hạn chót" : "") + " · tự cập nhật mỗi 6 giờ";
    }

    /// <summary>Unlinks a sheet and takes away what it brought.</summary>
    public void RemoveSource(string key)
    {
        S.ScheduleSources.RemoveAll(x => x.Key == key);
        S.Schedule.RemoveAll(c => c.Origin == key);
        S.Save();
        Deadlines?.ReplaceFromSource(key, Array.Empty<Deadline>());
        Changed?.Invoke();
    }

    /// <summary>A .xlsx, .csv or .ics file.</summary>
    public string FromFile(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            List<ClassSlot> slots = ext switch
            {
                ".ics" => ScheduleImport.FromIcs(File.ReadAllText(path), "file"),
                ".csv" or ".txt" or ".tsv" => ScheduleImport.FromTable(ScheduleImport.ParseCsv(File.ReadAllText(path)), "file"),
                ".xlsx" => ReadXlsx(path),
                _ => new()
            };
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp") return "Ảnh thì kéo thả vào ô lịch học nhé.";
            if (slots.Count == 0) return ext is ".xlsx" or ".csv"
                ? "Không thấy buổi học nào. Cần các cột: Thứ · Bắt đầu · Kết thúc · Môn · Phòng."
                : "File này không có buổi học nào đọc được.";
            Replace(slots, "file");
            CrashLog.Write($"Schedule: {slots.Count} classes from a {ext} file");
            return $"Đã thêm {slots.Count} buổi học từ file";
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Schedule file failed: {ex.GetType().Name}");
            return "Không mở được file (đang mở trong Excel thì đóng lại rồi thử nhé).";
        }
    }

    private static List<ClassSlot> ReadXlsx(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ScheduleImport.FromTable(ScheduleImport.ParseXlsx(fs), "file");
    }

    /// <summary>A photo / screenshot of a timetable, read by Windows' own text recognition (offline).</summary>
    public async Task<(string Message, int Count)> FromImageAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            // The reader takes pictures up to a size; a big screenshot is scaled down to fit
            double limit = Windows.Media.Ocr.OcrEngine.MaxImageDimension;
            double scale = Math.Min(1, limit / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new Windows.Graphics.Imaging.BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale), ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale)
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                transform, Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);

            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                      ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return ("Máy chưa có bộ đọc chữ của Windows (Settings → Time & language → Language → thêm English).", 0);

            var result = await engine.RecognizeAsync(bitmap);
            var words = result.Lines.SelectMany(l => l.Words)
                .Select(w => new ScheduleImport.Word(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))
                .ToList();
            var slots = ScheduleImport.FromWords(words, "photo");
            if (slots.Count == 0) return ("Không đọc được lịch trong ảnh. Thử ảnh rõ hơn, chụp cả hàng tên thứ ở trên.", 0);
            Replace(slots, "photo");
            CrashLog.Write($"Schedule: {slots.Count} classes read from a picture");
            return ($"Đọc được {slots.Count} buổi học từ ảnh · kiểm tra lại bên dưới, sai thì bấm ✕", slots.Count);
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Schedule picture failed: {ex.GetType().Name}");
            return ("Không mở được ảnh này.", 0);
        }
    }

    // ── Every 30 s ────────────────────────────────────────────────────
    private async Task TickAsync()
    {
        var now = DateTime.Now;

        // Keep the linked sheets up to date (each once per 6 h, even when reading fails)
        foreach (var src in S.ScheduleSources.ToList())
        {
            if (src.SyncedUtc != null && DateTime.UtcNow - src.SyncedUtc < TimeSpan.FromHours(6)) continue;
            src.SyncedUtc = DateTime.UtcNow;
            var (preview, _) = await PreviewSheetAsync(src.Url, src.Mode);
            if (preview != null) Apply(preview);
        }

        if (!S.ScheduleReminders || S.Schedule.Count == 0) return;

        foreach (var c in On(now))
        {
            var start = now.Date + c.Start;
            var left = start - now;
            string key = $"{c.Id}|{now:yyyyMMdd}";
            if (left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(15) && _reminded.Add(key))
                Upcoming?.Invoke(c, start);
        }

        if (now.Hour == 20 && S.TomorrowNoticeDay?.Date != now.Date)
        {
            S.TomorrowNoticeDay = now.Date;
            S.Save();
            var tomorrow = On(now.AddDays(1));
            if (tomorrow.Count > 0) Tomorrow?.Invoke(tomorrow);
        }
    }

    /// <summary>
    /// What kind of session, from its name: "teach" (Dạy …), "self" (Luyện …, BTVN …, Tự học, Ôn …),
    /// "extra" (with a teacher named: thầy / cô), else "class" (on the school timetable).
    /// </summary>
    public static string Kind(string subject)
    {
        var s = subject.Trim().ToLowerInvariant();
        if (S.ScheduleKinds.TryGetValue(s, out var picked)) return picked;
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^(dạy|day)\b")) return "teach";
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^(luyện|luyen|btvn|tự học|tu hoc|ôn|on tap|bù|làm bài)")) return "self";
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"(?<!\p{L})(thầy|cô)(?!\p{L})")) return "extra";
        return "class";
    }

    /// <summary>"7:30", "13:00".</summary>
    public static string Hm(TimeSpan t) => $"{(int)t.TotalHours % 24}:{t.Minutes:00}";
}
