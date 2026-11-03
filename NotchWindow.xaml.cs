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

    private static readonly Color DndColour = Color.FromRgb(0x8E, 0x7C, 0xFF);
    private static readonly string[] ShortDays = { "CN", "T2", "T3", "T4", "T5", "T6", "T7" };

    private static readonly string[] Weekdays =
        { "Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy" };

    /// <summary>"Mute this app" from a notification card.</summary>
    public event Action<NotificationData>? MuteRequested;

    private static readonly string[] Tabs     = { "music", "focus", "notifications", "texts", "images" };
    private static readonly string[] TabNames = { "Nhạc", "Trồng cây", "Thông báo", "Văn bản đã copy", "Hình ảnh đã copy" };
    private static readonly string[] TabGlyphs = { "\uE8D6", "", "\uEA8F", "\uE8A5", "\uEB9F" };   // tree is drawn

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

        BuildSideBar();

        _ready = true;
        _clips.Start(this);   // creates the window handle too
        // Two-finger sideways swipe / tilt wheel → previous / next tab
        System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(SidewaysHook);
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

    /// <summary>Mây peeks out from behind the right end of the strip / pill.</summary>
    private void PlaceCat()
    {
        double half = RestArea.ActualWidth > 0 ? RestArea.ActualWidth / 2 : 100;
        // Her head is half tucked behind the notch's right end.
        CatRestShift.X = half - MayPeek.HiddenPart + CatRest.Width / 2;
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
        NativeInterop.SetClickThrough(this, true);
        if (ClipSearch.Text.Length > 0) ClipSearch.Text = "";   // clicks go through right away

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
                Width = 34, Height = 24, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = content, ToolTip = TabNames[i]
            };
            var tab = Tabs[i];
            int index = i;
            button.MouseEnter += (_, _) => { if (_tab != tab) button.Background = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)); };
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
            button.Background = on ? Frozen(Color.FromArgb(0x30, accent.R, accent.G, accent.B)) : Brushes.Transparent;
            if (icon is TextBlock t) t.Foreground = on ? Frozen(accent) : Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            else icon.Opacity = on ? 1 : 0.6;
            mark.Background = Frozen(accent);
            mark.Opacity = on ? 1 : 0;
        }
    }

    private const int WM_MOUSEHWHEEL = 0x020E;
    private int _sideways;
    private DateTime _lastSwipe;

    /// <summary>A sideways swipe on the touchpad (or a tilt wheel) flips tabs, one per gesture.</summary>
    private IntPtr SidewaysHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
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
        bool animate = _tab != tab && Expanded.IsVisible && Expanded.Opacity > 0.9;
        bool animateWidth = animate;
        _tab = tab;
        ContentCard.BeginAnimation(HeightProperty, null);
        double fromHeight = ContentCard.ActualHeight;
        MusicTab.Visibility         = tab == "music"         ? Visibility.Visible : Visibility.Collapsed;
        FocusTab.Visibility         = tab == "focus"         ? Visibility.Visible : Visibility.Collapsed;
        NotificationsTab.Visibility = tab == "notifications" ? Visibility.Visible : Visibility.Collapsed;
        TextsTab.Visibility         = tab == "texts"         ? Visibility.Visible : Visibility.Collapsed;
        ImagesTab.Visibility        = tab == "images"        ? Visibility.Visible : Visibility.Collapsed;
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
                if (_showWeek) BuildWeekChart();
                break;
            case "notifications":
                RebuildRecent();
                break;
            case "texts":
            case "images":
                RebuildClipboard();
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
        if (_focus.IsRunning) UpdateFocusTick();
        if (Pill.IsVisible) UpdatePillProgress();
        UpdateCat();
        if (_expanded)
        {
            UpdateClock();
            if (_hasMusic && MusicTab.IsVisible) UpdateProgress();
            if (TextsTab.IsVisible || ImagesTab.IsVisible) RefreshClipTimes();

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
        if (_ready) UpdateSideBar();
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
            Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
            Background = on ? Frozen(Color.FromArgb(0x40, colour.R, colour.G, colour.B)) : Frozen(Color.FromRgb(0x2A, 0x2A, 0x2E)),
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

        var normal = Frozen(Color.FromRgb(0x28, 0x28, 0x2C));
        var hover  = Frozen(Color.FromRgb(0x33, 0x33, 0x38));
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
    private readonly List<(TextBlock Label, ClipItem Item)> _clipTimes = new();
    private readonly DispatcherTimer _clipToastTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Guid? _justCopied;

    private void RebuildClipboard()
    {
        if (!_expanded || (_tab != "texts" && _tab != "images")) return;
        _clipTimes.Clear();
        var all = _clips.Items;                      // pinned first, then newest
        if (_tab == "texts") RebuildTexts(all);
        else RebuildImages(all);
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

        var normal = Frozen(Color.FromRgb(0x28, 0x28, 0x2C));
        var hover  = Frozen(Color.FromRgb(0x33, 0x33, 0x38));
        var a = _hasColour ? _accent : Colors.White;
        var row = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 6, 7),
            Margin = new Thickness(0, 0, 0, 5), Background = normal, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = newest ? Frozen(Color.FromArgb(0x99, a.R, a.G, a.B)) : Brushes.Transparent
        };
        row.MouseEnter += (_, _) => { row.Background = hover;  buttons.Visibility = Visibility.Visible; };
        row.MouseLeave += (_, _) => { row.Background = normal; buttons.Visibility = Visibility.Hidden; };
        row.MouseLeftButtonUp += (_, _) => { if (!buttons.IsMouseOver) CopyClip(item); };
        return row;
    }

    private UIElement MakeClipImageTile(ClipItem item, bool newest)
    {
        if (!_thumbs.TryGetValue(item.Id, out var thumb))
            _thumbs[item.Id] = thumb = ClipboardService.LoadImage(item, 190);

        const double W = 150, H = 94;
        var box = new Grid { Width = W, Height = H, Clip = new RectangleGeometry(new Rect(0, 0, W, H), 10, 10) };
        box.Children.Add(new Border { Background = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E)) });
        if (thumb != null) box.Children.Add(new Image { Source = thumb, Stretch = Stretch.UniformToFill });

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
        CatRest.Mood = mood;

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
