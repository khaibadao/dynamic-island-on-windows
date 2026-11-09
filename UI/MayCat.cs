namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

/// <summary>What Mây is doing.</summary>
public enum CatMood
{
    Idle, Dance, Focus, Happy, Sad, Sleep,
    /// <summary>A new tree unlocked / a Pomodoro cycle done: hops, sparkles.</summary>
    Excited,
    /// <summary>A notification just came in: ears up, head tilted, a "?".</summary>
    Curious,
    /// <summary>A deadline within the hour, or an exam today: ears back, a drop of sweat.</summary>
    Worried,
    /// <summary>A Pomodoro study round: reads a little book.</summary>
    Study,
    /// <summary>A Pomodoro rest: stretches.</summary>
    Stretch,
    /// <summary>Late at night and still at the PC: droopy eyes, a yawn now and then.</summary>
    Yawn
}

/// <summary>Shapes and colours shared by both drawings of Mây.</summary>
internal static class CatParts
{
    public static readonly Brush Fur    = Solid(0xF5, 0xA6, 0x23);
    public static readonly Brush Tail   = Solid(0xF0, 0xA0, 0x30);
    public static readonly Brush Line   = Solid(0xD9, 0x82, 0x2B);
    public static readonly Brush Cream  = Solid(0xFF, 0xE7, 0xB3);
    public static readonly Brush Paw    = Solid(0xFF, 0xF1, 0xD2);
    public static readonly Brush Pink   = Solid(0xFF, 0x9F, 0xB0);
    public static readonly Brush Nose   = Solid(0xFF, 0x7F, 0x95);
    public static readonly Brush Eye    = Solid(0x2A, 0x1A, 0x10);
    public static readonly Brush Mouth  = Solid(0x8A, 0x50, 0x20);
    public static readonly Brush Blush  = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0x8F, 0xA3));
    public static readonly Brush Tear   = Solid(0x5A, 0xC8, 0xFA);

    static CatParts() => Blush.Freeze();

    public static Path P(string data, Brush? fill, Brush? stroke = null, double thickness = 0.6)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        return path;
    }

    public static Ellipse E(double cx, double cy, double rx, double ry, Brush fill, Brush? stroke = null, double thickness = 0.6)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return e;
    }

    /// <summary>The head (ears, face, eyes) in a 40×40 space; the eye parts are returned for animating.</summary>
    public static Canvas Head(out Canvas openEyes, out Path happyEyes, out Path closedEyes,
                              out RotateTransform leftEar, out RotateTransform rightEar, bool details)
    {
        var head = new Canvas { Width = 40, Height = 40 };

        var le = P("M10.2,11.5 L10.8,2.2 L17.5,7.2 Z", Fur, Line);
        var re = P("M29.8,11.5 L29.2,2.2 L22.5,7.2 Z", Fur, Line);
        var lei = P("M11.8,9 L12.1,4.6 L15.3,7.2 Z", Pink);
        var rei = P("M28.2,9 L27.9,4.6 L24.7,7.2 Z", Pink);
        leftEar  = new RotateTransform(0, 14, 9);
        rightEar = new RotateTransform(0, 26, 9);
        le.RenderTransform = lei.RenderTransform = leftEar;
        re.RenderTransform = rei.RenderTransform = rightEar;
        head.Children.Add(le);  head.Children.Add(re);
        head.Children.Add(lei); head.Children.Add(rei);

        head.Children.Add(E(20, 15, 11, 9.3, Fur, Line));
        if (details) head.Children.Add(P("M18.6,6.2 L18.9,9 M20,5.8 L20,9.2 M21.4,6.2 L21.1,9", null, Line, 0.9));
        head.Children.Add(E(20, 18.8, 4.8, 3.3, Cream));
        if (details)
        {
            head.Children.Add(E(12.8, 18, 1.8, 1.1, Blush));
            head.Children.Add(E(27.2, 18, 1.8, 1.1, Blush));
        }

        openEyes = new Canvas { RenderTransform = new ScaleTransform(1, 1, 20, 14.4) };
        openEyes.Children.Add(E(15.6, 14.4, 1.6, 2.1, Eye));
        openEyes.Children.Add(E(24.4, 14.4, 1.6, 2.1, Eye));
        if (details)
        {
            openEyes.Children.Add(E(16.1, 13.6, 0.5, 0.5, Brushes.White));
            openEyes.Children.Add(E(24.9, 13.6, 0.5, 0.5, Brushes.White));
        }
        head.Children.Add(openEyes);

        happyEyes  = P("M14,15 Q15.6,12.4 17.2,15 M22.8,15 Q24.4,12.4 26,15", null, Eye, 1.2);
        closedEyes = P("M14,14.6 Q15.6,16.2 17.2,14.6 M22.8,14.6 Q24.4,16.2 26,14.6", null, Eye, 1.1);
        happyEyes.Visibility = closedEyes.Visibility = Visibility.Collapsed;
        head.Children.Add(happyEyes);
        head.Children.Add(closedEyes);

        head.Children.Add(P("M19,17.2 L21,17.2 L20,18.3 Z", Nose, Nose, 0.4));
        if (details) head.Children.Add(P("M20,18.3 Q19,19.8 17.9,19.2 M20,18.3 Q21,19.8 22.1,19.2", null, Mouth, 0.5));
        return head;
    }

    public static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static DoubleAnimation Swing(double from, double to, double ms) =>
        new(from, to, TimeSpan.FromMilliseconds(ms))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
}

