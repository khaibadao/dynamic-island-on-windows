namespace DynamicIslandNotifier.UI;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIslandNotifier.Core;

/// <summary>
/// A small drawn tree (100×100) of one of several kinds. It grows from a
/// sprout to full size, or turns brown when it died. Scale it with LayoutTransform.
/// </summary>
public sealed class TreeShape : Canvas
{
    // Dead colours, shared by every kind
    private static readonly Brush DeadWood   = Frozen(0x5A, 0x46, 0x32);
    private static readonly Brush DeadLeaf   = Frozen(0x7A, 0x6A, 0x58);
    private static readonly Brush DeadLeaf2  = Frozen(0x6B, 0x5B, 0x4B);
    private static readonly Brush DeadDetail = Frozen(0x8A, 0x7A, 0x68);
    private static readonly Brush Soil       = Frozen(0x4A, 0x36, 0x26);

    private readonly Canvas _plant = new() { Width = 100, Height = 100 };
    private readonly ScaleTransform _scale = new();

    /// <summary>Every shape with its living and dead colour. Details only show once it's grown a bit.</summary>
    private readonly List<(Shape Shape, Brush Alive, Brush Dead, bool Detail)> _parts = new();

    private TreeKind _kind = TreeKind.Oak;
    private double _growth = 1;
    private bool _dead;

    public TreeShape()
    {
        Width  = 100;
        Height = 100;

        var soil = new Ellipse { Width = 64, Height = 12, Fill = Soil };
        SetLeft(soil, 18);
        SetTop(soil, 84);
        Children.Add(soil);

        // The plant grows from its base, just above the soil.
        _plant.RenderTransformOrigin = new Point(0.5, 0.9);
        _plant.RenderTransform = _scale;
        Children.Add(_plant);

        Build();
        Apply();
    }

