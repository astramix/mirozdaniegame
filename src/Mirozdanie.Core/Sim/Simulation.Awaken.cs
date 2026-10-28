using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Переход особи → группы (docs/design/awakening-language.md).</summary>
public sealed partial class Simulation
{
    public void Awaken(Species sp, bool forced)
    {
        var w = World;
        var rng = w.Rng[StreamId.Awakening];
        sp.Sapient = true;
        w.SapientSpecies = sp.Id;
        w.AwakeningTick = w.LifeTick;
        w.Era = Era.History;

        // Особи вида уходят из пула: дальше вид живёт группами.
        var ag = w.Life.Agents;
        for (int i = 0; i < ag.Capacity; i++) if (ag.Alive[i] && ag.Species[i] == sp.Id) ag.Kill(i);

        // Праязык из анатомии вида.
        var proto = ProtoLanguage.Generate(sp, w.Rng[StreamId.Language]);
        proto.Id = 0;
        proto.BornYear = 0;
        proto.Autonym = proto.MakeName(proto.Compound(Concept.People, Concept.Speech), "речь народа");
        w.Languages.Add(proto);
        w.ProtoLanguage = 0;
        w.ProtoSnapshot = proto.Clone();
        bool great = rng.Chance(0.5);
        var worldWord = great ? proto.Compound(Concept.Great, Concept.Land) : proto.Compound(Concept.Mother, Concept.Earth);
        w.WorldName = proto.MakeName(worldWord, great ? "великая земля" : "мать-земля");

        // Места первых групп: клетки с наибольшей биомассой вида, на расстоянии ≥ 4.
        var cells = new List<int>(w.Life.LandCells);
        cells.Sort((a, b) => { int c = sp.Biomass[b].CompareTo(sp.Biomass[a]); return c != 0 ? c : a.CompareTo(b); });
        if (sp.Biomass[cells[0]] <= 0)
        {
            // Биомасса могла выродиться: тогда — по пригодности климата для вида.
            cells.Sort((a, b) =>
            {
                float va = Genome.ThermalSuitability(sp.Genome, w.Planet.ETemp[a]) * w.Planet.ENpp[a];
                float vb = Genome.ThermalSuitability(sp.Genome, w.Planet.ETemp[b]) * w.Planet.ENpp[b];
                int c = vb.CompareTo(va); return c != 0 ? c : a.CompareTo(b);
            });
        }
        int want = 6 + rng.NextInt(7);
        var chosen = new List<int>();
        foreach (var c in cells)
        {
            if (chosen.Count >= want) break;
            bool ok = true;
            foreach (var o in chosen) if (w.Planet.EcoDistance(c, o) < 4) { ok = false; break; }
            if (ok) chosen.Add(c);
        }

        w.InitialFauna = new float[w.Planet.EW * w.Planet.EH];
        foreach (var c in w.Life.LandCells) w.InitialFauna[c] = w.Life.FaunaAt(c);

        w.Year = 0;
        w.Events.CurrentYear = 0;
        w.Events.Publish(EventType.Awakening, chosen.Count > 0 ? chosen[0] : sp.OriginCell, a: sp.Id, b: chosen.Count,
            flags: forced ? EventFlags.Crisis : EventFlags.None);
        foreach (var c in chosen)
        {
            var g = NewGroup(c, rng.Range(60f, 150f) * Demo.Ref, -1, 0);
            InitGroupCulture(g, sp, rng);
        }

        // Периодические кометы (planet.md, «Динамика»).
        int comets = 1 + rng.NextInt(2);
        for (int k = 0; k < comets; k++)
        {
            int period = rng.Range(40, 121);
            w.Comets.Add(new Comet { Id = k, Period = period, NextYear = rng.Range(5, period) });
        }
        UpdateTerritories();
        w.Events.Dispatch();
    }

    public Group NewGroup(int cell, float pop, int parent, long year)
    {
        var w = World;
        var g = new Group { Id = w.Groups.Count, Cell = cell, Pop = pop, Parent = parent, Founded = year };
        if (parent >= 0)
        {
            var p = w.Groups[parent];
            g.LanguageId = p.LanguageId;
            g.ReligionId = p.ReligionId;
            Array.Copy(p.Culture, g.Culture, g.Culture.Length);
            Array.Copy(p.Belief, g.Belief, g.Belief.Length);
            g.B = p.B; g.P = p.P; g.G = p.G * 0.5f; g.N = p.N; g.D = p.D * 0.5f;
            g.Knowledge = p.Knowledge * 0.9f;
            g.Agriculture = p.Agriculture && w.Planet.EDomesticable[cell];
            g.Writing = p.Writing; g.Astronomy = p.Astronomy; g.Metallurgy = p.Metallurgy;
            g.Techs = p.Techs;
            if (!g.Agriculture) g.Techs &= ~(1UL << (int)Tech.Agriculture);
            g.Watts = p.Watts; g.EraIdx = p.EraIdx;
            g.Attitude = p.Attitude; g.AttitudeInit = p.AttitudeInit;
            foreach (var e in p.Epithets) g.Epithets.Add(e);
            g.LastCreatorSeen = p.LastCreatorSeen;
            g.AnomalyLog.AddRange(p.AnomalyLog);
            g.LastCrisis = p.LastCrisis;
            g.Taboo = p.Taboo; g.TabooStrength = p.TabooStrength * 0.7f;
            var rng = w.Rng[StreamId.Culture];
            for (int i = 0; i < g.Culture.Length; i++) g.Culture[i] = Math.Clamp(g.Culture[i] + rng.NextGaussian() * 0.04f, 0, 1);
            for (int i = 0; i < g.Belief.Length; i++) g.Belief[i] = Math.Clamp(g.Belief[i] + rng.NextGaussian() * 0.03f, 0, 1);
        }
        g.Name = UniqueGroupName(g, cell);
        g.Subsistence = ChooseSubsistence(g);
        w.Groups.Add(g);
        w.CellOwner[cell] = g.Id;
        if (parent < 0) w.Events.Publish(EventType.GroupFormed, cell, a: g.Id);
        return g;
    }

