using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.UI;

namespace DynamicIslandNotifier;

/// <summary>
/// The Dynamic Island overlay window. Receives notification data and manages
/// the pill UI state. Animation logic is delegated to <see cref="IslandAnimator"/>.
/// </summary>
public partial class IslandWindow : Window
{
    // ── Sizes ──────────────────────────────────────────────────────────
    // The window is tall enough for the hover-expanded pill. Its transparent
    // area doesn't take mouse input, so clicks pass through to what's below.
    const double WIN_W = 560, WIN_H = 470;

    // Hover-expanded layout
    const double HOVER_ACTIONS_H = 48;   // space for the Mute / Clear row
    const double HOVER_MAX_H     = 440;  // tallest the pill may grow

    private IslandAnimator _animator = null!;
    private DispatcherTimer? _hideTimer;
    private bool _isExpanded;
    private bool _isFullyOpen;
    private string? _currentAppId;

    public IslandWindow()
    {
        InitializeComponent();
        Width  = WIN_W;
        Height = WIN_H;

        Loaded += (_, _) =>
        {
            // Initialise animator after XAML elements are ready
            _animator = new IslandAnimator(PillBorder, PillTransform, ContentRoot, HoverActionsPanel);

            CentreWindow();
            NativeInterop.MakeOverlay(this);
            Visibility = Visibility.Hidden;
        };
    }

    // ── Public API ─────────────────────────────────────────────────────
    public void ShowNotification(NotificationData data)
    {
        Dispatcher.Invoke(() =>
        {
            _currentAppId    = data.AppUserModelId;
            SetFullTextLayout(false);
            TitleText.Text   = data.Title;
            TimeText.Text    = "now";

            bool hasBody = !string.IsNullOrWhiteSpace(data.Body);
            BodyText.Text       = data.Body;
            BodyText.Visibility = hasBody ? Visibility.Visible : Visibility.Collapsed;

            // Avatar
            if (data.Icon != null)
            {
                AppIcon.Source     = data.Icon;
                AppIcon.Visibility = Visibility.Visible;
                AvatarFallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                AppIcon.Visibility        = Visibility.Collapsed;
                AvatarFallback.Visibility = Visibility.Visible;
                AvatarInitial.Text        = data.AppName.Length > 0
                    ? data.AppName[0].ToString().ToUpperInvariant() : "?";
                AvatarFallback.Background = AccentBrush(data.AppName);
            }

            _hideTimer?.Stop();
            Visibility = Visibility.Visible;
            NativeInterop.ForceTopMost(this);

            if (!_isExpanded)
            {
                Expand(hasBody);
            }
            else if (_isFullyOpen)
            {
                // Already showing something: swap content in place and
                // resize to fit, then restart the auto-hide countdown.
                if (PillBorder.IsMouseOver) TriggerHover();
                else
                {
                    _animator.CollapseFromHover(hasBody);
                    StartHideTimer();
                }
            }
        });
    }

    // ── Animation Orchestration ───────────────────────────────────────
    private void Expand(bool hasBody)
    {
        if (_isExpanded) return;
        _isExpanded  = true;
        _isFullyOpen = false;

        _animator.Expand(hasBody, onFullyOpen: () =>
        {
            _isFullyOpen = true;
            if (!PillBorder.IsMouseOver) StartHideTimer();
            else TriggerHover();
        });
    }

    private void Collapse()
    {
        if (!_isExpanded) return;
        _isExpanded  = false;
        _isFullyOpen = false;
        _hideTimer?.Stop();

        _animator.Collapse(onHidden: () => Visibility = Visibility.Hidden);
    }

    private void StartHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Collapse(); };
        _hideTimer.Start();
    }

    // ── Interaction ───────────────────────────────────────────────────
    private void PillBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullyOpen) return;
        TriggerHover();
    }

    private void TriggerHover()
    {
        _hideTimer?.Stop();
        SetFullTextLayout(true);
        _animator.ExpandToHover(MeasureFullTextHeight());
    }

    private void PillBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullyOpen) return;
        SetFullTextLayout(false);
        _animator.CollapseFromHover(BodyText.Visibility == Visibility.Visible);
        StartHideTimer();
    }

    /// <summary>
    /// true  = show the whole title and message, wrapped, above the action row.
    /// false = the compact one-line-each layout.
    /// </summary>
    private void SetFullTextLayout(bool full)
    {
        var wrap = full ? TextWrapping.Wrap : TextWrapping.NoWrap;
        TitleText.TextWrapping = wrap;
        BodyText.TextWrapping  = wrap;

        // Keep a very long message inside the pill (trimmed with "…" at the end).
        BodyText.MaxHeight = full ? HOVER_MAX_H - HOVER_ACTIONS_H - 80 : double.PositiveInfinity;

        TextStack.VerticalAlignment = full ? VerticalAlignment.Top : VerticalAlignment.Center;
        ContentRoot.Margin = new Thickness(68, 0, 0, full ? HOVER_ACTIONS_H : 0);
    }

    /// <summary>Pill height needed to show the full text plus the action row.</summary>
    private double MeasureFullTextHeight()
    {
        double timeW = TimeText.ActualWidth + TimeText.Margin.Left + TimeText.Margin.Right;
        double textW = IslandAnimator.PILL_W_OPEN - ContentRoot.Margin.Left - timeW;

        // DesiredSize includes the StackPanel's own top/bottom margin.
        TextStack.Measure(new Size(Math.Max(100, textW), double.PositiveInfinity));
        double needed = TextStack.DesiredSize.Height + HOVER_ACTIONS_H;

        return Math.Clamp(needed, IslandAnimator.PILL_H_HOVER, HOVER_MAX_H);
    }

    private void PillBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isFullyOpen || string.IsNullOrEmpty(_currentAppId)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName       = "explorer.exe",
                Arguments      = $"shell:appsFolder\\{_currentAppId}",
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
        Collapse();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e) => Collapse();
    private void MuteBtn_Click(object sender, RoutedEventArgs e)  { /* Placeholder */ }

    // ── Helpers ────────────────────────────────────────────────────────
    private void CentreWindow()
    {
        Left = (SystemParameters.PrimaryScreenWidth - WIN_W) / 2;
        Top  = 8;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeInterop.MakeOverlay(this);
    }

    static SolidColorBrush AccentBrush(string name)
    {
        string[] palette =
        [
            "#1A6B3C", "#0060A8", "#B94920", "#7B3FA0",
            "#A02020", "#136E6E", "#1A5FA0", "#C06010"
        ];
        int i = (int)((uint)name.GetHashCode() % palette.Length);
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
    }
}
