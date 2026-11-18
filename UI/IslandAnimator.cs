namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

/// <summary>
/// Encapsulates all WPF animations for the Dynamic Island pill:
/// slide, resize, fade, corner-radius interpolation, expand/collapse orchestration.
/// </summary>
public sealed class IslandAnimator
{
    // ── Size Constants ─────────────────────────────────────────────────
    public const double PILL_W_OPEN      = 520;
    public const double PILL_H_OPEN      = 64;
    public const double PILL_H_OPEN_BODY = 84;
    public const double PILL_H_HOVER     = 120;

    private readonly Border _pill;
    private readonly TranslateTransform _pillTransform;
    private readonly UIElement _contentRoot;
    private readonly UIElement _hoverPanel;

    // Bumped by every Expand/Collapse. Each step of a chained animation checks
    // it, so a hide that's still running stops as soon as a new notification
    // starts showing (otherwise it would slide the new one away mid-way).
    private int _generation;

    public IslandAnimator(Border pill, TranslateTransform pillTransform,
                          UIElement contentRoot, UIElement hoverPanel)
    {
        _pill          = pill;
        _pillTransform = pillTransform;
        _contentRoot   = contentRoot;
        _hoverPanel    = hoverPanel;

        // Dynamic corner-radius: morphs from pill → rounded box on hover expansion
        _pill.SizeChanged += (_, _) =>
        {
            double h = _pill.ActualHeight;
            double r = h / 2;
            if (h > 84)
            {
                double progress = Math.Min(1.0, (h - 84) / (120.0 - 84.0));
                r = 42.0 - (progress * (42.0 - 24.0));
            }
            _pill.CornerRadius = new CornerRadius(r);
        };
    }

    // ── Expand / Collapse Orchestration ───────────────────────────────
    public void Expand(bool hasBody, Action onFullyOpen)
    {
        int gen = ++_generation;

        // Drop whatever animation still holds these values (e.g. a hide that was
        // interrupted), then start from the small hidden circle.
        // iPhone-style: the notification grows straight out of the resting capsule
        // (same place, same size), springing a little past its size and settling.
        StopAll();
        _pill.Width  = REST_W;
        _pill.Height = REST_H;
        _pill.Opacity = 1;
        _hoverPanel.Opacity    = 0;
        _hoverPanel.Visibility = Visibility.Collapsed;
        _contentRoot.Opacity   = 0;
        _pillTransform.Y       = 0;

        double targetH = hasBody ? PILL_H_OPEN_BODY : PILL_H_OPEN;
        SizePill(PILL_W_OPEN, targetH, 520,
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
            onDone: () => { if (gen == _generation) onFullyOpen(); });
        FadeIn(_contentRoot, 220, delay: 160);
    }

    public void Collapse(Action onHidden)
    {
        int gen = ++_generation;
        FadeOut(_contentRoot, 140, () =>
        {
            if (gen != _generation) return;
            // Shrinks back into the capsule, which then hands over to the resting notch
            SizePill(REST_W, REST_H, 340, new CubicEase { EasingMode = EasingMode.EaseInOut }, onDone: () =>
            {
                if (gen != _generation) return;
                FadeOut(_pill, 120, () => { if (gen == _generation) onHidden(); });
            });
        });
    }

    /// <summary>The resting capsule's size on the notch (where notifications grow from).</summary>
    private const double REST_W = 124, REST_H = 32;

    private void StopAll()
    {
        _pill.BeginAnimation(FrameworkElement.WidthProperty,  null);
        _pill.BeginAnimation(FrameworkElement.HeightProperty, null);
        _pill.BeginAnimation(UIElement.OpacityProperty, null);
        _pillTransform.BeginAnimation(TranslateTransform.YProperty, null);
        _contentRoot.BeginAnimation(UIElement.OpacityProperty, null);
        _hoverPanel.BeginAnimation(UIElement.OpacityProperty, null);
    }

    public void ExpandToHover(double targetH = PILL_H_HOVER)
    {
        SizePill(PILL_W_OPEN, Math.Max(PILL_H_HOVER, targetH), 250,
            new CubicEase { EasingMode = EasingMode.EaseOut });
        FadeIn(_hoverPanel, 200, delay: 100);
    }

    public void CollapseFromHover(bool hasBody)
    {
        FadeOut(_hoverPanel, 150);
        double targetH = hasBody ? PILL_H_OPEN_BODY : PILL_H_OPEN;
        SizePill(PILL_W_OPEN, targetH, 250,
            new CubicEase { EasingMode = EasingMode.EaseIn });
    }

    // ── Primitives ────────────────────────────────────────────────────
    public void SlideY(double from, double to, int ms, IEasingFunction ease, Action? onDone = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        if (onDone != null) anim.Completed += (_, _) => onDone();   // already on the UI thread
        _pillTransform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    public void SizePill(double toW, double toH, int ms, IEasingFunction ease, Action? onDone = null)
    {
        var dur   = TimeSpan.FromMilliseconds(ms);
        var wAnim = new DoubleAnimation(_pill.Width,  toW, dur) { EasingFunction = ease };
        var hAnim = new DoubleAnimation(_pill.Height, toH, dur) { EasingFunction = ease };
        if (onDone != null) hAnim.Completed += (_, _) => onDone();
        _pill.BeginAnimation(FrameworkElement.WidthProperty,  wAnim);
        _pill.BeginAnimation(FrameworkElement.HeightProperty, hAnim);
    }

    public static void FadeIn(UIElement el, int ms, int delay = 0)
    {
        el.Visibility = Visibility.Visible;
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
            { BeginTime = TimeSpan.FromMilliseconds(delay) };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    public void FadeOut(UIElement el, int ms, Action? done = null)
    {
        var a = new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(ms));
        a.Completed += (_, _) => { el.Visibility = Visibility.Collapsed; done?.Invoke(); };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }
}
