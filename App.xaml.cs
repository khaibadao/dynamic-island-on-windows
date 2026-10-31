using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;

namespace DynamicIslandNotifier;

public partial class App : Application
{
    private NotificationListener? _listener;
    private IslandWindow? _island;
    private MediaWatcher? _media;
    private NotchWindow? _notch;
    private FocusService? _focus;
    private VolumeWatcher? _volume;
    private bool _enabled = true;
    private static System.Threading.Mutex? _singleInstance;
    private BatteryWatcher? _battery;

    // Newest first; shown under "Recent notifications" in the tray menu
    private readonly NotificationHistory _history = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one copy at a time (e.g. started both by Windows and by hand)
        _singleInstance = new System.Threading.Mutex(true, @"Local\DynamicIslandNotifier.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // Create the overlay pill window (invisible until a notification arrives)
        _island = new IslandWindow();
        _island.Show();

        // Create services
        var suppressor = new ToastSuppressor();
        _listener = new NotificationListener(suppressor);

        // Wire notification events to the UI
        _listener.OnNotificationReceived += data => _island.Dispatcher.Invoke(() => ShowOnIsland(data, keepInHistory: true));
        _island.MuteRequested += Mute;
        _island.ActiveChanged += active => _notch?.SetIslandActive(active);

        // Battery: charger plugged in / out, low battery
        _battery = new BatteryWatcher();
        _battery.Notify += data => _island.Dispatcher.Invoke(() => ShowOnIsland(data, keepInHistory: false));
        _battery.Start();

        // Rebuild the history / muted lists each time the tray menu opens
        if (Current.Resources["TrayIcon"] is Hardcodet.Wpf.TaskbarNotification.TaskbarIcon { ContextMenu: { } menu })
            menu.Opened += (_, _) => RebuildDynamicMenus();

        // The notch: music, tree planting, volume, recent notifications
        _media  = new MediaWatcher { Enabled = AppSettings.Current.MusicEnabled };
        _focus  = new FocusService();
        _notch  = new NotchWindow(_media, _focus,
            () => _history.Items.Select(entry => (entry.Time, entry.Data)).ToList());
        _ = _history.RestoreIconsAsync();   // site logos for notifications kept from last time
        _media.Changed += (state, newTrack) => _notch.Dispatcher.Invoke(() => _notch.UpdateMedia(state, newTrack));
        _focus.Finished += session => _island.Dispatcher.Invoke(() => ShowOnIsland(MakeFocusNotification(session), keepInHistory: false));
        _notch.Show();
        _notch.NotchEnabled = AppSettings.Current.NotchEnabled;

        _volume = new VolumeWatcher();
        _volume.Changed += (level, muted) => _notch.Dispatcher.BeginInvoke(new Action(() => _notch.ShowVolume(level, muted)));
        _volume.Start();

        UpdateMusicMenuHeader();
        UpdateNotchMenu();

        // Start with Windows: on by default from the first run
        AutoStart.EnsureOnStartup(AppSettings.Current);
        UpdateAutoStartMenu();

        await _listener.StartAsync();
        await _media.StartAsync();
    }

    // ── Notifications: mute + history ─────────────────────────────────
    private void ShowOnIsland(NotificationData data, bool keepInHistory)
    {
        if (!_enabled || _island == null) return;
        if (data.SourceKey != null && AppSettings.Current.MutedSources.ContainsKey(data.SourceKey)) return;

        if (keepInHistory)
        {
            _history.Add(data);
        }
        _island.ShowNotification(data);
    }

    private void Mute(NotificationData data)
    {
        if (data.SourceKey == null) return;
        AppSettings.Current.MutedSources[data.SourceKey] = data.AppName;
        AppSettings.Current.Save();
    }

