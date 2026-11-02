namespace DynamicIslandNotifier.UI;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

/// <summary>What Mây is doing.</summary>
public enum CatMood { Idle, Dance, Focus, Happy, Sad, Sleep }

/// <summary>
/// Mây, the orange cat that sits on the notch. Drawn as pixel art (14×13
/// pixels, 2 px each) so it stays crisp at its tiny size.
/// </summary>
public sealed class PixelCat : Canvas
{
    private const double Px = 2;

    // Legend: . clear · o orange · d dark orange · c cream · k eye · p pink · b tear (blue) · w white
    private static readonly string[] Sit =
    {
        "..o.......o...",
        ".odo.....odo..",
        ".ooooooooooo..",
        ".odooooooodo..",
        ".ookoooookoo..",
        ".oooocpcoooo..",
        "..oocccccoo...",
        "...ooooooo....",
        "..ooccccooo...",
        ".oooccccooood.",
        ".oooccccoooodd",
        ".oooccccooo.dd",
        "..cc....cc....",
    };

    private static readonly string[] Blink    = Replace(Sit, 4, ".oodooooodoo..");
    private static readonly string[] Sleepy   = Replace(Sit, 4, ".oodooooodoo..");
    private static readonly string[] HappyArt = Replace(Replace(Sit, 4, ".odkdooodkdo.."), 6, "..oocpppcoo...");
    private static readonly string[] SadArt   = Replace(Replace(Sit, 1, ".ooo.....ooo.."), 5, ".oobocpcoooo..");

    private static readonly Dictionary<char, Brush> Palette = new()
    {
        ['o'] = Solid(0xF5, 0xA6, 0x23),
        ['d'] = Solid(0xD9, 0x82, 0x2B),
        ['c'] = Solid(0xFF, 0xE7, 0xB3),
        ['k'] = Solid(0x22, 0x22, 0x22),
        ['p'] = Solid(0xFF, 0x8F, 0xA3),
        ['b'] = Solid(0x5A, 0xC8, 0xFA),
        ['w'] = Solid(0xFF, 0xFF, 0xFF),
    };

    private readonly Canvas _body = new() { Width = 14 * Px, Height = 13 * Px };
    private readonly RotateTransform _sway = new();
    private readonly TranslateTransform _hop = new();
    private readonly ScaleTransform _size = new(1, 1);
    private readonly TextBlock _zzz;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    private CatMood _mood = (CatMood)(-1);
    private string[] _art = Sit;

    public PixelCat()
    {
        Width  = 14 * Px;
        Height = 13 * Px;
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        var group = new TransformGroup();
        group.Children.Add(_size);
        group.Children.Add(_sway);
        group.Children.Add(_hop);
        _body.RenderTransformOrigin = new Point(0.5, 1);
        _body.RenderTransform = group;
        Children.Add(_body);

        _zzz = new TextBlock
        {
            Text = "z", Foreground = Solid(0xCC, 0xCC, 0xFF), FontSize = 9, FontWeight = FontWeights.Bold,
            Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform()
        };
        SetLeft(_zzz, 14 * Px - 2);
        SetTop(_zzz, -4);
        Children.Add(_zzz);

        _blinkTimer.Tick += (_, _) => DoBlink();
        Draw(Sit);
        Mood = CatMood.Idle;
    }

    /// <summary>1.0 = kitten; grows to 1.3 as Mây is fed.</summary>
    public double Growth
    {
        get => _size.ScaleX;
        set
        {
            double g = Math.Clamp(value, 1.0, 1.3);
            _size.ScaleX = _size.ScaleY = g;
            // Make room for the bigger cat so it isn't cut off at the screen edge.
            Width  = 14 * Px * g;
            Height = 13 * Px * g;
            SetLeft(_body, 7 * Px * (g - 1));
            SetTop(_body, 13 * Px * (g - 1));
            SetLeft(_zzz, 14 * Px * g - 2);
        }
    }

    public CatMood Mood
    {
        get => _mood;
        set
        {
            if (_mood == value) return;
            _mood = value;
            Animate();
        }
    }

    // ── Animation per mood ────────────────────────────────────────────
    private void Animate()
    {
        _sway.BeginAnimation(RotateTransform.AngleProperty, null);
        _hop.BeginAnimation(TranslateTransform.YProperty, null);
        _sway.Angle = 0;
        _hop.Y = 0;
        _zzz.Visibility = Visibility.Collapsed;
        _blinkTimer.Stop();

        switch (_mood)
        {
            case CatMood.Dance:     // sway to the music
                Draw(HappyArt);
                _sway.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-9, 9, TimeSpan.FromMilliseconds(480))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                break;

            case CatMood.Happy:     // little hops
                Draw(HappyArt);
                _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(220))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                break;

            case CatMood.Sad:
                Draw(SadArt);
                _sway.Angle = -6;
                break;

            case CatMood.Sleep:     // eyes shut, a floating "z"
                Draw(Sleepy);
                _zzz.Visibility = Visibility.Visible;
                var t = (TranslateTransform)_zzz.RenderTransform;
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(2, -6, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                _zzz.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                break;

            case CatMood.Focus:     // sits still beside the tree, blinking slowly
            default:
                Draw(Sit);
                _blinkTimer.Start();
                break;
        }
    }

    private void DoBlink()
    {
        Draw(Blink);
        var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        back.Tick += (_, _) => { back.Stop(); if (_mood is CatMood.Idle or CatMood.Focus) Draw(Sit); };
        back.Start();
    }

    // ── Pixels ────────────────────────────────────────────────────────
    private void Draw(string[] art)
    {
        if (ReferenceEquals(art, _art) && _body.Children.Count > 0) return;
        _art = art;
        _body.Children.Clear();
        for (int y = 0; y < art.Length; y++)
        {
            var row = art[y];
            for (int x = 0; x < row.Length; x++)
            {
                if (!Palette.TryGetValue(row[x], out var brush)) continue;
                var px = new Rectangle { Width = Px, Height = Px, Fill = brush };
                SetLeft(px, x * Px);
                SetTop(px, y * Px);
                _body.Children.Add(px);
            }
        }
    }

    private static string[] Replace(string[] art, int row, string line)
    {
        var copy = (string[])art.Clone();
        copy[row] = line;
        return copy;
    }

    private static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
