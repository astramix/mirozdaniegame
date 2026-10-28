using Mirozdanie.Core.Planet;

namespace Mirozdanie.Core.Output;

/// <summary>Слои карты в RGBA. Палитры — собственные, без чужих ассетов.</summary>
public static class MapRenderer
{
    private static readonly uint[] BiomeColors =
    {
        0x1B2F5Eu, // DeepOcean
        0x3A63A0u, // Shelf
        0x4A86C5u, // Lake
        0xEEF3F7u, // Ice
        0xA7B3A0u, // Tundra
        0x3F6B4Eu, // Taiga
        0xA9A28Au, // ColdDesert
        0x2E7A5Cu, // TemperateRainforest
        0x4F8F43u, // TemperateForest
        0x93A85Au, // Shrubland
        0xC2C46Eu, // Grassland
        0xE0C68Au, // Desert
        0xB9B04Cu, // Savanna
        0x5C9A2Fu, // TropicalSeasonalForest
        0x1F7A2Au, // TropicalRainforest
    };

    public static uint BiomeColor(Biome b) => BiomeColors[(int)b];

    internal static void Put(byte[] px, int i, uint rgb, byte a = 255)
    {
        px[i * 4] = (byte)(rgb >> 16); px[i * 4 + 1] = (byte)(rgb >> 8); px[i * 4 + 2] = (byte)rgb; px[i * 4 + 3] = a;
    }

    internal static uint Mix(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        int ar = (int)(a >> 16) & 255, ag = (int)(a >> 8) & 255, ab = (int)a & 255;
        int br = (int)(b >> 16) & 255, bg = (int)(b >> 8) & 255, bb = (int)b & 255;
        return (uint)((int)(ar + (br - ar) * t) << 16 | (int)(ag + (bg - ag) * t) << 8 | (int)(ab + (bb - ab) * t));
    }

    internal static uint Shade(uint c, float k)
    {
        int r = Math.Clamp((int)(((c >> 16) & 255) * k), 0, 255);
        int g = Math.Clamp((int)(((c >> 8) & 255) * k), 0, 255);
        int b = Math.Clamp((int)((c & 255) * k), 0, 255);
        return (uint)(r << 16 | g << 8 | b);
    }

    private static float Hillshade(PlanetData d, int x, int y)
    {
        int i = y * d.W + x;
        float e = Math.Max(0, d.Elevation[i]);
        float el = Math.Max(0, d.Elevation[y * d.W + d.WrapX(x - 1)]);
        float eu = y > 0 ? Math.Max(0, d.Elevation[i - d.W]) : e;
        float s = (e - el) + (e - eu);
        return Math.Clamp(1f + s * 0.9f, 0.6f, 1.35f);
    }

