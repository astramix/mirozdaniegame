using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Life;

/// <summary>
/// Экология популяций на эко-сетке + управление особями, видообразование, вымирание, порог разума.
/// docs/design/life.md.
/// </summary>
public sealed class LifeSystem
{
    public const int MaxSpecies = 96;

    public readonly List<Species> Species = new();
    public readonly float[] Plant;            // биомасса растений, доля от ёмкости (0…1)
    public readonly float[] HerbivoreBiomass; // сумма биомассы травоядной части
    public readonly float[] PredationPressure;
    public readonly Agents Agents;
    private readonly PlanetData _planet;
    private int[] _landCells;
    private readonly float[] _tmp;
    private readonly float[] _meanBuf = new float[Genome.G * 2];

    public float SpeciationThreshold = 0.22f;
    private readonly float[] _sums;
    private readonly float[] _rate = new float[MaxSpecies];
    private readonly float[] _m13 = new float[MaxSpecies];
    private readonly int[] _counts;

    public LifeSystem(PlanetData planet, int maxAgents)
    {
        _planet = planet;
        int n = planet.EW * planet.EH;
        Plant = new float[n];
        HerbivoreBiomass = new float[n];
        PredationPressure = new float[n];
        _tmp = new float[n];
        var land = new List<int>();
        for (int i = 0; i < n; i++) if (planet.EIsLand(i)) land.Add(i);
        _landCells = land.ToArray();
        Agents = new Agents(maxAgents, n);
        Agents.Bind(planet);
        _sums = new float[MaxSpecies * Genome.G];
        _counts = new int[MaxSpecies];
    }

    public IReadOnlyList<int> LandCells => _landCells;

    /// <summary>
    /// Суша изменилась (терраформирование, docs/design/terraforming.md, §4): пересобрать список суши.
    /// В ушедших под воду клетках растения и звери гибнут, новая суша получает треть ёмкости растений.
    /// </summary>
    public void RefreshLand(bool[] wasLand)
    {
        var land = new List<int>(_landCells.Length + 64);
        for (int c = 0; c < wasLand.Length; c++)
        {
            bool now = _planet.EIsLand(c);
            if (now) land.Add(c);
            if (wasLand[c] && !now)
            {
                Plant[c] = 0;
                foreach (var s in Species) if (s.Biomass.Length > c) s.Biomass[c] = 0;
            }
            else if (!wasLand[c] && now) Plant[c] = Capacity(c) * 0.3f;
        }
        _landCells = land.ToArray();
        RecomputeTotals();
    }

    /// <summary>Дрейф материка: растения и биомасса видов в уехавших клетках переезжают на (dx, dy).</summary>
    public void ShiftCells(bool[] moved, int dx, int dy)
    {
        int ew = _planet.EW, eh = _planet.EH;
        void ShiftArray(float[] a)
        {
            var src = (float[])a.Clone();
            for (int c = 0; c < moved.Length; c++) if (moved[c]) a[c] = 0;
            for (int c = 0; c < moved.Length; c++)
            {
                if (!moved[c]) continue;
                int y = c / ew + dy;
                if (y < 0 || y >= eh) continue;
                a[y * ew + (((c % ew) + dx) % ew + ew) % ew] = src[c];
            }
        }
        ShiftArray(Plant);
        foreach (var s in Species) ShiftArray(s.Biomass);
    }

    public float Capacity(int c) => _planet.ENpp[c] / 3000f;

    public Species NewSpecies(ReadOnlySpan<float> genome, int parent, int tick, int originCell)
    {
        var s = new Species { Id = Species.Count, Parent = parent, BornTick = tick, OriginCell = originCell };
        genome.CopyTo(s.Genome);
        s.Biomass = new float[_planet.EW * _planet.EH];
        Species.Add(s);
        return s;
    }