    public TreeKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Build();
            Apply();
        }
    }

    /// <param name="growth">0 = just planted, 1 = fully grown.</param>
    public void Set(double growth, bool dead)
    {
        _growth = Math.Clamp(growth, 0, 1);
        _dead   = dead;
        Apply();
    }

    private void Apply()
    {
        double s = 0.3 + 0.7 * _growth;
        _scale.ScaleX = s;
        _scale.ScaleY = s;

        bool sprout = _growth < 0.25;
        foreach (var (shape, alive, dead, detail) in _parts)
        {
            shape.Fill       = _dead ? dead : alive;
            shape.Visibility = detail && sprout ? Visibility.Hidden : Visibility.Visible;
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────
    private void Build()
    {
        _plant.Children.Clear();
        _parts.Clear();

        switch (_kind)
        {
            case TreeKind.Pine:   BuildPine();   break;
            case TreeKind.Cherry: BuildCherry(); break;
            case TreeKind.Cactus: BuildCactus(); break;
            case TreeKind.Orange: BuildOrange(); break;
            case TreeKind.Bamboo: BuildBamboo(); break;
            default:              BuildOak();    break;
        }
    }

    private void BuildOak()
    {
        Trunk(45, 48, 10, 42, Frozen(0x8B, 0x5A, 0x2B));
        Blob(20, 26, 38, 36, Frozen(0x28, 0xA7, 0x45), detail: true);
        Blob(42, 26, 38, 36, Frozen(0x28, 0xA7, 0x45), detail: true);
        Blob(29, 8, 42, 40, Frozen(0x34, 0xC7, 0x59));
    }

    private void BuildPine()
    {
        Trunk(46, 68, 8, 22, Frozen(0x7A, 0x4E, 0x2A));
        Triangle(new Point(18, 76), new Point(82, 76), new Point(50, 38), Frozen(0x1E, 0x7F, 0x4F), detail: true);
        Triangle(new Point(25, 58), new Point(75, 58), new Point(50, 22), Frozen(0x23, 0x91, 0x5A), detail: true);
        Triangle(new Point(32, 40), new Point(68, 40), new Point(50, 6),  Frozen(0x2B, 0xA5, 0x68));
    }

    private void BuildCherry()
    {
        Trunk(45, 50, 10, 40, Frozen(0x7A, 0x4A, 0x2E));
        Blob(18, 26, 40, 36, Frozen(0xF0, 0x62, 0x92), detail: true);
        Blob(42, 26, 40, 36, Frozen(0xF4, 0x8F, 0xB1), detail: true);
        Blob(28, 6, 44, 42, Frozen(0xF8, 0xBB, 0xD0));
        foreach (var (x, y) in new[] { (36.0, 18.0), (58.0, 30.0), (28.0, 42.0), (64.0, 46.0) })
            Blob(x, y, 6, 6, Frozen(0xFF, 0xF0, 0xF5), detail: true, dead: DeadDetail);
    }

    private void BuildCactus()
    {
        var body = Frozen(0x43, 0xA0, 0x47);
        var arm  = Frozen(0x38, 0x8E, 0x3C);
        Rounded(39, 30, 22, 60, 11, body);
        Rounded(24, 40, 10, 26, 5, arm, detail: true);    // left arm
        Rounded(28, 58, 14, 8, 4, arm, detail: true);
        Rounded(66, 34, 10, 24, 5, arm, detail: true);    // right arm
        Rounded(58, 48, 12, 8, 4, arm, detail: true);
        Blob(44, 22, 12, 10, Frozen(0xFF, 0x6B, 0x9A), detail: true, dead: DeadDetail);   // flower
    }

    private void BuildOrange()
    {
        Trunk(45, 50, 10, 40, Frozen(0x8B, 0x5A, 0x2B));
        Blob(20, 26, 38, 36, Frozen(0x23, 0x7A, 0x37), detail: true);
        Blob(42, 26, 38, 36, Frozen(0x23, 0x7A, 0x37), detail: true);
        Blob(29, 8, 42, 40, Frozen(0x2E, 0x8B, 0x3E));
        foreach (var (x, y) in new[] { (32.0, 30.0), (56.0, 20.0), (62.0, 42.0), (40.0, 48.0), (46.0, 30.0) })
            Blob(x, y, 9, 9, Frozen(0xFF, 0x9F, 0x1C), detail: true, dead: DeadDetail);
    }

    private void BuildBamboo()
    {
        var stalk = Frozen(0x8B, 0xC3, 0x4A);
        var stalk2 = Frozen(0x7C, 0xB3, 0x42);
        var node  = Frozen(0x55, 0x8B, 0x2F);
        var leaf  = Frozen(0x66, 0xBB, 0x6A);

        Rounded(46, 12, 8, 78, 3, stalk);
        Rounded(33, 32, 7, 58, 3, stalk2, detail: true);
        Rounded(60, 40, 7, 50, 3, stalk2, detail: true);

        foreach (var y in new[] { 30.0, 50.0, 70.0 }) Rounded(45, y, 10, 2.5, 1, node);
        foreach (var y in new[] { 52.0, 72.0 })       Rounded(32, y, 9, 2.5, 1, node, detail: true);
        foreach (var y in new[] { 58.0, 76.0 })       Rounded(59, y, 9, 2.5, 1, node, detail: true);

        Leaf(54, 16, 18, 6, -25, leaf);
        Leaf(30, 22, 18, 6, 25, leaf, detail: true);
        Leaf(64, 34, 16, 6, -20, leaf, detail: true);
        Leaf(22, 40, 14, 5, 20, leaf, detail: true);
    }

    // ── Shape helpers ─────────────────────────────────────────────────
    private void Trunk(double x, double y, double w, double h, Brush alive) =>
        Rounded(x, y, w, h, 4, alive, dead: DeadWood);

    private void Blob(double x, double y, double w, double h, Brush alive, bool detail = false, Brush? dead = null) =>
        Add(new Ellipse { Width = w, Height = h }, x, y, alive, dead ?? (detail ? DeadLeaf2 : DeadLeaf), detail);

    private void Rounded(double x, double y, double w, double h, double r, Brush alive, bool detail = false, Brush? dead = null) =>
        Add(new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r }, x, y, alive, dead ?? DeadLeaf, detail);

    private void Triangle(Point a, Point b, Point c, Brush alive, bool detail = false) =>
        Add(new Polygon { Points = new PointCollection { a, b, c } }, 0, 0, alive, detail ? DeadLeaf2 : DeadLeaf, detail);

    private void Leaf(double x, double y, double w, double h, double angle, Brush alive, bool detail = false)
    {
        var e = new Ellipse
        {
            Width = w, Height = h,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(angle)
        };
        Add(e, x, y, alive, DeadLeaf2, detail);
    }

    private void Add(Shape shape, double x, double y, Brush alive, Brush dead, bool detail)
    {
        SetLeft(shape, x);
        SetTop(shape, y);
        _plant.Children.Add(shape);
        _parts.Add((shape, alive, dead, detail));
    }

    // ── Image for the notification island ─────────────────────────────
    /// <summary>The tree on a round background, as an image.</summary>
    public static BitmapSource Render(double growth, bool dead, TreeKind kind = TreeKind.Oak)
    {
        const int size = 104;
        var tree = new TreeShape
        {
            Kind                = kind,
            LayoutTransform     = new ScaleTransform(0.85, 0.85),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        tree.Set(growth, dead);

        var root = new Grid { Width = size, Height = size };
        root.Children.Add(new Ellipse { Fill = dead ? Frozen(0x3A, 0x2E, 0x26) : Frozen(0x1F, 0x3A, 0x2A) });
        root.Children.Add(tree);

        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
