using System.Windows;
using System.Windows.Controls;
using DynamicIslandNotifier.Services;

namespace DynamicIslandNotifier;

public partial class App : Application
{
    private NotificationListener? _listener;
    private IslandWindow? _island;
    private MediaWatcher? _media;
    private MusicWindow? _music;
    private bool _enabled = true;
    private static System.Threading.Mutex? _singleInstance;

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
        _listener.OnNotificationReceived += data =>
        {
            _island.Dispatcher.Invoke(() => _island.ShowNotification(data));
        };

        // Music: a separate, draggable player (Spotify, YouTube, ...)
        _media = new MediaWatcher { Enabled = AppSettings.Current.MusicEnabled };
        _music = new MusicWindow(_media);
        _media.Changed += (state, newTrack) =>
        {
            _music.Dispatcher.Invoke(() => _music.UpdateState(state, newTrack));
        };
        UpdateMusicMenuHeader();

        // Start with Windows: on by default from the first run
        AutoStart.EnsureOnStartup(AppSettings.Current);
        UpdateAutoStartMenu();

        await _listener.StartAsync();
        await _media.StartAsync();
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
                item.Header = _media?.Enabled == true ? "Music player: On" : "Music player: Off";
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
