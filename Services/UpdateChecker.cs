namespace DynamicIslandNotifier.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

/// <summary>
/// Looks for a newer release on GitHub (at start and every 6 hours) and,
/// when you agree, downloads it and swaps it in: the app closes, a tiny
/// script replaces the .exe and starts the new one.
/// </summary>
public sealed class UpdateChecker
{
    private const string Repo = "khaibadao/dynamic-island-on-windows";
    private static readonly HttpClient Http = CreateClient();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private Version? _announced;

    /// <summary>A newer version is out.</summary>
    public event Action<Version>? Available;

    public static Version Current =>
        typeof(UpdateChecker).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandNotifier-updater");
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

    /// <summary>Returns the newer version, or null when you're up to date (or offline).</summary>
    public async Task<Version?> CheckAsync(bool manual)
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest) || latest <= Current) return null;
            if (manual || _announced != latest)
            {
                _announced = latest;
                Available?.Invoke(latest);
            }
            return latest;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Downloads the new .exe, then closes the app so a small script can swap it in and restart.</summary>
    public static async Task<bool> InstallAsync(Version version)
    {
        try
        {
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            string url = $"https://github.com/{Repo}/releases/download/v{version}/DynamicIslandNotifier-{arch}.exe";
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("no exe path");
            string temp = Path.Combine(Path.GetTempPath(), $"DynamicIslandNotifier-{version}.exe");

            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(temp);
                await resp.Content.CopyToAsync(fs);
            }
            if (new FileInfo(temp).Length < 1_000_000) throw new IOException("download too small");

            // Wait for this app to close, replace the .exe, unblock it, start it again.
            int pid = Environment.ProcessId;
            string script = Path.Combine(Path.GetTempPath(), "DynamicIslandNotifier-update.cmd");
            File.WriteAllText(script,
$@"@echo off
:wait
tasklist /FI ""PID eq {pid}"" | find ""{pid}"" >nul && (timeout /t 1 /nobreak >nul & goto wait)
move /y ""{temp}"" ""{exe}"" >nul
powershell -NoProfile -WindowStyle Hidden -Command ""Unblock-File -Path '{exe}'""
start """" ""{exe}""
del ""%~f0""
");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
            });
            CrashLog.Write($"Updating to v{version}");
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Update failed: {ex.Message}");
            return false;
        }
    }
}
