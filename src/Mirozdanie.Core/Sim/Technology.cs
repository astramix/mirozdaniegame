using Mirozdanie.Core.Culture;

namespace Mirozdanie.Core.Sim;

/// <summary>
/// Открытия (docs/design/kardashev.md, §3). Порядок — примерно по сложности; номер — бит в <see cref="Group.Techs"/> (не больше 64).
/// </summary>
public enum Tech : byte
{
    Agriculture, Pottery, Counting, Boats, DraftAnimals, Copper, Irrigation, Writing, Law, Bronze,
    Plow, Astronomy, Money, Roads, Sail, Iron, Bureaucracy, WaterWheel, CropRotation, Windmill,
    Printing, Navigation, Gunpowder, Universities, ScientificMethod, Steel, CoalMining, SteamEngine, Selection, Railroad,
    Chemistry, Ideologies, MassArmies, Oil, Electricity, Combustion, Fertilizer, Aviation, Computing, Fission,
    NuclearWeapons, Rockets, GreenRevolution, Semiconductors, Networks, Solar, GlobalInstitutions, Spaceflight, Fusion, AI,
    Nanomaterials, OrbitalCollectors,
    Count,
}

/// <summary>Ветви графа открытий (для Летописи и интерфейса).</summary>
public enum TechBranch : byte { Food, Materials, Energy, Transport, Knowledge, Society, War }

/// <summary>Ресурс, без которого открытие недоступно (в своих землях или, с дальней торговлей, у партнёров).</summary>
public enum TechResource : byte { None, Ore, RichOre, Coal, Oil, Uranium, Coast, River }

/// <summary>
/// Описание открытия: предпосылки, ресурс, порог образованности, наименьшее население (реальных людей),
/// базовая вероятность в год, эпоха, к которой оно относится, и фраза для Хроники.
/// </summary>
public sealed record TechInfo(Tech Id, TechBranch Branch, string Title, string Phrase, Tech[] Requires, TechResource Resource,
    float Knowledge, float MinPop, float Rate, int Era);

public static class Technology
{
    private static TechInfo T(Tech id, TechBranch b, string title, string phrase, Tech[] req, TechResource res, float know, float minPop, float rate, int era)
        => new(id, b, title, phrase, req, res, know, minPop, rate, era);

    private static Tech[] R(params Tech[] t) => t;

