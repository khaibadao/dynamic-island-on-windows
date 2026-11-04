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
        Children.Add(_shade);
        SizeChanged += (_, _) => ClipBottomCorners();
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
            foreach (var old in _layers.ToArray()) FadeOut(old);
            return;
        }

        var (blurred, colours) = Prepare(art);
        Blurred = blurred;
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

    private Layer BuildLayer(BitmapSource blurred, Color[] colours)
    {
        var root = new Grid { Opacity = 0 };
        var layer = new Layer { Root = root };

        // The cover itself, much bigger than the panel, slowly turning and wandering
        var scale  = new ScaleTransform(1.7, 1.7);
        var rotate = new RotateTransform(0);
        var shift  = new TranslateTransform();
        var cover = new Image
        {
            Source = blurred, Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, rotate, shift } }
        };
        RenderOptions.SetBitmapScalingMode(cover, BitmapScalingMode.HighQuality);
        root.Children.Add(cover);
        layer.Moves.Add((rotate, RotateTransform.AngleProperty, Loop(0, 360, 140, autoReverse: false)));
        layer.Moves.Add((shift, TranslateTransform.XProperty, Loop(-40, 40, 23)));
        layer.Moves.Add((shift, TranslateTransform.YProperty, Loop(-18, 18, 17)));

        // Three soft blobs of the cover's colours, each on its own slow loop
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
                    new(Color.FromArgb(0xD0, c.R, c.G, c.B), 0),
                    new(Color.FromArgb(0x60, c.R, c.G, c.B), 0.5),
                    new(Color.FromArgb(0x00, c.R, c.G, c.B), 1)
                })
            };
            blob.Fill.Freeze();
            var (x0, y0, x1, y1, tx, ty) = paths[i];
            var move = new TranslateTransform(x0, y0);
            blob.RenderTransform = move;
            root.Children.Add(blob);
            layer.Moves.Add((move, TranslateTransform.XProperty, Loop(x0, x1, tx)));
            layer.Moves.Add((move, TranslateTransform.YProperty, Loop(y0, y1, ty)));
        }
        return layer;
    }

    private static DoubleAnimation Loop(double from, double to, double seconds, bool autoReverse = true) =>
        new(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = autoReverse, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = autoReverse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
        };

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
