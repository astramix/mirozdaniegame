namespace Mirozdanie.Core.Language;

/// <summary>Базовые понятия лексикона. Порядок фиксирован (индекс — ключ словаря).</summary>
public enum Concept : byte
{
    Water, Fire, Sun, Moon, Sky, Earth, Stone, Tree, River, Mountain, Sea, Rain, Wind, Lightning,
    Death, Life, God, Spirit, Enemy, People, Father, Mother, Child, Blood, Food, Grain, Beast, Bird,
    Fish, Star, Night, Day, Hand, Eye, Heart, Word, Great, Small, Old, New, Good, Bad, Strong, Silent,
    Angry, Give, Throw, Hold, Holy, War, Peace, Chief, Land, City, House, Path, Gift, Punishment, Light,
    Dark, Dream, Voice, Judge, Red, White, Black, Cold, Hot, Hunger, Bread, Shake, Choose, Test, Fall,
    Speak, See, Hidden, Free, Mind, Victory, Grief, End, Clear, Speech, Hear,
    // Морфемы-суффиксы (тоже меняются звуковыми законами)
    SuffixPerson, SuffixPlace, SuffixPeople,
    Count,
}

public static class Concepts
{
    public static readonly string[] Russian =
    {
        "вода", "огонь", "солнце", "луна", "небо", "земля", "камень", "дерево", "река", "гора", "море", "дождь", "ветер", "молния",
        "смерть", "жизнь", "бог", "дух", "враг", "народ", "отец", "мать", "дитя", "кровь", "пища", "зерно", "зверь", "птица",
        "рыба", "звезда", "ночь", "день", "рука", "глаз", "сердце", "слово", "великий", "малый", "старый", "новый", "добрый", "злой", "сильный", "молчащий",
        "гневный", "давать", "бросать", "держать", "святой", "война", "мир", "вождь", "страна", "город", "дом", "путь", "дар", "кара", "свет",
        "тьма", "сон", "голос", "судья", "красный", "белый", "чёрный", "холод", "жар", "голод", "хлеб", "трясти", "избирать", "испытание", "падать",
        "говорить", "видеть", "скрытый", "свободный", "разум", "победа", "горе", "конец", "ясный", "речь", "слышать",
        "-(лицо)", "-(место)", "-(народ)",
    };

    /// <summary>Звукосимволизм: −1 — «малое/светлое» (передние гласные), +1 — «большое/тёмное» (задние). Ohala 1984.</summary>
    public static int Symbolism(Concept c) => c switch
    {
        Concept.Small or Concept.Child or Concept.Light or Concept.Star or Concept.Bird or Concept.Eye or Concept.Clear or Concept.White => -1,
        Concept.Great or Concept.Dark or Concept.Mountain or Concept.Strong or Concept.Death or Concept.Black or Concept.Earth or Concept.Old => 1,
        _ => 0,
    };

    /// <summary>Короткие понятия (служебные, частые) — однослоговые.</summary>
    public static bool Short(Concept c) => c is Concept.Water or Concept.Fire or Concept.Sun or Concept.Sky or Concept.Eye or Concept.Hand
        or Concept.Give or Concept.Hold or Concept.See or Concept.Blood or Concept.Day or Concept.Night
        or Concept.SuffixPerson or Concept.SuffixPlace or Concept.SuffixPeople;

    /// <summary>Ядро списка Сводеша для взаимопонятности.</summary>
    public static readonly Concept[] Core =
    {
        Concept.Water, Concept.Fire, Concept.Sun, Concept.Moon, Concept.Sky, Concept.Earth, Concept.Stone, Concept.Tree,
        Concept.River, Concept.Mountain, Concept.Rain, Concept.Wind, Concept.Death, Concept.Father, Concept.Mother,
        Concept.Child, Concept.Blood, Concept.Food, Concept.Fish, Concept.Bird, Concept.Star, Concept.Night, Concept.Day,
        Concept.Hand, Concept.Eye, Concept.Heart, Concept.Word, Concept.Great, Concept.Small, Concept.Old, Concept.New,
        Concept.Good, Concept.Bad, Concept.Give, Concept.See, Concept.Speak, Concept.Red, Concept.White, Concept.Black, Concept.Cold,
    };
}
