namespace DynamicIslandNotifier.UI;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

/// <summary>The pets you can choose (tray menu → Thú cưng). The cat is the original Mây drawing.</summary>
public enum PetKind { Cat, Dog, Rabbit, Hamster, Penguin, Fox }

/// <summary>What the pet is doing right now.</summary>
public enum PetState { Sit, Walk, Head, Near, Sleep, Happy, Angry, Sad }

/// <summary>
/// One of the pets other than the cat, drawn front-on in a 48×48 space
/// (ground at y≈44) and moved a little every frame like the preview page:
/// sitting, walking, peeking (head only), reacting to the pointer, sleeping,
/// happy, angry, sad. Only ticks while it's on screen.
/// </summary>
public sealed class PetShape : Canvas
{
    private sealed record Colours(string Fur, string Dark, string Light, string Inner);
    private static Colours Of(PetKind k) => k switch
    {
        PetKind.Dog     => new("#D9A066", "#8B5A2B", "#F6DDB8", "#E58FA0"),
        PetKind.Rabbit  => new("#ECECF2", "#B9B9C6", "#FFFFFF", "#FFB3C4"),
        PetKind.Hamster => new("#E8B97A", "#B7864A", "#FFF3E0", "#F7A9B4"),
        PetKind.Penguin => new("#2B2F3A", "#171A21", "#F4F4F6", "#FF9F2E"),
        PetKind.Fox     => new("#E8742A", "#9A4317", "#FFF5EC", "#3A2216"),
        _               => new("#F5A623", "#D9822B", "#FFE7B3", "#FF9FB0"),
    };

    private readonly Canvas _all = new(), _tail = new(), _body = new(), _head = new(), _earL = new(), _earR = new();
    private readonly Canvas _eyesOpen = new(), _zzz = new();
    private Path _eyesHappy = null!, _eyesClosed = null!, _brow = null!;
    private Canvas? _hands, _seed, _cheeks, _wingL, _wingR;
    private Path? _tongue, _nose, _whiskers;
    private UIElement? _seedGrain, _seedLine, _handL, _handR;

    private readonly DispatcherTimer _tick = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly double _seed;
    private PetKind _kind = PetKind.Dog;
    private PetState _state = PetState.Sit;

    public PetShape(double seed = 0)
    {
        _seed = seed;
        Width = 48;
        Height = 48;
        IsHitTestVisible = false;
        _tick.Tick += (_, _) => Frame();
        IsVisibleChanged += (_, _) => { if (IsVisible) _tick.Start(); else _tick.Stop(); };
        Build();
    }

