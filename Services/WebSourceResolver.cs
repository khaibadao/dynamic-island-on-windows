namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

/// <summary>
/// Identifies notifications that a browser (Edge, Chrome, ...) sends on behalf
/// of a website, and resolves the site's friendly name and favicon.
///
/// Chromium-based browsers put the site's domain in the toast's attribution
/// text (the last text element), e.g. ["Lê Khải", "Hello!", "messenger.com"].
/// </summary>
public static class WebSourceResolver
{
    public sealed record WebSource(string Host, string DisplayName, List<string> Texts);

    // Browsers whose notifications should be re-attributed to the website.
    private static readonly string[] BrowserIdHints =
        { "MSEdge", "Edge", "Chrome", "Brave", "Opera", "Vivaldi", "Firefox", "Coccoc" };

    // Matches a bare domain such as "messenger.com" or "https://chat.zalo.me/".
    private static readonly Regex DomainRegex = new(
        @"^(?:https?://)?(?:www\.)?((?:[a-z0-9-]+\.)+[a-z]{2,})/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Friendly names for common sites. Matched on the exact host, then on the
    // host's suffix, so "chat.zalo.me" matches "zalo.me".
    private static readonly Dictionary<string, string> KnownSites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["messenger.com"]        = "Messenger",
        ["facebook.com"]         = "Facebook",
        ["claude.ai"]            = "Claude",
        ["chatgpt.com"]          = "ChatGPT",
        ["gemini.google.com"]    = "Gemini",
        ["zalo.me"]              = "Zalo",
        ["mail.google.com"]      = "Gmail",
        ["calendar.google.com"]  = "Google Calendar",
        ["meet.google.com"]      = "Google Meet",
        ["drive.google.com"]     = "Google Drive",
        ["docs.google.com"]      = "Google Docs",
        ["classroom.google.com"] = "Google Classroom",
        ["youtube.com"]          = "YouTube",
        ["instagram.com"]        = "Instagram",
        ["discord.com"]          = "Discord",
        ["telegram.org"]         = "Telegram",
        ["whatsapp.com"]         = "WhatsApp",
        ["teams.microsoft.com"]  = "Microsoft Teams",
        ["teams.live.com"]       = "Microsoft Teams",
        ["outlook.live.com"]     = "Outlook",
        ["outlook.office.com"]   = "Outlook",
        ["github.com"]           = "GitHub",
        ["x.com"]                = "X",
        ["twitter.com"]          = "X",
        ["slack.com"]            = "Slack",
        ["notion.so"]            = "Notion",
        ["tiktok.com"]           = "TikTok",
        ["linkedin.com"]         = "LinkedIn",
        ["reddit.com"]           = "Reddit",
        ["fpt.edu.vn"]           = "FPT University",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly ConcurrentDictionary<string, BitmapSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string IconDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "icons");

    /// <summary>
    /// If the notification came from a browser and carries a site domain,
    /// returns the site plus the text elements with the domain removed.
    /// </summary>
    public static bool TryResolve(string? appUserModelId, IReadOnlyList<string> texts,
                                  IReadOnlyList<string> allTexts, out WebSource? source)
    {
        source = null;
        if (string.IsNullOrEmpty(appUserModelId)) return false;
        if (!BrowserIdHints.Any(h => appUserModelId.Contains(h, StringComparison.OrdinalIgnoreCase)))
            return false;

        // 1) A text element that is exactly a domain (the attribution line),
        //    normally the last one. Remove it from the visible texts.
        for (int i = texts.Count - 1; i >= 1; i--)
        {
            var m = DomainRegex.Match(texts[i].Trim());
            if (!m.Success) continue;

            var host = m.Groups[1].Value.ToLowerInvariant();
            var remaining = texts.Where((_, idx) => idx != i).ToList();
            source = new WebSource(host, FriendlyName(host), remaining);
            return true;
        }

        // 2) A short text (in any binding) that is not title/body and contains
        //    a domain, e.g. "claude.ai - Microsoft Edge" or "via claude.ai".
        var visible = new HashSet<string>(texts.Take(2));
        foreach (var t in allTexts.Reverse())
        {
            if (visible.Contains(t) || t.Length > 80) continue;
            var m = LooseDomainRegex.Match(t);
            if (!m.Success) continue;

            var host = m.Groups[1].Value.ToLowerInvariant();
            if (host.StartsWith("www.")) host = host[4..];
            source = new WebSource(host, FriendlyName(host), texts.Where(x => x != t).ToList());
            return true;
        }
        return false;
    }

    public static bool IsBrowser(string? appUserModelId) =>
        !string.IsNullOrEmpty(appUserModelId) &&
        BrowserIdHints.Any(h => appUserModelId.Contains(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds a WebSource from an attribution string such as "claude.ai".</summary>
    public static bool TryFromAttribution(string? attribution, IReadOnlyList<string> texts, out WebSource? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(attribution)) return false;

        var m = DomainRegex.Match(attribution.Trim());
        if (!m.Success) m = LooseDomainRegex.Match(attribution);
        if (!m.Success) return false;

        var host = m.Groups[1].Value.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        source = new WebSource(host, FriendlyName(host), texts.ToList());
        return true;
    }

    private static readonly Regex LooseDomainRegex = new(
        @"\b((?:[a-z0-9-]+\.)+(?:com|net|org|ai|io|me|vn|so|app|dev|co|tv|gg|edu\.vn|com\.vn))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string FriendlyName(string host)
    {
        if (KnownSites.TryGetValue(host, out var exact)) return exact;

        foreach (var kv in KnownSites.OrderByDescending(k => k.Key.Length))
            if (host.EndsWith("." + kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;

        // Unknown site: "example.com" -> "Example"
        var label = host.Split('.').FirstOrDefault(p => p is not "www" and not "web" and not "app") ?? host;
        return label.Length > 0 ? char.ToUpperInvariant(label[0]) + label[1..] : host;
    }

    /// <summary>
    /// Returns the site's favicon (64px), using a memory cache, then a disk
    /// cache, then a download. Returns null if it can't be obtained.
    /// </summary>
    public static async Task<BitmapSource?> GetIconAsync(string host)
    {
        if (IconCache.TryGetValue(host, out var cached)) return cached;

        BitmapSource? icon = null;
        var file = Path.Combine(IconDir, host + ".png");
        try
        {
            byte[]? bytes = null;
            if (File.Exists(file))
            {
                bytes = await File.ReadAllBytesAsync(file);
            }
            else
            {
                var url = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=64";
                bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length > 0)
                {
                    Directory.CreateDirectory(IconDir);
                    await File.WriteAllBytesAsync(file, bytes);
                }
            }

            if (bytes is { Length: > 0 })
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                icon = bmp;
            }
        }
        catch
        {
            // Offline or blocked: fall back to the coloured initial avatar.
        }

        // Only cache successes in memory, so a later retry can still succeed.
        if (icon != null) IconCache[host] = icon;
        return icon;
    }
}
