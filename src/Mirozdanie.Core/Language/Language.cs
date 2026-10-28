using Mirozdanie.Core.Life;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Language;

public enum Stress : byte { Initial, Penultimate, Final }
public enum WordOrder : byte { SOV, SVO, VSO }

/// <summary>Имя на языке культуры с переводом: «Тавен-Ашур» (букв. «Бросающий огонь»).</summary>
public readonly record struct Name(string Native, string Gloss, string Latin = "")
{
    /// <summary>Фонемы имени (если известны) — для производных слов.</summary>
    public int[]? Word { get; init; }
    public override string ToString() => Native;
    public string WithGloss => string.IsNullOrEmpty(Gloss) ? Native : $"{Native} («{Gloss}»)";
    public static readonly Name Empty = new("", "");
}

public sealed class Lang
{
    public int Id;
    public int Parent = -1;
    public long BornYear;
    public int FounderGroup = -1;
    public Name Autonym;
    public readonly bool[] Inventory = new bool[Phonemes.Count];
    public float ClusterRate;   // вероятность группы согласных в начале слога
    public float CodaRate;      // вероятность закрытого слога
    public Stress Stress;
    public WordOrder Order;
    /// <summary>Определение перед определяемым (огонь-бросающий) или после.</summary>
    public bool ModifierFirst;
    public int[][] Lexicon = new int[(int)Concept.Count][];
    public readonly List<string> History = new(); // применённые звуковые законы (для Хроники)
    public readonly List<int> Laws = new();
    public int Speakers;
    public bool Alive = true;

    public int[] Word(Concept c) => Lexicon[(int)c];

    public Lang Clone()
    {
        var c = new Lang { Id = Id, Parent = Parent, BornYear = BornYear, FounderGroup = FounderGroup, Autonym = Autonym, ClusterRate = ClusterRate,
            CodaRate = CodaRate, Stress = Stress, Order = Order, ModifierFirst = ModifierFirst, Alive = Alive };
        for (int i = 0; i < Lexicon.Length; i++) c.Lexicon[i] = (int[])Lexicon[i].Clone();
        Array.Copy(Inventory, c.Inventory, Inventory.Length);
        c.Laws.AddRange(Laws);
        return c;
    }
    public string Cyr(Concept c, bool cap = true) => Phonemes.ToCyrillic(Lexicon[(int)c], cap);

    public List<int> Consonants()
    {
        var l = new List<int>();
        for (int p = 0; p < Phonemes.Count; p++) if (Inventory[p] && !Phonemes.IsVowel(p)) l.Add(p);
        return l;
    }

    public List<int> Vowels()
    {
        var l = new List<int>();
        for (int p = 0; p < Phonemes.Count; p++) if (Inventory[p] && Phonemes.IsVowel(p)) l.Add(p);
        return l;
    }

    public void RecomputeInventory()
    {
        Array.Clear(Inventory);
        foreach (var w in Lexicon) foreach (var p in w) Inventory[p] = true;
    }

    /// <summary>Составное слово из корней (с соединительной гласной при стыке согласных).</summary>
    public int[] Compound(params Concept[] parts)
    {
        var res = new List<int>();
        IEnumerable<Concept> seq = ModifierFirst ? parts : parts.Reverse();
        foreach (var c in seq)
        {
            var w = Lexicon[(int)c];
            if (res.Count > 0 && !Phonemes.IsVowel(res[^1]) && w.Length > 0 && !Phonemes.IsVowel(w[0]))
                res.Add(LinkVowel());
            res.AddRange(w);
        }
        return res.ToArray();
    }

    public int LinkVowel()
    {
        if (Inventory[Phonemes.A]) return Phonemes.A;
        if (Inventory[Phonemes.E]) return Phonemes.E;
        for (int p = 0; p < Phonemes.Count; p++) if (Inventory[p] && Phonemes.IsVowel(p)) return p;
        return Phonemes.A;
    }

    /// <summary>Имя с суффиксом (лицо/место/народ).</summary>
    public int[] WithSuffix(int[] stem, Concept suffix)
    {
        var s = Lexicon[(int)suffix];
        var res = new List<int>(stem);
        if (res.Count > 0 && s.Length > 0 && Phonemes.IsVowel(res[^1]) == Phonemes.IsVowel(s[0]))
        {
            if (Phonemes.IsVowel(s[0])) res.RemoveAt(res.Count - 1); // слияние гласных
        }
        res.AddRange(s);
        return res.ToArray();
    }

    public Name MakeName(int[] word, string gloss) => new(Phonemes.ToCyrillic(word), gloss, Phonemes.ToLatin(word)) { Word = word };
}

