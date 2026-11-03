namespace DynamicIslandNotifier.Services;

using System;
using System.IO;

/// <summary>
/// A small log of when the app started and of any error that would have
/// closed it (%LOCALAPPDATA%\DynamicIslandNotifier\startup.log). Holds no
/// notification or clipboard contents, only times and error messages.
/// </summary>
public static class CrashLog
{
    private static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "startup.log");
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                // Keep it small: start over once it passes ~200 KB
                if (File.Exists(File_) && new FileInfo(File_).Length > 200_000) File.Delete(File_);
                File.AppendAllText(File_, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Error(string where, Exception ex) =>
        Write($"ERROR in {where}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
}
