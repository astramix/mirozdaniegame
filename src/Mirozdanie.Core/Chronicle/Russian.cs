namespace Mirozdanie.Core.Chronicle;

/// <summary>Мелкие правила русского языка для шаблонов Хроники.</summary>
public static class Russian
{
    /// <summary>Согласование с числительным: 1 год, 2 года, 5 лет.</summary>
    public static string Plural(long n, string one, string few, string many)
    {
        long a = Math.Abs(n) % 100;
        if (a is >= 11 and <= 14) return many;
        return (a % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    public static string Years(long n) => $"{n} {Plural(n, "год", "года", "лет")}";

    public static string Number(double v)
    {
        if (v >= 1_000_000_000) return $"{v / 1_000_000_000:0.#} млрд".Replace('.', ',');
        if (v >= 1_000_000) return $"{v / 1_000_000:0.#} млн".Replace('.', ',');
        if (v >= 10_000) return $"{Math.Round(v / 1000):0} тыс.";
        if (v >= 1000) return $"{Math.Round(v / 100) * 100:0}";
        return $"{Math.Round(v):0}";
    }

    public static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Percent(float f) => $"{Math.Round(f * 100):0} %";

    /// <summary>Перечисление «А, Б и В».</summary>
    public static string List(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return "";
        if (items.Count == 1) return items[0];
        return string.Join(", ", items.Take(items.Count - 1)) + " и " + items[^1];
    }

    public enum Case : byte { Nom, Gen, Dat, Acc, Ins, Prep }

    /// <summary>
    /// Склонение процедурного имени по окончанию (lore.md §7.1; упрощённые правила по Зализняку 1977):
    /// согласный — мужской род («Хогагоѳ — Хогагоѳа»), «-а» — женский, «-я», «-й», «-ь»;
    /// на «-и, -у, -о, -е, -ю, -ы, -э, -ў» и на «-ия/-ья» после гласной — не склоняются.
    /// Одушевлённость влияет на винительный падеж мужского рода.
    /// </summary>
    public static string Decline(string name, Case c, bool animate = false)
    {
        if (string.IsNullOrEmpty(name) || c == Case.Nom) return name;
        char last = char.ToLowerInvariant(name[^1]);
        string stem = name[..^1];
        char prev = name.Length >= 2 ? char.ToLowerInvariant(name[^2]) : ' ';
        bool hush = "жшчщц".IndexOf(prev) >= 0;
        bool velar = "гкхжшчщ".IndexOf(prev) >= 0;
        bool hushLast = "жшчщц".IndexOf(last) >= 0;
        if (last == 'а')
            return c switch
            {
                Case.Gen => stem + (velar ? "и" : "ы"), Case.Dat or Case.Prep => stem + "е", Case.Acc => stem + "у",
                Case.Ins => stem + (hush ? "ей" : "ой"), _ => name,
            };
        if (last == 'я')
        {
            if ("иь".IndexOf(prev) >= 0 && name.Length > 3) return name; // «-ия»: оставляем как есть, чтобы не ошибиться
            return c switch { Case.Gen => stem + "и", Case.Dat or Case.Prep => stem + "е", Case.Acc => stem + "ю", Case.Ins => stem + "ей", _ => name };
        }
        if (last is 'й' or 'ь')
            return c switch
            {
                Case.Gen => stem + "я", Case.Dat => stem + "ю", Case.Acc => animate ? stem + "я" : name, Case.Ins => stem + "ем", Case.Prep => stem + "е", _ => name,
            };
        if ("аеёиоуыэюяў".IndexOf(last) >= 0 || !char.IsLetter(last)) return name;
        // Согласный: мужской род.
        return c switch
        {
            Case.Gen => name + "а", Case.Dat => name + "у", Case.Acc => animate ? name + "а" : name,
            Case.Ins => name + (hushLast ? "ем" : "ом"), Case.Prep => name + "е", _ => name,
        };
    }
}
