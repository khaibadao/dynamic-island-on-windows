namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// Small settings file kept between runs, at
/// %LOCALAPPDATA%\DynamicIslandNotifier\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Where the music pill sits on screen (null = default spot).</summary>
    public double? MusicLeft { get; set; }
    public double? MusicTop  { get; set; }

    public bool MusicEnabled { get; set; } = true;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandNotifier", "settings.json");

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
