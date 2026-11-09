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

    public ScheduleService()
    {
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

    /// <summary>The next class today, if any.</summary>
    public static ClassSlot? NextToday(DateTime now) =>
        On(now).FirstOrDefault(c => c.Start > now.TimeOfDay);

    /// <summary>Puts in a new timetable from one source (the old one from that source goes).</summary>
    public int Replace(IReadOnlyList<ClassSlot> slots, string origin)
    {
        // A new timetable replaces the one imported before (from any source)
        S.Schedule.RemoveAll(c => c.Origin != "typed");
        S.Schedule.AddRange(slots);
        S.Save();
        _reminded.Clear();
        Changed?.Invoke();
        return slots.Count;
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
        S.ScheduleSheetUrl = null;
        S.Save();
        Changed?.Invoke();
    }

    // ── Sources ───────────────────────────────────────────────────────
    /// <summary>Reads a Google Sheet by its link ("Anyone with the link can view"). Returns a message for the user.</summary>
    public async Task<string> FromSheetAsync(string link, bool quiet = false)
    {
        var csv = ScheduleImport.SheetCsvUrl(link);
        if (csv == null) return "Link này không phải Google Sheet. Dán link dạng docs.google.com/spreadsheets/…";
        try
        {
            using var resp = await Http.GetAsync(csv);
            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!resp.IsSuccessStatusCode || type.Contains("html"))
                return "Không đọc được sheet. Mở sheet → Chia sẻ → “Bất kỳ ai có đường liên kết” → Người xem, rồi thử lại.";
            var rows = ScheduleImport.ParseCsv(await resp.Content.ReadAsStringAsync());
            var slots = ScheduleImport.FromTable(rows, "sheet");
            if (slots.Count == 0) return "Sheet không có buổi học nào đọc được. Cần các cột: Thứ · Bắt đầu · Kết thúc · Môn · Phòng.";
            S.ScheduleSheetUrl = link.Trim();
            S.ScheduleSyncedUtc = DateTime.UtcNow;
            Replace(slots, "sheet");
            CrashLog.Write($"Schedule: {slots.Count} classes from a Google Sheet");
            return $"Đã thêm {slots.Count} buổi học từ Google Sheet · tự cập nhật mỗi 6 giờ";
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Schedule sheet failed: {ex.GetType().Name}");
            return quiet ? "" : "Không tải được sheet, kiểm tra mạng rồi thử lại nhé.";
        }
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

        // Keep a sheet up to date
        if (S.ScheduleSheetUrl is { } link && (S.ScheduleSyncedUtc == null || DateTime.UtcNow - S.ScheduleSyncedUtc > TimeSpan.FromHours(6)))
        {
            S.ScheduleSyncedUtc = DateTime.UtcNow;   // once per 6 h even when it fails
            await FromSheetAsync(link, quiet: true);
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

    /// <summary>"7:30", "13:00".</summary>
    public static string Hm(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}";
}
