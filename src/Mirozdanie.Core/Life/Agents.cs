using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Life;

/// <summary>
/// Пул особей (структура массивов, без аллокаций в тике). Особи — носители отбора:
/// их средний геном становится геномом вида. Численность вида задаёт экология (квота ∝ биомассе),
/// а внутри квоты идёт локальный турнирный отбор (процесс Морана): более приспособленная особь
/// клетки замещает менее приспособленную своим мутировавшим потомком. См. docs/design/life.md.
/// </summary>
public sealed class Agents
{
    public readonly int Capacity;
    public readonly float[] X, Y, Energy;
    public readonly int[] Age, Species;
    public readonly bool[] Alive;
    public readonly float[] Genes; // Capacity × G
    private readonly int[] _free;
    private int _freeCount;
    public int Count { get; private set; }

    // Буферы тика (заранее выделены).
    private readonly int[] _cellOf;
    private readonly float[] _cellWeightHerb, _cellWeightCarn;
    private readonly int[] _cellStart, _cellCount, _cellItems;
    // Кэш степеней массы на особь (считается один раз за тик).
    private readonly float[] _m075, _w085, _m13, _mInv12, _m12, _riskMass;
    private readonly int[] _speciesQuota = new int[LifeSystem.MaxSpecies];
    private readonly int[] _speciesCount = new int[LifeSystem.MaxSpecies];

    /// <summary>Сколько «норм» пищи даёт единица биомассы растений/добычи в клетке.</summary>
    public static float FoodPerPlant = 7f;
    public static float FoodPerPrey = 10f;
    /// <summary>Доля особей, участвующих в турнире за тик.</summary>
    public static float TournamentRate = 0.35f;

    public Agents(int capacity, int ecoCells)
    {
        Capacity = capacity;
        X = new float[capacity]; Y = new float[capacity]; Energy = new float[capacity];
        Age = new int[capacity]; Species = new int[capacity]; Alive = new bool[capacity];
        Genes = new float[capacity * Genome.G];
        _free = new int[capacity];
        for (int i = 0; i < capacity; i++) _free[i] = capacity - 1 - i;
        _freeCount = capacity;
        _cellOf = new int[capacity];
        _cellWeightHerb = new float[ecoCells];
        _cellWeightCarn = new float[ecoCells];
        _cellStart = new int[ecoCells + 1];
        _cellCount = new int[ecoCells];
        _cellItems = new int[capacity];
        _m075 = new float[capacity]; _w085 = new float[capacity]; _m13 = new float[capacity];
        _mInv12 = new float[capacity]; _m12 = new float[capacity]; _riskMass = new float[capacity];
    }

    public Span<float> GenesOf(int i) => Genes.AsSpan(i * Genome.G, Genome.G);

    public int Spawn(int species, float x, float y, ReadOnlySpan<float> genes, float energy)
    {
        if (_freeCount == 0) return -1;
        int i = _free[--_freeCount];
        Alive[i] = true; Species[i] = species; X[i] = x; Y[i] = y; Energy[i] = energy; Age[i] = 0;
        genes.CopyTo(GenesOf(i));
        Count++;
        _speciesCount[species]++;
        return i;
    }

    public void Kill(int i)
    {
        if (!Alive[i]) return;
        Alive[i] = false;
        _free[_freeCount++] = i;
        Count--;
        _speciesCount[Species[i]]--;
    }

    public int CountOf(int species) => _speciesCount[species];

    /// <summary>Степени массы особи: M^0,75 (Клайбер), M^0,85 (доля в дележе), M^{1/3}, M^{±1/12} (терморегуляция).</summary>
    private void UpdateCache(int i)
    {
        var g = GenesOf(i);
        float logM = g[Trait.Mass];
        float m112 = MathF.Pow(10f, logM / 12f);
        float m3 = m112 * m112 * m112;
        _mInv12[i] = 1f / m112;
        _m12[i] = m112;
        _m075[i] = m3 * m3 * m3;
        _m13[i] = m3 * m112;
        _riskMass[i] = 1f + MathF.Pow(10f, logM - 1f);
        _w085[i] = MathF.Pow(10f, 0.85f * logM) * (0.6f + 0.4f * g[Trait.Senses]);
    }

