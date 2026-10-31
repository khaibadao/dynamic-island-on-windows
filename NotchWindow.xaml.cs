using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;
using DynamicIslandNotifier.UI;
using Windows.System.Power;

namespace DynamicIslandNotifier;

/// <summary>
/// The notch: a small black bar at the top centre of the screen, like the
/// iPhone's. It shows what's going on (music, focus timer) in miniature;
/// hovering it opens everything: music player, tree planting, recent
/// notifications. It hides during fullscreen apps and while a notification
/// is dropping down from the same spot.
/// </summary>
public partial class NotchWindow : Window
{
    private readonly MediaWatcher _media;
    private readonly FocusService _focus;
    private readonly Func<IReadOnlyList<(DateTime Time, NotificationData Data)>> _recent;

    private readonly DispatcherTimer _openTimer       = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _closeTimer      = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _tickTimer       = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _volumeTimer     = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _peekTimer       = new() { Interval = TimeSpan.FromMilliseconds(3500) };
    private readonly DispatcherTimer _giveUpTimer     = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _ready;           // controls exist (slider events fire during InitializeComponent)
    private bool _expanded;
    private bool _seeking;
    private bool _settingVolume;
    private bool _islandActive;
    private bool _fullscreen;
    private bool _enabled = true;
    private bool _hasMusic;
    private bool _playing;
    private bool _confirmGiveUp;
    private string? _appId;
    private int _selectedMinutes;

    private static readonly string[] Weekdays =
        { "Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy" };

