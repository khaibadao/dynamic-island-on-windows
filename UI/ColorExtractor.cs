namespace DynamicIslandNotifier.UI;

using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>Picks the main colour of an album cover, for tinting the music player.</summary>
public static class ColorExtractor
{
    /// <summary>
    /// The cover's dominant colour, favouring vivid pixels over greys, or null
    /// if there is no cover.
    /// </summary>
    public static Color? Dominant(BitmapSource? source)
    {
        if (source == null || source.PixelWidth == 0 || source.PixelHeight == 0) return null;
        try
        {
            // Shrink to 24×24: plenty to judge the colour, and fast.
            BitmapSource small = new TransformedBitmap(source,
                new ScaleTransform(24.0 / source.PixelWidth, 24.0 / source.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);

            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                byte B = px[i], G = px[i + 1], R = px[i + 2], A = px[i + 3];
                if (A < 128) continue;

                int max = Math.Max(R, Math.Max(G, B));
                int min = Math.Min(R, Math.Min(G, B));
                double saturation = max == 0 ? 0 : (max - min) / (double)max;
                double luminance  = (0.299 * R + 0.587 * G + 0.114 * B) / 255.0;

                double weight = 0.05 + saturation * saturation;
                if (luminance < 0.08 || luminance > 0.95) weight *= 0.1;   // near black / white

                r += R * weight; g += G * weight; b += B * weight;
                total += weight;
            }

            if (total <= 0) return null;
            return Color.FromRgb((byte)(r / total), (byte)(g / total), (byte)(b / total));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>a → b by t (0..1).</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>
    /// A bright, saturated version of the colour for accents on black (sound
    /// bars, seek bar, glow). Near-grey covers give white.
    /// </summary>
    public static Color Vivid(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 0.001) return Colors.White;

        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (s < 0.12) return Colors.White;

        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                 : max == g ? (b - r) / d + 2
                 : (r - g) / d + 4;
        h /= 6;

        return FromHsl(h, Math.Max(s, 0.6), 0.64);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromRgb(
            (byte)Math.Round(255 * Hue(p, q, h + 1.0 / 3)),
            (byte)Math.Round(255 * Hue(p, q, h)),
            (byte)Math.Round(255 * Hue(p, q, h - 1.0 / 3)));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    /// <summary>Lightens dark colours so they stay visible on black.</summary>
    public static Color EnsureBright(Color c)
    {
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance < 0.55 ? Mix(c, Colors.White, 0.55 - luminance + 0.15) : c;
    }
}