/// <summary>Генерация праязыка из анатомии вида (docs/design/awakening-language.md, «Фонология праязыка»).</summary>
public static class ProtoLanguage
{
    public static Lang Generate(Species species, Rng rng)
    {
        var g = species.Genome;
        var L = new Lang();
        float lips = g[Trait.Lips], tongue = g[Trait.TongueAgility], nasal = g[Trait.Nasality], larynx = g[Trait.LarynxDepth];
        float mass = g[Trait.Mass], cog = g[Trait.Cognition];

        // Согласные: базовая вероятность растёт с когнитивностью (вкусовой параметр, см. NOTES).
        float baseP = 0.45f + 0.3f * cog;
        for (int p = 0; p < Phonemes.Count; p++)
        {
            var ph = Phonemes.All[p];
            if (ph.Manner == Manner.Vowel) continue;
            float prob = baseP;
            if (ph.Place == Place.Labial && ph.Manner != Manner.Glide) prob *= lips < 0.3f ? 0f : 0.5f + lips;
            if (ph.Manner == Manner.Nasal) prob *= nasal < 0.2f ? (p == Phonemes.N ? 1f : 0f) : 0.6f + nasal;
            if (ph.Manner == Manner.Liquid) prob *= tongue < 0.4f ? (p == Phonemes.L ? 0.6f : 0.05f) : 0.5f + tongue;
            if (ph.Place is Place.Dental or Place.PostAlveolar) prob *= tongue < 0.4f ? 0.15f : 0.4f + tongue;
            if (ph.Place == Place.Uvular) prob *= mass > 1.5f ? 0.9f : 0.1f;
            if (ph.Place == Place.Glottal) prob *= 0.5f;
            if (ph.Manner == Manner.Affricate) prob *= 0.5f;
            if (ph.Voiced && ph.Manner is Manner.Stop or Manner.Fricative) prob *= 0.7f;
            if (rng.NextFloat() < prob) L.Inventory[p] = true;
        }
        // Гарантированный минимум: хотя бы по одной смычной, фрикативной и сонорной.
        L.Inventory[lips < 0.3f ? Phonemes.T : Phonemes.P] = true;
        L.Inventory[Phonemes.K] = true;
        if (!L.Inventory[Phonemes.S] && !L.Inventory[Phonemes.H] && !L.Inventory[Phonemes.X]) L.Inventory[rng.Chance(0.5) ? Phonemes.S : Phonemes.H] = true;
        if (!L.Inventory[Phonemes.N] && !L.Inventory[Phonemes.M]) L.Inventory[Phonemes.N] = true;

        // Гласные: глубокая гортань — больше гласных (Lieberman 1984 о форме речевого тракта; упрощено).
        L.Inventory[Phonemes.A] = L.Inventory[Phonemes.I] = L.Inventory[Phonemes.U] = true;
        if (larynx >= 0.4f)
        {
            if (rng.NextFloat() < 0.4f + larynx) L.Inventory[Phonemes.E] = true;
            if (rng.NextFloat() < 0.4f + larynx) L.Inventory[Phonemes.O] = true;
            if (rng.NextFloat() < larynx * 0.5f) L.Inventory[Phonemes.Y] = true;
            if (rng.NextFloat() < larynx * 0.35f) L.Inventory[Phonemes.Ue] = true;
        }

        L.ClusterRate = Math.Clamp(0.05f + 0.3f * tongue * rng.NextFloat(), 0, 0.35f);
        L.CodaRate = Math.Clamp(0.15f + 0.5f * rng.NextFloat(), 0, 0.7f);
        L.Stress = (Stress)rng.NextInt(3);
        L.Order = (WordOrder)rng.NextInt(3);
        // Корреляция Гринберга: в OV-языках определение чаще перед определяемым (Greenberg 1963).
        L.ModifierFirst = L.Order == WordOrder.SOV ? rng.Chance(0.8) : rng.Chance(0.3);

        for (int c = 0; c < (int)Concept.Count; c++) L.Lexicon[c] = NewRoot(L, (Concept)c, rng);
        // Однообразие суффиксов: суффикс — один короткий слог.
        L.RecomputeInventory();
        return L;
    }

    public static int[] NewRoot(Lang L, Concept c, Rng rng)
    {
        var cons = L.Consonants();
        var vows = L.Vowels();
        int sym = Concepts.Symbolism(c);
        int syll = c >= Concept.SuffixPerson ? 1 : Concepts.Short(c) ? 1 : (rng.Chance(0.55) ? 2 : (rng.Chance(0.75) ? 1 : 3));
        var w = new List<int>(8);
        for (int s = 0; s < syll; s++)
        {
            bool suffix = c >= Concept.SuffixPerson;
            // Начало слога.
            if (!(suffix && rng.Chance(0.6)) && (s > 0 || rng.Chance(0.85)))
            {
                w.Add(cons[rng.NextInt(cons.Count)]);
                if (rng.NextFloat() < L.ClusterRate && w.Count == 1)
                {
                    // Вторая согласная — сонорная (как в «кр», «пл»).
                    int son = L.Inventory[Phonemes.R] ? Phonemes.R : L.Inventory[Phonemes.L] ? Phonemes.L : -1;
                    if (son >= 0 && Phonemes.All[w[^1]].Manner is Manner.Stop or Manner.Fricative) w.Add(son);
                }
            }
            w.Add(PickVowel(vows, sym, rng));
            if (rng.NextFloat() < L.CodaRate && (s == syll - 1 || rng.Chance(0.3)))
            {
                int coda = cons[rng.NextInt(cons.Count)];
                if (w.Count == 0 || w[^1] != coda) w.Add(coda);
            }
        }
        return w.ToArray();
    }

    private static int PickVowel(List<int> vows, int sym, Rng rng)
    {
        if (sym != 0 && rng.Chance(0.7))
        {
            var pool = new List<int>();
            foreach (var v in vows) if (Phonemes.All[v].Front == (sym < 0) && (sym < 0 || v != Phonemes.A || true)) pool.Add(v);
            if (sym > 0) pool.RemoveAll(v => v == Phonemes.I || v == Phonemes.E);
            if (pool.Count > 0) return pool[rng.NextInt(pool.Count)];
        }
        return vows[rng.NextInt(vows.Count)];
    }
}
