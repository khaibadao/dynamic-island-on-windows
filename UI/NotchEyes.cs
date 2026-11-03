namespace DynamicIslandNotifier.UI;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// A pair of eyes that now and then replaces the clock on the notch, with
/// expressions (blink, look around, happy, wink, surprised, love, sleepy,
/// angry, sad, suspicious, dizzy), or follows the pointer. Eyes are white
/// rounded blocks; black "lids" cut them into shapes, like the Cozmo robot.
/// </summary>
public sealed class NotchEyes : FrameworkElement
{
    // ── One eye at a moment in time ───────────────────────────────────
    private record struct Eye(double Lx = 0, double Ly = 0, double Sx = 1, double Sy = 1,
                              double Top = 0, double Bot = 0, double Tilt = 0,
                              bool Happy = false, bool Heart = false, double Hs = 1, bool Cross = false);

    private enum Ease { InOut, Out, Back, Linear }

    private sealed record Key(double T, Eye L, Eye? R = null, Ease E = Ease.InOut, double Rot = 0, double Ly = 0);

    private sealed record Expression(string Name, Key[] Keys, bool Bob = false, bool Zzz = false, bool Sweat = false, bool Tremble = false);

    private static readonly Eye N = new();
    private static Eye Closed(double sx = 1.12) => new(Top: 1, Sx: sx, Sy: 0.85);
    private static readonly Eye Arc = new(Bot: 0.58, Happy: true);

