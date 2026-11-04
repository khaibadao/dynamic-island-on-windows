namespace DynamicIslandNotifier.UI;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

/// <summary>How much of the colour wash to draw.</summary>
public enum AmbientLevel { Off, Light, Full }

/// <summary>
/// "Loang màu": the panel's background made from the song's own cover,
/// blurred (once per song), enlarged and slowly turning, with three soft
/// blobs of its colours drifting over it. A new song fades in over the old
/// one. <see cref="AmbientLevel.Light"/> keeps the blurred cover but nothing
/// moves. Everything is plain transforms on ready-made pictures, so it's
/// cheap for the graphics card; it only moves while <see cref="Play"/> is on.
/// </summary>
public sealed class AmbientBackdrop : Grid
{
    private sealed class Layer
    {
        public required Grid Root;
        public readonly List<(Animatable Target, DependencyProperty Property, AnimationTimeline Animation)> Moves = new();
    }

    private readonly List<Layer> _layers = new();
    private readonly Rectangle _shade;
    private bool _playing;
    private AmbientLevel _level = AmbientLevel.Full;

    /// <summary>The blurred cover of the current song (for the glow outside the panel).</summary>
    public BitmapSource? Blurred { get; private set; }

    public AmbientBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _shade = new Rectangle
        {
            // darker at the bottom and top so text stays readable on any cover
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x70, 0, 0, 0), 0),
                new(Color.FromArgb(0x52, 0, 0, 0), 0.4),
                new(Color.FromArgb(0x90, 0, 0, 0), 1)
            }, new Point(0, 0), new Point(0, 1))
        };
        _shade.Fill.Freeze();
        _shade.Visibility = Visibility.Collapsed;   // only over a cover
        Children.Add(_shade);
        SizeChanged += (_, _) => ClipBottomCorners();
    }

    /// <summary>
    /// Takes no room of its own: it only fills whatever size the panel's content
    /// already has (otherwise the big picture and blobs would stretch the panel).
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    /// <summary>Radius of the panel's bottom corners, so the wash follows them exactly.</summary>
    public double CornerRadius { get; set; } = 23;

    public AmbientLevel Level
    {
        get => _level;
        set
        {
            _level = value;
            Visibility = value == AmbientLevel.Off ? Visibility.Collapsed : Visibility.Visible;
            Play(_playing);
        }
    }

    /// <summary>True when there is a cover to show.</summary>
    public bool HasArt => _layers.Count > 0 && Blurred != null;

    /// <summary>Moves only while the panel is open (and the level is Full).</summary>
    public void Play(bool on)
    {
        _playing = on;
        bool move = on && _level == AmbientLevel.Full;
        foreach (var layer in _layers)
            foreach (var (target, property, animation) in layer.Moves)
                target.BeginAnimation(property, move ? animation : null);
    }

    /// <summary>A new song's cover (null: no music, the wash fades away).</summary>
    public void SetArt(BitmapSource? art)
    {
        if (art == null)
        {
            Blurred = null;
            _shade.Visibility = Visibility.Collapsed;
            foreach (var old in _layers.ToArray()) FadeOut(old);
            return;
        }

        var (blurred, colours) = Prepare(art);
        Blurred = blurred;
        _shade.Visibility = Visibility.Visible;
        var layer = BuildLayer(blurred, colours);
        Children.Insert(Children.Count - 1, layer.Root);   // under the shade
        var previous = _layers.ToArray();
        _layers.Add(layer);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1600)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) => { foreach (var old in previous) Remove(old); };
        layer.Root.BeginAnimation(OpacityProperty, fade);
        Play(_playing);
    }

    private void FadeOut(Layer layer)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(600));
        fade.Completed += (_, _) => Remove(layer);
        layer.Root.BeginAnimation(OpacityProperty, fade);
    }

    private void Remove(Layer layer)
    {
        foreach (var (target, property, _) in layer.Moves) target.BeginAnimation(property, null);
        Children.Remove(layer.Root);
        _layers.Remove(layer);
    }

    // Seven notes, each on its own slow path: x/y where it starts (share of the panel),
    // font size, seconds to cross the panel, how far it bobs, how much it tilts, colour.
    private static readonly (string Glyph, double X, double Y, double Size, double Cross, double Bob, double Tilt, int Colour)[] Notes =
    {
        ("\u266A", 0.08, 0.50, 66, 130, 14, 14, 0),
        ("\u266B", 0.27, 0.25, 60, 160, 18, 10, 1),
        ("\U0001D11E", 0.46, 0.55, 88, 215, 10, 8, 2),
        ("\u266C", 0.63, 0.30, 62, 145, 16, 16, 0),
        ("\u266A", 0.80, 0.65, 52, 118, 20, 18, 1),
        ("\u2669", 0.93, 0.22, 50, 185, 12, 12, 2),
        ("\u266B", 0.38, 0.85, 56, 150, 14, 12, 1),
    };

    private Layer BuildLayer(BitmapSource blurred, Color[] colours)
    {
        var root = new Grid { Opacity = 0 };
        var layer = new Layer { Root = root };
        double w = ActualWidth > 0 ? ActualWidth : 640, h = ActualHeight > 0 ? ActualHeight : 220;

        // Base: the song's colours, bright and clear
        Color Scale(Color c, double k) => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
        var baseFill = new LinearGradientBrush(new GradientStopCollection
        {
            new(Scale(colours[0], 0.7), 0),
            new(Scale(colours[2], 0.55), 0.6),
            new(Color.FromRgb(12, 10, 16), 1)
        }, new Point(0, 0), new Point(1, 1));
        baseFill.Freeze();
        root.Children.Add(new Rectangle { Fill = baseFill });

        // Three soft glows of colour wandering underneath (each drawn once, then only moved)
        var paths = new[] { (-170.0, -50.0, 150.0, 40.0, 19.0, 13.0), (150.0, 30.0, -120.0, -40.0, 23.0, 16.0), (0.0, 70.0, 60.0, -60.0, 29.0, 21.0) };
        for (int i = 0; i < 3; i++)
        {
            var c = colours[i];
            var blob = new Ellipse
            {
                Width = 420, Height = 300,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb(0x8C, c.R, c.G, c.B), 0),
                    new(Color.FromArgb(0x40, c.R, c.G, c.B), 0.5),
                    new(Color.FromArgb(0x00, c.R, c.G, c.B), 1)
                }),
                CacheMode = new BitmapCache()
            };
            blob.Fill.Freeze();
            var (x0, y0, x1, y1, tx, ty) = paths[i];
            var move = new TranslateTransform(x0, y0);
            blob.RenderTransform = move;
            root.Children.Add(blob);
            layer.Moves.Add((move, TranslateTransform.XProperty, Loop(x0, x1, tx)));
            layer.Moves.Add((move, TranslateTransform.YProperty, Loop(y0, y1, ty)));
        }

        // The notes: blurred like coloured smoke (blurred once, kept as a picture),
        // drifting sideways across the panel, bobbing and tilting a little.
        var symbols = new FontFamily("Segoe UI Symbol, Segoe UI Emoji, Segoe UI");
        foreach (var n in Notes)
        {
            var c = colours[n.Colour];
            var tint = Color.FromArgb(0xD8, (byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40));
            var glyph = new TextBlock
            {
                Text = n.Glyph, FontFamily = symbols, FontSize = n.Size, Foreground = new SolidColorBrush(tint),
                Margin = new Thickness(14),   // room for the blur
                Effect = new BlurEffect { Radius = 13, RenderingBias = RenderingBias.Performance }
            };
            var shift = new TranslateTransform(n.X * w, n.Y * h - n.Size * 0.7);
            var tilt  = new RotateTransform(0);
            var note = new Border
            {
                Child = glyph, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { tilt, shift } },
                CacheMode = new BitmapCache()   // the blur is worked out once; moving it is then cheap
            };
            root.Children.Add(note);

            // Sideways: from where it starts to past the right edge, then round again from the left
            double from = -n.Size * 1.5, to = w + n.Size * 0.5;
            double start = n.X * w;
            var across = new DoubleAnimation(from, to, TimeSpan.FromSeconds(n.Cross)) { RepeatBehavior = RepeatBehavior.Forever };
            across.BeginTime = -TimeSpan.FromSeconds(n.Cross * (start - from) / (to - from));
            layer.Moves.Add((shift, TranslateTransform.XProperty, Smooth(across)));

            double y = n.Y * h - n.Size * 0.7;
            layer.Moves.Add((shift, TranslateTransform.YProperty, Loop(y - n.Bob, y + n.Bob, 9 + n.Colour * 1.3)));
            layer.Moves.Add((tilt, RotateTransform.AngleProperty, Loop(-n.Tilt, n.Tilt, 14 + n.Colour * 2)));
        }
        return layer;
    }

    /// <summary>A steady 30 frames a second: smooth enough for slow drifting, and even.</summary>
    private static AnimationTimeline Smooth(AnimationTimeline a)
    {
        Timeline.SetDesiredFrameRate(a, 30);
        return a;
    }

    private static AnimationTimeline Loop(double from, double to, double seconds, bool autoReverse = true) =>
        Smooth(new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = autoReverse, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = autoReverse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
        });

    /// <summary>Top corners square (they meet the screen edge), bottom ones rounded like the panel.</summary>
    private void ClipBottomCorners()
    {
        double w = ActualWidth, h = ActualHeight, r = Math.Min(CornerRadius, Math.Min(w, h) / 2);
        if (w <= 0 || h <= 0) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(w, 0), false, false);
            ctx.LineTo(new Point(w, h - r), false, false);
            ctx.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(r, h), false, false);
            ctx.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        Clip = g;
    }

    /// <summary>The cover shrunk to 64 px and blurred once (cheap to show at any size), plus three of its colours.</summary>
    public static (BitmapSource Blurred, Color[] Colours) Prepare(BitmapSource art)
    {
        const int S = 64;
        var small = new TransformedBitmap(art, new ScaleTransform((double)S / art.PixelWidth, (double)S / art.PixelHeight));
        var image = new Image
        {
            Source = small, Stretch = Stretch.Fill, Width = S + 24, Height = S + 24, Margin = new Thickness(-12),
            Effect = new BlurEffect { Radius = 9, KernelType = KernelType.Gaussian }
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var host = new Grid { Width = S, Height = S, ClipToBounds = true, Background = Brushes.Black };
        host.Children.Add(image);
        host.Measure(new Size(S, S));
        host.Arrange(new Rect(0, 0, S, S));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();

        var pixels = new byte[S * S * 4];
        rtb.CopyPixels(pixels, S * 4, 0);
        Color Sample(double fx, double fy)
        {
            long r = 0, g = 0, b = 0, n = 0;
            int cx = (int)(fx * S), cy = (int)(fy * S);
            for (int y = Math.Max(0, cy - 8); y < Math.Min(S, cy + 8); y++)
                for (int x = Math.Max(0, cx - 8); x < Math.Min(S, cx + 8); x++)
                {
                    int i = (y * S + x) * 4;
                    b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; n++;
                }
            var c = Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
            return ColorExtractor.Mix(c, ColorExtractor.Vivid(c), 0.5);
        }
        return (rtb, new[] { Sample(0.25, 0.3), Sample(0.75, 0.35), Sample(0.5, 0.8) });
    }
}
