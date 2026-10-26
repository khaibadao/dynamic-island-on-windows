namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Windows.UI.Notifications;

/// <summary>
/// Appends the raw contents of each notification to
/// %LOCALAPPDATA%\DynamicIslandNotifier\notifications.log for troubleshooting.
/// </summary>
public static class DebugLog
{
    /// <summary>Off by default: the log contains message contents. Set to true to troubleshoot.</summary>
    public static bool Enabled { get; set; } = false;

    private static readonly object Gate = new();
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "notifications.log");

    public static void Note(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Write(string? appId, string appName,
                             IEnumerable<NotificationBinding>? bindings, IReadOnlyList<string> texts)
    {
        if (!Enabled) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            sb.AppendLine($"AppUserModelId : {appId}");
            sb.AppendLine($"DisplayName    : {appName}");

            int bi = 0;
            foreach (var b in bindings ?? Enumerable.Empty<NotificationBinding>())
            {
                sb.AppendLine($"Binding[{bi++}] template={b.Template} language={b.Language}");
                foreach (var h in b.Hints)
                    sb.AppendLine($"    hint {h.Key} = {h.Value}");
                int ti = 0;
                foreach (var t in b.GetTextElements())
                    sb.AppendLine($"    text[{ti++}] = \"{t.Text}\"");
            }

            sb.AppendLine($"ToastGeneric texts: {string.Join(" | ", texts.Select(t => $"\"{t}\""))}");
            sb.AppendLine();

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                File.AppendAllText(LogFile, sb.ToString());
            }
        }
        catch { }
    }
}