    public void RecountSpecies()
    {
        Array.Clear(_speciesCount);
        for (int i = 0; i < Capacity; i++) if (Alive[i]) _speciesCount[Species[i]]++;
    }

    /// <summary>
    /// Приспособленность особи: энергетический баланс (пища − потребность)/потребность.
    /// Потребность ∝ M^0,75 (Клайбер), мозг дорог (Aiello &amp; Wheeler 1995), разум окупается
    /// в изменчивой среде (Potts 1998) и в обществе (Dunbar 1998), социальность защищает от хищников.
    /// </summary>
    private float Fitness(int i, int c, LifeSystem life, PlanetData planet, float[] tempOffset)
    {
        var g = GenesOf(i);
        float m075 = _m075[i];
        float diet = g[Trait.Diet], cog = g[Trait.Cognition], soc = g[Trait.Sociality];
        float temp = planet.ETemp[c] + tempOffset[c];
        float suit = Genome.ThermalSuitability(g, temp, _m13[i]);
        float var = planet.ESeason[c] / 12f;
        float pred = life.PredationPressure[c];

        float brain = 1f + 1.5f * cog * (1f - 0.35f * cog) * (0.15f + var) * (0.4f + 0.9f * soc);
        float social = 1f + 0.35f * soc * pred + 0.3f * soc * diet;
        float eff = (0.6f + 0.4f * g[Trait.Senses]) * brain * social;
        float w = _w085[i];
        float herbFood = life.Plant[c] * FoodPerPlant * w / Math.Max(1e-3f, _cellWeightHerb[c]);
        float carnFood = life.HerbivoreBiomass[c] * FoodPerPrey * w / Math.Max(1e-3f, _cellWeightCarn[c]);
        float food = ((1 - diet) * herbFood + diet * carnFood) * eff * suit;
        float need = m075 * (1f + 0.6f * cog) * (1f + 0.3f * g[Trait.Speed]) * (1f + 0.25f * soc);
        // Терморегуляция в холоде: теплопотеря ∝ M^{2/3}, теплопродукция ∝ M^{3/4} — на единицу метаболизма
        // цена обогрева ∝ M^{−1/12} (Scholander et al. 1950). Отсюда правило Бергмана.
        need *= 1f + 1.2f * Math.Max(0f, 18f - temp) / 20f * _mInv12[i];
        // В жаре наоборот: крупным труднее сбрасывать тепло.
        need *= 1f + 1.2f * Math.Max(0f, temp - 22f) / 15f * _m12[i];
        // Хищничество: мелкие и одиночки гибнут чаще.
        float risk = 0.6f * pred * (1 - diet) / _riskMass[i] * (1f - 0.6f * soc);
        // Насыщение: избыток пищи сверх 3 норм не даёт преимущества.
        float balance = Math.Min(food / need, 3f) - 1f;
        return balance - risk + 0.2f * g[Trait.Fecundity];
    }

