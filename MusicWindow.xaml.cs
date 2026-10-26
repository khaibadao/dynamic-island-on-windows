using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;

namespace DynamicIslandNotifier;

/// <summary>
/// The music player. Sits as a small draggable pill (album art + sound bars)
/// anywhere on screen; hovering it opens the full player with a seek bar,
/// ⏮ ⏯ ⏭ and a volume slider for the playing app.
/// </summary>
public partial class MusicWindow : Window
{
    private readonly MediaWatcher _media;

    private readonly DispatcherTimer _hoverOpenTimer   = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _leaveCloseTimer  = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _autoCloseTimer   = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _progressTimer    = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private bool _ready;           // controls exist (slider events fire during InitializeComponent)
    private bool _expanded;
    private bool _playing;
    private bool _seeking;         // user is dragging the seek bar
    private bool _settingVolume;   // we're moving the volume slider ourselves
    private string? _appId;

    // Top-left corner of the small pill. The full player opens from here.
    private double _anchorLeft, _anchorTop;

    public MusicWindow(MediaWatcher media)
    {
        InitializeComponent();
        _media = media;
        _ready = true;

        _hoverOpenTimer.Tick  += (_, _) => { _hoverOpenTimer.Stop();  if (MiniView.IsMouseOver) Expand(auto: false); };
        _leaveCloseTimer.Tick += (_, _) => { _leaveCloseTimer.Stop(); if (!FullView.IsMouseOver && !_seeking) Collapse(); };
        _autoCloseTimer.Tick  += (_, _) => { _autoCloseTimer.Stop();  if (!FullView.IsMouseOver) Collapse(); };
        _progressTimer.Tick   += (_, _) => UpdateProgress();

        RestorePosition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeInterop.MakeOverlay(this);   // never steals focus, no taskbar/Alt-Tab entry
    }

    // ── State from the media watcher ──────────────────────────────────
    public void UpdateState(MediaState state, bool newTrack)
    {
        if (!state.HasSession)
        {
            Collapse();
            SetBarsPlaying(false);
            Hide();
            return;
        }

        _appId   = state.AppId;
        _playing = state.IsPlaying;

        TitleText.Text   = state.Title;
        ArtistText.Text  = state.Artist;
        AppNameText.Text = state.AppName;
        MiniArt.Source   = state.Art;
        FullArt.Source   = state.Art;
        PlayPauseGlyph.Text = _playing ? "" : "";   // pause / play icon
        SetBarsPlaying(_playing);

        if (!IsVisible)
        {
            Left = _anchorLeft;
            Top  = _anchorTop;
            Show();
            NativeInterop.ForceTopMost(this);
        }

        // A new song: open the player for a few seconds, then shrink back.
        if (newTrack && !_expanded) Expand(auto: true);
        if (_expanded) UpdateProgress();
    }

