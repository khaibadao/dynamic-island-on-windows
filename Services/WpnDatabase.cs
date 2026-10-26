namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

/// <summary>
/// Reads Windows' own notification database (wpndatabase.db), which keeps the
/// full toast XML of each notification. The UserNotificationListener API hides
/// some parts of it, such as the attribution line where Edge/Chrome put the
/// website's domain ("claude.ai", "messenger.com").
/// </summary>
public static class WpnDatabase
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    /// <summary>
    /// Finds the most recent notification whose title (and body, if given)
    /// match, and returns its attribution text, or null.
    /// </summary>
    public static async Task<string?> FindAttributionAsync(string? title, string? body)
    {
        if (string.IsNullOrEmpty(title) || !File.Exists(DbPath)) return null;

        // The database may be written a moment after the listener sees the toast.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var result = TryQuery(title, body);
            if (result != null) return result;
            await Task.Delay(150);
        }
        return null;
    }

    private static string? TryQuery(string title, string? body)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Mode       = SqliteOpenMode.ReadOnly,
                Cache      = SqliteCacheMode.Shared,
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Payload FROM Notification ORDER BY Id DESC LIMIT 40";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                if (reader.IsDBNull(0)) continue;
                var raw = reader.GetValue(0);
                var xml = raw switch
                {
                    byte[] b => Encoding.UTF8.GetString(b),
                    string s => s,
                    _        => null
                };
                if (string.IsNullOrEmpty(xml)) continue;

                var attribution = MatchAndGetAttribution(xml, title, body);
                if (attribution != null) return attribution;
            }
            DebugLog.Note("wpndatabase: no row matched this notification's title/body");
        }
        catch (Exception ex)
        {
            DebugLog.Note($"wpndatabase read failed: {ex.Message}");
        }
        return null;
    }

    private static string? MatchAndGetAttribution(string xml, string title, string? body)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml.TrimEnd('\0')); }
        catch { return null; }

        var texts = doc.Descendants("text").ToList();
        var plain = texts
            .Where(t => (string?)t.Attribute("placement") is null)
            .Select(t => t.Value)
            .ToList();

        if (!plain.Contains(title)) return null;
        if (!string.IsNullOrEmpty(body) && !plain.Contains(body)) return null;

        DebugLog.Note("wpndatabase matched payload: " +
                      (xml.Length > 2500 ? xml[..2500] + "…" : xml));

        // 1) Chrome-style attribution line.
        var attribution = texts
            .FirstOrDefault(t => (string?)t.Attribute("placement") == "attribution")
            ?.Value;
        if (!string.IsNullOrWhiteSpace(attribution)) return attribution;

        // 2) Chromium puts the site's origin in the toast's launch argument,
        //    e.g. launch="0|0|Default|0|https://claude.ai/|p#https://claude.ai#..."
        var launch = (string?)doc.Root?.Attribute("launch") ?? "";
        var host = FirstWebHost(launch) ?? FirstWebHost(xml);
        return host ?? "";   // "" = matched, but no site found
    }

    private static readonly System.Text.RegularExpressions.Regex UrlRegex = new(
        @"https?://([a-z0-9-]+(?:\.[a-z0-9-]+)+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string? FirstWebHost(string s)
    {
        foreach (System.Text.RegularExpressions.Match m in UrlRegex.Matches(s))
        {
            var host = m.Groups[1].Value.ToLowerInvariant();
            // Skip schema / Microsoft resource URLs that aren't the site itself.
            if (host.EndsWith("schemas.microsoft.com") || host.EndsWith("w3.org")) continue;
            return host;
        }
        return null;
    }
}