    /// <summary>
    /// Граф открытий. Пороги образованности (Knowledge 0…1) растут с глубиной графа; вероятность — база в год
    /// при подходящих условиях, её умножают нужда, размер и связность общества (kardashev.md, §3).
    /// </summary>
    public static readonly TechInfo[] All =
    {
        T(Tech.Agriculture, TechBranch.Food, "земледелие", "первым в своих краях стал сеять зерно", R(), TechResource.None, 0.12f, 0, 0.02f, 2),
        T(Tech.Pottery, TechBranch.Materials, "гончарство", "научился обжигать глину: горшки хранят зерно и воду", R(), TechResource.None, 0.08f, 0, 0.01f, 1),
        T(Tech.Counting, TechBranch.Knowledge, "счёт", "придумал счёт: зарубки на палках стали числами", R(), TechResource.None, 0.1f, 0, 0.01f, 1),
        T(Tech.Boats, TechBranch.Transport, "лодки", "выдолбил первые лодки", R(), TechResource.Coast, 0.08f, 0, 0.012f, 1),
        T(Tech.DraftAnimals, TechBranch.Energy, "тягловый скот", "впряг зверей в работу: сила быка вместо силы рук", R(Tech.Agriculture), TechResource.None, 0.16f, 0, 0.008f, 2),
        T(Tech.Copper, TechBranch.Materials, "медь", "научился плавить руду", R(Tech.Pottery), TechResource.Ore, 0.2f, 0, 0.006f, 2),
        T(Tech.Irrigation, TechBranch.Food, "орошение", "прорыл каналы: вода пришла на сухие поля", R(Tech.Agriculture), TechResource.River, 0.2f, 0, 0.008f, 2),
        T(Tech.Writing, TechBranch.Knowledge, "письмо", "изобрёл письмо", R(Tech.Counting), TechResource.None, 0.32f, 300_000, 0.006f, 2),
        T(Tech.Law, TechBranch.Society, "закон", "записал первые законы: суд вместо мести", R(Tech.Writing), TechResource.None, 0.36f, 300_000, 0.006f, 2),
        T(Tech.Bronze, TechBranch.Materials, "бронза", "сплавил медь с оловом: родилась бронза", R(Tech.Copper), TechResource.RichOre, 0.34f, 0, 0.006f, 2),
        T(Tech.Plow, TechBranch.Food, "плуг", "взялся за плуг: одна упряжка поднимает поле на целую деревню", R(Tech.Agriculture, Tech.DraftAnimals), TechResource.None, 0.3f, 0, 0.006f, 2),
        T(Tech.Astronomy, TechBranch.Knowledge, "астрономия", "начал считать годы небес: мудрецы предсказывают возвращение хвостатых звёзд", R(Tech.Counting), TechResource.None, 0.4f, 0, 0.005f, 2),
        T(Tech.Money, TechBranch.Society, "деньги", "стал чеканить монету", R(Tech.Copper, Tech.Writing), TechResource.None, 0.42f, 500_000, 0.005f, 3),
        T(Tech.Roads, TechBranch.Transport, "дороги", "проложил мощёные дороги", R(Tech.Law), TechResource.None, 0.42f, 500_000, 0.005f, 3),
        T(Tech.Sail, TechBranch.Transport, "парус", "поднял парус: лодки ушли далеко от берега", R(Tech.Boats, Tech.Pottery), TechResource.Coast, 0.36f, 0, 0.006f, 3),
        T(Tech.Iron, TechBranch.Materials, "железо", "выковал железо: дешёвый металл для плуга и меча", R(Tech.Bronze), TechResource.Ore, 0.48f, 0, 0.005f, 3),
        T(Tech.Bureaucracy, TechBranch.Society, "чиновники", "завёл писцов и сборщиков податей: держава держится на бумаге", R(Tech.Law, Tech.Money), TechResource.None, 0.5f, 1_000_000, 0.004f, 3),
        T(Tech.WaterWheel, TechBranch.Energy, "водяное колесо", "поставил водяное колесо: река мелет зерно", R(Tech.Iron), TechResource.River, 0.54f, 0, 0.004f, 4),
        T(Tech.CropRotation, TechBranch.Food, "севооборот", "стал чередовать посевы: земля отдыхает, не пустуя", R(Tech.Plow, Tech.Iron), TechResource.None, 0.54f, 0, 0.004f, 4),
        T(Tech.Windmill, TechBranch.Energy, "ветряная мельница", "заставил ветер молоть зерно", R(Tech.WaterWheel), TechResource.None, 0.58f, 0, 0.004f, 4),
        T(Tech.Printing, TechBranch.Knowledge, "книгопечатание", "отпечатал первую книгу: знание стало дешёвым", R(Tech.Writing, Tech.Iron), TechResource.None, 0.6f, 1_000_000, 0.004f, 4),
        T(Tech.Navigation, TechBranch.Transport, "мореходство", "научился водить корабли по звёздам через открытое море", R(Tech.Sail, Tech.Astronomy), TechResource.Coast, 0.6f, 0, 0.004f, 4),
        T(Tech.Gunpowder, TechBranch.War, "порох", "смешал огненный порошок", R(Tech.Iron, Tech.Astronomy), TechResource.None, 0.62f, 0, 0.003f, 4),
        T(Tech.Universities, TechBranch.Knowledge, "университеты", "открыл дома учёности, где спорят о природе вещей", R(Tech.Printing, Tech.Bureaucracy), TechResource.None, 0.64f, 2_000_000, 0.003f, 4),
        T(Tech.ScientificMethod, TechBranch.Knowledge, "научный метод", "стал проверять мысли опытом: родилась наука", R(Tech.Universities, Tech.Astronomy), TechResource.None, 0.68f, 0, 0.003f, 4),
        T(Tech.Steel, TechBranch.Materials, "сталь", "научился варить сталь", R(Tech.Iron, Tech.ScientificMethod), TechResource.RichOre, 0.7f, 0, 0.003f, 5),
        T(Tech.CoalMining, TechBranch.Energy, "уголь", "стал добывать горючий камень", R(Tech.Iron), TechResource.Coal, 0.62f, 0, 0.004f, 4),
        T(Tech.SteamEngine, TechBranch.Energy, "паровая машина", "построил паровую машину: огонь стал работать за тысячу рук", R(Tech.CoalMining, Tech.Steel, Tech.ScientificMethod), TechResource.None, 0.72f, 0, 0.003f, 5),
        T(Tech.Selection, TechBranch.Food, "селекция", "вывел новые сорта и породы", R(Tech.CropRotation, Tech.ScientificMethod), TechResource.None, 0.72f, 0, 0.003f, 5),
        T(Tech.Railroad, TechBranch.Transport, "железная дорога", "проложил железную дорогу", R(Tech.SteamEngine), TechResource.None, 0.74f, 0, 0.003f, 5),
        T(Tech.Chemistry, TechBranch.Materials, "химия", "разложил вещества на начала: родилась химия", R(Tech.ScientificMethod), TechResource.None, 0.74f, 0, 0.003f, 5),
        T(Tech.Ideologies, TechBranch.Society, "идеологии", "заспорил о том, каким должно быть общество, — без богов и царей", R(Tech.Printing, Tech.ScientificMethod), TechResource.None, 0.74f, 0, 0.003f, 5),
        T(Tech.MassArmies, TechBranch.War, "массовые армии", "поставил под ружьё весь народ", R(Tech.Gunpowder, Tech.Railroad), TechResource.None, 0.76f, 3_000_000, 0.003f, 5),
        T(Tech.Oil, TechBranch.Energy, "нефть", "стал добывать земляное масло", R(Tech.Chemistry, Tech.SteamEngine), TechResource.Oil, 0.78f, 0, 0.003f, 6),
        T(Tech.Electricity, TechBranch.Energy, "электричество", "покорил молнию: по проводам побежал ток", R(Tech.Chemistry, Tech.WaterWheel), TechResource.None, 0.8f, 0, 0.003f, 6),
        T(Tech.Combustion, TechBranch.Transport, "двигатель внутреннего сгорания", "построил двигатель, что сжигает масло внутри себя", R(Tech.Oil, Tech.Steel), TechResource.None, 0.8f, 0, 0.003f, 6),
        T(Tech.Fertilizer, TechBranch.Food, "удобрения", "научился брать азот из воздуха: поля дают вдвое", R(Tech.Chemistry, Tech.Electricity), TechResource.None, 0.82f, 0, 0.003f, 6),
        T(Tech.Aviation, TechBranch.Transport, "авиация", "поднялся в небо — туда, где жил Создатель", R(Tech.Combustion), TechResource.None, 0.84f, 0, 0.002f, 6),
        T(Tech.Computing, TechBranch.Knowledge, "вычислительные машины", "построил машину, которая считает быстрее мудрецов", R(Tech.Electricity), TechResource.None, 0.86f, 0, 0.002f, 7),
        T(Tech.Fission, TechBranch.Energy, "расщепление ядра", "расщепил ядро вещества", R(Tech.Electricity, Tech.Chemistry), TechResource.Uranium, 0.86f, 0, 0.002f, 7),
        T(Tech.NuclearWeapons, TechBranch.War, "ядерное оружие", "создал оружие, способное сжечь мир", R(Tech.Fission, Tech.Aviation), TechResource.None, 0.86f, 3_000_000, 0.002f, 7),
        T(Tech.Rockets, TechBranch.Transport, "ракеты", "запустил ракету выше неба", R(Tech.Combustion, Tech.Chemistry), TechResource.None, 0.86f, 0, 0.002f, 7),
        T(Tech.GreenRevolution, TechBranch.Food, "зелёная революция", "вывел хлеба, что кормят миллиарды", R(Tech.Fertilizer, Tech.Selection), TechResource.None, 0.86f, 0, 0.002f, 7),
        T(Tech.Semiconductors, TechBranch.Materials, "полупроводники", "заключил мысль в кремний", R(Tech.Computing, Tech.Chemistry), TechResource.None, 0.88f, 0, 0.002f, 7),
        T(Tech.Networks, TechBranch.Knowledge, "всемирная сеть", "соединил все машины мира в одну сеть", R(Tech.Semiconductors), TechResource.None, 0.9f, 0, 0.002f, 7),
        T(Tech.Solar, TechBranch.Energy, "солнечная энергия", "научился пить свет звезды напрямую", R(Tech.Semiconductors), TechResource.None, 0.9f, 0, 0.002f, 7),
        T(Tech.GlobalInstitutions, TechBranch.Society, "мировые институты", "созвал совет всех держав мира", R(Tech.Networks, Tech.Ideologies), TechResource.None, 0.9f, 0, 0.002f, 8),
        T(Tech.Spaceflight, TechBranch.Transport, "космические полёты", "вышел за пределы неба", R(Tech.Rockets, Tech.Semiconductors), TechResource.None, 0.92f, 0, 0.0015f, 8),
        T(Tech.Fusion, TechBranch.Energy, "термоядерный синтез", "зажёг маленькое солнце на земле", R(Tech.Fission, Tech.Computing), TechResource.None, 0.94f, 0, 0.0015f, 8),
        T(Tech.AI, TechBranch.Knowledge, "машинный разум", "создал машину, которая думает сама", R(Tech.Networks, Tech.Semiconductors), TechResource.None, 0.94f, 0, 0.0015f, 8),
        T(Tech.Nanomaterials, TechBranch.Materials, "наноматериалы", "научился строить вещество по атому", R(Tech.Semiconductors, Tech.Chemistry), TechResource.None, 0.94f, 0, 0.0015f, 8),
        T(Tech.OrbitalCollectors, TechBranch.Energy, "орбитальные коллекторы", "развесил над планетой зеркала, ловящие свет звезды", R(Tech.Spaceflight, Tech.Solar, Tech.Nanomaterials), TechResource.None, 0.96f, 0, 0.0015f, 8),
    };

    public static TechInfo Get(Tech t) => All[(int)t];

    public static bool Has(this Group g, Tech t) => (g.Techs & (1UL << (int)t)) != 0;

    public static void Add(this Group g, Tech t) => g.Techs |= 1UL << (int)t;

    public static int CountOf(ulong techs)
    {
        int n = 0;
        while (techs != 0) { techs &= techs - 1; n++; }
        return n;
    }

    /// <summary>Эпохи мира (kardashev.md, §5) — названия для Летописи и интерфейса.</summary>
    public static readonly string[] EraNames =
    {
        "", "Племена", "Ранние государства", "Классическая эпоха", "Механическая эпоха", "Индустрия",
        "Электричество и нефть", "Информация и атом", "Планетарная эпоха", "Кардашёв I",
    };
}
