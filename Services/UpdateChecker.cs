namespace DynamicIslandNotifier.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;

/// <summary>What a check found: a newer version, nothing newer, or no answer.</summary>
public enum UpdateStatus { UpToDate, Available, Failed }

/// <summary>
/// Looks for a newer release on GitHub (at start and every 6 hours). Installing
/// shows what's new first; the download is checked against the release's
/// SHA-256 fingerprints before anything is replaced, in a fresh private folder.
/// </summary>
public sealed class UpdateChecker
{
    private const string Repo = "khaibadao/dynamic-island-on-windows";
    private static readonly HttpClient Http = CreateClient(followRedirects: true);
    private static readonly HttpClient NoRedirect = CreateClient(followRedirects: false);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private Version? _announced;

    /// <summary>A newer version is out.</summary>
    public event Action<Version>? Available;

    /// <summary>
    /// Versions carry the beta number as their 4th part (2.8.0-beta.3 → 2.8.0.3) and a
    /// finished release as 9999 (2.8.0 → 2.8.0.9999), so every beta of 2.8.0 is older
    /// than 2.8.0 itself and plain comparisons just work.
    /// </summary>
    public const int Stable = 9999;

    public static Version Current { get; } = ReadCurrent();

    private static Version ReadCurrent()
    {
        var info = typeof(UpdateChecker).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        if (info != null && ParseTag(info.Split('+')[0]) is { } fromInfo) return fromInfo;
        return typeof(UpdateChecker).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build), Stable) : new Version(0, 0, 0, Stable);
    }

    /// <summary>"v2.8.0", "2.8.0-beta.3" → a version (see <see cref="Stable"/>); null if it isn't one.</summary>
    internal static Version? ParseTag(string? tag)
    {
        var m = tag == null ? null : Regex.Match(tag.Trim(), @"^v?(\d+)\.(\d+)\.(\d+)(?:-beta\.?(\d+))?$", RegexOptions.IgnoreCase);
        if (m is not { Success: true }) return null;
        int beta = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : Stable;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), Math.Min(beta, Stable));
    }

    public static bool IsBeta(Version v) => v.Revision >= 0 && v.Revision < Stable;

    /// <summary>The release's tag: "v2.8.0" or "v2.8.0-beta.3".</summary>
    public static string Tag(Version v) => IsBeta(v) ? $"v{v.ToString(3)}-beta.{v.Revision}" : $"v{v.ToString(3)}";

    /// <summary>For people: "2.8.0" or "2.8.0 beta 3".</summary>
    public static string Show(Version v) => IsBeta(v) ? $"{v.ToString(3)} beta {v.Revision}" : v.ToString(3);

    private static HttpClient CreateClient(bool followRedirects)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = followRedirects }) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandNotifier-updater");
        http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return http;
    }

    public void Start()
    {
        _timer.Tick += async (_, _) => await CheckAsync(manual: false);
        _timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += async (_, _) => { first.Stop(); await CheckAsync(manual: false); };
        first.Start();
    }

    public async Task<(UpdateStatus Status, Version? Latest)> CheckAsync(bool manual)
    {
        var latest = await LatestAsync();
        if (latest == null) return (UpdateStatus.Failed, null);
        if (latest <= Current) return (UpdateStatus.UpToDate, latest);
        if (manual || _announced != latest)
        {
            _announced = latest;
            Available?.Invoke(latest);
        }
        return (UpdateStatus.Available, latest);
    }

    /// <summary>
    /// The newest version. First from the page github.com/…/releases/latest
    /// (it points at the newest tag and has no hourly limit, which matters on a
    /// shared school or office network); if that fails, from the GitHub API.
    /// </summary>
    private static async Task<Version?> LatestAsync()
    {
        if (AppSettings.Current.BetaChannel) return await LatestWithBetasAsync();
        try
        {
            using var resp = await NoRedirect.GetAsync($"https://github.com/{Repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead);
            if (VersionFromLink(resp.Headers.Location?.ToString()) is { } v) return v;
            CrashLog.Write($"Update check: no redirect ({(int)resp.StatusCode})");
        }
        catch (Exception ex) { CrashLog.Write($"Update check (page) failed: {ex.Message}"); }

        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode) { CrashLog.Write($"Update check (API): {(int)resp.StatusCode}"); return null; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return ParseTag(doc.RootElement.GetProperty("tag_name").GetString());
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Update check (API) failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Beta channel: the newest of all releases, betas included (GitHub's "latest" link
    /// leaves pre-releases out, which is what keeps everyone else on finished versions).
    /// </summary>
    private static async Task<Version?> LatestWithBetasAsync()
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases?per_page=30");
            if (!resp.IsSuccessStatusCode) { CrashLog.Write($"Update check (beta): {(int)resp.StatusCode}"); return null; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.EnumerateArray()
                .Where(r => !(r.TryGetProperty("draft", out var d) && d.GetBoolean()))
                .Select(r => ParseTag(r.GetProperty("tag_name").GetString()))
                .Where(v => v != null).Max();
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Update check (beta) failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The version in a link like …/releases/tag/v2.3.0 (or …/v2.8.0-beta.2), or null.</summary>
    internal static Version? VersionFromLink(string? link)
    {
        var m = link == null ? null : Regex.Match(link, @"/tag/([^/]+)$");
        return m is { Success: true } ? ParseTag(m.Groups[1].Value) : null;
    }

    /// <summary>From the release notes, the sections for versions newer than <paramref name="current"/>, as plain text.</summary>
    internal static string ExtractChanges(string notes, Version current)
    {
        if (current.Revision < 0) current = new Version(current.Major, current.Minor, Math.Max(0, current.Build), Stable);
        var keep = new StringBuilder();
        bool on = false;
        foreach (var raw in notes.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var heading = Regex.Match(line, @"^###\s.*?v(\d+\.\d+\.\d+)");
            if (heading.Success)
            {
                on = ParseTag(heading.Groups[1].Value) is { } v && v > current;
                if (on) keep.AppendLine().AppendLine(line.TrimStart('#', ' ').Trim());
                continue;
            }
            if (on && line.Trim().Length > 0)
                keep.AppendLine(Regex.Replace(line.TrimEnd(), @"\*\*|`", "").Replace("- ", "• ", StringComparison.Ordinal));
        }
        return keep.ToString().Trim();
    }

    /// <summary>"What's new" for every version after the one you have, from that release's notes.</summary>
    public static async Task<string> ChangesAsync(Version version)
    {
        try
        {
            var notes = await Http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/{Tag(version)}/RELEASE_NOTES.md");
            var text = ExtractChanges(notes, Current);
            return text.Length > 0 ? text : "Không có ghi chú cho bản này.";
        }
        catch
        {
            return "Không tải được danh sách thay đổi. Bạn vẫn có thể cập nhật.";
        }
    }

    /// <summary>
    /// Downloads the new .exe into a new private folder, checks its SHA-256
    /// against the release's fingerprints, then closes the app so a small
    /// script can swap it in and start it again. Returns an error, or null.
    /// </summary>
    public static async Task<string?> InstallAsync(Version version)
    {
        string? folder = null;
        try
        {
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            string name = $"DynamicIslandNotifier-{arch}.exe";
            string tag = Tag(version);
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("no exe path");

            // A fresh folder with a random name, so nothing can be swapped in on the way
            folder = Path.Combine(Path.GetTempPath(), "DynamicIsland-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string temp = Path.Combine(folder, name);

            string? expected = await ExpectedHashAsync(tag, name);
            if (expected == null) return "Không lấy được mã kiểm tra của bản mới";

            using (var resp = await Http.GetAsync($"https://github.com/{Repo}/releases/download/{tag}/{name}", HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(temp);
                await resp.Content.CopyToAsync(fs);
            }

            string actual;
            await using (var fs = File.OpenRead(temp))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (actual != expected)
            {
                CrashLog.Write($"Update {tag}: SHA-256 mismatch, not installed");
                Directory.Delete(folder, recursive: true);
                return "File tải về không khớp mã kiểm tra, đã huỷ";
            }

            // Wait for this app to close, replace the .exe, start it again, clean up.
            int pid = Environment.ProcessId;
            string script = Path.Combine(folder, "update.cmd");
            File.WriteAllText(script,
$@"@echo off
:wait
tasklist /FI ""PID eq {pid}"" | find ""{pid}"" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)
move /y ""{temp}"" ""{exe}"" >nul
start """" ""{exe}""
cd /d ""%TEMP%""
rmdir /s /q ""{folder}""
");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
            });
            CrashLog.Write($"Updating to {tag} (SHA-256 checked)");
            return null;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Update failed: {ex.Message}");
            try { if (folder != null) Directory.Delete(folder, recursive: true); } catch { }
            return "Không tải được bản mới";
        }
    }

    /// <summary>The fingerprint from the release's SHA256SUMS.txt; else from the GitHub API.</summary>
    private static async Task<string?> ExpectedHashAsync(string tag, string name)
    {
        try
        {
            var sums = await Http.GetStringAsync($"https://github.com/{Repo}/releases/download/{tag}/SHA256SUMS.txt");
            foreach (var line in sums.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1] == name && parts[0].Length == 64) return parts[0].ToLowerInvariant();
            }
        }
        catch (HttpRequestException) { }

        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/tags/{tag}");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (asset.GetProperty("name").GetString() == name &&
                    asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:"))
                    return digest[7..].ToLowerInvariant();
        }
        catch { }
        return null;
    }
}
