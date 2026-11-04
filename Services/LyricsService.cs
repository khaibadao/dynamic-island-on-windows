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

/// <summary>One line of lyrics.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Lyrics for a song: time-synced (highlight as it plays) or plain text.</summary>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool Synced, string Source)
{
    public static readonly Lyrics None = new(Array.Empty<LyricLine>(), false, "");
}

/// <summary>
/// Finds lyrics for the playing song. Tries, in order:
/// 1. LRCLIB (lrclib.net, free and open) for synced lyrics,
/// 2. NetEase Cloud Music (more Vietnamese songs; an unofficial API that may stop working),
/// 3. LRCLIB's plain (not synced) lyrics.
/// YouTube video titles ("JACK - J97 | LƯU NIÊN | Album … (Official MV)") are cleaned up
/// into several title/artist guesses first. Only title, artist and length are sent.
/// </summary>
public static class LyricsService
{
    private static readonly HttpClient Lrclib = Client("https://lrclib.net/");
    private static readonly HttpClient NetEase = Client("https://music.163.com/");
    private static readonly ConcurrentDictionary<string, Lyrics> Cache = new();

    private static HttpClient Client(string baseUrl)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DynamicIslandNotifier/2.0");
        http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        return http;
    }

    public static async Task<Lyrics> FindAsync(string title, string artist, TimeSpan duration)
    {
        var key = $"{title}|{artist}|{(int)duration.TotalSeconds}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var guesses = Guesses(title, artist).ToList();
        string? plain = null;
        Lyrics result = Lyrics.None;
        try
        {
            // 1) LRCLIB, synced
            foreach (var (t, a) in guesses)
            {
                var (synced, pl) = await LrclibAsync(t, a, duration);
                plain ??= pl;
                if (synced != null) { result = new Lyrics(ParseLrc(synced), true, "LRCLIB"); break; }
            }

            // 2) NetEase, synced
            if (result.Lines.Count == 0)
            {
                foreach (var (t, a) in guesses.Take(4))
                {
                    var lrc = await NetEaseAsync(t, a, duration);
                    if (lrc == null) continue;
                    var lines = ParseLrc(lrc);
                    if (lines.Count >= 4) { result = new Lyrics(lines, true, "NetEase"); break; }
                }
            }

            // 3) plain lyrics: shown as text, without following the music
            if (result.Lines.Count == 0 && !string.IsNullOrWhiteSpace(plain))
            {
                var lines = plain.Split('\n').Select(l => l.Trim()).Select(l => new LyricLine(TimeSpan.Zero, l.Length == 0 ? "" : l)).ToList();
                result = new Lyrics(lines, false, "LRCLIB");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Note($"Lyrics: {ex.Message}");
        }

        Cache[key] = result;
        return result;
    }

    // ── Title / artist guesses ────────────────────────────────────────
    private static readonly Regex Noise = new(
        @"[\(\[【][^\)\]】]*(official|mv|m/v|lyric|lyrics|audio|video|visuali[sz]er|music|live|karaoke|prod|remix|ver\.?|version|4k|hd|teaser)[^\)\]】]*[\)\]】]",
        RegexOptions.IgnoreCase);
    private static readonly Regex Feat = new(@"\s+(\(|\[)?(ft\.?|feat\.?|featuring)\s+[^\)\]|\-]+(\)|\])?", RegexOptions.IgnoreCase);
    private static readonly Regex Junk = new(@"\b(official\s+(music\s+)?video|official\s+mv|lyrics?\s+video|audio|mv)\b", RegexOptions.IgnoreCase);

    private static string Tidy(string s) => Regex.Replace(Junk.Replace(Noise.Replace(s, ""), ""), @"\s{2,}", " ").Trim(' ', '-', '|', '｜', '·', '"', '“', '”');

    private static IEnumerable<(string Title, string Artist)> Guesses(string title, string artist)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string mainArtist = Regex.Split(artist ?? "", @"\s*[/,&]\s*|\s+x\s+|\s+ft\.?\s+", RegexOptions.IgnoreCase).FirstOrDefault()?.Trim() ?? "";
        if (mainArtist.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) mainArtist = mainArtist[..^8];

        IEnumerable<(string, string)> Raw()
        {
            var clean = Tidy(title);
            yield return (clean, mainArtist);
            var noFeat = Tidy(Feat.Replace(clean, ""));
            yield return (noFeat, mainArtist);

            // "Artist - Song" / "Song - Artist"
            var parts = Regex.Split(clean, @"\s+[-–—]\s+").Select(Tidy).Where(p => p.Length > 0).ToArray();
            if (parts.Length >= 2)
            {
                yield return (Tidy(Feat.Replace(parts[1], "")), parts[0]);
                yield return (Tidy(Feat.Replace(parts[0], "")), parts[1]);
            }

            // "JACK - J97 | LƯU NIÊN | Album …": the song is usually a middle part
            var bars = clean.Split(new[] { '|', '｜' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Tidy).ToArray();
            if (bars.Length >= 2)
            {
                var who = Regex.Split(bars[0], @"\s+[-–—]\s+").Last();
                for (int i = 1; i < bars.Length; i++)
                    if (!bars[i].StartsWith("album", StringComparison.OrdinalIgnoreCase) && !bars[i].StartsWith("track", StringComparison.OrdinalIgnoreCase))
                        yield return (Tidy(Feat.Replace(bars[i], "")), string.IsNullOrEmpty(mainArtist) ? who : mainArtist);
                yield return (Tidy(Feat.Replace(bars[0], "")), mainArtist);
            }
        }

        foreach (var (t, a) in Raw())
            if (t.Length > 0 && seen.Add(t + "|" + a)) yield return (t, a);
    }

    // ── LRCLIB ────────────────────────────────────────────────────────
    private static async Task<(string? Synced, string? Plain)> LrclibAsync(string title, string artist, TimeSpan duration)
    {
        string? plain = null;
        // Exact lookup
        if (artist.Length > 0)
        {
            var url = $"api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}"
                      + (duration > TimeSpan.Zero ? $"&duration={(int)duration.TotalSeconds}" : "");
            using var resp = await Lrclib.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var s = Str(doc.RootElement, "syncedLyrics");
                plain = Str(doc.RootElement, "plainLyrics");
                if (s != null) return (s, plain);
            }
        }

        // Search, best match by length
        var q = $"api/search?track_name={Uri.EscapeDataString(title)}" + (artist.Length > 0 ? $"&artist_name={Uri.EscapeDataString(artist)}" : "");
        foreach (var url in new[] { q, $"api/search?q={Uri.EscapeDataString((title + " " + artist).Trim())}" })
        {
            using var resp = await Lrclib.GetAsync(url);
            if (!resp.IsSuccessStatusCode) continue;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
            var items = doc.RootElement.EnumerateArray()
                .Select(e => (e, diff: Diff(e, "duration", duration, 1)))
                .Where(x => x.diff <= 10).OrderBy(x => x.diff).Select(x => x.e).ToList();
            plain ??= items.Select(e => Str(e, "plainLyrics")).FirstOrDefault(p => p != null);
            var synced = items.Select(e => Str(e, "syncedLyrics")).FirstOrDefault(s => s != null);
            if (synced != null) return (synced, plain);
        }
        return (null, plain);
    }

    // ── NetEase Cloud Music (unofficial) ──────────────────────────────
    private static async Task<string?> NetEaseAsync(string title, string artist, TimeSpan duration)
    {
        try
        {
            var q = Uri.EscapeDataString((title + " " + artist).Trim());
            using var resp = await NetEase.GetAsync($"api/search/get/web?s={q}&type=1&offset=0&limit=10");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("songs", out var songs)) return null;

            string want = Normalize(title);
            var best = songs.EnumerateArray()
                .Select(s => (id: s.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                              name: Normalize(Str(s, "name") ?? ""),
                              diff: Diff(s, "duration", duration, 1000)))
                .Where(x => x.id != 0 && x.diff <= 10 && (x.name.Contains(want) || want.Contains(x.name)))
                .OrderBy(x => x.diff)
                .FirstOrDefault();
            if (best.id == 0) return null;

            using var lr = await NetEase.GetAsync($"api/song/lyric?id={best.id}&lv=-1&kv=-1&tv=-1");
            if (!lr.IsSuccessStatusCode) return null;
            using var ldoc = JsonDocument.Parse(await lr.Content.ReadAsStringAsync());
            return ldoc.RootElement.TryGetProperty("lrc", out var lrc) ? Str(lrc, "lyric") : null;
        }
        catch (Exception ex)
        {
            DebugLog.Note($"NetEase lyrics: {ex.Message}");
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(s.GetString()) ? s.GetString() : null;

    /// <summary>How many seconds the result's length is off (0 when we don't know the song's length).</summary>
    private static double Diff(JsonElement e, string name, TimeSpan duration, double unitsPerSecond)
    {
        if (duration <= TimeSpan.Zero) return 0;
        return e.TryGetProperty(name, out var d) && d.ValueKind == JsonValueKind.Number
            ? Math.Abs(d.GetDouble() / unitsPerSecond - duration.TotalSeconds) : 99;
    }

    private static string Normalize(string s)
    {
        var formD = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = formD.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c));
        return new string(chars.ToArray()).Replace("đ", "d");
    }

    private static readonly Regex Credit = new(@"^(作词|作曲|编曲|制作|混音|母带|演唱|词|曲|lyrics?|composer|arranger|producer)\s*[:：]", RegexOptions.IgnoreCase);

    /// <summary>Parses "[mm:ss.xx] text" lines (a line may carry several time tags).</summary>
    private static IReadOnlyList<LyricLine> ParseLrc(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var tags = Regex.Matches(raw, @"\[(\d+):(\d+(?:[\.:]\d+)?)\]");
            if (tags.Count == 0) continue;
            var text = Regex.Replace(raw, @"\[[^\]]*\]", "").Trim();
            if (Credit.IsMatch(text)) continue;
            foreach (Match m in tags)
            {
                var sec = m.Groups[2].Value.Replace(':', '.');
                var t = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                      + TimeSpan.FromSeconds(double.Parse(sec, CultureInfo.InvariantCulture));
                lines.Add(new LyricLine(t, text.Length == 0 ? "♪" : text));
            }
        }
        return lines.OrderBy(l => l.Time).ToList();
    }
}
