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
    /// <summary>
    /// What the database adds for a notification: the website's host
    /// ("claude.ai") and the toast's launch argument, which the browser needs
    /// to open the right tab when the notification is clicked.
    /// </summary>
    public sealed record WpnInfo(string? Host, string? Launch);

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    /// <summary>
    /// Finds the most recent notification whose title (and body, if given)
    /// match, and returns its site and launch argument, or null.
    /// </summary>
    public static async Task<WpnInfo?> FindAsync(string? title, string? body)
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

    private static WpnInfo? TryQuery(string title, string? body)
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

                var info = MatchAndGetInfo(xml, title, body);
                if (info != null) return info;
            }
            DebugLog.Note("wpndatabase: no row matched this notification's title/body");
        }
        catch (Exception ex)
        {
            DebugLog.Note($"wpndatabase read failed: {ex.Message}");
        }
        return null;
    }

    private static WpnInfo? MatchAndGetInfo(string xml, string title, string? body)
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

        // Chromium puts the site's origin in the toast's launch argument,
        // e.g. launch="1|1|0|Default|0|https://claude.ai/|p#https://claude.ai#..."
        var launch = (string?)doc.Root?.Attribute("launch");

        // Site: the Chrome-style attribution line, else the origin in launch.
        var attribution = texts
            .FirstOrDefault(t => (string?)t.Attribute("placement") == "attribution")
            ?.Value;
        var host = !string.IsNullOrWhiteSpace(attribution)
            ? attribution
            : FirstWebHost(launch ?? "") ?? FirstWebHost(xml);

        return new WpnInfo(host, string.IsNullOrWhiteSpace(launch) ? null : launch);
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