    /// <summary>All expressions, about 5.5 s each (times in seconds).</summary>
    private static readonly Dictionary<string, Expression> All = new()
    {
        ["blink"] = new("Chớp mắt", new Key[]
        {
            new(0, N), new(0.7, new(Lx: -2.5)), new(1.3, new(Lx: -2.5)),
            new(1.38, Closed() with { Lx: -2 }, E: Ease.Linear), new(1.5, new(Lx: -1.5), E: Ease.Out),
            new(2.3, new(Lx: 2.5)), new(3.1, new(Lx: 2.5)),
            new(3.18, Closed() with { Lx: 2 }, E: Ease.Linear), new(3.3, new(Lx: 2)), new(3.42, Closed() with { Lx: 2 }, E: Ease.Linear),
            new(3.56, new(Lx: 2), E: Ease.Out), new(4.4, N), new(5.5, N)
        }),
        ["look"] = new("Liếc trái, liếc phải", new Key[]
        {
            new(0, N), new(0.55, new(Lx: -4.5, Sx: 0.88, Ly: 0.4), E: Ease.Back), new(1.6, new(Lx: -4.5, Sx: 0.88, Ly: 0.4)),
            new(1.72, new(Lx: -1, Top: 1, Sy: 0.85), E: Ease.Linear), new(1.95, new(Lx: 4.5, Sx: 0.88, Ly: 0.4), E: Ease.Back),
            new(3.0, new(Lx: 4.5, Sx: 0.88, Ly: 0.4)), new(3.5, new(Lx: 2.5, Ly: -1.8, Sy: 0.95)), new(4.3, new(Lx: 2.5, Ly: -1.8, Sy: 0.95)),
            new(4.9, N), new(5.5, N)
        }),
        ["happy"] = new("Vui", new Key[]
        {
            new(0, N), new(0.12, new(Sy: 0.8, Sx: 1.1), E: Ease.Out), new(0.45, Arc with { Ly: -1 }, E: Ease.Back),
            new(2.4, Arc with { Ly: -1 }), new(2.6, Arc with { Bot: 0.7, Ly: -1.5 }), new(2.9, Arc with { Ly: -1 }, E: Ease.Back),
            new(4.6, Arc with { Ly: -1 }), new(5.1, N), new(5.5, N)
        }, Bob: true),
        ["wink"] = new("Nháy mắt", new Key[]
        {
            new(0, N, N), new(0.5, new(Top: 0.12), new(Top: 0.12), Rot: 0),
            new(0.75, new(Top: 0.18, Sy: 1.05), new(Sy: 0.75, Sx: 1.1), E: Ease.Out, Rot: -7, Ly: -0.5),
            new(0.95, new(Top: 0.18, Sy: 1.05), Arc with { Bot: 0.62, Sx: 1.08 }, E: Ease.Back, Rot: -8, Ly: -0.5),
            new(2.1, new(Top: 0.18, Sy: 1.05), Arc with { Bot: 0.62, Sx: 1.08 }, Rot: -8, Ly: -0.5),
            new(2.4, N, N, E: Ease.Back, Rot: 0),
            new(3.3, N, N), new(3.45, N, Arc with { Bot: 0.62 }, E: Ease.Out, Rot: -4),
            new(3.9, N, Arc with { Bot: 0.62 }, Rot: -4), new(4.15, N, N, E: Ease.Back, Rot: 0), new(5.5, N, N)
        }),
        ["surprise"] = new("Ngạc nhiên", new Key[]
        {
            new(0, N), new(0.18, new(Sx: 1.15, Sy: 0.7, Ly: 1), E: Ease.Out),                  // gathers itself…
            new(0.5, new(Sx: 1.32, Sy: 1.4, Ly: -1.2), E: Ease.Back),                           // …and pops wide open
            new(1.8, new(Sx: 1.3, Sy: 1.38, Ly: -1.2)),
            new(1.88, new(Sx: 1.35, Sy: 0.15, Top: 1, Ly: -1), E: Ease.Linear), new(2.0, new(Sx: 1.3, Sy: 1.38, Ly: -1.2), E: Ease.Out),
            new(2.3, new(Sx: 1.3, Sy: 1.38, Ly: -1.2, Lx: -1.5)), new(2.7, new(Sx: 1.3, Sy: 1.38, Ly: -1.2, Lx: 1.5)),
            new(3.2, new(Sx: 1.25, Sy: 1.3, Ly: -1)), new(4.6, new(Sx: 1.05, Sy: 1.05)), new(5.5, N)
        }, Tremble: true),
        ["love"] = new("Mắt trái tim", new Key[]
        {
            new(0, new(Heart: true, Hs: 0.2)), new(0.4, new(Heart: true, Hs: 1.1), E: Ease.Back),
            new(0.8, new(Heart: true, Hs: 0.9)), new(1.0, new(Heart: true, Hs: 1.15), E: Ease.Out),
            new(1.4, new(Heart: true, Hs: 0.9)), new(1.6, new(Heart: true, Hs: 1.15), E: Ease.Out),
            new(2.2, new(Heart: true, Hs: 0.9)), new(2.4, new(Heart: true, Hs: 1.15), E: Ease.Out),
            new(3.0, new(Heart: true, Hs: 0.9)), new(3.2, new(Heart: true, Hs: 1.15), E: Ease.Out),
            new(4.6, new(Heart: true, Hs: 1)), new(5.2, new(Heart: true, Hs: 0.2)), new(5.5, N)
        }, Bob: true),
        ["sleepy"] = new("Buồn ngủ", new Key[]
        {
            new(0, N), new(0.8, new(Top: 0.55, Ly: 1)), new(1.6, new(Top: 0.72, Ly: 1.5)),
            new(2.3, new(Top: 1, Ly: 2, Sx: 1.08)), new(3.2, new(Top: 1, Ly: 2.2, Sx: 1.08)),
            new(3.35, new(Top: 0.1, Ly: -0.5, Sy: 1.1), E: Ease.Out),                           // jolts awake
            new(3.9, new(Top: 0.35, Ly: 0.5)), new(4.8, new(Top: 0.75, Ly: 1.5)), new(5.5, N)
        }, Zzz: true),
        ["angry"] = new("Hờn dỗi", new Key[]
        {
            new(0, N), new(0.4, new(Top: 0.4, Tilt: 20, Sy: 0.92), E: Ease.Back),
            new(1.4, new(Top: 0.4, Tilt: 20, Sy: 0.92)), new(1.5, new(Top: 0.42, Tilt: 22, Sy: 0.92, Lx: -0.8), E: Ease.Linear),
            new(1.6, new(Top: 0.42, Tilt: 22, Sy: 0.92, Lx: 0.8), E: Ease.Linear), new(1.7, new(Top: 0.42, Tilt: 22, Sy: 0.92, Lx: -0.8), E: Ease.Linear),
            new(1.8, new(Top: 0.42, Tilt: 22, Sy: 0.92), E: Ease.Linear),
            new(2.6, new(Top: 0.4, Tilt: 20, Sy: 0.92, Lx: -3)), new(4.0, new(Top: 0.4, Tilt: 20, Sy: 0.92, Lx: -3)),
            new(4.9, N), new(5.5, N)
        }),
        ["sad"] = new("Buồn", new Key[]
        {
            new(0, N), new(0.7, new(Top: 0.34, Tilt: -18, Ly: 1.2, Sy: 0.92)),
            new(2.0, new(Top: 0.36, Tilt: -18, Ly: 1.8, Sy: 0.9, Lx: -1)),
            new(2.4, new(Top: 1, Tilt: -18, Ly: 2, Sy: 0.9), E: Ease.InOut), new(2.9, new(Top: 0.36, Tilt: -18, Ly: 1.8, Sy: 0.9)),
            new(4.5, new(Top: 0.34, Tilt: -18, Ly: 1.5, Sy: 0.92, Lx: 1)), new(5.2, N), new(5.5, N)
        }),
        ["suspicious"] = new("Nghi ngờ", new Key[]
        {
            new(0, N, N), new(0.5, new(Top: 0.5, Bot: 0.15, Lx: 3), new(Top: 0.12, Lx: 3), E: Ease.Back),
            new(1.6, new(Top: 0.5, Bot: 0.15, Lx: 3), new(Top: 0.12, Lx: 3)),
            new(2.6, new(Top: 0.5, Bot: 0.15, Lx: -3), new(Top: 0.12, Lx: -3)),
            new(3.6, new(Top: 0.5, Bot: 0.15, Lx: -3), new(Top: 0.12, Lx: -3)),
            new(4.2, new(Top: 0.5, Bot: 0.15, Lx: 3.5), new(Top: 0.12, Lx: 3.5), E: Ease.Out),
            new(4.9, N, N), new(5.5, N, N)
        }),
        ["dizzy"] = new("Chóng mặt", new Key[]
        {
            new(0, N), new(0.3, new(Cross: true), E: Ease.Out, Rot: 8), new(1.2, new(Cross: true), Rot: -8),
            new(2.1, new(Cross: true), Rot: 8), new(3.0, new(Cross: true), Rot: -8), new(3.9, new(Cross: true), Rot: 6),
            new(4.7, new(Cross: true), Rot: 0), new(5.1, N), new(5.5, N)
        }, Sweat: true),
    };

