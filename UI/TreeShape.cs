namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

/// <summary>
/// A small drawn tree (100×100) that grows from a sprout to a full tree,
/// or turns brown when it died. Scale it with LayoutTransform.
/// </summary>
public sealed class TreeShape : Canvas
{
    private static readonly Brush Soil      = Frozen(0x4A, 0x36, 0x26);
    private static readonly Brush Trunk     = Frozen(0x8B, 0x5A, 0x2B);
    private static readonly Brush LeafTop   = Frozen(0x34, 0xC7, 0x59);
    private static readonly Brush LeafSide  = Frozen(0x28, 0xA7, 0x45);
    private static readonly Brush DeadTrunk = Frozen(0x5A, 0x46, 0x32);
    private static readonly Brush DeadTop   = Frozen(0x7A, 0x6A, 0x58);
    private static readonly Brush DeadSide  = Frozen(0x6B, 0x5B, 0x4B);

    private readonly Canvas _plant = new() { Width = 100, Height = 100 };
    private readonly ScaleTransform _scale = new();
    private readonly Rectangle _trunk = new() { Width = 10, Height = 42, RadiusX = 4, RadiusY = 4 };
    private readonly Ellipse _left  = new() { Width = 38, Height = 36 };
    private readonly Ellipse _right = new() { Width = 38, Height = 36 };
    private readonly Ellipse _top   = new() { Width = 42, Height = 40 };

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

        Add(_trunk, 45, 48);
        Add(_left,  20, 26);
        Add(_right, 42, 26);
        Add(_top,   29, 8);

        Set(1, dead: false);
    }

    private void Add(UIElement element, double x, double y)
    {
        SetLeft(element, x);
        SetTop(element, y);
        _plant.Children.Add(element);
    }

    /// <param name="growth">0 = just planted, 1 = fully grown.</param>
    public void Set(double growth, bool dead)
    {
        growth = Math.Clamp(growth, 0, 1);
        double s = 0.3 + 0.7 * growth;
        _scale.ScaleX = s;
        _scale.ScaleY = s;

        _trunk.Fill = dead ? DeadTrunk : Trunk;
        _top.Fill   = dead ? DeadTop   : LeafTop;
        _left.Fill  = dead ? DeadSide  : LeafSide;
        _right.Fill = dead ? DeadSide  : LeafSide;

        // A sprout has just the one crown.
        var sides = growth < 0.25 ? Visibility.Hidden : Visibility.Visible;
        _left.Visibility  = sides;
        _right.Visibility = sides;
    }

    /// <summary>The tree on a round background, as an image for the notification island.</summary>
    public static BitmapSource Render(double growth, bool dead)
    {
        const int size = 104;
        var tree = new TreeShape
        {
            LayoutTransform     = new ScaleTransform(0.85, 0.85),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        tree.Set(growth, dead);

        var root = new Grid { Width = size, Height = size };
        root.Children.Add(new Ellipse
        {
            Fill = dead ? Frozen(0x3A, 0x2E, 0x26) : Frozen(0x1F, 0x3A, 0x2A)
        });
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
