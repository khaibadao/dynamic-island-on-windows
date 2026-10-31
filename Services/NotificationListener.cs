namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIslandNotifier.Core;
using AppNotification = DynamicIslandNotifier.Core.NotificationData;

/// <summary>
/// Polls the WinRT UserNotificationListener for new toast notifications,
/// packages them into <see cref="NotificationData"/>, and raises an event.
/// Has zero dependency on any UI class.
/// </summary>
public sealed class NotificationListener
{
    private CancellationTokenSource _cts = new();
    // "id|created" of notifications already handled. The creation time is part of
    // the key because a site that replaces its notification (Messenger does, one
    // per chat) can reuse the same id for the new message.
    private readonly HashSet<string> _seen = new();
    private bool _baselineTaken;
    private readonly ToastSuppressor _suppressor;

    /// <summary>Fired on the background thread when a new notification arrives.</summary>
    public event Action<AppNotification>? OnNotificationReceived;

    public bool Enabled { get; set; } = true;

    public NotificationListener(ToastSuppressor suppressor)
    {
        _suppressor = suppressor;
    }

    public async Task StartAsync()
    {
        var listener = UserNotificationListener.Current;

        var accessStatus = await listener.RequestAccessAsync();
        if (accessStatus != UserNotificationListenerAccessStatus.Allowed)
        {
            System.Windows.MessageBox.Show(
                "Dynamic Island Notifier needs access to your notifications.\n\n" +
                "Please go to: Settings → Privacy & Security → Notifications → " +
                "allow 'Dynamic Island Notifier'.",
                "Permission Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Older versions switched off ALL Windows notifications while running and
        // only switched them back on at a clean Quit; after a crash or a forced
        // shutdown they stayed off, so nothing reached the island. Undo that.
        _suppressor.DisableDND();
        _ = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        _suppressor.RestoreAll();
        _cts.Cancel();
    }

    private async Task PollLoop(CancellationToken token)
    {
        var listener = UserNotificationListener.Current;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Enabled)
                {
                    var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                    var current = notifications.Select(KeyOf).ToHashSet();

                    // At startup, whatever is already in the notification center is
                    // old news: remember it without popping it all up again.
                    if (!_baselineTaken)
                    {
                        _seen.UnionWith(current);
                        _baselineTaken = true;
                    }

                    foreach (var n in notifications.OrderBy(n => n.CreationTime))
                    {
                        if (!_seen.Add(KeyOf(n))) continue;

                        // One bad notification must not stop the rest from showing.
                        try
                        {
                            var appModelId = n.AppInfo?.AppUserModelId;
                            if (appModelId != null) _suppressor.SuppressApp(appModelId);

                            var data = await ExtractNotificationData(n);
                            OnNotificationReceived?.Invoke(data);

                            // Keep browser notifications in the notification center:
                            // if we remove them, the browser treats them as closed and
                            // ignores a later click (which is how the island opens the tab).
                            if (data.BrowserLaunchId == null)
                                try { listener.RemoveNotification(n.Id); } catch { }
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Note($"notification {n.Id} failed: {ex}");
                        }
                    }

                    _seen.IntersectWith(current);
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationListener] {ex.Message}");
            }

            await Task.Delay(500, token);
        }
    }

    private static string KeyOf(UserNotification n) => $"{n.Id}|{n.CreationTime.UtcTicks}";

    private static async Task<AppNotification> ExtractNotificationData(UserNotification n)
    {
        var appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "App";
        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);

        var texts = binding?.GetTextElements().Select(e => e.Text ?? "").ToList() ?? new List<string>();
        var appId = n.AppInfo?.AppUserModelId;

        // Text from every binding (not just ToastGeneric), in case the browser
        // puts the site's domain somewhere else.
        var allTexts = new List<string>();
        try
        {
            foreach (var b in n.Notification?.Visual?.Bindings ?? Enumerable.Empty<NotificationBinding>())
                allTexts.AddRange(b.GetTextElements().Select(e => e.Text ?? ""));
        }
        catch { }

        DebugLog.Write(appId, appName, n.Notification?.Visual?.Bindings, texts);

        // ── Web notifications (sent by a browser for a website) ──────────
        // Show the site's name and favicon instead of the browser's.
        WebSourceResolver.WebSource? web = null;
        WpnDatabase.WpnInfo? wpn = null;
        if (WebSourceResolver.IsBrowser(appId))
        {
            // The listener API hides the site and the click (launch) argument,
            // so read them from Windows' notification database.
            wpn = await WpnDatabase.FindAsync(texts.ElementAtOrDefault(0), texts.ElementAtOrDefault(1));
            DebugLog.Note($"wpndatabase host: \"{wpn?.Host}\" launch: \"{wpn?.Launch}\"");

            if (!WebSourceResolver.TryResolve(appId, texts, allTexts, out web))
                WebSourceResolver.TryFromAttribution(wpn?.Host, texts, out web);
        }

        if (web != null)
        {
            var webTitle = web.Texts.ElementAtOrDefault(0) ?? "";
            var webBody  = web.Texts.ElementAtOrDefault(1) ?? "";

            // Drop a title that just repeats the site's name ("Instagram: …").
            if (string.Equals(webTitle.Trim(), web.DisplayName, StringComparison.OrdinalIgnoreCase))
                webTitle = "";

            return new AppNotification
            {
                AppName        = web.DisplayName,
                // Line 1: the site's name. Line 2: "<sender/title>: <message>".
                Title          = web.DisplayName,
                Body           = (string.IsNullOrWhiteSpace(webTitle), string.IsNullOrWhiteSpace(webBody)) switch
                                 {
                                     (true,  true ) => "",
                                     (true,  false) => webBody,
                                     (false, true ) => webTitle,
                                     _              => $"{webTitle}: {webBody}"
                                 },
                Icon            = await WebSourceResolver.GetIconAsync(web.Host),
                AppUserModelId  = appId,
                BrowserExe      = BrowserLocator.FindExe(appId),
                BrowserLaunchId = wpn?.Launch,
                SiteUrl         = $"https://{web.Host}/",
                SourceKey       = $"site:{web.Host}"
            };
        }

        string title = texts.ElementAtOrDefault(0) ?? "";
        string body  = texts.ElementAtOrDefault(1) ?? "";

        BitmapSource? icon = null;
        try
        {
            var logoRef = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(24, 24));
            if (logoRef != null)
            {
                var stream = await logoRef.OpenReadAsync();
                var reader = new Windows.Storage.Streams.DataReader(stream);
                uint size = (uint)stream.Size;
                await reader.LoadAsync(size);
                var bytes = new byte[size];
                reader.ReadBytes(bytes);
                reader.DetachStream();

                var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                icon = bmp;
            }
        }
        catch { }

        return new AppNotification
        {
            AppName        = appName,
            Title          = title,
            Body           = body,
            Icon            = icon,
            AppUserModelId  = n.AppInfo?.AppUserModelId,
            BrowserExe      = wpn?.Launch != null ? BrowserLocator.FindExe(appId) : null,
            BrowserLaunchId = wpn?.Launch,
            SourceKey       = string.IsNullOrEmpty(appId) ? null : $"app:{appId}"
        };
    }
}
