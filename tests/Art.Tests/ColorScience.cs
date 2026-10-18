using System;
using System.Globalization;

namespace StickFighter.Art.Tests;

// The color math behind the palette test: WCAG contrast, simulated color-vision deficiency, and
// perceptual distance.
public static class ColorScience
{
    public enum Vision { Normal, Protanopia, Deuteranopia, Tritanopia }

    // Machado, Oliveira and Fernandes (2009), severity 1.0: full dichromacy, the worst case of each
    // deficiency. Applied to linear RGB.
    private static readonly double[,] Protan =
    {
        { 0.152286, 1.052583, -0.204868 },
        { 0.114503, 0.786281, 0.099216 },
        { -0.003882, -0.048116, 1.051998 },
    };

    private static readonly double[,] Deutan =
    {
        { 0.367322, 0.860646, -0.227968 },
        { 0.280085, 0.672501, 0.047413 },
        { -0.011820, 0.042940, 0.968881 },
    };

    private static readonly double[,] Tritan =
    {
        { 1.255528, -0.076749, -0.178779 },
        { -0.078411, 0.930809, 0.147602 },
        { 0.004733, 0.691367, 0.303900 },
    };

    // "#RRGGBB" to linear RGB in 0..1.
    public static (double R, double G, double B) Linear(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
            throw new FormatException($"'{hex}' is not #RRGGBB");
        double Channel(int at)
        {
            var c = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return (Channel(1), Channel(3), Channel(5));
    }

    public static double Luminance(string hex)
    {
        var (r, g, b) = Linear(hex);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    // WCAG 2 contrast ratio, 1 to 21.
    public static double Contrast(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (double R, double G, double B) Simulate((double R, double G, double B) c, Vision vision)
    {
        var m = vision switch
        {
            Vision.Protanopia => Protan,
            Vision.Deuteranopia => Deutan,
            Vision.Tritanopia => Tritan,
            _ => null,
        };
        if (m is null)
            return c;
        static double Clamp(double v) => Math.Clamp(v, 0, 1);
        return (
            Clamp(m[0, 0] * c.R + m[0, 1] * c.G + m[0, 2] * c.B),
            Clamp(m[1, 0] * c.R + m[1, 1] * c.G + m[1, 2] * c.B),
            Clamp(m[2, 0] * c.R + m[2, 1] * c.G + m[2, 2] * c.B));
    }

    // CIE L*a*b* (D65) from linear RGB.
    public static (double L, double A, double B) Lab((double R, double G, double B) c)
    {
        var x = (0.4124 * c.R + 0.3576 * c.G + 0.1805 * c.B) / 0.95047;
        var y = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        var z = (0.0193 * c.R + 0.1192 * c.G + 0.9505 * c.B) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    // CIE76 distance between two colors as someone with the given vision sees them. Around 2 is
    // just noticeable; above 20 reads as a different color at a glance.
    public static double Distance(string a, string b, Vision vision)
    {
        var la = Lab(Simulate(Linear(a), vision));
        var lb = Lab(Simulate(Linear(b), vision));
        return Math.Sqrt(Math.Pow(la.L - lb.L, 2) + Math.Pow(la.A - lb.A, 2) + Math.Pow(la.B - lb.B, 2));
    }
}
