using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;

namespace Mirozdanie.Core.Culture;

public enum Subsistence : byte { Foraging, Fishing, Herding, Farming }

/// <summary>Толкования события (docs/design/faith-creator.md, §3).</summary>
public enum InterpretationKind : byte { Natural, Punishment, Wrath, Trial, Gift, Bargain, Omen, Favoritism }

/// <summary>Позиции по отношению к Создателю (docs/design/core-loop.md, «Шкала отношения»).</summary>
public enum Attitude : byte { Love, Fear, Bargain, Indifference, Denial, Rebellion, Nihilism }

public static class CultureTrait
{
    public const int Aggression = 0, Collectivism = 1, Hierarchy = 2, Openness = 3, KnowledgeLove = 4, Piety = 5, Count = 6;
}

/// <summary>Измерения доктрины и убеждений (faith-creator.md, §4).</summary>
public static class Doctrine
{
    public const int Benevolence = 0, Wrathfulness = 1, Moralism = 2, Ritual = 3, Monotheism = 4, Knowledge = 5, Violence = 6, Proselytism = 7, Count = 8;

    /// <summary>Наибольшее расхождение по одному пункту доктрины (расколы чаще всего идут по одному спорному вопросу).</summary>
    public static float MaxDiff(float[] a, float[] b)
    {
        float m = 0;
        for (int i = 0; i < Count; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }

    public static float Distance(float[] a, float[] b)
    {
        float s = 0;
        for (int i = 0; i < Count; i++) { float d = a[i] - b[i]; s += d * d; }
        return MathF.Sqrt(s / Count);
    }
}

public struct MemoryEntry
{
    public EventType Event;
    public InterpretationKind Kind;
    public long Year;
    public int Cell;
    public float Anomaly;
    public float Strength;
    public bool ByCreator;
}

public enum EpithetKind : byte
{
    FireThrower, LightGiver, SkyVoice, RainHolder, AngrySun, BreadGiver, GiverTaker, Chooser, RainGiver, RainWord,
    DreamSpeaker, SkyFire, FallingStar, EarthShaker, Judge, HungerMother, VictoryGiver, Tester, Silent, Hidden, Hearer,
    EarthShaper, SeaLord, RiverGiver, Drowner, TrueWord, Deceiver, Count,
}

public struct Epithet
{
    public EpithetKind Kind;
    public Name Name;
    public long Year;
}

public sealed class Group
{
    public int Id;
    public Name Name;
    /// <summary>Фонемы самоназвания (для производных имён: державы, языки).</summary>
    public int[] NameWord = Array.Empty<int>();
    public long Founded;
    public int Parent = -1;
    public bool Alive = true;
    public long DiedYear = -1;
    public int Cell;
    public float Pop;
    public float Capacity;
    public float FoodRatio = 1f;
    public float Yield = 1f;
    public float Knowledge;
    public readonly float[] Culture = new float[CultureTrait.Count];
    public int LanguageId;
    public int ReligionId = -1;
    public int CultId = -1;
    public float CultShare;
    public readonly float[] Belief = new float[Doctrine.Count];
    // Латентные переменные отношения к Создателю.
    public float B, P, G, N, D, Guilt;
    public Attitude Attitude = Attitude.Indifference;
    public bool AttitudeInit;
    public readonly MemoryEntry[] Memory = new MemoryEntry[8];
    public int MemoryCount;
    public int MemoryHead;
    public readonly List<Epithet> Epithets = new(4);
    public int PolityId = -1;
    public Subsistence Subsistence;
    public long IsolatedSince = -1;
    public bool AnomalyNoticed;
    public long LastCreatorSeen = -1;
    public int AgentiveCount;
    public float YearLoss;
    public int FamineYears;
    public bool PrayedThisFamine;
    public long LastCrisis = -10000;
    public int Taboo = -1; // Concept
    public float TabooStrength;
    public readonly List<int> Territory = new();
    public readonly List<int> Neighbors = new();
    public readonly List<int> TradePartners = new();
    public bool Agriculture;
    public bool Writing, Metallurgy, Astronomy;
    /// <summary>Известные открытия — биты <see cref="Mirozdanie.Core.Sim.Tech"/> (kardashev.md, §3).</summary>
    public ulong Techs;
    /// <summary>Мощность на душу, Вт (kardashev.md, §4).</summary>
    public float Watts = 100f;
    /// <summary>Доля ископаемых в энергии народа (для выбросов и кризисов).</summary>
    public float FossilShare;
    /// <summary>Эпоха народа 1…9 (kardashev.md, §5).</summary>
    public int EraIdx = 1;
    /// <summary>Прибавка к ёмкости от торговли (до +30 %).</summary>
    public float TradeBonus;
    public int CrisisKinds;
    /// <summary>Сколько желаемой промышленной энергии народ получает (0…1).</summary>
    public float EnergyOk = 1f;
    // Петля бога (docs/design/god-loop.md)
    public float Dependence;      // зависимость от чуда, 0…1
    public float Suspicion;       // догадка о Создателе-симуляторе, 0…1
    public long LastPetition = -1000;
    public bool Freed;            // Создатель отпустил народ на свободу
    public float FaunaHunted;
    public long LastPrayer = -1;
    public long PrayerAnswered = -1;
    public long LastPlague = -1000;
    public long LastDefeat = -1000;
    public long BlessedUntil = -1;
    public readonly List<(int cell, long year, float anomaly)> AnomalyLog = new();