    /// <summary>Один тик эпохи жизни для всех особей.</summary>
    public void Tick(LifeSystem life, PlanetData planet, float[] tempOffset, Rng rng, float mutationRate)
    {
        int ew = planet.EW, eh = planet.EH;
        int cells = ew * eh;
        Array.Clear(_cellWeightHerb);
        Array.Clear(_cellWeightCarn);
        Array.Clear(_cellCount);

        // Квоты мест по видам ∝ биомассе (не меньше 150).
        float totalB = 0;
        var species = life.Species;
        for (int s = 0; s < species.Count; s++) if (species[s].Alive && !species[s].Sapient) totalB += species[s].TotalBiomass;
        for (int s = 0; s < species.Count; s++)
        {
            var sp = species[s];
            _speciesQuota[s] = sp.Alive && !sp.Sapient && sp.TotalBiomass > 0.05f
                ? Math.Max(150, (int)(Capacity * 0.97f * sp.TotalBiomass / Math.Max(1e-6f, totalB)))
                : 0;
        }

        // Проход 1: клетки, веса потребителей, сортировка подсчётом по клеткам.
        for (int i = 0; i < Capacity; i++)
        {
            if (!Alive[i]) continue;
            int cx = Math.Clamp((int)X[i], 0, ew - 1), cy = Math.Clamp((int)Y[i], 0, eh - 1);
            int c = cy * ew + cx;
            _cellOf[i] = c;
            _cellCount[c]++;
            var g = GenesOf(i);
            UpdateCache(i);
            float w = _w085[i];
            float diet = g[Trait.Diet];
            _cellWeightHerb[c] += w * (1 - diet);
            _cellWeightCarn[c] += w * diet;
        }
        _cellStart[0] = 0;
        for (int c = 0; c < cells; c++) _cellStart[c + 1] = _cellStart[c] + _cellCount[c];
        for (int c = 0; c < cells; c++) _cellCount[c] = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!Alive[i]) continue;
            int c = _cellOf[i];
            _cellItems[_cellStart[c] + _cellCount[c]++] = i;
        }

        // Проход 2: приспособленность (сглаженная), возраст, гибель в воде.
        for (int i = 0; i < Capacity; i++)
        {
            if (!Alive[i]) continue;
            int c = _cellOf[i];
            if (planet.ELand[c] < 0.5f) { Kill(i); continue; }
            Energy[i] = 0.5f * Energy[i] + 0.5f * Fitness(i, c, life, planet, tempOffset);
            Age[i]++;
        }

        // Проход 3: локальные турниры. Победитель замещает проигравшего своим потомком.
        int rounds = (int)(Count * TournamentRate);
        int total = _cellStart[cells];
        if (total > 0)
        {
            for (int r = 0; r < rounds; r++)
            {
                int a = _cellItems[rng.NextInt(total)];
                if (!Alive[a]) continue;
                int c = _cellOf[a];
                int n = _cellCount[c];
                if (n < 2) continue;
                int b = _cellItems[_cellStart[c] + rng.NextInt(n)];
                if (b == a || !Alive[b] || Species[b] != Species[a]) continue;
                int win = Energy[a] >= Energy[b] ? a : b, lose = win == a ? b : a;
                CopyChild(win, lose, rng, mutationRate, eh);
            }
        }

        // Проход 4: подгонка численности к квоте (рождение у лучших с положительным балансом, гибель худших).
        for (int s = 0; s < species.Count; s++)
        {
            int have = _speciesCount[s], want = _speciesQuota[s];
            if (have == 0) continue;
            if (have > want)
            {
                int excess = Math.Min(have - want, have / 5 + 1);
                // Гибнут особи с отрицательным балансом, начиная со случайной позиции.
                int start = rng.NextInt(Capacity);
                for (int k = 0; k < Capacity && excess > 0; k++)
                {
                    int i = (start + k) % Capacity;
                    if (Alive[i] && Species[i] == s && (Energy[i] < 0 || want == 0)) { Kill(i); excess--; }
                }
                if (want == 0) for (int i = 0; i < Capacity; i++) if (Alive[i] && Species[i] == s) Kill(i);
            }
            else if (have < want)
            {
                int births = Math.Min(want - have, have / 4 + 2);
                int start = rng.NextInt(Capacity);
                for (int k = 0; k < Capacity && births > 0; k++)
                {
                    int i = (start + k) % Capacity;
                    if (!Alive[i] || Species[i] != s || Energy[i] <= 0) continue;
                    if (_freeCount == 0) break;
                    int child = Spawn(s, X[i], Y[i], GenesOf(i), Energy[i] * 0.5f);
                    if (child < 0) break;
                    Disperse(child, rng, eh, 1.2f);
                    Genome.Mutate(GenesOf(child), rng, mutationRate);
                    UpdateCache(child);
                    births--;
                }
            }
        }

        // Проход 5: движение к лучшей соседней клетке (пища × пригодность) с шумом.
        for (int i = 0; i < Capacity; i++)
        {
            if (!Alive[i]) continue;
            var g = GenesOf(i);
            float speed = 0.3f + 0.7f * g[Trait.Speed];
            float diet = g[Trait.Diet];
            int c = Math.Clamp((int)Y[i], 0, eh - 1) * ew + Math.Clamp((int)X[i], 0, ew - 1);
            int cx = c % ew, cy = c / ew;
            int bestC = c;
            float m13 = _m13[i];
            float bestV = ((1 - diet) * life.Plant[c] + diet * life.HerbivoreBiomass[c]) *
                          Genome.ThermalSuitability(g, planet.ETemp[c] + tempOffset[c], m13);
            for (int d = 0; d < 4; d++)
            {
                int nx2 = cx + (d == 0 ? 1 : d == 1 ? -1 : 0);
                int ny2 = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (ny2 < 0 || ny2 >= eh) continue;
                nx2 = (nx2 + ew) % ew;
                int nc = ny2 * ew + nx2;
                if (planet.ELand[nc] < 0.5f) continue;
                float v = ((1 - diet) * life.Plant[nc] + diet * life.HerbivoreBiomass[nc]) *
                          Genome.ThermalSuitability(g, planet.ETemp[nc] + tempOffset[nc], m13) * (0.9f + 0.3f * rng.NextFloat());
                if (v > bestV) { bestV = v; bestC = nc; }
            }
            float tx = bestC % ew + 0.5f, ty = bestC / ew + 0.5f;
            float dx = tx - X[i];
            if (dx > ew / 2) dx -= ew; else if (dx < -ew / 2) dx += ew;
            float dy = ty - Y[i];
            MoveTo(i, X[i] + dx * 0.4f * speed + rng.NextGaussian() * 0.3f, Y[i] + dy * 0.4f * speed + rng.NextGaussian() * 0.3f, planet);
        }
    }

    private void CopyChild(int parent, int slot, Rng rng, float mutationRate, int eh)
    {
        GenesOf(parent).CopyTo(GenesOf(slot));
        Genome.Mutate(GenesOf(slot), rng, mutationRate);
        UpdateCache(slot);
        X[slot] = X[parent]; Y[slot] = Y[parent];
        Energy[slot] = Energy[parent] * 0.5f;
        Age[slot] = 0;
        Disperse(slot, rng, eh, 0.6f);
    }

    private void Disperse(int i, Rng rng, int eh, float sigma)
    {
        float nx = X[i] + rng.NextGaussian() * sigma;
        float ny = Math.Clamp(Y[i] + rng.NextGaussian() * sigma, 0, eh - 0.01f);
        X[i] = nx; Y[i] = ny;
        WrapPosition(i);
    }

    private int _ew;

    public void Bind(PlanetData planet) => _ew = planet.EW;

    private void WrapPosition(int i)
    {
        if (_ew <= 0) return;
        X[i] = ((X[i] % _ew) + _ew) % _ew;
    }

    /// <summary>
    /// Дрейф материка (terraforming.md, §4): особи в уехавших эко-клетках сдвигаются вместе с сушей;
    /// ушедшие за полюс гибнут. Возвращает число погибших.
    /// </summary>
    public int Shift(bool[] moved, int dx, int dy, PlanetData planet)
    {
        int ew = planet.EW, eh = planet.EH, lost = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!Alive[i]) continue;
            int c = (int)Y[i] * ew + Math.Min(ew - 1, (int)X[i]);
            if (!moved[c]) continue;
            float ny = Y[i] + dy;
            if (ny < 0 || ny >= eh) { Kill(i); lost++; continue; }
            X[i] = (((X[i] + dx) % ew) + ew) % ew;
            Y[i] = ny;
        }
        return lost;
    }

    private void MoveTo(int i, float nx, float ny, PlanetData planet)
    {
        int ew = planet.EW, eh = planet.EH;
        nx = ((nx % ew) + ew) % ew;
        ny = Math.Clamp(ny, 0, eh - 0.01f);
        int nc = (int)ny * ew + Math.Min(ew - 1, (int)nx);
        if (planet.ELand[nc] >= 0.5f) { X[i] = nx; Y[i] = ny; }
    }
}
