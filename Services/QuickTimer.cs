namespace DynamicIslandNotifier.Services;

using System;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// A quick countdown typed into the Hạn chót box: "15p nấu mì", "phơi đồ 40 phút",
/// "1h30p giặt đồ", "hẹn giờ 5p". Only lengths with minutes count ("1h" alone is a
/// clock time for a deadline, not a timer).
/// </summary>
public static class QuickTimer
{
    public const int MaxMinutes = 12 * 60;

    // [hẹn giờ] [1h|1 giờ] 30 p|ph|phút|m|min|'   — hours are optional, minutes required
    private static readonly Regex Length = new(
        @"(?<![\p{L}\d:/])(?:(?<h>\d{1,2})\s*(?:h|g|giờ|tiếng)\s*)?(?<m>\d{1,3})\s*(?:phút|ph|p|min|m|')(?![\p{L}\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Lead = new(@"^\s*(?:hẹn\s*giờ|đếm\s*ngược|timer)\s*[:\-]?\s*", RegexOptions.IgnoreCase);

    /// <summary>The length and what it's for, or null if the text isn't a timer.</summary>
    public static (int Minutes, string Label)? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var matches = Length.Matches(text);
        if (matches.Count != 1) return null;   // none, or ambiguous
        var m = matches[0];

        int hours = m.Groups["h"].Success ? int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        int minutes = hours * 60 + int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        if (minutes < 1 || minutes > MaxMinutes) return null;

        string label = (text[..m.Index] + " " + text[(m.Index + m.Length)..]).Trim();
        label = Lead.Replace(label, "");
        label = Regex.Replace(label, @"^(?:sau|trong|nữa)\s+|\s+(?:nữa|sau)$", "", RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"\s{2,}", " ").Trim(' ', ',', '-', ':', '·');
        if (label.Length == 0) label = "Hẹn giờ";
        else label = char.ToUpper(label[0], CultureInfo.CurrentCulture) + label[1..];
        return (minutes, label);
    }

    /// <summary>"12:05" or "1:02:05" left.</summary>
    public static string Format(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
    }
}
