using BiomeT = Mirozdanie.Core.Planet.Biome;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

public sealed class ActiveEffect
{
    public InterventionType Type;
    public int Cell;
    public float Radius;
    public long EndYear;
    public float Factor;
    public int EventId = -1;
    /// <summary>Чудо урожая перекрывает засуху (множитель от max(1, текущий)).</summary>
    public bool Override;
    /// <summary>С какого года действует (потоп: сначала беда, потом плодородный ил).</summary>
    public long StartYear = long.MinValue;
}

public readonly record struct CenturyStat(long Year, double Population, int Peoples, int Polities, int Religions, int Languages, float Kardashev, int Era, float Warming, int Cities);

public sealed class Comet
{
    public int Id;
    public int Period;
    public long NextYear;
    public int Appearances;
}

public sealed partial class World
{
    public readonly List<Lang> Languages = new();
    public readonly List<Group> Groups = new();
    public readonly List<Person> People = new();
    public readonly List<Religion> Religions = new();
    public readonly List<SacredSite> Sites = new();
    public readonly List<Polity> Polities = new();
    public readonly List<War> Wars = new();
    public readonly List<ActiveEffect> Effects = new();
    public readonly List<Comet> Comets = new();
    public readonly List<(int a, int b, float volume)> TradeRoutes = new();
    // Цивилизации (docs/design/civilizations.md)
    public readonly List<City> Cities = new();
    // Петля бога (docs/design/god-loop.md)
    public readonly List<Oath> Oaths = new();
    public readonly List<Petition> Petitions = new();
    public CreatorStyle Style;
    public long CreatorWoundedUntil = -1;
    /// <summary>Сводка мира раз в век (для Летописи, lore.md §11).</summary>
    public readonly List<CenturyStat> Annals = new();
    public int MegaCities;
    public readonly List<Army> Armies = new();
    public readonly List<Relation> Relations = new();
    /// <summary>Индекс отношений: ключ A·2³²+B (только поиск, без обхода).</summary>
    public readonly Dictionary<long, int> RelationIndex = new();

    public int[] CellOwner = Array.Empty<int>();
    public float CreatorEnergy;
    public float EnergyIncome;
    public float EnergyIncomePeak;
    public double PowerWatts;
    public float Kardashev;
    // Лестница Кардашёва (docs/design/kardashev.md)
    public double FossilWatts, OrbitalWatts, WorldPopulation;
    public float[] CoalLeft = Array.Empty<float>(), OilLeft = Array.Empty<float>(), UraniumLeft = Array.Empty<float>();
    public double Co2 = 280.0;
    public float Warming, AppliedWarming;
    public int WarmingSteps;
    public int WorldEra = 1;
    public readonly List<Megaproject> Projects = new();
    public bool GlobalGrid, ClimateControl;
    public long NuclearWinterUntil = -1, LastNuclearWar = -10000;
    public readonly long[] LastCrisisReport = { -1000, -1000, -1000 };
    public int RegionsX, RegionsY;
    public float[] RegionAnomaly = Array.Empty<float>();
    public bool[] RegionDrought = Array.Empty<bool>();
    public int[] RegionDroughtEvent = Array.Empty<int>();
    public float[] InitialFauna = Array.Empty<float>();
    public readonly HashSet<int> MegafaunaLost = new(); // только проверка принадлежности, без обхода
    public InterventionScript? Script;
    /// <summary>Название сценария, с которым мир был сотворён (null — без сценария).</summary>
    public string? ScenarioName;
    public readonly Queue<Intervention> Commands = new();
    /// <summary>Журнал применённых вмешательств в порядке применения — основа сохранения (replay).</summary>
    public readonly List<Intervention> AppliedLog = new();
    /// <summary>Песочница: силы ничего не стоят.</summary>
    public bool Sandbox;
    public long LastCreatorAct = -1;
    public int ProtoLanguage = -1;
    /// <summary>Праязык в момент Пробуждения (для имён видов и Хроники).</summary>
    public Lang? ProtoSnapshot;
    public Name WorldName = Name.Empty;
    public int FirstAnomalyGroup = -1;

    private readonly Dictionary<long, (long year, float value)> _intelligibility = new();

    partial void InitHistoryPartial()
    {
        CellOwner = new int[Planet.EW * Planet.EH];
        Array.Fill(CellOwner, -1);
        LandMask = new bool[Planet.EW * Planet.EH];
        for (int c = 0; c < LandMask.Length; c++) LandMask[c] = Planet.EIsLand(c);
        CreatorEnergy = Params.StartEnergy;
        RegionsX = Math.Max(1, Planet.EW / 16);
        RegionsY = Math.Max(1, Planet.EH / 16);
        RegionAnomaly = new float[RegionsX * RegionsY];
        RegionDrought = new bool[RegionsX * RegionsY];
        RegionDroughtEvent = new int[RegionsX * RegionsY];
    }

