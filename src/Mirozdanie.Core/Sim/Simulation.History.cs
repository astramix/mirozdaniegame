using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Эпоха истории: годовой шаг и демография (docs/design/core-loop.md, awakening-language.md).</summary>
public sealed partial class Simulation
{
    public const int MaxGroups = 400;
    private float[] _initialSpeciesBiomass = Array.Empty<float>();

    /// <summary>Прогоняет эпоху жизни (если ещё не пройдена) и <paramref name="years"/> лет истории.</summary>
    public void Run(int years, Action<long>? progress = null)
    {
        RunLife();
        for (int y = 0; y < years; y++)
        {
            Step();
            progress?.Invoke(World.Year);
        }
    }

    /// <summary>Один год истории. Порядок систем — core-loop.md.</summary>
    public void Step()
    {
        var w = World;
        if (w.Era != Era.History) { RunLife(); return; }
        if (_initialSpeciesBiomass.Length == 0)
        {
            _initialSpeciesBiomass = new float[w.Life.Species.Count];
            for (int s = 0; s < _initialSpeciesBiomass.Length; s++) _initialSpeciesBiomass[s] = w.Life.Species[s].TotalBiomass;
        }
        w.Year++;
        w.Events.CurrentYear = w.Year;

        CreatorStage();          w.Events.Dispatch();
        GeologyStage();          w.Events.Dispatch();
        NaturalStage();          w.Events.Dispatch();
        ClimateStage();
        EconomyStage();          w.Events.Dispatch();
        DemographyStage();       w.Events.Dispatch();
        FaithStage();            w.Events.Dispatch();
        PoliticsStage();         w.Events.Dispatch();
        LanguageStage();         w.Events.Dispatch();
        GodLoopStage();          w.Events.Dispatch();
        if (w.Year % 10 == 0) { UpdateTerritories(); _territorySig = TerritorySignature(); Assimilation(); Settlements(); }
        if (w.Year % 100 == 0) RecordAnnals();
        else TerritoriesIfChanged();
        // Потери года обнуляются в конце года, чтобы вмешательство «между годами» (ApplyNow) их сохранило.
        foreach (var g in w.Groups) g.YearLoss = 0;
    }

    // ---------- Территории ----------