/// <summary>
/// Mây, the orange cat, drawn with smooth shapes (not pixels) so she matches
/// the rest of the notch. Sits, blinks and swishes her tail; dances to music,
/// hops when happy, droops her ears when sad, and curls up to sleep.
/// </summary>
public sealed class MayCat : Canvas
{
    private readonly Canvas _root = new() { Width = 40, Height = 40 };
    private readonly RotateTransform _sway = new(0, 20, 40);
    private readonly TranslateTransform _hop = new();
    private readonly ScaleTransform _breathe = new(1, 1, 20, 40);
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _tail = new(0, 29, 35);
    private readonly RotateTransform _headTilt = new(0, 20, 22);
    private readonly RotateTransform _leftEar, _rightEar;
    private readonly Canvas _openEyes;
    private readonly Path _happyEyes, _closedEyes;
    private readonly Path _tear;
    private readonly TextBlock _zzz;
    private readonly Path _sweat;
    private readonly Ellipse _yawnMouth;
    private readonly ScaleTransform _yawn = new(1, 0, 20, 18.6);
    private readonly ScaleTransform _stretch = new(1, 1, 20, 38);
    private readonly TextBlock _question;
    private readonly Canvas _sparkles = new() { Visibility = Visibility.Collapsed };
    private readonly Canvas _book;
    private readonly Canvas _sitPaws = new();
    private readonly Canvas _wideP = new() { Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    private CatMood _mood = (CatMood)(-1);
    private double _size = 28, _growth = 1;
    private readonly Canvas _belly;
    private readonly Path _heart;
    private bool _bellyUp;

    public MayCat()
    {
        ClipToBounds = false;
        var group = new TransformGroup();
        group.Children.Add(_breathe);
        group.Children.Add(_sway);
        group.Children.Add(_hop);
        group.Children.Add(_scale);
        _root.RenderTransform = group;
        Children.Add(_root);

        var tail = CatParts.P("M29,35 C37,34 38,25 34.5,21.5", null, CatParts.Tail, 3.2);
        tail.RenderTransform = _tail;
        _root.Children.Add(tail);
        var body = new Canvas { RenderTransform = _stretch };
        body.Children.Add(CatParts.P("M9.5,38.5 C8,30 12,23.5 20,23.5 C28,23.5 32,30 30.5,38.5 Z", CatParts.Fur, CatParts.Line));
        body.Children.Add(CatParts.E(20, 32.5, 5.2, 5.6, CatParts.Cream));
        _sitPaws.Children.Add(CatParts.E(15.5, 38.3, 3, 1.8, CatParts.Paw, CatParts.Line, 0.5));
        _sitPaws.Children.Add(CatParts.E(24.5, 38.3, 3, 1.8, CatParts.Paw, CatParts.Line, 0.5));
        body.Children.Add(_sitPaws);
        // Stretching: front paws reach out to both sides
        _wideP.Children.Add(CatParts.E(7, 38.4, 3.4, 1.7, CatParts.Paw, CatParts.Line, 0.5));
        _wideP.Children.Add(CatParts.E(33, 38.4, 3.4, 1.7, CatParts.Paw, CatParts.Line, 0.5));
        body.Children.Add(_wideP);
        _root.Children.Add(body);

        var head = CatParts.Head(out _openEyes, out _happyEyes, out _closedEyes, out _leftEar, out _rightEar, details: true);
        head.RenderTransform = _headTilt;
        // Worried: a drop of sweat on the side of the head. Yawning: an open mouth.
        _sweat = CatParts.P("M30.2,8.5 Q29,11 30.2,12 Q31.4,11 30.2,8.5 Z", CatParts.Tear);
        _sweat.RenderTransform = new TranslateTransform();
        _sweat.Visibility = Visibility.Collapsed;
        head.Children.Add(_sweat);
        _yawnMouth = CatParts.E(20, 20.2, 1.5, 1.9, CatParts.Solid(0x7A, 0x2F, 0x2F));
        _yawnMouth.RenderTransform = _yawn;
        _yawnMouth.Visibility = Visibility.Collapsed;
        head.Children.Add(_yawnMouth);
        _root.Children.Add(head);

        // Studying: a small open book held in both paws
        _book = new Canvas { Visibility = Visibility.Collapsed };
        _book.Children.Add(CatParts.P("M12.5,29.5 L20,31 L27.5,29.5 L27.5,37 L20,38.4 L12.5,37 Z", Brushes.White, CatParts.Solid(0xC9, 0xC2, 0xB6), 0.5));
        _book.Children.Add(CatParts.P("M20,31 L20,38.4 M14,31.6 L18.5,32.4 M14,33.3 L18.5,34.1 M21.5,32.4 L26,31.6 M21.5,34.1 L26,33.3", null, CatParts.Solid(0xD0, 0xC9, 0xBE), 0.45));
        _book.Children.Add(CatParts.P("M13,36.6 L20,38 L20,39.2 L13,37.8 Z M27,36.6 L20,38 L20,39.2 L27,37.8 Z", CatParts.Solid(0x5A, 0x7B, 0xD8)));
        _book.Children.Add(CatParts.E(12.6, 35, 2.2, 1.6, CatParts.Paw, CatParts.Line, 0.5));
        _book.Children.Add(CatParts.E(27.4, 35, 2.2, 1.6, CatParts.Paw, CatParts.Line, 0.5));
        _root.Children.Add(_book);

        // Excited: little gold stars around her
        foreach (var (x, y, delay) in new[] { (-3.0, 4.0, 0.0), (37.0, 0.0, 0.35), (2.0, -6.0, 0.7), (40.0, 18.0, 1.05) })
        {
            var star = CatParts.P(FormattableString.Invariant(
                $"M{x},{y - 2.6} L{x + 0.8},{y - 0.8} L{x + 2.6},{y} L{x + 0.8},{y + 0.8} L{x},{y + 2.6} L{x - 0.8},{y + 0.8} L{x - 2.6},{y} L{x - 0.8},{y - 0.8} Z"),
                CatParts.Solid(0xFF, 0xD6, 0x0A));
            star.Tag = delay;
            _sparkles.Children.Add(star);
        }
        _root.Children.Add(_sparkles);

        _tear = CatParts.P("M26.4,17 Q25.4,19 26.4,19.8 Q27.4,19 26.4,17 Z", CatParts.Tear);
        _tear.Visibility = Visibility.Collapsed;
        _root.Children.Add(_tear);

        _zzz = new TextBlock
        {
            Text = "z", FontSize = 10, FontWeight = FontWeights.Bold, Visibility = Visibility.Collapsed,
            Foreground = CatParts.Solid(0xCC, 0xCC, 0xFF), RenderTransform = new TranslateTransform()
        };
        Children.Add(_zzz);

        _question = new TextBlock
        {
            Text = "?", FontSize = 10, FontWeight = FontWeights.ExtraBold, Visibility = Visibility.Collapsed,
            Foreground = Brushes.White, RenderTransform = new TranslateTransform()
        };
        Children.Add(_question);

        _belly = CatBelly.Build(out _heart);
        _belly.Visibility = Visibility.Collapsed;
        var bg = new TransformGroup();
        bg.Children.Add(_scale);
        _belly.RenderTransform = bg;
        Children.Add(_belly);

        _blinkTimer.Tick += (_, _) => Blink(_openEyes);
        ApplySize();
        Mood = CatMood.Idle;
    }

    /// <summary>Rolls onto her back, paws up, a heart floating (when the pointer comes close).</summary>
    public bool BellyUp
    {
        get => _bellyUp;
        set
        {
            if (value == _bellyUp) return;
            _bellyUp = value;
            _root.Visibility  = value ? Visibility.Collapsed : Visibility.Visible;
            _belly.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _zzz.Visibility   = !value && _mood == CatMood.Sleep ? Visibility.Visible : Visibility.Collapsed;
            var t = (TranslateTransform)_heart.RenderTransform;
            if (value)
            {
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, -6, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
                _heart.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                t.BeginAnimation(TranslateTransform.YProperty, null);
                _heart.BeginAnimation(OpacityProperty, null);
            }
        }
    }

    /// <summary>Height in px at growth 1.</summary>
    public double Size
    {
        get => _size;
        set { _size = value; ApplySize(); }
    }

    /// <summary>1.0 = kitten; grows to 1.3 as Mây is fed.</summary>
    public double Growth
    {
        get => _growth;
        set { _growth = Math.Clamp(value, 1.0, 1.3); ApplySize(); }
    }

    private void ApplySize()
    {
        double s = _size * _growth / 40;
        _scale.ScaleX = _scale.ScaleY = s;
        Width  = 40 * s;
        Height = 40 * s;
        SetLeft(_zzz, 34 * s);
        SetTop(_zzz, -2);
        SetLeft(_question, 33 * s);
        SetTop(_question, -6);
    }

    public CatMood Mood
    {
        get => _mood;
        set
        {
            if (_mood == value) return;
            _mood = value;
            Animate();
        }
    }

    private void Animate()
    {
        foreach (var (t, p) in new (Animatable, DependencyProperty)[]
                 {
                     (_sway, RotateTransform.AngleProperty), (_tail, RotateTransform.AngleProperty),
                     (_headTilt, RotateTransform.AngleProperty), (_hop, TranslateTransform.YProperty),
                     (_breathe, ScaleTransform.ScaleYProperty)
                 })
            t.BeginAnimation(p, null);
        _sway.Angle = _tail.Angle = _headTilt.Angle = _hop.Y = 0;
        _breathe.ScaleY = 1;
        _leftEar.Angle = _rightEar.Angle = 0;
        _tear.Visibility = Visibility.Collapsed;
        _zzz.Visibility  = Visibility.Collapsed;
        _sweat.Visibility = _yawnMouth.Visibility = _question.Visibility = Visibility.Collapsed;
        _sparkles.Visibility = _book.Visibility = _wideP.Visibility = Visibility.Collapsed;
        _sitPaws.Visibility = Visibility.Visible;
        _stretch.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _stretch.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _stretch.ScaleX = _stretch.ScaleY = 1;
        _yawn.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ((TranslateTransform)_sweat.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        _sweat.BeginAnimation(OpacityProperty, null);
        ((TranslateTransform)_question.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
        foreach (UIElement star in _sparkles.Children) star.BeginAnimation(OpacityProperty, null);
        if (_openEyes.RenderTransform is ScaleTransform eyeScale) { eyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); eyeScale.ScaleY = 1; }
        _blinkTimer.Stop();
        Eyes(open: true);

        switch (_mood)
        {
            case CatMood.Dance:
                Eyes(happy: true);
                _sway.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-8, 8, 480));
                _headTilt.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(5, -5, 480));
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-14, 14, 480));
                break;

            case CatMood.Happy:
                Eyes(happy: true);
                _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -5, TimeSpan.FromMilliseconds(240))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-12, 12, 260));
                break;

            case CatMood.Sad:
                _leftEar.Angle  = -22;
                _rightEar.Angle = 22;
                _headTilt.Angle = 7;
                _tail.Angle     = 30;
                _tear.Visibility = Visibility.Visible;
                break;

            case CatMood.Sleep:
                Eyes(closed: true);
                _headTilt.Angle = -6;
                _breathe.BeginAnimation(ScaleTransform.ScaleYProperty, CatParts.Swing(1, 1.035, 1600));
                _zzz.Visibility = Visibility.Visible;
                var t = (TranslateTransform)_zzz.RenderTransform;
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(2, -7, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever });
                _zzz.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever });
                break;

            case CatMood.Excited:   // big hops, tail going fast, stars twinkling
                Eyes(happy: true);
                _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(240))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-16, 16, 130));
                _sparkles.Visibility = Visibility.Visible;
                foreach (UIElement star in _sparkles.Children)
                    star.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromSeconds(1.4), RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromSeconds((double)((FrameworkElement)star).Tag),
                        KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)), new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.4)), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) }
                    });
                break;

            case CatMood.Curious:   // ears up, head tilting, a "?" bobbing
                _leftEar.Angle  = 8;
                _rightEar.Angle = -8;
                if (_openEyes.RenderTransform is ScaleTransform wide) wide.ScaleY = 1.2;
                _headTilt.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(0, -10, 1200));
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-6, 6, 1100));
                _question.Visibility = Visibility.Visible;
                ((TranslateTransform)_question.RenderTransform).BeginAnimation(TranslateTransform.YProperty, CatParts.Swing(0, -3, 600));
                break;

            case CatMood.Worried:   // ears back, sweat dripping, tail lashing
                _leftEar.Angle  = -16;
                _rightEar.Angle = 16;
                if (_openEyes.RenderTransform is ScaleTransform big) big.ScaleY = 1.15;
                _headTilt.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-2.5, 2.5, 320));
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-16, 16, 250));
                _sweat.Visibility = Visibility.Visible;
                ((TranslateTransform)_sweat.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 5, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                _sweat.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                break;

            case CatMood.Study:     // reads a little book, eyes down, slow blinks
                _book.Visibility = Visibility.Visible;
                _sitPaws.Visibility = Visibility.Collapsed;
                if (_openEyes.RenderTransform is ScaleTransform down) down.ScaleY = 0.65;
                _headTilt.Angle = 4;
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-4, 4, 2600));
                break;

            case CatMood.Stretch:   // a long, slow stretch, eyes shut happily
                Eyes(closed: true);
                _sitPaws.Visibility = Visibility.Collapsed;
                _wideP.Visibility = Visibility.Visible;
                _stretch.BeginAnimation(ScaleTransform.ScaleXProperty, CatParts.Swing(1, 1.2, 1500));
                _stretch.BeginAnimation(ScaleTransform.ScaleYProperty, CatParts.Swing(1, 0.84, 1500));
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-5, 5, 3000));
                break;

            case CatMood.Yawn:      // droopy eyes, a big yawn every few seconds
                if (_openEyes.RenderTransform is ScaleTransform sleepy) sleepy.ScaleY = 0.45;
                _leftEar.Angle  = -6;
                _rightEar.Angle = 6;
                _yawnMouth.Visibility = Visibility.Visible;
                _yawn.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(4.5), RepeatBehavior = RepeatBehavior.Forever,
                    KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.55)), new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.65)),
                                  new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.85)), new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)) }
                });
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-4, 4, 3400));
                break;

            case CatMood.Focus:     // sits still by the tree, slow blinks
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-3, 3, 2400));
                _blinkTimer.Interval = TimeSpan.FromSeconds(6);
                _blinkTimer.Start();
                break;

            default:                // idle: tail swishes, blinks now and then
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-6, 6, 1600));
                _blinkTimer.Interval = TimeSpan.FromSeconds(4);
                _blinkTimer.Start();
                break;
        }
    }

    private void Eyes(bool open = false, bool happy = false, bool closed = false)
    {
        _openEyes.Visibility   = open && !happy && !closed ? Visibility.Visible : Visibility.Collapsed;
        _happyEyes.Visibility  = happy  ? Visibility.Visible : Visibility.Collapsed;
        _closedEyes.Visibility = closed ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static void Blink(Canvas eyes)
    {
        if (eyes.RenderTransform is not ScaleTransform s) return;
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(80)) { AutoReverse = true });
    }
}

