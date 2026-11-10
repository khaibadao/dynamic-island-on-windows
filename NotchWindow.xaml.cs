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
    private readonly ClipboardService _clips;
    private int _animation;          // bumped by every open / close, so an old animation can't finish a new one
    private string? _pendingTab;     // a tab to open as soon as the notch is visible again
    private bool _typing;            // the to-do box has the keyboard
    private string _tab = "music";

    private const double PanelWidth       = 620;   // same frame as v1.7
    private const double PanelWidthLyrics = 920;
    private const double LyricsWidth      = 300;

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

    private static readonly Color DndColour = Color.FromRgb(0x8E, 0x7C, 0xFF);
    private static readonly string[] ShortDays = { "CN", "T2", "T3", "T4", "T5", "T6", "T7" };

    private static readonly string[] Weekdays =
        { "Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy" };

    /// <summary>"Mute this app" from a notification card.</summary>
    public event Action<NotificationData>? MuteRequested;

    private static readonly string[] Tabs     = { "music", "focus", "notifications", "clipboard", "deadlines", "schedule" };
    private static readonly string[] TabNames = { "Nhạc", "Trồng cây", "Thông báo", "Copy", "Hạn chót & ghi chú", "Lịch học" };
    private static readonly string[] TabGlyphs = { "\uE8D6", "", "\uEA8F", "\uE77F", "\uE787", "\uE8BF" };   // tree is drawn

    /// <summary>Each tab's icon has its own colour (the tree draws its own).</summary>
    private static readonly Color[] TabColours =
    {
        Color.FromRgb(0xFF, 0x5C, 0x7A),   // Nhạc: pink-red
        Color.FromRgb(0x34, 0xC7, 0x59),   // Trồng cây: green
        Color.FromRgb(0xFF, 0xB0, 0x20),   // Thông báo: orange-yellow
        Color.FromRgb(0x4D, 0xA3, 0xFF),   // Copy: blue
        Color.FromRgb(0xB3, 0x8C, 0xFF),   // Hạn chót: purple
        Color.FromRgb(0x34, 0xC7, 0xC7),   // Lịch học: teal
    };
    private string _clipMode = "texts";   // the Copy tab's section: "texts" or "images"

    public NotchWindow(MediaWatcher media, FocusService focus, NotificationHistory history, ClipboardService clips)
    {
        InitializeComponent();
        _media   = media;
        _focus   = focus;
        _history = history;
        _clips   = clips;
        _clips.Changed += () => Dispatcher.BeginInvoke(new Action(RebuildClipboard));
        Deactivated += (_, _) => { if (_typing) EndTyping(collapse: !NativeInterop.IsCursorOver(Expanded)); };

        _hoverTimer.Tick  += (_, _) => WatchPointer();
        _closeTimer.Tick  += (_, _) =>
        {
            _closeTimer.Stop();
            // Only the visible panel counts, not the window's transparent area.
            if (!_seeking && !_typing && !_pinned && !_dragging && !_pickingFile && !NativeInterop.IsCursorOver(Expanded)) Collapse();
        };
        _volumeTimer.Tick += (_, _) => { _volumeTimer.Stop(); VolumeView.Visibility = Visibility.Collapsed; ShowRest(); };
        _headsetTimer.Tick += (_, _) => HideHeadset();
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
        {
            double after = _focus.FocusedHours, before = after - session.Minutes / 60.0;
            bool unlocked = session.Success && TreeCatalog.All.Any(t => t.UnlockHours > before && t.UnlockHours <= after);
            CatReact(unlocked ? CatMood.Excited : session.Success ? CatMood.Happy : CatMood.Sad, unlocked ? 10 : session.Success ? 8 : 10);
            React(session.Success ? "love" : "sad");
        }));
        CatPanel.MouseLeftButtonUp += (_, _) => CatReact(CatMood.Happy, 2.5);   // a pat on the head
        CatRest.SizeChanged  += (_, _) => PlaceCat();
        RestArea.SizeChanged += (_, _) => PlaceCat();

        var s = AppSettings.Current;
        _selectedMinutes = Math.Clamp(s.LastFocusMinutes, FocusService.MinMinutes, FocusService.MaxMinutes);
        _selectedKind    = TreeCatalog.Parse(s.LastTreeKind);
        if (!_focus.IsUnlocked(_selectedKind)) _selectedKind = TreeKind.Oak;
        _lyricsOpen      = s.LyricsOpen;
        SetUpFocus();

        _focus.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateFocusUi));

        BuildSideBar();

        _ready = true;
        SetUpAmbient();
        ApplyCatVisibility();
        _clips.Start(this);   // creates the window handle too
        // Two-finger sideways swipe / tilt wheel → previous / next tab
        System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(SidewaysHook);
        // Ctrl + Alt + N anywhere: a quick note (skipped quietly if another app already uses it)
        if (!NativeInterop.RegisterQuickNoteHotkey(new System.Windows.Interop.WindowInteropHelper(this).Handle))
            CrashLog.Write("Ctrl+Alt+N is taken by another app: quick note only from the panel");
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
            ScheduleWalk();
            _eyesTimer.Start();
        };
        _walkTimer.Tick += (_, _) => { _walkTimer.Stop(); StartWalk(); ScheduleWalk(); };
        _eyesTimer.Tick += (_, _) => EyesTick();
        Eyes.Finished += () => { if (_zoneSince == null || !CanShowEyes) GrowForEyes(false); };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.PrimaryScreenWidth)) Recenter();
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Recenter));
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Recenter));
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

    private bool _presenting;

    /// <summary>A slide show or screen share is on: step aside until it ends.</summary>
    public void SetPresenting(bool on)
    {
        _presenting = on;
        UpdateVisibility();
    }

    /// <summary>A notification is using the top of the screen: step aside.</summary>
    public void SetIslandActive(bool active)
    {
        _islandActive = active;
        UpdateVisibility();
    }

    private void CheckFullscreen()
    {
        bool fs = AppSettings.Current.HideInFullscreen && NativeInterop.IsForegroundFullscreenOnPrimary();
        if (fs == _fullscreen) return;
        _fullscreen = fs;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool show = _enabled && !_islandActive && !_fullscreen && !_presenting;
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
        NativeInterop.PlaceTopCenter(this, 0);   // exact, in real pixels (any display scale)
    }

    // ── Resting: watch the pointer (the window lets clicks through) ──
    private void WatchPointer()
    {
        // In the open panel: pointer close to Mây → she rolls over for a belly rub
        if (_expanded) CatPanel.BellyUp = NativeInterop.IsCursorNear(CatPanel, 18);
        if (_expanded && _pinned && NativeInterop.KeyDown(0x1B) && NativeInterop.IsCursorOver(Expanded)) { Pinned = false; Collapse(); }
        if (!IsVisible || _expanded) { _dwellStart = null; return; }

        FrameworkElement? target = Pill.IsVisible ? Pill : StripHost.IsVisible ? StripHost : null;
        if (target == null) { _dwellStart = null; SetFaded(false); return; }

        // Only the very top of the notch (the screen's edge) opens it. Lower down, on
        // the pill, you're usually aiming at a browser tab under it: the pill all but
        // disappears and clicks go straight through to the tab.
        bool over = NativeInterop.IsCursorOver(target);
        bool atTop = over && NativeInterop.CursorBelowTop(this) <= TopEdgeZone;
        SetFaded(over && target == Pill && !atTop);

        // Pointer in the area around it (≈ the tab bar): the eyes come out and watch it.
        if (target == Pill) WatchWithEyes(over);

        if (!atTop) { _dwellStart = null; return; }

        _dwellStart ??= DateTime.UtcNow;
        double need = target == Pill ? 250 : 150;
        if ((DateTime.UtcNow - _dwellStart.Value).TotalMilliseconds >= need)
        {
            _dwellStart = null;
            Expand();
        }
    }

    /// <summary>The top strip of the notch (WPF units) where the pointer opens it.</summary>
    private const double TopEdgeZone = 7;

    private void SetFaded(bool faded)
    {
        if (faded == _faded) return;
        _faded = faded;
        if (faded) EndWalk();
        var anim = new DoubleAnimation(faded ? 0.07 : 1, TimeSpan.FromMilliseconds(140));   // nearly invisible
        RestArea.BeginAnimation(OpacityProperty, anim);
        CatRest.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Resting look: the slim pill while music plays or a tree grows, else the strip.</summary>
    private void ShowRest()
    {
        if (_expanded || VolumeView.Visibility == Visibility.Visible || HeadsetView.Visibility == Visibility.Visible) return;
        bool pill = true;   // the LED clock pill is always there
        Pill.Visibility      = pill ? Visibility.Visible : Visibility.Collapsed;
        StripHost.Visibility = pill ? Visibility.Collapsed : Visibility.Visible;
        RestArea.Visibility  = Visibility.Visible;
        CatRest.Visibility   = AppSettings.Current.ShowCat ? Visibility.Visible : Visibility.Collapsed;
        PlaceCat();
    }

    // ── Mây's strolls across the pill ────────────────────────────────
    private readonly DispatcherTimer _walkTimer = new();
    private readonly Random _rng = new();
    private bool _walking;
    private int _walkGen;

    /// <summary>Next stroll in 10–15 s.</summary>
    private void ScheduleWalk()
    {
        _walkTimer.Interval = TimeSpan.FromSeconds(10 + _rng.NextDouble() * 5);
        _walkTimer.Start();
    }

    /// <summary>Out from behind one end of the pill, along it to the other end, where she stays.</summary>
    private void StartWalk()
    {
        if (!AppSettings.Current.ShowCat || _inCall) return;
        if (_walking || Eyes.Active || !AppSettings.Current.MayWalks || _expanded || !IsVisible || !Pill.IsVisible || _faded
            || _focus.IsRunning || VolumeView.Visibility == Visibility.Visible
            || NativeInterop.IdleTime() >= TimeSpan.FromMinutes(10)) return;   // asleep, or you're away
        double w = WalkLayer.ActualWidth;
        if (w < 40) return;

        _walking = true;
        int gen = ++_walkGen;
        double right = w - 10, left = -8;
        bool toLeft = !_catOnLeft;   // walk over to the other end, and stay there until next time
        var dur = TimeSpan.FromSeconds(2.6);

        CatRest.Visibility = Visibility.Hidden;   // the peeking head becomes the walking cat
        Walker.Visibility  = Visibility.Visible;
        Walker.FacingRight = !toLeft;
        Walker.StartWalking();

        var go = new DoubleAnimation(toLeft ? right : left, toLeft ? left : right, dur);
        go.Completed += (_, _) =>
        {
            if (gen != _walkGen) return;
            _catOnLeft = toLeft;
            PlaceCat();
            EndWalk();
        };
        WalkerShift.BeginAnimation(TranslateTransform.XProperty, go);
    }

    /// <summary>Back to peeking from behind the pill (also when interrupted).</summary>
    private void EndWalk()
    {
        if (!_walking) return;
        _walking = false;
        _walkGen++;
        WalkerShift.BeginAnimation(TranslateTransform.XProperty, null);
        Walker.StopWalking();
        Walker.Visibility = Visibility.Collapsed;
        if (!_expanded && VolumeView.Visibility != Visibility.Visible && AppSettings.Current.ShowCat) CatRest.Visibility = Visibility.Visible;

        // Eyes that were due while Mây was out come right after her
        if (_eyesPending is { } kind)
        {
            _eyesPending = null;
            Dispatcher.BeginInvoke(new Action(() => ShowEyes(kind)));
        }
    }

    // ── Eyes watching the pointer ─────────────────────────────────────
    private const double ZoneHalfWidth = 240;
    /// <summary>How far down the eyes notice the pointer: the browser's tab + bookmark bars, plus a fifth of the screen.</summary>
    private static double ZoneHeight => 100 + SystemParameters.PrimaryScreenHeight * 0.2;
    private DateTime? _zoneSince, _leftZoneAt;
    private DateTime _lastMove = DateTime.UtcNow, _lastDizzy;
    private Point _lastCursor, _lastDir;
    private int _lastSign;
    private readonly Queue<DateTime> _reversals = new();

    private void WatchWithEyes(bool overPill)
    {
        var now = DateTime.UtcNow;
        var c = NativeInterop.CursorIn(Pill);
        double w = Pill.ActualWidth, h = Pill.ActualHeight;
        bool inZone = !_inCall && c is { } p0 && Math.Abs(p0.X - w / 2) <= ZoneHalfWidth && p0.Y >= -4 && p0.Y <= ZoneHeight;

        if (!inZone || c is not { } p || !CanShowEyes)
        {
            _zoneSince = null;
            _reversals.Clear();
            if (Eyes.Following)
            {
                // Keep looking where the pointer went for a moment, then back to the clock
                _leftZoneAt ??= now;
                if ((now - _leftZoneAt.Value).TotalSeconds < 1) Eyes.Follow(_lastDir);
                else { Eyes.Follow(null); _leftZoneAt = null; }
            }
            return;
        }
        _leftZoneAt = null;
        _zoneSince ??= now;

        // How the pointer moves: still for a while → drowsy; shaken back and forth → dizzy
        double dx = p.X - _lastCursor.X, dy = p.Y - _lastCursor.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) _lastMove = now;
        if (Math.Abs(dx) > 12)
        {
            int sign = Math.Sign(dx);
            if (_lastSign != 0 && sign != _lastSign) _reversals.Enqueue(now);
            _lastSign = sign;
        }
        _lastCursor = p;
        while (_reversals.Count > 0 && (now - _reversals.Peek()).TotalSeconds > 1.2) _reversals.Dequeue();
        if (_reversals.Count >= 4 && (now - _lastDizzy).TotalSeconds > 8)
        {
            _lastDizzy = now;
            _reversals.Clear();
            ShowEyes("dizzy");
            return;
        }

        // Only once the pointer stays around for 0.3 s (not when it just passes by to another tab)
        if ((now - _zoneSince.Value).TotalSeconds < 0.3) return;

        _lastDir = new Point((p.X - w / 2) / (w / 2 + 80), (p.Y - h / 2) / 60);
        if (!Eyes.Active) ShowEyes(null);
        Eyes.Drowsy = (now - _lastMove).TotalSeconds > 4;
        Eyes.Squint = overPill;
        Eyes.Follow(_lastDir);
    }

    // ── Eyes on the notch ─────────────────────────────────────────────
    private readonly DispatcherTimer _eyesTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _eyesPending;
    private bool _eyesUp;

    private bool CanShowEyes => !_expanded && IsVisible && Pill.IsVisible && VolumeView.Visibility != Visibility.Visible;

    /// <summary>Plays an expression on the notch now (e.g. "surprise" for a notification). False if it can't show.</summary>
    public bool React(string kind)
    {
        if (!CanShowEyes) return false;
        return ShowEyes(kind);
    }

    /// <summary>Every 30 s: an expression that suits the moment.</summary>
    private void EyesTick()
    {
        if (!CanShowEyes || Eyes.Active || _faded || _inCall) return;
        // Sleepy only when you've really stopped (no key or mouse for 3 minutes); otherwise a random face
        string kind = NativeInterop.IdleTime() >= TimeSpan.FromMinutes(3) ? "sleepy"
                    : _hasMusic && _playing && _rng.Next(3) == 0 ? "happy"   // music: now and then a happy bob
                    : RandomFace();
        if (_walking) { _eyesPending = kind; return; }   // after Mây's stroll
        _idleFace = true;
        ShowEyes(kind);
    }

    private string? _lastFace;
    private bool _idleFace;   // the next ShowEyes is a random idle face (watching you may cut it short)

    /// <summary>A random idle face, never the same one twice in a row.</summary>
    private string RandomFace()
    {
        string face;
        do face = NotchEyes.Idle[_rng.Next(NotchEyes.Idle.Length)];
        while (face == _lastFace && NotchEyes.Idle.Length > 1);
        _lastFace = face;
        return face;
    }

    /// <summary>The pill grows a little and the eyes take the clock's place (kind null = follow the pointer).</summary>
    private bool ShowEyes(string? kind)
    {
        if (!CanShowEyes) return false;
        if (_walking) EndWalk();
        GrowForEyes(true);
        if (kind != null) Eyes.Play(kind, interruptible: _idleFace);
        _idleFace = false;
        return true;
    }

    private void GrowForEyes(bool up)
    {
        if (up == _eyesUp) return;
        _eyesUp = up;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(260);
        if (up) Eyes.Visibility = Visibility.Visible;
        var width = new DoubleAnimation(up ? 66 : 0, dur) { EasingFunction = ease };
        if (!up) width.Completed += (_, _) => { if (!_eyesUp) Eyes.Visibility = Visibility.Collapsed; };
        Eyes.BeginAnimation(WidthProperty, width);
        Eyes.BeginAnimation(HeightProperty, new DoubleAnimation(up ? 28 : 22, dur) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(up ? 31 : 24, dur) { EasingFunction = ease });
        PillFace.BeginAnimation(OpacityProperty, new DoubleAnimation(up ? 0 : 1, TimeSpan.FromMilliseconds(up ? 160 : 260)));
    }

    /// <summary>Mây peeks out from behind the right end of the strip / pill.</summary>
    private bool _catOnLeft;                                  // which end of the notch Mây peeks from
    private readonly ScaleTransform _catFlip = new(1, 1, 13, 0);

    private void PlaceCat()
    {
        if (CatRest.RenderTransform != _catGroup)
        {
            _catGroup.Children.Clear();
            _catGroup.Children.Add(_catFlip);
            _catGroup.Children.Add(CatRestShift);
            CatRest.RenderTransform = _catGroup;
        }
        double half = RestArea.ActualWidth > 0 ? RestArea.ActualWidth / 2 : 100;
        // Her head is half tucked behind the notch's end; on the left she's mirrored.
        double x = half - MayPeek.HiddenPart + CatRest.Width / 2;
        _catFlip.ScaleX = _catOnLeft ? -1 : 1;
        CatRestShift.X  = _catOnLeft ? -x : x;
    }
    private readonly TransformGroup _catGroup = new();

    private void Expanded_MouseEnter(object sender, MouseEventArgs e) => _closeTimer.Stop();
    private void Expanded_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_seeking && !_typing && !_pinned && !_dragging) _closeTimer.Start();
    }

    // ── Open / close ──────────────────────────────────────────────────
    private void Expand() => Expand(null);

    /// <summary>Opens the panel (on <paramref name="tab"/>, or the tab used last).</summary>
    private void Expand(string? tab)
    {
        StallWatch.Doing = "opening the panel";
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
        EndWalk();
        Eyes.Stop();
        GrowForEyes(false);
        SetFaded(false);
        VolumeView.Visibility = Visibility.Collapsed;
        _headsetTimer.Stop();
        HeadsetView.Visibility = Visibility.Collapsed;
        RestArea.Visibility   = Visibility.Collapsed;
        CatRest.Visibility    = Visibility.Collapsed;
        Expanded.Visibility   = Visibility.Visible;

        UpdateClock();
        Dispatcher.BeginInvoke(new Action(UpdateQuickButtons), DispatcherPriority.Background);
        UpdateCat();

        // Open on the tab asked for, else the one used last; straight to the tree while one grows.
        tab ??= _focus.IsRunning ? "focus" : AppSettings.Current.LastNotchTab;
        if (Array.IndexOf(Tabs, tab) < 0) tab = "music";
        ShowTab(tab, direction: 0);

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
            sy.Completed += (_, _) => { if (gen == _animation) { EndMorph(); AmbientOpened(true); } };
            Expanded.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(160)));
            ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }

        if (wasOpen) AmbientOpened(true);
        if (!_typing && !_pinned) _closeTimer.Start();   // closes again if the mouse just passed by
    }

    private void Collapse()
    {
        _closeTimer.Stop();
        StallWatch.Doing = "closing the panel";
        if (!_expanded) return;
        _expanded = false;
        _seeking  = false;
        if (_typing) EndTyping(collapse: false);
        int gen = ++_animation;
        NativeInterop.SetClickThrough(this, true);
        if (ClipSearch.Text.Length > 0) ClipSearch.Text = "";   // clicks go through right away

        StopMarquee();
        ResetGiveUp();
        AmbientOpened(false);
        _lyricsTimer.Stop();
        CatPanel.BellyUp = false;

        // Shrinks back into the notch, then the resting notch returns.
        bool pill = true;   // the LED clock pill is always there
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
    }

    // ── Tabs: icon sidebar on the left, sideways swipe ────────────────
    private void StepTab(int step)
    {
        int i = Array.IndexOf(Tabs, _tab);
        int next = ((i < 0 ? 0 : i) + step + Tabs.Length) % Tabs.Length;
        SelectTab(Tabs[next], step);
    }

    private void SelectTab(string tab, int direction)
    {
        if (tab == _tab && Expanded.IsVisible) return;
        if (_typing) EndTyping(collapse: false);
        AppSettings.Current.LastNotchTab = tab;
        AppSettings.Current.Save();
        ShowTab(tab, direction);
    }

    private readonly List<(Border Button, FrameworkElement Icon, Border Mark)> _side = new();

    private void BuildSideBar()
    {
        SideBar.Children.Clear();
        _side.Clear();
        for (int i = 0; i < Tabs.Length; i++)
        {
            FrameworkElement icon = Tabs[i] == "focus"
                ? new TreeShape { LayoutTransform = new ScaleTransform(0.15, 0.15), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : new TextBlock
                {
                    Text = TabGlyphs[i], FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
            // A small bar on the left edge marks the open tab
            var mark = new Border
            {
                Width = 3, Height = 12, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-1, 0, 0, 0), Opacity = 0
            };
            var content = new Grid();
            content.Children.Add(icon);
            content.Children.Add(mark);
            var button = new Border
            {
                Width = 32, Height = 26, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = content, ToolTip = TabNames[i]
            };
            var tab = Tabs[i];
            int index = i;
            button.MouseEnter += (_, _) => { if (_tab != tab) button.Background = Frozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)); };
            button.MouseLeave += (_, _) => { if (_tab != tab) button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += (_, _) => SelectTab(tab, index - Array.IndexOf(Tabs, _tab));
            SideBar.Children.Add(button);
            _side.Add((button, icon, mark));
        }
    }

    private void UpdateSideBar()
    {
        var accent = _hasColour ? _accent : Colors.White;
        int current = Array.IndexOf(Tabs, _tab);
        for (int i = 0; i < _side.Count; i++)
        {
            var (button, icon, mark) = _side[i];
            bool on = i == current;
            var c = TabColours[Math.Min(i, TabColours.Length - 1)];
            // Open tab: its colour, bright, on a soft tint of it; the others keep their colour, dimmer
            button.Background = on ? Frozen(Color.FromArgb(0x33, c.R, c.G, c.B)) : Brushes.Transparent;
            if (icon is TextBlock t) t.Foreground = Frozen(on ? c : Color.FromArgb(0xA0, c.R, c.G, c.B));
            icon.Opacity = on ? 1 : 0.8;
            mark.Background = Frozen(accent);
            mark.Opacity = 0;
        }
    }

    private const int WM_MOUSEHWHEEL = 0x020E;
    private int _sideways;
    private DateTime _lastSwipe;

    /// <summary>A sideways swipe on the touchpad (or a tilt wheel) flips tabs, one per gesture.</summary>
    private IntPtr SidewaysHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeInterop.WM_HOTKEY && wParam.ToInt32() == NativeInterop.QuickNoteHotkey)
        {
            OpenQuickNote();
            handled = true;
            return IntPtr.Zero;
        }
        if (msg != WM_MOUSEHWHEEL || !_expanded) return IntPtr.Zero;
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        // The image row scrolls sideways itself.
        if (ImagesTab.IsVisible && NativeInterop.IsCursorOver(ClipImageScroll))
        {
            ClipImageScroll.ScrollToHorizontalOffset(ClipImageScroll.HorizontalOffset + delta / 2.0);
            handled = true;
            return IntPtr.Zero;
        }
        if ((DateTime.UtcNow - _lastSwipe).TotalMilliseconds < 450) { handled = true; return IntPtr.Zero; }
        _sideways += delta;
        if (Math.Abs(_sideways) >= 120)
        {
            StepTab(_sideways > 0 ? 1 : -1);
            _sideways = 0;
            _lastSwipe = DateTime.UtcNow;
        }
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>The mouse wheel scrolls a sideways row (filters, images).</summary>
    private void SidewaysWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private void ShowTab(string tab, int direction)
    {
        StallWatch.Doing = $"tab {tab}";
        bool animate = _tab != tab && Expanded.IsVisible && Expanded.Opacity > 0.9;
        bool animateWidth = animate;
        _tab = tab;
        ContentCard.BeginAnimation(HeightProperty, null);
        double fromHeight = ContentCard.ActualHeight;
        MusicTab.Visibility         = tab == "music"         ? Visibility.Visible : Visibility.Collapsed;
        FocusTab.Visibility         = tab == "focus"         ? Visibility.Visible : Visibility.Collapsed;
        NotificationsTab.Visibility = tab == "notifications" ? Visibility.Visible : Visibility.Collapsed;
        ClipboardTab.Visibility     = tab == "clipboard"     ? Visibility.Visible : Visibility.Collapsed;
        DeadlinesTab.Visibility     = tab == "deadlines"     ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTab.Visibility      = tab == "schedule"      ? Visibility.Visible : Visibility.Collapsed;
        ApplyAmbient();
        UpdateSideBar();

        // Slide in from the side we're moving to, with a soft fade
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TabContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        if (direction != 0)
            TabShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(Math.Sign(direction) * 36, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

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
                if (_focusView == "stats") BuildStats();
                if (_focusView == "pick") BuildSpecies();
                break;
            case "notifications":
                RebuildRecent();
                break;
            case "clipboard":
                // Open on the kind of thing copied last
                var last = _clips.Items.OrderByDescending(i => i.Time).FirstOrDefault();
                if (last != null) _clipMode = last.IsImage ? "images" : "texts";
                RebuildClipboard();
                break;
            case "deadlines":
                RebuildDeadlines();
                break;
            case "schedule":
                _scheduleDay = DateTime.Today;
                ShowScheduleImport(!ScheduleServiceHasAny);
                break;
        }
        ApplyLyricsLayout(animateWidth);

        // The panel's height follows the tab: glide from the old height to the new one.
        if (animate && fromHeight > 0)
        {
            ContentCard.UpdateLayout();
            double to = ContentCard.ActualHeight;
            if (Math.Abs(to - fromHeight) > 1)
                ContentCard.BeginAnimation(HeightProperty, new DoubleAnimation(fromHeight, to, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
                });
        }
    }

    // ── Every second ──────────────────────────────────────────────────
    private void OnTick()
    {
        if (_focus.IsRunning || _pomo?.OnBreak == true) UpdateFocusTick();
        UpdatePillClock();
        UpdateCat();
        if (_expanded)
        {
            UpdateClock();
            if (_hasMusic && MusicTab.IsVisible) UpdateProgress();
            if (ClipboardTab.IsVisible) RefreshClipTimes();

            // Safety net in case MouseLeave never came: close once the pointer is gone.
            if (!_seeking && !_typing && !_pinned && !_dragging && !_closeTimer.IsEnabled && !NativeInterop.IsCursorOver(Expanded))
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

        // Sound bars only while music actually plays; they're how you know it's on.
        PillBars.Visibility = _hasMusic && _playing ? Visibility.Visible : Visibility.Collapsed;
        UpdatePillClock();
        UpdateStrip();
        ShowRest();
    }

    private static readonly Color FocusGreen = Color.FromRgb(0x34, 0xC7, 0x59);
    private static readonly Brush PillWhite = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static readonly Brush PillRim   = Frozen(Color.FromRgb(0xBF, 0xBF, 0xBF));

    /// <summary>
    /// The LED clock: the time (HH:mm), or the time left on the tree (mm:ss) in green.
    /// Lit in the song's colour while music plays; the rim matches.
    /// </summary>
    private void UpdatePillClock()
    {
        bool focus = _focus.IsRunning;
        if (focus)
        {
            var left = _focus.Remaining;
            PillClock.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
        }
        else
        {
            PillClock.Text = DateTime.Now.ToString("HH:mm");
        }

        // In class (and no tree growing): "OSG202 · 25′" next to the time
        var lesson = focus ? null : ScheduleService.Now(DateTime.Now);
        if (lesson != null)
        {
            int left = Math.Max(1, (int)Math.Ceiling((lesson.End - DateTime.Now.TimeOfDay).TotalMinutes));
            PillClass.Text = $"{Short(lesson.Subject, 10)} · {left}\u2032";
        }
        PillClass.Visibility = lesson != null ? Visibility.Visible : Visibility.Collapsed;

        // Style C: one fixed white for the digits, the bars and (a little softer) the rim
        PillLeaf.Visibility   = focus ? Visibility.Visible : Visibility.Collapsed;
        SwayLeaf(focus);
        DndDot.Visibility     = QuickActions.DoNotDisturb ? Visibility.Visible : Visibility.Collapsed;
        PillClock.On          = PillWhite;
        PillClock.Off         = null;
        PillClockGlow.Opacity = 0;
        Pill.BorderBrush      = PillRim;
        PillGlow.Color        = Neutral;
        PillGlow.Opacity      = 0.25;
    }

    private static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

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
        UpdatePillClock();
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
            if (_ambientArt != null) SetAmbientArt(null);
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

        SongArtistRun.Text = state.Artist;
        MusicAppRun.Text   = string.IsNullOrWhiteSpace(state.AppName) ? ""
                           : string.IsNullOrWhiteSpace(state.Artist) ? state.AppName : "  ·  " + state.AppName;
        FullArt.ImageSource = state.Art;
        if (!ReferenceEquals(state.Art, _ambientArt)) SetAmbientArt(state.Art);
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
        foreach (var bar in Bars) bar.Fill = PillWhite;   // fixed, like the clock
        if (_ready) UpdateSideBar();
        SeekBar.Accent = Brushes.White;

        // The whole panel takes the song's colour, fading to near-black (like Apple Music)
        if (cover is { } baseColor)
        {
            var tint = ColorExtractor.Mix(baseColor, vivid, 0.45);
            var stops = new GradientStopCollection
            {
                new(ColorExtractor.Mix(tint, Colors.Black, 0.50), 0),
                new(ColorExtractor.Mix(tint, Colors.Black, 0.76), 0.55),
                new(ColorExtractor.Mix(tint, Colors.Black, 0.93), 1)
            };
            var bg = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 1));
            bg.Freeze();
            Expanded.Background = bg;
        }
        else
        {
            Expanded.Background = Brushes.Black;
        }

        var edge = hasColour ? ColorExtractor.Mix(vivid, Colors.Black, 0.35) : Color.FromRgb(0x3A, 0x3A, 0x3C);
        var border = new SolidColorBrush(edge);
        border.Freeze();
        Expanded.BorderBrush   = border;
        VolumeView.BorderBrush = border;

        PillGlow.Color       = hasColour ? vivid : Neutral;
        PillGlow.Opacity     = hasColour ? 0.45 : 0.3;
        ExpandedGlow.Color   = hasColour ? vivid : Neutral;
        ExpandedGlow.Opacity = hasColour ? 0.4 : 0.3;
        UpdateStrip();
    }

    // ── Loang màu (colour wash from the cover) ────────────────────────
    private BitmapSource? _ambientArt;
    private bool _ambientSlow;          // this computer couldn't keep up: still picture until changed in the tray
    private bool _ambientOpen;
    private bool _energySaver;

    /// <summary>"Máy hơi yếu…" and similar one-off notes for the island.</summary>
    public event Action<string, string>? Notice;

    private void SetUpAmbient()
    {
        try
        {
            _energySaver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            PowerManager.EnergySaverStatusChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _energySaver = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
                ApplyAmbient();
            }));
        }
        catch { }
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.BeginInvoke(new Action(ApplyAmbient));
        };
        RenderCapability.TierChanged += (_, _) => ApplyAmbient();
        ApplyAmbient();
    }

    /// <summary>Why the wash is weaker than chosen in the tray, or null.</summary>
    public string? AmbientReason
    {
        get
        {
            if (AppSettings.Current.Ambient == "off") return null;
            if ((RenderCapability.Tier >> 16) < 2) return "Máy không có tăng tốc đồ họa";
            if (AppSettings.Current.Ambient == "light") return null;
            if (_inCall) return "Đang gọi video (camera bật): tạm dừng cho máy nhẹ";
            if (AppSettings.Current.RespectWindowsAnimations && !SystemParameters.ClientAreaAnimation) return "Windows đang tắt hiệu ứng hoạt ảnh";
            if (_ambientSlow) return "Máy hơi yếu nên đã tự giảm";
            if (_energySaver) return "Tiết kiệm pin: nốt trôi chậm hơn để đỡ tốn pin";
            return null;
        }
    }

    // ── In a video call (camera on): fewer effects, the call gets the PC ──
    private bool _inCall;

    /// <summary>Camera in use: the notes stand still, Mây and the eyes keep still. Off: all back.</summary>
    public void SetInCall(bool on)
    {
        if (on == _inCall) return;
        _inCall = on;
        if (on) { EndWalk(); Eyes.Stop(); GrowForEyes(false); }
        ApplyAmbient();
        UpdateCat();
    }

    private AmbientLevel AmbientLevelNow()
    {
        var chosen = AppSettings.Current.Ambient;
        if (_inCall && chosen != "off" && (RenderCapability.Tier >> 16) >= 2) return AmbientLevel.Light;
        if (chosen == "off" || (RenderCapability.Tier >> 16) < 2) return AmbientLevel.Off;
        // Windows' "Animation effects" switch only counts if you asked for that in Cài đặt
        bool windowsSaysStill = AppSettings.Current.RespectWindowsAnimations && !SystemParameters.ClientAreaAnimation;
        if (chosen == "light" || windowsSaysStill || _ambientSlow) return AmbientLevel.Light;
        return AmbientLevel.Full;
    }

    /// <summary>A switch in Cài đặt & quyền riêng tư changed.</summary>
    public void ApplySettings()
    {
        SetBarsPlaying(_playing);
        ApplyAmbient();
    }

    /// <summary>After the tray choice changes (choosing again also forgets "máy hơi yếu").</summary>
    public void ReapplyAmbient()
    {
        _ambientSlow = false;
        ApplyAmbient();
    }

    private void ApplyAmbient()
    {
        Ambient.FrameRate = _energySaver ? 30 : 60;   // battery saver: still drifting, just fewer frames
        Ambient.Level = AmbientLevelNow();
        // Lịch học keeps a plain black background: the blocks are easier to read
        bool plain = _tab == "schedule";
        Ambient.Visibility = plain ? Visibility.Hidden : Visibility.Visible;
        Ambient.Play(_ambientOpen && !plain);
        // (no frame counting: listening to every frame makes WPF redraw the whole window non-stop)
    }

    private void SetAmbientArt(BitmapSource? art)
    {
        _ambientArt = art;
        try { Ambient.SetArt(art); }
        catch (Exception ex) { CrashLog.Write($"Ambient: {ex.Message}"); Ambient.SetArt(null); }
        ApplyAmbient();
    }

    private void AmbientOpened(bool open)
    {
        _ambientOpen = open;
        ApplyAmbient();
    }

    private bool _leafSwaying;

    /// <summary>The leaf rocks a few degrees on its stem, like in a light breeze.</summary>
    private void SwayLeaf(bool on)
    {
        if (on == _leafSwaying) return;
        _leafSwaying = on;
        if (!on) { PillLeafSway.BeginAnimation(RotateTransform.AngleProperty, null); PillLeafSway.Angle = 0; return; }
        var sway = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(3.2) };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9)), ease));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0)), ease));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(-2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.7)), ease));
        sway.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.2)), ease));
        PillLeafSway.BeginAnimation(RotateTransform.AngleProperty, sway);
    }

    private System.Windows.Shapes.Rectangle[] Bars => new[] { Bar1, Bar2, Bar3, Bar4, Bar5, Bar6, Bar7 };

    private readonly AudioSpectrum _spectrum = new();
    private readonly AudioMeter _meter = new();
    private DispatcherTimer? _meterTimer;
    private readonly double[] _barLevel = new double[7];
    private readonly double[] _bands = new double[AudioSpectrum.BandCount];
    private static readonly double[] BarWeight = { 0.55, 0.8, 1.0, 0.9, 1.0, 0.75, 0.5 };
    private readonly Random _barRandom = new();

    /// <summary>
    /// While music plays each bar follows one band of the real sound, bass on
    /// the left to treble on the right. If Windows won't share the sound, the
    /// overall loudness is used; failing that, a looping animation.
    /// </summary>
    private void SetBarsPlaying(bool playing)
    {
        var bars = Bars;
        var rest = new[] { 0.3, 0.55, 0.4, 0.7, 0.45, 0.6, 0.35 };

        bool listen = AppSettings.Current.ListenToAudio;   // can be switched off in Cài đặt & quyền riêng tư
        if (playing && listen && (_spectrum.Start() || _meter.Peak() != null))
        {
            foreach (var b in bars)
                if (b.RenderTransform is ScaleTransform sc) sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (_meterTimer == null)
            {
                _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
                _meterTimer.Tick += (_, _) => MeterTick();
            }
            _meterTimer.Start();
            return;
        }

        _meterTimer?.Stop();
        _spectrum.Stop();
        if (playing) { AnimateBars(); return; }
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i].RenderTransform is not ScaleTransform scale) continue;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleY = rest[i];
        }
    }

    private void MeterTick()
    {
        var bars = Bars;
        if (!PillBars.IsVisible)
        {
            _spectrum.Stop();   // nobody's looking (hidden, or the panel is open): stop listening
            return;
        }

        if (!AppSettings.Current.ListenToAudio) { _meterTimer?.Stop(); _spectrum.Stop(); AnimateBars(); return; }
        double[] target = new double[bars.Length];
        if (_spectrum.Start())
        {
            _spectrum.Read(_bands);
            for (int i = 0; i < bars.Length; i++) target[i] = 0.12 + 0.88 * _bands[i];
        }
        else
        {
            var peak = _meter.Peak();
            if (peak == null) { _meterTimer?.Stop(); AnimateBars(); return; }
            double level = Math.Min(1, Math.Sqrt(peak.Value) * 1.15);
            for (int i = 0; i < bars.Length; i++)
                target[i] = 0.15 + 0.85 * level * BarWeight[i] * (0.7 + 0.3 * _barRandom.NextDouble());
        }

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i].RenderTransform is not ScaleTransform scale) continue;
            // jump up at once, fall back gently — like a real level meter
            double k = target[i] > _barLevel[i] ? 0.85 : 0.22;
            _barLevel[i] += (target[i] - _barLevel[i]) * k;
            scale.ScaleY = Math.Clamp(_barLevel[i], 0.12, 1);
        }
    }

    private void AnimateBars()
    {
        var bars   = Bars;
        var speeds = new[] { 420, 560, 360, 500, 440, 620, 380 };   // different speeds so they look lively
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i].RenderTransform is not ScaleTransform scale) continue;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(speeds[i]))
            {
                AutoReverse    = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
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
        LyricsBtn.Foreground = _lyricsOpen ? Brushes.White : Frozen(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF));
        LyricsBtn.Background = _lyricsOpen ? Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
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
        var found = await LyricsService.FindAsync(_title, _artist, dur);
        if (key != _lyricsKey) return;   // the song changed meanwhile

        var lines = found.Lines;
        // Plain lyrics (no timing) are shown as text; only synced ones highlight and scroll.
        _lyrics = found.Synced ? lines : Array.Empty<LyricLine>();
        if (lines.Count == 0)
        {
            LyricsStatus.Text = "Không tìm thấy lời bài hát";
            // Try again next time (e.g. once the player reports the song's length).
            _lyricsKey = dur > TimeSpan.Zero ? key : null;
            return;
        }

        LyricsStatus.Visibility = Visibility.Collapsed;
        if (!found.Synced)
        {
            LyricsList.Children.Add(new TextBlock
            {
                Text = "Lời không chạy theo nhạc", FontSize = 11, FontFamily = new FontFamily("Segoe UI"),
                Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 0, 0, 6)
            });
        }
        foreach (var line in lines)
        {
            var text = new TextBlock
            {
                Text = line.Text.Length == 0 ? " " : line.Text, FontSize = found.Synced ? 15 : 13.5,
                FontWeight = found.Synced ? FontWeights.SemiBold : FontWeights.Normal, FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, found.Synced ? 5 : 2, 0, found.Synced ? 5 : 2),
                Foreground = Frozen(found.Synced ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF))
            };
            if (found.Synced)
            {
                text.Cursor = Cursors.Hand;
                var at = line.Time;
                text.MouseLeftButtonUp += (_, _) => { _media.Seek(at); _lyricIndex = -1; };
            }
            LyricsList.Children.Add(text);
        }
        if (found.Synced) UpdateLyrics();
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
        if (HeadsetView.Visibility == Visibility.Visible) { _headsetTimer.Stop(); HeadsetView.Visibility = Visibility.Collapsed; }

        VolGlyph.Text       = VolumeGlyph(level, muted);
        VolFillScale.ScaleX = muted ? 0 : Math.Clamp(level, 0f, 1f);
        VolText.Text        = muted ? "Tắt" : Math.Round(level * 100).ToString();

        EndWalk();
        Eyes.Stop();
        GrowForEyes(false);
        RestArea.Visibility   = Visibility.Collapsed;
        CatRest.Visibility    = Visibility.Collapsed;
        VolumeView.Visibility = Visibility.Visible;
        _volumeTimer.Stop();
        _volumeTimer.Start();
    }

    private readonly DispatcherTimer _headsetTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    /// <summary>
    /// Earbuds connected (or running low): the pill itself widens for a few
    /// seconds with the name, how many earbuds and the battery. False when the
    /// notch can't show it right now (hidden, open, full screen), so the app
    /// uses a normal notification instead.
    /// </summary>
    public bool ShowHeadset(BluetoothBatteryWatcher.Device d, bool low)
    {
        if (!IsVisible || _expanded || !_enabled) return false;
        var colour = d.Percent <= 20 ? Color.FromRgb(0xFF, 0x45, 0x3A) : d.Percent <= 50 ? Color.FromRgb(0xFF, 0xB0, 0x20) : Color.FromRgb(0x34, 0xC7, 0x59);
        HeadsetName.Text = d.Name;
        HeadsetSub.Text  = low ? "Pin yếu · nhớ sạc nhé"
                         : d.Parts >= 2 ? $"Đã kết nối · {d.Parts} tai"
                         : d.Parts == 1 ? "Đã kết nối · 1 tai"
                         : "Đã kết nối";
        HeadsetSub.Foreground = low ? Frozen(colour) : Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
        HeadsetPercent.Text = $"{d.Percent}%";
        HeadsetFill.Width = Math.Max(1.5, 16 * d.Percent / 100.0);
        HeadsetFill.Background = Frozen(colour);

        EndWalk();
        Eyes.Stop();
        GrowForEyes(false);
        VolumeView.Visibility = Visibility.Collapsed;
        RestArea.Visibility   = Visibility.Collapsed;
        CatRest.Visibility    = Visibility.Collapsed;
        HeadsetView.Visibility = Visibility.Visible;
        // grows out of the pill
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        HeadsetScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        HeadsetScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        _headsetTimer.Stop();
        _headsetTimer.Start();
        return true;
    }

    private void HideHeadset()
    {
        _headsetTimer.Stop();
        if (HeadsetView.Visibility != Visibility.Visible) return;
        var shrink = new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(220)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
        shrink.Completed += (_, _) => { HeadsetView.Visibility = Visibility.Collapsed; ShowRest(); };
        HeadsetScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        HeadsetScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, TimeSpan.FromMilliseconds(220)));
    }

    private static string VolumeGlyph(double level, bool muted) =>
        muted || level < 0.005 ? ""   // muted
        : level < 0.34 ? ""           // low
        : level < 0.67 ? ""           // medium
        : "";                         // high

    // ── Tree planting ─────────────────────────────────────────────────
    private string _focusView = "plant";    // plant | pick | stats | deep
    private string _period = "week";
    private DeepFocus? _deep;
    private static readonly (string Key, string Label)[] FocusViews = { ("plant", "Trồng cây"), ("pick", "Chọn cây"), ("stats", "Thống kê") };
    private static readonly (string Key, string Label)[] Periods = { ("day", "Ngày"), ("week", "Tuần"), ("month", "Tháng"), ("year", "Năm") };
    private static readonly Color Green = Color.FromRgb(0x34, 0xC7, 0x59);

    /// <summary>Raised when Deep focus spots a distraction (for the floating timer).</summary>
    public event Action<string>? Distracted;

    private void SetUpFocus()
    {
        Ruler.Min = FocusService.MinMinutes;
        Ruler.Max = FocusService.MaxMinutes;
        Ruler.Minutes = _selectedMinutes;
        Ruler.Changed += m =>
        {
            if (PomoMode) { AppSettings.Current.PomoCustomStudy = m; AppSettings.Current.Save(); }
            else _selectedMinutes = m;
            if (!_focus.IsRunning) UpdateFocusUi();
        };
        _deep = new DeepFocus(_focus);
        _deep.Distracted += name =>
        {
            FocusWarn.Text = $"👀 {name} đang mở. Quay lại học nhé! Cây vẫn đang chờ bạn.";
            FocusWarn.Visibility = Visibility.Visible;
            React("angry");
            Distracted?.Invoke(name);
        };
        _deep.Back += () => FocusWarn.Visibility = Visibility.Collapsed;
        BuildFocusNav();
    }

    /// <summary>A small segmented button (the views, the periods).</summary>
    private Border Segment(string label, bool on, Action click)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 2, 0), Cursor = Cursors.Hand,
            Background = on ? Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
            Child = new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"),
                Foreground = on ? Brushes.White : Frozen(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF))
            }
        };
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    private void BuildFocusNav()
    {
        FocusNav.Children.Clear();
        foreach (var (key, label) in FocusViews) FocusNav.Children.Add(Segment(label, _focusView == key, () => ShowFocusView(key)));
        PeriodNav.Children.Clear();
        foreach (var (key, label) in Periods) PeriodNav.Children.Add(Segment(label, _period == key, () => { _period = key; BuildFocusNav(); BuildStats(); }));
    }

    private void ShowFocusView(string view)
    {
        if (_typing) EndTyping(collapse: false);
        ContentCard.BeginAnimation(HeightProperty, null);
        double from = ContentCard.ActualHeight;

        _focusView = view;
        FocusMain.Visibility    = view == "plant" ? Visibility.Visible : Visibility.Collapsed;
        SpeciesPanel.Visibility = view == "pick"  ? Visibility.Visible : Visibility.Collapsed;
        FocusStats.Visibility   = view == "stats" ? Visibility.Visible : Visibility.Collapsed;
        DeepListView.Visibility = view == "deep"  ? Visibility.Visible : Visibility.Collapsed;
        BuildFocusNav();
        if (view == "pick") BuildSpecies();
        if (view == "stats") BuildStats();
        if (view == "deep") BuildDeepChips();
        if (view == "plant") UpdateFocusUi();

        if (Expanded.IsVisible && from > 0)
        {
            ContentCard.UpdateLayout();
            double to = ContentCard.ActualHeight;
            if (Math.Abs(to - from) > 1)
                ContentCard.BeginAnimation(HeightProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
                });
        }
    }

    private static string Clock(int minutes) => minutes >= 60 ? $"{minutes / 60}:{minutes % 60:00}:00" : $"{minutes:00}:00";

    private static string Duration(int minutes) =>
        minutes >= 60 ? $"{minutes / 60} giờ" + (minutes % 60 > 0 ? $" {minutes % 60} phút" : "") : $"{minutes} phút";

    // ── Pomodoro ──────────────────────────────────────────────────────
    private PomodoroService? _pomo;
    private static bool PomoMode => AppSettings.Current.FocusMode == "pomodoro";

    /// <summary>Called by the app once.</summary>
    public void UsePomodoro(PomodoroService pomo)
    {
        _pomo = pomo;
        _pomo.Changed += UpdateFocusUi;
        _pomo.CycleDone += () => CatReact(CatMood.Excited, 10);
        UpdateFocusUi();
    }

    private void SetFocusMode(string mode)
    {
        if (_focus.IsRunning || _pomo?.OnBreak == true || AppSettings.Current.FocusMode == mode) return;
        AppSettings.Current.FocusMode = mode;
        AppSettings.Current.Save();
        ContentCard.BeginAnimation(HeightProperty, null);
        double from = ContentCard.ActualHeight;
        UpdateFocusUi();
        ContentCard.UpdateLayout();
        double to = ContentCard.ActualHeight;
        if (Expanded.IsVisible && from > 0 && Math.Abs(to - from) > 1)
            ContentCard.BeginAnimation(HeightProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            });
    }

    /// <summary>The rhythm chips: Ngắn 25·5, Vừa 45·10, Sâu 50·10, Dài 90·20, Tự chỉnh.</summary>
    private void BuildPomoPlans()
    {
        PomoPlans.Children.Clear();
        var picked = AppSettings.Current.PomoPlan;
        var green = Color.FromRgb(0x34, 0xC7, 0x59);
        foreach (var plan in PomodoroService.Plans.Append(new PomoPlan("custom", "Tự chỉnh", 0, 0, "")))
        {
            bool on = plan.Key == picked;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 5, 5), Cursor = Cursors.Hand,
                Background = Frozen(on ? Color.FromArgb(0x40, green.R, green.G, green.B) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1), BorderBrush = on ? Frozen(green) : Brushes.Transparent,
                ToolTip = plan.Key == "custom" ? "Tự đặt số phút học và nghỉ" : $"Học {plan.Study} phút, nghỉ {plan.Break} phút · hợp với {plan.Hint}",
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = plan.Name, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock
                        {
                            Text = plan.Key == "custom" ? $"{AppSettings.Current.PomoCustomStudy} · {AppSettings.Current.PomoCustomBreak}" : $"{plan.Study} · {plan.Break}",
                            Foreground = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), FontSize = 10.5, FontFamily = new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
            var key = plan.Key;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                AppSettings.Current.PomoPlan = key;
                AppSettings.Current.Save();
                ContentCard.BeginAnimation(HeightProperty, null);
                UpdateFocusUi();
            };
            PomoPlans.Children.Add(chip);
        }
    }

    /// <summary>Own rhythm: the ruler sets the study minutes, this row the rest.</summary>
    private void BuildPomoCustom()
    {
        PomoCustomRow.Children.Clear();
        int rest = AppSettings.Current.PomoCustomBreak;
        Button Step(string label, int delta) 
        {
            var b = new Button { Content = label, Style = (Style)FindResource("QuickButton"), Width = 24, Height = 24, FontSize = 12, Margin = new Thickness(0) };
            b.Click += (_, _) =>
            {
                AppSettings.Current.PomoCustomBreak = Math.Clamp(AppSettings.Current.PomoCustomBreak + delta, 1, 60);
                AppSettings.Current.Save();
                UpdateFocusUi();
            };
            return b;
        }
        PomoCustomRow.Children.Add(new TextBlock { Text = "Nghỉ", Foreground = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontSize = 12, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        PomoCustomRow.Children.Add(Step("\u2212", -1));
        PomoCustomRow.Children.Add(new TextBlock { Text = $"{rest} phút", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), MinWidth = 44, TextAlignment = TextAlignment.Center });
        PomoCustomRow.Children.Add(Step("+", +1));
        PomoCustomRow.Children.Add(new TextBlock { Text = "· kéo thước để đổi phút học", Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), FontSize = 11, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
    }

    private string? _focusNavKey, _pomoPlansKey, _pomoCustomKey;

    private void SkipBreak_Click(object sender, RoutedEventArgs e) => _pomo?.SkipBreak();
    private void StopPomo_Click(object sender, RoutedEventArgs e) => _pomo?.Stop();

    private void UpdateFocusUi()
    {
        bool running = _focus.IsRunning;
        bool onBreak = _pomo?.OnBreak == true;
        bool idle = !running && !onBreak;
        bool pomo = PomoMode;
        var plan = PomodoroService.Selected;
        bool custom = pomo && plan.Key == "custom";
        var kind = running ? _focus.CurrentKind : _selectedKind;

        // Rebuilt only when what they show changes (this runs on every tab switch)
        string navKey = $"{AppSettings.Current.FocusMode}|{idle}";
        if (navKey != _focusNavKey)
        {
            _focusNavKey = navKey;
            FocusModeNav.Children.Clear();
            foreach (var (key, label) in new[] { ("free", "Tự do"), ("pomodoro", "Pomodoro") })
            {
                var seg = Segment(label, AppSettings.Current.FocusMode == key, () => SetFocusMode(key));
                seg.Padding = new Thickness(9, 3, 9, 3);
                if (!idle) { seg.Opacity = 0.5; seg.Cursor = Cursors.Arrow; }
                FocusModeNav.Children.Add(seg);
            }
        }

        string plansKey = $"{AppSettings.Current.PomoPlan}|{AppSettings.Current.PomoCustomStudy}|{AppSettings.Current.PomoCustomBreak}";
        if (custom) { Ruler.Minutes = Math.Clamp(AppSettings.Current.PomoCustomStudy, FocusService.MinMinutes, FocusService.MaxMinutes); if (plansKey != _pomoCustomKey) { _pomoCustomKey = plansKey; BuildPomoCustom(); } }
        else if (!pomo && Ruler.Minutes != _selectedMinutes) Ruler.Minutes = _selectedMinutes;
        if (pomo && idle && plansKey != _pomoPlansKey) { _pomoPlansKey = plansKey; BuildPomoPlans(); }

        Ruler.Visibility         = idle && (!pomo || custom) ? Visibility.Visible : Visibility.Collapsed;
        PomoPlans.Visibility     = idle && pomo ? Visibility.Visible : Visibility.Collapsed;
        PomoCustomRow.Visibility = idle && custom ? Visibility.Visible : Visibility.Collapsed;
        FocusProgress.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        PlantBtn.Visibility      = idle ? Visibility.Visible : Visibility.Collapsed;
        PlantBtn.Content         = pomo ? "Bắt đầu Pomodoro" : "Trồng cây";
        GiveUpBtn.Visibility     = running ? Visibility.Visible : Visibility.Collapsed;
        SkipBreakBtn.Visibility  = onBreak ? Visibility.Visible : Visibility.Collapsed;
        StopPomoBtn.Visibility   = onBreak ? Visibility.Visible : Visibility.Collapsed;
        SkipBreakBtn.Content     = _pomo?.LongBreakNow == true ? "Xong nghỉ" : "Học tiếp luôn";
        PickedTree.Opacity       = idle ? 1 : 0.6;
        PickedTree.Cursor        = idle ? Cursors.Hand : Cursors.Arrow;

        BigTree.Kind = kind;
        PickedTreeIcon.Kind = kind;
        PickedTreeText.Text = !idle ? TreeCatalog.Get(kind).Name : $"{TreeCatalog.Get(kind).Name} · đổi cây ›";

        if (!idle) UpdateFocusTick();
        else
        {
            BigTree.Set(1, dead: false);
            if (pomo)
            {
                FocusRemaining.Text = Clock(plan.Study);
                FocusSub.Text = $"Học {plan.Study} · nghỉ {plan.Break} · {PomodoroService.Rounds} phiên, nghỉ dài {plan.LongBreak} phút cuối";
            }
            else
            {
                FocusRemaining.Text = Clock(_selectedMinutes);
                FocusSub.Text = $"{Duration(_selectedMinutes)} · kéo thước để đổi";
            }
            FocusWarn.Visibility = Visibility.Collapsed;
            ResetGiveUp();
        }

        bool deep = DeepFocus.Enabled;
        DeepSwitch.Background = deep ? Frozen(Green) : Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        DeepKnob.HorizontalAlignment = deep ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var list = AppSettings.Current.DeepFocusList;
        DeepHint.Text = deep
            ? $"Nhắc khi mở {string.Join(", ", list.Take(3))}{(list.Count > 3 ? "…" : "")} · sửa ›"
            : "Đang tắt · sửa danh sách ›";

        if (_focusView == "stats" && FocusStats.IsVisible) BuildStats();
        UpdatePill();
    }

    private void UpdateFocusTick()
    {
        if (_pomo is { OnBreak: true } p)
        {
            var rest = p.BreakRemaining;
            FocusRemaining.Text   = rest.ToString(@"mm\:ss");
            FocusSub.Text         = p.LongBreakNow ? $"Nghỉ dài {p.BreakMinutes} phút · xong cả {PomodoroService.Rounds} phiên!"
                                                   : $"Nghỉ {p.BreakMinutes} phút · xong phiên {p.Round}/{PomodoroService.Rounds} · đứng dậy vươn vai nhé";
            FocusFillScale.ScaleX = p.BreakMinutes > 0 ? Math.Clamp(1 - rest.TotalMinutes / p.BreakMinutes, 0, 1) : 1;
            BigTree.Set(1, dead: false);
            return;
        }
        if (!_focus.IsRunning) return;
        var left = _focus.Remaining;
        FocusRemaining.Text   = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
        var done = TimeSpan.FromMinutes(_focus.Minutes) - left;
        FocusSub.Text         = (_pomo?.Studying == true ? $"Phiên {_pomo.Round}/{PomodoroService.Rounds} · " : "")
                              + $"{TreeCatalog.Get(_focus.CurrentKind).Name} · đã học {(int)done.TotalMinutes} / {_focus.Minutes} phút";
        FocusFillScale.ScaleX = _focus.Progress;
        BigTree.Set(_focus.Progress, dead: false);
    }

    private void PickedTree_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_focus.IsRunning) ShowFocusView("pick");
    }

    private void Plant_Click(object sender, RoutedEventArgs e)
    {
        if (PomoMode && _pomo != null)
        {
            AppSettings.Current.LastTreeKind = _selectedKind.ToString();
            _pomo.Start(PomodoroService.Selected, _selectedKind);
            return;
        }
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

    /// <summary>"3 giờ 20 phút", "45 phút".</summary>
    private static string HoursLeft(double hours)
    {
        var t = TimeSpan.FromHours(Math.Max(0, hours));
        int h = (int)t.TotalHours, m = (int)Math.Ceiling(t.TotalMinutes - h * 60);
        if (m == 60) { h++; m = 0; }
        return h == 0 ? $"{Math.Max(1, m)} phút" : m == 0 ? $"{h} giờ" : $"{h} giờ {m} phút";
    }

    // Pick a tree: 12 kinds, unlocked by hours of focus
    private void BuildSpecies()
    {
        SpeciesPanel.Children.Clear();
        double hours = _focus.FocusedHours;
        foreach (var info in TreeCatalog.All)
        {
            bool unlocked = hours >= info.UnlockHours, selected = info.Kind == _selectedKind;
            string left = HoursLeft(info.UnlockHours - hours);
            var tree = new TreeShape
            {
                Animated = unlocked, Kind = info.Kind, Opacity = unlocked ? 1 : 0.3,
                LayoutTransform = new ScaleTransform(0.56, 0.56), HorizontalAlignment = HorizontalAlignment.Center
            };
            var body = new StackPanel();
            body.Children.Add(tree);
            body.Children.Add(new TextBlock
            {
                Text = info.Name, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
            body.Children.Add(new TextBlock
            {
                Text = unlocked ? (selected ? "Đang chọn" : "Đã mở") : $"🔒 còn {left}",
                Foreground = selected ? Frozen(Green) : Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 10, FontFamily = new FontFamily("Segoe UI"), TextAlignment = TextAlignment.Center
            });
            var tile = new Border
            {
                Child = body, CornerRadius = new CornerRadius(12), Padding = new Thickness(2, 4, 2, 6), Margin = new Thickness(3),
                Background = Frozen(selected ? Color.FromArgb(0x26, Green.R, Green.G, Green.B) : Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1.5), BorderBrush = selected ? Frozen(Green) : Brushes.Transparent,
                Cursor = unlocked ? Cursors.Hand : Cursors.Arrow,
                ToolTip = unlocked ? info.Name : $"{info.Name} · mở khoá khi đủ {info.UnlockHours} giờ tập trung (còn {left})"
            };
            var kind = info.Kind;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (!_focus.IsUnlocked(kind) || _focus.IsRunning) return;
                _selectedKind = kind;
                AppSettings.Current.LastTreeKind = kind.ToString();
                AppSettings.Current.Save();
                ShowFocusView("plant");
            };
            SpeciesPanel.Children.Add(tile);
        }
    }

    // Stats: day / week / month / year, with a patch of land growing those trees
    private void BuildStats()
    {
        var today = DateTime.Today;
        DateTime from; int buckets; Func<DateTime, int> bucket; Dictionary<int, string> labels; string unit;
        switch (_period)
        {
            case "day":
                from = today; buckets = 24; bucket = t => t.Hour; unit = "phút / giờ";
                labels = new() { [0] = "0h", [6] = "6h", [12] = "12h", [18] = "18h" };
                break;
            case "month":
                from = new DateTime(today.Year, today.Month, 1); buckets = DateTime.DaysInMonth(today.Year, today.Month);
                bucket = t => t.Day - 1; unit = "phút / ngày";
                labels = new() { [0] = "1", [9] = "10", [19] = "20", [buckets - 1] = buckets.ToString() };
                break;
            case "year":
                from = new DateTime(today.Year, 1, 1); buckets = 12; bucket = t => t.Month - 1; unit = "phút / tháng";
                labels = Enumerable.Range(0, 12).ToDictionary(i => i, i => $"T{i + 1}");
                break;
            default:
                int back = ((int)today.DayOfWeek + 6) % 7;   // Monday first
                from = today.AddDays(-back); buckets = 7; bucket = t => (t.Date - from).Days; unit = "phút / ngày";
                labels = new() { [0] = "T2", [1] = "T3", [2] = "T4", [3] = "T5", [4] = "T6", [5] = "T7", [6] = "CN" };
                break;
        }
        var to = _period switch { "day" => from.AddDays(1), "month" => from.AddMonths(1), "year" => from.AddYears(1), _ => from.AddDays(7) };
        var sessions = _focus.Between(from, to);

        var values = new int[buckets];
        foreach (var s in sessions.Where(s => s.Success))
        {
            int i = bucket(s.StartUtc.ToLocalTime());
            if (i >= 0 && i < buckets) values[i] += s.Minutes;
        }
        int total = values.Sum(), alive = sessions.Count(s => s.Success), dead = sessions.Count - alive;
        WeekSummary.Text = $"{total / 60} giờ {total % 60} phút · 🌳 {alive}" + (dead > 0 ? $" · 🥀 {dead}" : "");
        ChartUnit.Text = unit;
        BuildBars(values, labels);
        BuildLand(sessions);
    }

    private void BuildBars(int[] values, Dictionary<int, string> labels)
    {
        WeekChart.Children.Clear();
        WeekChart.ColumnDefinitions.Clear();
        double max = Math.Max(30, values.DefaultIfEmpty(0).Max());
        const double area = 100;
        for (int i = 0; i < values.Length; i++)
        {
            WeekChart.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(new Border
            {
                Height = values[i] > 0 ? Math.Max(3, area * values[i] / max) : 2,
                Margin = new Thickness(values.Length > 12 ? 0.6 : 3, 0, values.Length > 12 ? 0.6 : 3, 0),
                CornerRadius = new CornerRadius(values.Length > 12 ? 1.5 : 4, values.Length > 12 ? 1.5 : 4, 1, 1),
                Background = values[i] > 0 ? Frozen(Green) : Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                ToolTip = $"{values[i]} phút"
            });
            col.Children.Add(new TextBlock
            {
                Text = labels.TryGetValue(i, out var l) ? l : " ", FontSize = 9.5, FontFamily = new FontFamily("Segoe UI"),
                Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(-10, 4, -10, 0), TextAlignment = TextAlignment.Center
            });
            Grid.SetColumn(col, i);
            WeekChart.Children.Add(col);
        }
    }

    /// <summary>An isometric patch of grass with one tree per session (withered ones for giving up).</summary>
    private void BuildLand(IReadOnlyList<FocusSession> sessions)
    {
        Land.Children.Clear();
        int count = sessions.Count;
        int n = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, count) * 1.6)), 4, 12);
        double tw = 200.0 / n, th = tw / 2, ox = 100, oy = 30;
        Land.Height = oy + n * th + 14;

        // grass, and its two visible sides
        var left  = new Point(ox - n * tw / 2, oy + n * th / 2);
        var bottom = new Point(ox, oy + n * th);
        var right = new Point(ox + n * tw / 2, oy + n * th / 2);
        Land.Children.Add(new System.Windows.Shapes.Polygon { Points = { left, bottom, new Point(bottom.X, bottom.Y + 10), new Point(left.X, left.Y + 10) }, Fill = Frozen(Color.FromRgb(0x4A, 0x31, 0x22)) });
        Land.Children.Add(new System.Windows.Shapes.Polygon { Points = { bottom, right, new Point(right.X, right.Y + 10), new Point(bottom.X, bottom.Y + 10) }, Fill = Frozen(Color.FromRgb(0x36, 0x24, 0x19)) });
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                double x = ox + (c - r) * tw / 2, y = oy + (c + r) * th / 2;
                Land.Children.Add(new System.Windows.Shapes.Polygon
                {
                    Points = { new Point(x, y), new Point(x + tw / 2, y + th / 2), new Point(x, y + th), new Point(x - tw / 2, y + th / 2) },
                    Fill = Frozen((r + c) % 2 == 0 ? Color.FromRgb(0x2F, 0x5A, 0x34) : Color.FromRgb(0x35, 0x63, 0x39))   // deep moss green: the trees stand out
                });
            }

        // trees on spread-out tiles, drawn back to front
        var spots = Enumerable.Range(0, n * n).OrderBy(i => (i * 7919) % 97).Take(Math.Min(count, n * n)).ToList();
        var placed = new List<(double Y, UIElement Tree)>();
        for (int i = 0; i < spots.Count; i++)
        {
            int r = spots[i] / n, c = spots[i] % n;
            double x = ox + (c - r) * tw / 2, y = oy + (c + r) * th / 2 + th / 2, size = tw * 0.95;
            var s = sessions[sessions.Count - 1 - i];   // newest first, if there are too many to show
            var tree = new TreeShape { Animated = false, Kind = s.TreeKind, RenderTransform = new ScaleTransform(size / 100, size / 100) };
            tree.Set(1, dead: !s.Success);
            Canvas.SetLeft(tree, x - size / 2);
            Canvas.SetTop(tree, y - size * 1.02);
            placed.Add((y, tree));
        }
        foreach (var (_, tree) in placed.OrderBy(p => p.Y)) Land.Children.Add(tree);

        if (count == 0)
        {
            var hint = new TextBlock { Text = "Chưa có cây nào", Foreground = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontSize = 11, FontFamily = new FontFamily("Segoe UI") };
            Canvas.SetLeft(hint, 62);
            Canvas.SetTop(hint, oy + n * th / 2 - 8);
            Land.Children.Add(hint);
        }
    }

    // Deep focus switch + list
    private void DeepSwitch_Click(object sender, MouseButtonEventArgs e)
    {
        DeepFocus.Enabled = !DeepFocus.Enabled;
        UpdateFocusUi();
    }

    private void DeepList_Click(object sender, MouseButtonEventArgs e) => ShowFocusView("deep");

    private void BuildDeepChips()
    {
        DeepChips.Children.Clear();
        foreach (var item in AppSettings.Current.DeepFocusList.ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = item, Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center });
            var x = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 9, Margin = new Thickness(8, 0, 0, 0),
                Foreground = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = "Bỏ khỏi danh sách"
            };
            var name = item;
            x.MouseLeftButtonUp += (_, _) =>
            {
                AppSettings.Current.DeepFocusList.Remove(name);
                AppSettings.Current.Save();
                BuildDeepChips();
            };
            row.Children.Add(x);
            DeepChips.Children.Add(new Border
            {
                Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 8, 4), Margin = new Thickness(0, 0, 6, 6),
                Background = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))
            });
        }
    }

    private void DeepAdd_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_typing) return;
        BeginTyping(DeepAdd);
        e.Handled = true;
    }

    private void DeepAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = DeepAdd.Text.Trim();
            if (text.Length > 0 && !AppSettings.Current.DeepFocusList.Contains(text, StringComparer.CurrentCultureIgnoreCase))
            {
                AppSettings.Current.DeepFocusList.Add(text);
                AppSettings.Current.Save();
                BuildDeepChips();
            }
            DeepAdd.Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DeepAdd.Text = "";
            EndTyping(collapse: false);
            e.Handled = true;
        }
    }

    private void DeepAdd_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        DeepAddPlaceholder.Visibility = DeepAdd.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Hạn chót ──────────────────────────────────────────────────────
    private DeadlineService? _deadlines;
    private DispatcherTimer? _deadlineTick;

    /// <summary>Called by the app once; the list refreshes each minute while it's open.</summary>
    public void UseDeadlines(DeadlineService deadlines)
    {
        _deadlines = deadlines;
        _deadlines.Changed += () => { if (_tab == "deadlines") RebuildDeadlines(); };
        _deadlineTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _deadlineTick.Tick += (_, _) => { if (_tab == "deadlines" && _expanded) RebuildDeadlines(); };
        _deadlineTick.Start();
    }

    private void RebuildDeadlines()
    {
        DeadlineList.Children.Clear();
        var items = _deadlines?.Items ?? Array.Empty<Deadline>();
        var notes = _deadlines?.Notes ?? Array.Empty<QuickNote>();
        NoDeadlinesText.Visibility = items.Count == 0 && notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var now = DateTime.Now;

        // Exams first (counted in days), then deadlines, then notes
        foreach (var d in items.Where(d => d.Exam))
        {
            int days = (d.Due.Date - now.Date).Days;
            var colour = days <= 0 ? Color.FromRgb(0xFF, 0x5C, 0x5C)
                       : days <= 3 ? Color.FromRgb(0xFF, 0x9F, 0x0A)
                       : Color.FromRgb(0x4D, 0xA3, 0xFF);
            var editing = d;
            DeadlineList.Children.Add(PlannerRow(colour, "\uE7BE", d.Title,
                $"Thi · {Weekdays[(int)d.Due.DayOfWeek]}, {d.Due:dd/MM} · {d.Due:HH:mm}",
                days == 0 && d.Due > now ? "Hôm nay · thi tốt nhé!" : DeadlineService.DaysLeft(d.Due, now),
                strike: d.Due < now.AddHours(-3), highlighted: d.Id == _editingDeadline,
                remove: () => _deadlines?.Remove(editing.Id), click: () => BeginEditDeadline(editing)));
        }

        foreach (var d in items.Where(d => !d.Exam))
        {
            var left = d.Due - now;
            // red when late or < 1 h, orange < 24 h, purple later
            var colour = left < TimeSpan.FromHours(1) ? Color.FromRgb(0xFF, 0x5C, 0x5C)
                       : left < TimeSpan.FromHours(24) ? Color.FromRgb(0xFF, 0xB0, 0x20)
                       : Color.FromRgb(0xB3, 0x8C, 0xFF);
            var editing = d;
            DeadlineList.Children.Add(PlannerRow(colour, null, d.Title,
                $"{Weekdays[(int)d.Due.DayOfWeek]}, {d.Due:dd/MM} · {d.Due:HH:mm}",
                DeadlineService.Left(d.Due, now), strike: left < TimeSpan.Zero, highlighted: d.Id == _editingDeadline,
                remove: () => _deadlines?.Remove(editing.Id), click: () => BeginEditDeadline(editing)));
        }

        if (notes.Count > 0)
        {
            DeadlineList.Children.Add(new TextBlock
            {
                Text = "GHI CHÚ", Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), FontSize = 10.5, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"), Margin = new Thickness(4, items.Count > 0 ? 6 : 0, 0, 5)
            });
            var yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
            foreach (var n in notes)
            {
                var note = n;
                DeadlineList.Children.Add(PlannerRow(yellow, "\uE70B", n.Text, Ago(n.Time), null,
                    strike: false, highlighted: n.Id == _editingNote,
                    remove: () => _deadlines?.RemoveNote(note.Id), click: () => BeginEditNote(note)));
            }
        }
    }

    /// <summary>One row in Hạn chót: a coloured mark, the name, a small line under it, a badge and ✕.</summary>
    private Border PlannerRow(Color colour, string? glyph, string title, string sub, string? badgeText,
                              bool strike, bool highlighted, Action remove, Action click)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement mark = glyph == null
            ? new Border { Width = 4, Height = 26, CornerRadius = new CornerRadius(2), Background = Frozen(colour), Margin = new Thickness(0, 0, 10, 0) }
            : new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14, Foreground = Frozen(colour),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0)
            };
        grid.Children.Add(mark);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = strike ? TextDecorations.Strikethrough : null
        });
        text.Children.Add(new TextBlock
        {
            Text = sub, Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), FontSize = 11, FontFamily = new FontFamily("Segoe UI")
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (badgeText != null)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(Color.FromArgb(0x30, colour.R, colour.G, colour.B)), Margin = new Thickness(8, 0, 6, 0),
                Child = new TextBlock { Text = badgeText, Foreground = Frozen(colour), FontSize = 11.5, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold }
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var x = new TextBlock
        {
            Text = "\uE711", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10, Cursor = Cursors.Hand,
            Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6), ToolTip = "Xoá"
        };
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; remove(); };
        Grid.SetColumn(x, 3);
        grid.Children.Add(x);

        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 4, 6), Margin = new Thickness(0, 0, 0, 5),
            Background = Frozen(highlighted ? Color.FromArgb(0x30, 0xB3, 0x8C, 0xFF) : Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand, ToolTip = "Bấm để sửa"
        };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return row;
    }

    private Guid? _editingNote;

    private void BeginEditNote(QuickNote n)
    {
        _editingDeadline = null;
        _editingNote = n.Id;
        if (!_typing) BeginTyping(DeadlineAdd);
        DeadlineAdd.Text = n.Text;
        DeadlineAdd.CaretIndex = n.Text.Length;
        DeadlineAdd.Focus();
        DeadlineHint.Text = "Đang sửa ghi chú · thêm giờ (vd 17h mai) để thành hạn chót · Enter để lưu";
        DeadlineHint.Foreground = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
        DeadlineHint.Visibility = Visibility.Visible;
        RebuildDeadlines();
    }

    /// <summary>Ctrl + Alt + N anywhere: the panel opens on Hạn chót with the cursor in the box.</summary>
    public void OpenQuickNote()
    {
        if (!IsVisible) return;
        Expand("deadlines");
        DeadlineAdd.Text = "";
        EndEditDeadline();
        BeginTyping(DeadlineAdd);
        DeadlineHint.Text = "Ghi nhanh · có giờ thì thành hạn chót, không có giờ thì lưu ghi chú · Esc để đóng";
        DeadlineHint.Foreground = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
        DeadlineHint.Visibility = Visibility.Visible;
    }

    private void DeadlineAdd_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_typing) return;
        BeginTyping(DeadlineAdd);
        e.Handled = true;
    }

    private Guid? _editingDeadline;

    /// <summary>Click a deadline: its name and time go back into the box to fix; Enter saves.</summary>
    private void BeginEditDeadline(Deadline d)
    {
        _editingNote = null;
        _editingDeadline = d.Id;
        if (!_typing) BeginTyping(DeadlineAdd);
        DeadlineAdd.Text = DeadlineService.EditText(d);
        DeadlineAdd.CaretIndex = d.Title.Length;
        DeadlineAdd.Focus();
        DeadlineHint.Text = "Đang sửa · Enter để lưu · Esc để huỷ";
        DeadlineHint.Foreground = Frozen(Color.FromRgb(0xB3, 0x8C, 0xFF));
        DeadlineHint.Visibility = Visibility.Visible;
        RebuildDeadlines();
    }

    private void EndEditDeadline()
    {
        _editingDeadline = null;
        _editingNote = null;
        DeadlineHint.Foreground = Frozen(Color.FromRgb(0xFF, 0xC0, 0x5A));
        DeadlineHint.Visibility = Visibility.Collapsed;
    }

    private void DeadlineAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = DeadlineAdd.Text.Trim();
            if (text.Length > 0 && _deadlines != null)
            {
                string? problem = null;
                bool hasTime = DeadlineService.Parse(text, DateTime.Now) != null;
                bool ok;
                if (_editingNote is { } noteId)
                {
                    // A note given a time becomes a deadline
                    if (hasTime) { ok = _deadlines.Add(text, out problem) != null; if (ok) _deadlines.RemoveNote(noteId); }
                    else { _deadlines.UpdateNote(noteId, text); ok = true; }
                }
                else if (_editingDeadline is { } id) ok = _deadlines.Update(id, text, out problem);
                else if (!hasTime) { _deadlines.AddNote(text); ok = true; }
                else ok = _deadlines.Add(text, out problem) != null;
                if (!ok)
                {
                    DeadlineHint.Text = problem;
                    DeadlineHint.Foreground = Frozen(Color.FromRgb(0xFF, 0xC0, 0x5A));
                    DeadlineHint.Visibility = Visibility.Visible;
                }
                else
                {
                    EndEditDeadline();
                    DeadlineAdd.Text = "";
                    RebuildDeadlines();
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DeadlineAdd.Text = "";
            EndEditDeadline();
            RebuildDeadlines();
            EndTyping(collapse: false);
            e.Handled = true;
        }
    }

    private void DeadlineAdd_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        DeadlineAddPlaceholder.Visibility = DeadlineAdd.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_editingDeadline == null && DeadlineHint.Visibility == Visibility.Visible && DeadlineAdd.Text.Length > 0)
        {
            var p = DeadlineService.Parse(DeadlineAdd.Text, DateTime.Now);
            if (p != null) DeadlineHint.Visibility = Visibility.Collapsed;
        }
    }

    // ── Lịch học ──────────────────────────────────────────────────────
    private ScheduleService? _schedule;
    private DateTime _scheduleDay = DateTime.Today;
    private bool _scheduleImporting;
    private bool ScheduleServiceHasAny => AppSettings.Current.Schedule.Count > 0;

    /// <summary>Called by the app once.</summary>
    public void UseSchedule(ScheduleService schedule)
    {
        _schedule = schedule;
        _schedule.Changed += () => { if (_tab == "schedule" && _expanded) RebuildSchedule(); };
    }

    private void ShowScheduleImport(bool on)
    {
        _scheduleImporting = on;
        ScheduleImportView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ScheduleScroll.Visibility     = on ? Visibility.Collapsed : Visibility.Visible;
        ScheduleImportBtn.Content     = on ? (ScheduleServiceHasAny ? "Xem lịch" : "Nhập lịch") : "Nhập lịch";
        ScheduleImportBtn.Visibility  = on && !ScheduleServiceHasAny ? Visibility.Collapsed : Visibility.Visible;
        ScheduleClearBtn.Visibility   = ScheduleServiceHasAny ? Visibility.Visible : Visibility.Collapsed;
        var remind = AppSettings.Current.ScheduleReminders;
        ScheduleRemindSwitch.Background = remind ? Frozen(Color.FromRgb(0x34, 0xC7, 0xC7)) : Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        ScheduleRemindKnob.HorizontalAlignment = remind ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        if (on) RebuildSources();
        RebuildSchedule();
    }

    private void ScheduleImport_Click(object sender, RoutedEventArgs e) => ResizeAfter(() => ShowScheduleImport(!_scheduleImporting));

    /// <summary>Runs a change inside the tab and lets the panel glide to its new height.</summary>
    private void ResizeAfter(Action change)
    {
        ContentCard.BeginAnimation(HeightProperty, null);
        double from = ContentCard.ActualHeight;
        change();
        ContentCard.UpdateLayout();
        double to = ContentCard.ActualHeight;
        if (Expanded.IsVisible && from > 0 && Math.Abs(to - from) > 1)
            ContentCard.BeginAnimation(HeightProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            });
    }

    private void RebuildSchedule()
    {
        // Day picker: T2 … CN of this week, today marked
        ScheduleDays.Children.Clear();
        var monday = DateTime.Today.AddDays(-(ScheduleImport.Iso(DateTime.Today.DayOfWeek) - 1));
        for (int i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i);
            bool count = ScheduleService.On(day).Count > 0;
            var seg = Segment(ShortDays[(int)day.DayOfWeek], day == _scheduleDay, () => { _scheduleDay = day; _kindPickerFor = null; if (ScheduleAddRow.Visibility == Visibility.Visible) BuildRepeatNav(); if (_scheduleImporting) ShowScheduleImport(false); else ResizeAfter(RebuildSchedule); });
            seg.Padding = new Thickness(8, 3, 8, 3);
            if (day == DateTime.Today && seg.Child is TextBlock tb) tb.TextDecorations = TextDecorations.Underline;
            if (!count) seg.Opacity = 0.55;
            ScheduleDays.Children.Add(seg);
        }

        ScheduleList.Children.Clear();
        if (_scheduleImporting) return;
        var classes = ScheduleService.On(_scheduleDay);
        var now = DateTime.Now;
        if (classes.Count == 0)
        {
            ScheduleList.Children.Add(new TextBlock
            {
                Text = ScheduleServiceHasAny ? $"{Weekdays[(int)_scheduleDay.DayOfWeek]} không có tiết nào. Nghỉ ngơi nhé!" : "Chưa có lịch học. Bấm \u201CNhập lịch\u201D để thêm.",
                Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), FontSize = 12, FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18)
            });
            return;
        }
        // Calendar-style day: times down the left, one coloured block per session (coloured by
        // kind), a red "now" line, past sessions faded, the current and next one marked.
        bool today = _scheduleDay == DateTime.Today;
        var nowT = now.TimeOfDay;
        var next = today ? classes.FirstOrDefault(c => c.Start > nowT) : null;
        var current = today ? classes.FirstOrDefault(c => nowT >= c.Start && nowT < c.End) : null;
        int left = today ? classes.Count(c => c.Start > nowT) : classes.Count;
        int minutes = (int)classes.Sum(c => (c.End - c.Start).TotalMinutes);

        var summary = new TextBlock { FontSize = 12, FontFamily = new FontFamily("Segoe UI"), Foreground = Frozen(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(2, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        void Bold(string t) => summary.Inlines.Add(new System.Windows.Documents.Run(t) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        void Plain(string t) => summary.Inlines.Add(new System.Windows.Documents.Run(t));
        Plain(today ? "Hôm nay " : $"{Weekdays[(int)_scheduleDay.DayOfWeek]} ");
        Bold($"{classes.Count} buổi");
        Plain($" · {minutes / 60} giờ{(minutes % 60 > 0 ? $" {minutes % 60} phút" : "")}");
        if (today)
        {
            Plain(" · còn "); Bold($"{left}");
            if (next != null) { Plain(" · tiếp theo "); Bold(next.Subject); Plain($" lúc {ScheduleService.Hm(next.Start)} (sau {Until(next.Start - nowT)})"); }
            else if (current == null) Plain(" · xong hết rồi!");
        }
        ScheduleList.Children.Add(summary);

        FrameworkElement? anchor = null;
        bool nowDrawn = !today;
        foreach (var c in classes)
        {
            if (!nowDrawn && c.Start > nowT)
            {
                nowDrawn = true;
                ScheduleList.Children.Add(anchor = NowLine(now));
            }
            bool live = c == current, past = today && c.End <= nowT, isNext = c == next;
            var colour = KindColour(ScheduleService.Kind(c.Subject));
            var block = ScheduleBlock(c, colour, live, isNext, nowT);
            if (past) block.Opacity = 0.4;
            if (live) anchor ??= block;
            ScheduleList.Children.Add(block);
        }
        if (!nowDrawn) ScheduleList.Children.Add(anchor = NowLine(now));

        // Open on "now": the current or next session near the top
        if (anchor != null)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    double y = anchor.TranslatePoint(new Point(0, 0), ScheduleList).Y;
                    ScheduleScroll.ScrollToVerticalOffset(Math.Max(0, y - 60));
                }
                catch { }
            }), DispatcherPriority.Loaded);
        else ScheduleScroll.ScrollToTop();
    }

    /// <summary>Colours by kind of session: on the timetable at school, extra class, self-study, teaching.</summary>
    private static Color KindColour(string kind) =>
        AppSettings.Current.CustomKinds.FirstOrDefault(k => k.Key == kind) is { } custom && ColorConverter.ConvertFromString(custom.Color) is Color c ? c
        : BuiltInColour(kind);

    private static Color BuiltInColour(string kind) => kind switch
    {
        "extra" => Color.FromRgb(0xFF, 0x9F, 0x0A),
        "self"  => Color.FromRgb(0xBF, 0x8C, 0xFF),
        "teach" => Color.FromRgb(0x30, 0xD1, 0x58),
        _       => Color.FromRgb(0x4D, 0xA3, 0xFF),
    };

    private static string KindName(string kind) =>
        AppSettings.Current.CustomKinds.FirstOrDefault(k => k.Key == kind)?.Name ?? BuiltInName(kind);

    private static string BuiltInName(string kind) => kind switch
    {
        "extra" => "Học thêm", "self" => "Tự học", "teach" => "Đi dạy", _ => "Trên lớp"
    };

    private static string Until(TimeSpan t) =>
        t.TotalMinutes >= 60 ? $"{(int)t.TotalHours} giờ{(t.Minutes > 0 ? $" {t.Minutes} phút" : "")}" : $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))} phút";

    /// <summary>The red "now" line with the time on its left.</summary>
    private static FrameworkElement NowLine(DateTime now)
    {
        var red = Color.FromRgb(0xFF, 0x45, 0x3A);
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = now.ToString("H:mm"), FontSize = 10.5, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI"), Foreground = Frozen(red), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) });
        var line = new Grid { VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Frozen(red), HorizontalAlignment = HorizontalAlignment.Left });
        line.Children.Add(new Border { Height = 1.5, Background = Frozen(red), Margin = new Thickness(3, 0, 0, 0) });
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        return grid;
    }

    /// <summary>One session: start time on the left, then a coloured block with name, time, kind / countdown and ✕.</summary>
    private FrameworkElement ScheduleBlock(ClassSlot c, Color colour, bool live, bool isNext, TimeSpan nowT)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock
        {
            Text = ScheduleService.Hm(c.Start), FontSize = 10.5, FontFamily = new FontFamily("Segoe UI"),
            Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 8, 0)
        });

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition());
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = Frozen(colour), Margin = new Thickness(0, 1, 9, 1) });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = c.Subject, FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        int length = (int)(c.End - c.Start).TotalMinutes;
        text.Children.Add(new TextBlock
        {
            Text = $"{ScheduleService.Hm(c.Start)} – {ScheduleService.Hm(c.End)} · {length} phút" + (c.Room != null ? $" · {c.Room}" : "")
                 + (live || isNext ? $" · {KindName(ScheduleService.Kind(c.Subject))}" : "")
                 + (c.Origin == "typed" ? (c.Date == null ? " · tự thêm, mỗi tuần" : " · tự thêm") : c.Date == null ? "" : $" · chỉ ngày {c.Date:dd/MM}"),
            FontSize = 10.5, FontFamily = new FontFamily("Segoe UI"), Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(text, 1);
        inner.Children.Add(text);

        string badge = live ? $"đang học · còn {Until(c.End - nowT)}" : isNext ? $"sau {Until(c.Start - nowT)}" : KindName(ScheduleService.Kind(c.Subject));
        var tag = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0),
            Background = live || isNext ? Frozen(Color.FromArgb(0x4D, colour.R, colour.G, colour.B)) : Brushes.Transparent,
            Child = new TextBlock { Text = badge, FontSize = 10.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), Foreground = Frozen(colour) }
        };
        Grid.SetColumn(tag, 2);
        inner.Children.Add(tag);

        var remove = new TextBlock
        {
            Text = "\uE711", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 9, Cursor = Cursors.Hand, Padding = new Thickness(6),
            Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Xoá buổi này", Visibility = Visibility.Hidden
        };
        var id = c.Id;
        remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; _schedule?.Remove(id); };
        Grid.SetColumn(remove, 3);
        inner.Children.Add(remove);

        bool strong = live || isNext;
        var box = new Border
        {
            Child = inner, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 5, 4, 5),
            Background = Frozen(Color.FromArgb((byte)(strong ? 0x48 : 0x22), colour.R, colour.G, colour.B)),
            BorderThickness = new Thickness(1), BorderBrush = strong ? Frozen(Color.FromArgb(0xCC, colour.R, colour.G, colour.B)) : Brushes.Transparent
        };
        box.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        box.MouseLeave += (_, _) => remove.Visibility = Visibility.Hidden;
        box.Cursor = Cursors.Hand;
        box.ToolTip = "Bấm để đổi loại buổi" + (c.Origin == "typed" ? " hoặc sửa" : "");
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; _kindPickerFor = _kindPickerFor == c.Id ? null : c.Id; ResizeAfter(RebuildSchedule); };
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        if (c.Origin == "typed")
        {
            // Typed in by hand: a dashed outline
            var dash = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 9, RadiusY = 9, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                Stroke = Frozen(Color.FromArgb(0xB0, colour.R, colour.G, colour.B)), IsHitTestVisible = false
            };
            Grid.SetColumn(dash, 1);
            grid.Children.Add(dash);
        }

        if (_kindPickerFor != c.Id) return grid;
        var wrap = new StackPanel();
        wrap.Children.Add(grid);
        wrap.Children.Add(KindPicker(c));
        return wrap;
    }

    private Guid? _kindPickerFor;

    /// <summary>Under a session: pick its kind (for every session with that name), make a new kind, or edit it.</summary>
    private FrameworkElement KindPicker(ClassSlot c)
    {
        var row = new WrapPanel { Margin = new Thickness(54, -1, 0, 7) };
        string now = ScheduleService.Kind(c.Subject);
        var kinds = new[] { "class", "extra", "self", "teach" }.Concat(AppSettings.Current.CustomKinds.Select(k => k.Key));
        Border Chip(string text, Color colour, bool on, Action click)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 5, 5), Cursor = Cursors.Hand,
                Background = Frozen(Color.FromArgb((byte)(on ? 0x55 : 0x22), colour.R, colour.G, colour.B)),
                BorderThickness = new Thickness(1), BorderBrush = on ? Frozen(colour) : Brushes.Transparent,
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), Foreground = Frozen(colour) }
            };
            chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return chip;
        }
        foreach (var k in kinds)
        {
            var key = k;
            row.Children.Add(Chip(KindName(key), KindColour(key), key == now, () =>
            {
                _kindPickerFor = null;
                _schedule?.SetKind(c.Subject, key);
                SayScheduleHint($"Mọi buổi \u201C{c.Subject}\u201D giờ là {KindName(key)}");
            }));
        }
        var white = Color.FromRgb(0xEE, 0xEE, 0xF2);
        row.Children.Add(Chip("+ Loại mới…", white, false, () => OpenScheduleEditor(namingKindFor: c.Subject)));
        if (c.Origin == "typed")
            row.Children.Add(Chip("Sửa buổi này", white, false, () => OpenScheduleEditor(editing: c)));
        return row;
    }

    // ── Typing a session (or a new kind's name) ──
    private bool _addOnce = true;          // only on this day / every week
    private Guid? _editingClass;
    private string? _namingKindFor;

    private void ScheduleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleAddRow.Visibility == Visibility.Visible && _editingClass == null && _namingKindFor == null) { CloseScheduleEditor(); return; }
        OpenScheduleEditor();
    }

    private void OpenScheduleEditor(ClassSlot? editing = null, string? namingKindFor = null)
    {
        if (_scheduleImporting) ShowScheduleImport(false);
        _editingClass = editing?.Id;
        _namingKindFor = namingKindFor;
        _addOnce = editing == null || editing.Date != null;
        ResizeAfter(() =>
        {
            ScheduleAddRow.Visibility = Visibility.Visible;
            ScheduleAddBox.Text = editing == null ? "" :
                $"{editing.Subject} {ScheduleService.Hm(editing.Start)}-{ScheduleService.Hm(editing.End)}" + (editing.Room != null ? $" phòng {editing.Room}" : "");
            ScheduleAddPlaceholder.Text = namingKindFor != null ? "Tên loại mới, vd: Thể thao, CLB, Làm thêm · Enter để lưu"
                                                                : "vd: Họp nhóm SWP 15h-16h30 phòng 304 · Enter để lưu";
            BuildRepeatNav();
        });
        BeginTyping(ScheduleAddBox);
        ScheduleAddBox.CaretIndex = ScheduleAddBox.Text.Length;
    }

    private void BuildRepeatNav()
    {
        ScheduleRepeatNav.Children.Clear();
        bool naming = _namingKindFor != null;
        ScheduleRepeatNav.Visibility = naming ? Visibility.Collapsed : Visibility.Visible;
        ScheduleAddNote.Text = naming ? $"cho mọi buổi \u201C{Short(_namingKindFor!, 24)}\u201D · Esc để huỷ"
                                      : _addOnce ? $"chỉ {Weekdays[(int)_scheduleDay.DayOfWeek]} {_scheduleDay:dd/MM} · Esc để huỷ"
                                                 : $"mỗi {Weekdays[(int)_scheduleDay.DayOfWeek]} · Esc để huỷ";
        if (naming) return;
        foreach (var (once, label) in new[] { (true, "Chỉ ngày này"), (false, "Mỗi tuần") })
        {
            var seg = Segment(label, _addOnce == once, () => { _addOnce = once; BuildRepeatNav(); });
            seg.Padding = new Thickness(9, 3, 9, 3);
            ScheduleRepeatNav.Children.Add(seg);
        }
    }

    private void CloseScheduleEditor()
    {
        _editingClass = null;
        _namingKindFor = null;
        EndTyping(collapse: false);
        ResizeAfter(() => ScheduleAddRow.Visibility = Visibility.Collapsed);
    }

    private void ScheduleAddBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_typing) return;
        BeginTyping(ScheduleAddBox);
        e.Handled = true;
    }

    private void ScheduleAddBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ScheduleAddPlaceholder.Visibility = ScheduleAddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScheduleAddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseScheduleEditor(); return; }
        if (e.Key != Key.Enter || _schedule == null) return;
        e.Handled = true;
        var text = ScheduleAddBox.Text.Trim();
        if (text.Length == 0) return;

        if (_namingKindFor is { } subject)
        {
            var key = ScheduleService.AddKind(text);
            _kindPickerFor = null;
            CloseScheduleEditor();
            _schedule.SetKind(subject, key);
            SayScheduleHint($"Đã tạo loại \u201C{text}\u201D cho mọi buổi \u201C{subject}\u201D");
            return;
        }

        var slot = ScheduleImport.FromLine(text);
        if (slot == null) { SayScheduleHint("Thiếu giờ rồi, vd: Họp nhóm 15h-16h30 (chỉ giờ bắt đầu thì tính 1 tiếng)", ok: false); return; }
        slot.Weekday = ScheduleImport.Iso(_scheduleDay.DayOfWeek);
        slot.Date = _addOnce ? _scheduleDay.Date : null;
        bool editing = _editingClass is { } id;
        if (editing) _schedule.UpdateTyped(_editingClass!.Value, slot);
        else _schedule.AddTyped(slot);
        _kindPickerFor = null;
        CloseScheduleEditor();
        SayScheduleHint((editing ? "Đã sửa: " : "Đã thêm: ") + $"{slot.Subject} {ScheduleService.Hm(slot.Start)}–{ScheduleService.Hm(slot.End)}"
                        + (slot.Date != null ? $" · {Weekdays[(int)_scheduleDay.DayOfWeek]} {_scheduleDay:dd/MM}" : $" · mỗi {Weekdays[(int)_scheduleDay.DayOfWeek]}"));
    }

    private void SayScheduleHint(string text, bool ok = true)
    {
        ScheduleHint.Text = text;
        ScheduleHint.Foreground = ok ? Frozen(Color.FromRgb(0x7F, 0xE3, 0xE3)) : Frozen(Color.FromRgb(0xFF, 0xC0, 0x5A));
        ScheduleHint.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>After an import: show the classes (the day with the most, or today).</summary>
    private void ScheduleImported(string message, bool ok)
    {
        SayScheduleHint(message, ok);
        if (ok && ScheduleServiceHasAny)
        {
            _scheduleDay = ScheduleService.On(DateTime.Today).Count > 0 ? DateTime.Today
                : Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i)).FirstOrDefault(d => ScheduleService.On(d).Count > 0, DateTime.Today);
            ResizeAfter(() => ShowScheduleImport(false));
        }
        else ResizeAfter(() => ShowScheduleImport(true));
    }

    private async void SheetLink_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { EndTyping(collapse: false); e.Handled = true; return; }
        if (e.Key != Key.Enter || _schedule == null) return;
        e.Handled = true;
        var link = SheetLink.Text.Trim();
        if (link.Length == 0) return;
        EndTyping(collapse: false);
        await LoadSheetPreview(link, "auto");
    }

    // ── A sheet's preview: what was found, how to read it, save / cancel ──
    private ScheduleService.SheetPreview? _sheetPreview;

    private async System.Threading.Tasks.Task LoadSheetPreview(string link, string mode)
    {
        SayScheduleHint("Đang đọc sheet…");
        var (preview, problem) = await ScheduleService.PreviewSheetAsync(link, mode);
        if (preview == null) { SayScheduleHint(problem ?? "", ok: false); return; }
        SayScheduleHint("");
        _sheetPreview = preview;
        ResizeAfter(() =>
        {
            SchedulePreview.Visibility = Visibility.Visible;
            SchedulePreviewTitle.Text = $"Đọc được từ \u201C{preview.Title}\u201D";
            SchedulePreviewText.Text = preview.Summary + (preview.Exams.Count > 0 ? " · lịch thi sẽ vào Hạn chót và được đếm ngược theo ngày" : "");
            SchedulePreviewModes.Children.Clear();
            foreach (var (key, label) in new[] { ("auto", "Tự nhận"), ("classes", "Tất cả là buổi học"), ("exams", "Ngày cụ thể là lịch thi") })
            {
                var seg = Segment(label, preview.Mode == key, () => { if (preview.Mode != key) _ = LoadSheetPreview(link, key); });
                seg.Padding = new Thickness(9, 3, 9, 3);
                seg.Margin = new Thickness(0, 0, 4, 0);
                SchedulePreviewModes.Children.Add(seg);
            }
        });
    }

    private void SchedulePreviewSave_Click(object sender, RoutedEventArgs e)
    {
        if (_sheetPreview == null || _schedule == null) return;
        var msg = _schedule.Apply(_sheetPreview);
        _sheetPreview = null;
        SchedulePreview.Visibility = Visibility.Collapsed;
        SheetLink.Text = "";
        ScheduleImported(msg, ok: true);
    }

    private void SchedulePreviewCancel_Click(object sender, RoutedEventArgs e)
    {
        _sheetPreview = null;
        ResizeAfter(() => SchedulePreview.Visibility = Visibility.Collapsed);
    }

    /// <summary>The linked sheets, each with what it brought and a way to unlink it.</summary>
    private void RebuildSources()
    {
        ScheduleSources.Children.Clear();
        foreach (var src in AppSettings.Current.ScheduleSources)
        {
            var key = src.Key;
            string ago = src.SyncedUtc is { } t ? Ago(t.ToLocalTime()) : "chưa đọc";
            var line = $"{src.Name} · {src.Classes} buổi" + (src.Exams > 0 ? $" · {src.Exams} lịch thi" : "") + $" · đọc {ago}";
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock
            {
                Text = "\uE8A5  " + line, FontSize = 11, FontFamily = new FontFamily("Segoe UI, Segoe MDL2 Assets"), Foreground = Frozen(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            var remove = new TextBlock
            {
                Text = "Gỡ", FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), Cursor = Cursors.Hand,
                Foreground = Frozen(Color.FromRgb(0xFF, 0x8A, 0x84)), Padding = new Thickness(8, 2, 2, 2), ToolTip = "Gỡ sheet này và những gì nó thêm vào"
            };
            remove.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; _schedule?.RemoveSource(key); SayScheduleHint("Đã gỡ sheet"); ResizeAfter(() => ShowScheduleImport(true)); };
            Grid.SetColumn(remove, 1);
            grid.Children.Add(remove);
            ScheduleSources.Children.Add(grid);
        }
    }

    private void SheetLink_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_typing) return;
        BeginTyping(SheetLink);
        e.Handled = true;
    }

    private void SheetLink_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        SheetLinkPlaceholder.Visibility = SheetLink.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SchedulePasteImage_Click(object sender, RoutedEventArgs e)
    {
        BitmapSource? picture = null;
        try { if (Clipboard.ContainsImage()) picture = Clipboard.GetImage(); } catch { }
        if (picture == null) { SayScheduleHint("Chưa có ảnh trong clipboard. Chụp lịch bằng Win + Shift + S rồi bấm lại, hoặc kéo ảnh vào đây.", ok: false); return; }
        await ReadSchedulePicture(SavePng(picture));
    }

    private static string SavePng(BitmapSource picture)
    {
        System.IO.Directory.CreateDirectory(DragFolder);
        var path = System.IO.Path.Combine(DragFolder, $"lich-{DateTime.Now:HHmmss}.png");
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(picture));
        using (var fs = System.IO.File.Create(path)) png.Save(fs);
        return path;
    }

    private async System.Threading.Tasks.Task ReadSchedulePicture(string path)
    {
        if (_schedule == null) return;
        SayScheduleHint("Đang đọc ảnh…");
        var (msg, count) = await _schedule.FromImageAsync(path);
        ScheduleImported(msg, count > 0);
    }

    private void ScheduleFile_Click(object sender, RoutedEventArgs e)
    {
        if (_schedule == null) return;
        _pickingFile = true;
        NativeInterop.SetActivatable(this, true);
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn file lịch học",
                Filter = "Lịch học (*.xlsx;*.csv;*.ics;*.png;*.jpg)|*.xlsx;*.csv;*.ics;*.png;*.jpg;*.jpeg|Tất cả|*.*"
            };
            if (dialog.ShowDialog(this) == true) _ = ImportScheduleFile(dialog.FileName);
        }
        finally
        {
            _pickingFile = false;
            NativeInterop.SetActivatable(this, false);
        }
    }

    private bool _pickingFile;

    private async System.Threading.Tasks.Task ImportScheduleFile(string path)
    {
        if (_schedule == null) return;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp") { await ReadSchedulePicture(path); return; }
        var msg = _schedule.FromFile(path);
        ScheduleImported(msg, msg.StartsWith("Đã"));
    }

    private void Schedule_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Schedule_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_dragging) return;   // our own copied item dragged over
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await ImportScheduleFile(files[0]);
        else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource picture) await ReadSchedulePicture(SavePng(picture));
    }

    /// <summary>A timetable file or picture dropped anywhere on the open panel goes to Lịch học.</summary>
    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        if (_dragging) return;
        _closeTimer.Stop();
        if (_tab != "schedule" && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)))
        {
            SelectTab("schedule", Array.IndexOf(Tabs, "schedule") - Array.IndexOf(Tabs, _tab));
            ShowScheduleImport(true);
        }
    }

    private void ScheduleRemind_Click(object sender, MouseButtonEventArgs e)
    {
        AppSettings.Current.ScheduleReminders = !AppSettings.Current.ScheduleReminders;
        AppSettings.Current.Save();
        ShowScheduleImport(_scheduleImporting);
    }

    private bool _confirmClearSchedule;

    private void ScheduleClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_confirmClearSchedule)
        {
            _confirmClearSchedule = true;
            ScheduleClearBtn.Content = "Bấm lần nữa để xoá";
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) => { t.Stop(); _confirmClearSchedule = false; ScheduleClearBtn.Content = "Xoá lịch"; };
            t.Start();
            return;
        }
        _confirmClearSchedule = false;
        ScheduleClearBtn.Content = "Xoá lịch";
        _schedule?.Clear();
        SheetLink.Text = "";
        SayScheduleHint("Đã xoá lịch học");
        ResizeAfter(() => ShowScheduleImport(true));
    }

    // ── Recent notifications ──────────────────────────────────────────
    private string? _notifFilter;                       // show only this app, or all
    private readonly Dictionary<string, Color> _appColours = new();

    private void RebuildRecent()
    {
        var items = _history.Items.ToList();   // newest first
        var apps = items.GroupBy(e => e.Data.AppName)
                        .Select(g => (Name: g.Key, Count: g.Count(), Sample: g.First().Data))
                        .ToList();
        if (_notifFilter != null && apps.All(a => a.Name != _notifFilter)) _notifFilter = null;

        // Filter chips, one per app (only worth it with more than one app)
        NotifFilters.Children.Clear();
        if (apps.Count > 1)
        {
            NotifFilters.Children.Add(MakeFilterChip(null, "Tất cả", items.Count, null));
            foreach (var app in apps) NotifFilters.Children.Add(MakeFilterChip(app.Name, app.Name, app.Count, app.Sample));
        }
        else if (items.Count > 0)
        {
            NotifFilters.Children.Add(new TextBlock
            {
                Text = $"{items.Count} thông báo gần đây", Foreground = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                FontSize = 11.5, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center
            });
        }

        var shown = _notifFilter == null ? items : items.Where(e => e.Data.AppName == _notifFilter).ToList();
        NoNotificationsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearAllBtn.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        RecentList.Children.Clear();
        string? group = null;
        foreach (var (time, data) in shown)
        {
            var g = time.Date == DateTime.Today ? "HÔM NAY" : time.Date == DateTime.Today.AddDays(-1) ? "HÔM QUA" : "TRƯỚC ĐÓ";
            if (g != group)
            {
                group = g;
                RecentList.Children.Add(new TextBlock
                {
                    Text = g, Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(4, RecentList.Children.Count == 0 ? 0 : 6, 0, 4)
                });
            }
            RecentList.Children.Add(MakeNotificationCard(time, data));
        }
    }

    /// <summary>The app's colour: from its logo, else a steady colour from its name.</summary>
    private Color AppColour(NotificationData data)
    {
        if (_appColours.TryGetValue(data.AppName, out var c)) return c;
        c = ColorExtractor.Dominant(data.Icon) is { } d ? ColorExtractor.EnsureBright(ColorExtractor.Vivid(d)) : NameColour(data.AppName);
        if (c == Colors.White) c = NameColour(data.AppName);
        return _appColours[data.AppName] = c;
    }

    private static Color NameColour(string name)
    {
        Color[] palette =
        {
            Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xFF, 0x9F, 0x0A),
            Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0x64, 0xD2, 0xFF),
            Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xFF, 0xD6, 0x0A)
        };
        uint h = 0;
        foreach (var ch in name) h = h * 31 + ch;
        return palette[h % (uint)palette.Length];
    }

    private FrameworkElement Avatar(NotificationData data, double size)
    {
        var avatar = new Grid
        {
            Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top,
            Clip  = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2)
        };
        if (data.Icon != null)
        {
            avatar.Children.Add(new Image { Source = data.Icon, Stretch = Stretch.UniformToFill });
        }
        else
        {
            avatar.Children.Add(new Border { Background = Frozen(AppColour(data)) });
            avatar.Children.Add(new TextBlock
            {
                Text = data.AppName.Length > 0 ? data.AppName[..1].ToUpperInvariant() : "?",
                Foreground = Brushes.White, FontSize = size * 0.45, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        }
        return avatar;
    }

    private UIElement MakeFilterChip(string? app, string label, int count, NotificationData? sample)
    {
        bool on = _notifFilter == app;
        var colour = sample != null ? AppColour(sample) : Colors.White;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (sample != null) row.Children.Add(Avatar(sample, 14));
        row.Children.Add(new TextBlock
        {
            Text = label, Foreground = on ? Brushes.White : Frozen(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
            FontSize = 11.5, FontFamily = new FontFamily("Segoe UI"), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(sample != null ? 6 : 0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = count.ToString(), Foreground = Frozen(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            FontSize = 11, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center
        });
        var chip = new Border
        {
            Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 6, 4), Cursor = Cursors.Hand,
            Background = on ? Frozen(Color.FromArgb(0x40, colour.R, colour.G, colour.B)) : Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            BorderBrush = on ? Frozen(Color.FromArgb(0xAA, colour.R, colour.G, colour.B)) : Brushes.Transparent
        };
        chip.MouseLeftButtonUp += (_, _) => { _notifFilter = app; RebuildRecent(); };
        return chip;
    }

    private UIElement MakeNotificationCard(DateTime time, NotificationData data)
    {
        var colour = AppColour(data);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Avatar(data, 28));

        var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0) };
        var top = new TextBlock { FontFamily = new FontFamily("Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis };
        top.Inlines.Add(new System.Windows.Documents.Run(data.AppName)
        {
            Foreground = Frozen(colour), FontWeight = FontWeights.SemiBold, FontSize = 11.5
        });
        top.Inlines.Add(new System.Windows.Documents.Run("  ·  " + Ago(time))
        {
            Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), FontSize = 11
        });
        text.Children.Add(top);

        bool hasTitle = !string.IsNullOrWhiteSpace(data.Title) && data.Title != data.AppName;
        if (hasTitle)
        {
            text.Children.Add(new TextBlock
            {
                Text = data.Title, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0)
            });
        }
        if (!string.IsNullOrWhiteSpace(data.Body))
        {
            text.Children.Add(new TextBlock
            {
                Text = data.Body.ReplaceLineEndings(" "), Foreground = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                FontSize = 12, FontFamily = new FontFamily("Segoe UI"),
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
                ToolTip = data.Body
            });
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Hover actions: mute this app, remove
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Hidden };
        if (data.SourceKey != null)
        {
            var mute = new Button
            {
                Content = "", Style = (Style)FindResource("QuickButton"), Width = 24, Height = 24, FontSize = 11,
                Margin = new Thickness(0), ToolTip = $"Tắt thông báo từ {data.AppName}"
            };
            mute.Click += (_, e) => { e.Handled = true; MuteRequested?.Invoke(data); };
            actions.Children.Add(mute);
        }
        var remove = new Button
        {
            Content = "", Style = (Style)FindResource("QuickButton"), Width = 24, Height = 24, FontSize = 10,
            Margin = new Thickness(0), ToolTip = "Xoá thông báo này"
        };
        remove.Click += (_, e) => { e.Handled = true; _history.Remove(data); RebuildRecent(); };
        actions.Children.Add(remove);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var normal = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        var hover  = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        var card = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 5), Background = normal, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = Frozen(Color.FromArgb(0xCC, colour.R, colour.G, colour.B))
        };
        card.MouseEnter += (_, _) => { card.Background = hover;  actions.Visibility = Visibility.Visible; };
        card.MouseLeave += (_, _) => { card.Background = normal; actions.Visibility = Visibility.Hidden; };
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (actions.IsMouseOver) return;
            IslandWindow.OpenSource(data, data.AppUserModelId);
            Collapse();
        };
        return card;
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        _notifFilter = null;
        RebuildRecent();
    }

    /// <summary>"vừa xong", "5 phút trước", "14:20", "Hôm qua 09:10", "02/11 18:00".</summary>
    private static string Ago(DateTime time)
    {
        var d = DateTime.Now - time;
        if (d.TotalSeconds < 45) return "vừa xong";
        if (d.TotalMinutes < 60) return $"{Math.Max(1, (int)d.TotalMinutes)} phút trước";
        if (time.Date == DateTime.Today) return d.TotalHours < 6 ? $"{(int)d.TotalHours} giờ trước" : time.ToString("HH:mm");
        if (time.Date == DateTime.Today.AddDays(-1)) return "Hôm qua " + time.ToString("HH:mm");
        return time.ToString("dd/MM HH:mm");
    }

    // ── Typing (clipboard search) ─────────────────────────────────────
    // The notch doesn't normally take the keyboard (so it never steals focus
    // from your work). Clicking the box lets it, until you're done.
    private TextBox? _typingBox;

    private void BeginTyping(TextBox box)
    {
        _typing = true;
        _typingBox = box;
        _closeTimer.Stop();
        NativeInterop.SetActivatable(this, true);
        Activate();
        box.Focus();
        Keyboard.Focus(box);
        box.CaretIndex = box.Text.Length;
    }

    private void EndTyping(bool collapse)
    {
        if (!_typing) return;
        _typing = false;
        _typingBox = null;
        Keyboard.ClearFocus();
        NativeInterop.SetActivatable(this, false);
        if (collapse) Collapse();
        else if (_expanded && !NativeInterop.IsCursorOver(Expanded)) _closeTimer.Start();
    }

    // ── Giữ im Dynamic: pinned open ───────────────────────────────────
    private bool _pinned;

    /// <summary>Pinned: the panel stays open when the pointer leaves (to copy several things in a row).</summary>
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (value == _pinned) return;
            _pinned = value;
            UpdatePinButton();
            PinnedChanged?.Invoke(value);
            if (value) _closeTimer.Stop();
            else if (_expanded && !NativeInterop.IsCursorOver(Expanded)) _closeTimer.Start();
        }
    }

    /// <summary>For the tray menu's tick.</summary>
    public event Action<bool>? PinnedChanged;

    /// <summary>From the tray: pin and open, or unpin and close.</summary>
    public void SetPinned(bool on)
    {
        Pinned = on;
        if (on && !_expanded) Expand();
        else if (!on && _expanded && !NativeInterop.IsCursorOver(Expanded)) Collapse();
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => Pinned = !Pinned;

    private void UpdatePinButton()
    {
        var on = Color.FromRgb(0xFF, 0x9F, 0x0A);
        PinBtn.Content    = _pinned ? "\uE841" : "\uE718";   // PinFill / Pin
        PinBtn.Foreground = _pinned ? Frozen(on) : Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        PinBtn.Background = _pinned ? Frozen(Color.FromArgb(0x33, on.R, on.G, on.B)) : Brushes.Transparent;
        PinBtn.ToolTip    = _pinned ? "Đang giữ im · bấm để thả (hoặc Esc khi chuột trên bảng)"
                                    : "Giữ im Dynamic: bảng không tự đóng khi chuột rời đi";
    }

    // ── Drag a copied item out (to Messenger, Word, a folder…) ──
    private Point? _dragStart;
    private bool _dragging, _dragDone;

    /// <summary>Where dragged pictures are written as .png files; emptied when the app starts and quits.</summary>
    internal static readonly string DragFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DynamicIsland-drag");

    internal static void ClearDragFolder()
    {
        try { if (System.IO.Directory.Exists(DragFolder)) System.IO.Directory.Delete(DragFolder, recursive: true); } catch { }
    }

    private void MakeDraggable(FrameworkElement element, ClipItem item)
    {
        element.PreviewMouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(this); _dragDone = false; };
        element.PreviewMouseLeftButtonUp   += (_, _) => _dragStart = null;
        element.PreviewMouseMove += (_, e) =>
        {
            if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance * 2 &&
                Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _dragStart = null;
            e.Handled = true;
            StartDrag(element, item);
        };
    }

    private void StartDrag(DependencyObject source, ClipItem item)
    {
        var data = DragData(item);
        if (data == null) return;
        _dragging = _dragDone = true;
        _closeTimer.Stop();
        try { DragDrop.DoDragDrop(source, data, DragDropEffects.Copy); }
        catch (Exception ex) { CrashLog.Write($"Drag failed: {ex.GetType().Name}"); }
        finally
        {
            _dragging = false;
            if (_expanded && !_pinned && !NativeInterop.IsCursorOver(Expanded)) _closeTimer.Start();
        }
    }

    /// <summary>Text as text; a picture as the picture itself plus a .png file (folders, chat apps take files).</summary>
    private static DataObject? DragData(ClipItem item)
    {
        if (!item.IsImage)
            return string.IsNullOrEmpty(item.Text) ? null : new DataObject(DataFormats.UnicodeText, item.Text);

        var picture = ClipboardService.LoadImage(item, 0);
        if (picture == null) return null;
        var data = new DataObject();
        data.SetImage(picture);
        try
        {
            System.IO.Directory.CreateDirectory(DragFolder);
            var file = System.IO.Path.Combine(DragFolder, $"anh-{item.Time:yyyyMMdd-HHmmss}.png");
            if (!System.IO.File.Exists(file))
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(picture));
                using var fs = System.IO.File.Create(file);
                png.Save(fs);
            }
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
        }
        catch { }
        return data;
    }

    // ── Quick buttons ─────────────────────────────────────────────────
    private void UpdateQuickButtons()
    {
        // The microphone's state comes from Windows' audio service, which can be slow
        // (Bluetooth headsets): asked in the background, the buttons update when it answers.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var muted = QuickActions.IsMicMuted();
            Dispatcher.BeginInvoke(new Action(() => ShowMic(muted)));
        });

        bool dnd = QuickActions.DoNotDisturb;
        DndBtn.Foreground = dnd ? Frozen(DndColour) : Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        DndBtn.Background = dnd ? Frozen(Color.FromArgb(0x33, DndColour.R, DndColour.G, DndColour.B)) : Brushes.Transparent;
        DndBtn.ToolTip    = dnd ? "Không làm phiền: BẬT · thông báo vẫn vào lịch sử" : "Không làm phiền: tắt";
        UpdateStrip();
    }

    private void ShowMic(bool? muted)
    {
        MicBtn.IsEnabled  = muted != null;
        MicBtn.Foreground = muted == true ? Frozen(Color.FromRgb(0xFF, 0x45, 0x3A)) : Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        MicBtn.ToolTip    = muted == null ? "Không tìm thấy micro"
                          : muted == true ? "Micro đang tắt · bấm để bật" : "Micro đang bật · bấm để tắt";
    }

    // ── Sound output: one click goes to the next speakers / headphones ──
    // Nothing is asked of Windows until the button is clicked, and then off the UI thread.
    private bool _switchingOutput;

    private void Output_Click(object sender, RoutedEventArgs e)
    {
        if (_switchingOutput) return;
        _switchingOutput = true;
        OutputBtn.Opacity = 0.5;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var outputs = AudioOutputs.List();
            AudioOutputs.Output? next = null;
            bool ok = false;
            if (outputs.Count >= 2)
            {
                int i = Math.Max(0, outputs.ToList().FindIndex(o => o.IsDefault));
                next = outputs[(i + 1) % outputs.Count];
                ok = AudioOutputs.Use(next.Id);
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _switchingOutput = false;
                OutputBtn.Opacity = 1;
                if (next == null) Notice?.Invoke("Chỉ có một loa", "Cắm tai nghe hoặc kết nối Bluetooth rồi bấm lại nhé");
                else if (ok)
                {
                    OutputBtn.Content = next.Headphones ? "\uE7F6" : "\uE7F5";
                    Notice?.Invoke(next.Headphones ? "Đã chuyển sang tai nghe" : "Đã chuyển sang loa", next.Name);
                }
                else Notice?.Invoke("Không chuyển được loa", "Thử chọn trong biểu tượng loa ở thanh taskbar nhé");
            }));
        });
    }

    private void Mic_Click(object sender, RoutedEventArgs e)
    {
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var muted = QuickActions.ToggleMic();
            Dispatcher.BeginInvoke(new Action(() => ShowMic(muted)));
        });
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

    // ── Clipboard history ─────────────────────────────────────────────
    private readonly Dictionary<Guid, BitmapSource?> _thumbs = new();
    private readonly List<(TextBlock Label, ClipItem Item)> _clipTimes = new();
    private readonly DispatcherTimer _clipToastTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Guid? _justCopied;

    private void RebuildClipboard()
    {
        if (!_expanded || _tab != "clipboard") return;
        _clipTimes.Clear();
        var all = _clips.Items;                      // pinned first, then newest

        // The switch: Văn bản 12 | Hình ảnh 18
        bool texts = _clipMode == "texts";
        ClipModeTextsLabel.Text  = $"Văn bản  {all.Count(i => !i.IsImage)}";
        ClipModeImagesLabel.Text = $"Hình ảnh  {all.Count(i => i.IsImage)}";
        var on  = Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        ClipModeTexts.Background  = texts ? on : Brushes.Transparent;
        ClipModeImages.Background = texts ? Brushes.Transparent : on;
        ClipModeTexts.Opacity  = texts ? 1 : 0.65;
        ClipModeImages.Opacity = texts ? 0.65 : 1;
        ((Panel)ClipModeTexts.Child).Children.OfType<TextBlock>().ToList().ForEach(t => t.Foreground = Brushes.White);
        ((Panel)ClipModeImages.Child).Children.OfType<TextBlock>().ToList().ForEach(t => t.Foreground = Brushes.White);
        TextsTab.Visibility  = texts ? Visibility.Visible : Visibility.Collapsed;
        ImagesTab.Visibility = texts ? Visibility.Collapsed : Visibility.Visible;

        if (texts) RebuildTexts(all);
        else RebuildImages(all);
    }

    private void ClipModeTexts_Click(object sender, MouseButtonEventArgs e)  => SetClipMode("texts");
    private void ClipModeImages_Click(object sender, MouseButtonEventArgs e) => SetClipMode("images");

    private void SetClipMode(string mode)
    {
        if (mode == _clipMode) return;
        if (_typing) EndTyping(collapse: false);
        ContentCard.BeginAnimation(HeightProperty, null);
        double from = ContentCard.ActualHeight;
        _clipMode = mode;
        RebuildClipboard();
        var panel = mode == "texts" ? (UIElement)TextsTab : ImagesTab;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(200)));
        ContentCard.UpdateLayout();
        double to = ContentCard.ActualHeight;
        if (Math.Abs(to - from) > 1)
            ContentCard.BeginAnimation(HeightProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            });
    }

    private void RebuildTexts(IReadOnlyList<ClipItem> all)
    {
        var mine   = all.Where(i => !i.IsImage).ToList();
        var newest = mine.OrderByDescending(i => i.Time).FirstOrDefault();
        var q      = ClipSearch.Text.Trim();
        var texts  = mine.Where(i => q.Length == 0
            || (i.Text ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || (i.Source ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        int pinned = mine.Count(i => i.Pinned);

        ClipSearchPlaceholder.Text = mine.Count == 0 ? "Tìm trong lịch sử copy…"
            : $"Tìm trong {mine.Count} đoạn chữ" + (pinned > 0 ? $" · {pinned} đã ghim" : "") + "…";
        ClipClearBtn.Visibility = mine.Any(i => !i.Pinned) ? Visibility.Visible : Visibility.Collapsed;
        ClipEmpty.Text = mine.Count == 0 ? "Chưa có gì. Copy (Ctrl + C) một đoạn chữ, nó sẽ hiện ở đây."
                       : $"Không tìm thấy \u201C{q}\u201D";
        ClipEmpty.Visibility = texts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ClipTextList.Children.Clear();
        bool hadPinned = false;
        foreach (var item in texts)
        {
            if (item.Pinned) hadPinned = true;
            else if (hadPinned)   // a thin line between pinned and recent
            {
                ClipTextList.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 2, 6, 6), Background = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
                hadPinned = false;
            }
            ClipTextList.Children.Add(MakeClipTextRow(item, item == newest));
        }
    }

    private void RebuildImages(IReadOnlyList<ClipItem> all)
    {
        var images = all.Where(i => i.IsImage).ToList();
        var newest = images.OrderByDescending(i => i.Time).FirstOrDefault();
        int pinned = images.Count(i => i.Pinned);

        ImgCount.Text = images.Count == 0 ? "" : $"{images.Count} ảnh" + (pinned > 0 ? $" · {pinned} đã ghim" : "") + " · lăn chuột hoặc kéo thanh bên dưới để xem hết";
        ImgClearBtn.Visibility = images.Any(i => !i.Pinned) ? Visibility.Visible : Visibility.Collapsed;
        ImgEmpty.Visibility = images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipImageScroll.Visibility = images.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        ClipImageList.Children.Clear();
        foreach (var item in images) ClipImageList.Children.Add(MakeClipImageTile(item, item == newest));

        // Forget thumbnails of deleted images
        var ids = images.Select(i => i.Id).ToHashSet();
        foreach (var id in _thumbs.Keys.Where(id => !ids.Contains(id)).ToList()) _thumbs.Remove(id);
    }

    /// <summary>"Edge · 2 phút trước · 120 ký tự"</summary>
    private static string ClipMeta(ClipItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Source)) parts.Add(item.Source!);
        parts.Add(Ago(item.Time));
        if (!item.IsImage) parts.Add($"{(item.Text ?? "").Length} ký tự");
        return string.Join(" · ", parts);
    }

    private void RefreshClipTimes()
    {
        foreach (var (label, item) in _clipTimes) label.Text = ClipMeta(item);
    }

    /// <summary>A small coloured label: "Vừa copy" for the newest, "✓ Đã chép" right after copying.</summary>
    private Border? Badge(ClipItem item, bool newest)
    {
        bool copied = _justCopied == item.Id;
        if (!copied && !newest) return null;
        var c = copied ? Color.FromRgb(0x34, 0xC7, 0x59) : (_hasColour ? _accent : Color.FromRgb(0x0A, 0x84, 0xFF));
        return new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 6, 0),
            Background = Frozen(Color.FromArgb(0x33, c.R, c.G, c.B)), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = copied ? "✓ Đã chép" : "Vừa copy", Foreground = Frozen(c), FontSize = 10.5,
                FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI")
            }
        };
    }

    private UIElement MakeClipTextRow(ClipItem item, bool newest)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var full = (item.Text ?? "").Trim();
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = full.Length > 400 ? full[..400] : full, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36, ToolTip = full.Length > 800 ? full[..800] + "…" : full
        });

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        if (Badge(item, newest) is { } badge) meta.Children.Add(badge);
        if (item.Pinned)
        {
            meta.Children.Add(new TextBlock
            {
                Text = "\uE718", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10.5, Margin = new Thickness(0, 0, 6, 0),
                Foreground = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A)), VerticalAlignment = VerticalAlignment.Center
            });
        }
        var when = new TextBlock
        {
            Text = ClipMeta(item), FontSize = 11, FontFamily = new FontFamily("Segoe UI"),
            Foreground = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center
        };
        _clipTimes.Add((when, item));
        meta.Children.Add(when);
        body.Children.Add(meta);
        grid.Children.Add(body);

        var buttons = MakeClipButtons(item);
        buttons.VerticalAlignment = VerticalAlignment.Top;
        buttons.Visibility = Visibility.Hidden;
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var normal = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        var hover  = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        var a = _hasColour ? _accent : Colors.White;
        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 6, 7),
            Margin = new Thickness(0, 0, 0, 5), Background = normal, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = newest ? Frozen(Color.FromArgb(0x99, a.R, a.G, a.B)) : Brushes.Transparent
        };
        row.MouseEnter += (_, _) => { row.Background = hover;  buttons.Visibility = Visibility.Visible; };
        row.MouseLeave += (_, _) => { row.Background = normal; buttons.Visibility = Visibility.Hidden; };
        row.MouseLeftButtonUp += (_, _) => { if (!buttons.IsMouseOver && !_dragDone) CopyClip(item); _dragDone = false; };
        MakeDraggable(row, item);
        return row;
    }

    private UIElement MakeClipImageTile(ClipItem item, bool newest)
    {
        if (!_thumbs.TryGetValue(item.Id, out var thumb))
            _thumbs[item.Id] = thumb = ClipboardService.LoadImage(item, 190);

        const double W = 150, H = 94;
        var box = new Grid { Width = W, Height = H, Clip = new RectangleGeometry(new Rect(0, 0, W, H), 10, 10) };
        box.Children.Add(new Border { Background = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E)) });
        // ImageBrush crops around the middle of the picture (an Image would keep its top-left)
        if (thumb != null) box.Children.Add(new Border { Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill } });

        // Time along the bottom on a soft shade
        var shade = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(7, 12, 7, 4),
            Background = new LinearGradientBrush(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0xCC, 0, 0, 0), 90)
        };
        var when = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Source) ? Ago(item.Time) : $"{item.Source} · {Ago(item.Time)}", Foreground = Frozen(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), FontSize = 11,
            FontFamily = new FontFamily("Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis
        };
        shade.Child = when;
        box.Children.Add(shade);

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        if (Badge(item, newest) is { } badge)
        {
            badge.Background = Frozen(Color.FromArgb(0xDD, 0, 0, 0));
            top.Children.Add(badge);
        }
        if (item.Pinned)
        {
            top.Children.Add(new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Frozen(Color.FromArgb(0xDD, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10,
                    Foreground = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        box.Children.Add(top);

        var buttons = MakeClipButtons(item);
        buttons.Background = Frozen(Color.FromArgb(0xDD, 0, 0, 0));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.VerticalAlignment   = VerticalAlignment.Top;
        buttons.Margin = new Thickness(0, 4, 4, 0);
        buttons.Visibility = Visibility.Collapsed;
        box.Children.Add(buttons);

        var rest   = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        var accent = Frozen(_hasColour ? _accent : Colors.White);
        var tile = new Border
        {
            Child = box, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(1.5), BorderBrush = newest ? accent : rest, Cursor = Cursors.Hand
        };

        // Hover: a bigger preview
        var tip = new ToolTip { Background = Frozen(Color.FromRgb(0x10, 0x10, 0x12)), BorderThickness = new Thickness(0), Padding = new Thickness(4) };
        tip.Opened += (_, _) =>
        {
            if (tip.Content != null) return;
            var big = ClipboardService.LoadImage(item, 0);
            tip.Content = big == null ? "Không mở được ảnh" : new StackPanel
            {
                Children =
                {
                    new Image { Source = big, MaxWidth = 440, MaxHeight = 300, Stretch = Stretch.Uniform },
                    new TextBlock
                    {
                        Text = $"{(int)big.PixelWidth}×{(int)big.PixelHeight} · {ClipMeta(item)} · bấm để chép",
                        Foreground = Frozen(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), FontSize = 11, Margin = new Thickness(2, 4, 2, 0)
                    }
                }
            };
        };
        tile.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(tile, 350);

        tile.MouseEnter += (_, _) => { tile.BorderBrush = accent; buttons.Visibility = Visibility.Visible; };
        tile.MouseLeave += (_, _) => { tile.BorderBrush = newest ? accent : rest; buttons.Visibility = Visibility.Collapsed; };
        tile.MouseLeftButtonUp += (_, _) => { if (!buttons.IsMouseOver && !_dragDone) CopyClip(item); _dragDone = false; };
        MakeDraggable(tile, item);
        return tile;
    }

    /// <summary>Pin / unpin and delete, shown on hover.</summary>
    private StackPanel MakeClipButtons(ClipItem item)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var pin = new Button
        {
            Content = item.Pinned ? "" : "", Style = (Style)FindResource("QuickButton"),
            Width = 24, Height = 24, FontSize = 10.5, Margin = new Thickness(0),
            ToolTip = item.Pinned ? "Bỏ ghim" : "Ghim (giữ lại, không bị xoá)"
        };
        pin.Click += (_, e) => { e.Handled = true; _clips.TogglePin(item); };
        var del = new Button
        {
            Content = "", Style = (Style)FindResource("QuickButton"),
            Width = 24, Height = 24, FontSize = 10, Margin = new Thickness(0), ToolTip = "Xoá"
        };
        del.Click += (_, e) => { e.Handled = true; _clips.Delete(item); };
        panel.Children.Add(pin);
        panel.Children.Add(del);
        return panel;
    }

    /// <summary>Puts the item back on the clipboard; the panel stays open.</summary>
    private void CopyClip(ClipItem item)
    {
        bool ok = _clips.CopyBack(item);     // → Changed → rebuilt, this item now on top
        _justCopied = ok ? item.Id : null;
        var toast = item.IsImage ? ImgToast : ClipToast;
        toast.Text = ok ? "✓ Đã chép · Ctrl + V để dán" : "Không chép được, thử lại nhé";
        toast.Foreground = ok ? Frozen(Color.FromRgb(0x34, 0xC7, 0x59)) : Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        RebuildClipboard();
        ClipTextScroll.ScrollToTop();
        ClipImageScroll.ScrollToLeftEnd();
        _clipToastTimer.Stop();
        _clipToastTimer.Tick -= ClipToastDone;
        _clipToastTimer.Tick += ClipToastDone;
        _clipToastTimer.Start();
    }

    private void ClipToastDone(object? sender, EventArgs e)
    {
        _clipToastTimer.Stop();
        _justCopied = null;
        ClipToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
        ImgToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
        RebuildClipboard();
    }

    private void ClipClear_Click(object sender, RoutedEventArgs e) => _clips.ClearUnpinned(images: false);
    private void ImgClear_Click(object sender, RoutedEventArgs e)  => _clips.ClearUnpinned(images: true);

    private void ClipImagesLeft_Click(object sender, RoutedEventArgs e)  => ScrollImages(-1);
    private void ClipImagesRight_Click(object sender, RoutedEventArgs e) => ScrollImages(+1);

    private void ScrollImages(int dir)
    {
        double target = Math.Clamp(ClipImageScroll.HorizontalOffset + dir * 316, 0, ClipImageScroll.ScrollableWidth);
        ClipImageScroll.ScrollToHorizontalOffset(target);
    }

    // Search box
    private void ClipSearch_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_typing) return;
        BeginTyping(ClipSearch);
        e.Handled = true;
    }

    private void ClipSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClipSearch.Text = "";
            EndTyping(collapse: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Enter copies the first match
            var q = ClipSearch.Text.Trim();
            var first = _clips.Items.FirstOrDefault(i => !i.IsImage && (q.Length == 0 || (i.Text ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase)));
            if (first != null) CopyClip(first);
            e.Handled = true;
        }
    }

    private void ClipSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ClipSearchPlaceholder.Visibility = ClipSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildClipboard();
    }

    // ── Mây the cat ───────────────────────────────────────────────────
    private IEnumerable<MayCat> Cats => new[] { CatPanel };

    /// <summary>A short reaction: happy hops, or sad after giving up.</summary>
    /// <summary>Tray → Hiện mèo: show or hide her everywhere.</summary>
    public void ApplyCatVisibility()
    {
        bool on = AppSettings.Current.ShowCat;
        if (!on) EndWalk();
        CatPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!_expanded && VolumeView.Visibility != Visibility.Visible && HeadsetView.Visibility != Visibility.Visible)
            CatRest.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        UpdateCat();
    }

    /// <summary>A notification came in: Mây looks up curiously for a few seconds (unless she's celebrating).</summary>
    public void CatCurious()
    {
        if (_inCall || (_catOverride == CatMood.Excited && DateTime.Now < _catOverrideUntil)) return;
        CatReact(CatMood.Curious, 4);
    }

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
            if (_inCall) { mood = CatMood.Still; goto apply; }
            int hour = DateTime.Now.Hour;
            bool idle = NativeInterop.IdleTime() >= TimeSpan.FromMinutes(10);
            mood = _deadlines?.Pressing == true && !idle ? CatMood.Worried           // a deadline within the hour / exam today
                 : _pomo?.OnBreak == true ? CatMood.Stretch                          // Pomodoro rest: stretches
                 : _focus.IsRunning ? (_pomo?.Studying == true ? CatMood.Study       // Pomodoro round: reads
                                                              : CatMood.Focus)       // sits by the tree
                 : _hasMusic && _playing ? CatMood.Dance                             // dances to the music
                 : idle ? CatMood.Sleep
                 : hour >= 23 || hour < 4 ? CatMood.Yawn                             // still up late
                 : CatMood.Idle;
        }

        apply:
        // Every tree you grow is a meal; Mây grows a little with each one.
        int food = _focus.TotalTrees;
        double growth = 1 + Math.Min(0.3, food * 0.015);
        foreach (var cat in Cats)
        {
            cat.Mood   = mood;
            cat.Growth = growth;
        }
        CatRest.Mood = mood;

        string doing = mood switch
        {
            CatMood.Dance => "đang nhảy theo nhạc",
            CatMood.Focus => "đang ngồi canh cây",
            CatMood.Happy => "đang vui lắm",
            CatMood.Sad   => "hơi buồn vì cây chết",
            CatMood.Sleep => "đang ngủ",
            CatMood.Excited => "phấn khích quá trời",
            CatMood.Curious => "tò mò xem ai nhắn",
            CatMood.Worried => "lo lắng vì sắp tới hạn",
            CatMood.Study   => "đang học cùng bạn",
            CatMood.Stretch => "đang vươn vai giờ nghỉ",
            CatMood.Yawn    => "buồn ngủ rồi, khuya lắm rồi đó",
            CatMood.Still   => "ngồi yên cho bạn gọi video",
            _             => "đang ngồi chơi"
        };
        CatPanel.ToolTip = $"Mây {doing} · đã được ăn {food} phần (mỗi cây trồng xong là 1 phần)";
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
