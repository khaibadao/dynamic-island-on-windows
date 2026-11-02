using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;
using DynamicIslandNotifier.UI;
using Windows.System.Power;

namespace DynamicIslandNotifier;

/// <summary>
/// The notch at the top centre of the screen, like the iPhone's. Resting, it
/// is a thin strip, or a slim "now playing" pill while music plays; the mouse
/// passes straight through it to the browser tabs underneath. Resting the
/// pointer on it opens a wide panel with tabs: music (with lyrics), tree
/// planting, to-dos, recent notifications and clipboard history. Mây the cat
/// sits beside it. It hides during fullscreen apps and while a notification
/// drops down from the same spot.
/// </summary>
public partial class NotchWindow : Window
{
    private readonly MediaWatcher _media;
    private readonly FocusService _focus;
    private readonly NotificationHistory _history;
    private readonly TodoService _todos;
    private readonly ClipboardService _clips;
    private int _animation;          // bumped by every open / close, so an old animation can't finish a new one
    private string? _pendingTab;     // a tab to open as soon as the notch is visible again
    private bool _typing;            // the to-do box has the keyboard
    private string _tab = "music";

    private const double PanelWidth       = 620;
    private const double PanelWidthLyrics = 960;
    private const double LyricsWidth      = 340;

    private readonly DispatcherTimer _hoverTimer      = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _closeTimer      = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _tickTimer       = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _volumeTimer     = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _giveUpTimer     = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _lyricsTimer     = new() { Interval = TimeSpan.FromMilliseconds(150) };

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
    private bool _faded;
    private DateTime? _dwellStart;
    private string? _appId;
    private string _title = "";
    private string _artist = "";
    private int _selectedMinutes;
    private TreeKind _selectedKind;
    private Color _accent = Colors.White;
    private bool _hasColour;
    private bool _showWeek;

    // Lyrics
    private bool _lyricsOpen;
    private string? _lyricsKey;
    private IReadOnlyList<LyricLine> _lyrics = Array.Empty<LyricLine>();
    private int _lyricIndex = -1;
    private double _scrollTarget;
    private bool _scrolling;

    // Mây
    private CatMood? _catOverride;
    private DateTime _catOverrideUntil;
    private bool _allDone;

    private static readonly Color DndColour = Color.FromRgb(0x8E, 0x7C, 0xFF);
    private static readonly string[] ShortDays = { "CN", "T2", "T3", "T4", "T5", "T6", "T7" };

    private static readonly string[] Weekdays =
        { "Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy" };