/// <summary>
/// Mây peeking out from behind the right end of the resting notch: just her
/// head and a tail dangling below the pill, swishing to the music.
/// Placed so its left ~12 px are hidden behind the pill.
/// </summary>
public sealed class MayPeek : Canvas
{
    private readonly RotateTransform _tail = new(0, 8, 20);
    private readonly TranslateTransform _bob = new();
    private readonly Canvas _openEyes;
    private readonly Path _happyEyes, _closedEyes;
    private readonly RotateTransform _leftEar, _rightEar;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private CatMood _mood = (CatMood)(-1);

    public const double HiddenPart = 12;   // px tucked behind the pill

    public MayPeek()
    {
        Width = 26;
        Height = 38;
        ClipToBounds = false;

        var tail = CatParts.P("M8,20 C8,29 15,29 13,36", null, CatParts.Tail, 2.2);
        tail.RenderTransform = _tail;
        Children.Add(tail);

        var head = CatParts.Head(out _openEyes, out _happyEyes, out _closedEyes, out _leftEar, out _rightEar, details: false);
        var g = new TransformGroup();
        g.Children.Add(new ScaleTransform(0.6, 0.6));
        g.Children.Add(new TranslateTransform(1, 0.5));
        g.Children.Add(_bob);
        head.RenderTransform = g;
        Children.Add(head);

        _blinkTimer.Tick += (_, _) => MayCat.Blink(_openEyes);
        Mood = CatMood.Idle;
    }

