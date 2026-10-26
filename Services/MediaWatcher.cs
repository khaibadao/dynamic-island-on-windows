namespace DynamicIslandNotifier.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

using MediaManager   = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession   = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

/// <summary>Snapshot of what the system is playing.</summary>
public sealed record MediaState(
    bool HasSession,
    bool IsPlaying,
    string Title,
    string Artist,
    BitmapSource? Art,
    string? AppId,
    string AppName)
{
    public static readonly MediaState None = new(false, false, "", "", null, null, "");
}

/// <summary>
/// Watches the system "now playing" session (Spotify, YouTube in a browser,
/// Apple Music, ...) through Windows' media transport controls, the same
/// source as the media flyout shown when you press a volume key.
/// </summary>
public sealed class MediaWatcher
{
    private MediaManager? _manager;
    private MediaSession? _session;
    private CancellationTokenSource? _debounce;

    private string _lastTrackKey = "";
    private BitmapSource? _lastArt;
    private bool _hadSession;

    /// <summary>
    /// Raised (on a background thread) whenever the playing state or track
    /// changes. <c>newTrack</c> is true when a different song started playing.
    /// </summary>
    public event Action<MediaState, bool>? Changed;

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; ScheduleCheck(); }
    }

    public string? CurrentAppId => _session?.SourceAppUserModelId;

    public bool IsPlaying
    {
        get
        {
            try { return _session?.GetPlaybackInfo()?.PlaybackStatus == PlaybackStatus.Playing; }
            catch { return false; }
        }
    }

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

    // ── Controls ──────────────────────────────────────────────────────
    public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next()      { try { if (_session != null) await _session.TrySkipNextAsync();       } catch { } }
    public async void Previous()  { try { if (_session != null) await _session.TrySkipPreviousAsync();   } catch { } }

    /// <summary>Current position and total length. Duration is zero if the player doesn't report it.</summary>
    public (TimeSpan Position, TimeSpan Duration) GetTimeline()
    {
        var s = _session;
        if (s == null) return (TimeSpan.Zero, TimeSpan.Zero);
        try
        {
            var t   = s.GetTimelineProperties();
            var dur = t.EndTime - t.StartTime;
            var pos = t.Position - t.StartTime;
            if (dur <= TimeSpan.Zero) return (TimeSpan.Zero, TimeSpan.Zero);

            // Players report the position only now and then; extrapolate while playing.
            if (IsPlaying)
            {
                var elapsed = DateTimeOffset.Now - t.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1)) pos += elapsed;
            }

            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > dur) pos = dur;
            return (pos, dur);
        }
        catch
        {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    public async void Seek(TimeSpan position)
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var start = s.GetTimelineProperties().StartTime;
            await s.TryChangePlaybackPositionAsync((start + position).Ticks);
        }
        catch { }
    }

    // ── Session tracking ──────────────────────────────────────────────
    private void AttachToCurrentSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged    -= OnSessionEvent;
        }

        _session = _manager?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnSessionEvent;
            _session.PlaybackInfoChanged    += OnSessionEvent;
        }
        ScheduleCheck();
    }

    private void OnSessionEvent(MediaSession sender, object args) => ScheduleCheck();

    /// <summary>
    /// Players fire several events per song (title first, album art a moment
    /// later), so wait briefly and read everything once.
    /// </summary>
    private void ScheduleCheck()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                await CheckAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { DebugLog.Note($"MediaWatcher: {ex.Message}"); }
        });
    }

    private async Task CheckAsync()
    {
        var session = _session;

        PlaybackStatus? status = null;
        try { status = session?.GetPlaybackInfo()?.PlaybackStatus; } catch { }

        // No player, or the player closed: hide.
        if (!Enabled || session == null || status is null or PlaybackStatus.Closed)
        {
            _lastTrackKey = "";
            _lastArt      = null;
            if (_hadSession) { _hadSession = false; Changed?.Invoke(MediaState.None, false); }
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var title = props?.Title ?? "";
        if (string.IsNullOrWhiteSpace(title)) return;   // player opened but nothing loaded yet

        var artist  = string.IsNullOrWhiteSpace(props!.Artist) ? props.AlbumTitle ?? "" : props.Artist;
        var playing = status == PlaybackStatus.Playing;
        var appId   = session.SourceAppUserModelId;

        var key      = $"{appId}|{title}|{artist}";
        var newTrack = key != _lastTrackKey;
        if (newTrack || _lastArt == null)
            _lastArt = await LoadThumbnailAsync(props.Thumbnail);
        _lastTrackKey = key;
        _hadSession   = true;

        Changed?.Invoke(new MediaState(true, playing, title, artist, _lastArt, appId, AppNameFor(appId)),
                        newTrack && playing);
    }

    private static string AppNameFor(string? appId)
    {
        if (string.IsNullOrEmpty(appId)) return "Music";
        if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (appId.Contains("Edge",    StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (appId.Contains("Chrome",  StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (appId.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
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
