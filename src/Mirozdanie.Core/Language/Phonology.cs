namespace Mirozdanie.Core.Language;

public enum Place : byte { Labial, Dental, Alveolar, PostAlveolar, Palatal, Velar, Uvular, Glottal, Vowel }
public enum Manner : byte { Stop, Fricative, Affricate, Nasal, Liquid, Glide, Vowel }

public readonly record struct Phoneme(string Ipa, string Lat, string Cyr, Place Place, Manner Manner, bool Voiced, bool Front = false);

/// <summary>Универсальный набор фонем. Языки берут из него подмножество (docs/design/awakening-language.md).</summary>
public static class Phonemes
{
    public static readonly Phoneme[] All =
    {
        new("p", "p", "п", Place.Labial, Manner.Stop, false),         // 0
        new("b", "b", "б", Place.Labial, Manner.Stop, true),          // 1
        new("t", "t", "т", Place.Alveolar, Manner.Stop, false),       // 2
        new("d", "d", "д", Place.Alveolar, Manner.Stop, true),        // 3
        new("k", "k", "к", Place.Velar, Manner.Stop, false),          // 4
        new("g", "g", "г", Place.Velar, Manner.Stop, true),           // 5
        new("q", "q", "къ", Place.Uvular, Manner.Stop, false),        // 6
        new("ʔ", "'", "ʼ", Place.Glottal, Manner.Stop, false),        // 7
        new("m", "m", "м", Place.Labial, Manner.Nasal, true),         // 8
        new("n", "n", "н", Place.Alveolar, Manner.Nasal, true),       // 9
        new("ŋ", "ng", "нг", Place.Velar, Manner.Nasal, true),        // 10
        new("f", "f", "ф", Place.Labial, Manner.Fricative, false),    // 11
        new("v", "v", "в", Place.Labial, Manner.Fricative, true),     // 12
        new("s", "s", "с", Place.Alveolar, Manner.Fricative, false),  // 13
        new("z", "z", "з", Place.Alveolar, Manner.Fricative, true),   // 14
        new("ʃ", "sh", "ш", Place.PostAlveolar, Manner.Fricative, false), // 15
        new("ʒ", "zh", "ж", Place.PostAlveolar, Manner.Fricative, true),  // 16
        new("x", "kh", "х", Place.Velar, Manner.Fricative, false),    // 17
        new("ɣ", "gh", "гх", Place.Velar, Manner.Fricative, true),    // 18
        new("h", "h", "һ", Place.Glottal, Manner.Fricative, false),   // 19
        new("θ", "th", "ѳ", Place.Dental, Manner.Fricative, false),   // 20
        new("ts", "ts", "ц", Place.Alveolar, Manner.Affricate, false),// 21
        new("tʃ", "ch", "ч", Place.PostAlveolar, Manner.Affricate, false), // 22
        new("dʒ", "j", "дж", Place.PostAlveolar, Manner.Affricate, true),  // 23
        new("l", "l", "л", Place.Alveolar, Manner.Liquid, true),      // 24
        new("r", "r", "р", Place.Alveolar, Manner.Liquid, true),      // 25
        new("j", "y", "й", Place.Palatal, Manner.Glide, true),        // 26
        new("w", "w", "ў", Place.Labial, Manner.Glide, true),         // 27
        new("χ", "qh", "хъ", Place.Uvular, Manner.Fricative, false),  // 28
        // Гласные
        new("a", "a", "а", Place.Vowel, Manner.Vowel, true),          // 29
        new("e", "e", "е", Place.Vowel, Manner.Vowel, true, true),    // 30
        new("i", "i", "и", Place.Vowel, Manner.Vowel, true, true),    // 31
        new("o", "o", "о", Place.Vowel, Manner.Vowel, true),          // 32
        new("u", "u", "у", Place.Vowel, Manner.Vowel, true),          // 33
        new("ɨ", "y", "ы", Place.Vowel, Manner.Vowel, true),          // 34
        new("y", "ü", "ю", Place.Vowel, Manner.Vowel, true, true),    // 35
    };

    public const int P = 0, B = 1, T = 2, D = 3, K = 4, G = 5, Q = 6, Glottal = 7, M = 8, N = 9, Ng = 10, F = 11, V = 12,
        S = 13, Z = 14, Sh = 15, Zh = 16, X = 17, Gh = 18, H = 19, Th = 20, Ts = 21, Ch = 22, Dzh = 23, L = 24, R = 25,
        J = 26, W = 27, Chi = 28, A = 29, E = 30, I = 31, O = 32, U = 33, Y = 34, Ue = 35;

    public static int Count => All.Length;
    public static bool IsVowel(int p) => All[p].Manner == Manner.Vowel;

    /// <summary>Кириллическая запись слова с заглавной буквы (для имён в Хронике).</summary>
    public static string ToCyrillic(ReadOnlySpan<int> word, bool capitalize = true)
    {
        var sb = new System.Text.StringBuilder(word.Length * 2);
        for (int k = 0; k < word.Length; k++)
        {
            int p = word[k];
            string s = All[p].Cyr;
            // «э» в начале слова и после гласной, «е» после согласной (как в русской передаче экзотических имён).
            if (p == E && (k == 0 || IsVowel(word[k - 1]))) s = "э";
            // Звонкие перед глухими и т. п. не трогаем: запись фонематическая.
            sb.Append(s);
        }
        if (capitalize && sb.Length > 0) sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    public static string ToLatin(ReadOnlySpan<int> word, bool capitalize = true)
    {
        var sb = new System.Text.StringBuilder(word.Length * 2);
        foreach (var p in word) sb.Append(All[p].Lat);
        if (capitalize && sb.Length > 0) sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    public static string ToIpa(ReadOnlySpan<int> word)
    {
        var sb = new System.Text.StringBuilder(word.Length * 2);
        foreach (var p in word) sb.Append(All[p].Ipa);
        return sb.ToString();
    }
}