    public NotchWindow(MediaWatcher media, FocusService focus, NotificationHistory history, TodoService todos,
                       ClipboardService clips)
    {
        InitializeComponent();
        _media   = media;
        _focus   = focus;
        _history = history;
        _todos   = todos;
        _clips   = clips;
        _todos.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateTodoUi));
        _clips.Changed += () => Dispatcher.BeginInvoke(new Action(RebuildClipboard));
        Deactivated += (_, _) => { if (_typing) EndTyping(collapse: !NativeInterop.IsCursorOver(Expanded)); };

        _hoverTimer.Tick  += (_, _) => WatchPointer();
        _closeTimer.Tick  += (_, _) =>
        {
            _closeTimer.Stop();
            // Only the visible panel counts, not the window's transparent area.
            if (!_seeking && !_typing && !NativeInterop.IsCursorOver(Expanded)) Collapse();
        };
        _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); VolumeView.Visibility = Visibility.Collapsed; ShowRest(); };
        _giveUpTimer.Tick += (_, _) => { _giveUpTimer.Stop(); ResetGiveUp(); };
        _tickTimer.Tick   += (_, _) => OnTick();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _lyricsTimer.Tick += (_, _) => UpdateLyrics();

        // Wavy seek bar
        SeekBar.SeekStarted   += () => { _seeking = true; _closeTimer.Stop(); };
        SeekBar.SeekPreview   += s => PositionText.Text = Format(TimeSpan.FromSeconds(s));
        SeekBar.SeekCommitted += s =>
        {
            _seeking = false;
            _media.Seek(TimeSpan.FromSeconds(s));
            _lyricIndex = -1;
            if (!NativeInterop.IsCursorOver(Expanded)) _closeTimer.Start();
        };

        // Mây
        _focus.Finished += session => Dispatcher.BeginInvoke(new Action(() =>
            CatReact(session.Success ? CatMood.Happy : CatMood.Sad, session.Success ? 8 : 10)));
        var (done, total) = _todos.Progress();
        _allDone = total > 0 && done == total;
        CatPanel.MouseLeftButtonUp += (_, _) => CatReact(CatMood.Happy, 2.5);   // a pat on the head
        CatRest.SizeChanged  += (_, _) => PlaceCat();
        RestArea.SizeChanged += (_, _) => PlaceCat();

        var s = AppSettings.Current;
        _selectedMinutes = FocusService.Presets.Contains(s.LastFocusMinutes) ? s.LastFocusMinutes : 25;
        _selectedKind    = TreeCatalog.Parse(s.LastTreeKind);
        if (!_focus.IsUnlocked(_selectedKind)) _selectedKind = TreeKind.Oak;
        _lyricsOpen      = s.LyricsOpen;
        BuildPresets();

        _focus.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateFocusUi));

        _ready = true;
        _clips.Start(this);   // creates the window handle too
        Loaded += (_, _) =>
        {
            Recenter();
            UpdateFocusUi();
            ApplyAccent(null);
            OnTick();
            ShowRest();
            _tickTimer.Start();
            _fullscreenTimer.Start();
            _hoverTimer.Start();
        };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.PrimaryScreenWidth)) Recenter();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeInterop.MakeOverlay(this);              // never steals focus, no taskbar/Alt-Tab entry
        NativeInterop.SetClickThrough(this, true);    // resting: clicks go to the tabs underneath
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
            if (_pendingTab is { } tab)
            {
                _pendingTab = null;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => OpenTab(tab)));
            }
        }
        else if (!show && IsVisible)
        {
            Collapse();
            Hide();
        }
    }

    /// <summary>The window has a fixed size (no jumps while opening), centred at the top.</summary>
    private void Recenter()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top  = 0;
    }

    // ── Resting: watch the pointer (the window lets clicks through) ──
    private void WatchPointer()
    {
        if (!IsVisible || _expanded) { _dwellStart = null; return; }

        FrameworkElement? target = Pill.IsVisible ? Pill : StripHost.IsVisible ? StripHost : null;
        if (target == null) { _dwellStart = null; SetFaded(false); return; }

        // Pointer close by: fade, so whatever is underneath shows through.
        SetFaded(target == Pill && NativeInterop.IsCursorNear(Pill, 26));

        bool over = NativeInterop.IsCursorOver(target);
        if (!over) { _dwellStart = null; return; }

        _dwellStart ??= DateTime.UtcNow;
        double need = target == Pill ? 400 : 150;   // the strip is so thin that touching it is enough
        if ((DateTime.UtcNow - _dwellStart.Value).TotalMilliseconds >= need)
        {
            _dwellStart = null;
            Expand();
        }
    }

    private void SetFaded(bool faded)
    {
        if (faded == _faded) return;
        _faded = faded;
        var anim = new DoubleAnimation(faded ? 0.3 : 1, TimeSpan.FromMilliseconds(160));
        RestArea.BeginAnimation(OpacityProperty, anim);
        CatRest.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Resting look: the slim pill while music plays or a tree grows, else the strip.</summary>
    private void ShowRest()
    {
        if (_expanded || VolumeView.Visibility == Visibility.Visible) return;
        bool pill = (_hasMusic && _playing) || _focus.IsRunning;
        Pill.Visibility      = pill ? Visibility.Visible : Visibility.Collapsed;
        StripHost.Visibility = pill ? Visibility.Collapsed : Visibility.Visible;
        RestArea.Visibility  = Visibility.Visible;
        CatRest.Visibility   = Visibility.Visible;
        PlaceCat();
    }

    /// <summary>Mây sits just to the right of the strip / pill.</summary>
    private void PlaceCat()
    {
        double half = RestArea.ActualWidth > 0 ? RestArea.ActualWidth / 2 : 100;
        CatRestShift.X = half + CatRest.ActualWidth / 2 + 6;
    }

    private void Expanded_MouseEnter(object sender, MouseEventArgs e) => _closeTimer.Stop();
    private void Expanded_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_seeking && !_typing) _closeTimer.Start();
    }

    // ── Open / close ──────────────────────────────────────────────────
    private void Expand() => Expand(null);

    /// <summary>Opens the panel (on <paramref name="tab"/>, or the tab used last).</summary>
    private void Expand(string? tab)
    {
        if (!IsVisible) return;
        bool wasOpen = _expanded;
        _expanded = true;
        int gen = ++_animation;
        NativeInterop.SetClickThrough(this, false);

        // Grow out of whatever is showing now
        double fromWidth = Pill.IsVisible ? Pill.ActualWidth : StripHost.IsVisible ? StripHost.ActualWidth : 200;
        double fromHeight = Pill.IsVisible ? Pill.ActualHeight : 9;

        _volumeTimer.Stop();
        _dwellStart = null;
        SetFaded(false);
        VolumeView.Visibility = Visibility.Collapsed;
        RestArea.Visibility   = Visibility.Collapsed;
        CatRest.Visibility    = Visibility.Collapsed;
        Expanded.Visibility   = Visibility.Visible;

        UpdateClock();
        UpdateQuickButtons();
        UpdateTodoBadge();
        UpdateCat();

        // Open on the tab asked for, else the one used last; straight to the tree while one grows.
        tab ??= _focus.IsRunning ? "focus" : AppSettings.Current.LastNotchTab;
        var button = TabButtonFor(tab);
        if (button.IsChecked == true) ShowTab(tab);
        else button.IsChecked = true;   // → Tab_Checked → ShowTab

        if (!wasOpen)
        {
            // The pill morphs into the panel. While it moves, the glow is off and the
            // panel is drawn once as a picture: much smoother on a big transparent window.
            Expanded.UpdateLayout();
            double w = Math.Max(1, Expanded.ActualWidth), h = Math.Max(1, Expanded.ActualHeight);
            BeginMorph();
            var grow = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };
            var dur  = TimeSpan.FromMilliseconds(360);
            var sx = new DoubleAnimation(Math.Clamp(fromWidth / w, 0.15, 1), 1, dur) { EasingFunction = grow };
            var sy = new DoubleAnimation(Math.Clamp(fromHeight / h, 0.04, 1), 1, dur) { EasingFunction = grow };
            sy.Completed += (_, _) => { if (gen == _animation) EndMorph(); };
            Expanded.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(160)));
            ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }

        if (!_typing) _closeTimer.Start();   // closes again if the mouse just passed by
    }

    private void Collapse()
    {
        _closeTimer.Stop();
        if (!_expanded) return;
        _expanded = false;
        _seeking  = false;
        if (_typing) EndTyping(collapse: false);
        int gen = ++_animation;
        NativeInterop.SetClickThrough(this, true);   // clicks go through right away

        StopMarquee();
        ResetGiveUp();
        _lyricsTimer.Stop();

        // Shrinks back into the notch, then the resting notch returns.
        bool pill = (_hasMusic && _playing) || _focus.IsRunning;
        double toWidth = pill ? Math.Max(140, Pill.ActualWidth) : 200;
        double w = Math.Max(1, Expanded.ActualWidth), h = Math.Max(1, Expanded.ActualHeight);

        BeginMorph();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var dur  = TimeSpan.FromMilliseconds(200);
        var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (gen != _animation) return;   // reopened meanwhile
            Expanded.Visibility = Visibility.Collapsed;
            EndMorph();
            ShowRest();
            RestArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(140)));
        };
        Expanded.BeginAnimation(OpacityProperty, fade);
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Math.Clamp(toWidth / w, 0.15, 1), dur) { EasingFunction = ease });
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Math.Clamp((pill ? 22 : 9) / h, 0.04, 1), dur) { EasingFunction = ease });
    }

    private void BeginMorph()
    {
        Expanded.Effect    = null;
        Expanded.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
    }

    private void EndMorph()
    {
        Expanded.CacheMode = null;
        Expanded.Effect    = ExpandedGlow;
    }

    /// <summary>Opens the notch on a tab, e.g. from a to-do reminder. Waits if the notch is hidden.</summary>
    public void OpenTab(string tab)
    {
        if (!IsVisible) { _pendingTab = tab; return; }
        Expand(tab);
        if (tab == "todo") BeginTyping();
    }

    private RadioButton TabButtonFor(string tab) => tab switch
    {
        "focus"         => TabFocus,
        "notifications" => TabNotifications,
        "todo"          => TabTodo,
        "clipboard"     => TabClipboard,
        _               => TabMusic
    };

    // ── Tabs ──────────────────────────────────────────────────────────
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var tab = sender == TabFocus ? "focus"
                : sender == TabNotifications ? "notifications"
                : sender == TabTodo ? "todo"
                : sender == TabClipboard ? "clipboard"
                : "music";
        AppSettings.Current.LastNotchTab = tab;
        AppSettings.Current.Save();
        ShowTab(tab);
    }

    private void ShowTab(string tab)
    {
        bool animateWidth = _tab != tab && Expanded.IsVisible && Expanded.Opacity > 0.9;
        _tab = tab;
        MusicTab.Visibility         = tab == "music"         ? Visibility.Visible : Visibility.Collapsed;
        FocusTab.Visibility         = tab == "focus"         ? Visibility.Visible : Visibility.Collapsed;
        NotificationsTab.Visibility = tab == "notifications" ? Visibility.Visible : Visibility.Collapsed;
        TodoTab.Visibility          = tab == "todo"          ? Visibility.Visible : Visibility.Collapsed;
        ClipboardTab.Visibility     = tab == "clipboard"     ? Visibility.Visible : Visibility.Collapsed;

        // Soft fade when switching tabs
        TabContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

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
                if (_showWeek) BuildWeekChart();
                break;
            case "notifications":
                RebuildRecent();
                break;
            case "todo":
                UpdateTodoUi();
                break;
            case "clipboard":
                RebuildClipboard();
                break;
        }
        ApplyLyricsLayout(animateWidth);
    }

    // ── Every second ──────────────────────────────────────────────────
    private void OnTick()
    {
        if (_focus.IsRunning) UpdateFocusTick();
        if (Pill.IsVisible) UpdatePillProgress();
        UpdateCat();
        if (_expanded)
        {
            UpdateClock();
            if (_hasMusic && MusicTab.IsVisible) UpdateProgress();

            // Safety net in case MouseLeave never came: close once the pointer is gone.
            if (!_seeking && !_typing && !_closeTimer.IsEnabled && !NativeInterop.IsCursorOver(Expanded))
                _closeTimer.Start();
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text    = now.ToString("HH:mm");
        ClockText.ToolTip = $"{Weekdays[(int)now.DayOfWeek]}, {now:dd/MM/yyyy}";

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

    // ── The resting pill ──────────────────────────────────────────────
    /// <summary>
    /// Left: album art (or the tree). Middle: the song's name. Right: time left
    /// on the tree, sound bars. A thin progress line runs along the bottom.
    /// </summary>
    private void UpdatePill()
    {
        bool focus = _focus.IsRunning;

        PillArtBox.Visibility    = _hasMusic ? Visibility.Visible : Visibility.Collapsed;
        PillBars.Visibility      = _hasMusic ? Visibility.Visible : Visibility.Collapsed;
        PillTreeLeft.Visibility  = !_hasMusic && focus ? Visibility.Visible : Visibility.Collapsed;
        PillFocusText.Visibility = focus ? Visibility.Visible : Visibility.Collapsed;
        PillText.Text = _hasMusic ? _title
                      : focus ? $"Đang trồng {TreeCatalog.Get(_focus.CurrentKind).Name.ToLowerInvariant()}"
                      : "";
        UpdatePillProgress();
        UpdateStrip();
        ShowRest();
    }

    private void UpdatePillProgress()
    {
        double frac = 0;
        if (_hasMusic)
        {
            var (pos, dur) = _media.GetTimeline();
            if (dur > TimeSpan.Zero) frac = pos.TotalSeconds / dur.TotalSeconds;
        }
        else if (_focus.IsRunning)
        {
            frac = _focus.Progress;
        }
        frac = Math.Clamp(frac, 0, 1);
        // Glide to the new value over the next second, so the line moves smoothly.
        bool jump = Math.Abs(frac - PillProgressScale.ScaleX) > 0.05;
        PillProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(frac, TimeSpan.FromMilliseconds(jump ? 200 : 1000)));
    }

    /// <summary>The strip takes the song's colour, or green while a tree grows.</summary>
    private void UpdateStrip()
    {
        Color c = _hasMusic && _hasColour ? _accent
                : _hasMusic ? Colors.White
                : _focus.IsRunning ? Color.FromRgb(0x34, 0xC7, 0x59)
                : Color.FromRgb(0x3A, 0x3A, 0x3C);
        bool lit = _hasMusic || _focus.IsRunning;
        if (!lit && QuickActions.DoNotDisturb)
        {
            c   = DndColour;
            lit = true;
        }

        // Black like the notch, so it blends with dark title bars;
        // the song's colour shows as a thin rim and a soft glow.
        Strip.Stroke      = Frozen(lit ? c : Color.FromRgb(0x3A, 0x3A, 0x3C));
        StripGlow.Color   = lit ? c : Neutral;
        StripGlow.Opacity = lit ? 0.55 : 0.25;
    }

    // ── Music ─────────────────────────────────────────────────────────
    public void UpdateMedia(Services.MediaState state, bool newTrack)
    {
        _hasMusic = state.HasSession;
        if (!state.HasSession)
        {
            MusicContent.Visibility = Visibility.Collapsed;
            NoMusicText.Visibility  = Visibility.Visible;
            _playing = false;
            SetBarsPlaying(false);
            SeekBar.IsPlaying = false;
            StopMarquee();
            ApplyAccent(null);
            UpdatePill();
            ApplyLyricsLayout(false);
            UpdateCat();
            return;
        }

        _appId   = state.AppId;
        _playing = state.IsPlaying;

        bool titleChanged = state.Title != _title || state.Artist != _artist;
        _title  = state.Title;
        _artist = state.Artist;

        SongArtist.Text   = state.Artist;
        MusicAppName.Text = state.AppName;
        PillArt.Source    = state.Art;
        FullArt.Source    = state.Art;
        PlayPauseGlyph.Text = _playing ? "" : "";   // pause / play icon
        ApplyAccent(ColorExtractor.Dominant(state.Art));
        SetBarsPlaying(_playing);
        SeekBar.IsPlaying = _playing;

        MusicContent.Visibility = Visibility.Visible;
        NoMusicText.Visibility  = Visibility.Collapsed;
        UpdatePill();
        UpdateCat();

        if (titleChanged || SongTitle.Text.Length == 0)
        {
            StopMarquee();
            if (_expanded && MusicTab.IsVisible) StartMarqueeSoon();
            if (LyricsPane.IsVisible) LoadLyrics();
        }
        if (_expanded && MusicTab.IsVisible) UpdateProgress();
        ApplyLyricsLayout(false);
    }

    /// <summary>
    /// Colours from the album art: a tint behind the player, the sound bars,
    /// the seek bar, and a thin border + soft glow around the notch.
    /// </summary>
    private void ApplyAccent(Color? cover)
    {
        var vivid = cover is { } c ? ColorExtractor.Vivid(c) : Colors.White;
        bool hasColour = cover != null && vivid != Colors.White;
        _accent    = vivid;
        _hasColour = hasColour;

        var accent = new SolidColorBrush(vivid);
        accent.Freeze();
        Resources["AccentBrush"] = accent;
        foreach (var bar in Bars) bar.Fill = accent;
        SeekBar.Accent = accent;
        if (_lyricsOpen) LyricsBtn.Foreground = accent;

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
        UpdateStrip();
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
        SeekBar.IsPlaying = _playing;
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

        SeekBar.Duration  = dur.TotalSeconds;
        SeekBar.Position  = pos.TotalSeconds;
        PositionText.Text = Format(pos);
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

    // ── Lyrics ────────────────────────────────────────────────────────
    private void Lyrics_Click(object sender, RoutedEventArgs e)
    {
        _lyricsOpen = !_lyricsOpen;
        AppSettings.Current.LyricsOpen = _lyricsOpen;
        AppSettings.Current.Save();
        ApplyLyricsLayout(animate: true);
    }

    /// <summary>Lyrics open: the panel widens and splits, player left, lyrics right.</summary>
    private void ApplyLyricsLayout(bool animate)
    {
        bool show = _lyricsOpen && _hasMusic && _tab == "music";
        LyricsBtn.Foreground = _lyricsOpen ? (Brush)Resources["AccentBrush"] : Frozen(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
        LyricsColumn.Width   = new GridLength(show ? LyricsWidth : 0);
        LyricsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        double target = show ? PanelWidthLyrics : PanelWidth;
        if (animate && Math.Abs(Expanded.ActualWidth - target) > 1)
        {
            var anim = new DoubleAnimation(Expanded.ActualWidth, target, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                Expanded.BeginAnimation(WidthProperty, null);
                Expanded.Width = _lyricsOpen && _hasMusic && _tab == "music" ? PanelWidthLyrics : PanelWidth;
                StartMarqueeSoon();
            };
            Expanded.BeginAnimation(WidthProperty, anim);
        }
        else
        {
            Expanded.BeginAnimation(WidthProperty, null);
            Expanded.Width = target;
        }

        if (show)
        {
            LoadLyrics();
            if (_expanded) _lyricsTimer.Start();
        }
        else
        {
            _lyricsTimer.Stop();
        }
    }

    private async void LoadLyrics()
    {
        var key = $"{_title}|{_artist}";
        if (key == _lyricsKey) return;
        _lyricsKey  = key;
        _lyrics     = Array.Empty<LyricLine>();
        _lyricIndex = -1;
        LyricsList.Children.Clear();
        LyricsScroll.ScrollToVerticalOffset(0);
        LyricsStatus.Text = "Đang tìm lời bài hát…";
        LyricsStatus.Visibility = Visibility.Visible;

        var (_, dur) = _media.GetTimeline();
        var lines = await LyricsService.FindAsync(_title, _artist, dur);
        if (key != _lyricsKey) return;   // the song changed meanwhile

        _lyrics = lines;
        if (lines.Count == 0)
        {
            LyricsStatus.Text = "Không tìm thấy lời bài hát";
            // Try again next time (e.g. once the player reports the song's length).
            _lyricsKey = dur > TimeSpan.Zero ? key : null;
            return;
        }

        LyricsStatus.Visibility = Visibility.Collapsed;
        foreach (var line in lines)
        {
            var text = new TextBlock
            {
                Text = line.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), Cursor = Cursors.Hand,
                Foreground = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
            };
            var at = line.Time;
            text.MouseLeftButtonUp += (_, _) => { _media.Seek(at); _lyricIndex = -1; };
            LyricsList.Children.Add(text);
        }
        UpdateLyrics();
    }

    /// <summary>Highlights the line being sung and keeps it in the middle.</summary>
    private void UpdateLyrics()
    {
        if (_lyrics.Count == 0 || !LyricsPane.IsVisible) return;
        var (pos, _) = _media.GetTimeline();
        pos += TimeSpan.FromMilliseconds(250);   // a little early feels in time

        int idx = -1;
        for (int i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].Time <= pos) idx = i;
            else break;
        }
        if (idx == _lyricIndex) return;

        var dim = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        if (_lyricIndex >= 0 && _lyricIndex < LyricsList.Children.Count)
            ((TextBlock)LyricsList.Children[_lyricIndex]).Foreground = dim;
        _lyricIndex = idx;
        if (idx < 0 || idx >= LyricsList.Children.Count) { ScrollLyricsTo(0); return; }

        var current = (TextBlock)LyricsList.Children[idx];
        current.Foreground = Brushes.White;
        try
        {
            double y = current.TransformToAncestor(LyricsList).Transform(new Point(0, 0)).Y + LyricsList.Margin.Top;
            ScrollLyricsTo(y + current.ActualHeight / 2 - LyricsScroll.ViewportHeight / 2);
        }
        catch { }
    }

    private void ScrollLyricsTo(double offset)
    {
        _scrollTarget = Math.Clamp(offset, 0, LyricsScroll.ScrollableHeight);
        if (_scrolling) return;
        _scrolling = true;
        CompositionTarget.Rendering += ScrollStep;
    }

    private void ScrollStep(object? sender, EventArgs e)
    {
        double now  = LyricsScroll.VerticalOffset;
        double next = now + (_scrollTarget - now) * 0.18;
        if (Math.Abs(_scrollTarget - next) < 0.5 || !LyricsPane.IsVisible)
        {
            next = _scrollTarget;
            _scrolling = false;
            CompositionTarget.Rendering -= ScrollStep;
        }
        LyricsScroll.ScrollToVerticalOffset(next);
    }

    // ── System volume bar ─────────────────────────────────────────────
    public void ShowVolume(float level, bool muted)
    {
        if (!IsVisible || _expanded) return;

        VolGlyph.Text       = VolumeGlyph(level, muted);
        VolFillScale.ScaleX = muted ? 0 : Math.Clamp(level, 0f, 1f);
        VolText.Text        = muted ? "Tắt" : Math.Round(level * 100).ToString();

        RestArea.Visibility   = Visibility.Collapsed;
        CatRest.Visibility    = Visibility.Collapsed;
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
        FocusSub.Text         = $"{TreeCatalog.Get(_focus.CurrentKind).Name} · {_focus.Minutes} phút";
        FocusFillScale.ScaleX = _focus.Progress;
        BigTree.Set(_focus.Progress, dead: false);
        PillTreeLeft.Set(_focus.Progress, dead: false);
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
        var items = _history.Items.Select(e => (e.Time, e.Data)).ToList();
        NoNotificationsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearAllBtn.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NotificationsCount.Text = items.Count == 0 ? "" : $"{items.Count} thông báo gần đây";
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

        var remove = new Button
        {
            Content = "\uE711", Style = (Style)FindResource("QuickButton"), Width = 22, Height = 22, FontSize = 10,
            Margin = new Thickness(0), ToolTip = "Xoá thông báo này", Visibility = Visibility.Collapsed
        };
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            _history.Remove(data);
            RebuildRecent();
        };
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 10, 5),
            Background = Brushes.Transparent, Cursor = Cursors.Hand
        };
        var hover = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        row.MouseEnter += (_, _) =>
        {
            row.Background = hover;
            when.Visibility = Visibility.Hidden;
            remove.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            when.Visibility = Visibility.Visible;
            remove.Visibility = Visibility.Collapsed;
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (remove.IsMouseOver) return;
            IslandWindow.OpenSource(data, data.AppUserModelId);
            Collapse();
        };
        return row;
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        RebuildRecent();
    }

    // ── To-do ─────────────────────────────────────────────────────────
    private void UpdateTodoBadge()
    {
        var (done, total) = _todos.Progress();
        int left = total - done;
        TodoBadge.Visibility = total == 0 || left > 0 ? Visibility.Visible : Visibility.Collapsed;
        TodoBadgeText.Text   = total == 0 ? "!" : left.ToString();
        TodoBadge.ToolTip    = total == 0 ? "Chưa có việc nào cho hôm nay" : $"Còn {left} việc chưa xong";
    }

    private void UpdateTodoUi()
    {
        UpdateTodoBadge();
        var (doneCount, totalCount) = _todos.Progress();
        bool allDone = totalCount > 0 && doneCount == totalCount;
        if (allDone && !_allDone) CatReact(CatMood.Happy, 8);   // Mây celebrates a finished list
        _allDone = allDone;
        if (!TodoTab.IsVisible && !_expanded) return;

        var items = _todos.Today();
        TodoEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TodoList.Children.Clear();
        foreach (var item in items) TodoList.Children.Add(MakeTodoRow(item));
    }

    private UIElement MakeTodoRow(TodoItem item)
    {
        var green = Frozen(Color.FromRgb(0x34, 0xC7, 0x59));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Round tick box
        var tick = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = item.Done ? green : Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            Background  = item.Done ? green : Brushes.Transparent,
            Child = item.Done
                ? new TextBlock { Text = "\uE73E", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10,
                                  Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
                                  VerticalAlignment = VerticalAlignment.Center }
                : null
        };
        tick.MouseLeftButtonUp += (_, _) => _todos.Toggle(item.Id);
        grid.Children.Add(tick);

        var text = new TextBlock
        {
            Text = item.Text, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Text,
            Foreground = item.Done ? Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)) : Brushes.White,
            TextDecorations = item.Done ? TextDecorations.Strikethrough : null
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Due time / "từ hôm qua"
        string meta = "";
        Brush metaBrush = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
        if (item.Due is { } due)
        {
            meta = $"{(int)due.TotalHours:00}:{due.Minutes:00}";
            if (!item.Done && DateTime.Now.TimeOfDay > due) metaBrush = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
            else if (!item.Done && DateTime.Now.TimeOfDay > due - TodoService.SoonBefore) metaBrush = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));
        }
        if (item.FromEarlier) meta = (meta.Length > 0 ? meta + " · " : "") + "từ hôm qua";
        var metaText = new TextBlock
        {
            Text = meta, FontSize = 11, FontFamily = new FontFamily("Segoe UI"), Foreground = metaBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(metaText, 2);
        grid.Children.Add(metaText);

        var remove = new Button
        {
            Content = "\uE711", Style = (Style)FindResource("QuickButton"), Width = 22, Height = 22, FontSize = 10,
            Margin = new Thickness(0), ToolTip = "Xoá việc này", Opacity = 0
        };
        remove.Click += (_, _) => _todos.Delete(item.Id);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 4, 4),
            Background = Brushes.Transparent
        };
        var hover = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        row.MouseEnter += (_, _) => { row.Background = hover; remove.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; remove.Opacity = 0; };
        return row;
    }

    // Typing: the notch doesn't normally take the keyboard (so it never steals
    // focus from your work). Clicking the box lets it, until you're done.
    private void TodoInput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_typing) return;
        BeginTyping();
        e.Handled = true;
    }

    private void BeginTyping()
    {
        _typing = true;
        _closeTimer.Stop();
        NativeInterop.SetActivatable(this, true);
        Activate();
        TodoInput.Focus();
        Keyboard.Focus(TodoInput);
        TodoInput.CaretIndex = TodoInput.Text.Length;
    }

    private void EndTyping(bool collapse)
    {
        if (!_typing) return;
        _typing = false;
        Keyboard.ClearFocus();
        NativeInterop.SetActivatable(this, false);
        if (collapse) Collapse();
        else if (_expanded && !NativeInterop.IsCursorOver(Expanded)) _closeTimer.Start();
    }

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTodo();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TodoInput.Text = "";
            EndTyping(collapse: true);
            e.Handled = true;
        }
    }

    private void TodoInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        TodoPlaceholder.Visibility = TodoInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        var text = TodoInput.Text.Trim();
        if (text.Length == 0) return;
        _todos.Add(text);
        TodoInput.Text = "";
    }

    // ── Quick buttons ─────────────────────────────────────────────────
    private void UpdateQuickButtons()
    {
        var muted = QuickActions.IsMicMuted();
        MicBtn.IsEnabled  = muted != null;
        MicBtn.Foreground = muted == true ? Frozen(Color.FromRgb(0xFF, 0x45, 0x3A)) : Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        MicBtn.ToolTip    = muted == null ? "Không tìm thấy micro"
                          : muted == true ? "Micro đang tắt · bấm để bật" : "Micro đang bật · bấm để tắt";

        bool dnd = QuickActions.DoNotDisturb;
        DndBtn.Foreground = dnd ? Frozen(DndColour) : Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        DndBtn.Background = dnd ? Frozen(Color.FromArgb(0x33, DndColour.R, DndColour.G, DndColour.B)) : Brushes.Transparent;
        DndBtn.ToolTip    = dnd ? "Không làm phiền: BẬT · thông báo vẫn vào lịch sử" : "Không làm phiền: tắt";
        UpdateStrip();
    }

    private void Mic_Click(object sender, RoutedEventArgs e)
    {
        QuickActions.ToggleMic();
        UpdateQuickButtons();
    }

    private void Dnd_Click(object sender, RoutedEventArgs e)
    {
        QuickActions.DoNotDisturb = !QuickActions.DoNotDisturb;
        UpdateQuickButtons();
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        Collapse();
        // Let the panel disappear before the snipping overlay freezes the screen.
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        t.Tick += (_, _) => { t.Stop(); QuickActions.Screenshot(); };
        t.Start();
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        Collapse();
        QuickActions.Lock();
    }

    // ── Bluetooth headphones ──────────────────────────────────────────
    public void SetHeadsets(IReadOnlyList<BluetoothBatteryWatcher.Device> devices)
    {
        if (devices.Count == 0)
        {
            HeadsetPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var first = devices[0];
        HeadsetText.Text = devices.Count == 1 ? $"{first.Percent}%" : $"{first.Percent}% +{devices.Count - 1}";
        HeadsetPanel.ToolTip = string.Join("\n", devices.Select(d => $"{d.Name}: {d.Percent}%"));
        HeadsetPanel.Visibility = Visibility.Visible;
    }

    // ── Weekly focus stats ────────────────────────────────────────────
    private void FocusViewToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _showWeek = !_showWeek;
        FocusMain.Visibility  = _showWeek ? Visibility.Collapsed : Visibility.Visible;
        FocusStats.Visibility = _showWeek ? Visibility.Visible   : Visibility.Collapsed;
        FocusViewToggle.Text  = _showWeek ? "‹ Trồng cây" : "Thống kê tuần ›";
        if (_showWeek) BuildWeekChart();
    }

    /// <summary>One bar per day for the last 7 days (minutes of grown trees), today on the right.</summary>
    private void BuildWeekChart()
    {
        var days = _focus.LastDays(7);
        int total  = days.Sum(d => d.Minutes);
        int trees  = days.Sum(d => d.Trees);
        int streak = _focus.Streak();
        WeekSummary.Text = $"{total} phút · {trees} cây · chuỗi {streak} ngày";

        WeekChart.Children.Clear();
        WeekChart.ColumnDefinitions.Clear();
        double max = Math.Max(30, days.Max(d => d.Minutes));
        const double barArea = 62;

        for (int i = 0; i < days.Count; i++)
        {
            var (day, minutes, _) = days[i];
            bool today = day == DateTime.Today;
            WeekChart.ColumnDefinitions.Add(new ColumnDefinition());

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(new TextBlock
            {
                Text = minutes > 0 ? minutes.ToString() : "",
                Foreground = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), FontSize = 10.5,
                FontFamily = new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
            });
            col.Children.Add(new Border
            {
                Width  = 26,
                Height = minutes > 0 ? Math.Max(4, barArea * minutes / max) : 3,
                CornerRadius = new CornerRadius(5, 5, 2, 2),
                Background   = minutes > 0
                    ? Frozen(today ? Color.FromRgb(0x34, 0xC7, 0x59) : Color.FromRgb(0x24, 0x8A, 0x3E))
                    : Frozen(Color.FromRgb(0x2A, 0x2A, 0x2C)),
                ToolTip = $"{Weekdays[(int)day.DayOfWeek]} {day:dd/MM}: {minutes} phút",
                HorizontalAlignment = HorizontalAlignment.Center
            });
            col.Children.Add(new TextBlock
            {
                Text = ShortDays[(int)day.DayOfWeek],
                Foreground = today ? Brushes.White : Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = 11, FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0)
            });

            Grid.SetColumn(col, i);
            WeekChart.Children.Add(col);
        }
    }

    // ── Clipboard history ─────────────────────────────────────────────
    private readonly Dictionary<Guid, BitmapSource?> _thumbs = new();
    private readonly DispatcherTimer _clipToastTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };

    private void RebuildClipboard()
    {
        if (!_expanded || _tab != "clipboard") return;

        var items  = _clips.Items;
        var texts  = items.Where(i => !i.IsImage).ToList();
        var images = items.Where(i => i.IsImage).ToList();

        ClipCount.Text = items.Count == 0 ? "" : $"{texts.Count} đoạn chữ · {images.Count} ảnh";
        ClipEmpty.Visibility       = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipClearBtn.Visibility    = items.Any(i => !i.Pinned) ? Visibility.Visible : Visibility.Collapsed;
        ClipImageScroll.Visibility = images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ClipTextList.Children.Clear();
        foreach (var item in texts) ClipTextList.Children.Add(MakeClipTextRow(item));

        ClipImageList.Children.Clear();
        foreach (var item in images) ClipImageList.Children.Add(MakeClipImageTile(item));

        // Forget thumbnails of deleted images
        foreach (var id in _thumbs.Keys.Where(id => images.All(i => i.Id != id)).ToList()) _thumbs.Remove(id);
    }

    private UIElement MakeClipTextRow(ClipItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pinMark = new TextBlock
        {
            Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11,
            Foreground = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A)), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0), Visibility = item.Pinned ? Visibility.Visible : Visibility.Collapsed
        };
        grid.Children.Add(pinMark);

        var full = item.Text ?? "";
        var line = new TextBlock
        {
            Text = full.ReplaceLineEndings(" ").Trim(), FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = full.Length > 600 ? full[..600] + "…" : full
        };
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);

        var when = new TextBlock
        {
            Text = item.Time.Date == DateTime.Today ? item.Time.ToString("HH:mm") : item.Time.ToString("dd/MM"),
            FontSize = 11, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
            Foreground = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 0, 0, 0)
        };
        var buttons = MakeClipButtons(item);
        buttons.Visibility = Visibility.Collapsed;
        var right = new Grid();
        right.Children.Add(when);
        right.Children.Add(buttons);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 6, 4),
            Background = Brushes.Transparent, Cursor = Cursors.Hand
        };
        var hover = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        row.MouseEnter += (_, _) => { row.Background = hover; when.Visibility = Visibility.Hidden; buttons.Visibility = Visibility.Visible; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; when.Visibility = Visibility.Visible; buttons.Visibility = Visibility.Collapsed; };
        row.MouseLeftButtonUp += (_, _) => { if (!buttons.IsMouseOver) CopyClip(item); };
        return row;
    }

    private UIElement MakeClipImageTile(ClipItem item)
    {
        if (!_thumbs.TryGetValue(item.Id, out var thumb))
            _thumbs[item.Id] = thumb = ClipboardService.LoadImage(item, 112);

        var box = new Grid { Width = 86, Height = 56 };
        box.Children.Add(new Border { Background = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E)) });
        if (thumb != null)
            box.Children.Add(new Image { Source = thumb, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center });

        if (item.Pinned)
        {
            box.Children.Add(new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(4),
                Background = Frozen(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10,
                    Foreground = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        var buttons = MakeClipButtons(item);
        buttons.Background = Frozen(Color.FromArgb(0xCC, 0x00, 0x00, 0x00));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.VerticalAlignment   = VerticalAlignment.Top;
        buttons.Visibility = Visibility.Collapsed;
        box.Children.Add(buttons);

        var tile = new Border
        {
            Child = box, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(1.5), BorderBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand, ClipToBounds = true,
            ToolTip = $"Ảnh · {item.Time:HH:mm dd/MM} · bấm để chép lại"
        };
        box.Clip = new RectangleGeometry(new Rect(0, 0, 86, 56), 8, 8);
        var accent = Frozen(_hasColour ? _accent : Colors.White);
        tile.MouseEnter += (_, _) => { tile.BorderBrush = accent; buttons.Visibility = Visibility.Visible; };
        tile.MouseLeave += (_, _) => { tile.BorderBrush = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)); buttons.Visibility = Visibility.Collapsed; };
        tile.MouseLeftButtonUp += (_, _) => { if (!buttons.IsMouseOver) CopyClip(item); };
        return tile;
    }

    /// <summary>Pin / unpin and delete, shown on hover.</summary>
    private StackPanel MakeClipButtons(ClipItem item)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var pin = new Button
        {
            Content = item.Pinned ? "" : "", Style = (Style)FindResource("QuickButton"),
            Width = 22, Height = 22, FontSize = 10, Margin = new Thickness(0),
            ToolTip = item.Pinned ? "Bỏ ghim" : "Ghim (không bị xoá)"
        };
        pin.Click += (_, e) => { e.Handled = true; _clips.TogglePin(item); };
        var del = new Button
        {
            Content = "", Style = (Style)FindResource("QuickButton"),
            Width = 22, Height = 22, FontSize = 10, Margin = new Thickness(0), ToolTip = "Xoá"
        };
        del.Click += (_, e) => { e.Handled = true; _clips.Delete(item); };
        panel.Children.Add(pin);
        panel.Children.Add(del);
        return panel;
    }

    /// <summary>Puts the item back on the clipboard, then closes so you can paste.</summary>
    private void CopyClip(ClipItem item)
    {
        bool ok = _clips.CopyBack(item);
        ClipToast.Text = ok ? "Đã chép · dán bằng Ctrl + V" : "Không chép được, thử lại nhé";
        ClipToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        if (!ok) return;
        _clipToastTimer.Stop();
        _clipToastTimer.Tick -= ClipToastDone;
        _clipToastTimer.Tick += ClipToastDone;
        _clipToastTimer.Start();
    }

    private void ClipToastDone(object? sender, EventArgs e)
    {
        _clipToastTimer.Stop();
        ClipToast.BeginAnimation(OpacityProperty, null);
        ClipToast.Opacity = 0;
        Collapse();
    }

    private void ClipClear_Click(object sender, RoutedEventArgs e) => _clips.ClearUnpinned();

    /// <summary>The mouse wheel scrolls the image row sideways.</summary>
    private void ClipImageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ClipImageScroll.ScrollToHorizontalOffset(ClipImageScroll.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    // ── Mây the cat ───────────────────────────────────────────────────
    private IEnumerable<PixelCat> Cats => new[] { CatRest, CatPanel, CatTree };

    /// <summary>A short reaction: happy hops, or sad after giving up.</summary>
    private void CatReact(CatMood mood, double seconds)
    {
        _catOverride      = mood;
        _catOverrideUntil = DateTime.Now.AddSeconds(seconds);
        UpdateCat();
    }

    private void UpdateCat()
    {
        CatMood mood;
        if (_catOverride is { } o && DateTime.Now < _catOverrideUntil)
        {
            mood = o;
        }
        else
        {
            _catOverride = null;
            mood = _focus.IsRunning ? CatMood.Focus                                  // sits by the tree
                 : _hasMusic && _playing ? CatMood.Dance                             // dances to the music
                 : NativeInterop.IdleTime() >= TimeSpan.FromMinutes(10) ? CatMood.Sleep
                 : CatMood.Idle;
        }

        // Every tree you grow is a meal; Mây grows a little with each one.
        int food = _focus.TotalTrees;
        double growth = 1 + Math.Min(0.3, food * 0.015);
        foreach (var cat in Cats)
        {
            cat.Mood   = mood;
            cat.Growth = growth;
        }

        string doing = mood switch
        {
            CatMood.Dance => "đang nhảy theo nhạc",
            CatMood.Focus => "đang ngồi canh cây",
            CatMood.Happy => "đang vui lắm",
            CatMood.Sad   => "hơi buồn vì cây chết",
            CatMood.Sleep => "đang ngủ",
            _             => "đang ngồi chơi"
        };
        CatPanel.ToolTip = $"Mây {doing} · đã được ăn {food} phần (mỗi cây trồng xong là 1 phần)";
        CatTree.ToolTip  = CatPanel.ToolTip;
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