    // ── Open / close ──────────────────────────────────────────────────
    private void Expand(bool auto)
    {
        _hoverOpenTimer.Stop();
        if (_expanded) return;
        _expanded = true;

        MiniView.Visibility = Visibility.Collapsed;
        FullView.Visibility = Visibility.Visible;

        // Open from the pill's spot, but keep the player fully on screen.
        FullView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = FullView.DesiredSize.Width  + 8;
        double h = FullView.DesiredSize.Height + 8;
        Left = Clamp(_anchorLeft, SystemParameters.VirtualScreenLeft,
                     SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - w);
        Top  = Clamp(_anchorTop,  SystemParameters.VirtualScreenTop,
                     SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - h);

        // Small "pop" animation
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = TimeSpan.FromMilliseconds(180);
        FullView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        FullScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease });
        FullScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, dur) { EasingFunction = ease });

        LoadVolume();
        UpdateProgress();
        _progressTimer.Start();

        if (auto) _autoCloseTimer.Start();
        else      _leaveCloseTimer.Start();   // closes if the mouse isn't on the player
        NativeInterop.ForceTopMost(this);
    }

    private void Collapse()
    {
        _autoCloseTimer.Stop();
        _leaveCloseTimer.Stop();
        _progressTimer.Stop();
        if (!_expanded) return;
        _expanded = false;
        _seeking  = false;

        FullView.Visibility = Visibility.Collapsed;
        MiniView.Visibility = Visibility.Visible;
        Left = _anchorLeft;
        Top  = _anchorTop;
    }

    // ── Mouse: hover to open, drag to move ────────────────────────────
    private void Mini_MouseEnter(object sender, MouseEventArgs e) => _hoverOpenTimer.Start();
    private void Mini_MouseLeave(object sender, MouseEventArgs e) => _hoverOpenTimer.Stop();

    private void Full_MouseEnter(object sender, MouseEventArgs e)
    {
        _leaveCloseTimer.Stop();
        _autoCloseTimer.Stop();
    }

    private void Full_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_seeking) _leaveCloseTimer.Start();
    }

    /// <summary>
    /// Press and drag anywhere on the pill or the player's background to move
    /// it. Buttons and sliders handle their own clicks, so they don't drag.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _hoverOpenTimer.Stop();
        _autoCloseTimer.Stop();

        double startLeft = Left, startTop = Top;
        try { DragMove(); } catch (InvalidOperationException) { return; }

        bool moved = Math.Abs(Left - startLeft) > 2 || Math.Abs(Top - startTop) > 2;
        if (moved)
        {
            _anchorLeft = Left;
            _anchorTop  = Top;
            SavePosition();
        }
        else if (!_expanded)
        {
            Expand(auto: false);   // a plain click on the pill opens the player
        }
    }

    // ── Buttons ───────────────────────────────────────────────────────
    private void Prev_Click(object sender, RoutedEventArgs e) => _media.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _media.Next();

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _media.PlayPause();
        // Flip right away; the watcher confirms it a moment later.
        _playing = !_playing;
        PlayPauseGlyph.Text = _playing ? "" : "";
        SetBarsPlaying(_playing);
    }

    // ── Seek bar ──────────────────────────────────────────────────────
    private void UpdateProgress()
    {
        var (pos, dur) = _media.GetTimeline();
        if (dur <= TimeSpan.Zero)
        {
            SeekRow.Visibility = Visibility.Collapsed;   // player doesn't report a timeline
            return;
        }

        SeekRow.Visibility = Visibility.Visible;
        DurationText.Text  = Format(dur);
        if (_seeking) return;

        SeekSlider.Maximum = dur.TotalSeconds;
        SeekSlider.Value   = pos.TotalSeconds;
        PositionText.Text  = Format(pos);
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
        _leaveCloseTimer.Stop();
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        _media.Seek(TimeSpan.FromSeconds(SeekSlider.Value));
        if (!FullView.IsMouseOver) _leaveCloseTimer.Start();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready && _seeking) PositionText.Text = Format(TimeSpan.FromSeconds(e.NewValue));
    }

    // ── Volume (only the playing app) ─────────────────────────────────
    private void LoadVolume()
    {
        var v = AppVolume.Get(_appId);
        VolumeRow.IsEnabled = v != null;
        VolumeRow.Opacity   = v != null ? 1.0 : 0.4;
        VolumeRow.ToolTip   = v != null ? null : "App này chưa phát âm thanh";

        _settingVolume = true;
        VolumeSlider.Value = Math.Round((v ?? 1f) * 100);
        _settingVolume = false;
        ShowVolume(VolumeSlider.Value);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        ShowVolume(e.NewValue);
        if (!_settingVolume) AppVolume.Set(_appId, (float)(e.NewValue / 100.0));
    }

    private void ShowVolume(double value)
    {
        VolumeText.Text  = Math.Round(value).ToString();
        VolumeGlyph.Text = value < 1 ? ""      // muted
                         : value < 34 ? ""     // low
                         : value < 67 ? ""     // medium
                         : "";                 // high
    }

    // ── Sound bars ────────────────────────────────────────────────────
    private void SetBarsPlaying(bool playing)
    {
        var bars   = new[] { Bar1, Bar2, Bar3, Bar4 };
        var speeds = new[] { 420, 560, 360, 500 };   // different speeds so they look lively
        var rest   = new[] { 0.35, 0.6, 0.45, 0.3 };

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i].RenderTransform is not ScaleTransform scale) continue;
            if (playing)
            {
                var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(speeds[i]))
                {
                    AutoReverse    = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = rest[i];
            }
        }
    }

    // ── Position memory ───────────────────────────────────────────────
    private void RestorePosition()
    {
        var s = AppSettings.Current;
        var area = SystemParameters.WorkArea;

        // Default: bottom-right corner, just above the taskbar.
        _anchorLeft = s.MusicLeft ?? area.Right  - 140;
        _anchorTop  = s.MusicTop  ?? area.Bottom - 70;

        // If the saved spot is off-screen now (monitor unplugged), bring it back.
        _anchorLeft = Clamp(_anchorLeft, SystemParameters.VirtualScreenLeft,
                            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60);
        _anchorTop  = Clamp(_anchorTop,  SystemParameters.VirtualScreenTop,
                            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50);
        Left = _anchorLeft;
        Top  = _anchorTop;
    }

    private void SavePosition()
    {
        AppSettings.Current.MusicLeft = _anchorLeft;
        AppSettings.Current.MusicTop  = _anchorTop;
        AppSettings.Current.Save();
    }

    // ── Helpers ───────────────────────────────────────────────────────
    private static double Clamp(double v, double min, double max) => max < min ? min : Math.Clamp(v, min, max);

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