    /// <summary>Random expressions for idle moments (sad and love are kept for real events).</summary>
    public static readonly string[] Idle = { "blink", "look", "happy", "wink", "surprise", "suspicious", "angry", "dizzy" };

    // ── State ─────────────────────────────────────────────────────────
    private Expression? _expr;
    private TimeSpan _start, _now;
    private double _duration = 5.5;
    private bool _following;
    private Point _target, _look;           // where the eyes look while following (-1..1)
    private double _alpha;                  // fades in and out
    private bool _rendering;

    private static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Brush HeartBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x7A)));
    private static readonly Brush SweatBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8E, 0xC5, 0xE8)));
    private static readonly Brush ZBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xFF)));
    private static readonly Geometry HeartShape = Geometry.Parse("M0,6 C-11,-1 -7,-10 0,-4 C7,-10 11,-1 0,6 Z");

    public NotchEyes()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    /// <summary>True while an expression plays or the eyes follow the pointer.</summary>
    public bool Active => _expr != null || _following;
    public bool Playing => _expr != null;

    /// <summary>The expression finished and the eyes have faded out.</summary>
    public event Action? Finished;

    /// <summary>Plays an expression (see <see cref="Idle"/>, or "love", "sad", "sleepy").</summary>
    public void Play(string name)
    {
        if (!All.TryGetValue(name, out var e)) return;
        _following = false;
        _expr = e;
        _duration = e.Keys[^1].T;
        _start = TimeSpan.Zero;   // set on the next frame
        StartRendering();
    }

    /// <summary>Look towards this point (-1..1 on each axis); null stops following.</summary>
    public void Follow(Point? where)
    {
        if (where is { } p)
        {
            if (_expr != null) return;   // an expression is playing: let it finish
            _target = new Point(Math.Clamp(p.X, -1, 1), Math.Clamp(p.Y, -1, 1));
            if (!_following) { _following = true; StartRendering(); }
        }
        else if (_following)
        {
            _following = false;   // fades out, then Finished
        }
    }

    public void Stop()
    {
        _expr = null;
        _following = false;
        _alpha = 0;
        StopRendering();
        InvalidateVisual();
    }

    // ── Animation loop ────────────────────────────────────────────────
    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _now == TimeSpan.Zero ? 1.0 / 60 : Math.Clamp((t - _now).TotalSeconds, 0, 0.1);
        _now = t;
        if (_expr != null && _start == TimeSpan.Zero) _start = t;

        bool wantVisible;
        if (_expr != null)
        {
            double el = (t - _start).TotalSeconds;
            wantVisible = el < _duration - 0.35;
            if (el >= _duration) _expr = null;
        }
        else wantVisible = _following;

        _alpha = Math.Clamp(_alpha + (wantVisible ? dt : -dt) / 0.3, 0, 1);
        _look = new Point(_look.X + (_target.X - _look.X) * Math.Min(1, dt * 10),
                          _look.Y + (_target.Y - _look.Y) * Math.Min(1, dt * 10));
        InvalidateVisual();

        if (!wantVisible && _alpha <= 0 && _expr == null)
        {
            StopRendering();
            Finished?.Invoke();
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────
    protected override void OnRender(DrawingContext dc)
    {
        if (_alpha <= 0) return;
        double w = ActualWidth, h = ActualHeight;
        double cy = h / 2, eyeW = h * 0.42, eyeH = h * 0.56, gap = h * 0.62;

        Eye l, r; double rot = 0, ly = 0;
        if (_expr is { } ex)
        {
            double el = (_now - _start).TotalSeconds;
            (l, r, rot, ly) = Sample(ex, el);
            if (ex.Bob) ly += Math.Sin(el * Math.PI * 3.2) * h * 0.035;
            if (ex.Tremble && el > 0.5 && el < 1.8) l = l with { Lx = l.Lx + Math.Sin(el * 90) * 0.25 };
            if (ex.Tremble && el > 0.5 && el < 1.8) r = r with { Lx = r.Lx + Math.Sin(el * 90) * 0.25 };
        }
        else
        {
            // Following the pointer: shift and squeeze a little towards it
            var f = new Eye(Lx: _look.X * 4.5, Ly: _look.Y * 2, Sx: 1 - Math.Abs(_look.X) * 0.1);
            l = r = f;
        }

        // Small "breathing" so the eyes never look frozen
        double breathe = 1 + Math.Sin(_now.TotalSeconds * 2.1) * 0.025;

        double scale = h / 24;   // keyframes are written for a 24 px tall face
        dc.PushOpacity(_alpha);
        dc.PushTransform(new RotateTransform(rot, w / 2, cy));
        dc.PushTransform(new TranslateTransform(0, ly * scale));
        DrawEye(dc, w / 2 - gap / 2 - eyeW / 2, cy, eyeW, eyeH * breathe, scale, l, left: true);
        DrawEye(dc, w / 2 + gap / 2 + eyeW / 2, cy, eyeW, eyeH * breathe, scale, r, left: false);
        dc.Pop();
        dc.Pop();

        if (_expr is { } x2)
        {
            double el = (_now - _start).TotalSeconds;
            if (x2.Zzz && el > 1.6 && el < 3.3)
            {
                double p = ((el - 1.6) / 0.85) % 1;
                var ft = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), h * 0.3, ZBrush, 1.0);
                dc.PushOpacity(1 - p);
                dc.DrawText(ft, new Point(w / 2 + gap / 2 + eyeW, h * 0.15 - p * h * 0.2));
                dc.Pop();
            }
            if (x2.Sweat)
            {
                double sx = w / 2 + gap / 2 + eyeW * 1.1, sy = h * 0.12;
                var drop = Geometry.Parse($"M{sx:0.##},{sy:0.##} q-1.8,3 0,4 q1.8,-1 0,-4 z");
                dc.DrawGeometry(SweatBrush, null, drop);
            }
        }
        dc.Pop();
    }

    private static void DrawEye(DrawingContext dc, double cx, double cy, double baseW, double baseH, double k, Eye e, bool left)
    {
        cx += e.Lx * k;
        cy += e.Ly * k;
        if (e.Heart)
        {
            double s = 0.075 * baseH * e.Hs;
            dc.PushTransform(new TransformGroup { Children = { new ScaleTransform(s, s), new TranslateTransform(cx, cy) } });
            dc.DrawGeometry(HeartBrush, null, HeartShape);
            dc.Pop();
            return;
        }
        if (e.Cross)
        {
            double c = baseW * 0.42;
            var pen = new Pen(White, baseW * 0.26) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(cx - c, cy - c), new Point(cx + c, cy + c));
            dc.DrawLine(pen, new Point(cx + c, cy - c), new Point(cx - c, cy + c));
            return;
        }

        double w = baseW * e.Sx, h = baseH * e.Sy;
        var box = new Rect(cx - w / 2, cy - h / 2, w, h);
        double radius = Math.Min(baseW * 0.36, h / 2);
        dc.PushClip(new RectangleGeometry(new Rect(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2)));
        dc.DrawRoundedRectangle(White, null, box, radius, radius);

        // Lids (black): top lid can tilt for angry (+) / sad (−) brows
        if (e.Top > 0)
        {
            double tilt = e.Tilt * (left ? 1 : -1), edge = box.Y + h * Math.Min(1, e.Top);
            dc.PushTransform(new RotateTransform(tilt, cx, edge));
            dc.DrawRectangle(Brushes.Black, null, new Rect(box.X - w, edge - h * 2, w * 3, h * 2));
            dc.Pop();
        }
        if (e.Bot > 0)
        {
            if (e.Happy)   // a big black curve from below turns the eye into a "^" arc
                dc.DrawEllipse(Brushes.Black, null, new Point(cx, box.Bottom + h * 0.45 - h * e.Bot), w * 0.8, h * 0.62);
            else
                dc.DrawRectangle(Brushes.Black, null, new Rect(box.X - w, box.Bottom - h * e.Bot, w * 3, h * 2));
        }
        dc.Pop();
    }

    // ── Keyframes ─────────────────────────────────────────────────────
    private static (Eye L, Eye R, double Rot, double Ly) Sample(Expression ex, double t)
    {
        var keys = ex.Keys;
        int i = 0;
        while (i < keys.Length - 2 && t > keys[i + 1].T) i++;
        var a = keys[i];
        var b = keys[Math.Min(i + 1, keys.Length - 1)];
        double span = Math.Max(1e-6, b.T - a.T);
        double k = Apply(b.E, Math.Clamp((t - a.T) / span, 0, 1));
        var l = Mix(a.L, b.L, k);
        var r = Mix(a.R ?? a.L, b.R ?? b.L, k);
        return (l, r, Lerp(a.Rot, b.Rot, k), Lerp(a.Ly, b.Ly, k));
    }

    private static double Apply(Ease e, double t) => e switch
    {
        Ease.Linear => t,
        Ease.Out    => 1 - (1 - t) * (1 - t),
        Ease.Back   => 1 + 2.2 * Math.Pow(t - 1, 3) + 1.2 * Math.Pow(t - 1, 2),   // overshoots a little, then settles
        _           => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2
    };

    private static Eye Mix(Eye a, Eye b, double k) => new(
        Lerp(a.Lx, b.Lx, k), Lerp(a.Ly, b.Ly, k), Lerp(a.Sx, b.Sx, k), Lerp(a.Sy, b.Sy, k),
        Lerp(a.Top, b.Top, k), Lerp(a.Bot, b.Bot, k), Lerp(a.Tilt, b.Tilt, k),
        a.Happy || b.Happy, k < 0.5 ? a.Heart : b.Heart, Lerp(a.Hs, b.Hs, k), k < 0.5 ? a.Cross : b.Cross);

    private static double Lerp(double a, double b, double k) => a + (b - a) * k;

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
