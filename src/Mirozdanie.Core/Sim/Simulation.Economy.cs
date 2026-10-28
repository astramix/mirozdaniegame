using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

public enum ScriptKind : byte { Logographic, Syllabic, Alphabetic }

/// <summary>Планетарные проекты (kardashev.md, §5).</summary>
public enum MegaprojectKind : byte { GlobalGrid, OrbitalCollectors, ClimateControl }

public sealed class Megaproject
{
    public MegaprojectKind Kind;
    public long Started;
    public float Progress;   // 0…1 текущего яруса
    public int Tiers;        // построено ярусов (для коллекторов)
    public bool Done;
}

/// <summary>
/// Демография в реальном масштабе (kardashev.md, §2): площадь клеток, плотности способов хозяйства.
/// </summary>
public static class Demo
{
    /// <summary>
    /// Реальных людей на «единицу» прежней модели: пороги, подобранные ночью 1 в единицах, переводятся умножением.
    /// Первые общины (60–150 единиц) — 6–15 тыс. человек, как у охотников-собирателей (Binford 2001).
    /// </summary>
    public const float Ref = 100f;
    private const float CircumferenceKm = 40075f;

    /// <summary>Площадь эко-клетки, км²: (длина экватора / EW)² · cos φ.</summary>
    public static float CellArea(PlanetData p, int c)
    {
        float side = CircumferenceKm / p.EW;
        float lat = p.EcoLatitude(c / p.EW) * MathF.PI / 180f;
        return side * side * MathF.Max(0.05f, MathF.Cos(lat));
    }

    /// <summary>Вес народа в событиях 0…1 по порядку величины: 1 тыс. → 0, 100 тыс. → 0,5, 10 млн → 1.</summary>
    public static float Weight(float pop) => Math.Clamp((MathF.Log10(Math.Max(1f, pop)) - 3f) / 4f, 0f, 1f);
}

/// <summary>
/// Хозяйство и лестница Кардашёва (docs/design/kardashev.md): ёмкость в реальных людях, открытия и их распространение,
/// энергия по источникам с исчерпанием ископаемых, выбросы и потепление, эпохи, планетарные проекты.
/// </summary>
public sealed partial class Simulation
{
    private readonly HashSet<long> _knownRoutes = new(); // только проверка «уже был такой путь»
    public static bool DebugEnergy = Environment.GetEnvironmentVariable("MIRO_ENERGY") is "1" or "10" or "20" or "y";
    private static readonly int DebugEvery = Environment.GetEnvironmentVariable("MIRO_ENERGY") switch { "10" => 10, "20" => 20, "1" => 100, _ => 1 };
    private float _lastKardashevMilestone;

    /// <summary>
    /// Плотность способа хозяйства, чел/км² суши (kardashev.md, §2): собиратели 0,01–0,5 (Binford 2001),
    /// скотоводы 1–3 (Smil 2017), мотыжное земледелие 5–20 (Boserup 1965), плужное с орошением 30–150, промышленное ×2,5 (Smil 2001).
    /// </summary>
    public float Density(Group g, int c)
    {
        var w = World;
        var p = w.Planet;
        float npp = p.ENpp[c] / 1000f;
        float fauna = Math.Clamp(w.Life.FaunaAt(c) * 1.5f, 0f, 1.5f);
        float forage = 0.05f * npp * (0.4f + fauna);
        float v;
        switch (g.Subsistence)
        {
            case Subsistence.Fishing:
                v = forage + (p.ECoast[c] ? 0.6f * p.EFish[c] : 0f) + 0.8f * p.ERiver[c];
                if (g.Has(Tech.Boats)) v *= 1.3f;
                break;
            case Subsistence.Herding:
                v = 0.8f * npp * (0.4f + fauna) * (g.Has(Tech.DraftAnimals) ? 1.1f : 1f);
                break;
            case Subsistence.Farming:
                v = 0.4f * forage + 18f * p.EFertility[c] * (1f + p.ERiver[c]) * FarmMultiplier(g, c);
                break;
            default:
                v = forage;
                break;
        }
        // Похолодание (метеоритная или ядерная зима) снижает урожай.
        return v * Math.Clamp(1f + w.TempOffset[c] * 0.08f, 0.2f, 1f);
    }

    /// <summary>Множитель земледелия от открытий (kardashev.md, §3, ветвь «Пища»).</summary>
    private float FarmMultiplier(Group g, int c)
    {
        var p = World.Planet;
        float m = 1f;
        if (g.Has(Tech.DraftAnimals)) m *= 1.15f;
        if (g.Has(Tech.Irrigation)) m *= p.ERiver[c] > 0.05f || p.EPrecip[c] < 1000f ? 1.5f : 1.15f;
        if (g.Has(Tech.Plow)) m *= 1.6f;
        if (g.Has(Tech.CropRotation)) m *= 1.3f;
        if (g.Has(Tech.Selection)) m *= 1.3f;
        // Удобрения и новые сорта держатся на добытой энергии (Smil 2001): без неё прибавка тает.
        if (g.Has(Tech.Fertilizer)) m *= 1f + 1.0f * g.EnergyOk;
        if (g.Has(Tech.GreenRevolution)) m *= 1f + 0.5f * g.EnergyOk;
        return m;
    }

