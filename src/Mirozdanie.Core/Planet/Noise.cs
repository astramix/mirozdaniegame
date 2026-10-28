using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Planet;

/// <summary>
/// Градиентный шум Перлина 3D («Improved Noise», Perlin 2002) с перестановкой из RNG проекта,
/// fBm и доменная деформация (Quilez 2002, https://iquilezles.org/articles/warp/).
/// </summary>
public sealed class GradientNoise
{
    private readonly int[] _perm = new int[512];

    public GradientNoise(Rng rng)
    {
        Span<int> p = stackalloc int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        rng.Shuffle(p);
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad(int hash, double x, double y, double z)
    {
        int h = hash & 15;
        double u = h < 8 ? x : y;
        double v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    /// <summary>Значение примерно в [−1, 1].</summary>
    public double Sample(double x, double y, double z)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        double xf = x - xi, yf = y - yi, zf = z - zi;
        int X = xi & 255, Y = yi & 255, Z = zi & 255;
        double u = Fade(xf), v = Fade(yf), w = Fade(zf);
        var p = _perm;
        int A = p[X] + Y, AA = p[A] + Z, AB = p[A + 1] + Z;
        int B = p[X + 1] + Y, BA = p[B] + Z, BB = p[B + 1] + Z;
        return Lerp(w,
            Lerp(v, Lerp(u, Grad(p[AA], xf, yf, zf), Grad(p[BA], xf - 1, yf, zf)),
                    Lerp(u, Grad(p[AB], xf, yf - 1, zf), Grad(p[BB], xf - 1, yf - 1, zf))),
            Lerp(v, Lerp(u, Grad(p[AA + 1], xf, yf, zf - 1), Grad(p[BA + 1], xf - 1, yf, zf - 1)),
                    Lerp(u, Grad(p[AB + 1], xf, yf - 1, zf - 1), Grad(p[BB + 1], xf - 1, yf - 1, zf - 1))));
    }

    public double Fbm(double x, double y, double z, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Sample(x, y, z);
            norm += amp;
            amp *= gain;
            x *= lacunarity; y *= lacunarity; z *= lacunarity;
        }
        return sum / norm;
    }
}

/// <summary>Отображение клетки карты на поверхность цилиндра (долгота замкнута, шва нет).</summary>
public readonly struct CylinderMap
{
    public readonly int Width, Height;
    private readonly double _radius;

    public CylinderMap(int width, int height)
    {
        Width = width; Height = height;
        _radius = width / (2 * Math.PI);
    }

    public (double x, double y, double z) Point(double cx, double cy, double scale)
    {
        double a = 2 * Math.PI * cx / Width;
        return (Math.Cos(a) * _radius * scale, Math.Sin(a) * _radius * scale, cy * scale);
    }
}