    public float Piety => Culture[CultureTrait.Piety];

    public void Remember(in MemoryEntry m)
    {
        Memory[MemoryHead] = m;
        MemoryHead = (MemoryHead + 1) % Memory.Length;
        if (MemoryCount < Memory.Length) MemoryCount++;
    }
}

public enum PersonRole : byte { Prophet, Leader, Sage }

public sealed class Person
{
    public int Id;
    public Name Name;
    public PersonRole Role;
    public int Group;
    public int Polity = -1;
    public long Born;
    public long Died = -1;
    public bool Martyr;
    public float Ambition, Piety, Cruelty, Wisdom;
    public string Message = "";
    public Concept Meaning;
}

public enum DeityForm : byte { OneCreator, Pantheon, Ancestors, Nature, None }

public sealed class Religion
{
    public int Id;
    public Name Name;
    public Name CreatorName;
    public int FounderGroup;
    public int Parent = -1;
    public long Founded;
    public readonly float[] Doctrine = new float[Culture.Doctrine.Count];
    public DeityForm Form;
    public bool IsCult;
    public int Prophet = -1;
    public float Cred;
    public float Zeal = 1f;
    public bool Alive = true;
    public long Faded = -1;
    public int Adherents;      // групп
    public float AdherentPop;
    public int MaxAdherents;
    public readonly List<int> Sites = new();
    public readonly List<EpithetKind> Epithets = new();
    public string Message = "";
    public bool Rebel;          // мятежная вера (Создатель — тиран)
    public bool Converted;      // хоть одна группа обратилась целиком
    public long ProphecyYear = -1; // срок предсказанного конца света
}

public sealed class SacredSite
{
    public int Id;
    public int Cell;
    public Name Name;
    public int Religion;
    public long Founded;
    public EventType Origin;
    public bool Temple;
}

public sealed class Polity
{
    public int Id;
    public Name Name;
    public int Capital;
    public readonly List<int> Members = new();
    public int Leader = -1;
    public Name Dynasty;
    public long Founded;
    public bool Alive = true;
    public long Ended = -1;
    public float Treasury = 0.5f;
    public float Elite = 0.05f;
    public float Legitimacy = 0.7f;
    public float Curse; // «кара на династию»
    public bool MandateLost;
    public long LeaderSince;
    public int LeaderLifespan;
    public int Parent = -1;
    public float Psi;
    public long LastWar = -1000;
    public long LastRevolt = -1000;
    /// <summary>Форма власти (civilizations.md, §1) — выводится из культуры, веры и хозяйства раз в 10 лет.</summary>
    public GovForm Form = GovForm.Chiefdom;
    public int PendingForm = -1;
    /// <summary>Щит от чудес (god-loop.md §6): деяния в землях державы ×0,3.</summary>
    public bool Shield;
    public long LastContact = -1000;
    public bool FreedomAsked;
}

public enum OathKind : byte { Rain, Victory, Protect, Spare, Answer }
public enum PetitionKind : byte { Victory, Punish, Healing, Sign, Answer }
public enum CreatorStyle : byte { Unknown, Merciful, Wrathful, Silent, Teacher, Architect, Deceiver, Fickle }

/// <summary>Обет Создателя народу (god-loop.md §3).</summary>
public sealed class Oath
{
    public int Id;
    public int Group;
    public OathKind Kind;
    public int Target = -1;      // держава-противник для «победы»
    public long Year, Deadline;
    public int Status;           // 0 открыт, 1 сдержан, −1 нарушен
    public int Event = -1;
}

/// <summary>Просьба народа к Создателю (god-loop.md §2).</summary>
public sealed class Petition
{
    public int Id;
    public int Group;
    public PetitionKind Kind;
    public int Target = -1;
    public long Year, Deadline;
    public int Status;           // 0 открыта, 1 исполнена, −1 забыта
    public int Event = -1;
}

/// <summary>Формы власти по Bueno de Mesquita et al. 2003 (размер «выборщиков» и «коалиции»).</summary>
public enum GovForm : byte { Chiefdom, Kingdom, Theocracy, Empire, Republic, Federation, Ideocracy, Democracy, Technocracy }

/// <summary>Поселение народа (civilizations.md, §10): размеры по закону Ципфа, имя на языке народа.</summary>
public sealed class City
{
    public int Id;
    public int Group;
    public int Cell;
    public Name Name;
    public long Founded;
    public float Pop;           // настоящие люди
    public bool Alive = true;
    public bool Capital;        // столица державы
    public bool Holy;           // святыня рядом
    /// <summary>Класс: 0 деревня, 1 город, 2 большой город, 3 мегаполис.</summary>
    public int Rank => Pop >= 1e7f ? 3 : Pop >= 5e5f ? 2 : Pop >= 1e4f ? 1 : 0;
}

/// <summary>Войско на марше (civilizations.md, §10): идёт по эко-клеткам к цели, бой — по прибытии.</summary>
public sealed class Army
{
    public int Id;
    public int Polity = -1;     // −1 — ополчение вольного народа
    public int Group;           // откуда набрано
    public int Target;          // народ-цель
    public float X, Y;          // позиция в эко-клетках (дробная, X — по долготе без заворота)
    public float TX, TY;
    public float Soldiers;      // в настоящих людях
    public bool Defender;
    public bool Holy;
    public int Declaration = -1; // событие объявления войны
    public long Started;
    public bool Alive = true;
    public int Outcome;         // 0 идёт, 1 победа, −1 поражение, 2 распущено
    public long Ended = -1;
}

/// <summary>Отношения двух держав (civilizations.md, §3): мнение, доверие, договоры, обиды.</summary>
public sealed class Relation
{
    public int A, B;            // A < B
    public float Opinion;       // −1…+1, затухает 3 %/год
    public float Trust = 0.3f;  // 0…1
    public long PeaceUntil = -1;   // мирный договор
    public long AllianceSince = -1;
    public long TradeSince = -1;
    public int Wars;
    public readonly List<Grievance> Grievances = new();
}

/// <summary>Обида державы Holder: Kind 0 — напали, 1 — отняли народ Group, 2 — нарушили договор.</summary>
public readonly record struct Grievance(long Year, int Kind, int Holder, int Group, int EventId);

public sealed class War
{
    public int Attacker; // группа
    public int Defender; // группа
    public int AttackerPolity = -1, DefenderPolity = -1;
    public bool Holy;
    public long Year;
    public int Winner = -1;
}
