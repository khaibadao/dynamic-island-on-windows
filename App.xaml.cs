using System.Windows;
using System.Windows.Controls;
using DynamicIslandNotifier.Services;

namespace DynamicIslandNotifier;

public partial class App : Application
{
    private NotificationListener? _listener;
    private IslandWindow? _island;
    private MediaWatcher? _media;
    private bool _enabled = true;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Music: show the song when a new track starts (Spotify, YouTube, ...)
        _media = new MediaWatcher();
        _island.Media = _media;
        _media.TrackChanged += data =>
        {
            if (_enabled) _island.Dispatcher.Invoke(() => _island.ShowNotification(data));
        };
        _media.PlayingChanged += playing => _island.SetPlaying(playing);

        await _listener.StartAsync();
        await _media.StartAsync();
    }

    private void MusicToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_media == null) return;
        _media.Enabled = !_media.Enabled;
        if (sender is MenuItem item)
            item.Header = _media.Enabled ? "Music: On" : "Music: Off";
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
