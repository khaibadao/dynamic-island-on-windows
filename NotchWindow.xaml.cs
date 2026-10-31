using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;
using DynamicIslandNotifier.UI;
using Windows.System.Power;

namespace DynamicIslandNotifier;

/// <summary>
/// The notch: a small black bar at the top centre of the screen, like the
/// iPhone's. In miniature it shows the album art with sound bars and the
/// growing tree; hovering it opens a wide panel with three tabs: music,
/// tree planting and recent notifications. It hides during fullscreen
/// apps and while a notification drops down from the same spot.
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

    private static readonly Color Neutral = Color.FromRgb(0x5A, 0x5A, 0x5E);

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
    private bool _peeking;
    private string? _appId;
    private string _title = "";
    private int _selectedMinutes;
    private TreeKind _selectedKind;

    private static readonly string[] Weekdays =
        { "Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy" };

    public NotchWindow(MediaWatcher media, FocusService focus,
                       Func<IReadOnlyList<(DateTime Time, NotificationData Data)>> recent)
    {
        InitializeComponent();
        _media  = media;
        _focus  = focus;
        _recent = recent;

        _openTimer.Tick   += (_, _) => { _openTimer.Stop();   if (Pill.IsMouseOver || NativeInterop.IsCursorOver(this)) Expand(); };
        _closeTimer.Tick  += (_, _) => { _closeTimer.Stop();  if (!_seeking && !NativeInterop.IsCursorOver(this)) Collapse(); };
        _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); VolumeView.Visibility = Visibility.Collapsed; if (!_expanded) Pill.Visibility = Visibility.Visible; };
        _peekTimer.Tick   += (_, _) => { _peekTimer.Stop();   _peeking = false; UpdatePill(); };
        _giveUpTimer.Tick += (_, _) => { _giveUpTimer.Stop(); ResetGiveUp(); };
        _tickTimer.Tick   += (_, _) => OnTick();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();

        var s = AppSettings.Current;
        _selectedMinutes = FocusService.Presets.Contains(s.LastFocusMinutes) ? s.LastFocusMinutes : 25;
        _selectedKind    = TreeCatalog.Parse(s.LastTreeKind);
        if (!_focus.IsUnlocked(_selectedKind)) _selectedKind = TreeKind.Oak;
        BuildPresets();

        _focus.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateFocusUi));

        _ready = true;
        SizeChanged += (_, _) => Recenter();
        Loaded += (_, _) =>
        {
            Recenter();
            UpdateFocusUi();
            ApplyAccent(null);
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

        UpdateClock();

        // Open on the tab used last; straight to the tree while one is growing.
        var tab = _focus.IsRunning ? "focus" : AppSettings.Current.LastNotchTab;
        var button = tab switch
        {
            "focus"         => TabFocus,
            "notifications" => TabNotifications,
            _               => TabMusic
        };
        if (button.IsChecked == true) ShowTab(tab);
        else button.IsChecked = true;   // → Tab_Checked → ShowTab

        // Drop down from the notch
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = TimeSpan.FromMilliseconds(200);
        Expanded.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, 1, dur) { EasingFunction = ease });
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, dur) { EasingFunction = ease });

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
        StopMarquee();
        ResetGiveUp();
    }

    // ── Tabs ──────────────────────────────────────────────────────────
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var tab = sender == TabFocus ? "focus" : sender == TabNotifications ? "notifications" : "music";
        AppSettings.Current.LastNotchTab = tab;
        AppSettings.Current.Save();
        ShowTab(tab);
    }

    private void ShowTab(string tab)
    {
        MusicTab.Visibility         = tab == "music"         ? Visibility.Visible : Visibility.Collapsed;
        FocusTab.Visibility         = tab == "focus"         ? Visibility.Visible : Visibility.Collapsed;
        NotificationsTab.Visibility = tab == "notifications" ? Visibility.Visible : Visibility.Collapsed;

        StopMarquee();
        switch (tab)
        {
            case "music":
                if (_hasMusic)
                {
                    LoadAppVolume();
                    UpdateProgress();
                    StartMarqueeSoon();
                }
                break;
            case "focus":
                UpdateFocusUi();
                break;
            case "notifications":
                RebuildRecent();
                break;
        }
    }

    // ── Every second ──────────────────────────────────────────────────
    private void OnTick()
    {
        if (_focus.IsRunning) UpdateFocusTick();
        if (_expanded)
        {
            UpdateClock();
            if (_hasMusic && MusicTab.IsVisible) UpdateProgress();

            // Safety net in case MouseLeave never came: close once the pointer is gone.
            if (!_seeking && !_closeTimer.IsEnabled && !NativeInterop.IsCursorOver(this))
                _closeTimer.Start();
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = $"{now:HH:mm}  ·  {Weekdays[(int)now.DayOfWeek]}, {now:dd/MM}";

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

    // ── The collapsed notch ───────────────────────────────────────────
    /// <summary>
    /// Left: album art (or the tree). Right: sound bars (or the time left).
    /// Middle: the song name for a moment, or tree + time when both run.
    /// </summary>
    private void UpdatePill()
    {
        bool focus = _focus.IsRunning;

        PillArtBox.Visibility   = _hasMusic ? Visibility.Visible : Visibility.Collapsed;
        PillBars.Visibility     = _hasMusic ? Visibility.Visible : Visibility.Collapsed;
        PillTreeLeft.Visibility = !_hasMusic && focus ? Visibility.Visible : Visibility.Collapsed;
        PillFocusText.Visibility = !_hasMusic && focus ? Visibility.Visible : Visibility.Collapsed;

        PillText.Visibility     = _peeking && _hasMusic ? Visibility.Visible : Visibility.Collapsed;
        PillFocusMid.Visibility = _hasMusic && focus && !_peeking ? Visibility.Visible : Visibility.Collapsed;
        PillLeft.Visibility     = _hasMusic || focus ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Music ─────────────────────────────────────────────────────────
    public void UpdateMedia(Services.MediaState state, bool newTrack)
    {
        _hasMusic = state.HasSession;
        if (!state.HasSession)
        {
            MusicContent.Visibility = Visibility.Collapsed;
            NoMusicText.Visibility  = Visibility.Visible;
            _peeking = false;
            SetBarsPlaying(false);
            StopMarquee();
            ApplyAccent(null);
            UpdatePill();
            return;
        }

        _appId   = state.AppId;
        _playing = state.IsPlaying;

        bool titleChanged = state.Title != _title;
        _title = state.Title;

        SongArtist.Text   = state.Artist;
        MusicAppName.Text = state.AppName;
        PillArt.Source    = state.Art;
        FullArt.Source    = state.Art;
        PlayPauseGlyph.Text = _playing ? "" : "";   // pause / play icon
        ApplyAccent(ColorExtractor.Dominant(state.Art));
        SetBarsPlaying(_playing);

        MusicContent.Visibility = Visibility.Visible;
        NoMusicText.Visibility  = Visibility.Collapsed;

        // A new song: show its name in the notch for a moment.
        if (newTrack && !_expanded)
        {
            PillText.Text = string.IsNullOrWhiteSpace(state.Artist) ? state.Title : $"{state.Title} · {state.Artist}";
            _peeking = true;
            _peekTimer.Stop();
            _peekTimer.Start();
        }
        UpdatePill();

        if (titleChanged || SongTitle.Text.Length == 0)
        {
            StopMarquee();
            if (_expanded && MusicTab.IsVisible) StartMarqueeSoon();
        }
        if (_expanded && MusicTab.IsVisible) UpdateProgress();
    }

    /// <summary>
    /// Colours from the album art: a tint behind the player, the sound bars,
    /// the seek bar, and a thin border + soft glow around the notch.
    /// </summary>
    private void ApplyAccent(Color? cover)
    {
        var vivid = cover is { } c ? ColorExtractor.Vivid(c) : Colors.White;
        bool hasColour = cover != null && vivid != Colors.White;

        var accent = new SolidColorBrush(vivid);
        accent.Freeze();
        Resources["AccentBrush"] = accent;
        foreach (var bar in Bars) bar.Fill = accent;

        if (cover is { } baseColor)
        {
            var tint = ColorExtractor.Mix(baseColor, vivid, 0.5);
            MusicTint.Background = new LinearGradientBrush(
                Color.FromArgb(0x66, tint.R, tint.G, tint.B),
                Color.FromArgb(0x10, tint.R, tint.G, tint.B), 0);
        }
        else
        {
            MusicTint.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        }

        var edge = hasColour ? ColorExtractor.Mix(vivid, Colors.Black, 0.35) : Color.FromRgb(0x3A, 0x3A, 0x3C);
        var border = new SolidColorBrush(edge);
        border.Freeze();
        Pill.BorderBrush       = border;
        Expanded.BorderBrush   = border;
        VolumeView.BorderBrush = border;

        PillGlow.Color       = hasColour ? vivid : Neutral;
        PillGlow.Opacity     = hasColour ? 0.45 : 0.3;
        ExpandedGlow.Color   = hasColour ? vivid : Neutral;
        ExpandedGlow.Opacity = hasColour ? 0.4 : 0.3;
    }

    private System.Windows.Shapes.Rectangle[] Bars => new[] { Bar1, Bar2, Bar3, Bar4, Bar5, Bar6, Bar7 };

    private void SetBarsPlaying(bool playing)
    {
        var bars   = Bars;
        var speeds = new[] { 420, 560, 360, 500, 440, 620, 380 };   // different speeds so they look lively
        var rest   = new[] { 0.3, 0.55, 0.4, 0.7, 0.45, 0.6, 0.35 };

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i].RenderTransform is not ScaleTransform scale) continue;
            if (playing)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(speeds[i]))
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

    // Long song names scroll from right to left
    private void StartMarqueeSoon() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StartMarquee));

    private void StartMarquee()
    {
        StopMarquee();
        SongTitle.Text = _title;

        double box = TitleClip.ActualWidth;
        double full = TextWidth(_title);
        if (box <= 0 || full <= box) return;

        const string gap = "          ";
        double cycle = TextWidth(_title + gap);
        SongTitle.Text = _title + gap + _title;

        var anim = new DoubleAnimation(0, -cycle, TimeSpan.FromSeconds(cycle / 40))   // 40 px per second
        {
            BeginTime      = TimeSpan.FromSeconds(1.5),
            RepeatBehavior = RepeatBehavior.Forever
        };
        TitleShift.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void StopMarquee()
    {
        TitleShift.BeginAnimation(TranslateTransform.XProperty, null);
        TitleShift.X   = 0;
        SongTitle.Text = _title;
    }

    private double TextWidth(string text)
    {
        var probe = new TextBlock
        {
            Text = text, FontSize = SongTitle.FontSize, FontWeight = SongTitle.FontWeight,
            FontFamily = SongTitle.FontFamily
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
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
        if (!NativeInterop.IsCursorOver(this)) _closeTimer.Start();
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

    // ── Tree planting ─────────────────────────────────────────────────
    private void BuildPresets()
    {
        PresetPanel.Children.Clear();
        foreach (var minutes in FocusService.Presets)
        {
            var chip = new RadioButton
            {
                Content   = $"{minutes}p",
                GroupName = "focusMinutes",
                Style     = (Style)FindResource("Chip"),
                IsChecked = minutes == _selectedMinutes,
                ToolTip   = $"{minutes} phút"
            };
            int m = minutes;
            chip.Checked += (_, _) => _selectedMinutes = m;
            PresetPanel.Children.Add(chip);
        }
    }

    /// <summary>One button per kind of tree; locked ones show what's needed to unlock them.</summary>
    private void BuildSpecies()
    {
        SpeciesPanel.Children.Clear();
        int grown = _focus.TotalTrees;

        foreach (var info in TreeCatalog.All)
        {
            bool unlocked = grown >= info.UnlockAt;
            bool selected = info.Kind == _selectedKind;

            var icon = new TreeShape
            {
                Kind = info.Kind,
                LayoutTransform = new ScaleTransform(0.3, 0.3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Opacity = unlocked ? 1 : 0.3
            };

            var content = new Grid();
            content.Children.Add(icon);
            if (!unlocked)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var tile = new Border
            {
                Width = 38, Height = 38, Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(10), Child = content,
                Background = Frozen(Color.FromRgb(0x1E, 0x1E, 0x20)),
                BorderThickness = new Thickness(1.5),
                BorderBrush = selected ? Frozen(Color.FromRgb(0x34, 0xC7, 0x59)) : Brushes.Transparent,
                Cursor = unlocked ? Cursors.Hand : Cursors.Arrow
            };

            string hint = unlocked
                ? info.Name
                : $"{info.Name} · trồng thêm {info.UnlockAt - grown} cây để mở khoá";
            tile.MouseEnter += (_, _) => SpeciesHint.Text = hint;
            tile.MouseLeave += (_, _) => SpeciesHint.Text = SelectedHint();

            var kind = info.Kind;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (!_focus.IsUnlocked(kind)) return;
                _selectedKind = kind;
                AppSettings.Current.LastTreeKind = kind.ToString();
                AppSettings.Current.Save();
                BigTree.Kind = kind;
                BuildSpecies();
            };

            SpeciesPanel.Children.Add(tile);
        }
        SpeciesHint.Text = SelectedHint();
    }

    private string SelectedHint()
    {
        var next = TreeCatalog.All.FirstOrDefault(i => !_focus.IsUnlocked(i.Kind));
        var name = TreeCatalog.Get(_selectedKind).Name;
        return next == null
            ? $"{name} · đã mở khoá tất cả các loại cây"
            : $"{name} · trồng thêm {next.UnlockAt - _focus.TotalTrees} cây để mở khoá {next.Name}";
    }

    private void Plant_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.LastFocusMinutes = _selectedMinutes;
        AppSettings.Current.LastTreeKind     = _selectedKind.ToString();
        _focus.Start(_selectedMinutes, _selectedKind);   // saves settings too
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

        if (running)
        {
            BigTree.Kind      = _focus.CurrentKind;
            PillTreeLeft.Kind = _focus.CurrentKind;
            PillTreeMid.Kind  = _focus.CurrentKind;
            UpdateFocusTick();
        }
        else
        {
            if (!_focus.IsUnlocked(_selectedKind)) _selectedKind = TreeKind.Oak;
            BigTree.Kind = _selectedKind;
            BigTree.Set(0.35, dead: false);   // a seedling waiting to be planted
            BuildSpecies();
            ResetGiveUp();
        }
        UpdateForest();
        UpdatePill();
    }

    private void UpdateFocusTick()
    {
        if (!_focus.IsRunning) return;
        var left = Format(_focus.Remaining);
        FocusRemaining.Text   = left;
        PillFocusText.Text    = left;
        PillFocusMidText.Text = left;
        FocusSub.Text         = $"{TreeCatalog.Get(_focus.CurrentKind).Name} · {_focus.Minutes} phút";
        FocusFillScale.ScaleX = _focus.Progress;
        BigTree.Set(_focus.Progress, dead: false);
        PillTreeLeft.Set(_focus.Progress, dead: false);
        PillTreeMid.Set(_focus.Progress, dead: false);
    }

    private void UpdateForest()
    {
        var today   = _focus.Today();
        int grown   = today.Count(s => s.Success);
        int minutes = today.Where(s => s.Success).Sum(s => s.Minutes);

        ForestStats.Text = today.Count == 0
            ? $"Hôm nay chưa trồng cây nào · Tổng {_focus.TotalTrees} cây"
            : $"Hôm nay {grown} cây · {minutes} phút · Tổng {_focus.TotalTrees} cây";

        // Today's forest: one tree per session (brown = gave up)
        ForestRow.Children.Clear();
        foreach (var session in today.Skip(Math.Max(0, today.Count - 12)))
        {
            var tree = new TreeShape
            {
                Kind = session.TreeKind,
                LayoutTransform = new ScaleTransform(0.2, 0.2),
                Margin  = new Thickness(1, 0, 1, 0),
                ToolTip = $"{TreeCatalog.Get(session.TreeKind).Name} · {session.StartUtc.ToLocalTime():HH:mm} · {session.Minutes} phút"
                          + (session.Success ? "" : " · đã bỏ cuộc")
            };
            tree.Set(1, dead: !session.Success);
            ForestRow.Children.Add(tree);
        }
    }

    // ── Recent notifications ──────────────────────────────────────────
    private void RebuildRecent()
    {
        RecentList.Children.Clear();
        var items = _recent();
        NoNotificationsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (time, data) in items)
            RecentList.Children.Add(MakeRecentRow(time, data));
    }

    private UIElement MakeRecentRow(DateTime time, NotificationData data)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Round avatar: the site/app logo, or its first letter
        var avatar = new Grid
        {
            Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center,
            Clip  = new EllipseGeometry(new Point(12, 12), 12, 12)
        };
        if (data.Icon != null)
        {
            avatar.Children.Add(new Image { Source = data.Icon, Stretch = Stretch.UniformToFill });
        }
        else
        {
            avatar.Children.Add(new Border { Background = Frozen(Color.FromRgb(0x3A, 0x3A, 0x3C)) });
            avatar.Children.Add(new TextBlock
            {
                Text = data.AppName.Length > 0 ? data.AppName[..1].ToUpperInvariant() : "?",
                Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        }
        grid.Children.Add(avatar);

        var body = string.IsNullOrWhiteSpace(data.Body) ? data.Title : data.Body;
        var line = new TextBlock
        {
            Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = body
        };
        line.Inlines.Add(new System.Windows.Documents.Run(data.AppName + "  ")
        {
            Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
        });
        line.Inlines.Add(new System.Windows.Documents.Run(body.ReplaceLineEndings(" "))
        {
            Foreground = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF))
        });
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);

        var when = new TextBlock
        {
            Text = time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("dd/MM"),
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), FontFamily = new FontFamily("Segoe UI")
        };
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 10, 5),
            Background = Brushes.Transparent, Cursor = Cursors.Hand
        };
        var hover = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
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

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
