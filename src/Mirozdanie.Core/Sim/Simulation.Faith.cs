using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>
/// Вера и Создатель — сердце главного цикла. Все формулы — docs/design/faith-creator.md.
/// </summary>
public sealed partial class Simulation
{
    public const float Epsilon = 0.15f;         // ограниченное доверие (Deffuant et al. 2000)
    public const float SchismDistance = 0.2f;   // по одному пункту доктрины (MaxDiff)
    private readonly List<WorldEvent> _perceived = new(256);
    public static bool DebugSchism = Environment.GetEnvironmentVariable("MIRO_SCHISM") == "1";
    private readonly List<int> _tmpGroups = new(64);
    private bool _envy;

    partial void InitPartial()
    {
        EventType[] perceivable =
        {
            EventType.NaturalDrought, EventType.Drought, EventType.Rain, EventType.LightningStrike, EventType.MiracleHarvest,
            EventType.Vision, EventType.MeteorImpact, EventType.Earthquake, EventType.Comet, EventType.Famine, EventType.Plague,
            EventType.Battle, EventType.MegafaunaGone, EventType.DroughtEnded, EventType.Bounty,
            EventType.Healing, EventType.Inspiration, EventType.Blessing, EventType.ColdSnap, EventType.Flood, EventType.HerdsReturn,
            EventType.RelicAppeared, EventType.CursedGround, EventType.DynastyBlessed, EventType.DynastyCursed, EventType.PeaceImposed,
            EventType.CreatorAppeared, EventType.Threat, EventType.PrayerAnswered,
            EventType.Terraformed, EventType.RiverBorn, EventType.Eruption, EventType.SeaLevelChanged, EventType.ClimateShifted,
            EventType.ContinentDrifted, EventType.Refugees,
        };
        foreach (var t in perceivable) World.Events.Subscribe(t, OnPerceivable);
    }

    private void OnPerceivable(in WorldEvent e)
    {
        if (World.Era == Era.History) _perceived.Add(e);
    }

    // ---------- Эпитеты ----------

    public static (Concept a, Concept b, string gloss) EpithetParts(EpithetKind k) => k switch
    {
        EpithetKind.FireThrower => (Concept.Fire, Concept.Throw, "Бросающий огонь"),
        EpithetKind.LightGiver => (Concept.Light, Concept.Give, "Дающий свет"),
        EpithetKind.SkyVoice => (Concept.Sky, Concept.Voice, "Голос неба"),
        EpithetKind.RainHolder => (Concept.Rain, Concept.Hold, "Удерживающий дождь"),
        EpithetKind.AngrySun => (Concept.Angry, Concept.Sun, "Гневное солнце"),
        EpithetKind.BreadGiver => (Concept.Bread, Concept.Give, "Дающий хлеб"),
        EpithetKind.GiverTaker => (Concept.Give, Concept.Hold, "Дающий и берущий"),
        EpithetKind.Chooser => (Concept.Choose, Concept.People, "Избирающий народы"),
        EpithetKind.RainGiver => (Concept.Rain, Concept.Give, "Дарующий дождь"),
        EpithetKind.RainWord => (Concept.Rain, Concept.Word, "Слово дождя"),
        EpithetKind.DreamSpeaker => (Concept.Dream, Concept.Speak, "Говорящий во сне"),
        EpithetKind.SkyFire => (Concept.Sky, Concept.Fire, "Огонь с неба"),
        EpithetKind.FallingStar => (Concept.Star, Concept.Fall, "Падающая звезда"),
        EpithetKind.EarthShaker => (Concept.Earth, Concept.Shake, "Сотрясающий землю"),
        EpithetKind.Judge => (Concept.Great, Concept.Judge, "Великий судья"),
        EpithetKind.HungerMother => (Concept.Hunger, Concept.Mother, "Мать голода"),
        EpithetKind.VictoryGiver => (Concept.Victory, Concept.Give, "Дающий победу"),
        EpithetKind.Tester => (Concept.Test, Concept.Father, "Отец испытаний"),
        EpithetKind.Silent => (Concept.Silent, Concept.God, "Молчащий"),
        EpithetKind.Hearer => (Concept.Hear, Concept.Heart, "Внимающий"),
        EpithetKind.EarthShaper => (Concept.Earth, Concept.Hand, "Лепящий землю"),
        EpithetKind.SeaLord => (Concept.Sea, Concept.Chief, "Владыка вод"),
        EpithetKind.RiverGiver => (Concept.River, Concept.Give, "Дающий реки"),
        EpithetKind.Drowner => (Concept.Water, Concept.Death, "Топящий"),
        EpithetKind.TrueWord => (Concept.Clear, Concept.Word, "Верный слову"),
        EpithetKind.Deceiver => (Concept.Bad, Concept.Word, "Обманщик"),
        _ => (Concept.Hidden, Concept.God, "Скрытый бог"),
    };

    public static EpithetKind EpithetFor(EventType t, InterpretationKind k) => t switch
    {
        EventType.LightningStrike => k is InterpretationKind.Punishment or InterpretationKind.Wrath ? EpithetKind.FireThrower
            : k is InterpretationKind.Gift or InterpretationKind.Bargain ? EpithetKind.LightGiver : EpithetKind.SkyVoice,
        EventType.Drought or EventType.NaturalDrought or EventType.Famine => k switch
        {
            InterpretationKind.Punishment => EpithetKind.RainHolder,
            InterpretationKind.Trial => EpithetKind.Tester,
            InterpretationKind.Favoritism => EpithetKind.Chooser,
            _ => t == EventType.Famine ? EpithetKind.HungerMother : EpithetKind.AngrySun,
        },
        EventType.MiracleHarvest => k switch
        {
            InterpretationKind.Bargain => EpithetKind.GiverTaker,
            InterpretationKind.Favoritism => EpithetKind.Chooser,
            _ => EpithetKind.BreadGiver,
        },
        EventType.Rain or EventType.DroughtEnded => k == InterpretationKind.Bargain ? EpithetKind.RainWord : EpithetKind.RainGiver,
        EventType.Bounty => k == InterpretationKind.Bargain ? EpithetKind.GiverTaker : EpithetKind.BreadGiver,
        EventType.Vision => EpithetKind.DreamSpeaker,
        EventType.MeteorImpact => k is InterpretationKind.Punishment or InterpretationKind.Wrath ? EpithetKind.SkyFire : EpithetKind.FallingStar,
        EventType.Earthquake => EpithetKind.EarthShaker,
        EventType.Plague => k == InterpretationKind.Punishment ? EpithetKind.Judge : EpithetKind.HungerMother,
        EventType.Battle => k == InterpretationKind.Gift ? EpithetKind.VictoryGiver : EpithetKind.Tester,
        EventType.Comet => EpithetKind.FallingStar,
        EventType.Healing or EventType.PrayerAnswered => EpithetKind.Hearer,
        EventType.Blessing or EventType.HerdsReturn => EpithetKind.BreadGiver,
        EventType.ColdSnap or EventType.CursedGround or EventType.DynastyCursed => EpithetKind.Judge,
        EventType.Flood => k == InterpretationKind.Punishment ? EpithetKind.Judge : EpithetKind.RainGiver,
        EventType.RelicAppeared => EpithetKind.SkyVoice,
        EventType.DynastyBlessed or EventType.PeaceImposed or EventType.Inspiration => EpithetKind.LightGiver,
        EventType.CreatorAppeared => k is InterpretationKind.Punishment or InterpretationKind.Wrath ? EpithetKind.SkyFire : EpithetKind.LightGiver,
        EventType.Threat => EpithetKind.AngrySun,
        EventType.Terraformed or EventType.ContinentDrifted => k is InterpretationKind.Punishment or InterpretationKind.Wrath ? EpithetKind.EarthShaker : EpithetKind.EarthShaper,
        EventType.RiverBorn => EpithetKind.RiverGiver,
        EventType.Eruption => k is InterpretationKind.Punishment or InterpretationKind.Wrath ? EpithetKind.SkyFire : EpithetKind.EarthShaker,
        EventType.SeaLevelChanged => k is InterpretationKind.Punishment or InterpretationKind.Wrath ? EpithetKind.Drowner : EpithetKind.SeaLord,
        EventType.Refugees => EpithetKind.Drowner,
        EventType.ClimateShifted => k is InterpretationKind.Gift ? EpithetKind.LightGiver : EpithetKind.AngrySun,
        _ => EpithetKind.Hidden,
    };

    public Name EpithetName(Lang L, EpithetKind k)
    {
        var (a, b, gloss) = EpithetParts(k);
        int[] word = k == EpithetKind.Silent ? L.WithSuffix(L.Word(Concept.Silent), Concept.SuffixPerson) : L.Compound(a, b);
        return L.MakeName(word, gloss);
    }

