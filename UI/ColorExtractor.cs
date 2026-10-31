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

    /// <summary>Lightens dark colours so they stay visible on black.</summary>
    public static Color EnsureBright(Color c)
    {
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance < 0.55 ? Mix(c, Colors.White, 0.55 - luminance + 0.15) : c;
    }
}
