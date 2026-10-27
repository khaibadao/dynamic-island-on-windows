namespace DynamicIslandNotifier.Core;

using System.Windows.Media.Imaging;

/// <summary>
/// Immutable data object representing a single notification.
/// Decouples the notification source from the UI layer.
/// </summary>
public sealed class NotificationData
{
    public string AppName { get; init; } = "App";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public BitmapSource? Icon { get; init; }
    public string? AppUserModelId { get; init; }

    /// <summary>
    /// Who sent it, for muting: "site:messenger.com", "app:&lt;AppUserModelId&gt;",
    /// or "system:battery". Null = can't be muted.
    /// </summary>
    public string? SourceKey { get; init; }

    /// <summary>Optional icon-font glyph shown in the avatar instead of a letter (e.g. a battery).</summary>
    public string? Glyph { get; init; }

    /// <summary>Avatar colour for <see cref="Glyph"/>, e.g. "#30D158".</summary>
    public string? GlyphColor { get; init; }

    // ── What clicking the island does ─────────────────────────────────
    /// <summary>Browser to hand the click to (e.g. ...\msedge.exe).</summary>
    public string? BrowserExe { get; init; }

    /// <summary>The toast's launch argument; the browser uses it to open the right tab.</summary>
    public string? BrowserLaunchId { get; init; }

    /// <summary>Fallback: the site to open if the browser can't be handed the click.</summary>
    public string? SiteUrl { get; init; }
}