    private static Concept EventConcept(EventType t) => t switch
    {
        EventType.LightningStrike => Concept.Lightning,
        EventType.Drought or EventType.NaturalDrought => Concept.Sun,
        EventType.MiracleHarvest => Concept.Grain,
        EventType.Rain or EventType.DroughtEnded => Concept.Rain,
        EventType.Bounty => Concept.Grain,
        EventType.Vision => Concept.Dream,
        EventType.MeteorImpact => Concept.Star,
        EventType.Earthquake => Concept.Earth,
        EventType.Plague => Concept.Blood,
        EventType.Famine => Concept.Hunger,
        EventType.Battle => Concept.War,
        EventType.Comet => Concept.Star,
        EventType.RelicAppeared => Concept.Stone,
        EventType.CreatorAppeared => Concept.Light,
        EventType.Healing => Concept.Heart,
        EventType.Flood => Concept.Water,
        EventType.ColdSnap => Concept.Cold,
        EventType.Terraformed or EventType.ContinentDrifted => Concept.Mountain,
        EventType.RiverBorn => Concept.River,
        EventType.Eruption => Concept.Fire,
        EventType.SeaLevelChanged or EventType.Refugees => Concept.Sea,
        EventType.ClimateShifted => Concept.Sun,
        _ => Concept.Sky,
    };

    // ---------- Стадия веры ----------

