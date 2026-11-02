namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

/// <summary>One copied text or image.</summary>
public sealed class ClipItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Time { get; set; } = DateTime.Now;
    public string? Text { get; set; }
    public string? ImageFile { get; set; }    // file name in the clipboard folder
    public bool Pinned { get; set; }

    public bool IsImage => ImageFile != null;
}

/// <summary>
/// Clipboard history: the last 30 texts and images you copied (Ctrl+C),
/// plus pinned ones, kept on this PC in %LOCALAPPDATA%\DynamicIslandNotifier\clipboard.
/// Copies from password managers (which mark their copies as private) are skipped.
/// Use from the UI thread.
/// </summary>
public sealed class ClipboardService
{
    public const int Max = 30;

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "clipboard");
    private static readonly string IndexFile = Path.Combine(Folder, "index.json");

    private readonly List<ClipItem> _items = Load();
    private HwndSource? _source;
    private bool _ignoreNext;          // our own "copy again" shouldn't add a new entry

    public event Action? Changed;

    /// <summary>Pinned first, then newest first.</summary>
    public IReadOnlyList<ClipItem> Items => _items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.Time).ToList();

    public static string ImagePath(ClipItem item) => Path.Combine(Folder, item.ImageFile ?? "");

    /// <summary>Starts listening; needs a window handle to receive clipboard messages.</summary>
    public void Start(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        AddClipboardFormatListener(hwnd);
    }

    public void Stop()
    {
        if (_source == null) return;
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) Capture();
        return IntPtr.Zero;
    }

    // ── Capture ───────────────────────────────────────────────────────
    private void Capture()
    {
        if (_ignoreNext) { _ignoreNext = false; return; }
        try
        {
            var data = Retry(() => Clipboard.GetDataObject());
            if (data == null) return;

            // Password managers and other apps mark private copies like this.
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
                data.GetDataPresent("Clipboard Viewer Ignore") ||
                data.GetDataPresent("CanIncludeInClipboardHistory") && IsZero(data.GetData("CanIncludeInClipboardHistory")))
                return;

            // Text first: Excel and Word put a picture of the cells next to the text.
            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                var text = data.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrWhiteSpace(text)) AddText(text);
            }
            else if (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib))
            {
                var image = Retry(() => Clipboard.GetImage());
                if (image != null) AddImage(image);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Note($"Clipboard: {ex.Message}");
        }
    }

    private void AddText(string text)
    {
        if (text.Length > 20000) text = text[..20000];
        var same = _items.FirstOrDefault(i => i.Text == text);
        if (same != null) same.Time = DateTime.Now;      // copied again: move to the top
        else _items.Add(new ClipItem { Text = text });
        Trim();
        Save();
        Changed?.Invoke();
    }

    private void AddImage(BitmapSource image)
    {
        Directory.CreateDirectory(Folder);
        var item = new ClipItem { ImageFile = $"{Guid.NewGuid():N}.png" };
        using (var fs = File.Create(ImagePath(item)))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            enc.Save(fs);
        }
        _items.Add(item);
        Trim();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Keeps pinned items plus the newest 30 others.</summary>
    private void Trim()
    {
        var extra = _items.Where(i => !i.Pinned).OrderByDescending(i => i.Time).Skip(Max).ToList();
        foreach (var item in extra) Remove(item, save: false);
    }

    // ── Actions ───────────────────────────────────────────────────────
    /// <summary>Puts the item back on the clipboard, ready to paste.</summary>
    public bool CopyBack(ClipItem item)
    {
        try
        {
            _ignoreNext = true;
            if (item.IsImage)
            {
                var bmp = LoadImage(item, 0);
                if (bmp == null) { _ignoreNext = false; return false; }
                Retry(() => { Clipboard.SetImage(bmp); return true; });
            }
            else
            {
                Retry(() => { Clipboard.SetText(item.Text ?? ""); return true; });
            }
            item.Time = DateTime.Now;
            Save();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _ignoreNext = false;
            DebugLog.Note($"Clipboard copy: {ex.Message}");
            return false;
        }
    }

    public void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        Trim();
        Save();
        Changed?.Invoke();
    }

    public void Delete(ClipItem item)
    {
        Remove(item, save: true);
        Changed?.Invoke();
    }

    /// <summary>Clears everything except pinned items.</summary>
    public void ClearUnpinned()
    {
        foreach (var item in _items.Where(i => !i.Pinned).ToList()) Remove(item, save: false);
        Save();
        Changed?.Invoke();
    }

    private void Remove(ClipItem item, bool save)
    {
        _items.Remove(item);
        if (item.IsImage) try { File.Delete(ImagePath(item)); } catch { }
        if (save) Save();
    }

    /// <summary>The image, or a small thumbnail when <paramref name="height"/> &gt; 0.</summary>
    public static BitmapSource? LoadImage(ClipItem item, int height)
    {
        try
        {
            var path = ImagePath(item);
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // don't keep the file locked
            if (height > 0) bmp.DecodePixelHeight = height;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────
    /// <summary>The clipboard is sometimes briefly locked by the app that just wrote to it.</summary>
    private static T? Retry<T>(Func<T?> action)
    {
        for (int i = 0; ; i++)
        {
            try { return action(); }
            catch (COMException) when (i < 5) { Thread.Sleep(40); }
            catch (ExternalException) when (i < 5) { Thread.Sleep(40); }
        }
    }

    private static bool IsZero(object? value) => value switch
    {
        MemoryStream ms => ms.Length >= 4 && BitConverter.ToInt32(ms.ToArray(), 0) == 0,
        int n => n == 0,
        _ => false
    };

    private static List<ClipItem> Load()
    {
        try
        {
            if (File.Exists(IndexFile))
            {
                var items = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(IndexFile)) ?? new();
                return items.Where(i => !i.IsImage || File.Exists(ImagePath(i))).ToList();
            }
        }
        catch { }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(IndexFile, JsonSerializer.Serialize(_items));
        }
        catch { }
    }
}
