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
        if (stage != 1) _plant.Children.Add(Soil());   // grown trees bring their own soft shadow
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
    // Drawn like an illustration rather than plastic balls: crowns are many small
    // leaf clumps painted in three flat tones (shade down-right, mid, sunlit
    // up-left), trunks taper with bark shading and branches, a soft shadow below.
    // Shapes come from a seeded random, so a kind always looks the same.

    private static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The same little random sequence as the design preview, so trees match it.</summary>
    private sealed class Seeded
    {
        private long _s;
        public Seeded(long seed) => _s = seed;
        public double Next() { _s = (_s * 16807) % 2147483647; return _s / 2147483647.0; }
    }

    private sealed record Palette(string Light, string Base, string Dark, string Deep);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Brush Across(params string[] colors)   // left → right (bark, stalks, shards)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i < colors.Length; i++) g.GradientStops.Add(new GradientStop(C(colors[i]), i / (double)(colors.Length - 1)));
        g.Freeze();
        return g;
    }

    private static Brush Lit(params (double Offset, string Color)[] stops)   // round, lit from the top-left
    {
        var g = new RadialGradientBrush { GradientOrigin = new Point(0.3, 0.28), Center = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75 };
        foreach (var (o, c) in stops) g.GradientStops.Add(new GradientStop(C(c), o));
        g.Freeze();
        return g;
    }

    private static UIElement GroundShadow(double w)
    {
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0x73, 0, 0, 0), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        g.Freeze();
        return E(50, 100, w, 5, g);
    }

    private static Path Shape(Geometry g, Brush fill, double opacity = 1) => new() { Data = g, Fill = fill, Opacity = opacity };

    private static UIElement Trunk(string dark, string mid, string light, double x = 50, double top = 52, double bottom = 100,
                                   double w = 9, double lean = 0, int branches = 2, long seed = 3)
    {
        var r = new Seeded(seed);
        double tx = x + lean;
        var group = new Canvas { Width = 100, Height = 110 };
        group.Children.Add(P($"M{F(x - w / 2)},{F(bottom)} C{F(x - w / 2)},{F(bottom - 18)} {F(tx - w / 4)},{F(top + 14)} {F(tx - w / 5)},{F(top)} " +
                             $"L{F(tx + w / 5)},{F(top)} C{F(tx + w / 4)},{F(top + 14)} {F(x + w / 2)},{F(bottom - 18)} {F(x + w / 2)},{F(bottom)} " +
                             $"Q{F(x + w * .9)},{F(bottom + 1.5)} {F(x + w * 1.1)},{F(bottom + 1)} L{F(x - w * 1.1)},{F(bottom + 1)} Q{F(x - w * .9)},{F(bottom + 1.5)} {F(x - w / 2)},{F(bottom)} Z",
                             Across(dark, mid, light)));
        for (int i = 0; i < branches; i++)
        {
            int side = i % 2 == 1 ? 1 : -1;
            double y = top + 8 + i * 6 + r.Next() * 4, len = 9 + r.Next() * 6;
            group.Children.Add(P($"M{F(tx + side * 1.5)},{F(y + 3)} Q{F(tx + side * len * .5)},{F(y - 2)} {F(tx + side * len)},{F(y - 7 - r.Next() * 4)}",
                                 null, Hex(mid), 2.4 - i * .4));
        }
        var lines = P($"M{F(x - 1.5)},{F(bottom - 4)} q1,-10 0,-20 M{F(x + 1.8)},{F(bottom - 6)} q-1,-8 .5,-16", null, Hex(dark), .6);
        lines.Opacity = .55;
        group.Children.Add(lines);
        return group;
    }

    private static readonly (string Dark, string Mid, string Light) Wood = ("#3B2416", "#6B4428", "#8E6340");

    /// <summary>A crown: shade, mid and light tones of many small clumps, plus a few leaf flecks.</summary>
    private static UIElement Crown(double cx, double cy, double w, double h, Palette pal, int n = 9, long seed = 1,
                                   bool flecks = true, string? fleckColor = null)
    {
        var r = new Seeded(seed);
        int count = (int)Math.Round(n * 2.2);
        var clumps = new (double X, double Y, double R)[count];
        for (int i = 0; i < count; i++)
        {
            double a = r.Next() * Math.PI * 2, d = Math.Pow(r.Next(), .6);
            double x = cx + Math.Cos(a) * d * w * .44, y = cy + Math.Sin(a) * d * h * .4;
            clumps[i] = (x, y, (w + h) / 4 * (.27 + (1 - d) * .16 + r.Next() * .08));
        }
        Array.Sort(clumps, (p, q) => p.Y.CompareTo(q.Y));

        var shade = new GeometryGroup { FillRule = FillRule.Nonzero };
        var mid   = new GeometryGroup { FillRule = FillRule.Nonzero };
        var light = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var c in clumps)
        {
            shade.Children.Add(new EllipseGeometry(new Point(c.X + c.R * .28, c.Y + c.R * .34), c.R, c.R));
            mid.Children.Add(new EllipseGeometry(new Point(c.X, c.Y), c.R * .9, c.R * .9));
            bool lit = (cx - c.X) * .6 + (cy - c.Y) > -h * .05;   // the upper-left side faces the sun
            if (lit) light.Children.Add(new EllipseGeometry(new Point(c.X - c.R * .28, c.Y - c.R * .3), c.R * .52, c.R * .52));
        }
        var group = new Canvas { Width = 100, Height = 110 };
        group.Children.Add(E(cx + w * .04, cy + h * .14, w * .44, h * .36, Hex(pal.Deep)));
        group.Children.Add(Shape(shade, Hex(pal.Dark)));
        group.Children.Add(Shape(mid, Hex(pal.Base)));
        group.Children.Add(Shape(light, Hex(pal.Light), .85));
        if (flecks)
        {
            var pale = new GeometryGroup();
            var dark = new GeometryGroup();
            for (int i = 0; i < 16; i++)
            {
                var c = clumps[(int)(r.Next() * clumps.Length)];
                double x = c.X + (r.Next() - .6) * c.R, y = c.Y + (r.Next() - .7) * c.R;
                var leaf = new EllipseGeometry(new Point(x + 1.3, y), 1.3, .55) { Transform = new RotateTransform(-40 + r.Next() * 80, x, y) };
                (r.Next() > .4 ? pale : dark).Children.Add(leaf);
                r.Next();   // keeps the sequence in step with the preview (opacity there)
            }
            group.Children.Add(Shape(pale, Hex(fleckColor ?? pal.Light), .75));
            group.Children.Add(Shape(dark, Hex(pal.Dark), .7));
        }
        return group;
    }

    private UIElement Twinkle(double x, double y, double size, double seconds, double delay)
    {
        double a = size, b = size * .25;
        var star = P($"M{F(x)},{F(y - a)} L{F(x + b)},{F(y - b)} L{F(x + a)},{F(y)} L{F(x + b)},{F(y + b)} L{F(x)},{F(y + a)} L{F(x - b)},{F(y + b)} L{F(x - a)},{F(y)} L{F(x - b)},{F(y - b)} Z", Hex("#F2FDFF"));
        if (_animated) star.BeginAnimation(OpacityProperty, Swing(0, 1, seconds / 2, delay));
        else star.Opacity = .8;
        return star;
    }

    private void Draw(TreeKind kind)
    {
        var plant = _plant.Children;
        switch (kind)
        {
            case TreeKind.Oak:
                plant.Add(GroundShadow(32));
                plant.Add(Trunk(Wood.Dark, Wood.Mid, Wood.Light, w: 11, top: 56, branches: 3, seed: 5));
                plant.Add(Sway(Crown(50, 42, 66, 50, new("#8CCB5E", "#4E9A3F", "#2F6A31", "#1D4423"), n: 11, seed: 7), seconds: 5));
                break;

            case TreeKind.Pine:
            {
                // a slim blue-green spruce: five neat tiers with jagged, drooping hems,
                // lit half on the left, frost on the points, a pale tip that twinkles
                var r = new Seeded(5);
                plant.Add(GroundShadow(20));
                plant.Add(Trunk(Wood.Dark, Wood.Mid, Wood.Light, w: 6, top: 86, branches: 0, seed: 2));
                var tree = new Canvas { Width = 100, Height = 110 };
                var tiers = new (double Top, double Bottom, double Half)[] { (11, 34, 9), (22, 50, 14), (34, 64, 18), (47, 77, 21), (60, 89, 23) };
                for (int i = tiers.Length - 1; i >= 0; i--)
                {
                    var (top, bottom, hw) = tiers[i];
                    int teeth = 4 + i;
                    var hem = new (double X, double Y)[teeth + 1];
                    for (int k = 0; k <= teeth; k++)
                        hem[k] = (50 - hw + 2 * hw * k / teeth, bottom + (k % 2 == 1 ? -3.2 : 0) + (r.Next() - .5) * .8);
                    string Outline((double X, double Y)[] pts) => $"M50,{F(top)} " + string.Join(" ", Array.ConvertAll(pts, p => $"L{F(p.X)},{F(p.Y)}")) + " Z";
                    var left = Array.FindAll(hem, p => p.X <= 50.01);
                    var leftWithCentre = new (double X, double Y)[left.Length + 1];
                    left.CopyTo(leftWithCentre, 0);
                    leftWithCentre[^1] = (50, bottom - 3);
                    tree.Children.Add(P(Outline(hem), Hex("#1A5F6B")));
                    tree.Children.Add(P(Outline(leftWithCentre), Hex(i < 2 ? "#4FB0B9" : "#2A8592")));
                    for (int k = 2; k < teeth; k += 2)
                    {
                        var (x, y) = hem[k]; var (px, py) = hem[k - 1]; var (nx, ny) = hem[k + 1];
                        const double f = .55;
                        var frost = P($"M{F(x)},{F(y)} L{F(x + (px - x) * f)},{F(y + (py - y) * f)} L{F(x + (nx - x) * f)},{F(y + (ny - y) * f)} Z", Hex("#DDF6F9"));
                        frost.Opacity = x < 50 ? .8 : .4;
                        tree.Children.Add(frost);
                    }
                    if (i > 1) tree.Children.Add(E(50 + (r.Next() - .3) * hw, bottom - 6, 1, 1.5, Hex("#8A5A2E")));
                }
                tree.Children.Add(P("M50,8 L53,17 L47,17 Z", Hex("#9FE3EA")));
                tree.Children.Add(Twinkle(50, 7, 2.2, 2.4, 0));
                tree.Children.Add(Twinkle(42, 42, 2.2, 3.1, 1));
                tree.Children.Add(Twinkle(58, 60, 2.2, 2.8, 2));
                plant.Add(Sway(tree, angle: 1, seconds: 6.5));
                break;
            }

            case TreeKind.Cherry:
                plant.Add(GroundShadow(30));
                plant.Add(Trunk("#2E1B18", "#5A3A33", "#7A5248", w: 9, top: 54, lean: 4, branches: 3, seed: 9));
                plant.Add(Sway(Crown(50, 40, 68, 46, new("#FFD6E5", "#F4A3C0", "#D27499", "#8E4463"), n: 12, seed: 11, fleckColor: "#FFFFFF"), angle: 1.5, seconds: 5.5));
                Falling("#FFC2D8", leaf: true, 6, 22, 78, 50, 1.6);
                break;

            case TreeKind.Cactus:
            {
                var body = Across("#2F6B3A", "#4E9A4C", "#6DB65A", "#2F6B3A");
                plant.Add(E(50, 99, 26, 5, Hex("#C8A26A")));
                plant.Add(E(50, 98, 20, 3.4, Hex("#DDBB82")));
                plant.Add(GroundShadow(18));
                plant.Add(P("M44,98 L44,40 Q44,30 50,30 Q56,30 56,40 L56,98 Z", body));
                plant.Add(P("M44,66 L37,66 Q32,66 32,60 L32,48 Q32,44 35.5,44 Q39,44 39,48 L39,59 L44,59 Z", body));
                plant.Add(P("M56,74 L63,74 Q68,74 68,68 L68,54 Q68,50 64.5,50 Q61,50 61,54 L61,67 L56,67 Z", body));
                var ribs = P("M47.5,96 L47.5,36 M50,96 L50,31 M52.5,96 L52.5,36 M35.5,46 L35.5,64 M64.5,52 L64.5,72", null, Hex("#255A2E"), .7);
                ribs.Opacity = .6;
                plant.Add(ribs);
                var r = new Seeded(8);
                var spines = new GeometryGroup();
                for (int i = 0; i < 26; i++) spines.Children.Add(new EllipseGeometry(new Point(44.6 + r.Next() * 10.8, 36 + r.Next() * 58), .45, .45));
                plant.Add(Shape(spines, Hex("#F3F0D8"), .85));
                var flower = new Canvas { Width = 100, Height = 110 };
                for (int i = 0; i < 6; i++)
                {
                    var petal = E(50, 24.5, 2.6, 5, Hex(i % 2 == 1 ? "#FF6FA0" : "#FF8FB6"));
                    petal.RenderTransform = new RotateTransform(i * 60, 2.6, 9.5);   // around the flower's middle (50, 29)
                    flower.Children.Add(petal);
                }
                flower.Children.Add(E(50, 29, 2.4, 2.4, Hex("#FFD45C")));
                plant.Add(Sway(flower, 50, 31, 4, 3.5));
                break;
            }

            case TreeKind.Orange:
            {
                var crown = (Canvas)Crown(50, 42, 64, 50, new("#5FAE4E", "#2F7F3A", "#1D5A2B", "#11381B"), n: 11, seed: 21);
                var r = new Seeded(13);
                var fruit = Lit((0, "#FFE29A"), (.35, "#FFA51F"), (1, "#D9650A"));
                for (int i = 0; i < 8; i++)
                {
                    double x = 30 + r.Next() * 40, y = 26 + r.Next() * 34, R = 2.8 + r.Next() * .9;
                    crown.Children.Add(E(x, y, R, R, fruit));
                    crown.Children.Add(P($"M{F(x)},{F(y - R)} l.8,-1.2", null, Hex("#2F6B2A"), .7));
                }
                plant.Add(GroundShadow(30));
                plant.Add(Trunk(Wood.Dark, Wood.Mid, Wood.Light, w: 9, top: 58, branches: 2, seed: 17));
                plant.Add(Sway(crown, angle: 1.4, seconds: 5.2));
                break;
            }

            case TreeKind.Bamboo:
            {
                plant.Add(GroundShadow(22));
                var stalks = new (double X, double Top, double W)[] { (40, 14, 4), (50, 6, 4.6), (59, 18, 3.8), (33, 30, 3.2), (66, 28, 3.2) };
                for (int i = 0; i < stalks.Length; i++)
                {
                    var (x, top, w) = stalks[i];
                    var stalk = new Canvas { Width = 100, Height = 110 };
                    stalk.Children.Add(Rect(x - w / 2, top, w, 100 - top, w / 2, Across("#3E7A2E", "#7CB342", "#A4CF5E", "#4C8A33")));
                    for (double y = top + 10; y < 98; y += 11) stalk.Children.Add(Rect(x - w / 2 - .4, y, w + .8, 1.3, .6, Hex("#2F5E22")));
                    var lr = new Seeded(30 + i);
                    for (int k = 0; k < 4; k++)
                    {
                        double y = top + 4 + lr.Next() * 30, side = lr.Next() > .5 ? 1 : -1, len = 9 + lr.Next() * 6;
                        stalk.Children.Add(P($"M{F(x)},{F(y)} q{F(side * len * .6)},-3 {F(side * len)},2 q{F(-side * len * .5)},1.5 {F(-side * len)},-2 Z",
                                             Hex(lr.Next() > .5 ? "#5FA83C" : "#8CC152")));
                    }
                    plant.Add(Sway(stalk, x, 100, 1 + i * .3, 4 + i * .7));
                }
                break;
            }

            case TreeKind.Maple:
                plant.Add(GroundShadow(30));
                plant.Add(Trunk(Wood.Dark, Wood.Mid, Wood.Light, w: 9, top: 56, branches: 3, seed: 23));
                plant.Add(Sway(Crown(50, 40, 68, 48, new("#F8A05A", "#DD5630", "#A3331E", "#651C13"), n: 12, seed: 31, fleckColor: "#FFD08A"), angle: 1.5, seconds: 5));
                Falling("#F2793A", leaf: true, 5, 24, 76, 50, 2);
                break;

            case TreeKind.Willow:
            {
                var crown = (Canvas)Crown(50, 34, 60, 30, new("#C7E58A", "#8DBF4E", "#5C8E34", "#3B6223"), n: 9, seed: 41);
                var r = new Seeded(19);
                for (int i = 0; i < 18; i++)
                {
                    double x = 24 + i * 3 + r.Next() * 2, y0 = 30 + r.Next() * 10, len = 30 + r.Next() * 26;
                    var strand = P($"M{F(x)},{F(y0)} q{F(-2 + r.Next() * 4)},{F(len * .5)} {F(-1 + r.Next() * 2)},{F(len)}",
                                   null, Hex(r.Next() > .5 ? "#7DB043" : "#A3CC62"), 1.3 + r.Next());
                    strand.StrokeDashArray = new DoubleCollection { 2.4 / strand.StrokeThickness, 1.2 / strand.StrokeThickness };
                    crown.Children.Add(strand);
                }
                plant.Add(GroundShadow(32));
                plant.Add(Trunk(Wood.Dark, Wood.Mid, Wood.Light, w: 9, top: 52, lean: -2, branches: 2, seed: 29));
                plant.Add(Sway(crown, 50, 50, 2.4, 6));
                break;
            }

            case TreeKind.Wisteria:
            {
                var crown = (Canvas)Crown(50, 34, 64, 34, new("#A5D78A", "#5F9E4E", "#3C6E35", "#264A24"), n: 9, seed: 51, flecks: false);
                var r = new Seeded(27);
                var bloom = new LinearGradientBrush(C("#C9B6FF"), C("#6E4DC4"), 90);
                bloom.GradientStops.Insert(1, new GradientStop(C("#9B7BEA"), .6));
                bloom.Freeze();
                for (int i = 0; i < 9; i++)
                {
                    double x = 24 + i * 6.4 + r.Next() * 2, y = 38 + r.Next() * 6, len = 14 + r.Next() * 12;
                    crown.Children.Add(P($"M{F(x - 3.4)},{F(y)} Q{F(x - 3)},{F(y + len * .6)} {F(x)},{F(y + len)} Q{F(x + 3)},{F(y + len * .6)} {F(x + 3.4)},{F(y)} Z", bloom));
                    for (int k = 1; k < 5; k++) crown.Children.Add(E(x + (k % 2 == 1 ? -1 : 1), y + k * len / 5.5, 1, 1, Hex("#E5DAFF"), .55));
                }
                plant.Add(GroundShadow(30));
                plant.Add(Trunk("#3A2A22", "#5E463A", "#7C6050", w: 9, top: 50, lean: 3, branches: 3, seed: 33));
                plant.Add(Sway(crown, angle: 1.8, seconds: 6));
                break;
            }

            case TreeKind.Firefly:
            {
                plant.Add(GroundShadow(30));
                plant.Add(Trunk("#1E1A18", "#3A322C", "#544A40", w: 9, top: 58, branches: 2, seed: 41));
                plant.Add(Sway(Crown(50, 42, 64, 50, new("#3F8F7A", "#1F5E54", "#143F3A", "#0B2826"), n: 11, seed: 61, fleckColor: "#6CC1A6"), angle: 1.2, seconds: 6));
                var r = new Seeded(37);
                var glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(C("#FFF6A8"), 0));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xBF, 0xFF, 0xE6, 0x5A), .35));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xDC, 0x50), 1));
                glow.Freeze();
                for (int i = 0; i < 12; i++)
                {
                    double x = 22 + r.Next() * 56, y = 18 + r.Next() * 60, d = 1.6 + r.Next() * 2;
                    var fly = E(x, y, 2.6, 2.6, glow);
                    if (_animated) fly.BeginAnimation(OpacityProperty, Swing(.15, 1, d / 2, r.Next() * d));
                    plant.Add(fly);
                }
                break;
            }

            case TreeKind.Crystal:
            {
                plant.Add(GroundShadow(22));
                plant.Add(E(50, 97, 24, 4, Hex("#3A4A6A")));
                var shards = new (double X, double Top, double W, double H)[] { (50, 20, 9, 76), (38, 46, 7, 52), (62, 40, 7, 58), (30, 66, 5, 32), (70, 62, 5, 36), (45, 58, 4.5, 40), (56, 62, 4.5, 36) };
                var ice = Across("#E4F6FF", "#9FDCFF", "#5BA8F0", "#3B6FD0");
                foreach (var (x, top, w, h) in shards)
                {
                    double bottom = top + h;
                    var body = P($"M{F(x)},{F(top)} L{F(x + w)},{F(top + w * 1.4)} L{F(x + w * .8)},{F(bottom)} L{F(x - w * .8)},{F(bottom)} L{F(x - w)},{F(top + w * 1.4)} Z", ice);
                    body.Opacity = .95;
                    plant.Add(body);
                    var face = P($"M{F(x)},{F(top)} L{F(x - w)},{F(top + w * 1.4)} L{F(x - w * .8)},{F(bottom)} L{F(x - w * .2)},{F(bottom)} Z", Hex("#FFFFFF"));
                    face.Opacity = .22;
                    plant.Add(face);
                    var edge = P($"M{F(x)},{F(top)} L{F(x + w * .25)},{F(bottom)}", null, Hex("#FFFFFF"), .5);
                    edge.Opacity = .5;
                    plant.Add(edge);
                }
                plant.Add(Twinkle(50, 33, 3, 2.6, 0));
                plant.Add(Twinkle(64, 52, 2.2, 3.1, 1.2));
                break;
            }

            case TreeKind.Bonsai:
            {
                plant.Add(P("M24,86 L76,86 L71,99 L29,99 Z", Across("#5A3424", "#8A5236", "#4A2A1C")));
                plant.Add(Rect(21, 83, 58, 4, 2, Hex("#9A5E3E")));
                plant.Add(GroundShadow(28));
                plant.Add(P("M44,84 C42,74 54,70 50,60 C47,52 36,52 36,44 L41,44 C42,50 52,50 55,57 C59,66 50,74 54,84 Z", Across("#2E1E16", "#5C4030", "#7A5A44")));
                plant.Add(P("M50,60 C58,56 64,52 68,44", null, Hex("#5C4030"), 3));
                var pal = new Palette("#8FD06A", "#3E8A3A", "#255E28", "#163E1A");
                var pads = new Canvas { Width = 100, Height = 110 };
                pads.Children.Add(Crown(37, 38, 34, 16, pal, n: 7, seed: 71));
                pads.Children.Add(Crown(68, 40, 28, 13, pal, n: 6, seed: 73));
                pads.Children.Add(Crown(54, 24, 26, 13, pal, n: 6, seed: 75));
                plant.Add(Sway(pads, 50, 84, 1, 6.5));
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
