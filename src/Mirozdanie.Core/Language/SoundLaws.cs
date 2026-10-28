using Mirozdanie.Core.Random;
using static Mirozdanie.Core.Language.Phonemes;

namespace Mirozdanie.Core.Language;

/// <summary>
/// Регулярные звуковые законы (младограмматическая гипотеза о регулярности, Osthoff &amp; Brugmann 1878).
/// Применяются ко всему словарю. См. docs/design/awakening-language.md.
/// </summary>
public static class SoundLaws
{
    public const int FinalDevoicing = 0, IntervocalicVoicing = 1, Palatalization = 2, Lenition = 3, FinalVowelLoss = 4,
        Syncope = 5, VowelShift = 6, ClusterSimplification = 7, Debuccalization = 8, NasalAssimilation = 9, Count = 10;

    public static readonly string[] Russian =
    {
        "оглушение конечных звонких",
        "озвончение между гласными",
        "палатализация к, г перед и, е",
        "ослабление смычных (п→ф, т→с, к→х)",
        "выпадение конечного безударного гласного",
        "выпадение срединного гласного",
        "сдвиг гласных (а→о)",
        "упрощение начальных групп согласных",
        "переход с в һ",
        "уподобление носовых (н→м перед п, б)",
    };

    private static int Devoice(int p) => p switch { B => P, D => T, G => K, Z => S, Zh => Sh, V => F, Gh => X, Dzh => Ch, _ => p };
    private static int Voice(int p) => p switch { P => B, T => D, K => G, S => Z, Sh => Zh, F => V, X => Gh, Ch => Dzh, _ => p };

    public static int[] Apply(int law, int[] w, Stress stress)
    {
        var r = new List<int>(w);
        switch (law)
        {
            case FinalDevoicing:
                if (r.Count > 1 && !IsVowel(r[^1])) r[^1] = Devoice(r[^1]);
                break;
            case IntervocalicVoicing:
                for (int i = 1; i < r.Count - 1; i++)
                    if (IsVowel(r[i - 1]) && IsVowel(r[i + 1])) r[i] = Voice(r[i]);
                break;
            case Palatalization:
                for (int i = 0; i < r.Count - 1; i++)
                    if ((r[i] == K || r[i] == G) && (r[i + 1] == I || r[i + 1] == E)) r[i] = r[i] == K ? Ch : Dzh;
                break;
            case Lenition:
                for (int i = 1; i < r.Count; i++)
                    if (IsVowel(r[i - 1])) r[i] = r[i] switch { P => F, T => S, K => X, B => V, _ => r[i] };
                break;
            case FinalVowelLoss:
                if (stress != Stress.Final && r.Count >= 4 && IsVowel(r[^1]) && !IsVowel(r[^2])) r.RemoveAt(r.Count - 1);
                break;
            case Syncope:
                for (int i = 2; i < r.Count - 2; i++)
                    if (IsVowel(r[i]) && !IsVowel(r[i - 1]) && !IsVowel(r[i + 1]) && IsVowel(r[i - 2]) && IsVowel(r[i + 2]))
                    { r.RemoveAt(i); break; }
                break;
            case VowelShift:
                // Цепной сдвиг без слияния: a→o, только если o в слове нет (слияние гласных уничтожило бы контрасты).
                for (int i = 0; i < r.Count; i++) r[i] = r[i] switch { A => O, _ => r[i] };
                break;
            case ClusterSimplification:
                if (r.Count > 2 && !IsVowel(r[0]) && !IsVowel(r[1])) r.RemoveAt(1);
                break;
            case Debuccalization:
                for (int i = 0; i < r.Count; i++) if (r[i] == S) r[i] = H;
                break;
            case NasalAssimilation:
                for (int i = 0; i < r.Count - 1; i++) if (r[i] == N && (r[i + 1] == P || r[i + 1] == B)) r[i] = M;
                break;
        }
        if (r.Count == 0 || !r.Exists(IsVowel)) return w; // слово не должно исчезнуть
        return r.ToArray();
    }

    /// <summary>Закон применим, если он изменит хотя бы 3 слова словаря.</summary>
    public static bool Useful(Lang L, int law)
    {
        int changed = 0;
        foreach (var w in L.Lexicon)
        {
            var nw = Apply(law, w, L.Stress);
            if (!nw.AsSpan().SequenceEqual(w)) changed++;
            if (changed >= 3) return true;
        }
        return false;
    }

    public static bool ApplyRandom(Lang L, Rng rng, out int law)
    {
        law = -1;
        int start = rng.NextInt(Count);
        for (int k = 0; k < Count; k++)
        {
            int cand = (start + k) % Count;
            if (L.Laws.Contains(cand)) continue;
            if (cand == VowelShift && L.Inventory[O]) continue; // не сливаем a и o
            if (!Useful(L, cand)) continue;
            for (int c = 0; c < L.Lexicon.Length; c++) L.Lexicon[c] = Apply(cand, L.Lexicon[c], L.Stress);
            L.Laws.Add(cand);
            L.RecomputeInventory();
            law = cand;
            return true;
        }
        return false;
    }

    /// <summary>Нормированная схожесть двух слов (1 − Левенштейн/длина).</summary>
    public static float Similarity(int[] a, int[] b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0) return 0;
        Span<int> prev = stackalloc int[m + 1];
        Span<int> cur = stackalloc int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            cur.CopyTo(prev);
        }
        return 1f - prev[m] / (float)Math.Max(n, m);
    }
}
