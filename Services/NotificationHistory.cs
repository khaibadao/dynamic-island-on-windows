namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DynamicIslandNotifier.Core;

/// <summary>
/// The most recent notifications, kept between runs in
/// %LOCALAPPDATA%\DynamicIslandNotifier\history.json (on this PC only).
/// Use from the UI thread.
/// </summary>
public sealed class NotificationHistory
{
    public const int Max = 15;

    public sealed record Entry(DateTime Time, NotificationData Data);

    // What goes in the file: text only. Logos are fetched again from the site
    // (cached on disk); a browser's click id is only valid until it restarts.
    private sealed record Stored(DateTime Time, string AppName, string Title, string Body,
                                 string? AppUserModelId, string? SourceKey, string? SiteUrl);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "history.json");

    private readonly List<Entry> _items = Load();

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Entry> Items => _items.ToList();

    public void Add(NotificationData data)
    {
        _items.Insert(0, new Entry(DateTime.Now, data));
        if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
        Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    /// <summary>Puts the site logos back on entries loaded from the file.</summary>
    public async Task RestoreIconsAsync()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var (time, data) = (_items[i].Time, _items[i].Data);
            if (data.Icon != null || data.SourceKey is not { } key || !key.StartsWith("site:")) continue;

            var icon = await WebSourceResolver.GetIconAsync(key["site:".Length..]);
            if (icon == null) continue;

            // The list may have changed while we were waiting.
            int at = _items.FindIndex(e => ReferenceEquals(e.Data, data));
            if (at >= 0) _items[at] = new Entry(time, Copy(data, icon));
        }
    }

    private static NotificationData Copy(NotificationData d, System.Windows.Media.Imaging.BitmapSource? icon) => new()
    {
        AppName        = d.AppName,
        Title          = d.Title,
        Body           = d.Body,
        Icon           = icon,
        AppUserModelId = d.AppUserModelId,
        SourceKey      = d.SourceKey,
        SiteUrl        = d.SiteUrl,
        Glyph          = d.Glyph,
        GlyphColor     = d.GlyphColor
    };

    private static List<Entry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var stored = JsonSerializer.Deserialize<List<Stored>>(File.ReadAllText(FilePath)) ?? new();
            return stored.Take(Max).Select(s => new Entry(s.Time, new NotificationData
            {
                AppName        = s.AppName,
                Title          = s.Title,
                Body           = s.Body,
                AppUserModelId = s.AppUserModelId,
                SourceKey      = s.SourceKey,
                SiteUrl        = s.SiteUrl
            })).ToList();
        }
        catch
        {
            return new();
        }
    }

    private void Save()
    {
        try
        {
            var stored = _items.Select(e => new Stored(e.Time, e.Data.AppName, e.Data.Title, e.Data.Body,
                                                       e.Data.AppUserModelId, e.Data.SourceKey, e.Data.SiteUrl)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));
        }
        catch { }
    }
}
