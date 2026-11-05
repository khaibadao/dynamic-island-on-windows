namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Files only your Windows account can read (Windows DPAPI): copied text,
/// copied pictures and the notification history are kept like this, so
/// another account on the PC, or someone who copies the folder, sees nothing.
/// </summary>
public static class SecureStore
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("DynamicIslandNotifier/v1");

    public static void WriteBytes(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sealedData = ProtectedData.Protect(data, Salt, DataProtectionScope.CurrentUser);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, sealedData);
        File.Move(temp, path, overwrite: true);
    }

    public static byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path)) return null;
        try { return ProtectedData.Unprotect(File.ReadAllBytes(path), Salt, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { return null; }   // another account's file, or damaged
    }

    public static void WriteText(string path, string text) => WriteBytes(path, Encoding.UTF8.GetBytes(text));

    public static string? ReadText(string path) => ReadBytes(path) is { } b ? Encoding.UTF8.GetString(b) : null;

    /// <summary>
    /// The text from the protected file, or (once, from older versions) from the
    /// plain one next to it, which is then deleted.
    /// </summary>
    public static string? ReadTextOrMigrate(string path, string oldPlainPath)
    {
        var text = ReadText(path);
        if (text != null) return text;
        if (!File.Exists(oldPlainPath)) return null;
        try
        {
            text = File.ReadAllText(oldPlainPath);
            WriteText(path, text);
            File.Delete(oldPlainPath);
            CrashLog.Write($"Protected {Path.GetFileName(oldPlainPath)}");
            return text;
        }
        catch { return text; }
    }
}
