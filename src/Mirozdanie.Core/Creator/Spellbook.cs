namespace Mirozdanie.Core.Creator;

/// <summary>Сферы сил Создателя (PLAN.md, §8).</summary>
public enum Sphere : byte { Nature, Life, Mind, Shrines, History, Earth, Word }

/// <summary>
/// Как сила выбирает цель: клетка эко-сетки, народ; для сил Земли — точка карты, отрезок из двух точек,
/// весь мир или мазок кисти (docs/design/terraforming.md).
/// </summary>
public enum Targeting : byte { Cell, Group, Point, Line, Global, Brush }

/// <summary>Режим кисти: ключ для журнала, название, подсказка, иконка и смысл для народов (+1 дар, −1 вред).</summary>
public sealed record BrushInfo(Mirozdanie.Core.Planet.BrushMode Mode, string Key, string Title, string Hint, string Icon, int Benefit);

/// <summary>Описание силы: каталог для интерфейса, сценариев и сохранений.</summary>
public sealed record Spell(
    InterventionType Type, string Key, Sphere Sphere, string Title, float Cost, Targeting Targeting, bool Benevolent, string Hint, string Icon);

/// <summary>Книга заклинаний. Эффекты — Simulation.Creator.cs, дизайн — docs/design/viewer-2.md.</summary>
public static class Spellbook
{
    public static readonly Spell[] All =
    {
        // Природа
        new(InterventionType.Rain, "rain", Sphere.Nature, "Дождь", 10, Targeting.Cell, true, "Проливной дождь над землями; снимает засуху.", "rain"),
        new(InterventionType.Drought, "drought", Sphere.Nature, "Засуха", 20, Targeting.Cell, false, "Три года без дождей в радиусе четырёх земель.", "sun"),
        new(InterventionType.Lightning, "lightning", Sphere.Nature, "Молния", 5, Targeting.Cell, false, "Удар с ясного неба; в столице убивает правителя.", "bolt"),
        new(InterventionType.Earthquake, "earthquake", Sphere.Nature, "Землетрясение", 15, Targeting.Cell, false, "Земля содрогается: потери в радиусе трёх земель.", "crack"),
        new(InterventionType.Flood, "flood", Sphere.Nature, "Потоп", 20, Targeting.Cell, false, "Реки выходят из берегов: гибнут люди, но земля потом жирнее.", "wave"),
        new(InterventionType.Winter, "winter", Sphere.Nature, "Долгая зима", 25, Targeting.Cell, false, "Холод на годы: урожаи падают.", "snow"),
        new(InterventionType.Meteor, "meteor", Sphere.Nature, "Метеорит", 60, Targeting.Cell, false, "Огненный камень: кратер, гибель народов, «зима».", "meteor"),
        // Жизнь
        new(InterventionType.Harvest, "harvest", Sphere.Life, "Чудо урожая", 25, Targeting.Cell, true, "Небывалый урожай на год.", "wheat"),
        new(InterventionType.Fertility, "fertility", Sphere.Life, "Плодородие", 20, Targeting.Group, true, "Народ растёт: +15 % людей.", "seed"),
        new(InterventionType.Herds, "herds", Sphere.Life, "Стада", 15, Targeting.Cell, true, "Звери возвращаются в выбитые охотой земли.", "horn"),
        new(InterventionType.Heal, "heal", Sphere.Life, "Исцеление", 20, Targeting.Group, true, "Мор отступает, больные встают.", "cross"),
        new(InterventionType.Plague, "plague", Sphere.Life, "Мор", 30, Targeting.Group, false, "Поветрие уносит пятую часть народа.", "skull"),
        // Разум
        new(InterventionType.Vision, "vision", Sphere.Mind, "Видение", 15, Targeting.Group, true, "Пророк видит сон с вашим посланием.", "eye"),
        new(InterventionType.Inspire, "inspire", Sphere.Mind, "Озарение", 25, Targeting.Group, true, "Знание народа растёт; возможно открытие.", "star"),
        new(InterventionType.Teach, "teach", Sphere.Mind, "Наставить", 15, Targeting.Group, true, "Вера народа сдвигается к посланию.", "scroll"),
        new(InterventionType.Forget, "forget", Sphere.Mind, "Забвение", 20, Targeting.Group, false, "Народ забывает знаки неба и имена Создателя.", "mist"),
        new(InterventionType.Spark, "spark", Sphere.Mind, "Искра", 40, Targeting.Group, true, "Вспышка разума (до Пробуждения — снижает порог разума).", "spark"),
        // Святыни
        new(InterventionType.Shrine, "shrine", Sphere.Shrines, "Реликвия", 20, Targeting.Cell, true, "Сияющий камень: здесь возникнет святыня.", "relic"),
        new(InterventionType.CursedPlace, "cursed", Sphere.Shrines, "Проклятое место", 20, Targeting.Cell, false, "Земля чахнет двадцать лет.", "curse"),
        // История
        new(InterventionType.Appear, "appear", Sphere.History, "Явиться", 30, Targeting.Group, true, "Вы являетесь народу. Реакция зависит от их веры.", "crown"),
        new(InterventionType.Bless, "bless", Sphere.History, "Благословить", 20, Targeting.Group, true, "Пять лет обильных урожаев и любви.", "halo"),
        new(InterventionType.Demand, "demand", Sphere.History, "Потребовать жертв", 0, Targeting.Group, false, "Послушные отдадут часть пищи — энергия Создателю.", "bowl"),
        new(InterventionType.Threaten, "threaten", Sphere.History, "Пригрозить", 5, Targeting.Group, false, "Страх растёт; мятежники крепнут.", "fist"),
        new(InterventionType.BlessDynasty, "bless_dynasty", Sphere.History, "Благословить династию", 20, Targeting.Group, true, "Правители державы обретают мандат неба.", "sceptre"),
        new(InterventionType.CurseDynasty, "curse_dynasty", Sphere.History, "Проклясть династию", 20, Targeting.Group, false, "Небо отворачивается от правителей.", "broken"),
        new(InterventionType.Peace, "peace", Sphere.History, "Погасить войну", 15, Targeting.Group, true, "Держава и соседи 20 лет не воюют.", "dove"),
        new(InterventionType.Strife, "strife", Sphere.History, "Разжечь войну", 15, Targeting.Group, false, "Держава немедленно идёт войной на соседа.", "sword"),
        // Земля (docs/design/terraforming.md)
        new(InterventionType.Sculpt, "sculpt", Sphere.Earth, "Лепка земли", 0, Targeting.Brush, true, "Ведите кистью по планете.", "brush"),
        new(InterventionType.Ridge, "ridge", Sphere.Earth, "Горный хребет", 40, Targeting.Line, false, "Два клика: начало и конец хребта. В море — цепь островов.", "mountain"),
        new(InterventionType.Bridge, "bridge", Sphere.Earth, "Перешеек", 30, Targeting.Line, true, "Два клика: полоса суши через море. По ней пойдут люди и звери.", "bridge"),
        new(InterventionType.Strait, "strait", Sphere.Earth, "Пролив", 25, Targeting.Line, false, "Два клика: море прорезает сушу и разделяет народы.", "strait"),
        new(InterventionType.Island, "island", Sphere.Earth, "Остров", 30, Targeting.Point, true, "Из моря поднимается остров.", "island"),
        new(InterventionType.Sink, "sink", Sphere.Earth, "Погрузить землю", 35, Targeting.Point, false, "Земля уходит под воду: залив или внутреннее море. Кто не успел бежать — тонет.", "sink"),
        new(InterventionType.River, "river", Sphere.Earth, "Река", 15, Targeting.Point, true, "Родник в этом месте — река потечёт к морю.", "river"),
        new(InterventionType.Lake, "lake", Sphere.Earth, "Озеро", 15, Targeting.Point, true, "Чаша, которую наполнит вода.", "lake"),
        new(InterventionType.Volcano, "volcano", Sphere.Earth, "Вулкан", 45, Targeting.Point, false, "Огненная гора: гибель рядом и пепловая зима, но потом — жирная почва и руда.", "volcano"),
        new(InterventionType.Oasis, "oasis", Sphere.Earth, "Оазис", 20, Targeting.Point, true, "Дожди здесь станут вдвое обильнее.", "oasis"),
        new(InterventionType.Desert, "desert", Sphere.Earth, "Пустыня", 20, Targeting.Point, false, "Дожди уйдут отсюда навсегда.", "desert"),
        new(InterventionType.Glacier, "glacier", Sphere.Earth, "Ледник", 25, Targeting.Point, false, "Вечный холод: −8 °C.", "glacier"),
        new(InterventionType.Thaw, "thaw", Sphere.Earth, "Растопить льды", 25, Targeting.Point, true, "Тепло: +8 °C, льды отступают.", "thaw"),
        new(InterventionType.Drift, "drift", Sphere.Earth, "Дрейф материка", 120, Targeting.Line, false, "Клик по суше, затем — куда сдвинуть. Народы едут вместе с землёй; при столкновении растут горы.", "drift"),
        new(InterventionType.Warmer, "warmer", Sphere.Earth, "Дыхание мира: теплее", 80, Targeting.Global, false, "Весь мир теплее на 1 °C; льды тают, море поднимается на 20 м.", "warm"),
        new(InterventionType.Colder, "colder", Sphere.Earth, "Дыхание мира: холоднее", 80, Targeting.Global, false, "Весь мир холоднее на 1 °C; льды растут, море отступает на 20 м.", "cold"),
        new(InterventionType.SeaRise, "sea_rise", Sphere.Earth, "Море наступает", 80, Targeting.Global, false, "Уровень моря +100 м по всей планете: береговые народы бегут или гибнут.", "searise"),
        new(InterventionType.SeaFall, "sea_fall", Sphere.Earth, "Море отступает", 80, Targeting.Global, true, "Уровень моря −100 м: обнажаются шельфы и сухопутные мосты.", "seafall"),
        // Слово (docs/design/god-loop.md): только из разговора.
        new(InterventionType.Oath, "oath", Sphere.Word, "Обет", 0, Targeting.Group, true, "Пообещать народу дождь, победу, защиту или пощаду. Сдержанный обет рождает любовь, нарушенный — имя «Обманщик».", "chat"),
        new(InterventionType.Reply, "reply", Sphere.Word, "Ответ на послание", 5, Targeting.Group, true, "Ответить учёным, что ищут вас, — или отпустить народ на свободу.", "chat"),
    };

