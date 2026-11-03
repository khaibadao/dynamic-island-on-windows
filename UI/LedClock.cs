namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// Digits drawn like a seven-segment LED clock ("18:28"). Unlit segments
/// show faintly behind the lit ones, like a real LED display. Every digit
/// takes the same width, so the clock never changes size.
/// </summary>
public sealed class LedClock : FrameworkElement
{
    // Which segments light up for each digit: a (top) b c d e f g (middle)
    private static readonly string[] Lit =
        { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

    private string _text = "00:00";
    private Brush _on = Brushes.White;
    private Brush? _off;
    private double _digitHeight = 14;

    public string Text
    {
        get => _text;
        set { if (value == _text) return; _text = value ?? ""; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>Colour of the lit segments.</summary>
    public Brush On { get => _on; set { _on = value; InvalidateVisual(); } }

    /// <summary>Colour of the unlit segments (null = hidden).</summary>
    public Brush? Off { get => _off; set { _off = value; InvalidateVisual(); } }

    public double DigitHeight
    {
        get => _digitHeight;
        set { _digitHeight = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    private double DigitWidth => _digitHeight * 0.55;
    private double Thick      => _digitHeight * 0.17;
    private double Advance    => DigitWidth + Thick * 0.75;
    private double ColonWidth => Thick * 1.8;

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 0;
        foreach (var ch in _text) w += ch == ':' ? ColonWidth : Advance;
        if (w > 0 && _text.Length > 0 && _text[^1] != ':') w -= Thick * 0.75;   // no gap after the last digit
        return new Size(Math.Max(0, w), _digitHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double x = 0, h = _digitHeight, t = Thick;
        foreach (var ch in _text)
        {
            if (ch == ':')
            {
                double r = t * 0.55;
                dc.DrawEllipse(_on, null, new Point(x + t * 0.9, h * 0.3), r, r);
                dc.DrawEllipse(_on, null, new Point(x + t * 0.9, h * 0.72), r, r);
                x += ColonWidth;
                continue;
            }
            int d = ch - '0';
            string lit = d is >= 0 and <= 9 ? Lit[d] : "";
            DrawDigit(dc, x, lit);
            x += Advance;
        }
    }

    private void DrawDigit(DrawingContext dc, double x, string lit)
    {
        double w = DigitWidth, h = _digitHeight, t = Thick, g = t / 2, gap = t * 0.16;
        var segments = new (char Name, Point[] Points)[]
        {
            ('a', Horizontal(x + w / 2, g,         w - 2 * gap, g)),
            ('g', Horizontal(x + w / 2, h / 2,     w - 2 * gap, g)),
            ('d', Horizontal(x + w / 2, h - g,     w - 2 * gap, g)),
            ('f', Vertical  (x + g,     h / 4,     h / 2 - 2 * gap, g)),
            ('b', Vertical  (x + w - g, h / 4,     h / 2 - 2 * gap, g)),
            ('e', Vertical  (x + g,     3 * h / 4, h / 2 - 2 * gap, g)),
            ('c', Vertical  (x + w - g, 3 * h / 4, h / 2 - 2 * gap, g)),
        };
        foreach (var (name, pts) in segments)
        {
            var brush = lit.IndexOf(name) >= 0 ? _on : _off;
            if (brush == null) continue;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], false, false);
            }
            geo.Freeze();
            dc.DrawGeometry(brush, null, geo);
        }
    }

    // A segment is a long hexagon with pointed ends.
    private static Point[] Horizontal(double cx, double cy, double len, double g) => new[]
    {
        new Point(cx - len / 2, cy), new Point(cx - len / 2 + g, cy - g), new Point(cx + len / 2 - g, cy - g),
        new Point(cx + len / 2, cy), new Point(cx + len / 2 - g, cy + g), new Point(cx - len / 2 + g, cy + g)
    };

    private static Point[] Vertical(double cx, double cy, double len, double g) => new[]
    {
        new Point(cx, cy - len / 2), new Point(cx + g, cy - len / 2 + g), new Point(cx + g, cy + len / 2 - g),
        new Point(cx, cy + len / 2), new Point(cx - g, cy + len / 2 - g), new Point(cx - g, cy - len / 2 + g)
    };
}
