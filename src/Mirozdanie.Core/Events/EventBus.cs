namespace Mirozdanie.Core.Events;

/// <summary>
/// Типы событий. Смысл полей A/B/C указан в комментарии. См. docs/design/interactions.md.
/// </summary>
public enum EventType : ushort
{
    None = 0,
    // Планета
    PlanetFormed,          // A=материков
    NaturalDrought,        // Cell=центр региона, Magnitude=сила
    DroughtEnded,          // Cell, A=регион, Magnitude
    Bounty,                // Cell, A=регион: обильный год
    Earthquake,            // Cell
    Comet,                 // A=id кометы, B=период
    // Жизнь
    LifeBegan,             // A=первых видов
    SpeciesBorn,           // A=новый вид, B=родительский
    SpeciesExtinct,        // A=вид
    Awakening,             // A=вид, B=групп
    MegafaunaGone,         // A=вид, B=группа-охотник
    Plague,                // A=группа, Magnitude=доля умерших
    // Группы и языки
    GroupFormed,           // A=группа
    GroupFissioned,        // A=дочерняя, B=родительская
    GroupDestroyed,        // A=группа
    Famine,                // A=группа, Magnitude=дефицит
    LanguageSplit,         // A=новый язык, B=родитель, C=группа
    SoundLawApplied,       // A=язык, B=закон
    WordBorrowed,          // A=язык-получатель, B=язык-источник, C=понятие
    // Вера
    Interpretation,        // A=группа, B=InterpretationKind, C=EventType исходного события
    ReligionFounded,       // A=вера, B=группа-основатель
    CreatorNamed,          // A=вера
    EpithetGained,         // A=группа, B=эпитет (индекс)
    SacredSiteFounded,     // A=святыня, B=вера
    CultFounded,           // A=культ (вера), B=группа, C=пророк
    ProphetBorn,           // A=персонаж, B=группа
    Martyrdom,             // A=персонаж, B=группа
    Conversion,            // A=группа, B=новая вера, C=старая вера
    Schism,                // A=новая вера, B=родительская
    AttitudeShift,         // A=группа, B=новая позиция, C=старая
    CultFaded,             // A=культ
    AnomalyNoticed,        // A=группа
    PrayerUnanswered,      // A=группа
    CreatorEnergyLow,
    // Создатель
    Intervention,          // A=InterventionType, Cell
    LightningStrike,       // Cell, A=группа (или -1)
    MiracleHarvest,        // Cell
    Rain,                  // Cell
    Drought,               // Cell (вмешательство)
    Vision,                // A=группа, B=пророк
    MeteorImpact,          // Cell
    Spark,                 // A=вид
    // Хозяйство
    AgricultureAdopted,    // A=группа
    TradeRoute,            // A=группа, B=группа
    TempleBuilt,           // A=группа, B=святыня
    Discovery,             // A=группа, B=DiscoveryKind
    KardashevMilestone,    // Magnitude=K
    // Политика
    PolityFounded,         // A=полития, B=группа-столица
    LeaderDied,            // A=полития, B=персонаж
    LeaderStruck,          // A=полития, B=персонаж
    LeaderRose,            // A=полития, B=персонаж
    WarDeclared,           // A=агрессор (полития или ~группа), B=цель
    HolyWar,               // A, B как у WarDeclared
    Battle,                // A=победитель(группа), B=проигравший(группа), Magnitude=потери
    Annexation,            // A=полития, B=группа
    Revolt,                // A=полития, B=группа-мятежник
    PolitySplit,           // A=новая полития, B=старая
    Collapse,              // A=полития
    // Уровень 3
    ProphecyMade,          // A=вера, B=год срока
    ProphecyFulfilled,     // A=вера, C=событие-исполнение
    ProphecyFailed,        // A=вера, B=1 — верные перетолковали, 0 — культ рассыпался
    MandateLost,           // A=полития
    // Зритель 2: силы и разговор (docs/design/viewer-2.md)
    Healing,               // A=группа
    Inspiration,           // A=группа
    Blessing,              // A=группа
    ColdSnap,              // Cell
    Flood,                 // Cell
    HerdsReturn,           // Cell
    Forgetting,            // A=группа
    RelicAppeared,         // Cell, A=группа-владелец клетки
    CursedGround,          // Cell
    DynastyBlessed,        // A=группа-столица, B=полития
    DynastyCursed,         // A=группа-столица, B=полития
    PeaceImposed,          // A=группа, B=полития
    CreatorAppeared,       // A=группа, B=реакция (Attitude)
    DemandMet,             // A=группа, Magnitude=энергия
    DemandRefused,         // A=группа
    Threat,                // A=группа
    Teaching,              // A=группа, B=послание (индекс), C=вера
    PrayerAnswered,        // A=группа, C=EventType силы
    // Терраформирование (docs/design/terraforming.md)
    Terraformed,           // Cell=эко-клетка центра, A=TerraformKind, B=+1 дар / 0 знамение / −1 вред, C=1 «как природа», Magnitude
    RiverBorn,             // Cell=исток, C=1 «как природа»
    Eruption,              // Cell, A=погибших (сотни)
    SeaLevelChanged,       // A=+1 море наступает / −1 отступает, B=метров, C=1 «как природа»
    ClimateShifted,        // A=+1 теплее / −1 холоднее, C=1 «как природа»
    ContinentDrifted,      // Cell=эко-клетка суши после сдвига, A=dx, B=dy (эко-клетки), C=1 «как природа»
    GroupDrowned,          // Cell, A=группа: бежать было некуда
    Refugees,              // Cell=новое место, A=группа, B=старая клетка, Magnitude=доля погибших
    LandSank,              // Cell, A=особей погибло (эпоха жизни)
    // Лестница Кардашёва (docs/design/kardashev.md)
    EraBegan,              // Cell, A=эпоха (1…9), B=народ-первопроходец
    EnergyCrisis,          // Cell, A=народ, B=0 уголь / 1 нефть / 2 уран
    ClimateWarming,        // A=потепление ×10 (°C), Magnitude
    NuclearWar,            // Cell, A=держава-1, B=держава-2, Magnitude=доля погибших
    MegaprojectStarted,    // A=MegaprojectKind
    MegaprojectCompleted,  // A=MegaprojectKind, B=ярус
    // Цивилизации (docs/design/civilizations.md)
    CityFounded,           // Cell, A=город, B=народ
    CityGrew,              // Cell, A=город, B=новый класс (1 город, 2 большой, 3 мегаполис)
    GovernmentChanged,     // A=держава, B=новая GovForm, C=старая
    Assimilated,           // Cell, A=растворившийся народ, B=народ-приёмник
    ArmyMarched,           // Cell=откуда, A=войско, B=держава, C=народ-цель
    PeaceMade,             // A=держава, B=держава, C=лет мира
    TreatyBroken,          // A=нарушитель, B=пострадавшая держава
    AllianceFormed,        // A=держава, B=держава
    // Петля бога (docs/design/god-loop.md)
    PetitionMade,          // Cell, A=народ, B=PetitionKind, C=цель (держава)
    PetitionGranted,       // Cell, A=народ, B=PetitionKind
    PetitionIgnored,       // Cell, A=народ, B=PetitionKind
    OathSworn,             // Cell, A=народ, B=OathKind, C=цель
    OathKept,              // Cell, A=народ, B=OathKind
    OathBroken,            // Cell, A=народ, B=OathKind
    StyleNamed,            // A=CreatorStyle, B=прежний
    SimulationSuspected,   // Cell, A=народ, Magnitude=уверенность
    ProbeLaunched,         // Cell=откуда, A=народ, B=0 зонд / 1 удар / 2 посольство
    GeoglyphMade,          // Cell, A=народ
    LightMessage,          // Cell, A=народ
    ShieldRaised,          // Cell, A=держава
    FreedomDemanded,       // Cell, A=народ, B=держава
    ContactAnswered,       // Cell, A=народ, B=0 ответ / 1 свобода / 2 молчание
    CreatorWounded,        // Magnitude=потеря силы
    Count,
}