    public CatMood Mood
    {
        get => _mood;
        set
        {
            if (_mood == value) return;
            _mood = value;
            Animate();
        }
    }

    private void Animate()
    {
        _tail.BeginAnimation(RotateTransform.AngleProperty, null);
        _bob.BeginAnimation(TranslateTransform.YProperty, null);
        _tail.Angle = 0;
        _bob.Y = 0;
        _leftEar.Angle = _rightEar.Angle = 0;
        _blinkTimer.Stop();
        // The little head on the pill has fewer parts: the new moods borrow the nearest look
        var mood = _mood switch
        {
            CatMood.Excited => CatMood.Happy,
            CatMood.Stretch or CatMood.Yawn => CatMood.Sleep,
            _ => _mood
        };
        _openEyes.Visibility   = mood is CatMood.Dance or CatMood.Happy or CatMood.Sleep ? Visibility.Collapsed : Visibility.Visible;
        _happyEyes.Visibility  = mood is CatMood.Dance or CatMood.Happy ? Visibility.Visible : Visibility.Collapsed;
        _closedEyes.Visibility = mood == CatMood.Sleep ? Visibility.Visible : Visibility.Collapsed;

        switch (mood)
        {
            case CatMood.Curious:
                _leftEar.Angle = 8;
                _rightEar.Angle = -8;
                _bob.Y = -1;
                break;
            case CatMood.Worried:
                _leftEar.Angle = -16;
                _rightEar.Angle = 16;
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-18, 18, 250));
                break;
            case CatMood.Dance:   // tail swings with the beat, head bobs
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-20, 20, 480));
                _bob.BeginAnimation(TranslateTransform.YProperty, CatParts.Swing(0, 1.2, 240));
                break;
            case CatMood.Happy:
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-16, 16, 260));
                _bob.BeginAnimation(TranslateTransform.YProperty, CatParts.Swing(0, -1.5, 200));
                break;
            case CatMood.Sad:
                _leftEar.Angle = -22;
                _rightEar.Angle = 22;
                _bob.Y = 1.5;
                break;
            case CatMood.Sleep:
                _bob.Y = 1;
                break;
            default:
                _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-8, 8, 1800));
                _blinkTimer.Start();
                break;
        }
    }
}