    public int RegionOf(int cell)
    {
        int ex = cell % Planet.EW, ey = cell / Planet.EW;
        return Math.Min(RegionsY - 1, ey / 16) * RegionsX + Math.Min(RegionsX - 1, ex / 16);
    }

    public IEnumerable<Group> LiveGroups()
    {
        foreach (var g in Groups) if (g.Alive) yield return g;
    }

    public Lang LangOf(Group g) => Languages[g.LanguageId];

    /// <summary>Взаимопонятность языков: доля схожих базовых слов (кеш на 25 лет).</summary>
    public float Intelligibility(int a, int b)
    {
        if (a == b) return 1f;
        if (a > b) (a, b) = (b, a);
        long key = (long)a * 100_000 + b;
        if (_intelligibility.TryGetValue(key, out var v) && Year - v.year < 25) return v.value;
        var la = Languages[a]; var lb = Languages[b];
        float sum = 0;
        foreach (var c in Concepts.Core) sum += SoundLaws.Similarity(la.Word(c), lb.Word(c));
        float sim = sum / Concepts.Core.Length;
        float val = Math.Clamp((sim - 0.25f) / 0.6f, 0f, 1f);
        _intelligibility[key] = (Year, val);
        return val;
    }

    // ---------- Имена ----------

    /// <summary>Повторяет генерацию, пока имя не станет уникальным среди <paramref name="taken"/>.</summary>
    public static Name Unique(Func<int, Name> generate, Func<string, bool> taken)
    {
        var n = generate(0);
        for (int attempt = 1; attempt < 30 && taken(n.Native); attempt++) n = generate(attempt);
        return n;
    }

    public static readonly Concept[] Distinguishers =
    {
        Concept.New, Concept.Old, Concept.Clear, Concept.Red, Concept.White, Concept.Black, Concept.Strong, Concept.Star,
        Concept.Stone, Concept.Wind, Concept.Moon, Concept.Light, Concept.Heart, Concept.Mountain, Concept.River, Concept.Sea,
    };

    public static readonly string[] DistinguisherGloss =
    {
        "новь", "старина", "ясность", "багрянец", "белизна", "чернота", "сила", "звезда", "камень", "ветер", "луна", "свет", "сердце", "гора", "река", "море",
    };

    public Name PersonName(Lang L, Rng rng, out Concept meaning)
    {
        Concept[] pool = { Concept.Strong, Concept.Red, Concept.Star, Concept.Bird, Concept.Stone, Concept.Wind, Concept.Light,
            Concept.River, Concept.Fire, Concept.Moon, Concept.Clear, Concept.Heart, Concept.Voice, Concept.Tree, Concept.Old, Concept.White };
        meaning = pool[rng.NextInt(pool.Length)];
        int[] stem = L.Word(meaning);
        // Слишком короткие имена («Ы») неразличимы — тогда имя из двух корней.
        if (rng.Chance(0.35) || stem.Length < 3)
        {
            var second = pool[rng.NextInt(pool.Length)];
            if (second == meaning) second = second == Concept.Star ? Concept.Wind : Concept.Star;
            { stem = L.Compound(meaning, second); return L.MakeName(L.WithSuffix(stem, Concept.SuffixPerson), $"{Concepts.Russian[(int)meaning]}-{Concepts.Russian[(int)second]}"); }
        }
        return L.MakeName(L.WithSuffix(stem, Concept.SuffixPerson), Concepts.Russian[(int)meaning]);
    }