    private void FaithStage()
    {
        var w = World;
        var rng = w.Rng[StreamId.Faith];
        int n = _perceived.Count;
        for (int i = 0; i < n; i++) Perceive(_perceived[i], rng);
        _perceived.RemoveRange(0, n);

        Prayers();
        Prophecies(rng);
        SpreadReligions(rng);
        BoundedConfidence(rng);
        foreach (var r in w.Religions) r.Cred *= 0.98f;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            g.Guilt *= 0.95f;
            g.B *= 0.99f; // память о милостях и бедах тускнеет (B отражает последние десятилетия)
            g.D *= 0.985f;
            g.G *= 0.99f;
            g.P *= 0.995f;
        }
        if (w.Year % 10 == 0)
        {
            UpdateReligionStats();
            Schisms(rng);
            Attitudes(rng);
            Anomalies();
            SilentCreator();
            FadeCults();
        }
        CreatorIncome();
    }

    private (float radius, float anomaly, float harm, bool directOnly) PerceptionOf(in WorldEvent e, Group g)
    {
        var w = World;
        bool byC = e.ByCreator;
        float presence = 0.6f + 0.8f * w.Params.CreatorPresence;
        switch (e.Type)
        {
            case EventType.NaturalDrought:
                return (10f, 0.1f, Math.Max(0.15f, 1f - g.FoodRatio), false);
            case EventType.Drought:
                return (6f, 0.4f * presence, Math.Max(0.25f, 1f - g.FoodRatio), false);
            case EventType.Rain:
                return (5f, 0.5f * presence, -0.4f, false);
            case EventType.LightningStrike:
                return (2.5f, byC ? Math.Min(1f, 0.9f * presence) : 0.3f, e.B >= 0 ? 0.5f : 0.15f, false);
            case EventType.MiracleHarvest:
                return (3f, Math.Min(1f, 0.8f * presence), -0.8f, false);
            case EventType.Vision:
                return (0f, 0.7f, 0f, true);
            case EventType.MeteorImpact:
                return (14f, 0.5f * presence, Math.Max(0.1f, g.YearLoss + 0.2f), false);
            case EventType.Earthquake:
                return (5f, byC ? 0.7f * presence : 0.2f, g.YearLoss + 0.1f, false);
            case EventType.Healing: return (0f, 0.8f, -0.6f, true);
            case EventType.Inspiration: return (0f, 0.6f, -0.3f, true);
            case EventType.Blessing: return (0f, 0.7f, -0.5f, true);
            case EventType.ColdSnap: return (7f, 0.6f * presence, Math.Max(0.2f, 1f - g.FoodRatio), false);
            case EventType.Flood: return (4f, 0.5f * presence, g.YearLoss + 0.2f, false);
            case EventType.HerdsReturn: return (4f, 0.5f, -0.4f, false);
            case EventType.RelicAppeared: return (4f, 1f, -0.2f, false);
            case EventType.CursedGround: return (3f, 0.9f, 0.3f, false);
            case EventType.DynastyBlessed: return (0f, 0.7f, -0.4f, true);
            case EventType.DynastyCursed: return (0f, 0.7f, 0.4f, true);
            case EventType.PeaceImposed: return (0f, 0.6f, -0.3f, true);
            case EventType.CreatorAppeared: return (8f, 1f, -0.3f, false);
            case EventType.Threat: return (0f, 0.8f, 0.3f, true);
            case EventType.PrayerAnswered: return (0f, 0.9f, -0.7f, true);
            case EventType.Comet:
                return (1000f, 0.3f, 0f, false);
            // Терраформирование (terraforming.md, §5): вред или дар считается для каждого народа.
            case EventType.Terraformed:
            {
                bool slow = e.C == 1;
                float an = AnomalyOf((TerraformKind)e.A, slow) * (slow ? 1f : presence);
                return (8f, Math.Min(1f, an), TerraformHarm(e, g, 3f), false);
            }
            case EventType.RiverBorn:
                return (4f, Math.Min(1f, AnomalyOf(TerraformKind.River, e.C == 1) * (e.C == 1 ? 1f : presence)), -0.45f, false);
            case EventType.Eruption:
                return (8f, 0.4f * presence, Math.Max(0.15f, g.YearLoss + 0.25f), false);
            case EventType.SeaLevelChanged:
            case EventType.ClimateShifted:
            {
                bool slow = e.C == 1;
                float an = AnomalyOf(TerraformKind.SeaRise, slow) * (slow ? 1f : presence);
                float harm = g.YearLoss > 0.05f ? g.YearLoss + 0.3f : e.A > 0 ? (g.Subsistence == Subsistence.Fishing ? 0.25f : 0.1f) : -0.15f;
                return (1000f, Math.Min(1f, an), harm, false);
            }
            case EventType.ContinentDrifted:
                return (30f, Math.Min(1f, AnomalyOf(TerraformKind.Drift, e.C == 1) * (e.C == 1 ? 1f : presence)), Math.Max(0.1f, g.YearLoss + 0.2f), false);
            case EventType.Refugees:
                return (0f, (e.Flags & EventFlags.ByCreator) != 0 ? 0.8f : 0.2f, e.Magnitude + 0.3f, true);
            case EventType.Famine:
                return (0f, 0.05f, e.Magnitude, true);
            case EventType.Plague:
                return (0f, 0.15f, e.Magnitude + 0.2f, true);
            case EventType.Battle:
                return (0f, 0f, e.A == g.Id ? -0.5f : 0.6f, true);
            case EventType.MegafaunaGone:
                return (0f, 0.1f, 0.3f, true);
            case EventType.DroughtEnded:
                return (10f, 0.15f, -0.25f, false);
            case EventType.Bounty:
                return (10f, 0.1f, -0.5f, false);
        }
        return (0, 0, 0, true);
    }

    /// <summary>Вред правки земли для народа: кто пострадал — кара, кому дали — дар, далёким — знамение.</summary>
    private float TerraformHarm(in WorldEvent e, Group g, float near)
    {
        if (g.YearLoss > 0.05f) return g.YearLoss + 0.2f;
        bool close = e.Cell >= 0 && World.Planet.EcoDistance(g.Cell, e.Cell) <= near;
        return e.B > 0 ? (close ? -0.5f : -0.15f) : e.B < 0 ? (close ? 0.35f : 0.12f) : 0.05f;
    }

    private void Perceive(in WorldEvent e, Rng rng)
    {
        var w = World;
        var p = w.Planet;
        // Кто воспринимает: прямые свидетели и (с весом 0,4) — их соседи по слухам.
        int count = w.Groups.Count;
        for (int gi = 0; gi < count; gi++)
        {
            var g = w.Groups[gi];
            if (!g.Alive) continue;
            var (radius, anomaly, harm, directOnly) = PerceptionOf(e, g);
            float weight;
            if (directOnly)
            {
                bool involved = e.Type == EventType.Battle ? (e.A == g.Id || e.B == g.Id)
                    : e.Type == EventType.MegafaunaGone ? e.B == g.Id : e.A == g.Id;
                if (!involved) continue;
                weight = 1f;
            }
            else
            {
                if (e.Cell < 0) weight = 1f;
                else
                {
                    float d = p.EcoDistance(g.Cell, e.Cell);
                    if (d <= radius) weight = 1f;
                    else if (d <= radius + 6f)
                    {
                        // Весть искажается через языки (faith-creator.md, §1).
                        int witness = e.Cell >= 0 && w.CellOwner[e.Cell] >= 0 ? w.CellOwner[e.Cell] : -1;
                        float intel = witness >= 0 ? w.Intelligibility(w.Groups[witness].LanguageId, g.LanguageId) : 0.5f;
                        weight = 0.4f * intel;
                        harm *= 0.3f;
                        if (weight < 0.1f) continue;
                    }
                    else continue;
                }
            }
            Interpret(g, e, weight, harm, anomaly, rng);
        }
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    /// <summary>
    /// Гиперактивное обнаружение агентов (Guthrie 1993; Barrett 2000): p = σ(4·(S + 0,8·piety + 0,9·anomaly − 1,2·N − 0,6)).
    /// Негативный сдвиг: удачи приписывают намерению реже бед (Morewedge 2009, JEP: General 138), поэтому S×0,5 при выгоде.
    /// </summary>
    public static float AgentProbability(float S, float harm, float piety, float anomaly, float naturalism)
    {
        float sAgent = harm < -0.05f ? 0.5f * S : S;
        return Sigmoid(4f * (sAgent + 0.8f * piety + 0.9f * anomaly - 1.2f * naturalism - 0.6f));
    }

    /// <summary>Шаг SIR внутри группы: s' = s + β·s(1−s)·compat − γ·s + конформизм (faith-creator.md, §5).</summary>
    public static float SirStep(float s, float beta, float gamma, float compat, float conform)
        => Math.Clamp(s + beta * s * (1 - s) * compat - gamma * s + conform * s, 0f, 1f);

    /// <summary>Совместимость идеи с убеждениями (ограниченное доверие): max(0, 1 − d/0,3).</summary>
    public static float Compatibility(float[] doctrine, float[] belief) => Math.Max(0f, 1f - Doctrine.Distance(doctrine, belief) / 0.3f);

    /// <summary>Модель Деффюана (Deffuant et al. 2000): сближение на μ, только если расстояние &lt; ε.</summary>
    public static bool BoundedConfidenceStep(float[] a, float[] b, float eps, float mu)
    {
        if (Doctrine.Distance(a, b) >= eps) return false;
        for (int i = 0; i < a.Length; i++) a[i] += mu * (b[i] - a[i]);
        return true;
    }

    private void Interpret(Group g, in WorldEvent e, float weight, float harm, float anomaly, Rng rng)
    {
        var w = World;
        // Зависть: чудо досталось другим, а мы голодаем (core-loop.md, цепочка 3).
        bool envy = e.ByCreator && e.Type is EventType.MiracleHarvest or EventType.Rain && e.A != g.Id && g.FoodRatio < 0.85f
                    && w.Planet.EcoDistance(g.Cell, e.Cell) > 1.5f;
        if (envy) { harm = 0.3f + (0.85f - g.FoodRatio); _envy = true; }
        else _envy = false;
        float S = Math.Clamp(Math.Max(0.2f, e.Magnitude) * weight * (1f + MathF.Abs(harm)), 0f, 1.5f);
        if (e.Type == EventType.Comet) S = 0.35f * weight;
        // Гиперактивное обнаружение агентов (Guthrie 1993; Barrett 2000).
        float pAgent = AgentProbability(S, harm, g.Piety, anomaly, g.N);
        if (e.Type == EventType.Vision) pAgent = Math.Max(pAgent, 0.85f - 0.4f * g.N);
        if (e.Type == EventType.Comet && g.Astronomy) pAgent *= 0.3f;
        bool agent = rng.NextFloat() < pAgent;
        if (e.ByCreator && weight >= 0.99f)
        {
            g.LastCreatorSeen = w.Year;
            if (anomaly >= 0.45f)
            {
                g.AnomalyLog.Add((e.Cell, w.Year, anomaly));
                if (g.AnomalyLog.Count > 16) g.AnomalyLog.RemoveAt(0);
            }
        }

        InterpretationKind kind;
        if (!agent)
        {
            kind = InterpretationKind.Natural;
            // Натурализм не обгоняет знание: объяснять природой может только тот, кто её изучил.
            if (g.N < g.Knowledge) g.N = Math.Min(g.Knowledge, g.N + 0.005f * S + 0.005f * g.Knowledge);
        }
        else kind = ChooseKind(g, e, harm, anomaly, S, rng);

        ApplyInterpretation(g, e, kind, S, harm, anomaly);
        // Мандат неба (идея 47): беда, истолкованная как кара или гнев, бьёт по легитимности правителей столицы.
        if (g.PolityId >= 0 && harm > 0.1f && kind is InterpretationKind.Punishment or InterpretationKind.Wrath)
        {
            var pol = w.Polities[g.PolityId];
            if (pol.Capital == g.Id && pol.Alive)
            {
                pol.Curse = Math.Min(1f, pol.Curse + 0.12f * S);
                if (pol.Curse > 0.5f && !pol.MandateLost)
                {
                    pol.MandateLost = true;
                    w.Events.Publish(EventType.MandateLost, g.Cell, a: pol.Id, magnitude: 0.7f, cause: e.Id);
                }
            }
        }
        var mem = new MemoryEntry { Event = e.Type, Kind = kind, Year = w.Year, Cell = e.Cell, Anomaly = anomaly, Strength = S, ByCreator = e.ByCreator };
        g.Remember(mem);

        if (S > 0.3f || e.ByCreator)
            w.Events.Publish(EventType.Interpretation, e.Cell, a: g.Id, b: (int)kind, c: (int)e.Type, magnitude: S * Math.Min(1f, Demo.Weight(g.Pop) + 0.3f), cause: e.Id,
                flags: e.ByCreator ? EventFlags.ByCreator : EventFlags.None);

        if (e.Type == EventType.Vision) { FoundVisionCult(g, e, rng, kind == InterpretationKind.Natural); }
        if (kind == InterpretationKind.Natural) return;
        g.AgentiveCount++;

        // Эпитеты (faith-creator.md, §3).
        var ek = EpithetFor(e.Type, kind);
        if (S > 0.55f && g.Epithets.Count < 4 && !g.Epithets.Exists(x => x.Kind == ek))
        {
            g.Epithets.Add(new Epithet { Kind = ek, Name = EpithetName(w.LangOf(g), ek), Year = w.Year });
            w.Events.Publish(EventType.EpithetGained, e.Cell, a: g.Id, b: (int)ek, cause: e.Id);
        }
        // Табу на понятие самого страшного события.
        if (harm > 0.4f && S > 0.8f && S > g.TabooStrength)
        {
            g.Taboo = (int)EventConcept(e.Type);
            g.TabooStrength = S;
        }

        if (e.Type == EventType.Vision) return;

        // Рождение веры у группы без веры.
        if (g.ReligionId < 0 && g.AgentiveCount >= 4) FoundReligion(g, e.Id, -1, rng);

        // Святыни на местах сильных чудес.
        if (g.ReligionId >= 0 && e.Cell >= 0 && S > 0.6f && (e.ByCreator || e.Type == EventType.Earthquake && S > 0.9f) && e.Type is not (EventType.Famine or EventType.Plague or EventType.Battle or EventType.Comet or EventType.MegafaunaGone))
            TryFoundSite(g, e);

        // Культ: сильное толкование, расходящееся с доктриной веры.
        if (g.ReligionId >= 0 && g.CultId < 0 && S > 0.7f)
        {
            var r = w.Religions[g.ReligionId];
            if (Doctrine.Distance(g.Belief, r.Doctrine) > Epsilon && rng.Chance(0.35))
                FoundCult(g, e.Id, null, rng);
        }
    }

    private InterpretationKind ChooseKind(Group g, in WorldEvent e, float harm, float anomaly, float S, Rng rng)
    {
        var w = World;
        Span<float> wts = stackalloc float[8];
        float moral = g.Belief[Doctrine.Moralism];
        bool neighborBlessed = _envy;
        if (harm > 0.1f && !neighborBlessed)
        {
            // Избирательность: соседу недавно досталось благо, а у нас беда (идея 1).
            for (int k = 0; k < g.Neighbors.Count && !neighborBlessed; k++)
            {
                var o = w.Groups[g.Neighbors[k]];
                for (int m = 0; m < o.MemoryCount; m++)
                {
                    ref var me = ref o.Memory[m];
                    if (me.ByCreator && w.Year - me.Year <= 3 && me.Event is EventType.MiracleHarvest or EventType.Rain && me.Strength > 0.5f)
                    { neighborBlessed = true; break; }
                }
            }
        }
        bool recentPrayer = g.LastPrayer >= 0 && w.Year - g.LastPrayer <= 6;
        bool recentCrisis = w.Year - g.LastCrisis <= 5;
        if (harm > 0.1f)
        {
            wts[(int)InterpretationKind.Punishment] = 0.5f + 1.5f * g.Guilt + 1.0f * moral + 0.3f * g.Culture[CultureTrait.Collectivism];
            wts[(int)InterpretationKind.Wrath] = 0.6f + 0.8f * (1 - moral) + 0.5f * Math.Max(0, -g.B) + 0.3f * g.Belief[Doctrine.Wrathfulness];
            wts[(int)InterpretationKind.Trial] = 0.2f + 1.2f * g.Piety * Math.Max(0, g.B);
            wts[(int)InterpretationKind.Favoritism] = neighborBlessed ? 2.2f : 0f;
            wts[(int)InterpretationKind.Omen] = harm < 0.2f ? 0.3f + 0.8f * anomaly : 0f;
        }
        else if (harm < -0.1f)
        {
            wts[(int)InterpretationKind.Gift] = 1.0f + 0.5f * g.B + (recentPrayer ? 0.5f : 0f);
            wts[(int)InterpretationKind.Bargain] = 1.5f * g.Belief[Doctrine.Ritual] * (recentCrisis ? 1f : 0.3f) + (recentPrayer ? 1.2f : 0f);
            wts[(int)InterpretationKind.Omen] = 0.3f + 0.5f * anomaly;
        }
        else
        {
            // Слабый отголосок события: беду не назовут даром, удачу — карой.
            bool badType = e.Type is EventType.NaturalDrought or EventType.Drought or EventType.MeteorImpact or EventType.Earthquake
                or EventType.Plague or EventType.Famine or EventType.LightningStrike or EventType.MegafaunaGone or EventType.ColdSnap
                or EventType.Flood or EventType.CursedGround or EventType.DynastyCursed or EventType.Threat
                or EventType.Eruption or EventType.Refugees || e.Type == EventType.Terraformed && e.B < 0;
            bool goodType = e.Type is EventType.Rain or EventType.MiracleHarvest or EventType.Bounty or EventType.DroughtEnded or EventType.Healing
                or EventType.Blessing or EventType.HerdsReturn or EventType.PrayerAnswered or EventType.PeaceImposed or EventType.DynastyBlessed
                or EventType.RiverBorn || e.Type == EventType.Terraformed && e.B > 0;
            wts[(int)InterpretationKind.Omen] = 1f;
            wts[(int)InterpretationKind.Gift] = badType ? 0f : 0.3f + 0.3f * Math.Max(0, g.B);
            wts[(int)InterpretationKind.Wrath] = goodType ? 0f : 0.3f + 0.3f * Math.Max(0, -g.B);
        }
        int best = 1; float bv = float.MinValue;
        for (int k = 1; k < 8; k++)
        {
            if (wts[k] <= 0) continue;
            float v = MathF.Log(wts[k]) + 0.6f * rng.NextGumbel();
            if (v > bv) { bv = v; best = k; }
        }
        return (InterpretationKind)best;
    }

    private bool NeighborsProsper(Group g)
    {
        int n = 0, ok = 0;
        foreach (var id in g.Neighbors) { var o = World.Groups[id]; if (!o.Alive) continue; n++; if (o.FoodRatio > 1.1f) ok++; }
        return n > 0 && ok * 2 > n && g.FoodRatio < 0.9f;
    }

    private static void Nudge(float[] b, int i, float d) => b[i] = Math.Clamp(b[i] + d, 0f, 1f);

    private void ApplyInterpretation(Group g, in WorldEvent e, InterpretationKind kind, float S, float harm, float anomaly)
    {
        var b = g.Belief;
        float c = g.Culture[CultureTrait.Piety];
        switch (kind)
        {
            case InterpretationKind.Punishment:
                Nudge(b, Doctrine.Moralism, 0.06f * S); Nudge(b, Doctrine.Ritual, 0.05f * S);
                // Кара без вины ощущается несправедливой.
                if (g.Guilt < 0.1f) g.G += 0.06f * S;
                g.B -= 0.1f * S; g.Guilt = 0; g.D += 0.05f * S;
                break;
            case InterpretationKind.Wrath:
                Nudge(b, Doctrine.Wrathfulness, 0.08f * S); Nudge(b, Doctrine.Benevolence, -0.05f * S);
                // Обида: беда пришла к нам, а соседи сыты; или нас разбили в бою.
                if (e.Type == EventType.Battle || NeighborsProsper(g)) g.G += 0.12f * S;
                g.B -= 0.2f * S; g.D += 0.08f * S;
                break;
            case InterpretationKind.Trial:
                g.Culture[CultureTrait.Piety] = Math.Min(1f, c + 0.03f * S); g.D += 0.03f * S; Nudge(b, Doctrine.Ritual, 0.02f * S);
                break;
            case InterpretationKind.Gift:
                Nudge(b, Doctrine.Benevolence, 0.08f * S); g.B += 0.2f * S; g.D -= 0.2f * S; g.G -= 0.05f * S;
                break;
            case InterpretationKind.Bargain:
                Nudge(b, Doctrine.Ritual, 0.06f * S); g.B += 0.1f * S; g.D -= 0.1f * S;
                break;
            case InterpretationKind.Omen:
                g.P += 0.1f * S;
                break;
            case InterpretationKind.Favoritism:
                g.G += 0.25f * S; g.B -= 0.1f * S; Nudge(b, Doctrine.Proselytism, -0.05f * S);
                break;
        }
        if (kind != InterpretationKind.Natural)
        {
            g.P += 0.1f * S;
            // Явное чудо подрывает натурализм: «так природа не умеет».
            if (e.ByCreator && anomaly >= 0.6f) g.N = Math.Max(0f, g.N - 0.05f * S);
            // Вмешательства одного источника сдвигают к единобожию, разные природные — к пантеону.
            Nudge(b, Doctrine.Monotheism, e.ByCreator ? 0.03f * S : -0.01f * S);
        }
        g.B = Math.Clamp(g.B, -1f, 1f);
        g.P = Math.Clamp(g.P, 0f, 1f);
        g.G = Math.Clamp(g.G, 0f, 1f);
        g.D = Math.Clamp(g.D, 0f, 1f);
    }

    // ---------- Веры, культы, святыни ----------

    public Person NewPerson(Group g, PersonRole role)
    {
        var w = World;
        var rng = w.Rng[StreamId.Naming];
        var person = new Person
        {
            Id = w.People.Count, Role = role, Group = g.Id, Born = w.Year,
            Name = w.PersonName(w.LangOf(g), rng, out var meaning),
            Meaning = meaning,
            Ambition = Math.Clamp(0.5f * g.Culture[CultureTrait.Hierarchy] + 0.5f * rng.NextFloat(), 0, 1),
            Piety = Math.Clamp(0.5f * g.Piety + 0.5f * rng.NextFloat(), 0, 1),
            Cruelty = Math.Clamp(0.5f * g.Culture[CultureTrait.Aggression] + 0.5f * rng.NextFloat(), 0, 1),
            Wisdom = Math.Clamp(0.5f * g.Knowledge + 0.5f * rng.NextFloat(), 0, 1),
        };
        w.People.Add(person);
        return person;
    }

    private DeityForm DeriveForm(Group g)
    {
        float mono = g.Belief[Doctrine.Monotheism];
        int creatorEvents = 0;
        float anomalySum = 0;
        for (int m = 0; m < g.MemoryCount; m++)
        {
            if (g.Memory[m].ByCreator) creatorEvents++;
            anomalySum += g.Memory[m].Anomaly;
        }
        float meanAnomaly = g.MemoryCount > 0 ? anomalySum / g.MemoryCount : 0;
        if (g.N > 0.45f && meanAnomaly < 0.3f) return DeityForm.Nature;
        if (mono > 0.5f || creatorEvents >= 2) return DeityForm.OneCreator;
        if (g.Culture[CultureTrait.Collectivism] > 0.6f && g.P < 0.35f) return DeityForm.Ancestors;
        return mono < 0.4f ? DeityForm.Pantheon : DeityForm.OneCreator;
    }

    private Name CreatorNameFor(Group g, DeityForm form, out EpithetKind? kind)
    {
        var L = World.LangOf(g);
        kind = null;
        if (g.Epithets.Count > 0)
        {
            var ep = g.Epithets[^1];
            kind = ep.Kind;
            return ep.Name;
        }
        return form switch
        {
            DeityForm.Ancestors => L.MakeName(L.Compound(Concept.Old, Concept.Father), "Древний отец"),
            DeityForm.Nature => L.MakeName(L.Compound(Concept.Mother, Concept.Earth), "Мать-земля"),
            DeityForm.Pantheon => L.MakeName(L.Compound(Concept.Sky, Concept.Spirit), "Духи неба"),
            _ => L.MakeName(L.Compound(Concept.Great, Concept.Spirit), "Великий дух"),
        };
    }

    public Religion FoundReligion(Group g, int cause, int parent, Rng rng, bool fromCult = false)
    {
        var w = World;
        var L = w.LangOf(g);
        var form = DeriveForm(g);
        var r = new Religion { Id = w.Religions.Count, FounderGroup = g.Id, Parent = parent, Founded = w.Year, Form = form };
        Array.Copy(g.Belief, r.Doctrine, r.Doctrine.Length);
        r.CreatorName = CreatorNameFor(g, form, out var ek);
        if (ek.HasValue) r.Epithets.Add(ek.Value);
        var (ca, _, _) = ek.HasValue ? EpithetParts(ek.Value) : (Concept.Spirit, Concept.God, "");
        r.Name = World.Unique(k => k == 0 ? L.MakeName(L.Compound(Concept.Path, ca), $"путь „{r.CreatorName.Gloss}“")
            : L.MakeName(L.Compound(Concept.Path, ca, World.Distinguishers[(k - 1) % 16]), $"путь „{r.CreatorName.Gloss}“ ({World.DistinguisherGloss[(k - 1) % 16]})"), ReligionNameTaken);
        w.Religions.Add(r);
        int old = g.ReligionId;
        g.ReligionId = r.Id;
        w.Events.Publish(EventType.ReligionFounded, g.Cell, a: r.Id, b: g.Id, c: old, cause: cause);
        w.Events.Publish(EventType.CreatorNamed, g.Cell, a: r.Id, b: g.Id);
        return r;
    }

    private static readonly Dictionary<string, (int dim, float delta)[]> MessageShifts = new()
    {
        ["peace"] = new[] { (Doctrine.Violence, -0.4f), (Doctrine.Proselytism, 0.2f) },
        ["war"] = new[] { (Doctrine.Violence, 0.4f), (Doctrine.Wrathfulness, 0.15f) },
        ["love"] = new[] { (Doctrine.Benevolence, 0.4f), (Doctrine.Violence, -0.15f) },
        ["wrath"] = new[] { (Doctrine.Wrathfulness, 0.4f), (Doctrine.Moralism, 0.2f) },
        ["knowledge"] = new[] { (Doctrine.Knowledge, 0.45f), (Doctrine.Ritual, -0.15f) },
        ["obedience"] = new[] { (Doctrine.Moralism, 0.3f), (Doctrine.Ritual, 0.3f) },
        ["freedom"] = new[] { (Doctrine.Monotheism, -0.2f), (Doctrine.Ritual, -0.25f), (Doctrine.Knowledge, 0.2f) },
    };

    private void FoundVisionCult(Group g, in WorldEvent e, Rng rng, bool doubted)
    {
        var w = World;
        Person prophet = e.B >= 0 ? w.People[e.B] : NewPerson(g, PersonRole.Prophet);
        w.Events.Publish(EventType.ProphetBorn, g.Cell, a: prophet.Id, b: g.Id, c: doubted ? 1 : 0, flags: EventFlags.ByCreator, cause: e.Id);
        FoundCult(g, e.Id, prophet, rng);
        // Учёная группа сочла пророка безумцем: культ рождается слабым.
        if (doubted && g.CultId >= 0) { w.Religions[g.CultId].Zeal = 0.7f; g.CultShare = 0.03f; }
    }

    private void FoundCult(Group g, int cause, Person? prophet, Rng rng)
    {
        var w = World;
        var L = w.LangOf(g);
        bool byVision = prophet != null;
        prophet ??= NewPerson(g, PersonRole.Prophet);
        if (!byVision) w.Events.Publish(EventType.ProphetBorn, g.Cell, a: prophet.Id, b: g.Id, cause: cause);
        var r = new Religion
        {
            Id = w.Religions.Count, FounderGroup = g.Id, Parent = g.ReligionId, Founded = w.Year, IsCult = true,
            Prophet = prophet.Id, Form = DeriveForm(g), Zeal = 1.3f, Message = prophet.Message,
        };
        Array.Copy(g.Belief, r.Doctrine, r.Doctrine.Length);
        if (!string.IsNullOrEmpty(prophet.Message) && MessageShifts.TryGetValue(prophet.Message, out var shifts))
            foreach (var (dim, delta) in shifts) r.Doctrine[dim] = Math.Clamp(r.Doctrine[dim] + delta, 0, 1);
        else
        {
            // Культ без видения — «радикализация» самого сильного недавнего сдвига.
            for (int i = 0; i < r.Doctrine.Length; i++)
                r.Doctrine[i] = Math.Clamp(r.Doctrine[i] + (g.Belief[i] - (g.ReligionId >= 0 ? w.Religions[g.ReligionId].Doctrine[i] : 0.5f)) * 1.0f, 0, 1);
        }
        if (byVision) r.Form = r.Form == DeityForm.Nature ? DeityForm.OneCreator : r.Form;
        r.CreatorName = byVision ? EpithetName(L, EpithetKind.DreamSpeaker) : CreatorNameFor(g, r.Form, out _);
        if (byVision) r.Epithets.Add(EpithetKind.DreamSpeaker);
        r.Name = World.Unique(k => k == 0 ? L.MakeName(L.WithSuffix(L.Word(prophet.Meaning), Concept.SuffixPeople), $"последователи {prophet.Name.Native}")
            : L.MakeName(L.WithSuffix(L.Compound(prophet.Meaning, World.Distinguishers[(k - 1) % 16]), Concept.SuffixPeople), $"последователи {prophet.Name.Native}"), ReligionNameTaken);
        w.Religions.Add(r);
        if (g.ReligionId < 0)
        {
            g.ReligionId = r.Id;
            r.IsCult = false;
            w.Events.Publish(EventType.ReligionFounded, g.Cell, a: r.Id, b: g.Id, c: -1, cause: cause);
            w.Events.Publish(EventType.CreatorNamed, g.Cell, a: r.Id, b: g.Id);
        }
        else
        {
            g.CultId = r.Id;
            g.CultShare = 0.08f;
            w.Events.Publish(EventType.CultFounded, g.Cell, a: r.Id, b: g.Id, c: prophet.Id, cause: cause);
        }
        // Апокалиптическое пророчество (идея 12): гневные и отчаявшиеся культы назначают срок конца света.
        if ((r.Doctrine[Doctrine.Wrathfulness] > 0.45f || g.D > 0.3f || r.Message == "wrath") && rng.Chance(0.5))
        {
            r.ProphecyYear = w.Year + rng.Range(15, 60);
            w.Events.Publish(EventType.ProphecyMade, g.Cell, a: r.Id, b: (int)r.ProphecyYear, magnitude: 0.5f, cause: cause);
        }
                // Мученичество: иерархичная группа казнит пророка, чьё учение резко расходится с её убеждениями (CRED, Henrich 2009).
        float clash = Doctrine.Distance(r.Doctrine, g.Belief);
        if (g.Culture[CultureTrait.Hierarchy] > 0.45f && clash > 0.12f && rng.Chance(0.25 + clash))
        {
            prophet.Martyr = true;
            prophet.Died = w.Year + 1 + rng.NextInt(8);
            r.Cred += 1.5f;
            w.Events.Publish(EventType.Martyrdom, g.Cell, a: prophet.Id, b: g.Id, c: r.Id, cause: cause);
        }
    }

    private bool ReligionNameTaken(string native)
    {
        foreach (var r in World.Religions) if (r.Name.Native == native) return true;
        return false;
    }

    private void TryFoundSite(Group g, in WorldEvent e)
    {
        var w = World;
        var r = w.Religions[g.ReligionId];
        foreach (var sid in r.Sites)
            if (w.Planet.EcoDistance(w.Sites[sid].Cell, e.Cell) < 3f) return;
        var L = w.LangOf(g);
        var concept = EventConcept(e.Type);
        var name = World.Unique(k => k == 0
                ? L.MakeName(L.WithSuffix(L.Compound(Concept.Holy, concept), Concept.SuffixPlace), $"святое место: {Concepts.Russian[(int)concept]}")
                : L.MakeName(L.WithSuffix(L.Compound(Concept.Holy, concept, World.Distinguishers[(k - 1) % 16]), Concept.SuffixPlace), $"святое место: {Concepts.Russian[(int)concept]} ({World.DistinguisherGloss[(k - 1) % 16]})"),
            n => { foreach (var x in w.Sites) if (x.Name.Native == n) return true; return false; });
        var site = new SacredSite { Id = w.Sites.Count, Cell = e.Cell, Name = name, Religion = r.Id, Founded = w.Year, Origin = e.Type };
        w.Sites.Add(site);
        r.Sites.Add(site.Id);
        r.Cred += 0.3f;
        w.Events.Publish(EventType.SacredSiteFounded, e.Cell, a: site.Id, b: r.Id, c: g.Id, cause: e.Id);
    }

    // ---------- Пророчества (Festinger, Riecken & Schachter 1956, «When Prophecy Fails») ----------

    private void Prophecies(Rng rng)
    {
        var w = World;
        foreach (var r in w.Religions)
        {
            if (!r.Alive || r.ProphecyYear != w.Year) continue;
            r.ProphecyYear = -1;
            // Исполнилось ли: сильная беда в землях верных за последние 3 года.
            int fulfilled = -1;
            var log = w.Events.Log;
            for (int i = log.Count - 1; i >= 0 && log[i].Year >= w.Year - 3; i--)
            {
                var e = log[i];
                if (e.Type is not (EventType.MeteorImpact or EventType.Drought or EventType.Earthquake or EventType.Plague or EventType.NaturalDrought)) continue;
                foreach (var g in w.Groups)
                    if (g.Alive && (g.ReligionId == r.Id || g.CultId == r.Id) && e.Cell >= 0 && w.Planet.EcoDistance(g.Cell, e.Cell) <= 6f) { fulfilled = e.Id; break; }
                if (fulfilled >= 0) break;
            }
            if (fulfilled >= 0)
            {
                r.Cred += 1f;
                r.Zeal = Math.Min(2f, r.Zeal + 0.3f);
                w.Events.Publish(EventType.ProphecyFulfilled, -1, a: r.Id, c: fulfilled, magnitude: 0.7f, cause: fulfilled);
            }
            else if (rng.Chance(0.5))
            {
                // Фестингер: после провала верные перетолковывают и проповедуют ещё усерднее.
                r.Zeal = Math.Min(2f, r.Zeal + 0.25f);
                r.Doctrine[Doctrine.Proselytism] = Math.Min(1f, r.Doctrine[Doctrine.Proselytism] + 0.2f);
                w.Events.Publish(EventType.ProphecyFailed, -1, a: r.Id, b: 1, magnitude: 0.6f);
            }
            else
            {
                foreach (var g in w.Groups) if (g.CultId == r.Id) g.CultShare *= 0.4f;
                r.Zeal *= 0.6f;
                w.Events.Publish(EventType.ProphecyFailed, -1, a: r.Id, b: 0, magnitude: 0.5f);
            }
        }
    }

    // ---------- Молитвы ----------

    private void Prayers()
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.ReligionId < 0 || g.FamineYears < 3 || g.PrayedThisFamine || g.Piety < 0.45f) continue;
            g.PrayedThisFamine = true;
            g.LastPrayer = w.Year;
            g.D = Math.Min(1f, g.D + 0.1f);
            w.Events.Publish(EventType.PrayerUnanswered, g.Cell, a: g.Id, b: g.FamineYears, magnitude: Demo.Weight(g.Pop));
        }
    }

    // ---------- Распространение (SIR + Бойд–Ричерсон) ----------

    private float Prestige(Group g) => MathF.Log10(Math.Max(10f, g.Pop)) * (0.5f + g.Knowledge) * (0.5f + Math.Min(1.5f, g.FoodRatio));

    private void SpreadReligions(Rng rng)
    {
        var w = World;
        var p = w.Planet;
        int count = w.Groups.Count;
        // 1. Внутри группы: s' = s + β·s(1−s)·compat − γ·s (+ конформизм).
        for (int gi = 0; gi < count; gi++)
        {
            var g = w.Groups[gi];
            if (!g.Alive || g.CultId < 0) continue;
            var r = w.Religions[g.CultId];
            if (!r.Alive) { g.CultId = -1; g.CultShare = 0; continue; }
            float compat = Compatibility(r.Doctrine, g.Belief);
            float beta = 0.12f * (1f + r.Cred) * r.Zeal;
            const float gamma = 0.03f;
            float s = g.CultShare;
            // Конформизм (Boyd & Richerson 1985): доля соседей, у которых эта вера — основная.
            int same = 0, tot = 0;
            foreach (var nId in g.Neighbors) { var o = w.Groups[nId]; if (!o.Alive) continue; tot++; if (o.ReligionId == r.Id) same++; }
            float q = tot > 0 ? same / (float)tot : 0f;
            float conform = 0.06f * g.Culture[CultureTrait.Collectivism] * (q - 0.5f) * (tot > 0 ? 1f : 0f);
            g.CultShare = SirStep(s, beta, gamma, compat, conform);
            if (g.CultShare > 0.5f) Convert(g, r);
            else if (g.CultShare < 0.01f) { g.CultId = -1; g.CultShare = 0; }
        }
        // 2. Между группами: засев культа или веры у соседей (контакт × престиж × прозелитизм).
        for (int gi = 0; gi < count; gi++)
        {
            var g = w.Groups[gi];
            if (!g.Alive) continue;
            int rid = g.CultId >= 0 && g.CultShare > 0.25f ? g.CultId : g.ReligionId;
            if (rid < 0) continue;
            var r = w.Religions[rid];
            float pres = Prestige(g);
            float pros = r.Doctrine[Doctrine.Proselytism];
            for (int k = 0; k < g.Neighbors.Count; k++)
            {
                var o = w.Groups[g.Neighbors[k]];
                if (!o.Alive || o.ReligionId == rid || o.CultId >= 0) continue;
                float d = p.EcoDistance(g.Cell, o.Cell);
                float contact = w.Intelligibility(g.LanguageId, o.LanguageId) * (g.TradePartners.Contains(o.Id) ? 2f : 1f) / (1f + 0.3f * d);
                float prob = 0.03f * contact * r.Zeal * (0.3f + pros) * (1f + r.Cred) * Math.Clamp(pres / Math.Max(1f, Prestige(o)), 0.3f, 2f);
                if (o.ReligionId < 0) prob *= 3f;
                if (rng.NextFloat() < prob)
                {
                    o.CultId = rid;
                    o.CultShare = 0.05f;
                }
            }
        }
    }

    private void Convert(Group g, Religion r)
    {
        var w = World;
        int old = g.ReligionId;
        g.ReligionId = r.Id;
        g.CultId = -1;
        g.CultShare = 0;
        // Синкретизм: 20 % старых убеждений остаются (faith-creator.md, §6).
        for (int i = 0; i < g.Belief.Length; i++) g.Belief[i] += 0.8f * (r.Doctrine[i] - g.Belief[i]);
        if (r.IsCult) r.IsCult = false;
        r.Converted = true;
        w.Events.Publish(EventType.Conversion, g.Cell, a: g.Id, b: r.Id, c: old, magnitude: Demo.Weight(g.Pop));
    }

    /// <summary>
    /// Ограниченное доверие (Deffuant et al. 2000; Hegselmann &amp; Krause 2002): убеждения сближаются,
    /// только если уже близки. Плюс культурный дрейф.
    /// </summary>
    private void BoundedConfidence(Rng rng)
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            for (int i = 0; i < g.Belief.Length; i++) g.Belief[i] = Math.Clamp(g.Belief[i] + rng.NextGaussian() * 0.01f, 0, 1);
            if (g.Neighbors.Count == 0) continue;
            var o = w.Groups[g.Neighbors[rng.NextInt(g.Neighbors.Count)]];
            if (!o.Alive || o.ReligionId != g.ReligionId) continue;
            BoundedConfidenceStep(g.Belief, o.Belief, Epsilon, 0.05f);
            if (g.ReligionId >= 0)
            {
                var r = w.Religions[g.ReligionId];
                if (Doctrine.Distance(g.Belief, r.Doctrine) < Epsilon)
                    for (int i = 0; i < g.Belief.Length; i++) g.Belief[i] += 0.01f * (r.Doctrine[i] - g.Belief[i]);
            }
        }
    }

    private void UpdateReligionStats()
    {
        var w = World;
        foreach (var r in w.Religions) { r.Adherents = 0; r.AdherentPop = 0; }
        var sums = new float[w.Religions.Count * Doctrine.Count];
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.ReligionId < 0) continue;
            var r = w.Religions[g.ReligionId];
            r.Adherents++;
            r.AdherentPop += g.Pop;
            for (int i = 0; i < Doctrine.Count; i++) sums[r.Id * Doctrine.Count + i] += g.Belief[i] * g.Pop;
        }
        foreach (var r in w.Religions)
        {
            r.MaxAdherents = Math.Max(r.MaxAdherents, r.Adherents);
            if (r.AdherentPop <= 0) continue;
            // Доктрина медленно следует за убеждениями адептов.
            for (int i = 0; i < Doctrine.Count; i++)
                r.Doctrine[i] += 0.3f * (sums[r.Id * Doctrine.Count + i] / r.AdherentPop - r.Doctrine[i]);
            r.Zeal = Math.Max(0.6f, r.Zeal * 0.99f);
        }
    }

    /// <summary>Расколы: 2-means по убеждениям адептов (faith-creator.md, §6).</summary>
    private void Schisms(Rng rng)
    {
        var w = World;
        int rc = w.Religions.Count;
        for (int ri = 0; ri < rc; ri++)
        {
            var r = w.Religions[ri];
            if (!r.Alive || r.Adherents < 3) continue;
            _tmpGroups.Clear();
            foreach (var g in w.Groups) if (g.Alive && g.ReligionId == r.Id) _tmpGroups.Add(g.Id);
            // Начальные центры: самая далёкая от первой и самая далёкая от неё.
            int a = Farthest(_tmpGroups[0]);
            int b = Farthest(a);
            var ca = (float[])w.Groups[a].Belief.Clone();
            var cb = (float[])w.Groups[b].Belief.Clone();
            var inA = new bool[_tmpGroups.Count];
            for (int it = 0; it < 4; it++)
            {
                var sa = new float[Doctrine.Count]; var sb = new float[Doctrine.Count];
                float na = 0, nb = 0;
                for (int k = 0; k < _tmpGroups.Count; k++)
                {
                    var g = w.Groups[_tmpGroups[k]];
                    inA[k] = Doctrine.Distance(g.Belief, ca) <= Doctrine.Distance(g.Belief, cb);
                    var s = inA[k] ? sa : sb;
                    for (int i = 0; i < Doctrine.Count; i++) s[i] += g.Belief[i] * g.Pop;
                    if (inA[k]) na += g.Pop; else nb += g.Pop;
                }
                if (na <= 0 || nb <= 0) break;
                for (int i = 0; i < Doctrine.Count; i++) { ca[i] = sa[i] / na; cb[i] = sb[i] / nb; }
            }
            float popA = 0, popB = 0;
            for (int k = 0; k < _tmpGroups.Count; k++) { var g = w.Groups[_tmpGroups[k]]; if (inA[k]) popA += g.Pop; else popB += g.Pop; }
            if (popA <= 0 || popB <= 0) continue;
            bool crisis = false;
            foreach (var gid in _tmpGroups) if (w.Year - w.Groups[gid].LastCrisis <= 10) { crisis = true; break; }
            float threshold = SchismDistance * (crisis ? 0.8f : 1f);
            float dist = Doctrine.MaxDiff(ca, cb);
            if (DebugSchism) Console.Error.WriteLine($"y{w.Year} rel {r.Id} n={_tmpGroups.Count} dist={dist:F3} thr={threshold:F3} small={Math.Min(popA, popB) / (popA + popB):F2}");
            bool smallerIsA = popA < popB;
            float smallPop = Math.Min(popA, popB);
            if (dist < threshold || smallPop < 0.15f * (popA + popB)) continue;

            // Основатель — крупнейшая группа меньшей части.
            Group? founder = null;
            for (int k = 0; k < _tmpGroups.Count; k++)
            {
                if (inA[k] != smallerIsA) continue;
                var g = w.Groups[_tmpGroups[k]];
                if (founder == null || g.Pop > founder.Pop) founder = g;
            }
            if (founder == null) continue;
            var L = w.LangOf(founder);
            var nr = new Religion { Id = w.Religions.Count, FounderGroup = founder.Id, Parent = r.Id, Founded = w.Year, Form = r.Form };
            Array.Copy(smallerIsA ? ca : cb, nr.Doctrine, Doctrine.Count);
            // Имя Создателя у раскольников — их собственный эпитет, которого нет у родительской веры.
            EpithetKind? ek = null;
            for (int m = founder.Epithets.Count - 1; m >= 0; m--)
                if (!r.Epithets.Contains(founder.Epithets[m].Kind)) { ek = founder.Epithets[m].Kind; break; }
            if (ek.HasValue) { nr.CreatorName = EpithetName(L, ek.Value); nr.Epithets.Add(ek.Value); }
            else { nr.CreatorName = EpithetName(L, EpithetKind.Hidden); nr.Epithets.Add(EpithetKind.Hidden); }
            Concept adj = nr.Doctrine[Doctrine.Moralism] > r.Doctrine[Doctrine.Moralism] + 0.05f ? Concept.Holy
                : nr.Doctrine[Doctrine.Knowledge] > r.Doctrine[Doctrine.Knowledge] + 0.05f ? Concept.Clear
                : nr.Doctrine[Doctrine.Violence] > r.Doctrine[Doctrine.Violence] + 0.05f ? Concept.Strong : Concept.New;
            string adjRu = adj switch { Concept.Holy => "Святой", Concept.Clear => "Ясный", Concept.Strong => "Сильный", _ => "Новый" };
            nr.Name = World.Unique(k => k == 0 ? L.MakeName(L.Compound(adj, Concept.Path), $"{adjRu} путь")
                : L.MakeName(L.Compound(adj, Concept.Path, World.Distinguishers[(k - 1) % 16]), $"{adjRu} путь ({World.DistinguisherGloss[(k - 1) % 16]})"), ReligionNameTaken);
            w.Religions.Add(nr);
            for (int k = 0; k < _tmpGroups.Count; k++)
            {
                if (inA[k] != smallerIsA) continue;
                var g = w.Groups[_tmpGroups[k]];
                g.ReligionId = nr.Id;
                if (g.CultId == nr.Id) { g.CultId = -1; g.CultShare = 0; }
            }
            w.Events.Publish(EventType.Schism, founder.Cell, a: nr.Id, b: r.Id, c: founder.Id, magnitude: Math.Min(1f, dist * 3),
                flags: crisis ? EventFlags.Crisis : EventFlags.None);
        }
    }

    private int Farthest(int from)
    {
        var w = World;
        int best = from; float bd = -1;
        foreach (var gid in _tmpGroups)
        {
            float d = Doctrine.Distance(w.Groups[gid].Belief, w.Groups[from].Belief);
            if (d > bd) { bd = d; best = gid; }
        }
        return best;
    }

    // ---------- Отношение к Создателю ----------

    /// <summary>Классификация позиции из латентных переменных (core-loop.md, «Шкала отношения»).</summary>
    public static Attitude Classify(Group g)
    {
        if (g.B < -0.5f && g.D > 0.6f) return Attitude.Nihilism;
        if (g.G > 0.45f && g.B < 0f && g.P > 0.25f) return Attitude.Rebellion;
        if (g.N > 0.65f && g.P < 0.45f) return Attitude.Denial;
        if (g.P < 0.12f) return Attitude.Indifference;
        if (g.B > 0.3f) return Attitude.Love;
        if (g.B < -0.15f) return Attitude.Fear;
        return Attitude.Bargain;
    }

    private void Attitudes(Rng rng)
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            // Натурализм растёт со знанием и любознательностью.
            g.N = Math.Clamp(g.N + 0.0015f * g.Knowledge * (0.5f + g.Culture[CultureTrait.KnowledgeLove]), 0, Math.Max(g.N, g.Knowledge));
            g.N *= 0.99f; // без новых объяснений натурализм медленно уступает традиции
            var att = Classify(g);
            if (!g.AttitudeInit) { g.Attitude = att; g.AttitudeInit = true; continue; }
            if (att == g.Attitude) continue;
            var old = g.Attitude;
            g.Attitude = att;
            w.Events.Publish(EventType.AttitudeShift, g.Cell, a: g.Id, b: (int)att, c: (int)old, magnitude: Math.Min(1f, Demo.Weight(g.Pop) + 0.1f));
            // Движения: мятеж, отрицание, нигилизм порождают свои учения.
            if (att is Attitude.Rebellion or Attitude.Denial or Attitude.Nihilism && g.CultId < 0 && rng.Chance(0.5))
            {
                // Учение, которое уже есть у соседей, перенимают, а не изобретают заново (civilizations.md, §5).
                int existing = NearbyMovement(g, att);
                if (existing < 0)
                {
                    // Угасшее учение возрождается у родственного народа — то же имя и та же доктрина, а не новое «изобретение».
                    existing = DormantMovement(g, att);
                    if (existing >= 0) { w.Religions[existing].Alive = true; w.Religions[existing].Faded = -1; }
                }
                if (existing >= 0)
                {
                    g.CultId = existing;
                    g.CultShare = 0.15f;
                    w.Events.Publish(EventType.Conversion, g.Cell, a: g.Id, b: existing, c: -1, magnitude: 0.3f);
                }
                else FoundMovement(g, att);
            }
        }
    }

    /// <summary>Живое движение того же рода (мятеж, «пустое небо», конец) у народа в радиусе 14 клеток: идеи ходят и через границы языков.</summary>
    private int NearbyMovement(Group g, Attitude att)
    {
        var w = World;
        int best = -1; float bd = 14.5f;
        foreach (var o in w.Groups)
        {
            if (!o.Alive || o.Id == g.Id) continue;
            foreach (int rid in new[] { o.CultId, o.ReligionId })
            {
                if (rid < 0) continue;
                var r = w.Religions[rid];
                if (!r.Alive || !MovementKind(r, att)) continue;
                float d = w.Planet.EcoDistance(o.Cell, g.Cell);
                if (d < bd) { bd = d; best = rid; }
            }
        }
        return best;
    }

    /// <summary>Самое позднее угасшее движение того же рода, основанное народом с понятным языком (≥ 0,3).</summary>
    private int DormantMovement(Group g, Attitude att)
    {
        var w = World;
        for (int i = w.Religions.Count - 1; i >= 0; i--)
        {
            var r = w.Religions[i];
            if (r.Alive || !MovementKind(r, att) || r.FounderGroup < 0) continue;
            if (w.Intelligibility(w.Groups[r.FounderGroup].LanguageId, g.LanguageId) >= 0.3f) return r.Id;
        }
        return -1;
    }

    private static bool MovementKind(Religion r, Attitude att) => att switch
    {
        Attitude.Rebellion => r.Rebel,
        Attitude.Denial => !r.Rebel && r.Form == DeityForm.None && r.CreatorName.Gloss == "Пустое небо",
        _ => !r.Rebel && r.Form == DeityForm.None && r.CreatorName.Gloss == "Тьма конца",
    };

    private void FoundMovement(Group g, Attitude att)
    {
        var w = World;
        var L = w.LangOf(g);
        var leader = NewPerson(g, PersonRole.Sage);
        var r = new Religion { Id = w.Religions.Count, FounderGroup = g.Id, Parent = g.ReligionId, Founded = w.Year, IsCult = true, Prophet = leader.Id, Zeal = 1.2f };
        Array.Copy(g.Belief, r.Doctrine, r.Doctrine.Length);
        switch (att)
        {
            case Attitude.Rebellion:
                r.Rebel = true;
                r.Form = DeityForm.OneCreator;
                r.Doctrine[Doctrine.Benevolence] = 0.05f;
                r.Doctrine[Doctrine.Knowledge] = Math.Max(r.Doctrine[Doctrine.Knowledge], 0.6f);
                r.CreatorName = L.MakeName(L.Compound(Concept.Bad, Concept.Chief), "Злой владыка");
                r.Name = World.Unique(k => L.MakeName(k == 0 ? L.Compound(Concept.Free, Concept.People) : L.Compound(Concept.Free, Concept.People, World.Distinguishers[(k - 1) % 16]), "Свободные"), ReligionNameTaken);
                r.Message = "freedom";
                break;
            case Attitude.Denial:
                r.Form = DeityForm.None;
                r.Doctrine[Doctrine.Knowledge] = Math.Max(r.Doctrine[Doctrine.Knowledge], 0.8f);
                r.Doctrine[Doctrine.Ritual] = 0.1f;
                r.CreatorName = L.MakeName(L.Compound(Concept.Silent, Concept.Sky), "Пустое небо");
                r.Name = World.Unique(k => L.MakeName(k == 0 ? L.Compound(Concept.Clear, Concept.Mind) : L.Compound(Concept.Clear, Concept.Mind, World.Distinguishers[(k - 1) % 16]), "Ясный разум"), ReligionNameTaken);
                break;
            default:
                r.Form = DeityForm.None;
                r.Doctrine[Doctrine.Benevolence] = 0f;
                r.Doctrine[Doctrine.Violence] = Math.Max(r.Doctrine[Doctrine.Violence], 0.6f);
                r.CreatorName = L.MakeName(L.Compound(Concept.End, Concept.Dark), "Тьма конца");
                r.Name = World.Unique(k => L.MakeName(k == 0 ? L.Compound(Concept.End, Concept.Path) : L.Compound(Concept.End, Concept.Path, World.Distinguishers[(k - 1) % 16]), "Путь конца"), ReligionNameTaken);
                break;
        }
        w.Religions.Add(r);
        g.CultId = r.Id;
        g.CultShare = 0.1f;
        w.Events.Publish(EventType.CultFounded, g.Cell, a: r.Id, b: g.Id, c: leader.Id, magnitude: 0.6f);
    }

    private void FadeCults()
    {
        var w = World;
        var holders = new int[w.Religions.Count];
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            if (g.ReligionId >= 0) holders[g.ReligionId]++;
            if (g.CultId >= 0) holders[g.CultId]++;
        }
        foreach (var r in w.Religions)
        {
            if (!r.Alive || holders[r.Id] > 0) continue;
            r.Alive = false;
            r.Faded = w.Year;
            w.Events.Publish(EventType.CultFaded, -1, a: r.Id, magnitude: r.Converted ? 0.6f : 0.25f);
        }
    }

    // ---------- «Аномалия замечена» и «Молчащий» ----------

    private void Anomalies()
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.AnomalyNoticed || g.Knowledge < 0.55f || g.AnomalyLog.Count < 3) continue;
            bool cluster = false;
            for (int i = 0; i < g.AnomalyLog.Count && !cluster; i++)
                for (int j = i + 1; j < g.AnomalyLog.Count; j++)
                    if (w.Planet.EcoDistance(g.AnomalyLog[i].cell, g.AnomalyLog[j].cell) <= 3f) { cluster = true; break; }
            if (!cluster && g.AnomalyLog.Count < 5) continue;
            g.AnomalyNoticed = true;
            g.N = Math.Min(1f, g.N + 0.1f);
            g.P = Math.Min(1f, g.P + 0.1f);
            if (w.FirstAnomalyGroup < 0) w.FirstAnomalyGroup = g.Id;
            w.Events.Publish(EventType.AnomalyNoticed, g.Cell, a: g.Id, b: g.AnomalyLog.Count, c: cluster ? 1 : 0, magnitude: 1f);
        }
    }

    private void SilentCreator()
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.ReligionId < 0 || g.Epithets.Count >= 4) continue;
            long since = g.LastCreatorSeen >= 0 ? w.Year - g.LastCreatorSeen : w.Year;
            if (since < 300 || g.Epithets.Exists(e => e.Kind == EpithetKind.Silent)) continue;
            g.Epithets.Add(new Epithet { Kind = EpithetKind.Silent, Name = EpithetName(w.LangOf(g), EpithetKind.Silent), Year = w.Year });
            g.P *= 0.7f;
            w.Events.Publish(EventType.EpithetGained, g.Cell, a: g.Id, b: (int)EpithetKind.Silent, magnitude: 0.5f);
        }
    }

    // ---------- Вера как ресурс Создателя ----------

    public static float AttitudeWeight(Attitude a) => a switch
    {
        Attitude.Love => 1f, Attitude.Fear => 0.7f, Attitude.Bargain => 0.5f, Attitude.Indifference => 0.05f,
        Attitude.Denial => 0f, Attitude.Rebellion => -0.4f, _ => -0.6f,
    };

    public static float CreatorFocus(DeityForm f) => f switch
    {
        DeityForm.OneCreator => 1f, DeityForm.Pantheon => 0.6f, DeityForm.Ancestors => 0.3f, _ => 0f,
    };

    private void CreatorIncome()
    {
        var w = World;
        float income = 0;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            float focus = g.ReligionId >= 0 ? CreatorFocus(w.Religions[g.ReligionId].Form) : 0.2f;
            float wgt = AttitudeWeight(g.Attitude);
            if (g.ReligionId >= 0 && w.Religions[g.ReligionId].Rebel) wgt = Math.Min(wgt, -0.4f);
            if (wgt < 0) focus = Math.Max(focus, 0.5f);
            // Вера даёт силу ∝ √численности: держава в миллионы не в тысячу раз сильнее племени в божественном (faith-creator.md, §8).
            income += MathF.Sqrt(g.Pop / (1000f * Demo.Ref)) * g.Piety * wgt * focus * 0.1f;
        }
        w.EnergyIncome = income;
        w.EnergyIncomePeak = Math.Max(w.EnergyIncomePeak, income);
        w.CreatorEnergy = Math.Max(0f, w.CreatorEnergy + income);
    }
}
