using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;
using DynamicIslandNotifier.UI;

namespace DynamicIslandNotifier;

public partial class App : Application
{
    private NotificationListener? _listener;
    private IslandWindow? _island;
    private MediaWatcher? _media;
    private NotchWindow? _notch;
    private ClipboardService? _clips;
    private FocusService? _focus;
    private PomodoroService? _pomo;
    private ScheduleService? _schedule;
    private CameraWatcher? _camera;
    private VolumeWatcher? _volume;
    private BluetoothBatteryWatcher? _headsets;
    private FocusWidget? _focusWidget;
    private PresentationWatcher? _presenting;
    private UpdateChecker? _updates;
    private DeadlineService? _deadlines;
    private ToastSuppressor? _suppressor;
    private bool _firstRun;
    private bool _enabled = true;
    private static System.Threading.Mutex? _singleInstance;
    private BatteryWatcher? _battery;

    // Newest first; shown under "Recent notifications" in the tray menu
    private readonly NotificationHistory _history = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Run by the uninstaller: close the running copy, undo every change to Windows, leave.
        if (e.Args.Contains("--uninstall"))
        {
            UninstallCleanup();
            Shutdown();
            return;
        }

        // Only one copy at a time (e.g. started both by Windows and by hand)
        _singleInstance = new System.Threading.Mutex(true, @"Local\DynamicIslandNotifier.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }
        ListenForQuit();

        // Keep running if something goes wrong, and note it in startup.log
        bool atBoot = e.Args.Contains("--autostart");
        CrashLog.Write($"Started v{UpdateChecker.Show(UpdateChecker.Current)} {(atBoot ? "(with Windows)" : "(by hand)")}, " +
                       $"{TimeSpan.FromMilliseconds(Environment.TickCount64):h\\:mm\\:ss} after the PC started");
        DispatcherUnhandledException += (_, ev) => { CrashLog.Error("UI", ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => { if (ev.ExceptionObject is Exception ex) CrashLog.Error("app", ex); };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) => { CrashLog.Error("task", ev.Exception); ev.SetObserved(); };

        // Register "start with Windows" first, so a later problem can't skip it
        try { _firstRun = AutoStart.EnsureOnStartup(AppSettings.Current); } catch (Exception ex) { CrashLog.Error("autostart", ex); }

        // Right after sign-in Windows is still busy (notifications, media and
        // Bluetooth services may not answer yet), so give it a few seconds.
        if (atBoot) await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(6));

        // Create the overlay pill window (invisible until a notification arrives)
        _island = new IslandWindow();
        _island.Show();

        // Create services
        _suppressor = new ToastSuppressor();
        _listener = new NotificationListener(_suppressor);
        SettingsWindow.Changed += ApplySettings;
        // Signing out / shutting down: put Windows' banners back first
        // (Windows' banners are no longer put back when signing out: see NotificationListener.Stop)

        // Wire notification events to the UI
        _listener.AccessDenied += () => _island.Dispatcher.BeginInvoke(new Action(() => ShowOnIsland(new NotificationData
        {
            AppName = "Dynamic Island", Title = "Chưa được phép đọc thông báo",
            Body = "Bấm để mở Cài đặt → Quyền riêng tư → Thông báo, rồi cho phép Dynamic Island",
            Glyph = "\uE7BA", GlyphColor = "#FFB020",
            OnClick = () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); } catch { } }
        }, keepInHistory: false, bypassDnd: true)));
        _listener.OnNotificationReceived += data => _island.Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowOnIsland(data, keepInHistory: true);
            _notch?.CatCurious();   // Mây looks up at the new message
        }));
        _island.MuteRequested += Mute;
        _island.ActiveChanged += active => _notch?.SetIslandActive(active);

        // Battery: charger plugged in / out, low battery
        _battery = new BatteryWatcher();
        _battery.Notify += data => _island.Dispatcher.BeginInvoke(new Action(() => ShowOnIsland(data, keepInHistory: false)));
        Try("battery", () => _battery.Start());

        // Rebuild the history / muted lists each time the tray menu opens
        if (Current.Resources["TrayIcon"] is Hardcodet.Wpf.TaskbarNotification.TaskbarIcon { ContextMenu: { } menu })
            menu.Opened += (_, _) => { RebuildDynamicMenus(); UpdateNotchMenu(); };

        // The notch: music, tree planting, volume, recent notifications
        _media  = new MediaWatcher { Enabled = AppSettings.Current.MusicEnabled };
        _focus  = new FocusService();
        _pomo   = new PomodoroService(_focus);   // before anything else listens for finished trees
        _clips  = new ClipboardService();
        NotchWindow.ClearDragFolder();   // pictures dragged out last time
        _notch  = new NotchWindow(_media, _focus, _history, _clips);
        _notch.MuteRequested += Mute;
        _notch.UsePomodoro(_pomo);
        _pomo.BreakStarted += (round, minutes, last) => ShowOnIsland(new NotificationData
        {
            AppName = "Pomodoro",
            Title   = last ? $"Xong cả {PomodoroService.Rounds} phiên! Nghỉ dài {minutes} phút" : $"Nghỉ {minutes} phút nhé",
            Body    = last ? "Giỏi lắm! Đi lại, uống nước, cho mắt nghỉ một chút"
                           : $"Xong phiên {round}/{PomodoroService.Rounds} · {Core.TreeCatalog.Get(_focus.CurrentKind).Name} đã lớn · đứng dậy vươn vai",
            Icon    = DynamicIslandNotifier.UI.TreeShape.Render(1, dead: false, _focus.CurrentKind),
            SourceKey = "system:focus",
            OnClick = () => _notch?.OpenTab("focus")
        }, keepInHistory: false);
        _pomo.RoundStarted += round => ShowOnIsland(new NotificationData
        {
            AppName = "Pomodoro", Title = $"Vào phiên {round}/{PomodoroService.Rounds}",
            Body = $"{AppSettings.Current.PomoStudy} phút · một cây mới đang lớn", Glyph = "\uE916", GlyphColor = "#34C759",
            SourceKey = "system:focus", OnClick = () => _notch?.OpenTab("focus")
        }, keepInHistory: false);
        _pomo.CycleDone += () =>
        {
            System.Media.SystemSounds.Asterisk.Play();
            ShowOnIsland(new NotificationData
            {
                AppName = "Pomodoro", Title = "Hoàn thành một vòng Pomodoro 🎉",
                Body = $"{PomodoroService.Rounds} phiên · {PomodoroService.Rounds * AppSettings.Current.PomoStudy} phút học · {PomodoroService.Rounds} cây mới trong vườn",
                Glyph = "\uE735", GlyphColor = "#FFD60A", SourceKey = "system:focus", OnClick = () => _notch?.OpenTab("focus")
            }, keepInHistory: false);
        };
        _notch.Distracted += name => _focusWidget?.Nudge(name);
        _notch.Notice += (title, body) => ShowOnIsland(new NotificationData
        {
            AppName = "Dynamic Island", Title = title, Body = body, Glyph = "\uE790", GlyphColor = "#FF5C7A"
        }, keepInHistory: false);

        // Presenting (slide show / sharing the screen): the notch steps aside, notifications wait
        _presenting = new PresentationWatcher();
        _presenting.Changed += on =>
        {
            _notch.SetPresenting(on);
            if (!on) ReleaseHeld("trong lúc bạn thuyết trình");
        };
        // Hạn chót: reminders a day before, an hour before and when it's due (even with Do Not Disturb)
        _deadlines = new DeadlineService();
        _notch.UseDeadlines(_deadlines);
        _deadlines.Remind += (d, kind) => ShowOnIsland(new NotificationData
        {
            AppName = "Hạn chót",
            Title   = kind switch { "due" => $"Đến hạn: {d.Title}", "1h" => $"Còn 1 giờ: {d.Title}", _ => $"Còn 1 ngày: {d.Title}" },
            Body    = $"{d.Due:HH:mm · dd/MM} · bấm để xem",
            Glyph   = "\uE787",
            GlyphColor = "#B38CFF",
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("deadlines")))
        }, keepInHistory: false, bypassDnd: AppSettings.Current.DeadlinesBypassDnd);

        _deadlines.Summary += next => ShowOnIsland(new NotificationData
        {
            AppName = "Hạn chót",
            Title   = next.Count == 1 ? $"Sắp tới: {next[0].Title}" : $"{next.Count} hạn chót sắp tới",
            Body    = string.Join("  ·  ", next.Select(d => $"{d.Title} ({DeadlineService.Left(d.Due, DateTime.Now)})")),
            Glyph   = "\uE787",
            GlyphColor = "#B38CFF",
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("deadlines")))
        }, keepInHistory: false, bypassDnd: AppSettings.Current.DeadlinesBypassDnd);

        _deadlines.ExamNotice += exams => ShowOnIsland(new NotificationData
        {
            AppName = "Lịch thi",
            Title   = exams.Count == 1 ? $"{exams[0].Title} · {DeadlineService.DaysLeft(exams[0].Due, DateTime.Now)}"
                                       : $"{exams.Count} kỳ thi sắp tới",
            Body    = exams.Count == 1
                ? ((exams[0].Due.Date - DateTime.Today).Days == 0 ? $"Hôm nay lúc {exams[0].Due:HH:mm} · thi tốt nhé!" : $"{exams[0].Due:dd/MM · HH:mm} · ôn dần là kịp")
                : string.Join("  ·  ", exams.Select(e => $"{e.Title} ({DeadlineService.DaysLeft(e.Due, DateTime.Now)})")),
            Glyph   = "\uE7BE",
            GlyphColor = (exams[0].Due.Date - DateTime.Today).Days switch { <= 0 => "#FF5C5C", <= 3 => "#FF9F0A", _ => "#4DA3FF" },
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("deadlines")))
        }, keepInHistory: false, bypassDnd: true);

        // Lịch học: 15 minutes before each class, and at 20:00 what's on tomorrow
        _schedule = new ScheduleService();
        _schedule.Deadlines = _deadlines;
        _notch.UseSchedule(_schedule);
        _schedule.Upcoming += (c, start) => ShowOnIsland(new NotificationData
        {
            AppName = "Lịch học",
            Title   = $"Sắp vào {c.Subject}",
            Body    = $"{start:HH:mm}" + (c.Room != null ? $" · phòng {c.Room}" : "") + $" · còn {Math.Max(1, (int)Math.Round((start - DateTime.Now).TotalMinutes))} phút",
            Glyph   = "\uE8BF", GlyphColor = "#34C7C7",
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("schedule")))
        }, keepInHistory: false);
        _schedule.Tomorrow += list => ShowOnIsland(new NotificationData
        {
            AppName = "Lịch học",
            Title   = $"Mai bạn có {list.Count} buổi học",
            Body    = $"Tiết đầu {ScheduleService.Hm(list[0].Start)} · {list[0].Subject}" + (list[0].Room != null ? $" · {list[0].Room}" : "") + " · nhớ đặt báo thức",
            Glyph   = "\uE8BF", GlyphColor = "#34C7C7",
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("schedule")))
        }, keepInHistory: false);

        // Video call (camera on): the notch goes easy on effects so the call stays smooth
        _camera = new CameraWatcher();
        _camera.Changed += on => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.SetInCall(on && AppSettings.Current.LightDuringCalls)));
        Try("camera", _camera.Start);

        // Sunday evening: the week in one card
        var weekly = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        weekly.Tick += (_, _) => WeeklySummary();
        weekly.Start();

        _focusWidget = new FocusWidget(_focus);   // the small floating timer while a tree grows
        _ = _history.RestoreIconsAsync();   // site logos for notifications kept from last time
        _media.Changed += (state, newTrack) => _notch.Dispatcher.BeginInvoke(new Action(() => _notch.UpdateMedia(state, newTrack)));
        _focus.Finished += session => _island.Dispatcher.BeginInvoke(new Action(() =>
        {
            // In Pomodoro the "rest now" notice says the tree grew; one notice, not two
            if (!(session.Success && _pomo?.OnBreak == true))
                ShowOnIsland(MakeFocusNotification(session), keepInHistory: false);
            ReleaseHeld("trong lúc bạn tập trung");
        }));
        _notch.Show();
        _notch.NotchEnabled = AppSettings.Current.NotchEnabled;

        _volume = new VolumeWatcher();
        _volume.Changed += (level, muted) => _notch.Dispatcher.BeginInvoke(new Action(() => _notch.ShowVolume(level, muted)));
        Try("volume", () => _volume.Start());

        // Battery of connected Bluetooth headphones
        _headsets = new BluetoothBatteryWatcher();
        _headsets.Changed += () => _notch.Dispatcher.BeginInvoke(new Action(() => _notch.SetHeadsets(_headsets.Devices)));
        _headsets.Notify  += data => _island.Dispatcher.BeginInvoke(new Action(() => ShowOnIsland(data, keepInHistory: false)));
        // Earbuds connecting: on the notch itself (the pill widens); a normal notification if it can't
        _headsets.Connected += (d, low) => _notch.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_notch.ShowHeadset(d, low)) ShowOnIsland(BluetoothBatteryWatcher.MakeFor(d, low), keepInHistory: false);
        }));
        Try("bluetooth", () => _headsets.Start());

        UpdateMusicMenuHeader();
        UpdateNotchMenu();

        UpdateAutoStartMenu();

        // New versions on GitHub: a notice on the island, one click installs it
        _updates = new UpdateChecker();
        _updates.Available += v => _island.Dispatcher.BeginInvoke(new Action(() => AnnounceUpdate(v)));
        _updates.Start();

        // The very first time: say that it now starts with Windows, and where to turn that off
        if (_firstRun)
        {
            var hello = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            hello.Tick += (_, _) =>
            {
                hello.Stop();
                ShowOnIsland(new NotificationData
                {
                    AppName = "Dynamic Island", Title = "Tai thỏ sẽ tự mở cùng Windows",
                    Body = "Tắt ở menu khay → Start with Windows · Cài đặt & quyền riêng tư cũng ở đó",
                    Glyph = "\uE7E8", GlyphColor = "#4DA3FF", OnClick = SettingsWindow.ShowOnce
                }, keepInHistory: false, bypassDnd: true);
            };
            hello.Start();
        }

        await StartWithRetry("notifications", _listener.StartAsync);
        await StartWithRetry("music", _media.StartAsync);
        CrashLog.Write("Ready");
        StallWatch.Start(Dispatcher);   // logs any moment the screen freezes > 150 ms
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
    private int _held;
    private bool HoldForFocus => _focus?.IsRunning == true && DeepFocus.Enabled;
    private bool HoldForPresenting => _presenting?.IsPresenting == true;

    /// <summary>"3 thông báo trong lúc bạn tập trung" once the quiet time is over.</summary>
    private void ReleaseHeld(string when)
    {
        if (_held == 0 || HoldForFocus || HoldForPresenting) return;
        int n = _held;
        _held = 0;
        ShowOnIsland(new NotificationData
        {
            AppName = "Dynamic Island",
            Title   = $"{n} thông báo {when}",
            Body    = "Bấm để xem trong tab Thông báo",
            Glyph   = "\uEA8F",
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("notifications")))
        }, keepInHistory: false, bypassDnd: true);
    }

    private void ShowOnIsland(NotificationData data, bool keepInHistory, bool bypassDnd = false)
    {
        if (!_enabled || _island == null) return;
        if (data.SourceKey != null && AppSettings.Current.MutedSources.ContainsKey(data.SourceKey))
        {
            CrashLog.Write($"Not shown (muted in the tray menu): {data.AppName}");
            return;
        }

        if (keepInHistory)
        {
            _history.Add(data);
        }

        // Do Not Disturb (quick button in the notch): keep it in the history, don't drop down.
        if (QuickActions.DoNotDisturb && !bypassDnd)
        {
            if (keepInHistory) CrashLog.Write($"Not shown (Do Not Disturb is on): {data.AppName}");
            return;
        }

        // Deep focus (while a tree grows) or presenting: keep it in the history, show a summary after.
        if (keepInHistory && !bypassDnd && (HoldForFocus || HoldForPresenting))
        {
            _held++;
            CrashLog.Write($"Held until later ({(HoldForPresenting ? "presenting" : "deep focus")}): {data.AppName}");
            return;
        }

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

    /// <summary>Sunday from 19:00, once: hours of focus, trees, streak and the best day of the week.</summary>
    private void WeeklySummary()
    {
        var now = DateTime.Now;
        if (_focus == null || now.DayOfWeek != DayOfWeek.Sunday || now.Hour < 19) return;
        if (AppSettings.Current.WeeklySummaryDay?.Date == now.Date) return;
        AppSettings.Current.WeeklySummaryDay = now.Date;
        AppSettings.Current.Save();

        var monday = now.Date.AddDays(-6);
        var days = _focus.LastDays(7);
        int minutes = days.Sum(d => d.Minutes), trees = days.Sum(d => d.Trees);
        var best = days.OrderByDescending(d => d.Minutes).First();
        string[] names = { "Chủ nhật", "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };
        int classes = AppSettings.Current.Schedule.Count(c => c.Date == null || (c.Date >= monday && c.Date <= now.Date));

        var parts = new List<string>();
        if (minutes > 0) parts.Add($"{(minutes >= 60 ? $"{minutes / 60} giờ {minutes % 60:00} phút" : $"{minutes} phút")} tập trung");
        if (trees > 0) parts.Add($"{trees} cây");
        int streak = _focus.Streak();
        if (streak >= 2) parts.Add($"chuỗi {streak} ngày");
        if (best.Minutes > 0) parts.Add($"chăm nhất: {names[(int)best.Day.DayOfWeek]}");
        if (classes > 0) parts.Add($"{classes} buổi học");

        ShowOnIsland(new NotificationData
        {
            AppName = "Tổng kết tuần",
            Title   = minutes > 0 ? "Tuần này của bạn" : "Tuần này chưa trồng cây nào",
            Body    = parts.Count > 0 ? string.Join(" · ", parts) : "Tuần sau thử một vòng Pomodoro nhé, Mây chờ đó!",
            Glyph   = "\uE9D2",
            GlyphColor = "#34C759",
            OnClick = () => _notch?.Dispatcher.BeginInvoke(new Action(() => _notch.OpenTab("focus")))
        }, keepInHistory: true, bypassDnd: false);
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
            if (obj is MenuItem { Tag: "showcat" } cat) cat.IsChecked = AppSettings.Current.ShowCat;
            if (obj is MenuItem { Tag: "pin" } pin) pin.IsChecked = _notch?.Pinned == true;
            if (obj is MenuItem { Tag: "banners" } banners) banners.IsChecked = ToastSuppressor.Enabled;
            if (obj is MenuItem { Tag: "maywalk" } walk2) walk2.IsEnabled = AppSettings.Current.ShowCat;
            if (obj is MenuItem { Tag: "fullscreen" } fs) fs.IsChecked = AppSettings.Current.HideInFullscreen;
            if (obj is MenuItem { Tag: "ambient" } amb)
            {
                string? why = _notch?.AmbientReason;
                foreach (var sub in amb.Items)
                {
                    if (sub is MenuItem { Tag: "why" } info) { info.Header = why != null ? $"Đang giảm: {why}" : ""; info.Visibility = why != null ? Visibility.Visible : Visibility.Collapsed; }
                    else if (sub is Separator { Tag: "why" } line) line.Visibility = why != null ? Visibility.Visible : Visibility.Collapsed;
                    else if (sub is MenuItem { Tag: string mode } choice) choice.IsChecked = AppSettings.Current.Ambient == mode;
                }
            }
        }
    }

    /// <summary>Hide the notch during fullscreen videos / games (on), or keep it always (off).</summary>
    private void FullscreenToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.HideInFullscreen = !AppSettings.Current.HideInFullscreen;
        AppSettings.Current.Save();
        UpdateNotchMenu();
    }

    /// <summary>
    /// Hide Windows' own banner for apps shown on the island. Unticking it puts
    /// every banner back (do this before deleting the app if it was installed
    /// with the PowerShell command; the installer's uninstall does it itself).
    /// </summary>
    private void Banners_Click(object sender, RoutedEventArgs e)
    {
        ToastSuppressor.Enabled = !ToastSuppressor.Enabled;
        if (ToastSuppressor.Enabled) _suppressor?.HideKnownApps();
        else _suppressor?.RestoreAll();
        UpdateNotchMenu();
    }

    /// <summary>Giữ im Dynamic: pin the panel open (from the tray it also opens it).</summary>
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _notch?.SetPinned(!_notch.Pinned);
        UpdateNotchMenu();
    }

    /// <summary>Show or hide the cat everywhere (hidden, she costs nothing).</summary>
    private void ShowCat_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ShowCat = !AppSettings.Current.ShowCat;
        AppSettings.Current.Save();
        _notch?.ApplyCatVisibility();
        UpdateNotchMenu();
    }

    /// <summary>Loang màu: full / light / off.</summary>
    private void Ambient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string mode }) return;
        AppSettings.Current.Ambient = mode;
        AppSettings.Current.Save();
        _notch?.ReapplyAmbient();
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
        if (AutoStart.IsActive) AutoStart.Disable();
        else                    AutoStart.EnableByUser();
        UpdateAutoStartMenu();
    }

    private void UpdateAutoStartMenu()
    {
        var tray = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon?)Current.Resources["TrayIcon"];
        if (tray?.ContextMenu == null) return;
        foreach (var obj in tray.ContextMenu.Items)
            if (obj is MenuItem { Tag: "autostart" } item)
                item.IsChecked = AutoStart.IsActive;   // off when switched off in Task Manager (left as you chose)
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
        NotchWindow.ClearDragFolder();
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

    // ── Uninstall ─────────────────────────────────────────────────────
    private const string QuitSignal = @"Local\DynamicIslandNotifier.Quit";

    /// <summary>The uninstaller (or another copy) can ask this one to quit cleanly.</summary>
    private void ListenForQuit()
    {
        var signal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, QuitSignal);
        var t = new System.Threading.Thread(() =>
        {
            signal.WaitOne();
            Dispatcher.BeginInvoke(new Action(() => Current.Shutdown()));
        }) { IsBackground = true, Name = "quit signal" };
        t.Start();
    }

    /// <summary>
    /// Before the app is removed: ask the running copy to quit (it puts Windows'
    /// banners back as it closes), then make sure of that here too, and stop
    /// starting with Windows. Your settings and history files are left alone.
    /// </summary>
    private static void UninstallCleanup()
    {
        try
        {
            if (System.Threading.EventWaitHandle.TryOpenExisting(QuitSignal, out var signal))
            {
                signal.Set();
                using var running = new System.Threading.Mutex(false, @"Local\DynamicIslandNotifier.SingleInstance");
                try { if (running.WaitOne(TimeSpan.FromSeconds(8))) running.ReleaseMutex(); }
                catch (System.Threading.AbandonedMutexException) { }
            }
            new ToastSuppressor().RestoreAll();
            AutoStart.Disable();
            CrashLog.Write("Uninstall: Windows settings restored, start with Windows off");
        }
        catch (Exception ex) { CrashLog.Error("uninstall", ex); }
    }

    // ── Updates ───────────────────────────────────────────────────────
    private bool _installing;

    /// <summary>A note on the island; clicking it shows what's new and the install button.</summary>
    private void AnnounceUpdate(Version v)
    {
        ShowOnIsland(new NotificationData
        {
            AppName = "Dynamic Island",
            Title   = $"Có bản mới v{UpdateChecker.Show(v)}",
            Body    = "Bấm để xem có gì mới và cập nhật",
            Glyph   = "\uE896",
            GlyphColor = "#4DA3FF",
            OnClick = () => ShowUpdate(v)
        }, keepInHistory: false, bypassDnd: true);
    }

    private void ShowUpdate(Version v)
    {
        UpdateWindow? window = null;
        window = UpdateWindow.ShowFor(v, async () =>
        {
            if (_installing) return;
            _installing = true;
            var error = await UpdateChecker.InstallAsync(v);
            if (error == null) { Current.Shutdown(); return; }
            _installing = false;
            window?.Failed(error);
        });
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updates == null) return;
        var (status, _) = await _updates.CheckAsync(manual: true);
        if (status == UpdateStatus.UpToDate)
            ShowOnIsland(new NotificationData
            {
                AppName = "Dynamic Island", Title = "Bạn đang dùng bản mới nhất",
                Body = $"v{UpdateChecker.Show(UpdateChecker.Current)}", Glyph = "\uE73E", GlyphColor = "#34C759"
            }, keepInHistory: false, bypassDnd: true);
        else if (status == UpdateStatus.Failed)
            ShowOnIsland(new NotificationData
            {
                AppName = "Dynamic Island", Title = "Không kiểm tra được bản mới",
                Body = "Kiểm tra mạng rồi thử lại sau", Glyph = "\uE783", GlyphColor = "#FFB020"
            }, keepInHistory: false, bypassDnd: true);
    }

    // ── Settings & privacy ────────────────────────────────────────────
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.ShowOnce();

    private void ApplySettings()
    {
        if (!ToastSuppressor.Enabled) _suppressor?.RestoreAll();   // banners back to how Windows had them
        else _suppressor?.HideKnownApps();
        _notch?.ApplySettings();
        _notch?.SetInCall(_camera?.InUse == true && AppSettings.Current.LightDuringCalls);
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Current.Shutdown();
    }
}
