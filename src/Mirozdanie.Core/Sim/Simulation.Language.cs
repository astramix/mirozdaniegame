using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Дрейф языков: глоттохронология, звуковые законы, расколы, заимствования (awakening-language.md).</summary>
public sealed partial class Simulation
{
    /// <summary>Интенсивность замены слова базовой лексики: λ = −ln(0,86)/1000 в год (Lees 1953).</summary>
    public static readonly double ReplacementRate = -Math.Log(0.86) / 1000.0;
    public static double SoundLawRate = 1.0 / 150.0;
    public const int IsolationYears = 120;

    private void LanguageStage()
    {
        var w = World;
        var rng = w.Rng[StreamId.Language];
        foreach (var L in w.Languages) L.Speakers = 0;
        foreach (var g in w.Groups) if (g.Alive) w.Languages[g.LanguageId].Speakers++;
        int lc = w.Languages.Count;
        for (int li = 0; li < lc; li++)
        {
            var L = w.Languages[li];
            if (!L.Alive) continue;
            if (L.Speakers == 0) { L.Alive = false; continue; }
            int nConcepts = (int)Concept.SuffixPerson;
            // Пуассоновская замена: P(хоть одна замена в год) = 1 − e^{−λN}.
            if (rng.NextDouble() < 1 - Math.Exp(-ReplacementRate * nConcepts))
            {
                var c = (Concept)rng.NextInt(nConcepts);
                L.Lexicon[(int)c] = ProtoLanguage.NewRoot(L, c, rng);
                L.RecomputeInventory();
            }
            if (rng.NextDouble() < SoundLawRate && SoundLaws.ApplyRandom(L, rng, out int law))
            {
                L.History.Add($"{w.Year}: {SoundLaws.Russian[law]}");
                w.Events.Publish(EventType.SoundLawApplied, -1, a: L.Id, b: law, magnitude: 0.2f);
            }
        }
        if (w.Year % 10 == 0) LanguageSplits(rng);
        // Заимствования по торговым путям.
        if (w.Year % 10 == 7)
            foreach (var (a, b, t) in w.TradeRoutes)
            {
                var ga = w.Groups[a]; var gb = w.Groups[b];
                if (ga.LanguageId == gb.LanguageId || t < 0.6f || !rng.Chance(0.05)) continue;
                var (from, to) = Prestige(ga) >= Prestige(gb) ? (ga, gb) : (gb, ga);
                Concept[] tradeWords = { Concept.Grain, Concept.Bread, Concept.God, Concept.City, Concept.Gift, Concept.Stone };
                Borrow(w.Languages[to.LanguageId], w.Languages[from.LanguageId], tradeWords[rng.NextInt(tradeWords.Length)], rng);
            }
    }

    private void Borrow(Lang to, Lang from, Concept c, Rng rng)
    {
        if (to.Word(c).AsSpan().SequenceEqual(from.Word(c))) return;
        to.Lexicon[(int)c] = (int[])from.Word(c).Clone();
        to.RecomputeInventory();
        World.Events.Publish(EventType.WordBorrowed, -1, a: to.Id, b: from.Id, c: (int)c, magnitude: 0.15f);
    }

    /// <summary>Язык-потомок рождается у групп, изолированных от основной массы носителей дольше 120 лет.</summary>
    private void LanguageSplits(Rng rng)
    {
        var w = World;
        var p = w.Planet;
        int lc = w.Languages.Count;
        for (int li = 0; li < lc; li++)
        {
            var L = w.Languages[li];
            if (!L.Alive || L.Speakers < 2) continue;
            var members = new List<Group>();
            foreach (var g in w.Groups) if (g.Alive && g.LanguageId == L.Id) members.Add(g);
            // Компоненты связности (ребро — расстояние ≤ 6).
            var comp = new int[members.Count];
            Array.Fill(comp, -1);
            int nc = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < members.Count; i++)
            {
                if (comp[i] >= 0) continue;
                comp[i] = nc; stack.Push(i);
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    for (int j = 0; j < members.Count; j++)
                    {
                        if (comp[j] >= 0) continue;
                        if (p.EcoDistance(members[k].Cell, members[j].Cell) <= 6f) { comp[j] = nc; stack.Push(j); }
                    }
                }
                nc++;
            }
            // Основная компонента — та, где старейшая группа (наименьший id).
            int main = comp[0];
            if (nc == 1)
            {
                foreach (var g in members) g.IsolatedSince = -1;
                continue;
            }
            for (int c = 0; c < nc; c++)
            {
                if (c == main) continue;
                long since = long.MaxValue;
                Group? founder = null;
                for (int i = 0; i < members.Count; i++)
                {
                    if (comp[i] != c) continue;
                    var g = members[i];
                    if (g.IsolatedSince < 0) g.IsolatedSince = w.Year;
                    since = Math.Min(since, g.IsolatedSince);
                    if (founder == null || g.Pop > founder.Pop) founder = g;
                }
                if (founder == null || w.Year - since < IsolationYears) continue;
                var child = SplitLanguage(L, founder, rng);
                for (int i = 0; i < members.Count; i++)
                {
                    if (comp[i] != c) continue;
                    members[i].LanguageId = child.Id;
                    members[i].IsolatedSince = -1;
                }
            }
            for (int i = 0; i < members.Count; i++) if (comp[i] == main) members[i].IsolatedSince = -1;
        }
    }

    private Lang SplitLanguage(Lang parent, Group founder, Rng rng)
    {
        var w = World;
        var child = new Lang
        {
            Id = w.Languages.Count, Parent = parent.Id, BornYear = w.Year, FounderGroup = founder.Id,
            ClusterRate = parent.ClusterRate, CodaRate = parent.CodaRate, Stress = parent.Stress, Order = parent.Order, ModifierFirst = parent.ModifierFirst,
        };
        for (int c = 0; c < parent.Lexicon.Length; c++) child.Lexicon[c] = (int[])parent.Lexicon[c].Clone();
        child.Laws.AddRange(parent.Laws);
        child.RecomputeInventory();
        int laws = 1 + rng.NextInt(2);
        for (int k = 0; k < laws; k++)
            if (SoundLaws.ApplyRandom(child, rng, out int law)) child.History.Add($"{w.Year}: {SoundLaws.Russian[law]}");
        if (rng.Chance(0.2)) child.Stress = (Stress)rng.NextInt(3);
        child.Autonym = child.MakeName(child.Compound(Concept.People, Concept.Speech), "речь народа");
        w.Languages.Add(child);
        w.Events.Publish(EventType.LanguageSplit, founder.Cell, a: child.Id, b: parent.Id, c: founder.Id, magnitude: 0.4f);
        return child;
    }
}
