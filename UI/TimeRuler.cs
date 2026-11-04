namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>
/// A ruler you drag sideways to pick a duration (5 min – 3 h, steps of 5),
/// like a tape measure under a fixed marker. Mouse wheel and arrow keys work too.
/// </summary>
public sealed class TimeRuler : FrameworkElement
{
    private const double Step = 10;          // px per 5 minutes
    private int _minutes = 25;
    private double _dragX, _dragOffset;
    private bool _dragging;
    private double _offset;                  // smooth position (minutes) while gliding

    public int Min { get; set; } = 5;
    public int Max { get; set; } = 180;

    public event Action<int>? Changed;

    public TimeRuler()
    {
        Height = 34;
        Cursor = Cursors.SizeWE;
        Focusable = false;
        _offset = _minutes;
    }

    public int Minutes
    {
        get => _minutes;
        set
        {
            int v = Math.Clamp((int)Math.Round(value / 5.0) * 5, Min, Max);
            if (v == _minutes) return;
            _minutes = v;
            if (!_dragging) _offset = v;
            InvalidateVisual();
            Changed?.Invoke(v);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), null, new Rect(0, 0, w, h), 10, 10);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 10, 10));

        double center = w / 2;
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1.6);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), 2);
        for (int m = Min; m <= Max; m += 5)
        {
            double x = center + (m - _offset) / 5 * Step;
            if (x < -2 || x > w + 2) continue;
            double edge = Math.Min(x, w - x) / 40;                   // fade towards both ends
            bool big = m % 30 == 0;
            dc.PushOpacity(Math.Clamp(edge, 0, 1));
            dc.DrawLine(big ? major : minor, new Point(x, h - 6), new Point(x, h - 6 - (big ? 14 : 7)));
            dc.Pop();
        }
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(center - 1.5, 4, 3, h - 8), 1.5, 1.5);
        dc.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragX = e.GetPosition(this).X;
        _dragOffset = _offset;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _offset = Math.Clamp(_dragOffset - (e.GetPosition(this).X - _dragX) / Step * 5, Min, Max);
        Minutes = (int)Math.Round(_offset);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        _offset = _minutes;     // settle on the nearest 5 minutes
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Minutes = _minutes + (e.Delta > 0 ? 5 : -5);
        e.Handled = true;
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);
}