/// <summary>
/// Mây seen from the side, walking. Legs step in turn; <see cref="FacingRight"/>
/// flips her around. Drawn in a 40×30 space and scaled by <see cref="Size"/>.
/// </summary>
public sealed class MayWalker : Canvas
{
    private readonly Canvas _root = new() { Width = 40, Height = 30 };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScaleTransform _flip = new(1, 1, 20, 15);
    private readonly RotateTransform _tail = new(0, 33, 14);
    private readonly RotateTransform[] _legs = new RotateTransform[4];
    private readonly TranslateTransform _bob = new();
    private readonly DispatcherTimer _step = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _phase;

    public MayWalker()
    {
        IsHitTestVisible = false;
        var g = new TransformGroup();
        g.Children.Add(_flip);
        g.Children.Add(_bob);
        g.Children.Add(_scale);
        _root.RenderTransform = g;
        Children.Add(_root);

        var tail = CatParts.P("M33,14 C40,12 40,4 36,2", null, CatParts.Tail, 3);
        tail.RenderTransform = _tail;
        _root.Children.Add(tail);

        double[] legX = { 12, 17, 25, 30 };
        for (int i = 0; i < 4; i++)
        {
            _legs[i] = new RotateTransform(0, legX[i] + 1.6, 19);
            var leg = new Rectangle
            {
                Width = 3.2, Height = 8, RadiusX = 1.6, RadiusY = 1.6, Fill = CatParts.Fur,
                Stroke = CatParts.Line, StrokeThickness = 0.5, RenderTransform = _legs[i]
            };
            SetLeft(leg, legX[i]);
            SetTop(leg, 19);
            _root.Children.Add(leg);
        }

        _root.Children.Add(CatParts.E(23, 16, 12, 6.8, CatParts.Fur, CatParts.Line));
        _root.Children.Add(CatParts.E(22, 19, 7, 3, CatParts.Cream));
        _root.Children.Add(CatParts.P("M4.5,7 L5.5,0.5 L10,4.5 Z M9.5,5 L12.5,0.5 L14.5,6 Z", CatParts.Fur, CatParts.Line));
        _root.Children.Add(CatParts.P("M6,5.5 L6.5,2.5 L8.6,4.5 Z", CatParts.Pink));
        _root.Children.Add(CatParts.E(10, 11, 7, 7, CatParts.Fur, CatParts.Line));
        _root.Children.Add(CatParts.E(6, 13.5, 3.6, 2.6, CatParts.Cream));
        _root.Children.Add(CatParts.E(8, 10, 1.2, 1.6, CatParts.Eye));
        _root.Children.Add(CatParts.P("M3,12.6 L4.4,12.6 L3.7,13.5 Z", CatParts.Nose));

        _step.Tick += (_, _) => Step();
        Size = 18;
    }