    private void RebuildDynamicMenus()
    {
        if (Current.Resources["TrayIcon"] is not Hardcodet.Wpf.TaskbarNotification.TaskbarIcon { ContextMenu: { } menu })
            return;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "history") FillHistory(item);
            if (item.Tag as string == "muted")   FillMuted(item);
        }
    }

    private void FillHistory(MenuItem parent)
    {
        parent.Items.Clear();
        var items = _history.Items;
        if (items.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = Plain("Chưa có thông báo nào"), IsEnabled = false });
            return;
        }

        foreach (var (time, data) in items)
        {
            var text = string.IsNullOrWhiteSpace(data.Body) ? data.Title : data.Body;
            var item = new MenuItem
            {
                Header  = Plain($"{time:HH:mm}   {data.AppName} — {Shorten(text, 45)}"),
                ToolTip = text
            };
            var captured = data;
            item.Click += (_, _) => IslandWindow.OpenSource(captured, captured.AppUserModelId);
            parent.Items.Add(item);
        }

        parent.Items.Add(new Separator());
        var clear = new MenuItem { Header = Plain("Xoá lịch sử") };
        clear.Click += (_, _) => _history.Clear();
        parent.Items.Add(clear);
    }

    private static void FillMuted(MenuItem parent)
    {
        parent.Items.Clear();
        var muted = AppSettings.Current.MutedSources;
        if (muted.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = Plain("Chưa tắt nguồn nào"), IsEnabled = false });
            return;
        }

        foreach (var (key, name) in muted.OrderBy(kv => kv.Value))
        {
            var item = new MenuItem { Header = Plain($"Bật lại: {name}"), ToolTip = key };
            item.Click += (_, _) =>
            {
                AppSettings.Current.MutedSources.Remove(key);
                AppSettings.Current.Save();
            };
            parent.Items.Add(item);
        }
    }

    /// <summary>A header that shows "_" literally instead of as a keyboard shortcut.</summary>
    private static TextBlock Plain(string text) => new() { Text = text };

    private static string Shorten(string s, int max)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private NotificationData MakeFocusNotification(FocusSession session)
    {
        int grownToday = _focus?.Today().Count(s => s.Success) ?? 0;
        if (session.Success) System.Media.SystemSounds.Asterisk.Play();

        return new NotificationData
        {
            AppName   = "Trồng cây",
            Title     = session.Success ? $"{Core.TreeCatalog.Get(session.TreeKind).Name} đã lớn!" : "Cây đã chết",
            Body      = session.Success
                ? $"Bạn đã tập trung {session.Minutes} phút · Hôm nay đã trồng {grownToday} cây"
                : "Bạn đã bỏ cuộc giữa chừng. Lần sau cố lên nhé!",
            Icon      = DynamicIslandNotifier.UI.TreeShape.Render(1, dead: !session.Success, session.TreeKind),
            SourceKey = "system:focus"
        };
    }

    private void NotchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_notch == null) return;
        _notch.NotchEnabled = !_notch.NotchEnabled;
        AppSettings.Current.NotchEnabled = _notch.NotchEnabled;
        AppSettings.Current.Save();
        UpdateNotchMenu();
    }

    private void UpdateNotchMenu()
    {
        var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon?)Current.Resources["TrayIcon"];
        if (tray?.ContextMenu == null) return;
        foreach (var obj in tray.ContextMenu.Items)
            if (obj is MenuItem { Tag: "notch" } item)
                item.IsChecked = _notch?.NotchEnabled == true;
    }

    private void MusicToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_media == null) return;
        _media.Enabled = !_media.Enabled;   // turning it off hides the player
        AppSettings.Current.MusicEnabled = _media.Enabled;
        AppSettings.Current.Save();
        UpdateMusicMenuHeader();
    }

    private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
    {
        if (AutoStart.IsEnabled) AutoStart.Disable();
        else                     AutoStart.Enable();
        UpdateAutoStartMenu();
    }

    private void UpdateAutoStartMenu()
    {
        var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon?)Current.Resources["TrayIcon"];
        if (tray?.ContextMenu == null) return;
        foreach (var obj in tray.ContextMenu.Items)
            if (obj is MenuItem { Tag: "autostart" } item)
                item.IsChecked = AutoStart.IsEnabled;
    }

    private void UpdateMusicMenuHeader()
    {
        var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon?)Current.Resources["TrayIcon"];
        if (tray?.ContextMenu == null) return;
        foreach (var obj in tray.ContextMenu.Items)
            if (obj is MenuItem { Tag: "music" } item)
                item.Header = _media?.Enabled == true ? "Music: On" : "Music: Off";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listener?.Stop();
        var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon?)Current.Resources["TrayIcon"];
        tray?.Dispose();
        base.OnExit(e);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _enabled = !_enabled;
        if (_listener != null) _listener.Enabled = _enabled;
        var item = sender as MenuItem;
        if (item != null)
            item.Header = _enabled ? "Disable" : "Enable";
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Current.Shutdown();
    }
}
