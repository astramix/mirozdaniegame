using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Planet;

/// <summary>Генерация планеты. Все формулы — docs/design/planet.md.</summary>
public static class PlanetGenerator
{
    private struct PlateInfo
    {
        public float Cx, Cy, Vx, Vy;
        public bool Continental;
    }

    public static PlanetData Generate(WorldParams p, Rng rng)
    {
        var d = new PlanetData(p.Width, p.Height, p.EcoScale);
        var noise = new GradientNoise(rng);
        var warpNoise = new GradientNoise(rng);
        var cyl = new CylinderMap(d.W, d.H);

        var plates = MakePlates(d, p, rng, p.PlateCount);
        AssignPlates(d, plates, noise, cyl, p.OceanFraction);
        var conv = BoundaryConvergence(d, plates, out var boundary);
        float[] nearestConv = new float[d.W * d.H];
        bool[] nearestOceanic = new bool[d.W * d.H];
        DistanceFromBoundaries(d, boundary, conv, plates, d.BoundaryDist, nearestConv, nearestOceanic);
        BuildElevation(d, p, plates, noise, warpNoise, cyl, nearestConv, nearestOceanic);

        // «Древние» границы плит — для руд (planet.md, «Ресурсы»).
        var ancient = MakePlates(d, p, rng, Math.Max(4, p.PlateCount - 2));
        float[] ancientDist = AncientBoundaryDistance(d, ancient);

        // Рельеф генератора — основа, поверх которой лягут правки Создателя (terraforming.md, §2).
        Array.Copy(d.Elevation, d.BaseElevation, d.Elevation.Length);
        NoiseFields(d, ancientDist, noise, cyl);
        Recompute(d, p);
        if (Environment.GetEnvironmentVariable("MIRO_DEBUG") == "1")
        {
            var land = d.Elevation.Where(e => e >= 0).OrderBy(e => e).ToArray();
            Console.WriteLine($"land frac {land.Length / (float)d.Elevation.Length:F2} median {land[land.Length / 2]:F2} p90 {land[land.Length * 9 / 10]:F2}");
        }
        return d;
    }

    /// <summary>
    /// Пересчёт производных полей из рельефа и слоёв правок: климат, осадки, сток, биомы, эко-сетка.
    /// Генерация вызывает его с нулевыми правками, поэтому мир без правок совпадает с прежним (terraforming.md, §2).
    /// </summary>
    public static void Recompute(PlanetData d, WorldParams p)
    {
        int n = d.W * d.H;
        for (int i = 0; i < n; i++) d.Elevation[i] = d.BaseElevation[i] + d.ElevDelta[i] - d.SeaLevel;
        Climate(d, p);
        Hydrology(d);
        BiomesAndResources(d);
        Aggregate(d);
        Summaries(d);
    }

    /// <summary>Шумы, нужные пересчёту, считаются один раз: пересчёт не трогает RNG.</summary>
    private static void NoiseFields(PlanetData d, float[] ancientDist, GradientNoise noise, CylinderMap cyl)
    {
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            var (ix, iy, iz) = cyl.Point(x, y, 0.05);
            d.IceNoise[i] = (float)noise.Sample(ix + 57, iy, iz);
            var (px, py, pz) = cyl.Point(x, y, 0.08);
            float nz = (float)(0.5 + 0.5 * noise.Sample(px + 101, py, pz));
            float tect = MathF.Exp(-d.BoundaryDist[i] / 5f) + 0.7f * MathF.Exp(-ancientDist[i] / 4f);
            d.OreField[i] = Math.Clamp(tect * nz, 0, 1);
            // Ископаемые (kardashev.md, §4), запас в ЭДж на клетку; масштаб подобран так, чтобы «Земля» имела
            // ~20 000 ЭДж угля, ~10 000 нефти и газа, ~3 000 урана (порядки по BP Statistical Review / WNA).
            float e = d.Elevation[i];
            var (qx, qy, qz) = cyl.Point(x, y, 0.06);
            // Уголь: древние низинные бассейны (каменноугольные болота) вдали от молодых гор.
            float basin = Smooth(-0.05f, 0.15f, e) * (1f - Smooth(0.7f, 1.4f, e)) * Smooth(5f, 12f, d.BoundaryDist[i]);
            float cn = (float)(0.5 + 0.5 * noise.Sample(qx + 211, qy, qz));
            d.Coal[i] = basin * Smooth(0.55f, 0.8f, cn) * 6f;
            // Нефть и газ: бывшие мелководья — шельфы и прибрежные низины.
            float shelf = Smooth(-0.45f, -0.2f, e) * (1f - Smooth(0.2f, 0.6f, e)) * Smooth(3f, 8f, d.BoundaryDist[i]);
            float on = (float)(0.5 + 0.5 * noise.Sample(qx + 307, qy + 11, qz));
            d.Oil[i] = shelf * Smooth(0.58f, 0.82f, on) * 5f;
            // Уран: древние кратоны — далеко и от нынешних, и от древних границ плит.
            float craton = Smooth(8f, 20f, d.BoundaryDist[i]) * Smooth(6f, 14f, ancientDist[i]) * (e >= 0 ? 1f : 0f);
            float un = (float)(0.5 + 0.5 * noise.Sample(qx + 401, qy - 7, qz));
            d.Uranium[i] = craton * Smooth(0.6f, 0.85f, un) * 1.5f;
        }
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // ---------- Плиты ----------