    public PetKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; Build(); }
    }

    public PetState State
    {
        get => _state;
        set { _state = value; Frame(); }
    }

    // ── Drawing ───────────────────────────────────────────────────────
    private static Brush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    private static Path P(Canvas into, string data, string? fill, string? stroke = null, double width = .7, double opacity = 1)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data), Fill = fill == null ? null : B(fill), Stroke = stroke == null ? null : B(stroke),
            StrokeThickness = width, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacity
        };
        into.Children.Add(p);
        return p;
    }

    private static Ellipse E(Canvas into, double cx, double cy, double rx, double ry, string fill, string? stroke = null, double opacity = 1)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = B(fill), Stroke = stroke == null ? null : B(stroke), StrokeThickness = .7, Opacity = opacity };
        SetLeft(e, cx - rx);
        SetTop(e, cy - ry);
        into.Children.Add(e);
        return e;
    }

    private static Canvas Sub(Canvas into) { var c = new Canvas(); into.Children.Add(c); return c; }

    private void Build()
    {
        Children.Clear();
        foreach (var c in new[] { _all, _tail, _body, _head, _earL, _earR, _eyesOpen, _zzz }) c.Children.Clear();
        _hands = _seed = _cheeks = _wingL = _wingR = null;
        _tongue = _nose = _whiskers = null;

        var c = Of(_kind);
        string d = c.Dark;
        Children.Add(_all);
        _all.Children.Add(_tail);
        _all.Children.Add(_body);
        _all.Children.Add(_head);

        switch (_kind)
        {
            case PetKind.Dog:
                P(_tail, "M33 38 Q40 36 40 30", null, c.Fur, 3.2);
                var el = E(_earL, 13.6, 18, 3.4, 7, d); el.RenderTransform = new RotateTransform(14, 3.4, 1);
                var er = E(_earR, 34.4, 18, 3.4, 7, d); er.RenderTransform = new RotateTransform(-14, 3.4, 1);
                SitBody(c);
                E(_head, 24, 20, 10.6, 9.6, c.Fur, d);
                E(_head, 24, 24, 5.6, 4, c.Light);
                E(_head, 24, 22.2, 1.9, 1.3, "#3A2216");
                P(_head, "M24 23.4 L24 24.8 M24 24.8 Q22.4 26 21.2 25 M24 24.8 Q25.6 26 26.8 25", null, "#5A3010", .6);
                _tongue = P(_head, "M22.8 25.4 Q24 29.4 25.2 25.4 Z", "#FF7F95");
                break;

            case PetKind.Rabbit:
                E(_tail, 34.6, 40, 3.2, 3.2, "#FFFFFF", d);
                E(_earL, 19, 5, 2.9, 9, c.Fur, d); E(_earL, 19, 5.6, 1.4, 6.4, c.Inner);
                E(_earR, 29, 5, 2.9, 9, c.Fur, d); E(_earR, 29, 5.6, 1.4, 6.4, c.Inner);
                E(_body, 24, 36, 10, 8.6, c.Fur, d);
                E(_body, 24, 38, 5.4, 5, c.Light);
                E(_body, 19, 43.4, 3.6, 1.7, c.Light, d);
                E(_body, 29, 43.4, 3.6, 1.7, c.Light, d);
                E(_head, 24, 20.4, 10, 9, c.Fur, d);
                _nose = P(_head, "M22.9 22.4 L25.1 22.4 L24 23.6 Z", "#FF7F95");
                P(_head, "M24 23.6 Q23 25 21.9 24.4 M24 23.6 Q25 25 26.1 24.4", null, "#8A6A7A", .55);
                break;

            case PetKind.Hamster:
                E(_earL, 15.4, 11.4, 3.2, 3.2, c.Fur, d); E(_earL, 15.4, 11.6, 1.6, 1.6, c.Inner);
                E(_earR, 32.6, 11.4, 3.2, 3.2, c.Fur, d); E(_earR, 32.6, 11.6, 1.6, 1.6, c.Inner);
                E(_body, 24, 35.4, 12, 9.4, c.Fur, d);
                E(_body, 24, 37.6, 7, 6, c.Light);
                E(_body, 19, 44, 2.2, 1.3, "#F7A9B4");
                E(_body, 29, 44, 2.2, 1.3, "#F7A9B4");
                _seed = Sub(_body);
                var grainBox = Sub(_seed);
                var grain = E(grainBox, 24, 31, 1.8, 3, "#4A4A52"); grain.RenderTransform = new RotateTransform(20, 1.8, 3);
                _seedGrain = grainBox;
                _seedLine = P(_seed, "M23.3 29.2 L24.8 32.6", null, "#DDDDDD", .5);
                _handL = E(_seed, 21.6, 31.4, 1.4, 1.2, "#F7A9B4");
                _handR = E(_seed, 26.4, 31.4, 1.4, 1.2, "#F7A9B4");
                E(_head, 24, 20.6, 11.4, 9.6, c.Fur, d);
                _cheeks = Sub(_head);
                E(_cheeks, 16.6, 23.6, 3.6, 3, c.Light);
                E(_cheeks, 31.4, 23.6, 3.6, 3, c.Light);
                E(_head, 24, 23, 3.6, 2.6, c.Light);
                E(_head, 24, 22.3, .9, .9, "#FF7F95");
                _whiskers = P(_head, "M20 23.4 L15.4 22.6 M20 24.4 L15.6 25 M28 23.4 L32.6 22.6 M28 24.4 L32.4 25", null, "#8A6A50", .35);
                break;

            case PetKind.Penguin:
                E(_body, 24, 34.6, 11, 10, c.Fur);
                E(_body, 24, 36, 7.2, 8, c.Light);
                _wingL = Sub(_body); P(_wingL, "M14.4 29 Q8.6 34 10.6 40 Q13.6 37 15.2 33 Z", c.Fur);
                _wingR = Sub(_body); P(_wingR, "M33.6 29 Q39.4 34 37.4 40 Q34.4 37 32.8 33 Z", c.Fur);
                E(_body, 20, 44.4, 3, 1.4, c.Inner);
                E(_body, 28, 44.4, 3, 1.4, c.Inner);
                E(_head, 24, 19.6, 10.4, 9.6, c.Fur);
                P(_head, "M24 13.6 C18 13.6 15.4 18 16.4 22 C17.6 26.6 30.4 26.6 31.6 22 C32.6 18 30 13.6 24 13.6 Z", c.Light);
                P(_head, "M22.2 22 L25.8 22 L24 24.8 Z", c.Inner);
                break;

            case PetKind.Fox:
                var tail = Sub(_tail);
                P(tail, "M32 41 C44 44 46 30 40 24 C42 32 38 37 32 37 Z", c.Fur, d);
                P(tail, "M40 24 C42 27 42.6 30 41.6 32.6 C40.4 30 39.6 27 40 24 Z", c.Light);
                P(_earL, "M14.6 14 L14.2 2.8 L22 9.4 Z", c.Fur, d); P(_earL, "M14.5 6.4 L14.2 2.8 L17.3 5.4 Z", c.Inner);
                P(_earR, "M33.4 14 L33.8 2.8 L26 9.4 Z", c.Fur, d); P(_earR, "M33.5 6.4 L33.8 2.8 L30.7 5.4 Z", c.Inner);
                E(_body, 24, 36, 9.6, 8.4, c.Fur, d);
                P(_body, "M19 30 Q24 44 29 30 Z", c.Light);
                E(_body, 20, 43.4, 2.4, 1.5, c.Inner);
                E(_body, 28, 43.4, 2.4, 1.5, c.Inner);
                P(_head, "M13 16 Q14 28 24 29.4 Q34 28 35 16 Q30 10 24 10 Q18 10 13 16 Z", c.Fur, d);
                P(_head, "M15.4 21 Q18 28.4 24 29 Q30 28.4 32.6 21 Q28 24 24 24 Q20 24 15.4 21 Z", c.Light);
                E(_head, 24, 25.2, 1.5, 1.1, "#2A1A10");
                break;
        }

        // ears sit with the head; for the rabbit they're drawn first so the face overlaps them
        _head.Children.Insert(0, _earR);
        _head.Children.Insert(0, _earL);

        if (_kind != PetKind.Penguin)
        {
            E(_head, 15.6, 22.6, 1.9, 1.1, "#FF8FA3", opacity: .5);
            E(_head, 32.4, 22.6, 1.9, 1.1, "#FF8FA3", opacity: .5);
        }
        E(_eyesOpen, 19.6, 19, 1.7, 2.2, "#2A1A10"); E(_eyesOpen, 28.4, 19, 1.7, 2.2, "#2A1A10");
        E(_eyesOpen, 20.1, 18.2, .55, .55, "#FFFFFF"); E(_eyesOpen, 28.9, 18.2, .55, .55, "#FFFFFF");
        _head.Children.Add(_eyesOpen);
        _eyesHappy  = P(_head, "M17.8 19.6 Q19.6 17 21.4 19.6 M26.6 19.6 Q28.4 17 30.2 19.6", null, "#2A1A10", 1.2);
        _eyesClosed = P(_head, "M17.8 19 Q19.6 20.6 21.4 19 M26.6 19 Q28.4 20.6 30.2 19", null, "#2A1A10", 1.1);
        _brow       = P(_head, "M17.4 15.6 L21.4 17 M30.6 15.6 L26.6 17", null, "#2A1A10", 1);

        if (_kind == PetKind.Rabbit)
        {
            _hands = Sub(_body);
            E(_hands, 20.6, 31.4, 2, 2.6, "#FFFFFF", c.Dark);
            E(_hands, 27.4, 31.4, 2, 2.6, "#FFFFFF", c.Dark);
        }

        var z1 = new TextBlock { Text = "z", FontSize = 6, FontWeight = FontWeights.Bold, Foreground = B("#CFD3FF") };
        var z2 = new TextBlock { Text = "z", FontSize = 4.6, FontWeight = FontWeights.Bold, Foreground = B("#CFD3FF") };
        SetLeft(z1, 34); SetTop(z1, 3); SetLeft(z2, 38); SetTop(z2, -1.5);
        _zzz.Children.Add(z1); _zzz.Children.Add(z2);
        Children.Add(_zzz);
        Frame();
    }

    private void SitBody(Colours c)
    {
        E(_body, 24, 36, 10.5, 8.4, c.Fur, c.Dark);
        E(_body, 24, 38, 5.6, 5, c.Light);
        E(_body, 19.5, 43.4, 2.6, 1.6, c.Light, c.Dark);
        E(_body, 28.5, 43.4, 2.6, 1.6, c.Light, c.Dark);
    }

    // ── Moving ────────────────────────────────────────────────────────
    /// <summary>SVG-style transform lists, applied in the same order as the preview page.</summary>
    private sealed class X
    {
        public Matrix M = Matrix.Identity;
        public X T(double x, double y) { M.TranslatePrepend(x, y); return this; }
        public X R(double a, double cx = 0, double cy = 0) { M.RotateAtPrepend(a, cx, cy); return this; }
        public X S(double sx, double sy) { M.ScalePrepend(sx, sy); return this; }
        public X S(double s) => S(s, s);
        public X SAt(double sx, double sy, double cx, double cy) { T(cx, cy); S(sx, sy); T(-cx, -cy); return this; }
    }

    private static void Set(UIElement? e, X? x) { if (e != null) e.RenderTransform = new MatrixTransform(x?.M ?? Matrix.Identity); }
    private static void Show(UIElement? e, bool on) { if (e != null) e.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

    private void Frame()
    {
        double t = (DateTime.UtcNow - _start).TotalSeconds + _seed;
        double k = t * Math.PI * 2;
        static double s(double v) => Math.Sin(v);
        var kind = _kind;
        X? A = null, H = null, T = null, L = null, R = null, Bd = null;
        string eyes = ((t + _seed) % 3.6) < .14 ? "closed" : "open";
        bool brow = false, zzz = false;
        Show(_hands, false); Set(_hands, null);
        Show(_seed, true); Set(_cheeks, null); Set(_wingL, null); Set(_wingR, null); Set(_tongue, null); Set(_nose, null); Set(_whiskers, null);
        Show(_body, true); Show(_tail, true);

        switch (_state)
        {
            case PetState.Sit:
                if (kind == PetKind.Dog) { T = new X().R(s(k * 2) * 14, 33, 38); Set(_tongue, new X().SAt(1, 1 + .25 * s(k * 3), 24, 25.4)); }
                if (kind == PetKind.Rabbit)
                {
                    Set(_nose, new X().SAt(1 + .12 * s(k * 6), 1 + .12 * s(k * 6), 24, 23));
                    if (t % 6 > 4.8) { Show(_hands, true); H = new X().R(s(k * 2) * 4, 24, 26); eyes = "closed"; }
                }
                if (kind == PetKind.Hamster)
                {
                    double fill = Math.Min(1, t % 8 / 7);
                    H = new X().T(0, s(k * 4) * .4);
                    Set(_cheeks, new X().SAt(1 + fill * .35, 1 + fill * .2, 24, 23.6));
                }
                if (kind == PetKind.Penguin) { Set(_wingL, new X().R(-12 - 10 * Math.Abs(s(k)), 14.4, 30)); Set(_wingR, new X().R(12 + 10 * Math.Abs(s(k)), 33.6, 30)); }
                if (kind == PetKind.Fox) T = new X().R(s(k * .7) * 6, 33, 40);
                break;

            case PetState.Walk:
            {
                double speed = kind switch { PetKind.Dog => 1.6, PetKind.Rabbit => 1.1, PetKind.Hamster => 2.6, PetKind.Penguin => 1.2, _ => .8 };
                double p = t * speed;
                double bob = Math.Abs(s(p * Math.PI * 2)) * 1.4;
                if (kind == PetKind.Rabbit) bob = Math.Max(0, s(p * Math.PI * 2)) * 7;
                if (kind == PetKind.Hamster) bob = Math.Abs(s(p * Math.PI * 4)) * .9;
                double rot = kind == PetKind.Penguin ? s(p * Math.PI * 2) * 9 : 0;
                A = new X().T(0, -bob).R(rot, 24, 44);
                if (kind == PetKind.Dog) { T = new X().R(s(k * 3) * 18, 33, 38); Set(_tongue, new X().SAt(1, 1.2, 24, 25.4)); }
                if (kind == PetKind.Rabbit) { L = new X().R(-bob * 1.2, 19, 13); R = new X().R(bob * 1.2, 29, 13); }
                if (kind == PetKind.Fox) T = new X().R(s(k * .8) * 5, 33, 40);
                if (kind == PetKind.Penguin) { Set(_wingL, new X().R(-18, 14.4, 30)); Set(_wingR, new X().R(18, 33.6, 30)); }
                break;
            }

            case PetState.Head:   // peeking from behind the pill: just the head, ears busy
                Show(_body, false); Show(_tail, false);
                if (kind == PetKind.Rabbit) { L = new X().R(s(k * .5) * 10, 19, 13); R = new X().R(-s(k * .5) * 10, 29, 13); }
                if (kind == PetKind.Dog) R = new X().R(-40, 34.4, 11);
                if (kind == PetKind.Fox) eyes = "happy";
                if (kind == PetKind.Hamster) Set(_whiskers, new X().T(0, s(k * 2) * .4));
                H = new X().T(0, s(k * .3) * .6);
                break;

            case PetState.Near:
                if (kind == PetKind.Dog) { H = new X().R(-16, 24, 28); T = new X().R(s(k * 6) * 22, 33, 38); Set(_tongue, new X().SAt(1, 1.3, 24, 25.4)); }
                if (kind == PetKind.Rabbit)
                {
                    A = new X().T(0, -5 - Math.Abs(s(k * 1.4)) * 1.4).S(1, 1.08);
                    H = new X().R(s(k * .7) * 7, 24, 28);
                    L = new X().R(-4 + s(k * 2.2) * 12, 19, 13);
                    R = new X().R(4 + s(k * 2.2 + 1.6) * 12, 29, 13);
                    Set(_nose, new X().SAt(1 + .2 * s(k * 9), 1 + .2 * s(k * 9), 24, 23));
                    Show(_hands, true); Set(_hands, new X().T(0, s(k * 2.8) * .8));
                }
                if (kind == PetKind.Hamster)
                {
                    HandsUp(k);
                    A = new X().T(0, -Math.Abs(s(k * 1.8)) * 1.8);
                    H = new X().R(s(k * .9) * 8, 24, 26);
                    Set(_whiskers, new X().T(0, s(k * 7) * .5));
                    Set(_cheeks, new X().SAt(1 + .06 * s(k * 3), 1 + .06 * s(k * 3), 24, 23.6));
                }
                if (kind == PetKind.Penguin) { H = new X().R(14, 24, 28); Set(_wingL, new X().R(-30 - 30 * Math.Abs(s(k * 4)), 14.4, 30)); Set(_wingR, new X().R(30 + 30 * Math.Abs(s(k * 4)), 33.6, 30)); }
                if (kind == PetKind.Fox) { A = new X().T(0, 3).S(1, .88); T = new X().R(s(k * 4) * 12, 33, 40); Bd = new X().T(s(k * 4) * .8, 0); }
                break;

            case PetState.Sleep:
            {
                eyes = "closed"; zzz = true;
                double breathe = 1 + .03 * s(k * .4);
                A = new X().T(0, 5).S(1, .86 * breathe);
                if (kind == PetKind.Rabbit) { L = new X().R(70, 19, 13); R = new X().R(-70, 29, 13); }
                if (kind == PetKind.Dog) H = new X().T(0, 6);
                if (kind == PetKind.Penguin) { A = new X().S(1, breathe); H = new X().T(0, 4).R(8, 24, 28); }
                if (kind == PetKind.Fox) T = new X().T(-20, -2).R(-35, 33, 40);
                Show(_seed, false);
                break;
            }

            case PetState.Happy:
            {
                eyes = "happy";
                double j = Math.Max(0, s(k * 1.2)) * 10;
                A = new X().T(0, -j);
                if (kind == PetKind.Rabbit) A = new X().T(0, -j * 1.3).R(t * 1.2 % 1 * 360, 24, 30);
                if (kind == PetKind.Fox) A = new X().T(0, -j * 1.4).R(j * 6 - 20, 24, 30);
                if (kind == PetKind.Penguin) { A = new X().T(0, -Math.Abs(s(k * 2.4)) * 6); Set(_wingL, new X().R(-40, 14.4, 30)); Set(_wingR, new X().R(40, 33.6, 30)); }
                if (kind == PetKind.Hamster) A = Math.Cos(k * 1.6) > 0 ? new X().T(s(k * 1.6) * 10, -Math.Abs(s(k * 3.2)) * 2) : new X().T(s(k * 1.6) * 10, -Math.Abs(s(k * 3.2)) * 2).T(48, 0).S(-1, 1);
                if (kind == PetKind.Dog) T = new X().R(s(k * 6) * 22, 33, 38);
                break;
            }

            case PetState.Angry:
                brow = true;
                A = new X().T(s(k * 9) * .7, 0);
                if (kind == PetKind.Dog) { Set(_tongue, new X().S(0)); H = new X().T(0, 1); }
                if (kind == PetKind.Rabbit) { A = new X().T(0, t * 2 % 1 < .15 ? -2 : 0); L = new X().R(-20, 19, 13); R = new X().R(20, 29, 13); }
                if (kind == PetKind.Hamster) { A = new X().T(48, 0).S(-1, 1); Set(_cheeks, new X().SAt(1.6, 1.4, 24, 23.6)); brow = false; }
                if (kind == PetKind.Penguin) { A = new X().T(0, t * 3 % 1 < .2 ? -1.6 : 0); Set(_wingL, new X().R(-70, 14.4, 30)); Set(_wingR, new X().R(70, 33.6, 30)); }
                if (kind == PetKind.Fox) { L = new X().R(-40, 15, 13); R = new X().R(40, 33, 13); T = new X().SAt(1.5, 1.5, 33, 40); }
                break;

            case PetState.Sad:   // a tree died: head down, ears low, eyes shut
                eyes = "closed";
                H = new X().T(0, 1.6);
                L = new X().R(-18, 16, 13); R = new X().R(18, 32, 13);
                if (kind == PetKind.Rabbit) { L = new X().R(-50, 19, 13); R = new X().R(50, 29, 13); }
                break;
        }
        if (!(kind == PetKind.Hamster && _state == PetState.Near)) HandsDown();

        Show(_eyesOpen, eyes == "open");
        Show(_eyesHappy, eyes == "happy");
        Show(_eyesClosed, eyes == "closed");
        Show(_brow, brow);
        Show(_zzz, zzz);
        if (zzz) { double z = t * 3 % 6; _zzz.RenderTransform = new TranslateTransform(0, -z); _zzz.Opacity = 1 - z / 6; }
        Set(_all, A); Set(_head, H); Set(_tail, T); Set(_earL, L); Set(_earR, R); Set(_body, Bd);
    }

    private void HandsUp(double k)
    {
        if (_seed == null) return;
        Show(_seed, true);
        Show(_seedGrain, false); Show(_seedLine, false);
        double wave = Math.Sin(k * 4) * 1.6;
        Set(_handL, new X().T(-1.4, .2 + wave).SAt(1.35, 1.35, 21.6, 31.4));
        Set(_handR, new X().T(1.4, .2 - wave).SAt(1.35, 1.35, 26.4, 31.4));
    }

    private void HandsDown()
    {
        if (_seed == null) return;
        Show(_seedGrain, true); Show(_seedLine, true);
        Set(_handL, null); Set(_handR, null);
    }
}
