using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Output;

/// <summary>Динамические слои мира (языки, веры, державы, отношение к Создателю) поверх биомов.</summary>
public static class WorldMaps
{
    public static readonly string[] Layers = { "biomes", "elevation", "temperature", "precipitation", "plates", "resources", "languages", "religions", "polities", "attitude" };

    public static uint AttitudeColor(Attitude a) => a switch
    {
        Attitude.Love => 0xF2C14Eu,
        Attitude.Fear => 0x8E3B46u,
        Attitude.Bargain => 0x6FA36Bu,
        Attitude.Indifference => 0x9BA4B5u,
        Attitude.Denial => 0x4D8FD6u,
        Attitude.Rebellion => 0xE0482Fu,
        _ => 0x2B2B2Bu,
    };

    public static byte[] Render(World w, string layer)
    {
        var p = w.Planet;
        switch (layer)
        {
            case "biomes": return MapRenderer.Biomes(p);
            case "elevation": return MapRenderer.Elevation(p);
            case "temperature": return MapRenderer.Temperature(p);
            case "precipitation": return MapRenderer.Precipitation(p);
            case "plates": return MapRenderer.Plates(p);
            case "resources": return MapRenderer.Resources(p);
            case "polities": return Polities(w);
        }
        int n = p.EW * p.EH;
        var color = new uint[n];
        var cat = new int[n];
        for (int c = 0; c < n; c++)
        {
            int gid = w.CellOwner.Length > 0 ? w.CellOwner[c] : -1;
            if (gid < 0) { color[c] = uint.MaxValue; cat[c] = -1; continue; }
            var g = w.Groups[gid];
            int k = layer switch
            {
                "languages" => g.LanguageId,
                "religions" => g.ReligionId,
                "polities" => g.PolityId,
                "attitude" => (int)g.Attitude,
                _ => -1,
            };
            cat[c] = k;
            color[c] = layer == "attitude" ? AttitudeColor(g.Attitude) : k < 0 ? 0xB0B0B0u : MapRenderer.Palette(k);
        }
        return MapRenderer.EcoOverlay(p, color, cat);
    }

    /// <summary>
    /// Карта держав (civilizations.md, §10): полупрозрачная заливка цветом державы, контур границы державы в 2 пикселя,
    /// пунктир между народами внутри державы, штриховка земель, куда идёт войско.
    /// </summary>
    public static byte[] Polities(World w)
    {
        var p = w.Planet;
        var px = MapRenderer.Biomes(p, rivers: true);
        int en = p.EW * p.EH;
        var pol = new int[en];
        var grp = new int[en];
        var war = new bool[en];
        for (int c = 0; c < en; c++)
        {
            int gid = w.CellOwner.Length > 0 ? w.CellOwner[c] : -1;
            grp[c] = gid;
            pol[c] = gid >= 0 ? w.Groups[gid].PolityId : -1;
        }
        foreach (var a in w.Armies)
            if (a.Alive && !a.Defender)
                foreach (var c in w.Groups[a.Target].Territory) war[c] = true;
        int Pol(int x, int y) => y < 0 || y >= p.H ? -2 : pol[p.EcoIndex((x + p.W) % p.W / p.Scale, y / p.Scale)];
        int Grp(int x, int y) => y < 0 || y >= p.H ? -2 : grp[p.EcoIndex((x + p.W) % p.W / p.Scale, y / p.Scale)];
        for (int y = 0; y < p.H; y++)
        for (int x = 0; x < p.W; x++)
        {
            int i = y * p.W + x;
            if (p.Elevation[i] < 0) continue;
            int e = (y / p.Scale) * p.EW + x / p.Scale;
            uint baseC = (uint)(px[i * 4] << 16 | px[i * 4 + 1] << 8 | px[i * 4 + 2]);
            uint gray = MapRenderer.Shade(MapRenderer.Mix(baseC, 0x808080u, 0.45f), 0.88f);
            int k = pol[e];
            uint c;
            if (k >= 0)
            {
                uint pc = MapRenderer.Palette(k);
                c = MapRenderer.Mix(gray, pc, 0.55f);
                bool border = Pol(x - 1, y) != k || Pol(x + 1, y) != k || Pol(x, y - 1) != k || Pol(x, y + 1) != k;
                if (border) c = MapRenderer.Shade(pc, 0.42f);
                else if ((Grp(x - 1, y) != grp[e] || Grp(x, y - 1) != grp[e]) && ((x + y) / 2) % 2 == 0) c = MapRenderer.Shade(c, 0.78f);
            }
            else if (grp[e] >= 0) c = MapRenderer.Mix(gray, 0xC8C0B0u, 0.3f);
            else c = MapRenderer.Mix(baseC, gray, 0.5f);
            if (war[e] && (x + y) % 6 < 2) c = MapRenderer.Mix(c, 0xD03020u, 0.6f);
            MapRenderer.Put(px, i, c);
        }
        return px;
    }

    /// <summary>
    /// Ночные огни (civilizations.md, §10; kardashev.md): яркость — население города × энергия на душу, пятно — по рангу города.
    /// Один байт на пиксель карты.
    /// </summary>
    public static byte[] NightLights(World w)
    {
        var p = w.Planet;
        var lum = new float[p.W * p.H];
        foreach (var city in w.Cities)
        {
            if (!city.Alive) continue;
            var g = w.Groups[city.Group];
            // Огонь очагов у земледельцев едва виден; электричество зажигает города (порядок — по мощности на душу).
            float glow = MathF.Log10(1f + city.Pop * MathF.Max(0.02f, g.Watts / 3000f) / 1000f) / 5f;
            if (glow <= 0.01f) continue;
            int cx = (city.Cell % p.EW) * p.Scale + p.Scale / 2, cy = (city.Cell / p.EW) * p.Scale + p.Scale / 2;
            int r = 1 + city.Rank;
            uint h = (uint)city.Id * 2654435761u;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int y = cy + dy;
                if (y < 0 || y >= p.H) continue;
                int x = (cx + dx + p.W) % p.W;
                float d2 = (dx * dx + dy * dy) / (float)(r * r + 1);
                if (d2 > 1f) continue;
                // Россыпь пригородов: хеш даёт неровный край.
                float speck = ((h >> ((dx + dy * 3) & 15)) & 1) == 0 ? 1f : 0.55f;
                int i = y * p.W + x;
                if (p.Elevation[i] < 0) continue;
                lum[i] += glow * (1f - d2) * speck;
            }
        }
        var res = new byte[p.W * p.H];
        for (int i = 0; i < res.Length; i++) res[i] = (byte)Math.Clamp(lum[i] * 255f, 0f, 255f);
        return res;
    }
}