    private static PlateInfo[] MakePlates(PlanetData d, WorldParams p, Rng rng, int count)
    {
        var plates = new PlateInfo[count];
        for (int i = 0; i < count; i++)
        {
            float a = rng.Range(0f, MathF.PI * 2);
            float speed = rng.Range(0.3f, 1f);
            plates[i] = new PlateInfo
            {
                Cx = rng.Range(0f, d.W),
                // Центры не у самых полюсов.
                Cy = rng.Range(d.H * 0.1f, d.H * 0.9f),
                Vx = MathF.Cos(a) * speed,
                Vy = MathF.Sin(a) * speed,
            };
        }
        return plates;
    }

    private static float WrapDx(float dx, int w)
    {
        if (dx > w * 0.5f) dx -= w;
        else if (dx < -w * 0.5f) dx += w;
        return dx;
    }

    private static void AssignPlates(PlanetData d, PlateInfo[] plates, GradientNoise noise, CylinderMap cyl, float oceanFraction)
    {
        int[] area = new int[plates.Length];
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            // Искажение расстояния шумом даёт неровные границы плит.
            var (px, py, pz) = cyl.Point(x, y, 0.02);
            float jx = (float)noise.Fbm(px, py, pz, 3) * 26f;
            float jy = (float)noise.Fbm(px + 31.7, py + 11.3, pz + 5.1, 3) * 26f;
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < plates.Length; i++)
            {
                float dx = WrapDx(x + jx - plates[i].Cx, d.W);
                float dy = y + jy - plates[i].Cy;
                float dist = dx * dx + dy * dy;
                if (dist < best) { best = dist; bi = i; }
            }
            d.Plate[y * d.W + x] = (byte)bi;
            area[bi]++;
        }
        // Континентальные плиты набираем, пока не покроем долю суши с запасом (часть континента уйдёт под воду).
        float target = (1f - oceanFraction) * 1.15f * d.W * d.H;
        int[] order = new int[plates.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        // Детерминированный порядок: по координате центра (уже случайной).
        Array.Sort(order, (a, b) => plates[a].Cx.CompareTo(plates[b].Cx));
        float acc = 0;
        for (int k = 0; k < order.Length; k++)
        {
            int i = order[(k * 3) % order.Length];
            if (plates[i].Continental) continue;
            if (acc >= target) break;
            plates[i].Continental = true;
            acc += area[i];
        }
    }

    private static float[] BoundaryConvergence(PlanetData d, PlateInfo[] plates, out bool[] boundary)
    {
        var conv = new float[d.W * d.H];
        boundary = new bool[d.W * d.H];
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            int a = d.Plate[i];
            // Соседи справа и снизу.
            for (int k = 0; k < 2; k++)
            {
                int nx = k == 0 ? d.WrapX(x + 1) : x;
                int ny = k == 0 ? y : y + 1;
                if (ny >= d.H) continue;
                int j = ny * d.W + nx;
                int b = d.Plate[j];
                if (a == b) continue;
                // Сходимость c = (v_a − v_b)·n, n — единичный вектор от центра плиты a к центру b
                // (одинаков вдоль всей границы пары, поэтому хребет не «полосатый»).
                float nxv = WrapDx(plates[b].Cx - plates[a].Cx, d.W), nyv = plates[b].Cy - plates[a].Cy;
                float len = MathF.Max(1e-3f, MathF.Sqrt(nxv * nxv + nyv * nyv));
                float c = ((plates[a].Vx - plates[b].Vx) * nxv + (plates[a].Vy - plates[b].Vy) * nyv) / len;
                if (Math.Abs(c) > Math.Abs(conv[i])) conv[i] = c;
                if (Math.Abs(c) > Math.Abs(conv[j])) conv[j] = c;
                boundary[i] = boundary[j] = true;
            }
        }
        return conv;
    }

    /// <summary>Многоисточниковый BFS от граничных клеток; переносит сходимость ближайшей границы.</summary>
    private static void DistanceFromBoundaries(PlanetData d, bool[] boundary, float[] conv, PlateInfo[] plates,
        float[] dist, float[] nearestConv, bool[] nearestOceanicPair)
    {
        int n = d.W * d.H;
        var queue = new int[n];
        int head = 0, tail = 0;
        for (int i = 0; i < n; i++)
        {
            if (boundary[i])
            {
                dist[i] = 0; nearestConv[i] = conv[i];
                queue[tail++] = i;
            }
            else dist[i] = float.MaxValue;
        }
        while (head < tail)
        {
            int c = queue[head++];
            int cx = c % d.W, cy = c / d.W;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx, ny = cy;
                if (k == 0) nx = d.WrapX(cx + 1); else if (k == 1) nx = d.WrapX(cx - 1);
                else if (k == 2) ny = cy + 1; else ny = cy - 1;
                if (ny < 0 || ny >= d.H) continue;
                int j = ny * d.W + nx;
                if (dist[j] <= dist[c] + 1) continue;
                dist[j] = dist[c] + 1;
                nearestConv[j] = nearestConv[c];
                queue[tail++] = j;
            }
        }
        for (int i = 0; i < n; i++) nearestOceanicPair[i] = !plates[d.Plate[i]].Continental;
    }

    private static void BuildElevation(PlanetData d, WorldParams p, PlateInfo[] plates, GradientNoise noise,
        GradientNoise warp, CylinderMap cyl, float[] nearestConv, bool[] oceanic)
    {
        int n = d.W * d.H;
        var h = new float[n];
        for (int i = 0; i < n; i++) h[i] = plates[d.Plate[i]].Continental ? 0.35f : -0.45f;
        // Сглаживание основания, чтобы край плиты не был ступенькой.
        BoxBlur(h, d.W, d.H, 4);
        BoxBlur(h, d.W, d.H, 4);

        const float sigma = 6f;
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            float c = nearestConv[i];
            float bd = d.BoundaryDist[i];
            float fall = MathF.Exp(-bd / sigma);
            if (c > 0)
            {
                // Сходящаяся граница: хребет; у океанической плиты у самой границы — жёлоб.
                if (oceanic[i] && bd < 3) h[i] -= p.Tectonics * 0.12f * c * MathF.Exp(-bd / 1.5f);
                else h[i] += p.Tectonics * 0.9f * c * fall;
            }
            else
            {
                h[i] += p.Tectonics * 0.2f * c * MathF.Exp(-bd / 3f); // рифт
            }

            // fBm с доменной деформацией.
            var (px, py, pz) = cyl.Point(x, y, 0.012);
            double wx = warp.Fbm(px, py, pz, 3);
            double wy = warp.Fbm(px + 19.1, py + 7.7, pz + 3.3, 3);
            double nval = noise.Fbm(px + 0.8 * 4 * wx, py + 0.8 * 4 * wy, pz, 6);
            h[i] += 0.45f * (float)nval;
            // Ледяные шапки полюсов не трогаем: только чуть опускаем крайние ряды, чтобы не было «стен».
        }

        // Уровень моря — квантиль OceanFraction. Высоты отображаются по рангу на гипсометрическую
        // кривую, близкую к земной (медиана суши ≈ 0,4 км, 90 % ≈ 2 км, глубины до 6 км; ETOPO1).
        var idx = new int[n];
        var keys = (float[])h.Clone();
        for (int i = 0; i < n; i++) idx[i] = i;
        Array.Sort(keys, idx);
        int seaRank = Math.Clamp((int)(p.OceanFraction * n), 1, n - 1);
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            if (k >= seaRank)
            {
                float r = (k - seaRank) / (float)Math.Max(1, n - seaRank - 1);
                float t = p.Tectonics;
                d.Elevation[i] = 0.002f + 0.9f * MathF.Pow(r, 1.2f) + (3.5f + 1.5f * t) * MathF.Pow(r, 12f);
            }
            else
            {
                float r = (seaRank - 1 - k) / (float)Math.Max(1, seaRank - 1); // 0 у берега, 1 в глубине
                d.Elevation[i] = r < 0.08f ? -0.002f - 0.25f * r / 0.08f
                    : -(0.25f + 5.5f * MathF.Pow((r - 0.08f) / 0.92f, 0.6f));
            }
        }
    }

    private static float[] AncientBoundaryDistance(PlanetData d, PlateInfo[] plates)
    {
        int n = d.W * d.H;
        var owner = new byte[n];
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < plates.Length; i++)
            {
                float dx = WrapDx(x - plates[i].Cx, d.W);
                float dy = y - plates[i].Cy;
                float dd = dx * dx + dy * dy;
                if (dd < best) { best = dd; bi = i; }
            }
            owner[y * d.W + x] = (byte)bi;
        }
        var boundary = new bool[n];
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            int r = y * d.W + d.WrapX(x + 1);
            if (owner[r] != owner[i]) boundary[i] = boundary[r] = true;
            if (y + 1 < d.H && owner[i + d.W] != owner[i]) boundary[i] = boundary[i + d.W] = true;
        }
        var dist = new float[n];
        var dummyC = new float[n];
        var conv = new float[n];
        var oc = new bool[n];
        var fake = new PlateInfo[256];
        DistanceFromBoundaries(d, boundary, conv, fake, dist, dummyC, oc);
        return dist;
    }

    // ---------- Климат ----------

    private static void Climate(PlanetData d, WorldParams p)
    {
        int n = d.W * d.H;
        // Звезда: ΔT ≈ T₀·(L^{1/4} − 1), T₀ = 288 K (закон Стефана–Больцмана).
        float dTStar = 288f * (MathF.Pow(p.StarBrightness, 0.25f) - 1f);

        // Континентальность: расстояние до океана (BFS по суше).
        var oceanDist = new float[n];
        var queue = new int[n];
        int head = 0, tail = 0;
        for (int i = 0; i < n; i++)
        {
            if (d.Elevation[i] < 0) { oceanDist[i] = 0; queue[tail++] = i; }
            else oceanDist[i] = float.MaxValue;
        }
        while (head < tail)
        {
            int c = queue[head++];
            int cx = c % d.W, cy = c / d.W;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx, ny = cy;
                if (k == 0) nx = d.WrapX(cx + 1); else if (k == 1) nx = d.WrapX(cx - 1);
                else if (k == 2) ny = cy + 1; else ny = cy - 1;
                if (ny < 0 || ny >= d.H) continue;
                int j = ny * d.W + nx;
                if (oceanDist[j] <= oceanDist[c] + 1) continue;
                oceanDist[j] = oceanDist[c] + 1;
                queue[tail++] = j;
            }
        }

        for (int y = 0; y < d.H; y++)
        {
            float lat = d.Latitude(y);
            float latRad = lat * MathF.PI / 180f;
            float sin = MathF.Sin(latRad);
            // T₀(φ) = MeanT + 13 − 44·|sin φ|³ (подогнано к зональным средним Земли: 27 °C у экватора, ≈0 °C на 60°).
            float as3 = MathF.Abs(sin * sin * sin);
            float t0 = p.MeanTemperature + 13f - 44f * as3 + dTStar;
            float absLat = MathF.Abs(lat);
            // Расстояние от западного берега (для тёплого течения в поясе западных ветров).
            int lastOcean = -100000;
            for (int pass = 0; pass < 2; pass++)
            for (int x = 0; x < d.W; x++)
            {
                int i = y * d.W + x;
                int gx = pass * d.W + x;
                if (d.Elevation[i] < 0) lastOcean = gx;
                if (pass == 0) continue;
                float cont = Math.Min(1f, oceanDist[i] / 30f);
                float z = Math.Max(0f, d.Elevation[i]);
                float warm = 0f;
                if (absLat >= 40 && absLat <= 65 && d.Elevation[i] >= 0)
                {
                    float dw = gx - lastOcean;
                    warm = 4f * MathF.Exp(-dw / 10f);
                }
                // Градиент 6,5 °C/км (стандартная атмосфера ICAO).
                d.Temperature[i] = t0 - 6.5f * z + warm + d.TempDelta[i] + d.GlobalTempDelta;
                d.SeasonAmp[i] = 18f * (p.AxialTilt / 23.4f) * MathF.Abs(sin) * (0.35f + 0.65f * cont);
            }
        }

        Precipitation(d, p);
    }

    /// <summary>Средняя годовая температура без течений: T₀(φ) − 6,5·z (planet.md).</summary>
    public static float BaseTemperature(float latDeg, float elevKm, float meanT, float starBrightness = 1f)
    {
        float sin = MathF.Sin(latDeg * MathF.PI / 180f);
        float as3 = MathF.Abs(sin * sin * sin);
        float dTStar = 288f * (MathF.Pow(starBrightness, 0.25f) - 1f);
        return meanT + 13f - 44f * as3 + dTStar - 6.5f * Math.Max(0f, elevKm);
    }

    /// <summary>
    /// Осадки вдоль строки по направлению ветра (орографический эффект и дождевая тень).
    /// <paramref name="elev"/> и <paramref name="temp"/> — значения строки, dir — +1 (ветер с запада) или −1.
    /// </summary>
    public static float[] PrecipitationRow(float[] elev, float[] temp, int dir, float p0)
    {
        int w = elev.Length;
        var res = new float[w];
        float moisture = 0.6f, prevZ = 0f;
        for (int pass = 0; pass < 3; pass++)
        for (int s = 0; s < w; s++)
        {
            int x = ((dir * s) % w + w) % w;
            float z = Math.Max(0f, elev[x]);
            // Клаузиус–Клапейрон: влагоёмкость воздуха ~6–7 %/K; ослаблено до 4 %/K, т. к. P₀ уже учитывает пояса.
            float cc = Math.Clamp(MathF.Exp(0.04f * (temp[x] - 15f)), 0.3f, 1.3f);
            float rain;
            if (elev[x] < 0)
            {
                moisture += (1f - moisture) * 0.35f;
                rain = p0 * moisture * cc;
                prevZ = 0;
            }
            else
            {
                float rise = Math.Max(0f, z - prevZ);
                // Орографический эффект: подъём выжимает влагу, за хребтом — дождевая тень.
                rain = p0 * cc * (0.25f + 0.75f * moisture) * (1f + 3.0f * rise);
                moisture *= 0.975f * MathF.Exp(-1.6f * rise);
                moisture += 0.004f; // рециркуляция испарения
                moisture = Math.Clamp(moisture, 0f, 1f);
                prevZ = z;
            }
            if (pass == 2) res[x] = rain;
        }
        return res;
    }

    /// <summary>Базовые осадки по широтным поясам (ячейки Хэдли, Феррела, полярная).</summary>
    public static float BasePrecip(float lat)
    {
        float a = MathF.Abs(lat);
        float p0 = 2200f * MathF.Exp(-(lat / 12f) * (lat / 12f))
                 + 1100f * MathF.Exp(-((a - 55f) / 15f) * ((a - 55f) / 15f))
                 + 150f
                 - 700f * MathF.Exp(-((a - 28f) / 8f) * ((a - 28f) / 8f));
        return Math.Max(60f, p0);
    }

    private static void Precipitation(PlanetData d, WorldParams p)
    {
        var elev = new float[d.W];
        var temp = new float[d.W];
        for (int y = 0; y < d.H; y++)
        {
            float lat = d.Latitude(y);
            float a = MathF.Abs(lat);
            // Пассаты и полярные восточные — с востока (движемся к −x), западные — с запада.
            int dir = (a < 30f || a > 60f) ? -1 : 1;
            float p0 = 1.35f * BasePrecip(lat) * p.PrecipitationScale;
            Array.Copy(d.Elevation, y * d.W, elev, 0, d.W);
            Array.Copy(d.Temperature, y * d.W, temp, 0, d.W);
            var row = PrecipitationRow(elev, temp, dir, p0);
            Array.Copy(row, 0, d.Precip, y * d.W, d.W);
        }
        // Сглаживание, чтобы строки не давали полос.
        BoxBlur(d.Precip, d.W, d.H, 3);
        BoxBlur(d.Precip, d.W, d.H, 2);
        // Оазисы и пустыни Создателя (terraforming.md).
        for (int i = 0; i < d.Precip.Length; i++) d.Precip[i] *= d.PrecipMul[i];
    }

    // ---------- Гидрология ----------

    /// <summary>Priority-Flood (Barnes, Lehman, Mulla 2014) с записью дерева стока.</summary>
    private static void Hydrology(PlanetData d)
    {
        int n = d.W * d.H;
        var closed = new bool[n];
        var pq = new PriorityQueue<int, (float, int)>(n / 4);
        var order = new int[n];
        int orderLen = 0;
        int seq = 0;
        for (int i = 0; i < n; i++)
        {
            d.Downstream[i] = -1;
            if (d.Elevation[i] < 0)
            {
                d.Filled[i] = d.Elevation[i];
                closed[i] = true;
                order[orderLen++] = i;
            }
        }
        // Затравка: береговые клетки суши.
        for (int i = 0; i < n; i++)
        {
            if (closed[i]) continue;
            int x = i % d.W, y = i / d.W;
            bool coast = false;
            int down = -1;
            ForNeighbors8(d, x, y, j => { if (d.Elevation[j] < 0) { coast = true; if (down < 0) down = j; } });
            if (coast || y == 0 || y == d.H - 1)
            {
                closed[i] = true;
                d.Filled[i] = d.Elevation[i];
                d.Downstream[i] = down;
                pq.Enqueue(i, (d.Filled[i], seq++));
            }
        }
        while (pq.TryDequeue(out int c, out _))
        {
            order[orderLen++] = c;
            int x = c % d.W, y = c / d.W;
            for (int k = 0; k < 8; k++)
            {
                int dx = k switch { 0 => 1, 1 => -1, 2 => 0, 3 => 0, 4 => 1, 5 => 1, 6 => -1, _ => -1 };
                int dy = k switch { 0 => 0, 1 => 0, 2 => 1, 3 => -1, 4 => 1, 5 => -1, 6 => 1, _ => -1 };
                int ny = y + dy;
                if (ny < 0 || ny >= d.H) continue;
                int j = ny * d.W + d.WrapX(x + dx);
                if (closed[j]) continue;
                closed[j] = true;
                d.Filled[j] = Math.Max(d.Elevation[j], d.Filled[c] + 1e-5f);
                d.Downstream[j] = c;
                pq.Enqueue(j, (d.Filled[j], seq++));
            }
        }
        // Накопление стока: от истоков к устьям (обратный порядок извлечения).
        for (int i = 0; i < n; i++) d.Flow[i] = d.Elevation[i] >= 0 ? d.Precip[i] / 1000f + d.ExtraFlow[i] : 0f;
        for (int k = orderLen - 1; k >= 0; k--)
        {
            int c = order[k];
            int dn = d.Downstream[c];
            if (dn >= 0 && d.Elevation[c] >= 0) d.Flow[dn] += d.Flow[c];
        }
        // Реки: накопление выше квантиля 0,97 по суше.
        var land = new List<float>(n / 2);
        for (int i = 0; i < n; i++) if (d.Elevation[i] >= 0) land.Add(d.Flow[i]);
        land.Sort();
        float thr = land.Count > 0 ? land[(int)(land.Count * 0.97)] : float.MaxValue;
        d.RiverThreshold = thr;
        for (int i = 0; i < n; i++) d.River[i] = d.Elevation[i] >= 0 && d.Flow[i] >= thr;

        // Озёра: впадины, заполненные больше чем на 20 м.
        for (int i = 0; i < n; i++)
            if (d.Elevation[i] >= 0 && d.Filled[i] - d.Elevation[i] > 0.08f) d.Biome[i] = Biome.Lake;
            else d.Biome[i] = Biome.Count; // временно «не задан»
    }

    private static void ForNeighbors8(PlanetData d, int x, int y, Action<int> f)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int ny = y + dy;
            if (ny < 0 || ny >= d.H) continue;
            f(ny * d.W + d.WrapX(x + dx));
        }
    }

    // ---------- Биомы и ресурсы ----------

    private static void BiomesAndResources(PlanetData d)
    {
        int n = d.W * d.H;
        for (int i = 0; i < n; i++)
        {
            float e = d.Elevation[i];
            if (e < 0)
            {
                d.Biome[i] = e > -0.25f ? Biome.Shelf : Biome.DeepOcean;
                // Паковый лёд с рваным краем (шум к порогу температуры).
                if (d.Temperature[i] + 4f * d.IceNoise[i] < -12f) d.Biome[i] = Biome.Ice;
            }
            else if (d.Biome[i] != Biome.Lake)
            {
                d.Biome[i] = Biomes.Whittaker(d.Temperature[i], d.Precip[i]);
            }
            float npp = (d.Elevation[i] >= 0 && d.Biome[i] != Biome.Lake) ? Biomes.MiamiNpp(d.Temperature[i], d.Precip[i]) : 0f;
            if (d.Biome[i] == Biome.Ice) npp = 0;
            d.Npp[i] = npp;
        }
        for (int y = 0; y < d.H; y++)
        for (int x = 0; x < d.W; x++)
        {
            int i = y * d.W + x;
            if (d.Elevation[i] < 0 || d.Biome[i] == Biome.Lake)
            {
                // Рыба: шельф и озёра.
                d.Fish[i] = d.Biome[i] is Biome.Shelf or Biome.Lake ? 0.6f + 0.4f * Math.Clamp(d.Temperature[i] / 30f, 0, 1) : 0.1f;
                d.Fertility[i] = 0f;
                d.Ore[i] = 0f;
                continue;
            }
            // Уклон.
            float e = d.Elevation[i];
            float er = d.Elevation[y * d.W + d.WrapX(x + 1)];
            float slope = Math.Clamp(MathF.Abs(er - e) * 1.5f, 0, 1);
            float river = d.River[i] ? 1f : 0f;
            float fert = d.Npp[i] / 3000f * (1f + 0.6f * river) * (1f - slope);
            if (d.Biome[i] is Biome.Ice or Biome.Desert or Biome.ColdDesert) fert *= 0.15f;
            d.Fertility[i] = Math.Clamp(fert + d.FertDelta[i], 0, 1);
            d.Ore[i] = d.OreField[i];
            d.Fish[i] = d.River[i] ? 0.5f : 0f;
        }
    }

    // ---------- Эко-сетка ----------

    private static void Aggregate(PlanetData d)
    {
        int s = d.Scale;
        Span<int> biomeCount = stackalloc int[(int)Biome.Count + 1];
        for (int ey = 0; ey < d.EH; ey++)
        for (int ex = 0; ex < d.EW; ex++)
        {
            int e = ey * d.EW + ex;
            biomeCount.Clear();
            int land = 0, total = 0, rivers = 0;
            float t = 0, tAll = 0, pr = 0, npp = 0, fert = 0, ore = 0, fish = 0, elev = 0, season = 0, bd = float.MaxValue;
            for (int dy = 0; dy < s; dy++)
            for (int dx = 0; dx < s; dx++)
            {
                int i = (ey * s + dy) * d.W + ex * s + dx;
                total++;
                tAll += d.Temperature[i];
                fish = Math.Max(fish, d.Fish[i]);
                bd = Math.Min(bd, d.BoundaryDist[i]);
                if (d.Elevation[i] >= 0 && d.Biome[i] != Biome.Lake)
                {
                    land++;
                    t += d.Temperature[i]; pr += d.Precip[i]; npp += d.Npp[i]; fert += d.Fertility[i];
                    ore = Math.Max(ore, d.Ore[i]); elev += d.Elevation[i]; season += d.SeasonAmp[i];
                    if (d.River[i]) rivers++;
                    biomeCount[(int)d.Biome[i]]++;
                }
                else biomeCount[(int)d.Biome[i]]++;
            }
            d.ELand[e] = land / (float)total;
            if (land > 0)
            {
                d.ETemp[e] = t / land; d.EPrecip[e] = pr / land; d.ENpp[e] = npp / land; d.EFertility[e] = fert / land;
                d.EOre[e] = ore; d.EElev[e] = elev / land; d.ESeason[e] = season / land; d.ERiver[e] = rivers / (float)land;
            }
            else
            {
                // Клетка ушла под воду (или всегда была морем): суши нет — нет и её свойств.
                d.ETemp[e] = tAll / total;
                d.EPrecip[e] = 0; d.ENpp[e] = 0; d.EFertility[e] = 0; d.EOre[e] = 0; d.EElev[e] = 0; d.ESeason[e] = 0; d.ERiver[e] = 0;
            }
            d.EFish[e] = fish;
            d.EBoundary[e] = MathF.Exp(-bd / 6f);
            int best = 0;
            for (int b = 0; b < (int)Biome.Count; b++)
            {
                // Для суши — преобладающий наземный биом.
                if (d.ELand[e] >= 0.5f && Biomes.IsWater((Biome)b)) continue;
                if (biomeCount[b] > biomeCount[best] || (Biomes.IsWater((Biome)best) && d.ELand[e] >= 0.5f && biomeCount[b] > 0)) best = b;
            }
            d.EBiome[e] = (Biome)best;
            d.EDomesticable[e] = d.ELand[e] >= 0.5f && Biomes.Domesticable(d.EBiome[e]) && d.EFertility[e] > 0.28f;
            // Энергетические ресурсы клетки (kardashev.md, §4).
            float coal = 0, oil = 0, ur = 0;
            for (int dy = 0; dy < s; dy++)
            for (int dx = 0; dx < s; dx++)
            {
                int i = (ey * s + dy) * d.W + ex * s + dx;
                coal += d.Coal[i]; oil += d.Oil[i]; ur += d.Uranium[i];
            }
            d.ECoal[e] = coal; d.EOil[e] = oil; d.EUranium[e] = ur;
            // Солнце: инсоляция ∝ cos широты, облачность ∝ осадкам; ветер — пояса западных ветров и пассатов; вода — реки с перепадом.
            float lat = MathF.Abs(d.EcoLatitude(ey)) * MathF.PI / 180f;
            d.ESolar[e] = Math.Clamp(MathF.Cos(lat) * (1f - Math.Min(0.6f, d.EPrecip[e] / 4000f)), 0f, 1f);
            d.EWind[e] = Math.Clamp(0.25f + 0.6f * MathF.Abs(MathF.Sin(2f * lat)) + 0.15f * Math.Min(1f, d.EElev[e] / 2f) + (d.ECoast[e] ? 0.1f : 0f), 0f, 1f);
            d.EHydro[e] = Math.Clamp(d.ERiver[e] * 3f * (0.3f + Math.Min(1f, d.EElev[e])), 0f, 1f);
        }
        for (int ey = 0; ey < d.EH; ey++)
        for (int ex = 0; ex < d.EW; ex++)
        {
            int e = ey * d.EW + ex;
            bool coast = d.ELand[e] < 1f && d.ELand[e] >= 0.5f;
            for (int k = 0; k < 4 && !coast; k++)
            {
                int nx = ex + (k == 0 ? 1 : k == 1 ? -1 : 0);
                int ny = ey + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (ny < 0 || ny >= d.EH) continue;
                if (d.ELand[d.EcoIndex(nx, ny)] < 0.5f) coast = true;
            }
            d.ECoast[e] = coast && d.ELand[e] >= 0.5f;
        }
    }

    private static void Summaries(PlanetData d)
    {
        int n = d.W * d.H;
        int hi = 0;
        for (int i = 1; i < n; i++) if (d.Elevation[i] > d.Elevation[hi]) hi = i;
        d.HighestCell = hi;
        d.ContinentCount = 0;
        // Материки: связные компоненты суши площадью ≥ 1 % карты.
        var comp = new int[n];
        Array.Fill(comp, -1);
        var stack = new int[n];
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (d.Elevation[i] < 0 || comp[i] >= 0) continue;
            int sp = 0, size = 0;
            stack[sp++] = i; comp[i] = count;
            while (sp > 0)
            {
                int c = stack[--sp]; size++;
                int x = c % d.W, y = c / d.W;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x, ny = y;
                    if (k == 0) nx = d.WrapX(x + 1); else if (k == 1) nx = d.WrapX(x - 1);
                    else if (k == 2) ny = y + 1; else ny = y - 1;
                    if (ny < 0 || ny >= d.H) continue;
                    int j = ny * d.W + nx;
                    if (d.Elevation[j] < 0 || comp[j] >= 0) continue;
                    comp[j] = count; stack[sp++] = j;
                }
            }
            if (size >= n / 100) d.ContinentCount++;
            count++;
        }
        // Великие реки: устья (речная клетка, сток которой уходит в море) с наибольшим стоком.
        var mouths = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!d.River[i]) continue;
            int dn = d.Downstream[i];
            if (dn >= 0 && d.Elevation[dn] < 0) mouths.Add(i);
        }
        mouths.Sort((a, b) => { int c = d.Flow[b].CompareTo(d.Flow[a]); return c != 0 ? c : a.CompareTo(b); });
        d.GreatRivers = mouths.Take(3).ToArray();
    }

    public static void BoxBlur(float[] a, int w, int h, int r)
    {
        var tmp = new float[a.Length];
        for (int y = 0; y < h; y++)
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += a[y * w + ((k % w) + w) % w];
            for (int x = 0; x < w; x++)
            {
                tmp[y * w + x] = sum / (2 * r + 1);
                int add = ((x + r + 1) % w + w) % w, rem = ((x - r) % w + w) % w;
                sum += a[y * w + add] - a[y * w + rem];
            }
        }
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            float sum = 0; int cnt = 0;
            for (int k = -r; k <= r; k++)
            {
                int yy = y + k;
                if (yy < 0 || yy >= h) continue;
                sum += tmp[yy * w + x]; cnt++;
            }
            a[y * w + x] = sum / cnt;
        }
    }
}