    /// <summary>Режимы кисти (terraforming.md, §1).</summary>
    public static readonly BrushInfo[] Brushes =
    {
        new(Mirozdanie.Core.Planet.BrushMode.Raise, "raise", "Поднять", "Земля растёт: суша из моря, холмы, горы.", "raise", 0),
        new(Mirozdanie.Core.Planet.BrushMode.Lower, "lower", "Опустить", "Земля опускается: впадины, заливы, моря.", "lower", -1),
        new(Mirozdanie.Core.Planet.BrushMode.Flatten, "flatten", "Выровнять", "К высоте точки, где начат мазок: плато, равнины.", "flatten", 0),
        new(Mirozdanie.Core.Planet.BrushMode.Smooth, "smooth", "Сгладить", "Горы оплывают, рельеф мягчеет.", "smooth", 0),
        new(Mirozdanie.Core.Planet.BrushMode.Roughen, "roughen", "Изрезать", "Гряды, холмы, фьорды.", "roughen", 0),
        new(Mirozdanie.Core.Planet.BrushMode.Wet, "wet", "Увлажнить", "Больше дождей.", "rain", 1),
        new(Mirozdanie.Core.Planet.BrushMode.Dry, "dry", "Иссушить", "Меньше дождей.", "sun", -1),
        new(Mirozdanie.Core.Planet.BrushMode.Warm, "warm", "Согреть", "Теплее навсегда.", "warm", 0),
        new(Mirozdanie.Core.Planet.BrushMode.Cool, "cool", "Остудить", "Холоднее навсегда.", "cold", -1),
        new(Mirozdanie.Core.Planet.BrushMode.Fertile, "fertile", "Удобрить", "Почвы жирнее.", "seed", 1),
        new(Mirozdanie.Core.Planet.BrushMode.Barren, "barren", "Истощить", "Почвы скуднее.", "curse", -1),
    };

    public static BrushInfo? BrushByKey(string key)
    {
        foreach (var b in Brushes) if (b.Key == key) return b;
        return null;
    }

    public static BrushInfo BrushOf(Mirozdanie.Core.Planet.BrushMode m)
    {
        foreach (var b in Brushes) if (b.Mode == m) return b;
        throw new ArgumentOutOfRangeException(nameof(m));
    }

    public static Spell Get(InterventionType t)
    {
        foreach (var s in All) if (s.Type == t) return s;
        throw new ArgumentOutOfRangeException(nameof(t));
    }

    public static Spell? ByKey(string key)
    {
        foreach (var s in All) if (s.Key == key) return s;
        return null;
    }

    public static string SphereTitle(Sphere s) => s switch
    {
        Sphere.Nature => "Природа", Sphere.Life => "Жизнь", Sphere.Mind => "Разум", Sphere.Shrines => "Святыни", Sphere.History => "История", Sphere.Word => "Слово", _ => "Земля",
    };
}
