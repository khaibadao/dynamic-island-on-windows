namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>
/// Seek bar whose played part is a gently moving wave and the rest a thin
/// straight line, like the Android 13 / iOS media player. The wave flattens
/// when paused. Click or drag to seek.
/// </summary>
public sealed class WavySeekBar : FrameworkElement
{
    private const double Amplitude  = 3.0;   // px, half the wave's height
    private const double Wavelength = 22.0;  // px
    private const double Speed      = 28.0;  // px per second the wave travels

    private double _phase;
    private double _amp;            // current amplitude: eases to Amplitude when playing, 0 when paused
    private TimeSpan _lastFrame;
    // 30 frames a second from a plain timer: hooking every screen frame instead made
    // WPF redraw the whole (large, see-through) notch window non-stop while music played.
    private readonly System.Windows.Threading.DispatcherTimer _frameTimer = new(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };   // 60 a second
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private bool _animating;
    private bool _dragging;

    public WavySeekBar()
    {
        Height = 18;
        Cursor = Cursors.Hand;
        Focusable = false;
        _frameTimer.Tick += (s, e) => OnFrame(s, e);
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    // ── Values ────────────────────────────────────────────────────────
    public double Duration { get; set; }                 // seconds
    private double _position;
    public double Position                               // seconds
    {
        get => _position;
        set { if (!_dragging) { _position = value; InvalidateVisual(); } }
    }

    private bool _playing;
    public bool IsPlaying
    {
        get => _playing;
        set { _playing = value; UpdateAnimation(); }
    }

    private Brush _accent = Brushes.White;
    public Brush Accent
    {
        get => _accent;
        set { _accent = value; InvalidateVisual(); }
    }

    public bool IsDragging => _dragging;

    /// <summary>The user started dragging.</summary>
    public event Action? SeekStarted;
    /// <summary>While dragging: the time under the pointer (seconds).</summary>
    public event Action<double>? SeekPreview;
    /// <summary>Released: seek here (seconds).</summary>
    public event Action<double>? SeekCommitted;

    // ── Drawing ───────────────────────────────────────────────────────
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, mid = h / 2;
        if (w <= 0) return;

        // Hit area
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        double frac = Duration > 0 ? Math.Clamp(_position / Duration, 0, 1) : 0;
        double x = frac * w;

        // Remaining part: thin straight line
        var rest = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 2.5)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (x < w - 1) dc.DrawLine(rest, new Point(Math.Min(x + 4, w), mid), new Point(w, mid));

        // Played part: wave
        if (x > 1)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, mid + Wave(0)), false, false);
                for (double px = 2; px <= x; px += 2)
                    ctx.LineTo(new Point(px, mid + Wave(px)), true, true);
                ctx.LineTo(new Point(x, mid + Wave(x)), true, true);
            }
            geo.Freeze();
            var pen = new Pen(_accent, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
        }

        // Knob
        double r = IsMouseOver || _dragging ? 7 : 5.5;
        dc.DrawEllipse(Brushes.White, null, new Point(Math.Clamp(x, r, w - r), mid), r, r);
    }

    private double Wave(double px) => _amp * Math.Sin((px - _phase) / Wavelength * 2 * Math.PI);

    // ── Wave animation ────────────────────────────────────────────────
    private void UpdateAnimation()
    {
        bool want = IsVisible && (_playing || _amp > 0.05);
        if (want && !_animating)
        {
            _animating = true;
            _lastFrame = TimeSpan.Zero;
            _frameClock.Restart();
            _frameTimer.Start();
        }
        else if (!want && _animating)
        {
            _animating = false;
            _frameTimer.Stop();
            InvalidateVisual();
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _frameClock.Elapsed;
        double dt = _lastFrame == TimeSpan.Zero ? 0 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        if (dt <= 0 || dt > 0.25) dt = 1.0 / 60;

        if (_playing) _phase += Speed * dt;
        double target = _playing ? Amplitude : 0;
        _amp += (target - _amp) * Math.Min(1, dt * 6);   // ease in / out of the wave

        InvalidateVisual();
        if (!_playing && _amp < 0.05) { _amp = 0; UpdateAnimation(); }
    }

    // ── Mouse ─────────────────────────────────────────────────────────
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Duration <= 0) return;
        _dragging = true;
        CaptureMouse();
        SeekStarted?.Invoke();
        MoveTo(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) MoveTo(e.GetPosition(this).X);
        else InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        SeekCommitted?.Invoke(_position);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    private void MoveTo(double x)
    {
        double w = Math.Max(1, ActualWidth);
        _position = Math.Clamp(x / w, 0, 1) * Duration;
        SeekPreview?.Invoke(_position);
        InvalidateVisual();
    }
}
