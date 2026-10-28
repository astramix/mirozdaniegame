namespace Mirozdanie.Core.Planet;

/// <summary>Режимы кисти Создателя (docs/design/terraforming.md, §1).</summary>
public enum BrushMode : byte { Raise, Lower, Flatten, Smooth, Roughen, Wet, Dry, Warm, Cool, Fertile, Barren }

/// <summary>Сдвиг суши при дрейфе: на сколько эко-клеток и какие эко-клетки уехали (до сдвига).</summary>
public sealed class DriftResult
{
    public int Dx, Dy;
    public bool[] EcoMoved = Array.Empty<bool>();
    public int FineCells;
    public int CollisionCells;
}

/// <summary>
/// Правки рельефа и климата на слоях <see cref="PlanetData"/> (docs/design/terraforming.md, §2).
/// Операции меняют только слои правок; производные поля потом пересчитывает <see cref="PlanetGenerator.Recompute"/>.
/// Все операции детерминированы: никакого RNG, шум — хеш от координат.
/// Возвращают «работу» — сумму модулей изменений (для цены в энергии).
/// </summary>
public static class Terraform
{
    public const float MinElev = -9f, MaxElev = 9.5f;

    // ---------- Детерминированный шум (хеш координат) ----------

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>Сглаженный value-noise 0…1.</summary>
    public static float ValueNoise(float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(x0, y0), b = Hash(x0 + 1, y0), c = Hash(x0, y0 + 1), d = Hash(x0 + 1, y0 + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Гребнистый шум (горные хребты): 1 − |2·fbm − 1|, три октавы.</summary>
    public static float Ridged(float x, float y)
    {
        float sum = 0, amp = 0.5f, f = 1f, norm = 0;
        for (int o = 0; o < 3; o++)
        {
            sum += amp * ValueNoise(x * f + 17.3f * o, y * f - 9.1f * o);
            norm += amp;
            amp *= 0.5f;
            f *= 2.1f;
        }
        float v = sum / norm;
        return 1f - MathF.Abs(2f * v - 1f);
    }

    // ---------- Геометрия ----------

    /// <summary>Кратчайшая разность по долготе с замыканием карты.</summary>
    private static float WrapDx(PlanetData d, float dx)
    {
        if (dx > d.W / 2f) dx -= d.W;
        else if (dx < -d.W / 2f) dx += d.W;
        return dx;
    }

    /// <summary>Расстояние в клетках карты с поправкой на широту (кружок кисти на сфере остаётся кружком).</summary>
    public static float Distance(PlanetData d, float x0, float y0, float x1, float y1)
    {
        float cos = d.CosLat((int)Math.Clamp((y0 + y1) * 0.5f, 0, d.H - 1));
        float dx = WrapDx(d, x1 - x0) * cos, dy = y1 - y0;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Перебор клеток в радиусе (с замыканием долготы): индекс и расстояние.</summary>
    private static int Disc(PlanetData d, float cx, float cy, float r, Span<int> idx, Span<float> dist)
    {
        int count = 0;
        int y0 = Math.Max(0, (int)MathF.Floor(cy - r)), y1 = Math.Min(d.H - 1, (int)MathF.Ceiling(cy + r));
        for (int y = y0; y <= y1; y++)
        {
            float cos = d.CosLat(y);
            float rx = Math.Min(d.W / 2f, r / cos);
            int x0 = (int)MathF.Floor(cx - rx), x1 = (int)MathF.Ceiling(cx + rx);
            if (x1 - x0 + 1 > d.W) x1 = x0 + d.W - 1; // у полюса круг охватывает всю широту — без повторов
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) * cos, dy = y + 0.5f - cy;
                float dd = MathF.Sqrt(dx * dx + dy * dy);
                if (dd > r || count >= idx.Length) continue;
                idx[count] = y * d.W + d.WrapX(x);
                dist[count] = dd;
                count++;
            }
        }
        return count;
    }

    private static int DiscCapacity(PlanetData d, float r) => (int)(4 * (r + 2) * (Math.Min(d.W / 2f, r * 20f) + 2) + 16);

    private static float Clamp(float v) => Math.Clamp(v, MinElev, MaxElev);

    // ---------- Кисть ----------

    /// <summary>
    /// Один мазок кисти в точке (cx, cy) карты. Купол Гаусса Δ = s·exp(−r²/2σ²), σ = радиус/2 (terraforming.md, §2).
    /// <paramref name="flattenTarget"/> — высота (без уровня моря), к которой тянет «Выровнять».
    /// </summary>
    public static float Dab(PlanetData d, BrushMode mode, float cx, float cy, float radius, float strength, float flattenTarget)
    {
        radius = Math.Clamp(radius, 0.5f, 64f);
        strength = Math.Clamp(strength, 0f, 1f);
        float reach = radius * 1.25f;
        int cap = DiscCapacity(d, reach);
        var idx = new int[cap];
        var dist = new float[cap];
        int n = Disc(d, cx, cy, reach, idx, dist);
        float sigma = radius * 0.5f;
        float inv2s2 = 1f / (2f * sigma * sigma);
        float work = 0;
        float[]? before = null;
        if (mode == BrushMode.Smooth)
        {
            // «Сгладить» читает соседей до мазка: порядок обхода не влияет на результат.
            before = new float[n];
            for (int k = 0; k < n; k++) before[k] = NeighborMean(d, idx[k]);
        }
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            float wgt = MathF.Exp(-dist[k] * dist[k] * inv2s2) * strength;
            if (wgt < 1e-4f) continue;
            switch (mode)
            {
                case BrushMode.Raise:
                case BrushMode.Lower:
                {
                    float dh = (mode == BrushMode.Raise ? 0.12f : -0.12f) * wgt;
                    work += ApplyElev(d, i, dh);
                    break;
                }
                case BrushMode.Flatten:
                    work += ApplyElev(d, i, (flattenTarget - d.Total(i)) * 0.3f * wgt);
                    break;
                case BrushMode.Smooth:
                    // Термальная эрозия (Musgrave, Kolb, Mace 1989): материал сползает к среднему соседей.
                    work += ApplyElev(d, i, (before![k] - d.Total(i)) * 0.5f * wgt);
                    break;
                case BrushMode.Roughen:
                {
                    int x = i % d.W, y = i / d.W;
                    float r = Ridged(x * 0.18f, y * 0.18f);
                    work += ApplyElev(d, i, 0.16f * wgt * (2f * r - 0.9f));
                    break;
                }
                case BrushMode.Wet:
                case BrushMode.Dry:
                {
                    float f = 1f + 0.12f * wgt;
                    float old = d.PrecipMul[i];
                    d.PrecipMul[i] = Math.Clamp(mode == BrushMode.Wet ? old * f : old / f, 0.05f, 6f);
                    work += 2f * MathF.Abs(MathF.Log(d.PrecipMul[i] / old));
                    break;
                }
                case BrushMode.Warm:
                case BrushMode.Cool:
                {
                    float old = d.TempDelta[i];
                    d.TempDelta[i] = Math.Clamp(old + (mode == BrushMode.Warm ? 0.6f : -0.6f) * wgt, -40f, 40f);
                    work += MathF.Abs(d.TempDelta[i] - old) / 5f;
                    break;
                }
                case BrushMode.Fertile:
                case BrushMode.Barren:
                {
                    float old = d.FertDelta[i];
                    d.FertDelta[i] = Math.Clamp(old + (mode == BrushMode.Fertile ? 0.05f : -0.05f) * wgt, -1f, 1f);
                    work += 4f * MathF.Abs(d.FertDelta[i] - old);
                    break;
                }
            }
        }
        return work;
    }