    public void UpdateTerritories()
    {
        var w = World;
        var p = w.Planet;
        Array.Fill(w.CellOwner, -1);
        var best = _territoryDist ??= new float[p.EW * p.EH];
        Array.Fill(best, float.MaxValue);
        foreach (var g in w.Groups) g.Territory.Clear();
        const int R = 3;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            int gx = g.Cell % p.EW, gy = g.Cell / p.EW;
            for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
            {
                int y = gy + dy;
                if (y < 0 || y >= p.EH) continue;
                int c = p.EcoIndex(gx + dx, y);
                if (!p.EIsLand(c)) continue;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > R + 0.5f) continue;
                // Ничьи разрешаются в пользу меньшего id (стабильно).
                if (d < best[c]) { best[c] = d; w.CellOwner[c] = g.Id; }
            }
        }
        for (int c = 0; c < w.CellOwner.Length; c++)
            if (w.CellOwner[c] >= 0) w.Groups[w.CellOwner[c]].Territory.Add(c);
        // Соседи: группы в радиусе 7 (для распространения идей и войн).
        foreach (var g in w.Groups)
        {
            g.Neighbors.Clear();
            if (!g.Alive) continue;
            foreach (var o in w.Groups)
                if (o.Alive && o.Id != g.Id && p.EcoDistance(g.Cell, o.Cell) <= 7f) g.Neighbors.Add(o.Id);
        }
    }

    private float[]? _territoryDist;

    // ---------- Демография ----------

    /// <summary>
    /// Собственный прирост (Malthus 1798) с демографическим переходом (Notestein 1945; Galor 2011):
    /// с энергией на душу > 1,5 кВт рождаемость падает, к 8 кВт — на 85 % (kardashev.md, §6).
    /// </summary>
    public static float GrowthRate(Subsistence s, float watts)
    {
        float r = s == Subsistence.Farming ? 0.02f : 0.014f;
        float t = Math.Clamp((watts - 1500f) / 6500f, 0f, 1f);
        return r * (1f - 0.85f * t * t * (3f - 2f * t));
    }

    private void DemographyStage()
    {
        var w = World;
        var rng = w.Rng[StreamId.Culture];
        int alive = 0;
        foreach (var g in w.Groups) if (g.Alive) alive++;
        int count = w.Groups.Count;
        for (int gi = 0; gi < count; gi++)
        {
            var g = w.Groups[gi];
            if (!g.Alive) continue;
            float keff = Math.Max(10f * Demo.Ref, g.Capacity * g.Yield);
            // Мальтузианская логистика (Malthus 1798): рост до предела урожая.
            float r = GrowthRate(g.Subsistence, g.Watts);
            // Убыль от перенаселения — не больше 10 % в год: логистика иначе «съедает» больше людей, чем есть, при резком падении ёмкости.
            g.Pop += Math.Max(r * g.Pop * (1f - g.Pop / keff), -0.1f * g.Pop);
            if (g.FoodRatio < 0.75f)
            {
                float loss = g.Pop * 0.12f * (0.75f - g.FoodRatio) / 0.75f;
                g.Pop -= loss;
                g.YearLoss += loss / Math.Max(1, g.Pop + loss);
            }
            if (g.Pop < 15f * Demo.Ref)
            {
                KillGroup(g);
                continue;
            }
            // Деление (fission): переполненная группа отправляет часть людей на свободное место.
            if (alive < MaxGroups && g.Pop > 250f * Demo.Ref && g.Pop > 0.8f * g.Capacity && g.FoodRatio > 0.6f)
            {
                int target = FindFissionCell(g);
                if (target >= 0)
                {
                    float share = rng.Range(0.35f, 0.45f);
                    var child = NewGroup(target, g.Pop * share, g.Id, w.Year);
                    g.Pop *= 1 - share;
                    child.PolityId = -1;
                    w.Events.Publish(EventType.GroupFissioned, target, a: child.Id, b: g.Id);
                    alive++;
                    // Новая группа сразу получает территорию вокруг себя (без полного пересчёта).
                    child.Capacity = CellFood(child, target) * 4;
                }
            }
        }
        // Мегафауна и мор.
        if (w.Year % 5 == 0) Hunting();
        Plagues(rng);
    }

    /// <param name="announce">false — гибель уже описана своим событием (утонул).</param>
    public void KillGroup(Group g, bool announce = true)
    {
        var w = World;
        if (!g.Alive) return;
        g.Alive = false;
        g.DiedYear = w.Year;
        g.Pop = 0;
        foreach (var c in g.Territory) if (w.CellOwner[c] == g.Id) w.CellOwner[c] = -1;
        if (g.PolityId >= 0) w.Polities[g.PolityId].Members.Remove(g.Id);
        if (announce) w.Events.Publish(EventType.GroupDestroyed, g.Cell, a: g.Id);
    }

    /// <summary>
    /// Куда может уйти часть народа: свободная суша в радиусе 5, **достижимая** — по клеткам с долей суши ≥ 0,15
    /// (перешейки и цепочки островов проходимы), рыболовы переплывают одну клетку моря.
    /// Поэтому новый перешеек открывает путь, а пролив его закрывает (terraforming.md, §3).
    /// </summary>
    private const int ReachR = 5, ReachS = 2 * ReachR + 1;

    /// <summary>Достижимость клеток окна (2R+1)² вокруг народа — поиском в ширину по проходимым клеткам.</summary>
    private void ReachWindow(Group g, Span<bool> reach)
    {
        var p = World.Planet;
        int gx = g.Cell % p.EW, gy = g.Cell / p.EW;
        const int R = ReachR, S = ReachS;
        Span<int> queue = stackalloc int[S * S];
        bool fisher = g.Subsistence == Subsistence.Fishing;
        reach.Clear();
        int head = 0, tail = 0;
        reach[R * S + R] = true;
        queue[tail++] = R * S + R;
        while (head < tail)
        {
            int k = queue[head++];
            int lx = k % S, ly = k / S;
            bool fromLand = p.ELand[p.EcoIndex(gx + lx - R, gy + ly - R)] >= 0.15f;
            for (int ny = ly - 1; ny <= ly + 1; ny++)
            for (int nx = lx - 1; nx <= lx + 1; nx++)
            {
                if (nx < 0 || nx >= S || ny < 0 || ny >= S) continue;
                int y = gy + ny - R;
                if (y < 0 || y >= p.EH) continue;
                int nk = ny * S + nx;
                if (reach[nk]) continue;
                bool walk = p.ELand[p.EcoIndex(gx + nx - R, y)] >= 0.15f;
                if (!walk && !(fisher && fromLand)) continue;
                reach[nk] = true;
                queue[tail++] = nk;
            }
        }
    }

    /// <summary>Может ли народ дойти до клетки, чтобы поселиться там (в пределах радиуса деления, terraforming.md, §3).</summary>
    public bool IsReachable(Group g, int cell)
    {
        var p = World.Planet;
        int dx = cell % p.EW - g.Cell % p.EW, dy = cell / p.EW - g.Cell / p.EW;
        if (dx > p.EW / 2) dx -= p.EW; else if (dx < -p.EW / 2) dx += p.EW;
        if (Math.Abs(dx) > ReachR || Math.Abs(dy) > ReachR) return false;
        Span<bool> reach = stackalloc bool[ReachS * ReachS];
        ReachWindow(g, reach);
        return reach[(dy + ReachR) * ReachS + dx + ReachR];
    }

    /// <summary>
    /// Куда может уйти часть народа: свободная суша в радиусе 5, **достижимая** — по клеткам с долей суши ≥ 0,15
    /// (перешейки и цепочки островов проходимы), рыболовы переплывают одну клетку моря.
    /// Поэтому новый перешеек открывает путь, а пролив его закрывает (terraforming.md, §3).
    /// </summary>
    private int FindFissionCell(Group g)
    {
        var w = World;
        var p = w.Planet;
        int gx = g.Cell % p.EW, gy = g.Cell / p.EW;
        const int R = ReachR, S = ReachS;
        Span<bool> reach = stackalloc bool[S * S];
        ReachWindow(g, reach);
        int best = -1;
        float bestV = 0;
        for (int dy = -R; dy <= R; dy++)
        for (int dx = -R; dx <= R; dx++)
        {
            int y = gy + dy;
            if (y < 0 || y >= p.EH) continue;
            if (!reach[(dy + R) * S + dx + R]) continue;
            int c = p.EcoIndex(gx + dx, y);
            if (!p.EIsLand(c)) continue;
            int owner = w.CellOwner[c];
            if (owner >= 0 && owner != g.Id) continue;
            bool free = true;
            foreach (var nId in g.Neighbors)
            {
                var o = w.Groups[nId];
                if (o.Alive && p.EcoDistance(o.Cell, c) < 3f) { free = false; break; }
            }
            if (!free || p.EcoDistance(g.Cell, c) < 3f) continue;
            float v = CellFood(g, c) - 0.05f * MathF.Sqrt(dx * dx + dy * dy);
            if (v > bestV) { bestV = v; best = c; }
        }
        return best;
    }

    /// <summary>Переохота мегафауны: крупные виды исчезают там, где охотники (идея 31).</summary>
    private void Hunting()
    {
        var w = World;
        var species = w.Life.Species;
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.Subsistence == Subsistence.Farming) continue;
            float pressure = Math.Min(0.05f, 0.012f * g.Pop / (1000f * Demo.Ref));
            foreach (var c in g.Territory)
                foreach (var s in species)
                {
                    if (!s.Alive || s.Sapient || s.LogMass < 1.3f) continue;
                    s.Biomass[c] *= 1f - pressure * (0.5f + 0.5f * s.LogMass / 3.5f);
                }
        }
        for (int si = 0; si < species.Count && si < _initialSpeciesBiomass.Length; si++)
        {
            var s = species[si];
            if (!s.Alive || s.Sapient || s.LogMass < 1.3f || w.MegafaunaLost.Contains(si)) continue;
            float tot = 0;
            foreach (var c in w.Life.LandCells) tot += s.Biomass[c];
            s.TotalBiomass = tot;
            if (_initialSpeciesBiomass[si] > 0.5f && tot < 0.15f * _initialSpeciesBiomass[si])
            {
                w.MegafaunaLost.Add(si);
                // Виновник — группа охотников с наибольшим населением рядом с ареалом.
                int culprit = -1; float bp = 0;
                foreach (var g in w.Groups)
                    if (g.Alive && g.Subsistence != Subsistence.Farming && g.Pop > bp && s.Biomass[g.Cell] >= 0) { bp = g.Pop; culprit = g.Id; }
                s.ExtinctTick = w.LifeTick;
                w.Events.Publish(EventType.MegafaunaGone, s.OriginCell, a: si, b: culprit, magnitude: 0.6f);
                w.Events.Publish(EventType.SpeciesExtinct, s.OriginCell, a: si);
            }
        }
    }

    private void Plagues(Rng rng)
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.Pop < 2500f * Demo.Ref || g.Subsistence != Subsistence.Farming) continue;
            // Переболевшие поколения несут иммунитет ~60 лет (McNeill 1976, «Plagues and Peoples»).
            if (w.Year - g.LastPlague < 60) continue;
            // Поветрия любят большие и связные общества; наука и химия (гигиена, вакцины) их укрощают.
            float p = 0.0012f * Math.Min(4f, g.Pop / (3000f * Demo.Ref)) * (1 + g.TradePartners.Count * 0.5f) * w.Params.CatastropheRate
                      * (g.Has(Tech.ScientificMethod) ? 0.6f : 1f) * (g.Has(Tech.Chemistry) ? 0.4f : 1f);
            if (!rng.Chance(p)) continue;
            float frac = rng.Range(0.1f, 0.25f);
            g.Pop *= 1 - frac;
            g.YearLoss += frac;
            g.LastPlague = w.Year;
            int ev = w.Events.Publish(EventType.Plague, g.Cell, a: g.Id, magnitude: frac);
            foreach (var t in g.TradePartners)
            {
                var o = w.Groups[t];
                if (!o.Alive || w.Year - o.LastPlague < 60 || !rng.Chance(0.4)) continue;
                float f2 = frac * rng.Range(0.4f, 0.9f);
                o.Pop *= 1 - f2;
                o.YearLoss += f2;
                o.LastPlague = w.Year;
                w.Events.Publish(EventType.Plague, o.Cell, a: o.Id, magnitude: f2, cause: ev);
            }
        }
    }
}