    /// <summary>Width in px.</summary>
    public double Size
    {
        get => Width;
        set
        {
            double s = value / 40;
            _scale.ScaleX = _scale.ScaleY = s;
            Width  = 40 * s;
            Height = 30 * s;
        }
    }

    /// <summary>She's drawn facing left; true turns her to face right.</summary>
    public bool FacingRight
    {
        get => _flip.ScaleX < 0;
        set => _flip.ScaleX = value ? -1 : 1;
    }

    public void StartWalking()
    {
        _tail.BeginAnimation(RotateTransform.AngleProperty, CatParts.Swing(-10, 10, 300));
        _step.Start();
    }

    public void StopWalking()
    {
        _step.Stop();
        _tail.BeginAnimation(RotateTransform.AngleProperty, null);
        foreach (var l in _legs) l.Angle = 0;
        _bob.Y = 0;
    }

    private void Step()
    {
        _phase = !_phase;
        double a = _phase ? 14 : -14;
        for (int i = 0; i < 4; i++) _legs[i].Angle = i % 2 == 0 ? a : -a;
        _bob.Y = _phase ? -0.6 : 0;
    }
}

/// <summary>Mây lying on her back, paws in the air, with a little heart.</summary>
internal static class CatBelly
{
    public static Canvas Build(out Path heart)
    {
        var c = new Canvas { Width = 40, Height = 40 };
        var g = new Canvas { RenderTransform = new TranslateTransform(-1, 8) };
        g.Children.Add(CatParts.P("M35,24 C42,24 42,16 38,14", null, CatParts.Tail, 3));
        g.Children.Add(CatParts.E(22, 22, 13, 7, CatParts.Fur, CatParts.Line));
        g.Children.Add(CatParts.E(22, 19.5, 9, 4.2, CatParts.Cream));
        (double x, double y, double angle)[] paws = { (14, 8, -18), (19, 7, -6), (24, 7, 8), (29, 8.5, 20) };
        foreach (var (x, y, angle) in paws)
        {
            var paw = new Rectangle
            {
                Width = 3.4, Height = 9, RadiusX = 1.7, RadiusY = 1.7, Fill = CatParts.Paw, Stroke = CatParts.Line,
                StrokeThickness = 0.5, RenderTransform = new RotateTransform(angle, 1.7, 8)
            };
            SetPos(paw, x, y);
            g.Children.Add(paw);
        }
        g.Children.Add(CatParts.P("M2,20 L1,12 L7,15.5 Z M9,15 L11.5,9.5 L13.5,16 Z", CatParts.Fur, CatParts.Line));
        g.Children.Add(CatParts.E(8, 21, 7, 7, CatParts.Fur, CatParts.Line));
        g.Children.Add(CatParts.E(8, 24, 3.8, 2.6, CatParts.Cream));
        g.Children.Add(CatParts.P("M3.6,20.5 Q5,19 6.4,20.5 M9.6,20.5 Q11,19 12.4,20.5", null, CatParts.Eye, 1));
        g.Children.Add(CatParts.E(3.5, 23.5, 1.6, 1, CatParts.Blush));
        g.Children.Add(CatParts.E(12.5, 23.5, 1.6, 1, CatParts.Blush));
        c.Children.Add(g);

        heart = CatParts.P("M30,4 C30,2 32.5,1.5 33,3.5 C33.5,1.5 36,2 36,4 C36,6 33,8 33,8 C33,8 30,6 30,4 Z",
                           CatParts.Solid(0xFF, 0x5C, 0x7A));
        heart.RenderTransform = new TranslateTransform();
        c.Children.Add(heart);
        return c;
    }

    private static void SetPos(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
    }
}