    /// <summary>Пища (ёмкость, человек) клетки для народа: плотность × площадь суши; своя клетка — ×1,5.</summary>
    public float CellFood(Group g, int c)
    {
        var p = World.Planet;
        float v = Density(g, c) * Demo.CellArea(p, c) * p.ELand[c];
        return c == g.Cell ? v * 1.5f : v;
    }

    private void EconomyStage()
    {
        var w = World;
        var rng = w.Rng[StreamId.Economy];
        var p = w.Planet;
        EnsureResources();
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            float cap = 0, yield = 0;
            int n = 0;
            if (g.Territory.Count == 0) { cap = CellFood(g, g.Cell); yield = w.NppFactor[g.Cell]; n = 1; }
            foreach (var c in g.Territory) { cap += CellFood(g, c); yield += w.NppFactor[c]; n++; }
            // Дальняя торговля кормит города сверх местной ёмкости (железные дороги, моторы, kardashev.md, §2).
            float imports = 1f + g.TradeBonus;
            if (g.Has(Tech.Railroad)) imports += 0.2f * Math.Min(4, g.TradePartners.Count) * g.EnergyOk;
            if (g.Has(Tech.Combustion)) imports += 0.2f * Math.Min(4, g.TradePartners.Count) * g.EnergyOk;
            if (g.Has(Tech.Networks)) imports += 0.3f * g.EnergyOk; // мировой рынок
            g.Capacity = Math.Max(20f * Demo.Ref, cap * imports);
            g.Yield = n > 0 && g.Territory.Count > 0 ? yield / n : yield;
            g.FoodRatio = Math.Min(3f, g.Capacity * g.Yield / Math.Max(1f, g.Pop));

            // Голод: событие в первый год и каждые 5 лет затяжного голода.
            if (g.FoodRatio < 0.7f)
            {
                g.FamineYears++;
                g.LastCrisis = w.Year;
                if (g.FamineYears == 1 || g.FamineYears % 5 == 0)
                    w.Events.Publish(EventType.Famine, g.Cell, a: g.Id, b: g.FamineYears, magnitude: Math.Min(1f, (0.7f - g.FoodRatio) * 2f + 0.2f), cause: FamineCause(g));
            }
            else
            {
                g.FamineYears = 0;
                g.PrayedThisFamine = false;
            }

            // Образованность растёт с числом умов (Kremer 1993), любознательностью, торговлей и учреждениями знания.
            float k = 0.00022f * (1f - 0.8f * g.Knowledge) * (0.4f + g.Culture[CultureTrait.KnowledgeLove]) * (0.5f + MathF.Log10(Math.Max(10f, g.Pop / Demo.Ref)) / 4f)
                      * (1f + 0.3f * g.TradePartners.Count) * (0.75f + 0.5f * g.Belief[Doctrine.Knowledge])
                      * (g.Subsistence == Subsistence.Farming ? 1.5f : 1f) * KnowledgeMultiplier(g) * w.Params.TechPace
                      // Зависимость от чуда: кому всё дают, тот меньше ищет сам (god-loop.md §1).
                      * (1f - 0.5f * g.Dependence);
            g.Knowledge = Math.Min(1f, g.Knowledge + k);
            foreach (var nId in g.Neighbors)
            {
                var o = w.Groups[nId];
                if (o.Knowledge > g.Knowledge + 0.05f)
                    g.Knowledge += 0.004f * (o.Knowledge - g.Knowledge) * w.Intelligibility(o.LanguageId, g.LanguageId);
            }

            // Земледелие и рост общин ведут к иерархии (Flannery & Marcus 2012, «The Creation of Inequality»).
            if (g.Subsistence == Subsistence.Farming && g.Pop > 800f * Demo.Ref)
                g.Culture[CultureTrait.Hierarchy] = Math.Min(0.9f, g.Culture[CultureTrait.Hierarchy] + 0.0012f);
            Discoveries(g, rng);
        }
        if (w.Year % 10 == 5) Trade();
        Energy();
        CarbonAndClimate();
        Eras();
        Megaprojects();
    }

    /// <summary>Учреждения знания ускоряют образованность (kardashev.md, §3, ветвь «Знание»).</summary>
    private static float KnowledgeMultiplier(Group g)
    {
        float m = 1f;
        if (g.Writing) m *= 1.4f;
        if (g.Has(Tech.Printing)) m *= 1.5f;
        if (g.Has(Tech.Universities)) m *= 1.4f;
        if (g.Has(Tech.ScientificMethod)) m *= 1.6f;
        if (g.Has(Tech.Computing)) m *= 1.3f;
        if (g.Has(Tech.Networks)) m *= 1.4f;
        if (g.Has(Tech.AI)) m *= 1.5f;
        return m;
    }

    private int FamineCause(Group g)
    {
        var w = World;
        foreach (var e in w.Effects)
            if (e.Type is Creator.InterventionType.Drought or Creator.InterventionType.Meteor && w.Planet.EcoDistance(e.Cell, g.Cell) <= e.Radius) return e.EventId;
        int r = w.RegionOf(g.Cell);
        return w.RegionDrought[r] ? w.RegionDroughtEvent[r] : -1;
    }

    // ---------- Открытия (kardashev.md, §3) ----------

    private bool PrereqsMet(Group g, TechInfo t)
    {
        foreach (var r in t.Requires) if (!g.Has(r)) return false;
        return true;
    }

    /// <summary>Есть ли нужный открытию ресурс в своих землях (уголь, нефть, уран — и у торговых партнёров при дальней торговле).</summary>
    private bool ResourceOk(Group g, TechInfo t)
    {
        var w = World;
        var p = w.Planet;
        switch (t.Resource)
        {
            case TechResource.None:
                if (t.Id == Tech.Agriculture)
                {
                    foreach (var c in g.Territory) if (p.EDomesticable[c]) return true;
                    return p.EDomesticable[g.Cell];
                }
                return true;
            case TechResource.Ore:
            case TechResource.RichOre:
            {
                float best = p.EOre[g.Cell];
                foreach (var c in g.Territory) best = Math.Max(best, p.EOre[c]);
                return best > (t.Resource == TechResource.Ore ? 0.4f : 0.62f);
            }
            case TechResource.Coal: return FossilAvailable(g, 0) > 5f;
            case TechResource.Oil: return FossilAvailable(g, 1) > 5f;
            case TechResource.Uranium: return FossilAvailable(g, 2) > 1f;
            case TechResource.Coast:
            {
                if (p.ECoast[g.Cell]) return true;
                foreach (var c in g.Territory) if (p.ECoast[c]) return true;
                return false;
            }
            case TechResource.River:
            {
                float best = p.ERiver[g.Cell];
                foreach (var c in g.Territory) best = Math.Max(best, p.ERiver[c]);
                return best > 0.08f;
            }
        }
        return true;
    }

    /// <summary>Нужда ускоряет открытие (Boserup 1965): голод — к агротехнике, войны — к оружию, торговля — к дорогам и парусу.</summary>
    private float Need(Group g, Tech t)
    {
        var w = World;
        bool hungry = g.FoodRatio < 1.05f || g.FamineYears > 0;
        bool war = w.Year - g.LastDefeat < 50 || g.PolityId >= 0 && w.Year - w.Polities[g.PolityId].LastWar < 30;
        bool trade = g.TradePartners.Count >= 2;
        switch (t)
        {
            case Tech.Irrigation:
            {
                float prec = 0; int n = 0;
                foreach (var c in g.Territory) { prec += w.Planet.EPrecip[c]; n++; }
                return n > 0 && prec / n < 900f ? 2f : 0.6f;
            }
            case Tech.Plow: case Tech.CropRotation: case Tech.Selection: case Tech.Fertilizer: case Tech.GreenRevolution:
                // Голод толкает к агротехнике; чудеса урожая — гасят нужду (god-loop.md §1).
                return (hungry ? 2f : 1f) * (1f - 0.8f * g.Dependence);
            case Tech.Bronze: case Tech.Iron: case Tech.Steel: case Tech.Gunpowder: case Tech.MassArmies: case Tech.NuclearWeapons:
                return war ? 2f : 0.8f;
            case Tech.Money: case Tech.Roads: case Tech.Sail: case Tech.Navigation: case Tech.Railroad:
                return trade ? 1.8f : 0.8f;
            case Tech.Ideologies:
                return g.PolityId >= 0 && w.Polities[g.PolityId].Psi > 1f ? 1.6f : 1f;
            case Tech.Electricity:
                // Генератору нужен привод: с паровой машиной — быстро, на одной воде — в пять раз дольше (kardashev.md, §3).
                return g.Has(Tech.SteamEngine) ? 1f : 0.2f;
            case Tech.Solar: case Tech.Fusion:
                return g.FossilShare > 0.4f && FossilAvailable(g, 1) < 50f ? 2f : 1f;
        }
        return 1f;
    }

    private void Discoveries(Group g, Rng rng)
    {
        var w = World;
        float popEff = g.Pop;
        foreach (var t in g.TradePartners) popEff += 0.3f * w.Groups[t].Pop;
        // Убывающая отдача от числа умов: степень 0,3 (Kremer 1993 с поправкой Jones 1995).
        float size = Math.Clamp(MathF.Pow(popEff / 1e6f, 0.3f), 0.3f, 4f);
        float culture = (0.5f + g.Culture[CultureTrait.KnowledgeLove]) * (0.75f + 0.5f * g.Belief[Doctrine.Knowledge]);
        // Своё изобретение.
        for (int i = 0; i < (int)Tech.Count; i++)
        {
            var info = Technology.All[i];
            if (g.Has(info.Id) || g.Knowledge < info.Knowledge || g.Pop < info.MinPop) continue;
            if (!PrereqsMet(g, info) || !ResourceOk(g, info)) continue;
            // Поздние открытия требуют огромных вложений: эпохи 6–8 идут медленнее (иначе от пара до атома — полтора века).
            float late = info.Era >= 6 ? 0.35f : info.Era == 5 ? 0.6f : 1f;
            float pr = info.Rate * late * Need(g, info.Id) * size * culture * w.Params.TechPace;
            if (rng.Chance(pr)) Learn(g, info.Id, -1);
        }
        // Перенятое у соседей и торговых партнёров (Comin & Hobijn 2010: задержка тем меньше, чем теснее связь).
        ulong around = 0;
        foreach (var nId in g.Neighbors) around |= w.Groups[nId].Techs;
        foreach (var t in g.TradePartners) around |= w.Groups[t].Techs;
        ulong cand = around & ~g.Techs;
        while (cand != 0)
        {
            int bit = System.Numerics.BitOperations.TrailingZeroCount(cand);
            cand &= cand - 1;
            var info = Technology.All[bit];
            // Знание переходит и без своего ресурса (знать о паровой машине можно и без угля); энергию даёт только добыча.
            if (g.Knowledge < info.Knowledge * 0.8f || !PrereqsMet(g, info)) continue;
            if (info.Id == Tech.Agriculture && !ResourceOk(g, info)) continue;
            // Источник — самый понятный сосед, который знает.
            int src = -1; float best = 0; bool partner = false;
            foreach (var nId in g.Neighbors)
            {
                var o = w.Groups[nId];
                if (!o.Has(info.Id)) continue;
                float intel = w.Intelligibility(o.LanguageId, g.LanguageId);
                if (intel > best || src < 0) { best = intel; src = o.Id; }
            }
            foreach (var t in g.TradePartners)
                if (w.Groups[t].Has(info.Id)) { partner = true; if (src < 0) src = t; }
            float pr = (info.Era >= 6 ? 0.008f : 0.02f) * (0.3f + 0.7f * best) * (partner ? 2f : 1f) * (0.5f + g.Culture[CultureTrait.Openness]);
            if (rng.Chance(pr)) Learn(g, info.Id, src);
        }
    }

    /// <summary>Народ узнаёт открытие: свои изобретения и перенятые (source ≥ 0) — разные строки Хроники.</summary>
    private void Learn(Group g, Tech t, int source)
    {
        var w = World;
        g.Add(t);
        switch (t)
        {
            case Tech.Agriculture:
                g.Agriculture = true;
                g.Subsistence = Subsistence.Farming;
                w.Events.Publish(EventType.AgricultureAdopted, g.Cell, a: g.Id, b: source >= 0 ? 1 : 0);
                return;
            case Tech.Writing:
                g.Writing = true;
                break;
            case Tech.Copper:
                g.Metallurgy = true;
                break;
            case Tech.Astronomy:
                g.Astronomy = true;
                // Знание периодичности комет — натурализм (идея 32).
                g.N = Math.Min(1f, g.N + 0.15f);
                break;
            case Tech.ScientificMethod:
                g.N = Math.Min(1f, g.N + 0.1f);
                break;
        }
        float mag = t == Tech.Writing ? (float)ScriptFor(g) : Technology.Get(t).Era / 9f;
        w.Events.Publish(EventType.Discovery, g.Cell, a: g.Id, b: (int)t, c: source, magnitude: mag);
    }

    /// <summary>Тип письма из слоговой структуры языка: простые слоги — слоговое, сложные — алфавит, ранние — логограммы.</summary>
    public ScriptKind ScriptFor(Group g)
    {
        var L = World.LangOf(g);
        if (L.ClusterRate < 0.12f && L.CodaRate < 0.35f) return ScriptKind.Syllabic;
        if (World.Year < 300) return ScriptKind.Logographic;
        return ScriptKind.Alphabetic;
    }

    // ---------- Торговля ----------

    /// <summary>Гравитационная модель торговли (Tinbergen 1962): T = G·M_i·M_j / d².</summary>
    private void Trade()
    {
        var w = World;
        w.TradeRoutes.Clear();
        foreach (var g in w.Groups) { g.TradePartners.Clear(); g.TradeBonus = 0; }
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            float mi = Mass(g);
            foreach (var nId in g.Neighbors)
            {
                if (nId <= g.Id) continue;
                var o = w.Groups[nId];
                if (!o.Alive) continue;
                float d = Math.Max(1f, w.Planet.EcoDistance(g.Cell, o.Cell));
                // Дальняя торговля (дороги, парус, железные дороги) «укорачивает» расстояние.
                float reach = 1f + (g.Has(Tech.Roads) || o.Has(Tech.Roads) ? 0.4f : 0f) + (g.Has(Tech.Railroad) || o.Has(Tech.Railroad) ? 0.8f : 0f);
                float t = mi * Mass(o) / (d * d) * reach * reach * (0.4f + 0.6f * w.Intelligibility(g.LanguageId, o.LanguageId));
                if (t < 0.12f) continue;
                w.TradeRoutes.Add((g.Id, o.Id, t));
                g.TradePartners.Add(o.Id);
                o.TradePartners.Add(g.Id);
                long key = (long)g.Id * 100_000 + o.Id;
                if (_knownRoutes.Add(key) && t > 0.5f) w.Events.Publish(EventType.TradeRoute, g.Cell, a: g.Id, b: o.Id, magnitude: Math.Min(1f, t / 4f));
            }
        }
        // Выгода торговли — прибавка к ёмкости партнёров (до +30 %).
        foreach (var (a, b, t) in w.TradeRoutes)
        {
            w.Groups[a].TradeBonus = Math.Min(0.3f, w.Groups[a].TradeBonus + 0.04f * Math.Min(t, 3f));
            w.Groups[b].TradeBonus = Math.Min(0.3f, w.Groups[b].TradeBonus + 0.04f * Math.Min(t, 3f));
        }
    }

    /// <summary>«Экономическая масса» народа: излишек плюс население (в единицах прежней модели, логарифм для крупных держав).</summary>
    private static float Mass(Group g)
    {
        float units = g.Pop / Demo.Ref;
        float surplus = Math.Max(0f, g.Capacity * g.Yield - g.Pop) / Demo.Ref;
        return Math.Min(surplus / 1000f + units / 3000f, 3f + MathF.Log10(1f + units / 3000f));
    }

    // ---------- Энергия и ресурсы (kardashev.md, §4) ----------

    /// <summary>Остатки ископаемых по эко-клеткам (ЭДж) — исчерпываются добычей; заводятся при Пробуждении.</summary>
    private void EnsureResources()
    {
        var w = World;
        if (w.CoalLeft.Length == w.Planet.EW * w.Planet.EH) return;
        var p = w.Planet;
        int en = p.EW * p.EH;
        float k = w.Params.ResourceRichness;
        w.CoalLeft = new float[en]; w.OilLeft = new float[en]; w.UraniumLeft = new float[en];
        for (int c = 0; c < en; c++) { w.CoalLeft[c] = p.ECoal[c] * k; w.OilLeft[c] = p.EOil[c] * k; w.UraniumLeft[c] = p.EUranium[c] * k; }
    }

    private float[] FossilArray(int kind) => kind == 0 ? World.CoalLeft : kind == 1 ? World.OilLeft : World.UraniumLeft;

    /// <summary>Доступный запас ископаемого (ЭДж): свои земли, с дальней торговлей — и земли партнёров.</summary>
    private float FossilAvailable(Group g, int kind)
    {
        var w = World;
        var arr = FossilArray(kind);
        if (arr.Length == 0) return 0;
        float s = arr[g.Cell];
        foreach (var c in g.Territory) if (c != g.Cell) s += arr[c];
        if (g.Has(Tech.Railroad) || g.Has(Tech.Navigation))
            foreach (var t in g.TradePartners)
            {
                var o = w.Groups[t];
                foreach (var c in o.Territory) s += 0.5f * arr[c];
            }
        if (g.Has(Tech.Combustion))
            foreach (var c in MarketCells(kind)) s += 0.3f * arr[c];
        return s;
    }

    /// <summary>Списать добычу (ЭДж) с клеток: сначала свои, потом партнёров. Возвращает, сколько удалось добыть.</summary>
    private float Extract(Group g, int kind, float ej)
    {
        var w = World;
        var arr = FossilArray(kind);
        // Добыча не больше 5 % запаса клетки в год — так кривая добычи сама проходит пик (Hubbert 1956).
        float need = ExtractFrom(arr, g.Cell, 0.05f, ej);
        foreach (var c in g.Territory) if (c != g.Cell) need = ExtractFrom(arr, c, 0.05f, need);
        if (need > 0 && (g.Has(Tech.Railroad) || g.Has(Tech.Navigation)))
            foreach (var t in g.TradePartners)
                foreach (var c in w.Groups[t].Territory) need = ExtractFrom(arr, c, 0.03f, need);
        // Мировой рынок топлива (kardashev.md §4): с двигателем внутреннего сгорания и танкерами
        // индустриальный народ покупает уголь и нефть с богатейших месторождений планеты.
        if (need > 0 && g.Has(Tech.Combustion))
            foreach (var c in MarketCells(kind)) need = ExtractFrom(arr, c, 0.02f, need);
        return ej - need;
    }

    private readonly int[][] _market = { Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>() };
    private readonly long[] _marketYear = { -1000, -1000, -1000 };

    /// <summary>64 богатейших клетки по виду топлива; пересчёт раз в 50 лет (стабильный порядок: запас, затем индекс).</summary>
    private int[] MarketCells(int kind)
    {
        var w = World;
        if (w.Year - _marketYear[kind] < 50 && _market[kind].Length > 0) return _market[kind];
        _marketYear[kind] = w.Year;
        var arr = FossilArray(kind);
        const int N = 64;
        var top = _market[kind].Length == N ? _market[kind] : new int[N];
        int count = 0;
        for (int c = 0; c < arr.Length; c++)
        {
            if (arr[c] <= 0.5f) continue;
            if (count < N) { top[count++] = c; Sift(top, count - 1, arr); continue; }
            if (arr[c] <= arr[top[N - 1]]) continue;
            top[N - 1] = c; Sift(top, N - 1, arr);
        }
        if (count < N) { var t = new int[count]; Array.Copy(top, t, count); top = t; }
        _market[kind] = top;
        return top;
    }

    // Вставка по убыванию запаса; при равенстве меньший индекс раньше.
    private static void Sift(int[] top, int i, float[] arr)
    {
        while (i > 0 && (arr[top[i]] > arr[top[i - 1]] || (arr[top[i]] == arr[top[i - 1]] && top[i] < top[i - 1])))
        {
            (top[i], top[i - 1]) = (top[i - 1], top[i]);
            i--;
        }
    }

    private static float ExtractFrom(float[] arr, int c, float share, float need)
    {
        if (need <= 0) return 0;
        float take = Math.Min(need, arr[c] * share);
        arr[c] -= take;
        return need - take;
    }

    private const float SecondsPerYear = 3.156e7f;

    /// <summary>
    /// Мощность на душу по источникам (Cook 1971; Smil 2017): пища и огонь, тягловый скот, вода и ветер, уголь, нефть, атом, солнце, синтез.
    /// Ископаемые добываются из запасов; когда запасов мало — мощность падает (энергетический кризис).
    /// </summary>
    private void Energy()
    {
        var w = World;
        var p = w.Planet;
        double watts = 0, fossilWatts = 0, people = 0;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            // Пища и огонь (Cook 1971): собиратели 240 Вт, раннее земледелие 580, развитое 1260.
            float bio = g.Subsistence switch
            {
                Subsistence.Farming => g.Has(Tech.Plow) && g.Has(Tech.CropRotation) ? 1260f : 580f,
                Subsistence.Herding => 350f,
                Subsistence.Fishing => 260f,
                _ => 240f,
            };
            if (g.Has(Tech.DraftAnimals)) bio += 120f;
            float hydro = 0, wind = 0, solar = 0;
            int n = 0;
            foreach (var c in g.Territory) { hydro += p.EHydro[c]; wind += p.EWind[c]; solar += p.ESolar[c]; n++; }
            if (n > 0) { hydro /= n; wind /= n; solar /= n; }
            float mech = (g.Has(Tech.WaterWheel) ? 250f * hydro : 0f) + (g.Has(Tech.Windmill) ? 120f * wind : 0f);
            // Ископаемые: желаемая мощность на душу, затем — сколько удалось добыть.
            float coalW = g.Has(Tech.SteamEngine) ? 1800f + (g.Has(Tech.Railroad) ? 600f : 0f) + (g.Has(Tech.Steel) ? 400f : 0f) : g.Has(Tech.CoalMining) ? 150f : 0f;
            float oilW = g.Has(Tech.Combustion) ? 2500f + (g.Has(Tech.Aviation) ? 800f : 0f) : g.Has(Tech.Oil) ? 400f : 0f;
            float atomW = g.Has(Tech.Fission) ? 2200f : 0f;
            float coalGot = Supply(g, 0, coalW), oilGot = Supply(g, 1, oilW), atomGot = Supply(g, 2, atomW);
            float clean = (g.Has(Tech.Solar) ? 3000f * solar : 0f) + (g.Has(Tech.Fusion) ? 6000f : 0f) + mech;
            // Достаточность энергии: чистые источники заменяют ископаемые, если их хватает.
            float want = coalW + oilW + atomW;
            g.EnergyOk = want <= 0 ? 1f : Math.Clamp((coalGot + oilGot + atomGot + clean - mech) / want, 0f, 1f);
            float commercial = coalGot + oilGot + atomGot + clean;
            if (g.Has(Tech.Electricity)) commercial *= 1.25f;
            if (g.Has(Tech.Networks) && w.GlobalGrid) commercial *= 1.3f;
            if (g.Has(Tech.AI)) commercial += 800f;
            g.Watts = bio + commercial;
            g.FossilShare = g.Watts > 0 ? (coalGot + oilGot) / g.Watts : 0;
            watts += g.Pop * (double)g.Watts;
            fossilWatts += g.Pop * (double)(coalGot + oilGot);
            people += g.Pop;
        }
        // Коллекторы нужно обслуживать: без космической цивилизации ярусы падают (2 % в год).
        if (w.OrbitalWatts > 0)
        {
            double space = 0;
            foreach (var g in w.Groups) if (g.Alive && g.Has(Tech.Spaceflight)) space += g.Pop;
            if (space < 1e8) w.OrbitalWatts *= 0.98;
        }
        watts += w.OrbitalWatts;
        if (DebugEnergy && w.Year % DebugEvery == 0)
        {
            double coal = 0, oil = 0, food = 0, eok = 0, farm = 0; int ng = 0;
            foreach (var g in w.Groups) if (g.Alive) { food += g.FoodRatio; eok += g.EnergyOk; ng++; if (g.Subsistence == Subsistence.Farming) farm += g.Pop; }
            Console.WriteLine($"   народов {ng}, пища {food / Math.Max(1, ng):0.00}, энергия {eok / Math.Max(1, ng):0.00}, земледельцев {farm / Math.Max(1, people):P0}");
            foreach (var x in w.CoalLeft) coal += x;
            foreach (var x in w.OilLeft) oil += x;
            Console.WriteLine($"[энергия] год {w.Year}: люди {people / 1e6:0.0} млн, мощность {watts:0.0e0} Вт, ископаемые {fossilWatts:0.0e0} Вт, " +
                              $"CO₂ {w.Co2:0} ppm, +{w.Warming:0.00} °C, эпоха {w.WorldEra}, уголь {coal:0} ЭДж, нефть {oil:0} ЭДж");
        }
        w.PowerWatts = watts;
        w.FossilWatts = fossilWatts;
        w.WorldPopulation = people;
        w.Kardashev = Kardashev(watts);
        float[] milestones = { 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };
        foreach (var m in milestones)
        {
            if (w.Kardashev >= m && _lastKardashevMilestone < m)
            {
                _lastKardashevMilestone = m;
                w.Events.Publish(EventType.KardashevMilestone, -1, magnitude: m);
            }
        }
    }

    /// <summary>Добыть ископаемое для мощности wantW на душу; при нехватке — пропорционально меньше и событие «кризис».</summary>
    private float Supply(Group g, int kind, float wantW)
    {
        if (wantW <= 0) return 0;
        // Атомная энергия: 1 ЭДж урана ≈ много лет работы; масштаб запаса уже в «эквиваленте тепла».
        float ej = g.Pop * wantW * SecondsPerYear / 1e18f;
        float got = Extract(g, kind, ej);
        float frac = ej > 0 ? got / ej : 1f;
        // Кризис — когда запасы действительно иссякают (меньше 30 лет спроса) у крупного народа;
        // в Летописи — не чаще раза в век на вид топлива.
        int bit = 1 << kind;
        if (frac < 0.6f && (g.CrisisKinds & bit) == 0 && FossilAvailable(g, kind) < 30f * ej && g.Pop >= 0.03 * World.WorldPopulation)
        {
            g.CrisisKinds |= bit;
            if (World.Year - World.LastCrisisReport[kind] >= 100)
            {
                World.LastCrisisReport[kind] = World.Year;
                World.Events.Publish(EventType.EnergyCrisis, g.Cell, a: g.Id, b: kind, magnitude: 1f - frac);
            }
        }
        return wantW * frac;
    }

    public static float Kardashev(double watts) => watts <= 0 ? 0f : (float)((Math.Log10(watts) - 6.0) / 10.0);

    // ---------- Выбросы и климат (kardashev.md, §6) ----------

    /// <summary>
    /// Ископаемое топливо копит CO₂: ≈ 38 ЭДж на ГтС, в атмосфере остаётся 45 % (IPCC AR5), 2,12 ГтС на ppm.
    /// Сила ΔF = 5,35·ln(C/C₀) Вт/м² (Myhre et al. 1998), равновесие ΔT = λ·ΔF, отклик климата — десятилетия.
    /// Потепление раз в 10 лет переносится в климат планеты и уровень моря (0,6 м на °C).
    /// </summary>
    /// <summary>Равновесное потепление: ΔF = 5,35·ln(C/280) Вт/м² (Myhre et al. 1998), ΔT = λ·ΔF.</summary>
    public static float GreenhouseWarming(double co2, float lambda) => lambda * 5.35f * MathF.Log((float)Math.Max(280.0, co2) / 280f);

    private void CarbonAndClimate()
    {
        var w = World;
        double gtc = w.FossilWatts * SecondsPerYear / 3.8e19;
        w.Co2 += 0.45 * gtc / 2.12;
        w.Co2 -= (w.Co2 - 280.0) / 200.0; // медленное поглощение океаном
        if (w.ClimateControl) w.Co2 -= (w.Co2 - 280.0) / 25.0;
        float target = GreenhouseWarming(w.Co2, w.Params.CarbonSensitivity);
        w.Warming += (target - w.Warming) / 40f;
        if (w.Year % 10 != 0 || MathF.Abs(w.Warming - w.AppliedWarming) < 0.25f) return;
        float delta = w.Warming - w.AppliedWarming;
        var p = w.Planet;
        p.GlobalTempDelta += delta;
        p.SeaLevel += 0.0006f * delta;
        w.AppliedWarming = w.Warming;
        var wasLand = (bool[])w.LandMask.Clone();
        PlanetGenerator.Recompute(p, w.Params);
        RefreshLifeLand(wasLand);
        ReconcileGroups(slow: true, cause: -1);
        UpdateTerritories();
        int step = (int)MathF.Floor(w.Warming * 2f);
        if (step > w.WarmingSteps && w.Warming > 0)
        {
            w.WarmingSteps = step;
            w.Events.Publish(EventType.ClimateWarming, -1, a: (int)MathF.Round(w.Warming * 10f), magnitude: Math.Min(1f, w.Warming / 4f));
        }
    }

    // ---------- Эпохи и проекты (kardashev.md, §5) ----------

    /// <summary>Эпоха народа — по открытиям (не по году); эпоха мира — по передовому народу.</summary>
    public int EraOf(Group g)
    {
        var w = World;
        if (w.Kardashev >= 1f && g.Has(Tech.OrbitalCollectors)) return 9;
        if (g.Has(Tech.GlobalInstitutions) && (g.Has(Tech.Fusion) || g.Has(Tech.Spaceflight))) return 8;
        if (g.Has(Tech.Computing) && g.Has(Tech.Networks) && g.Has(Tech.Fission)) return 7;
        if (g.Has(Tech.Electricity) && g.Has(Tech.Combustion)) return 6;
        if (g.Has(Tech.SteamEngine) && g.Watts > 1000f) return 5;
        if (g.Has(Tech.WaterWheel) && g.Has(Tech.Printing)) return 4;
        if (g.Has(Tech.Iron) && g.Has(Tech.Money)) return 3;
        if (g.Has(Tech.Agriculture) && g.Writing) return 2;
        return 1;
    }

    private void Eras()
    {
        var w = World;
        int best = 0; Group? leader = null;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            g.EraIdx = EraOf(g);
            if (g.EraIdx > best) { best = g.EraIdx; leader = g; }
        }
        if (best > w.WorldEra)
        {
            for (int e = w.WorldEra + 1; e <= best; e++)
                w.Events.Publish(EventType.EraBegan, leader?.Cell ?? -1, a: e, b: leader?.Id ?? -1, magnitude: e / 9f);
            w.WorldEra = best;
        }
    }

    /// <summary>
    /// Планетарные проекты: единая энергосеть (мощность ×1,3 у подключённых), орбитальные коллекторы
    /// (ярус +2·10¹⁴ Вт, всё быстрее; до 80 ярусов — 1,6·10¹⁶ Вт, потолок честный: планета перехватывает ~1,7·10¹⁷ Вт), управление климатом.
    /// </summary>
    private void Megaprojects()
    {
        var w = World;
        if (w.WorldEra < 7) return;
        double withNet = 0, inst = 0;
        bool collectors = false, climateTech = false;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            if (g.Has(Tech.Networks)) withNet += g.Pop;
            if (g.Has(Tech.GlobalInstitutions)) inst += g.Pop;
            if (g.Has(Tech.OrbitalCollectors)) collectors = true;
            if (g.Has(Tech.GlobalInstitutions) && g.Has(Tech.Chemistry) && g.Has(Tech.Computing)) climateTech = true;
        }
        double pop = Math.Max(1.0, w.WorldPopulation);
        if (withNet / pop >= 0.6 && inst / pop >= 0.3) Project(MegaprojectKind.GlobalGrid, 0.03f);
        // Ярусы коллекторов строятся всё быстрее: каждый готовый ярус удешевляет следующий (рост промышленности в космосе).
        // …и только в согласии: доля мира в общих институтах и вес крупнейшей державы (расколотый мир строит медленно, kardashev.md, §5).
        double biggest = 0;
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive) continue;
            double s2 = 0;
            foreach (var m in pol.Members) s2 += w.Groups[m].Pop;
            biggest = Math.Max(biggest, s2);
        }
        float unity = (float)(Math.Clamp(inst / pop / 0.5, 0.1, 1.0) * Math.Clamp(biggest / pop * 2.5, 0.15, 1.0));
        // Скорость ∝ unity²: согласие нужно и чтобы строить, и чтобы не растаскивать построенное.
        if (collectors) Project(MegaprojectKind.OrbitalCollectors, unity * unity * (1f + ProjectTiers(MegaprojectKind.OrbitalCollectors) / 6f) / 50f);
        if (climateTech && w.Warming > 1.5f) Project(MegaprojectKind.ClimateControl, 0.02f);
    }

    private void Project(MegaprojectKind kind, float rate)
    {
        var w = World;
        Megaproject? m = null;
        foreach (var x in w.Projects) if (x.Kind == kind) m = x;
        if (m == null)
        {
            m = new Megaproject { Kind = kind, Started = w.Year };
            w.Projects.Add(m);
            w.Events.Publish(EventType.MegaprojectStarted, -1, a: (int)kind, magnitude: 0.8f);
        }
        if (m.Done && kind != MegaprojectKind.OrbitalCollectors) return;
        if (kind == MegaprojectKind.OrbitalCollectors && m.Tiers >= 80) return;
        // Война тормозит стройку века.
        float peace = w.Year - LastWorldWar() < 10 ? 0.4f : 1f;
        m.Progress += rate * peace;
        if (m.Progress < 1f) return;
        m.Progress = 0;
        m.Tiers++;
        m.Done = true;
        switch (kind)
        {
            case MegaprojectKind.GlobalGrid: w.GlobalGrid = true; break;
            case MegaprojectKind.OrbitalCollectors: w.OrbitalWatts += 2e14; break;
            case MegaprojectKind.ClimateControl: w.ClimateControl = true; break;
        }
        w.Events.Publish(EventType.MegaprojectCompleted, -1, a: (int)kind, b: m.Tiers, magnitude: 0.9f);
    }

    private int ProjectTiers(MegaprojectKind kind)
    {
        foreach (var m in World.Projects) if (m.Kind == kind) return m.Tiers;
        return 0;
    }

    private long LastWorldWar()
    {
        var w = World;
        long last = -1000;
        for (int i = w.Wars.Count - 1; i >= 0 && i >= w.Wars.Count - 20; i--) last = Math.Max(last, w.Wars[i].Year);
        return last;
    }
}