    public NotchWindow(MediaWatcher media, FocusService focus,
                       Func<IReadOnlyList<(DateTime Time, NotificationData Data)>> recent)
    {
        InitializeComponent();
        _media  = media;
        _focus  = focus;
        _recent = recent;
        _ready  = true;

        _openTimer.Tick   += (_, _) => { _openTimer.Stop();   if (Pill.IsMouseOver || NativeInterop.IsCursorOver(this)) Expand(); };
        _closeTimer.Tick  += (_, _) => { _closeTimer.Stop();  if (!_seeking && !NativeInterop.IsCursorOver(this)) Collapse(); };
        _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); VolumeView.Visibility = Visibility.Collapsed; if (!_expanded) Pill.Visibility = Visibility.Visible; };
        _peekTimer.Tick   += (_, _) => { _peekTimer.Stop();   PillText.Visibility = Visibility.Collapsed; };
        _giveUpTimer.Tick += (_, _) => { _giveUpTimer.Stop(); ResetGiveUp(); };
        _tickTimer.Tick   += (_, _) => OnTick();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();

        _selectedMinutes = FocusService.Presets.Contains(AppSettings.Current.LastFocusMinutes)
            ? AppSettings.Current.LastFocusMinutes : 25;
        BuildPresets();

        _focus.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateFocusUi));

        SizeChanged += (_, _) => Recenter();
        Loaded += (_, _) =>
        {
            Recenter();
            UpdateFocusUi();
            OnTick();
            _tickTimer.Start();
            _fullscreenTimer.Start();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeInterop.MakeOverlay(this);   // never steals focus, no taskbar/Alt-Tab entry
    }

    // ── Visibility ────────────────────────────────────────────────────
    /// <summary>Turned on/off from the tray menu.</summary>
    public bool NotchEnabled
    {
        get => _enabled;
        set { _enabled = value; UpdateVisibility(); }
    }

    /// <summary>A notification is using the top of the screen: step aside.</summary>
    public void SetIslandActive(bool active)
    {
        _islandActive = active;
        UpdateVisibility();
    }

    private void CheckFullscreen()
    {
        bool fs = NativeInterop.IsForegroundFullscreenOnPrimary();
        if (fs == _fullscreen) return;
        _fullscreen = fs;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool show = _enabled && !_islandActive && !_fullscreen;
        if (show && !IsVisible)
        {
            Show();
            Recenter();
            NativeInterop.ForceTopMost(this);
        }
        else if (!show && IsVisible)
        {
            Collapse();
            Hide();
        }
    }

    private void Recenter()
    {
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top  = 0;
    }

    // ── Open / close ──────────────────────────────────────────────────
    private void Pill_MouseEnter(object sender, MouseEventArgs e) => _openTimer.Start();
    private void Pill_MouseLeave(object sender, MouseEventArgs e) => _openTimer.Stop();
    private void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Expand();

    private void Expanded_MouseEnter(object sender, MouseEventArgs e) => _closeTimer.Stop();
    private void Expanded_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_seeking) _closeTimer.Start();
    }

    private void Expand()
    {
        _openTimer.Stop();
        if (_expanded || !IsVisible) return;
        _expanded = true;

        _volumeTimer.Stop();
        VolumeView.Visibility = Visibility.Collapsed;
        Pill.Visibility       = Visibility.Collapsed;
        Expanded.Visibility   = Visibility.Visible;

        RebuildRecent();
        UpdateClock();
        UpdateFocusUi();
        if (_hasMusic) { LoadAppVolume(); UpdateProgress(); }

        // Drop down from the notch
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = TimeSpan.FromMilliseconds(200);
        Expanded.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, dur) { EasingFunction = ease });
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, dur) { EasingFunction = ease });

        _closeTimer.Start();   // closes again if the mouse just passed by
    }

    private void Collapse()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (!_expanded) return;
        _expanded = false;
        _seeking  = false;

        Expanded.Visibility = Visibility.Collapsed;
        Pill.Visibility     = Visibility.Visible;
        ResetGiveUp();
    }

    // ── Every second ──────────────────────────────────────────────────
    private void OnTick()
    {
        if (_focus.IsRunning) UpdateFocusTick();
        if (_expanded)
        {
            UpdateClock();
            if (_hasMusic) UpdateProgress();

            // Safety net in case MouseLeave never came: close once the pointer is gone.
            if (!_seeking && !_closeTimer.IsEnabled && !NativeInterop.IsCursorOver(this))
                _closeTimer.Start();
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = $"{now:HH:mm}   {Weekdays[(int)now.DayOfWeek]}, {now:dd/MM}";

        try
        {
            if (PowerManager.BatteryStatus == BatteryStatus.NotPresent)
            {
                BatteryPanel.Visibility = Visibility.Collapsed;
                return;
            }
            int p = PowerManager.RemainingChargePercent;
            bool charging = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
            int step = Math.Clamp(p / 10, 0, 10);
            BatteryGlyph.Text = char.ConvertFromUtf32((charging ? 0xEBAB : 0xEBA0) + step);
            BatteryText.Text  = $"{p}%";
            BatteryPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            BatteryPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ── Music ─────────────────────────────────────────────────────────
    public void UpdateMedia(Services.MediaState state, bool newTrack)
    {
        _hasMusic = state.HasSession;
        if (!state.HasSession)
        {
            MusicSection.Visibility = Visibility.Collapsed;
            PillMusic.Visibility    = Visibility.Collapsed;
            PillText.Visibility     = Visibility.Collapsed;
            SetBarsPlaying(false);
            return;
        }

        _appId   = state.AppId;
        _playing = state.IsPlaying;

        SongTitle.Text    = state.Title;
        SongArtist.Text   = state.Artist;
        MusicAppName.Text = state.AppName;
        PillArt.Source    = state.Art;
        FullArt.Source    = state.Art;
        PlayPauseGlyph.Text = _playing ? "" : "";   // pause / play icon
        ApplyAccent(ColorExtractor.Dominant(state.Art));
        SetBarsPlaying(_playing);

        MusicSection.Visibility = Visibility.Visible;
        PillMusic.Visibility    = Visibility.Visible;

        // A new song: show its name in the notch for a moment.
        if (newTrack && !_expanded)
        {
            PillText.Text = string.IsNullOrWhiteSpace(state.Artist) ? state.Title : $"{state.Title} · {state.Artist}";
            PillText.Visibility = Visibility.Visible;
            _peekTimer.Stop();
            _peekTimer.Start();
        }

        if (_expanded) UpdateProgress();
    }

    /// <summary>Tints the player with the album's main colour.</summary>
    private void ApplyAccent(Color? cover)
    {
        var baseColor = cover ?? Color.FromRgb(0x3A, 0x3A, 0x3C);

        var top    = ColorExtractor.Mix(baseColor, Colors.Black, 0.45);
        var bottom = ColorExtractor.Mix(baseColor, Colors.Black, 0.82);
        MusicSection.Background  = new LinearGradientBrush(top, bottom, 60);
        MusicSection.BorderBrush = new SolidColorBrush(ColorExtractor.Mix(baseColor, Colors.Black, 0.25)) { Opacity = 0.6 };

        var accent = new SolidColorBrush(ColorExtractor.EnsureBright(baseColor));
        foreach (var bar in new[] { Bar1, Bar2, Bar3, Bar4 }) bar.Fill = accent;
    }

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

    private void UpdateProgress()
    {
        var (pos, dur) = _media.GetTimeline();
        if (dur <= TimeSpan.Zero)
        {
            SeekRow.Visibility = Visibility.Collapsed;   // the player doesn't report a timeline
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
        _closeTimer.Stop();
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking) return;
        _seeking = false;
        _media.Seek(TimeSpan.FromSeconds(SeekSlider.Value));
        if (!Expanded.IsMouseOver) _closeTimer.Start();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ready && _seeking) PositionText.Text = Format(TimeSpan.FromSeconds(e.NewValue));
    }

    // Volume of the playing app only (its entry in the Volume Mixer)
    private void LoadAppVolume()
    {
        var v = AppVolume.Get(_appId);
        VolumeRow.IsEnabled = v != null;
        VolumeRow.Opacity   = v != null ? 1.0 : 0.4;
        VolumeRow.ToolTip   = v != null ? null : "App này chưa phát âm thanh";

        _settingVolume = true;
        VolumeSlider.Value = Math.Round((v ?? 1f) * 100);
        _settingVolume = false;
        ShowAppVolume(VolumeSlider.Value);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        ShowAppVolume(e.NewValue);
        if (!_settingVolume) AppVolume.Set(_appId, (float)(e.NewValue / 100.0));
    }

    private void ShowAppVolume(double value)
    {
        AppVolumeText.Text  = Math.Round(value).ToString();
        AppVolumeGlyph.Text = VolumeGlyph(value / 100.0, muted: false);
    }

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
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(speeds[i]))
                {
                    AutoReverse    = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = rest[i];
            }
        }
    }

    // ── System volume bar ─────────────────────────────────────────────
    public void ShowVolume(float level, bool muted)
    {
        if (!IsVisible || _expanded) return;

        VolGlyph.Text       = VolumeGlyph(level, muted);
        VolFillScale.ScaleX = muted ? 0 : Math.Clamp(level, 0f, 1f);
        VolText.Text        = muted ? "Tắt" : Math.Round(level * 100).ToString();

        Pill.Visibility       = Visibility.Collapsed;
        VolumeView.Visibility = Visibility.Visible;
        _volumeTimer.Stop();
        _volumeTimer.Start();
    }

    private static string VolumeGlyph(double level, bool muted) =>
        muted || level < 0.005 ? ""   // muted
        : level < 0.34 ? ""           // low
        : level < 0.67 ? ""           // medium
        : "";                         // high

    // ── Focus: plant a tree ───────────────────────────────────────────
    private void BuildPresets()
    {
        PresetPanel.Children.Clear();
        foreach (var minutes in FocusService.Presets)
        {
            var chip = new RadioButton
            {
                Content   = $"{minutes}p",
                GroupName = "focus",
                Style     = (Style)FindResource("Chip"),
                IsChecked = minutes == _selectedMinutes,
                ToolTip   = $"{minutes} phút"
            };
            int m = minutes;
            chip.Checked += (_, _) => _selectedMinutes = m;
            PresetPanel.Children.Add(chip);
        }
    }

    private void Plant_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.LastFocusMinutes = _selectedMinutes;
        _focus.Start(_selectedMinutes);   // saves settings too
    }

    /// <summary>Two clicks: the first asks, the second kills the tree.</summary>
    private void GiveUp_Click(object sender, RoutedEventArgs e)
    {
        if (!_confirmGiveUp)
        {
            _confirmGiveUp = true;
            GiveUpBtn.Content = "Bấm lần nữa để bỏ cuộc";
            _giveUpTimer.Stop();
            _giveUpTimer.Start();
            return;
        }
        ResetGiveUp();
        _focus.GiveUp();
    }

    private void ResetGiveUp()
    {
        _giveUpTimer.Stop();
        _confirmGiveUp = false;
        GiveUpBtn.Content = "Bỏ cuộc";
    }

    private void UpdateFocusUi()
    {
        bool running = _focus.IsRunning;
        FocusIdle.Visibility    = running ? Visibility.Collapsed : Visibility.Visible;
        FocusRunning.Visibility = running ? Visibility.Visible   : Visibility.Collapsed;
        PillFocus.Visibility    = running ? Visibility.Visible   : Visibility.Collapsed;

        if (running) UpdateFocusTick();
        else
        {
            BigTree.Set(0.35, dead: false);   // a seedling waiting to be planted
            ResetGiveUp();
        }
        UpdateForest();
    }

    private void UpdateFocusTick()
    {
        if (!_focus.IsRunning) return;
        var left = Format(_focus.Remaining);
        FocusRemaining.Text   = left;
        PillFocusText.Text    = left;
        FocusSub.Text         = $"Đang tập trung · {_focus.Minutes} phút";
        FocusFillScale.ScaleX = _focus.Progress;
        BigTree.Set(_focus.Progress, dead: false);
        PillTree.Set(_focus.Progress, dead: false);
    }

    private void UpdateForest()
    {
        var today   = _focus.Today();
        int grown   = today.Count(s => s.Success);
        int minutes = today.Where(s => s.Success).Sum(s => s.Minutes);

        ForestStats.Text = today.Count == 0
            ? $"Hôm nay chưa trồng cây nào · Tổng {_focus.TotalTrees} cây"
            : $"Hôm nay {grown} cây · {minutes} phút · Tổng {_focus.TotalTrees}";

        // A tiny forest: one tree per session today (brown = gave up)
        ForestRow.Children.Clear();
        foreach (var session in today.Skip(Math.Max(0, today.Count - 10)))
        {
            var tree = new TreeShape
            {
                LayoutTransform = new ScaleTransform(0.2, 0.2),
                Margin  = new Thickness(1, 0, 1, 0),
                ToolTip = $"{session.StartUtc.ToLocalTime():HH:mm} · {session.Minutes} phút" + (session.Success ? "" : " · đã bỏ cuộc")
            };
            tree.Set(1, dead: !session.Success);
            ForestRow.Children.Add(tree);
        }
    }

    // ── Recent notifications ──────────────────────────────────────────
    private void RebuildRecent()
    {
        RecentList.Children.Clear();
        var items = _recent().Take(3).ToList();
        RecentSection.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (time, data) in items)
            RecentList.Children.Add(MakeRecentRow(time, data));
    }

    private UIElement MakeRecentRow(DateTime time, NotificationData data)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Round avatar: the site/app logo, or its first letter
        var avatar = new Grid
        {
            Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center,
            Clip  = new EllipseGeometry(new Point(11, 11), 11, 11)
        };
        if (data.Icon != null)
        {
            avatar.Children.Add(new Image { Source = data.Icon, Stretch = Stretch.UniformToFill });
        }
        else
        {
            avatar.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)) });
            avatar.Children.Add(new TextBlock
            {
                Text = data.AppName.Length > 0 ? data.AppName[..1].ToUpperInvariant() : "?",
                Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        }
        grid.Children.Add(avatar);

        var text = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = data.AppName, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(data.Body) ? data.Title : data.Body,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), FontSize = 11.5,
            FontFamily = new FontFamily("Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var when = new TextBlock
        {
            Text = time.ToString("HH:mm"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), FontFamily = new FontFamily("Segoe UI")
        };
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 4, 6, 4),
            Background = Brushes.Transparent, Cursor = Cursors.Hand
        };
        var hover = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            IslandWindow.OpenSource(data, data.AppUserModelId);
            Collapse();
        };
        return row;
    }

    // ── Helpers ───────────────────────────────────────────────────────
    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