    public static byte[] Biomes(PlanetData d, bool rivers = true)
    {
        var px = new byte[d.W * d.H * 4];
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            uint c = BiomeColors[(int)d.Biome[i]];
            if (d.Elevation[i] >= 0)
            {
                if (d.Elevation[i] > 3.2f && d.Biome[i] != Planet.Biome.Ice) c = Mix(c, 0x8C8279u, (d.Elevation[i] - 3.2f) / 2f);
                c = Shade(c, Hillshade(d, x, y));
            }
            else c = Mix(c, 0x0E1A3Au, -d.Elevation[i] / 6f);
            if (rivers && d.River[i]) c = 0x3B7BD0u;
            Put(px, i, c);
        }
        return px;
    }

    public static byte[] Elevation(PlanetData d)
    {
        var px = new byte[d.W * d.H * 4];
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            float e = d.Elevation[i];
            uint c = e < 0 ? Mix(0x2C5C9Cu, 0x0B1530u, -e / 6f)
                : e < 1.5f ? Mix(0x4E8A46u, 0xC8B76Au, e / 1.5f)
                : Mix(0xC8B76Au, 0xFFFFFFu, (e - 1.5f) / 4.5f);
            if (e >= 0) c = Shade(c, Hillshade(d, x, y));
            Put(px, i, c);
        }
        return px;
    }

    public static byte[] Temperature(PlanetData d)
    {
        var px = new byte[d.W * d.H * 4];
        for (int i = 0; i < d.W * d.H; i++)
        {
            float t = (d.Temperature[i] + 30f) / 60f;
            uint c = t < 0.5f ? Mix(0x2D3E9Eu, 0xF2F0E6u, t * 2) : Mix(0xF2F0E6u, 0xC2271Fu, (t - 0.5f) * 2);
            if (d.Elevation[i] < 0) c = Shade(c, 0.75f);
            Put(px, i, c);
        }
        return px;
    }

    public static byte[] Precipitation(PlanetData d)
    {
        var px = new byte[d.W * d.H * 4];
        for (int i = 0; i < d.W * d.H; i++)
        {
            float p = Math.Clamp(d.Precip[i] / 3000f, 0, 1);
            uint c = p < 0.3f ? Mix(0xD9B77Au, 0x9FC56Au, p / 0.3f) : Mix(0x9FC56Au, 0x1E4FA0u, (p - 0.3f) / 0.7f);
            if (d.Elevation[i] < 0) c = Shade(c, 0.6f);
            Put(px, i, c);
        }
        return px;
    }

    public static byte[] Plates(PlanetData d)
    {
        var px = new byte[d.W * d.H * 4];
        for (int i = 0; i < d.W * d.H; i++)
        {
            uint c = Palette(d.Plate[i] * 7 + 3);
            if (d.BoundaryDist[i] < 1) c = 0x000000u;
            if (d.Elevation[i] < 0) c = Shade(c, 0.6f);
            Put(px, i, c);
        }
        return px;
    }

    public static byte[] Resources(PlanetData d)
    {
        var px = new byte[d.W * d.H * 4];
        for (int i = 0; i < d.W * d.H; i++)
        {
            uint c;
            if (d.Elevation[i] < 0) c = Mix(0x101828u, 0x3FA7B5u, d.Fish[i]);
            else
            {
                c = Mix(0x3A2E22u, 0x8EDB5Au, d.Fertility[i]);
                if (d.Ore[i] > 0.55f) c = Mix(c, 0xD07A2Eu, (d.Ore[i] - 0.55f) * 2.2f);
            }
            Put(px, i, c);
        }
        return px;
    }

    /// <summary>Устойчивая палитра для категорий (языки, веры, политии): золотое сечение по тону.</summary>
    public static uint Palette(int k)
    {
        if (k < 0) return 0x808080u;
        float h = (k * 0.618034f) % 1f;
        float s = 0.55f + 0.3f * ((k * 7 % 5) / 4f);
        float v = 0.75f + 0.2f * ((k * 3 % 4) / 3f);
        return Hsv(h, s, v);
    }

    public static uint Hsv(float h, float s, float v)
    {
        float r, g, b;
        int i = (int)(h * 6) % 6;
        float f = h * 6 - MathF.Floor(h * 6);
        float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (r, g, b) = i switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
        };
        return (uint)((int)(r * 255) << 16 | (int)(g * 255) << 8 | (int)(b * 255));
    }

    /// <summary>
    /// Наложение категорий эко-сетки поверх приглушённой карты биомов.
    /// <paramref name="ecoColor"/> — цвет на эко-клетку или <c>uint.MaxValue</c> (нет данных).
    /// Границы между категориями обводятся тёмным.
    /// </summary>
    public static byte[] EcoOverlay(PlanetData d, uint[] ecoColor, int[]? ecoCategory = null, float alpha = 0.7f)
    {
        var px = Biomes(d, rivers: true);
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            int ex = x / d.Scale, ey = y / d.Scale;
            int e = ey * d.EW + ex;
            uint baseC = (uint)(px[i * 4] << 16 | px[i * 4 + 1] << 8 | px[i * 4 + 2]);
            uint gray = Shade(Mix(baseC, 0x808080u, 0.6f), 0.8f);
            uint c = gray;
            if (ecoColor[e] != uint.MaxValue && d.Elevation[i] >= 0)
            {
                c = Mix(gray, ecoColor[e], alpha);
                if (ecoCategory != null)
                {
                    // Обводка на границе категорий.
                    bool edge = false;
                    if (x % d.Scale == 0) edge |= ecoCategory[d.EcoIndex(ex - 1, ey)] != ecoCategory[e];
                    if (y % d.Scale == 0 && ey > 0) edge |= ecoCategory[e - d.EW] != ecoCategory[e];
                    if (edge) c = Shade(c, 0.45f);
                }
            }
            else if (d.Elevation[i] >= 0) c = baseC;
            Put(px, i, c);
        }
        return px;
    }
}
