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
    public string? Source { get; set; }       // the app it was copied from, e.g. "Microsoft Edge"
    public string? Hash { get; set; }         // images: fingerprint of the pixels, to spot the same picture twice

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
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    /// <summary>Friendly name of the app that put this on the clipboard (or the app in front).</summary>
    private static string? SourceApp()
    {
        try
        {
            var hwnd = GetClipboardOwner();
            if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            string? name = null;
            try { name = p.MainModule?.FileVersionInfo.FileDescription; } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = p.ProcessName;
            return name switch
            {
                "Windows Explorer" => "File Explorer",
                "Snipping Tool" or "ScreenClippingHost" => "Chụp màn hình",
                _ => name
            };
        }
        catch
        {
            return null;
        }
    }

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "clipboard");
    private static readonly string IndexFile = Path.Combine(Folder, "index.dat");        // only your account can read it
    private static readonly string OldIndexFile = Path.Combine(Folder, "index.json");    // before v2.3

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
        var source = SourceApp();
        if (same != null) { same.Time = DateTime.Now; same.Source = source ?? same.Source; }   // copied again: move to the top
        else _items.Add(new ClipItem { Text = text, Source = source });
        Trim();
        Save();
        Changed?.Invoke();
    }

    private void AddImage(BitmapSource image)
    {
        // Screenshot tools (Snipping Tool) put the same picture on the clipboard
        // twice in a row, the second copy a little different inside (e.g. with or
        // without transparency). So: the same picture (colours only), or a picture
        // of the same size from the same app within 3 seconds, counts as one.
        var hash = PixelHash(image);
        var source = SourceApp();
        string size = $"{image.PixelWidth}x{image.PixelHeight}";
        var same = hash == null ? null : _items.FirstOrDefault(i => i.IsImage && i.Hash == hash);
        var justNow = _items.Where(i => i.IsImage && i.Hash != null && i.Hash.StartsWith(size + ":")
                                         && (DateTime.Now - i.Time).TotalSeconds < 3
                                         && (source == null || i.Source == null || i.Source == source))
                            .OrderByDescending(i => i.Time).FirstOrDefault();
        same ??= justNow;
        if (same != null)
        {
            same.Time = DateTime.Now;
            Save();
            Changed?.Invoke();
            return;
        }

        Directory.CreateDirectory(Folder);
        var item = new ClipItem { ImageFile = $"{Guid.NewGuid():N}.bin", Source = source, Hash = hash };
        using (var ms = new MemoryStream())
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            enc.Save(ms);
            SecureStore.WriteBytes(ImagePath(item), ms.ToArray());   // protected like the text
        }
        _items.Add(item);
        Trim();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Size + a fingerprint of the colours (transparency ignored).</summary>
    private static string? PixelHash(BitmapSource image)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 0;
            return $"{bgra.PixelWidth}x{bgra.PixelHeight}:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(pixels));
        }
        catch
        {
            return null;
        }
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

    /// <summary>Clears the texts (or the images), except pinned ones.</summary>
    public void ClearUnpinned(bool images)
    {
        foreach (var item in _items.Where(i => !i.Pinned && i.IsImage == images).ToList()) Remove(item, save: false);
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
            var bytes = path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ? SecureStore.ReadBytes(path) : File.ReadAllBytes(path);
            if (bytes == null) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
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
            var json = SecureStore.ReadTextOrMigrate(IndexFile, OldIndexFile);
            if (json != null)
            {
                var items = JsonSerializer.Deserialize<List<ClipItem>>(json) ?? new();
                items = items.Where(i => !i.IsImage || File.Exists(ImagePath(i))).ToList();
                // Pictures saved by older versions as plain .png: protect them too
                bool changed = false;
                foreach (var i in items.Where(i => i.IsImage && i.ImageFile!.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var old = ImagePath(i);
                        i.ImageFile = Path.ChangeExtension(i.ImageFile, ".bin");
                        SecureStore.WriteBytes(ImagePath(i), File.ReadAllBytes(old));
                        File.Delete(old);
                        changed = true;
                    }
                    catch { }
                }
                if (changed) SecureStore.WriteText(IndexFile, JsonSerializer.Serialize(items));
                return items;
            }
        }
        catch { }
        return new();
    }

    private void Save()
    {
        try
        {
            SecureStore.WriteText(IndexFile, JsonSerializer.Serialize(_items));
        }
        catch { }
    }
}
