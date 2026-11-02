namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/// <summary>One line of synced lyrics.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>
/// Time-synced lyrics from LRCLIB (lrclib.net: free, no account). Only the
/// song's title, artist and length are sent. Results are cached per song.
/// </summary>
public static class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, IReadOnlyList<LyricLine>> Cache = new();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandNotifier/1.7 (+https://github.com/khaibadao/dynamic-island-on-windows)");
        return http;
    }

    /// <summary>Synced lyrics for the song, or an empty list if none were found.</summary>
    public static async Task<IReadOnlyList<LyricLine>> FindAsync(string title, string artist, TimeSpan duration)
    {
        var key = $"{title}|{artist}|{(int)duration.TotalSeconds}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        IReadOnlyList<LyricLine> result = Array.Empty<LyricLine>();
        try
        {
            // 1) Exact match on title + artist (+ length when known)
            var url = $"api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}"
                      + (duration > TimeSpan.Zero ? $"&duration={(int)duration.TotalSeconds}" : "");
            result = await TryGet(url);

            // 2) Search with a cleaned-up title. YouTube titles often look like
            //    "JACK - J97 | NGƯỜI DƯNG | Album …" or "Song (Official MV)".
            if (result.Count == 0)
            {
                foreach (var q in Queries(title, artist))
                {
                    result = await Search(q, duration);
                    if (result.Count > 0) break;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Note($"Lyrics: {ex.Message}");
        }

        Cache[key] = result;
        return result;
    }

    private static async Task<IReadOnlyList<LyricLine>> TryGet(string url)
    {
        using var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return Array.Empty<LyricLine>();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return Parse(Synced(doc.RootElement));
    }

    private static async Task<IReadOnlyList<LyricLine>> Search(string query, TimeSpan duration)
    {
        using var resp = await Http.GetAsync($"api/search?q={Uri.EscapeDataString(query)}");
        if (!resp.IsSuccessStatusCode) return Array.Empty<LyricLine>();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<LyricLine>();

        // Prefer the result whose length is closest to the song playing.
        var best = doc.RootElement.EnumerateArray()
            .Where(e => Synced(e) != null)
            .Select(e => (e, diff: duration > TimeSpan.Zero && e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                                    ? Math.Abs(d.GetDouble() - duration.TotalSeconds) : 0))
            .Where(x => x.diff <= 8)
            .OrderBy(x => x.diff)
            .Select(x => x.e)
            .FirstOrDefault();

        return best.ValueKind == JsonValueKind.Undefined ? Array.Empty<LyricLine>() : Parse(Synced(best));
    }

    private static string? Synced(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() : null;

    private static IEnumerable<string> Queries(string title, string artist)
    {
        var clean = Regex.Replace(title, @"[\(\[][^\)\]]*(official|mv|lyric|audio|video|visualizer|music)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        var parts = clean.Split(new[] { '|', '｜' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // "JACK - J97 | NGƯỜI DƯNG | …" → the song is usually the second part
        if (parts.Length >= 2) yield return $"{parts[1]} {artist}".Trim();
        if (parts.Length >= 1)
        {
            var first = parts[0];
            var dash = first.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) yield return $"{first[(dash + 3)..]} {first[..dash]}".Trim();   // "Artist - Song"
            yield return $"{first} {artist}".Trim();
        }
    }

    /// <summary>Parses "[mm:ss.xx] text" lines.</summary>
    private static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return Array.Empty<LyricLine>();
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = Regex.Match(raw, @"^\s*\[(\d+):(\d+(?:\.\d+)?)\](.*)$");
            if (!m.Success) continue;
            var t = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                  + TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            var text = m.Groups[3].Value.Trim();
            lines.Add(new LyricLine(t, text.Length == 0 ? "♪" : text));
        }
        return lines.OrderBy(l => l.Time).ToList();
    }
}