    /// <summary>Имя группы, не совпадающее с уже существующими (иначе добавляется второе понятие).</summary>
    private Name UniqueGroupName(Group g, int cell)
    {
        var w = World;
        var L = w.Languages[g.LanguageId];
        var rng = w.Rng[StreamId.Naming];
        var name = w.GroupName(L, cell, rng);
        Concept[] extra = { Concept.New, Concept.Red, Concept.White, Concept.Black, Concept.Strong, Concept.Small, Concept.Star, Concept.Stone, Concept.Bird, Concept.Wind, Concept.Moon, Concept.Clear };
        for (int attempt = 0; attempt < 24 && Taken(name.Native); attempt++)
        {
            var c = extra[rng.NextInt(extra.Length)];
            var baseName = w.GroupName(L, cell, rng);
            var word = L.Compound(c, Concept.People);
            if (attempt >= 6) word = L.WithSuffix(L.Compound(c, attempt % 2 == 0 ? Concept.River : Concept.Land), Concept.SuffixPeople);
            name = L.MakeName(word, $"{baseName.Gloss.Replace("люди", "люди")}, {Concepts.Russian[(int)c]}");
        }
        return name;
    }

    private bool Taken(string native)
    {
        foreach (var o in World.Groups) if (o.Name.Native == native) return true;
        return false;
    }

    /// <summary>Культура первых групп выводится из генома вида и среды (awakening-language.md).</summary>
    private void InitGroupCulture(Group g, Species sp, Rng rng)
    {
        var w = World;
        var gn = sp.Genome;
        float season = Math.Clamp(w.Planet.ESeason[g.Cell] / 15f, 0, 1);
        float coop = w.Params.CooperationBias;
        g.Culture[CultureTrait.Aggression] = Math.Clamp(0.25f + 0.5f * gn[Trait.Diet] - 0.2f * coop + rng.NextGaussian() * 0.1f, 0, 1);
        g.Culture[CultureTrait.Collectivism] = Math.Clamp(0.3f + 0.5f * gn[Trait.Sociality] + 0.15f * coop + rng.NextGaussian() * 0.1f, 0, 1);
        g.Culture[CultureTrait.Hierarchy] = Math.Clamp(0.25f + 0.3f * gn[Trait.Mass] / 3.5f + rng.NextGaussian() * 0.12f, 0, 1);
        g.Culture[CultureTrait.Openness] = Math.Clamp(0.5f + 0.2f * coop + rng.NextGaussian() * 0.15f, 0, 1);
        g.Culture[CultureTrait.KnowledgeLove] = Math.Clamp(0.3f + 0.4f * gn[Trait.Cognition] + rng.NextGaussian() * 0.12f, 0, 1);
        // Набожность выше в непредсказуемой среде (Malinowski 1948; спорно — см. NOTES).
        g.Culture[CultureTrait.Piety] = Math.Clamp(0.35f + 0.35f * season + rng.NextGaussian() * 0.12f, 0, 1);
        g.Belief[Doctrine.Benevolence] = Math.Clamp(0.5f + rng.NextGaussian() * 0.08f, 0, 1);
        g.Belief[Doctrine.Wrathfulness] = Math.Clamp(0.3f + rng.NextGaussian() * 0.08f, 0, 1);
        g.Belief[Doctrine.Moralism] = Math.Clamp(0.2f + rng.NextGaussian() * 0.08f, 0, 1);
        g.Belief[Doctrine.Ritual] = Math.Clamp(0.3f + 0.3f * g.Piety + rng.NextGaussian() * 0.08f, 0, 1);
        g.Belief[Doctrine.Monotheism] = Math.Clamp(0.3f + rng.NextGaussian() * 0.1f, 0, 1);
        g.Belief[Doctrine.Knowledge] = g.Culture[CultureTrait.KnowledgeLove];
        g.Belief[Doctrine.Violence] = g.Culture[CultureTrait.Aggression];
        g.Belief[Doctrine.Proselytism] = Math.Clamp(0.3f + 0.3f * g.Culture[CultureTrait.Openness] + rng.NextGaussian() * 0.08f, 0, 1);
        g.Knowledge = 0.03f + 0.05f * gn[Trait.Cognition];
        g.P = 0.1f;
        g.N = 0.05f;
    }

    public Subsistence ChooseSubsistence(Group g)
    {
        var p = World.Planet;
        int c = g.Cell;
        if (g.Agriculture) return Subsistence.Farming;
        if (p.ECoast[c] && p.EFish[c] > 0.5f || p.ERiver[c] > 0.3f) return Subsistence.Fishing;
        if (p.EBiome[c] is Planet.Biome.Grassland or Planet.Biome.Savanna or Planet.Biome.Shrubland && World.Life.FaunaAt(c) > 0.3f)
            return Subsistence.Herding;
        return Subsistence.Foraging;
    }
}