    /// <summary>Три первичных вида вокруг архетипов (травоядный, всеядный, хищник).</summary>
    public void Seed(Rng rng, EventBus bus, WorldParams p)
    {
        for (int i = 0; i < _landCells.Length; i++) Plant[_landCells[i]] = Capacity(_landCells[i]) * 0.8f;
        Span<float> g = stackalloc float[Genome.G];
        float[][] archetypes =
        {
            // Mass Speed Senses Diet Fec Life Topt W   Soc  Cog  Hue Limbs Eyes Lips Tongue Nasal Larynx
            new[] { 0.3f, 0.5f, 0.5f, 0.05f, 0.7f, 0.3f, 16f, 10f, 0.4f, 0.2f, 0.3f, 0.5f, 0.4f, 0.5f, 0.5f, 0.5f, 0.5f },
            new[] { 1.2f, 0.5f, 0.5f, 0.45f, 0.5f, 0.5f, 14f, 10f, 0.5f, 0.3f, 0.6f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
            new[] { 1.5f, 0.7f, 0.6f, 0.9f, 0.4f, 0.5f, 15f, 9f, 0.4f, 0.28f, 0.9f, 0.6f, 0.6f, 0.5f, 0.5f, 0.5f, 0.5f },
        };
        for (int a = 0; a < archetypes.Length; a++)
        {
            archetypes[a].CopyTo(g);
            for (int t = 0; t < Genome.G; t++)
            {
                float range = Genome.Max[t] - Genome.Min[t];
                float jitter = t >= Trait.Hue ? 0.45f : 0.08f;
                g[t] = Math.Clamp(g[t] + rng.NextGaussian() * jitter * range, Genome.Min[t], Genome.Max[t]);
            }
            // Стартовая клетка: случайная суша с хорошей продуктивностью.
            int origin = _landCells[0];
            float best = -1;
            for (int k = 0; k < 40; k++)
            {
                int c = _landCells[rng.NextInt(_landCells.Length)];
                float v = Capacity(c) * Genome.ThermalSuitability(g, _planet.ETemp[c]) + rng.NextFloat() * 0.1f;
                if (v > best) { best = v; origin = c; }
            }
            var s = NewSpecies(g, -1, 0, origin);
            int ox = origin % _planet.EW, oy = origin / _planet.EW;
            for (int i = 0; i < _landCells.Length; i++)
            {
                int c = _landCells[i];
                float dist = _planet.EcoDistance(c, origin);
                if (dist < 14) s.Biomass[c] = 0.3f * (1 - dist / 14f) * (s.Diet > 0.6f ? 0.3f : 1f);
            }
            for (int k = 0; k < 1500; k++)
            {
                float x = ox + 0.5f + rng.NextGaussian() * 5f, y = Math.Clamp(oy + 0.5f + rng.NextGaussian() * 4f, 0, _planet.EH - 0.01f);
                x = ((x % _planet.EW) + _planet.EW) % _planet.EW;
                int c = (int)y * _planet.EW + (int)x;
                if (!_planet.EIsLand(c)) continue;
                Agents.Spawn(s.Id, x, y, s.Genome, 0.8f);
            }
        }
        RecomputeTotals();
        bus.Publish(EventType.LifeBegan, archetypes.Length > 0 ? Species[0].OriginCell : -1, a: archetypes.Length);
    }

    private void RecomputeTotals()
    {
        Array.Clear(HerbivoreBiomass);
        Array.Clear(PredationPressure);
        foreach (var s in Species)
        {
            if (!s.Alive) continue;
            float tot = 0;
            float herbShare = 1 - s.Diet;
            for (int i = 0; i < _landCells.Length; i++)
            {
                int c = _landCells[i];
                float b = s.Biomass[c];
                tot += b;
                HerbivoreBiomass[c] += b * herbShare;
                PredationPressure[c] += b * s.Diet;
            }
            s.TotalBiomass = tot;
        }
        for (int i = 0; i < _landCells.Length; i++)
        {
            int c = _landCells[i];
            PredationPressure[c] = Math.Min(1f, PredationPressure[c] * 4f);
        }
    }

    /// <summary>
    /// Шаг экологии (тик = 1000 лет). Растения — логистика; потребители — Холлинг II;
    /// хищники едят травоядную часть — колебания Лотки–Вольтерры возникают сами.
    /// </summary>
    public void EcologyTick(float[] tempOffset, float[] nppFactor)
    {
        var sp = Species;
        // Кэш на вид: темп размножения (MTE) и M^{1/3}.
        for (int s = 0; s < sp.Count; s++)
        {
            _rate[s] = Genome.MaxGrowthRate(Genome.MassKg(sp[s].LogMass), sp[s].Genome[Trait.Fecundity]);
            _m13[s] = MathF.Pow(10f, sp[s].LogMass / 3f);
        }
        for (int i = 0; i < _landCells.Length; i++)
        {
            int c = _landCells[i];
            float K = Capacity(c) * nppFactor[c];
            float V = Plant[c];
            float prey = HerbivoreBiomass[c];
            float temp = _planet.ETemp[c] + tempOffset[c];
            float grazeTotal = 0, huntTotal = 0;
            // Холлинг II с полунасыщением 0,3 (растения) и 0,15 (добыча).
            float grazeF = V / (0.3f + V);
            float huntF = prey / (0.15f + prey);
            for (int s = 0; s < sp.Count; s++)
            {
                var x = sp[s];
                if (!x.Alive || x.Sapient) continue;
                float B = x.Biomass[c];
                if (B <= 0) continue;
                float r = _rate[s];
                float suit = Genome.ThermalSuitability(x.Genome, temp, _m13[s]);
                float graze = 0.9f * r * grazeF * B * (1 - x.Diet);
                float hunt = 0.9f * r * huntF * B * x.Diet;
                grazeTotal += graze; huntTotal += hunt;
                // Удельная смертность ∝ M^−0,25 (Клайбер), мозг дорог (Aiello & Wheeler 1995).
                float mort = 0.25f * r * (1f + 0.6f * x.Genome[Trait.Cognition]) + 0.3f * (1 - suit) * r;
                float gain = 1.2f * (graze + hunt) * suit;
                _tmp[c] = 0; // не используется здесь
                x.Biomass[c] = Math.Max(0f, B + gain - mort * B - 0.4f * B * B);
            }
            // Потери добычи от хищников — пропорционально травоядной части.
            if (huntTotal > 0 && prey > 1e-6f)
            {
                float lossFrac = Math.Min(0.9f, huntTotal / prey);
                for (int s = 0; s < sp.Count; s++)
                {
                    var x = sp[s];
                    if (!x.Alive || x.Sapient) continue;
                    x.Biomass[c] *= 1f - lossFrac * (1 - x.Diet);
                }
            }
            Plant[c] = Math.Clamp(V + 0.8f * V * (1 - V / Math.Max(1e-3f, K)) - Math.Min(V * 0.9f, grazeTotal) + 0.002f * K, 0f, Math.Max(K, 0.0001f));
        }
        Diffuse(tempOffset);
        RecomputeTotals();
    }

    /// <summary>Миграция: 5 % биомассы растекается к соседям пропорционально пригодности.</summary>
    private void Diffuse(float[] tempOffset)
    {
        int ew = _planet.EW, eh = _planet.EH;
        Span<int> nb = stackalloc int[4];
        Span<float> nw = stackalloc float[4];
        foreach (var x in Species)
        {
            if (!x.Alive || x.Sapient) continue;
            Array.Clear(_tmp);
            float speed = 0.04f + 0.06f * x.Genome[Trait.Speed];
            for (int i = 0; i < _landCells.Length; i++)
            {
                int c = _landCells[i];
                float B = x.Biomass[c];
                if (B < 1e-5f) { _tmp[c] += B; continue; }
                float out_ = B * speed;
                _tmp[c] += B - out_;
                int cx = c % ew, cy = c / ew;
                float wsum = 0;
                int nn = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ny < 0 || ny >= eh) continue;
                    int j = ny * ew + ((nx + ew) % ew);
                    if (!_planet.EIsLand(j)) continue;
                    float w = 0.05f + Genome.ThermalSuitability(x.Genome, _planet.ETemp[j] + tempOffset[j], _m13[x.Id]) * (0.2f + Plant[j]);
                    nb[nn] = j; nw[nn] = w; nn++; wsum += w;
                }
                if (nn == 0) { _tmp[c] += out_; continue; }
                for (int k = 0; k < nn; k++) _tmp[nb[k]] += out_ * nw[k] / wsum;
            }
            for (int i = 0; i < _landCells.Length; i++)
            {
                int c = _landCells[i];
                x.Biomass[c] = _tmp[c] < 1e-6f ? 0f : _tmp[c];
            }
        }
    }

    /// <summary>Геном вида = средний геном его особей (раз в 10 тиков).</summary>
    public void UpdateSpeciesGenomes()
    {
        var ag = Agents;
        int G = Genome.G;
        var sums = _sums; var counts = _counts;
        Array.Clear(sums); Array.Clear(counts);
        for (int i = 0; i < ag.Capacity; i++)
        {
            if (!ag.Alive[i]) continue;
            int s = ag.Species[i];
            counts[s]++;
            var g = ag.GenesOf(i);
            for (int t = 0; t < G; t++) sums[s * G + t] += g[t];
        }
        for (int s = 0; s < Species.Count; s++)
        {
            Species[s].AgentCount = counts[s];
            if (counts[s] == 0) continue;
            for (int t = 0; t < G; t++) Species[s].Genome[t] = sums[s * G + t] / counts[s];
        }
    }

    /// <summary>
    /// Аллопатрическое видообразование (Mayr 1942): 2-means по положению особей вида,
    /// новый вид — если кластеры далеко и генетически разошлись.
    /// </summary>
    public int TrySpeciation(Species sp, int tick, EventBus bus)
    {
        if (!sp.Alive || sp.Sapient || Species.Count >= MaxSpecies) return -1;
        var ag = Agents;
        int ew = _planet.EW;
        // Цилиндрические координаты, чтобы долгота была замкнута.
        float R = ew / (2 * MathF.PI);
        int first = -1, n = 0;
        for (int i = 0; i < ag.Capacity; i++) if (ag.Alive[i] && ag.Species[i] == sp.Id) { if (first < 0) first = i; n++; }
        if (n < 80) return -1;
        (float, float, float) P(int i) { float a = ag.X[i] / ew * 2 * MathF.PI; return (MathF.Cos(a) * R, MathF.Sin(a) * R, ag.Y[i]); }
        float D2((float, float, float) a, (float, float, float) b) { float dx = a.Item1 - b.Item1, dy = a.Item2 - b.Item2, dz = a.Item3 - b.Item3; return dx * dx + dy * dy + dz * dz; }
        int Far((float, float, float) from)
        {
            int best = first; float bd = -1;
            for (int i = 0; i < ag.Capacity; i++)
            {
                if (!ag.Alive[i] || ag.Species[i] != sp.Id) continue;
                float d = D2(P(i), from);
                if (d > bd) { bd = d; best = i; }
            }
            return best;
        }
        int a1 = Far(P(first));
        int a2 = Far(P(a1));
        var c1 = P(a1); var c2 = P(a2);
        int n1 = 0, n2 = 0;
        for (int it = 0; it < 4; it++)
        {
            float s1x = 0, s1y = 0, s1z = 0, s2x = 0, s2y = 0, s2z = 0; n1 = n2 = 0;
            for (int i = 0; i < ag.Capacity; i++)
            {
                if (!ag.Alive[i] || ag.Species[i] != sp.Id) continue;
                var p = P(i);
                if (D2(p, c1) <= D2(p, c2)) { s1x += p.Item1; s1y += p.Item2; s1z += p.Item3; n1++; }
                else { s2x += p.Item1; s2y += p.Item2; s2z += p.Item3; n2++; }
            }
            if (n1 == 0 || n2 == 0) return -1;
            c1 = (s1x / n1, s1y / n1, s1z / n1); c2 = (s2x / n2, s2y / n2, s2z / n2);
        }
        float spatial = MathF.Sqrt(D2(c1, c2));
        if (spatial < 12f || Math.Min(n1, n2) < 30) return -1;
        // Средние геномы кластеров.
        int G = Genome.G;
        var m1 = _meanBuf.AsSpan(0, G); var m2 = _meanBuf.AsSpan(G, G);
        m1.Clear(); m2.Clear();
        for (int i = 0; i < ag.Capacity; i++)
        {
            if (!ag.Alive[i] || ag.Species[i] != sp.Id) continue;
            var p = P(i);
            var g = ag.GenesOf(i);
            var m = D2(p, c1) <= D2(p, c2) ? m1 : m2;
            for (int t = 0; t < G; t++) m[t] += g[t];
        }
        for (int t = 0; t < G; t++) { m1[t] /= n1; m2[t] /= n2; }
        float gd = Genome.Distance(m1, m2);
        if (gd < SpeciationThreshold) return -1;

        bool secondSmaller = n2 <= n1;
        var newMean = secondSmaller ? m2 : m1;
        var newCenter = secondSmaller ? c2 : c1;
        var oldCenter = secondSmaller ? c1 : c2;
        // Клетка-центр нового вида.
        float ang = MathF.Atan2(newCenter.Item2, newCenter.Item1);
        if (ang < 0) ang += 2 * MathF.PI;
        int ox = Math.Clamp((int)(ang / (2 * MathF.PI) * ew), 0, ew - 1);
        int oy = Math.Clamp((int)newCenter.Item3, 0, _planet.EH - 1);
        var ns = NewSpecies(newMean, sp.Id, tick, oy * ew + ox);
        for (int i = 0; i < ag.Capacity; i++)
        {
            if (!ag.Alive[i] || ag.Species[i] != sp.Id) continue;
            var p = P(i);
            if ((D2(p, newCenter) < D2(p, oldCenter))) ag.Species[i] = ns.Id;
        }
        // Биомасса делится по ближайшему центру.
        for (int k = 0; k < _landCells.Length; k++)
        {
            int c = _landCells[k];
            float a = (c % ew + 0.5f) / ew * 2 * MathF.PI;
            var p = (MathF.Cos(a) * R, MathF.Sin(a) * R, c / ew + 0.5f);
            if (D2(p, newCenter) < D2(p, oldCenter)) { ns.Biomass[c] = sp.Biomass[c]; sp.Biomass[c] = 0; }
        }
        bus.Publish(EventType.SpeciesBorn, ns.OriginCell, a: ns.Id, b: sp.Id, magnitude: gd);
        return ns.Id;
    }

    public void CheckExtinctions(int tick, EventBus bus)
    {
        foreach (var s in Species)
        {
            if (!s.Alive || s.Sapient) continue;
            if (s.TotalBiomass < 0.02f && Agents.CountOf(s.Id) == 0)
            {
                s.ExtinctTick = tick;
                Array.Clear(s.Biomass);
                bus.Publish(EventType.SpeciesExtinct, s.OriginCell, a: s.Id);
            }
            else if (Agents.CountOf(s.Id) == 0 && s.TotalBiomass < 0.5f)
            {
                s.ExtinctTick = tick;
                Array.Clear(s.Biomass);
                bus.Publish(EventType.SpeciesExtinct, s.OriginCell, a: s.Id);
            }
        }
    }

    /// <summary>Вид с биомассой, но без особей, получает особей в клетке наибольшей биомассы.</summary>
    public void Reseed(Rng rng)
    {
        foreach (var s in Species)
        {
            if (!s.Alive || s.Sapient || Agents.CountOf(s.Id) > 20 || s.TotalBiomass < 0.3f) continue;
            int best = -1; float bv = 0;
            for (int k = 0; k < _landCells.Length; k++)
            {
                int c = _landCells[k];
                if (s.Biomass[c] > bv) { bv = s.Biomass[c]; best = c; }
            }
            if (best < 0) continue;
            for (int k = 0; k < 60; k++)
                Agents.Spawn(s.Id, best % _planet.EW + rng.NextFloat(), best / _planet.EW + rng.NextFloat(), s.Genome, 0.5f);
        }
    }

    /// <summary>Вид, достигший порога разума, или null.</summary>
    public Species? CheckAwakening(WorldParams p)
    {
        Species? best = null;
        float bestScore = 0;
        foreach (var s in Species)
        {
            if (!s.Alive || s.Sapient || s.AgentCount < 60) continue;
            float cog = s.Genome[Trait.Cognition], soc = s.Genome[Trait.Sociality];
            if (cog > p.MindThreshold - s.ThresholdBonus && soc > p.SocialThreshold - s.ThresholdBonus * 0.5f)
            {
                float score = cog * soc;
                if (score > bestScore) { bestScore = score; best = s; }
            }
        }
        return best;
    }

    public Species BestMindCandidate()
    {
        Species? best = null;
        float bs = -1;
        foreach (var s in Species)
        {
            if (!s.Alive || s.Sapient) continue;
            float score = s.Genome[Trait.Cognition] * (0.5f + s.Genome[Trait.Sociality]) + 0.01f * MathF.Min(1, s.TotalBiomass);
            if (score > bs) { bs = score; best = s; }
        }
        return best ?? Species[0];
    }

    /// <summary>Биомасса вида в клетке после Пробуждения (фауна для охотников).</summary>
    public float FaunaAt(int c)
    {
        float sum = 0;
        foreach (var s in Species) if (s.Alive && !s.Sapient) sum += s.Biomass[c];
        return sum;
    }
}
