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
    private ClipboardService? _clips;
    private FocusService? _focus;
    private VolumeWatcher? _volume;
    private BluetoothBatteryWatcher? _headsets;
    private FocusWidget? _focusWidget;
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

        // Keep running if something goes wrong, and note it in startup.log
        bool atBoot = e.Args.Contains("--autostart");
        CrashLog.Write($"Started v{typeof(App).Assembly.GetName().Version} {(atBoot ? "(with Windows)" : "(by hand)")}");
        DispatcherUnhandledException += (_, ev) => { CrashLog.Error("UI", ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => { if (ev.ExceptionObject is Exception ex) CrashLog.Error("app", ex); };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) => { CrashLog.Error("task", ev.Exception); ev.SetObserved(); };

        // Register "start with Windows" first, so a later problem can't skip it
        try { AutoStart.EnsureOnStartup(AppSettings.Current); } catch (Exception ex) { CrashLog.Error("autostart", ex); }

        // Right after sign-in Windows is still busy (notifications, media and
        // Bluetooth services may not answer yet), so give it a few seconds.
        if (atBoot) await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(6));

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
        Try("battery", () => _battery.Start());

        // Rebuild the history / muted lists each time the tray menu opens
        if (Current.Resources["TrayIcon"] is Hardcodet.Wpf.TaskbarNotification.TaskbarIcon { ContextMenu: { } menu })
            menu.Opened += (_, _) => RebuildDynamicMenus();

        // The notch: music, tree planting, volume, recent notifications
        _media  = new MediaWatcher { Enabled = AppSettings.Current.MusicEnabled };
        _focus  = new FocusService();
        _clips  = new ClipboardService();
        _notch  = new NotchWindow(_media, _focus, _history, _clips);
        _notch.MuteRequested += Mute;
        _focusWidget = new FocusWidget(_focus);   // the small floating timer while a tree grows
        _ = _history.RestoreIconsAsync();   // site logos for notifications kept from last time
        _media.Changed += (state, newTrack) => _notch.Dispatcher.Invoke(() => _notch.UpdateMedia(state, newTrack));
        _focus.Finished += session => _island.Dispatcher.Invoke(() => ShowOnIsland(MakeFocusNotification(session), keepInHistory: false));
        _notch.Show();
        _notch.NotchEnabled = AppSettings.Current.NotchEnabled;

        _volume = new VolumeWatcher();
        _volume.Changed += (level, muted) => _notch.Dispatcher.BeginInvoke(new Action(() => _notch.ShowVolume(level, muted)));
        Try("volume", () => _volume.Start());

        // Battery of connected Bluetooth headphones
        _headsets = new BluetoothBatteryWatcher();
        _headsets.Changed += () => _notch.Dispatcher.BeginInvoke(new Action(() => _notch.SetHeadsets(_headsets.Devices)));
        _headsets.Notify  += data => _island.Dispatcher.Invoke(() => ShowOnIsland(data, keepInHistory: false));
        Try("bluetooth", () => _headsets.Start());

        UpdateMusicMenuHeader();
        UpdateNotchMenu();

        UpdateAutoStartMenu();

        await StartWithRetry("notifications", _listener.StartAsync);
        await StartWithRetry("music", _media.StartAsync);
        CrashLog.Write("Ready");
    }

    private static void Try(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { CrashLog.Error(what, ex); }
    }

    /// <summary>Some Windows services aren't ready right after sign-in: try a few times.</summary>
    private static async System.Threading.Tasks.Task StartWithRetry(string what, Func<System.Threading.Tasks.Task> start)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await start();
                return;
            }
            catch (Exception ex)
            {
                CrashLog.Error($"{what} (attempt {attempt})", ex);
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    // ── Notifications: mute + history ─────────────────────────────────
    private void ShowOnIsland(NotificationData data, bool keepInHistory, bool bypassDnd = false)
    {
        if (!_enabled || _island == null) return;
        if (data.SourceKey != null && AppSettings.Current.MutedSources.ContainsKey(data.SourceKey)) return;

        if (keepInHistory)
        {
            _history.Add(data);
        }

        // Do Not Disturb (quick button in the notch): keep it in the history, don't drop down.
        if (QuickActions.DoNotDisturb && !bypassDnd) return;

        // Real notifications: the notch's eyes open wide first, then it drops down.
        if (keepInHistory && !_island.IsShowing && _notch?.React("surprise") == true)
        {
            var island = _island;
            var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            wait.Tick += (_, _) => { wait.Stop(); island.ShowNotification(data); };
            wait.Start();
            return;
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
        {
            if (obj is MenuItem { Tag: "notch" } item)   item.IsChecked = _notch?.NotchEnabled == true;
            if (obj is MenuItem { Tag: "maywalk" } walk) walk.IsChecked = AppSettings.Current.MayWalks;
            if (obj is MenuItem { Tag: "fullscreen" } fs) fs.IsChecked = AppSettings.Current.HideInFullscreen;
        }
    }

    /// <summary>Hide the notch during fullscreen videos / games (on), or keep it always (off).</summary>
    private void FullscreenToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.HideInFullscreen = !AppSettings.Current.HideInFullscreen;
        AppSettings.Current.Save();
        UpdateNotchMenu();
    }

    /// <summary>Mây's strolls across the notch on / off.</summary>
    private void MayWalkToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.MayWalks = !AppSettings.Current.MayWalks;
        AppSettings.Current.Save();
        UpdateNotchMenu();
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
        _clips?.Stop();
        _focusWidget?.Shutdown();
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
