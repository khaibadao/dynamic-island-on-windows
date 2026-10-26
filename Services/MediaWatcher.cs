namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using DynamicIslandNotifier.Core;

using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

/// <summary>
/// Watches the system "now playing" session (Spotify, YouTube in a browser,
/// Apple Music, ...) through Windows' media transport controls, the same
/// source as the media flyout shown when you press a volume key.
/// Raises <see cref="TrackChanged"/> when a new song starts playing.
/// </summary>
public sealed class MediaWatcher
{
    private MediaManager? _manager;
    private MediaSession? _session;
    private string _lastTrackKey = "";
    private CancellationTokenSource? _debounce;

    /// <summary>A new track started playing. Raised on a background thread.</summary>
    public event Action<NotificationData>? TrackChanged;

    /// <summary>Play/pause state changed. Raised on a background thread.</summary>
    public event Action<bool>? PlayingChanged;

    public bool Enabled { get; set; } = true;

    public bool IsPlaying =>
        _session?.GetPlaybackInfo()?.PlaybackStatus == PlaybackStatus.Playing;

    public async Task StartAsync()
    {
        try
        {
            _manager = await MediaManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => AttachToCurrentSession();
            AttachToCurrentSession();
        }
        catch (Exception ex)
        {
            DebugLog.Note($"MediaWatcher start failed: {ex.Message}");
        }
    }

    // ── Controls (used by the island's ⏮ ⏯ ⏭ buttons) ────────────────
    public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next()      { try { if (_session != null) await _session.TrySkipNextAsync();       } catch { } }
    public async void Previous()  { try { if (_session != null) await _session.TrySkipPreviousAsync();   } catch { } }

    // ── Session tracking ──────────────────────────────────────────────
    private void AttachToCurrentSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }

        _session = _manager?.GetCurrentSession();
        if (_session == null) return;

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged    += OnPlaybackInfoChanged;
        ScheduleTrackCheck();
    }

    private void OnPlaybackInfoChanged(MediaSession sender, PlaybackInfoChangedEventArgs args)
    {
        PlayingChanged?.Invoke(IsPlaying);
        ScheduleTrackCheck();   // a track may only "start" when playback resumes
    }

    private void OnMediaPropertiesChanged(MediaSession sender, MediaPropertiesChangedEventArgs args)
        => ScheduleTrackCheck();

    /// <summary>
    /// Players fire several property-change events per song (title first,
    /// album art a moment later), so wait briefly and read everything once.
    /// </summary>
    private void ScheduleTrackCheck()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, cts.Token);
                await CheckTrackAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { DebugLog.Note($"MediaWatcher: {ex.Message}"); }
        });
    }

    private async Task CheckTrackAsync()
    {
        var session = _session;
        if (!Enabled || session == null || !IsPlaying) return;

        var props = await session.TryGetMediaPropertiesAsync();
        if (props == null || string.IsNullOrWhiteSpace(props.Title)) return;

        var key = $"{session.SourceAppUserModelId}|{props.Title}|{props.Artist}";
        if (key == _lastTrackKey) return;   // same song, nothing new to show
        _lastTrackKey = key;

        TrackChanged?.Invoke(new NotificationData
        {
            AppName        = AppNameFor(session.SourceAppUserModelId),
            Title          = props.Title,
            Body           = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumTitle : props.Artist,
            Icon           = await LoadThumbnailAsync(props.Thumbnail),
            AppUserModelId = session.SourceAppUserModelId,
            IsMedia        = true
        });
    }

    private static string AppNameFor(string? appId)
    {
        if (string.IsNullOrEmpty(appId)) return "Music";
        if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (appId.Contains("Edge",    StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (appId.Contains("Chrome",  StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (appId.Contains("Music",   StringComparison.OrdinalIgnoreCase)) return "Music";
        return Path.GetFileNameWithoutExtension(appId);
    }

    private static async Task<BitmapSource?> LoadThumbnailAsync(IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            using var stream = await thumb.OpenReadAsync();
            if (stream.Size == 0) return null;

            var bytes  = new byte[stream.Size];
            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            reader.DetachStream();

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
