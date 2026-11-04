namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIslandNotifier.Core;

/// <summary>
/// A drawn tree (100×110, ground at y≈100) of one of twelve kinds, with its
/// own little animation: swaying, falling petals or leaves, snow, blinking
/// fireflies, sparkles… It grows from a sprout to full size, and withers
/// (brown, bare) when it died. Scale it with LayoutTransform.
/// </summary>
public sealed class TreeShape : Canvas
{
    private readonly Canvas _plant = new() { Width = 100, Height = 110 };
    private readonly ScaleTransform _scale = new(1, 1, 50, 100);
    private TreeKind _kind = TreeKind.Oak;
    private double _growth = 1;
    private bool _dead;
    private int _stage = -1;      // 0 sprout, 1 tree, 2 dead (rebuild only when it changes)
    private bool _animated = true;
    private readonly Random _rng = new(7);

    public TreeShape()
    {
        Width = 100;
        Height = 110;
        _plant.RenderTransform = _scale;
        Children.Add(_plant);
        Rebuild();
    }

    public TreeKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; _stage = -1; Rebuild(); }
    }

    /// <summary>False for many small trees at once (the forest), or for pictures.</summary>
    public bool Animated
    {
        get => _animated;
        set { if (_animated == value) return; _animated = value; _stage = -1; Rebuild(); }
    }

    /// <param name="growth">0 = just planted, 1 = fully grown.</param>
    public void Set(double growth, bool dead)
    {
        _growth = Math.Clamp(growth, 0, 1);
        _dead = dead;
        Rebuild();
    }

    private void Rebuild()
    {
        int stage = _dead ? 2 : _growth < 0.18 ? 0 : 1;
        double s = _dead ? 1 : stage == 0 ? 1 : 0.35 + 0.65 * _growth;
        _scale.ScaleX = _scale.ScaleY = s;
        if (stage == _stage) return;
        _stage = stage;

        _plant.Children.Clear();
        _plant.Children.Add(Soil());
        if (stage == 0) Sprout();
        else if (stage == 2) Withered();
        else Draw(_kind);
    }

    // ── Shapes ────────────────────────────────────────────────────────
    private static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Brush Radial(params string[] colors)
    {
        var g = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.42, 0.38), RadiusX = 0.7, RadiusY = 0.7 };
        for (int i = 0; i < colors.Length; i++) g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), i / (double)(colors.Length - 1)));
        g.Freeze();
        return g;
    }

    private static Brush Linear(params string[] colors)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i < colors.Length; i++) g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), i / (double)(colors.Length - 1)));
        g.Freeze();
        return g;
    }

    private static Path P(string data, Brush? fill, Brush? stroke = null, double thickness = 0) => new()
    {
        Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    };

    private static Ellipse E(double cx, double cy, double rx, double ry, Brush fill, double opacity = 1)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Opacity = opacity };
        SetLeft(e, cx - rx);
        SetTop(e, cy - ry);
        return e;
    }

    private static Rectangle Rect(double x, double y, double w, double h, double r, Brush fill)
    {
        var rect = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill };
        SetLeft(rect, x);
        SetTop(rect, y);
        return rect;
    }

    private static Canvas Group(params UIElement[] parts)
    {
        var c = new Canvas { Width = 100, Height = 110 };
        foreach (var p in parts) c.Children.Add(p);
        return c;
    }

    private static UIElement Soil() => Group(E(50, 101, 30, 5.5, Hex("#4A3322")), E(50, 99.5, 27, 4, Hex("#5C8A3A")));

    // ── Animations ────────────────────────────────────────────────────
    private static DoubleAnimation Swing(double from, double to, double seconds, double delay = 0) => new(from, to, TimeSpan.FromSeconds(seconds))
    {
        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-delay),
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
    };

    /// <summary>Gentle swaying around a point (the whole tree around its base, or a branch).</summary>
    private UIElement Sway(UIElement e, double cx = 50, double cy = 100, double angle = 1.6, double seconds = 4.5, double delay = 0)
    {
        var r = new RotateTransform(0, cx, cy);
        e.RenderTransform = r;
        if (_animated) r.BeginAnimation(RotateTransform.AngleProperty, Swing(-angle, angle, seconds / 2, delay));
        return e;
    }

    /// <summary>Petals / leaves / snowflakes drifting down.</summary>
    private void Falling(string color, bool leaf, int count, double x0, double x1, double y, double size)
    {
        if (!_animated) return;
        var brush = Hex(color);
        for (int i = 0; i < count; i++)
        {
            double x = x0 + _rng.NextDouble() * (x1 - x0), dx = _rng.NextDouble() * 16 - 8, delay = i * 5.0 / count;
            var p = leaf ? E(x, y, size, size * 0.6, brush) : E(x, y, size * 0.7, size * 0.7, brush);
            var move = new TranslateTransform();
            var spin = new RotateTransform(0, x, y);
            p.RenderTransform = new TransformGroup { Children = { spin, move } };
            var span = TimeSpan.FromSeconds(5);
            var begin = TimeSpan.FromSeconds(-delay);
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 70, span) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, span) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 220, span) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            var fade = new DoubleAnimationUsingKeyFrames { Duration = span, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            p.BeginAnimation(OpacityProperty, fade);
            _plant.Children.Add(p);
        }
    }

    private void Blink(UIElement e, double delay)
    {
        if (_animated) e.BeginAnimation(OpacityProperty, Swing(0.15, 1, 1.2, delay));
    }

    private void Bob(UIElement e, double delay = 0)
    {
        var t = new TranslateTransform();
        e.RenderTransform = t;
        if (_animated) t.BeginAnimation(TranslateTransform.YProperty, Swing(0, -1.5, 1.3, delay));
    }

    private void Pulse(UIElement e, double cx, double cy, double delay = 0)
    {
        var s = new ScaleTransform(1, 1, cx, cy);
        e.RenderTransform = s;
        if (!_animated) return;
        s.BeginAnimation(ScaleTransform.ScaleXProperty, Swing(1, 1.12, 1.1, delay));
        s.BeginAnimation(ScaleTransform.ScaleYProperty, Swing(1, 1.12, 1.1, delay));
    }

    // ── Special stages ────────────────────────────────────────────────
    private void Sprout()
    {
        var green = Linear("#9BE27A", "#3E9A48");
        _plant.Children.Add(Sway(Group(
            P("M50,100 C50,94 50,90 50,86", null, Hex("#5E8A3A"), 2.4),
            P("M50,88 C44,86 40,82 41,78 C46,78 50,82 50,88 Z", green),
            P("M50,86 C56,84 60,79 59,75 C54,75 50,79 50,86 Z", green)), angle: 4, seconds: 3));
    }

    private void Withered()
    {
        var wood = Hex("#6A5442");
        _plant.Children.Add(Group(
            P("M47,100 C48,86 47,74 48,62 L52,62 C53,74 52,86 53,100 Z", wood),
            P("M50,70 C44,64 38,62 34,56 M50,66 C56,60 62,58 66,52 M49,62 C47,54 44,50 42,44 M51,62 C53,54 56,50 58,44", null, wood, 2.2),
            E(36, 56, 3.5, 2.2, Hex("#8A6B4E")), E(64, 54, 3, 2, Hex("#7A5E44")), E(57, 44, 2.6, 1.8, Hex("#8A6B4E"))));
    }

    // ── The twelve trees ──────────────────────────────────────────────
    private void Draw(TreeKind kind)
    {
        switch (kind)
        {
            case TreeKind.Oak:
            {
                var c = Radial("#A8F07A", "#4FBF4A", "#2A7D34");
                _plant.Children.Add(Sway(Group(
                    P("M46,100 C47,85 46,72 44,62 L56,62 C54,72 53,85 54,100 Z", Linear("#8A5A36", "#5E3A20")),
                    P("M50,70 C44,66 40,64 36,58 M50,72 C56,66 60,64 64,60", null, Hex("#6B4428"), 2.5),
                    E(50, 42, 24, 24, c), E(32, 54, 15, 15, c), E(68, 54, 15, 15, c), E(44, 34, 7, 7, Hex("#D4FFB0"), 0.35))));
                break;
            }
            case TreeKind.Pine:
            {
                var c = Linear("#4FAE7A", "#1E5C42");
                var snow = Hex("#F4FBFF");
                _plant.Children.Add(Sway(Group(
                    Rect(46.5, 84, 7, 16, 2, Hex("#6B4428")),
                    P("M50,58 L74,88 L26,88 Z", c), P("M50,38 L70,68 L30,68 Z", c), P("M50,16 L65,48 L35,48 Z", c),
                    P("M50,16 L57,31 L53,29 L50,33 L46,29 L43,31 Z M50,38 L58,50 L54,49 L50,52 L46,49 L42,50 Z M50,58 L60,71 L55,70 L50,73 L45,70 L40,71 Z", snow))));
                Falling("#FFFFFF", false, 7, 20, 80, 10, 2);
                break;
            }
            case TreeKind.Cherry:
            {
                var c = Radial("#FFE1EF", "#FF9DC6", "#E0679F");
                _plant.Children.Add(Sway(Group(
                    P("M48,100 C49,86 46,74 41,64 M50,80 C55,70 60,64 66,58 M50,100 C51,86 51,76 50,66", null, Hex("#5A3626"), 5),
                    E(40, 44, 18, 18, c), E(62, 42, 19, 19, c), E(51, 28, 16, 16, c), E(68, 58, 11, 11, c), E(46, 24, 5, 5, Hex("#FFFFFF"), 0.45))));
                Falling("#FFB6D3", true, 9, 25, 78, 45, 2.4);
                break;
            }
            case TreeKind.Maple:
            {
                var c = Radial("#FFCF6B", "#FF7A2F", "#C3321F");
                _plant.Children.Add(Sway(Group(
                    P("M46,100 C47,84 46,70 45,60 L55,60 C54,70 53,84 54,100 Z", Hex("#5E3A20")),
                    E(50, 40, 27, 24, c), E(33, 56, 13, 11, c), E(67, 55, 13, 11, c), E(42, 30, 6, 6, Hex("#FFF2B0"), 0.35))));
                Falling("#FF6A2A", true, 8, 26, 76, 50, 2.6);
                break;
            }
            case TreeKind.Cactus:
            {
                var c = Linear("#7FE08A", "#2F9A52");
                _plant.Children.Clear();   // sits in a pot instead of on grass
                var flower = Group(E(50, 37, 6, 6, Hex("#FF5C9A")), E(50, 37, 2.4, 2.4, Hex("#FFE36B")));
                Pulse(flower, 50, 37);
                _plant.Children.Add(Group(P("M30,92 L70,92 L66,104 L34,104 Z", Hex("#D9844E")), Rect(28, 88, 44, 6, 2, Hex("#E99A63"))));
                _plant.Children.Add(Sway(Group(
                    Rect(42, 40, 16, 50, 8, c),
                    P("M42,66 L34,66 C30,66 28,63 28,59 L28,50", null, c, 9),
                    P("M58,60 L65,60 C69,60 71,57 71,53 L71,46", null, c, 8),
                    P("M46,48 l-2,-1 M54,56 l2,-1 M47,70 l-2,-1 M53,78 l2,-1", null, Hex("#E8FFE9"), 1),
                    flower), cy: 90, angle: 3, seconds: 3.2));
                break;
            }
            case TreeKind.Orange:
            {
                var c = Radial("#9BE27A", "#2F8A3E");
                var f = Radial("#FFE08A", "#FF8A1C");
                var fruits = Group(E(38, 44, 4.6, 4.6, f), E(60, 36, 4.6, 4.6, f), E(56, 56, 4.6, 4.6, f), E(44, 28, 4.2, 4.2, f));
                Bob(fruits);
                _plant.Children.Add(Sway(Group(
                    P("M46,100 C47,86 46,74 45,64 L55,64 C54,74 53,86 54,100 Z", Hex("#6B4428")),
                    E(50, 44, 26, 26, c), fruits)));
                break;
            }
            case TreeKind.Bamboo:
            {
                (double x, double top, double d)[] stalks = { (38, 22, 0), (50, 10, 0.8), (62, 26, 1.6) };
                foreach (var (x, top, d) in stalks)
                {
                    var g = Group(Rect(x - 3, top, 6, 100 - top, 3, Hex("#6CC06A")),
                        P($"M{x},{top + 10} q10,-6 16,-2 q-8,4 -16,2 M{x},{top + 30} q-10,-6 -16,-2 q8,4 16,2", Hex("#4FAE55")));
                    for (int i = 0; i < 5; i++) g.Children.Add(Rect(x - 3.6, top + 12 + i * 14, 7.2, 2, 1, Hex("#3F8F45")));
                    _plant.Children.Add(Sway(g, x, 100, 3, 3.2, d));
                }
                break;
            }
            case TreeKind.Willow:
            {
                var g = Group(P("M47,100 C48,84 46,70 49,52 L53,52 C54,70 53,84 54,100 Z", Hex("#5E3A20")), E(50, 34, 28, 16, Hex("#5CAE5A")));
                for (int i = 0; i < 11; i++)
                {
                    double x = 24 + i * 5.2, len = 30 + _rng.NextDouble() * 16;
                    g.Children.Add(Sway(P($"M{x},36 C{x - 2},{36 + len / 2} {x + 2},{36 + len * 0.8} {x},{36 + len}", null, Hex("#7FD06E"), 2.2), x, 36, 3, 3.2, i * 0.3));
                }
                _plant.Children.Add(Sway(g));
                break;
            }
            case TreeKind.Wisteria:
            {
                var c = Linear("#E6D4FF", "#9B6CFF");
                var g = Group(P("M46,100 C48,80 42,62 50,46 C58,62 52,80 54,100 Z", Hex("#5E3A20")), E(50, 32, 32, 14, Hex("#5F9E56")));
                double[] xs = { 22, 32, 42, 52, 62, 72, 78 };
                for (int i = 0; i < xs.Length; i++)
                    g.Children.Add(Sway(P($"M{xs[i] - 4},34 Q{xs[i]},{66 + (i % 3) * 6} {xs[i] + 4},34 Z", c), xs[i], 34, 3, 3.2, i * 0.4));
                _plant.Children.Add(Sway(g));
                Falling("#C9A8FF", true, 6, 24, 76, 55, 2);
                break;
            }
            case TreeKind.Firefly:
            {
                var c = Radial("#3F8F8A", "#163D44");
                _plant.Children.Add(Sway(Group(
                    P("M46,100 C47,86 46,74 45,62 L55,62 C54,74 53,86 54,100 Z", Hex("#4A3322")),
                    E(50, 44, 27, 27, c), E(31, 58, 13, 13, c), E(69, 58, 13, 13, c))));
                for (int i = 0; i < 10; i++)
                {
                    var dot = E(22 + _rng.NextDouble() * 56, 22 + _rng.NextDouble() * 44, 1.6, 1.6, Hex("#FFF47A"));
                    Blink(dot, i * 0.37);
                    _plant.Children.Add(dot);
                }
                break;
            }
            case TreeKind.Crystal:
            {
                _plant.Children.Add(Sway(Group(
                    P("M47,100 L48,62 L52,62 L53,100 Z", Hex("#7D8CC4")),
                    P("M50,12 L72,40 L62,68 L38,68 L28,40 Z", Linear("#E9F6FF", "#8FC8FF", "#6A6CFF")),
                    P("M50,12 L50,68 M28,40 L72,40 M50,12 L38,68 M50,12 L62,68", null, Hex("#99FFFFFF"), 0.7))));
                (double x, double y)[] spots = { (30, 26), (70, 30), (64, 62), (36, 60), (50, 8) };
                for (int i = 0; i < spots.Length; i++)
                {
                    var (x, y) = spots[i];
                    var star = P($"M{x},{y - 4} L{x + 1.2},{y - 1.2} L{x + 4},{y} L{x + 1.2},{y + 1.2} L{x},{y + 4} L{x - 1.2},{y + 1.2} L{x - 4},{y} L{x - 1.2},{y - 1.2} Z", Hex("#FFFFFF"));
                    Blink(star, i * 0.5);
                    _plant.Children.Add(star);
                }
                break;
            }
            case TreeKind.Bonsai:
            {
                _plant.Children.Clear();   // in a pot
                var c = Radial("#9FE07C", "#2F8A3E");
                _plant.Children.Add(Group(P("M26,92 L74,92 L70,104 L30,104 Z", Hex("#3B4A6B")), Rect(24, 88, 52, 6, 2, Hex("#4C5D84"))));
                var pads = new[] { E(58, 38, 18, 8, c), E(72, 54, 12, 6, c), E(28, 63, 12, 6, c) };
                for (int i = 0; i < pads.Length; i++) Bob(pads[i], i * 0.9);
                var g = Group(P("M48,90 C40,80 58,72 50,62 C44,55 52,48 58,44", null, Hex("#6B4428"), 6),
                              P("M52,66 C60,64 66,60 70,56 M48,76 C40,74 34,70 30,66", null, Hex("#6B4428"), 3));
                foreach (var p in pads) g.Children.Add(p);
                _plant.Children.Add(Sway(g, cy: 90, angle: 1.2));
                break;
            }
        }
    }

    // ── Image for the notification island ─────────────────────────────
    /// <summary>The tree on a round background, as an image.</summary>
    public static BitmapSource Render(double growth, bool dead, TreeKind kind = TreeKind.Oak)
    {
        const int size = 104;
        var tree = new TreeShape
        {
            Animated            = false,
            Kind                = kind,
            LayoutTransform     = new ScaleTransform(0.8, 0.8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        tree.Set(growth, dead);

        var root = new Grid { Width = size, Height = size };
        root.Children.Add(new Ellipse { Fill = dead ? Hex("#3A2E26") : Hex("#1F3A2A") });
        root.Children.Add(tree);

        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }
}