    private static float NeighborMean(PlanetData d, int i)
    {
        int x = i % d.W, y = i / d.W;
        float s = 0; int c = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= d.H) continue;
            for (int dx = -1; dx <= 1; dx++)
            {
                s += d.Total(yy * d.W + d.WrapX(x + dx));
                c++;
            }
        }
        return s / c;
    }

    /// <summary>Добавить к высоте с ограничением; вернуть модуль фактического изменения.</summary>
    private static float ApplyElev(PlanetData d, int i, float dh)
    {
        float t = d.Total(i);
        float nt = Clamp(t + dh);
        d.ElevDelta[i] += nt - t;
        return MathF.Abs(nt - t);
    }

    /// <summary>Поднять до высоты (не ниже текущей).</summary>
    private static float RaiseTo(PlanetData d, int i, float target)
    {
        float t = d.Total(i);
        return target > t ? ApplyElev(d, i, target - t) : 0f;
    }

    /// <summary>Опустить до высоты (не выше текущей).</summary>
    private static float LowerTo(PlanetData d, int i, float target)
    {
        float t = d.Total(i);
        return target < t ? ApplyElev(d, i, target - t) : 0f;
    }

    // ---------- Отрезки (хребет, перешеек, пролив) ----------

    /// <summary>Расстояние от клетки до отрезка a→b (с замыканием долготы) и параметр t∈[0,1] ближайшей точки.</summary>
    private static (float dist, float t) SegmentDistance(PlanetData d, float ax, float ay, float bx, float by, float px, float py)
    {
        float cos = d.CosLat((int)Math.Clamp(py, 0, d.H - 1));
        float vx = WrapDx(d, bx - ax) * cos, vy = by - ay;
        float wx = WrapDx(d, px - ax) * cos, wy = py - ay;
        float len2 = vx * vx + vy * vy;
        float t = len2 > 1e-6f ? Math.Clamp((wx * vx + wy * vy) / len2, 0f, 1f) : 0f;
        float dx = wx - t * vx, dy = wy - t * vy;
        return (MathF.Sqrt(dx * dx + dy * dy), t);
    }

    /// <summary>Клетки в полосе ширины <paramref name="halfWidth"/> вокруг отрезка.</summary>
    private static List<(int i, float dist, float t)> Band(PlanetData d, float ax, float ay, float bx, float by, float halfWidth)
    {
        var res = new List<(int, float, float)>();
        float dxs = WrapDx(d, bx - ax);
        float minX = Math.Min(ax, ax + dxs), maxX = Math.Max(ax, ax + dxs);
        int y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, by) - halfWidth - 1));
        int y1 = Math.Min(d.H - 1, (int)MathF.Ceiling(Math.Max(ay, by) + halfWidth + 1));
        for (int y = y0; y <= y1; y++)
        {
            float pad = Math.Min(d.W / 2f, (halfWidth + 1) / d.CosLat(y));
            for (int x = (int)MathF.Floor(minX - pad); x <= (int)MathF.Ceiling(maxX + pad); x++)
            {
                int wx = d.WrapX(x);
                var (dist, t) = SegmentDistance(d, ax, ay, bx, by, x + 0.5f, y + 0.5f);
                if (dist <= halfWidth) res.Add((y * d.W + wx, dist, t));
            }
        }
        // Одна клетка — одна запись (при длинном отрезке через шов x мог дать повтор).
        res.Sort((p, q) => p.Item1 != q.Item1 ? p.Item1.CompareTo(q.Item1) : p.Item2.CompareTo(q.Item2));
        var uniq = new List<(int, float, float)>(res.Count);
        foreach (var r in res) if (uniq.Count == 0 || uniq[^1].Item1 != r.Item1) uniq.Add(r);
        return uniq;
    }

    /// <summary>Горный хребет: гребень высотой до <paramref name="heightKm"/> вдоль отрезка, склоны по гребнистому шуму.</summary>
    public static float Ridge(PlanetData d, float ax, float ay, float bx, float by, float heightKm)
    {
        const float half = 7f;
        float work = 0;
        foreach (var (i, dist, t) in Band(d, ax, ay, bx, by, half))
        {
            int x = i % d.W, y = i / d.W;
            float taper = MathF.Min(1f, MathF.Min(t, 1f - t) * 6f + 0.25f);
            float across = MathF.Exp(-dist * dist / (2f * 2.6f * 2.6f));
            float rough = 0.55f + 0.45f * Ridged(x * 0.22f, y * 0.22f);
            float t0 = d.Total(i);
            // В море хребет поднимается со дна и выходит над водой цепью островов; на суше растёт от её высоты.
            float baseLine = t0 < d.SeaLevel ? t0 + (d.SeaLevel + 0.3f - t0) * across : t0;
            work += RaiseTo(d, i, baseLine + heightKm * taper * across * rough);
        }
        return work;
    }

    /// <summary>Перешеек: полоса суши 50–150 м над морем, ширина около эко-клетки.</summary>
    public static float Bridge(PlanetData d, float ax, float ay, float bx, float by)
    {
        float work = 0;
        foreach (var (i, dist, _) in Band(d, ax, ay, bx, by, 4.5f))
        {
            int x = i % d.W, y = i / d.W;
            float core = dist <= 2.6f ? 1f : 1f - (dist - 2.6f) / 1.9f;
            float h = d.SeaLevel + (0.05f + 0.1f * ValueNoise(x * 0.3f, y * 0.3f)) * core - 0.12f * (1f - core);
            work += RaiseTo(d, i, h);
        }
        return work;
    }

    /// <summary>Пролив: канал ниже уровня моря через сушу.</summary>
    public static float Strait(PlanetData d, float ax, float ay, float bx, float by)
    {
        float work = 0;
        foreach (var (i, dist, _) in Band(d, ax, ay, bx, by, 3.5f))
        {
            float depth = 0.15f + 0.35f * (1f - dist / 3.5f);
            work += LowerTo(d, i, d.SeaLevel - depth);
        }
        return work;
    }

    // ---------- Точечные формы ----------

    /// <summary>Нормированное расстояние с изрезанным краем: d / (R·(0,75 + 0,5·шум)).</summary>
    private static float Ragged(PlanetData d, int i, float dist, float r)
    {
        int x = i % d.W, y = i / d.W;
        return dist / (r * (0.75f + 0.5f * ValueNoise(x * 0.25f + 3.7f, y * 0.25f - 1.3f)));
    }

    /// <summary>Остров: конус суши с изрезанным берегом.</summary>
    public static float Island(PlanetData d, float cx, float cy, float r = 9f)
    {
        float reach = r * 1.3f;
        var idx = new int[DiscCapacity(d, reach)];
        var dist = new float[idx.Length];
        int n = Disc(d, cx, cy, reach, idx, dist);
        float work = 0;
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            float q = Ragged(d, i, dist[k], r);
            if (q >= 1f) continue;
            float h = d.SeaLevel - 0.25f + 1.9f * MathF.Pow(1f - q, 1.4f);
            work += RaiseTo(d, i, h);
        }
        return work;
    }

    /// <summary>Погрузить землю: диск уходит под воду — залив или внутреннее море.</summary>
    public static float Sink(PlanetData d, float cx, float cy, float r = 9f)
    {
        float reach = r * 1.3f;
        var idx = new int[DiscCapacity(d, reach)];
        var dist = new float[idx.Length];
        int n = Disc(d, cx, cy, reach, idx, dist);
        float work = 0;
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            float q = Ragged(d, i, dist[k], r);
            if (q >= 1f) continue;
            float target = d.SeaLevel - 0.08f - 0.9f * MathF.Pow(1f - q, 0.6f);
            work += LowerTo(d, i, target);
        }
        return work;
    }

    /// <summary>Озеро: чаша ниже окружающей суши; Priority-Flood заполнит её водой.</summary>
    public static float Lake(PlanetData d, float cx, float cy, float r = 4.5f)
    {
        var idx = new int[DiscCapacity(d, r)];
        var dist = new float[idx.Length];
        int n = Disc(d, cx, cy, r, idx, dist);
        float work = 0;
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            if (d.Total(i) < d.SeaLevel) continue;
            work += ApplyElev(d, i, -0.4f * MathF.Pow(1f - dist[k] / r, 1.2f));
        }
        return work;
    }

    /// <summary>
    /// Река: родник в истоке (добавка к стоку чуть выше порога реки) и русло, прорезанное по последнему дереву стока.
    /// Сток копится вниз по течению, поэтому река доходит до моря или озера (terraforming.md).
    /// </summary>
    public static float River(PlanetData d, float cx, float cy)
    {
        int x = Math.Clamp((int)cx, 0, d.W - 1), y = Math.Clamp((int)cy, 0, d.H - 1);
        int src = y * d.W + d.WrapX(x);
        if (d.Total(src) < d.SeaLevel) return 0f;
        float thr = d.RiverThreshold > 0 && d.RiverThreshold < float.MaxValue ? d.RiverThreshold : 1f;
        d.ExtraFlow[src] += thr * 1.15f;
        float work = thr * 0.05f;
        int c = src;
        for (int step = 0; step < 4000 && c >= 0; step++)
        {
            if (d.Total(c) < d.SeaLevel) break;
            work += ApplyElev(d, c, -0.03f);
            c = d.Downstream[c];
        }
        return work;
    }

    /// <summary>Вулкан: конус с кратером, руда на склонах. Возвращает работу.</summary>
    public static float Volcano(PlanetData d, float cx, float cy, float r = 5.5f)
    {
        var idx = new int[DiscCapacity(d, r)];
        var dist = new float[idx.Length];
        int n = Disc(d, cx, cy, r, idx, dist);
        int x = Math.Clamp((int)cx, 0, d.W - 1), y = Math.Clamp((int)cy, 0, d.H - 1);
        float baseH = Math.Max(d.Total(y * d.W + d.WrapX(x)), d.SeaLevel + 0.1f);
        float work = 0;
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            float q = dist[k] / r;
            float h = baseH + 2.6f * MathF.Pow(1f - q, 1.6f);
            if (q < 0.12f) h -= 0.35f; // кратер
            work += RaiseTo(d, i, h);
            d.OreField[i] = Math.Min(1f, d.OreField[i] + 0.35f * (1f - q));
        }
        return work;
    }

    /// <summary>Множитель осадков в круге: оазис (×≈2,5 в центре) или пустыня (×≈0,3).</summary>
    public static float Moisture(PlanetData d, float cx, float cy, bool wet, float r = 6f)
    {
        var idx = new int[DiscCapacity(d, r * 1.3f)];
        var dist = new float[idx.Length];
        int n = Disc(d, cx, cy, r * 1.3f, idx, dist);
        float work = 0;
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            float wgt = MathF.Exp(-dist[k] * dist[k] / (2f * (r * 0.55f) * (r * 0.55f)));
            float old = d.PrecipMul[i];
            d.PrecipMul[i] = Math.Clamp(wet ? old * (1f + 1.5f * wgt) : old * (1f - 0.7f * wgt), 0.05f, 6f);
            if (wet) d.FertDelta[i] = Math.Min(1f, d.FertDelta[i] + 0.1f * wgt);
            work += 2f * MathF.Abs(MathF.Log(d.PrecipMul[i] / old));
        }
        return work;
    }

    /// <summary>Ледник (−8 °C) или «растопить льды» (+8 °C) в круге — постоянно.</summary>
    public static float Temperature(PlanetData d, float cx, float cy, float deltaC, float r = 6f)
    {
        var idx = new int[DiscCapacity(d, r * 1.3f)];
        var dist = new float[idx.Length];
        int n = Disc(d, cx, cy, r * 1.3f, idx, dist);
        float work = 0;
        for (int k = 0; k < n; k++)
        {
            int i = idx[k];
            float wgt = MathF.Exp(-dist[k] * dist[k] / (2f * (r * 0.55f) * (r * 0.55f)));
            float old = d.TempDelta[i];
            d.TempDelta[i] = Math.Clamp(old + deltaC * wgt, -40f, 40f);
            work += MathF.Abs(d.TempDelta[i] - old) / 5f;
        }
        return work;
    }

    // ---------- Дрейф материка ----------

    /// <summary>
    /// Сдвигает связную сушу (с шельфом), которой принадлежит клетка (ax, ay), на (dx, dy) эко-клеток.
    /// Освобождённое место становится океанским дном, при столкновении с другой сушей растёт хребет.
    /// Слои, «едущие» с сушей: высота, плодородие, руда, родники. Климатические слои остаются на месте.
    /// Возвращает null, если в точке нет суши.
    /// </summary>
    public static DriftResult? Drift(PlanetData d, int ax, int ay, int dxE, int dyE)
    {
        int n = d.W * d.H;
        ax = d.WrapX(ax);
        ay = Math.Clamp(ay, 0, d.H - 1);
        int seed = ay * d.W + ax;
        float shelf = d.SeaLevel - 0.3f;
        if (d.Total(seed) < d.SeaLevel) return null;
        if (dxE == 0 && dyE == 0) return null;
        // Связная суша с шельфом (4-соседство, долгота замкнута).
        var mask = new bool[n];
        var stack = new int[n];
        int sp = 0, size = 0;
        stack[sp++] = seed; mask[seed] = true;
        while (sp > 0)
        {
            int c = stack[--sp]; size++;
            int x = c % d.W, y = c / d.W;
            for (int k = 0; k < 4; k++)
            {
                int nx = x, ny = y;
                if (k == 0) nx = d.WrapX(x + 1); else if (k == 1) nx = d.WrapX(x - 1); else if (k == 2) ny = y + 1; else ny = y - 1;
                if (ny < 0 || ny >= d.H) continue;
                int j = ny * d.W + nx;
                if (mask[j] || d.Total(j) < shelf) continue;
                mask[j] = true; stack[sp++] = j;
            }
        }
        var res = new DriftResult { Dx = dxE, Dy = dyE, FineCells = size, EcoMoved = new bool[d.EW * d.EH] };
        // Эко-клетка «едет», если больше половины её клеток карты — в сдвигаемой суше.
        int s = d.Scale;
        for (int ey = 0; ey < d.EH; ey++)
        for (int ex = 0; ex < d.EW; ex++)
        {
            int cnt = 0;
            for (int yy = 0; yy < s; yy++)
            for (int xx = 0; xx < s; xx++)
                if (mask[(ey * s + yy) * d.W + ex * s + xx]) cnt++;
            res.EcoMoved[ey * d.EW + ex] = cnt * 2 > s * s;
        }
        int fdx = dxE * s, fdy = dyE * s;
        var total = new float[n];
        for (int i = 0; i < n; i++) total[i] = d.Total(i);
        var newTotal = (float[])total.Clone();
        var newFert = (float[])d.FertDelta.Clone();
        var newOre = (float[])d.OreField.Clone();
        var newFlow = (float[])d.ExtraFlow.Clone();
        var newCoal = (float[])d.Coal.Clone();
        var newOil = (float[])d.Oil.Clone();
        var newUr = (float[])d.Uranium.Clone();
        // Ушедшая суша оставляет океанское дно.
        for (int i = 0; i < n; i++)
        {
            if (!mask[i]) continue;
            int x = i % d.W, y = i / d.W;
            newTotal[i] = d.SeaLevel - 2.6f - 0.6f * ValueNoise(x * 0.1f, y * 0.1f);
            newFert[i] = 0; newOre[i] = 0; newFlow[i] = 0; newCoal[i] = 0; newOil[i] = 0; newUr[i] = 0;
        }
        for (int i = 0; i < n; i++)
        {
            if (!mask[i]) continue;
            int x = i % d.W, y = i / d.W;
            int ny = y + fdy;
            if (ny < 0 || ny >= d.H) continue; // ушло за полюс
            int j = ny * d.W + d.WrapX(x + fdx);
            if (!mask[j] && total[j] >= shelf)
            {
                // Столкновение с другой сушей: складчатые горы (как Гималаи при встрече плит).
                newTotal[j] = Clamp(Math.Max(total[j], total[i]) + 1.2f + 0.8f * Ridged(x * 0.2f, y * 0.2f));
                newOre[j] = Math.Max(d.OreField[j], d.OreField[i]);
                res.CollisionCells++;
            }
            else
            {
                newTotal[j] = total[i];
                newOre[j] = d.OreField[i];
            }
            newFert[j] = d.FertDelta[i];
            newFlow[j] = d.ExtraFlow[i];
            newCoal[j] = d.Coal[i]; newOil[j] = d.Oil[i]; newUr[j] = d.Uranium[i];
        }
        for (int i = 0; i < n; i++) d.ElevDelta[i] = newTotal[i] - d.BaseElevation[i];
        Array.Copy(newFert, d.FertDelta, n);
        Array.Copy(newOre, d.OreField, n);
        Array.Copy(newFlow, d.ExtraFlow, n);
        Array.Copy(newCoal, d.Coal, n);
        Array.Copy(newOil, d.Oil, n);
        Array.Copy(newUr, d.Uranium, n);
        return res;
    }

    // ---------- Снимок слоёв (отмена в лепке до жизни, «как природа») ----------

    public sealed class Layers
    {
        public float[] ElevDelta = Array.Empty<float>(), PrecipMul = Array.Empty<float>(), TempDelta = Array.Empty<float>(),
            FertDelta = Array.Empty<float>(), OreField = Array.Empty<float>(), ExtraFlow = Array.Empty<float>(),
            Coal = Array.Empty<float>(), Oil = Array.Empty<float>(), Uranium = Array.Empty<float>();
        public float SeaLevel, GlobalTempDelta;
    }

    public static Layers Snapshot(PlanetData d) => new()
    {
        ElevDelta = (float[])d.ElevDelta.Clone(), PrecipMul = (float[])d.PrecipMul.Clone(), TempDelta = (float[])d.TempDelta.Clone(),
        FertDelta = (float[])d.FertDelta.Clone(), OreField = (float[])d.OreField.Clone(), ExtraFlow = (float[])d.ExtraFlow.Clone(),
        Coal = (float[])d.Coal.Clone(), Oil = (float[])d.Oil.Clone(), Uranium = (float[])d.Uranium.Clone(),
        SeaLevel = d.SeaLevel, GlobalTempDelta = d.GlobalTempDelta,
    };

    public static void Restore(PlanetData d, Layers l)
    {
        Array.Copy(l.ElevDelta, d.ElevDelta, l.ElevDelta.Length);
        Array.Copy(l.PrecipMul, d.PrecipMul, l.PrecipMul.Length);
        Array.Copy(l.TempDelta, d.TempDelta, l.TempDelta.Length);
        Array.Copy(l.FertDelta, d.FertDelta, l.FertDelta.Length);
        Array.Copy(l.OreField, d.OreField, l.OreField.Length);
        Array.Copy(l.ExtraFlow, d.ExtraFlow, l.ExtraFlow.Length);
        if (l.Coal.Length > 0) { Array.Copy(l.Coal, d.Coal, l.Coal.Length); Array.Copy(l.Oil, d.Oil, l.Oil.Length); Array.Copy(l.Uranium, d.Uranium, l.Uranium.Length); }
        d.SeaLevel = l.SeaLevel;
        d.GlobalTempDelta = l.GlobalTempDelta;
    }
}