[Flags]
public enum EventFlags : int
{
    None = 0,
    ByCreator = 1,       // последствие вмешательства (культуры этого не знают)
    Weakened = 2,        // у Создателя не хватило энергии
    Crisis = 4,
}

/// <summary>Событие журнала. Тексты не хранятся: Хроника строит их из состояния мира.</summary>
public readonly record struct WorldEvent(
    int Id, long Year, EventType Type, int Cell, int A, int B, int C,
    float Magnitude, EventFlags Flags, int Cause)
{
    public bool ByCreator => (Flags & EventFlags.ByCreator) != 0;
}

/// <summary>
/// Журнал событий и шина подписки. Подписчики вызываются в порядке регистрации,
/// очередь раздаётся до опустения с ограничением глубины каскада.
/// </summary>
public sealed class EventBus
{
    public delegate void Handler(in WorldEvent e);

    private readonly List<WorldEvent> _log = new(4096);
    private readonly List<Handler>[] _subs;
    private WorldEvent[] _queue = new WorldEvent[256];
    private int _qHead, _qTail;
    private const int MaxDispatchPerTick = 100_000;

    public EventBus()
    {
        _subs = new List<Handler>[(int)EventType.Count];
        for (int i = 0; i < _subs.Length; i++) _subs[i] = new List<Handler>(2);
    }

    public IReadOnlyList<WorldEvent> Log => _log;
    public long CurrentYear { get; set; }

    public void Subscribe(EventType type, Handler h) => _subs[(int)type].Add(h);

    /// <summary>
    /// Отбросить события начиная с номера <paramref name="count"/> — только для отмены лепки до жизни,
    /// когда отменённые события уже розданы и никто на них не ссылается (terraforming.md, §1).
    /// </summary>
    public void TruncateTo(int count)
    {
        if (_qHead != _qTail) throw new InvalidOperationException("Нельзя отбросить события, пока очередь не раздана.");
        if (count < _log.Count) _log.RemoveRange(count, _log.Count - count);
    }

    public int Publish(EventType type, int cell = -1, int a = -1, int b = -1, int c = -1,
        float magnitude = 0f, EventFlags flags = EventFlags.None, int cause = -1)
    {
        int id = _log.Count;
        var e = new WorldEvent(id, CurrentYear, type, cell, a, b, c, magnitude, flags, cause);
        _log.Add(e);
        if (_qTail == _queue.Length)
        {
            if (_qHead > 0)
            {
                Array.Copy(_queue, _qHead, _queue, 0, _qTail - _qHead);
                _qTail -= _qHead; _qHead = 0;
            }
            if (_qTail == _queue.Length) Array.Resize(ref _queue, _queue.Length * 2);
        }
        _queue[_qTail++] = e;
        return id;
    }

    /// <summary>Раздаёт накопленные события подписчикам. Новые события, опубликованные подписчиками, тоже раздаются.</summary>
    public void Dispatch()
    {
        int n = 0;
        while (_qHead < _qTail && n++ < MaxDispatchPerTick)
        {
            var e = _queue[_qHead++];
            var list = _subs[(int)e.Type];
            for (int i = 0; i < list.Count; i++) list[i](in e);
        }
        _qHead = _qTail = 0;
    }

    public WorldEvent Get(int id) => _log[id];
}