    /// <summary>Самоназвание группы: «люди реки», «люди гор» — из особенности местности.</summary>
    public Name GroupName(Lang L, int cell, Rng rng)
    {
        var p = Planet;
        Concept feat;
        string gloss;
        if (p.ERiver[cell] > 0.15f) { feat = Concept.River; gloss = "люди реки"; }
        else if (p.EElev[cell] > 1.6f) { feat = Concept.Mountain; gloss = "люди гор"; }
        else if (p.ECoast[cell]) { feat = Concept.Sea; gloss = "люди моря"; }
        else if (p.ETemp[cell] < 0) { feat = Concept.Cold; gloss = "люди холода"; }
        else if (p.EBiome[cell] is BiomeT.Desert or BiomeT.ColdDesert) { feat = Concept.Hot; gloss = "люди зноя"; }
        else if (p.EBiome[cell] is BiomeT.TemperateForest or BiomeT.TropicalRainforest or BiomeT.Taiga or BiomeT.TemperateRainforest or BiomeT.TropicalSeasonalForest)
        { feat = Concept.Tree; gloss = "люди леса"; }
        else
        {
            Concept[] alt = { Concept.Sun, Concept.Wind, Concept.Stone, Concept.Grain, Concept.Bird, Concept.Red, Concept.White };
            feat = alt[rng.NextInt(alt.Length)];
            gloss = "люди: " + Concepts.Russian[(int)feat];
        }
        int[] w = rng.Chance(0.75) ? L.WithSuffix(L.Word(feat), Concept.SuffixPeople) : L.Compound(feat, Concept.People);
        return L.MakeName(w, gloss);
    }

    private readonly List<Name> _speciesNames = new();

    /// <summary>Название вида на праязыке первого разумного народа (уникальное, с признаком места обитания при совпадении).</summary>
    public Name SpeciesName(Species s)
    {
        if (ProtoLanguage < 0) return new Name($"вид №{s.Id}", "");
        while (_speciesNames.Count <= s.Id)
        {
            var sp = Life.Species[_speciesNames.Count];
            var n = MakeSpeciesName(sp, 0);
            for (int k = 1; k < 12 && _speciesNames.Exists(x => x.Native == n.Native); k++) n = MakeSpeciesName(sp, k);
            _speciesNames.Add(n);
        }
        return _speciesNames[s.Id];
    }

    private Name MakeSpeciesName(Species s, int variant)
    {
        var L = ProtoSnapshot ?? Languages[ProtoLanguage];
        var g = s.Genome;
        bool bird = g[Trait.Mass] < -0.5f && g[Trait.Diet] < 0.65f;
        Concept head = bird ? Concept.Bird : Concept.Beast;
        Concept mod = g[Trait.Mass] > 1.8f ? Concept.Great : g[Trait.Mass] < 0f ? Concept.Small
            : g[Trait.Hue] < 0.33f ? Concept.Red : g[Trait.Hue] < 0.66f ? Concept.White : Concept.Black;
        if (g[Trait.Diet] > 0.7f && mod != Concept.Great) mod = Concept.Blood;
        // Признаки для различения: окраска, затем место обитания.
        // Климатический признак не должен противоречить виду: «снежный» — только у любящих холод.
        Concept climate = g[Trait.ThermalOpt] < 5f ? Concept.Cold : g[Trait.ThermalOpt] > 22f ? Concept.Hot : Concept.Tree;
        Concept[] extra = { g[Trait.Hue] < 0.33f ? Concept.Red : g[Trait.Hue] < 0.66f ? Concept.White : Concept.Black,
            climate, Concept.Mountain, Concept.River, Concept.Sea, Concept.Night, Concept.Stone, Concept.Wind, Concept.Star, Concept.Moon, Concept.Tree };
        var parts = new List<Concept> { mod };
        if (variant > 0)
        {
            var x = extra[(variant - 1) % extra.Length];
            if (x != mod) parts.Add(x);
        }
        parts.Add(head);
        var w = L.Compound(parts.ToArray());
        static string Adj(Concept c, bool fem) => c switch
        {
            Concept.Great => fem ? "великая" : "великий",
            Concept.Small => fem ? "малая" : "малый",
            Concept.Red => fem ? "красная" : "красный",
            Concept.White => fem ? "белая" : "белый",
            Concept.Black => fem ? "чёрная" : "чёрный",
            Concept.Blood => fem ? "кровавая" : "кровавый",
            Concept.Tree => fem ? "лесная" : "лесной",
            Concept.Mountain => fem ? "горная" : "горный",
            Concept.River => fem ? "речная" : "речной",
            Concept.Sea => fem ? "морская" : "морской",
            Concept.Night => fem ? "ночная" : "ночной",
            Concept.Cold => fem ? "снежная" : "снежный",
            Concept.Hot => fem ? "знойная" : "знойный",
            Concept.Stone => fem ? "каменная" : "каменный",
            Concept.Wind => fem ? "ветряная" : "ветряной",
            Concept.Star => fem ? "звёздная" : "звёздный",
            Concept.Moon => fem ? "лунная" : "лунный",
            _ => "",
        };
        string gloss = string.Join(" ", parts.Take(parts.Count - 1).Select(c => Adj(c, bird))) + (bird ? " птица" : " зверь");
        return L.MakeName(w, gloss);
    }
}
