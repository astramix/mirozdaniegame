using Mirozdanie.Core.Language;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;
using Xunit;
using static Mirozdanie.Core.Language.Phonemes;

namespace Mirozdanie.Core.Tests;

public class LifeTests
{
    [Fact]
    public void KleiberThreeQuartersPower()
    {
        float r = Genome.KleiberWatts(10f) / Genome.KleiberWatts(1f);
        Approx.Equal(MathF.Pow(10f, 0.75f), r, 4);
        // 70 кг ≈ 82 Вт (порядок величины для млекопитающих).
        Assert.InRange(Genome.KleiberWatts(70f), 70f, 95f);
    }

    [Fact]
    public void GrowthRateScalesAsMinusQuarter()
    {
        float r1 = Genome.MaxGrowthRate(1f, 0.5f), r16 = Genome.MaxGrowthRate(16f, 0.5f);
        Approx.Equal(0.5f, r16 / r1, 3);
    }

    [Fact]
    public void BergmannMechanism()
    {
        var big = new float[Genome.G]; var small = new float[Genome.G];
        big[Trait.Mass] = 3f; small[Trait.Mass] = -1f;
        big[Trait.ThermalOpt] = small[Trait.ThermalOpt] = 15f;
        big[Trait.ThermalWidth] = small[Trait.ThermalWidth] = 8f;
        // В холоде крупным легче, в жаре — мелким.
        Assert.True(Genome.ThermalSuitability(big, 0f) > Genome.ThermalSuitability(small, 0f));
        Assert.True(Genome.ThermalSuitability(small, 30f) > Genome.ThermalSuitability(big, 30f));
    }

    private static PlanetData TinyPlanet()
    {
        var d = new PlanetData(8, 8, 2);
        for (int i = 0; i < d.EW * d.EH; i++) { d.ELand[i] = 1f; d.ENpp[i] = 1500f; d.ETemp[i] = 15f; d.ESeason[i] = 10f; }
        return d;
    }

    [Fact]
    public void PredatorPreyOscillate()
    {
        var d = TinyPlanet();
        var life = new LifeSystem(d, 100);
        for (int c = 0; c < 16; c++) life.Plant[c] = life.Capacity(c) * 0.8f;
        var herb = new float[Genome.G]; herb[Trait.Mass] = 0.3f; herb[Trait.Diet] = 0.05f; herb[Trait.ThermalOpt] = 15; herb[Trait.ThermalWidth] = 10; herb[Trait.Fecundity] = 0.7f;
        var carn = new float[Genome.G]; carn[Trait.Mass] = 1.2f; carn[Trait.Diet] = 0.95f; carn[Trait.ThermalOpt] = 15; carn[Trait.ThermalWidth] = 10; carn[Trait.Fecundity] = 0.5f;
        var h = life.NewSpecies(herb, -1, 0, 0);
        var p = life.NewSpecies(carn, -1, 0, 0);
        for (int c = 0; c < 16; c++) { h.Biomass[c] = 0.3f; p.Biomass[c] = 0.02f; }
        var zeros = new float[16]; var ones = Enumerable.Repeat(1f, 16).ToArray();
        var series = new List<float>();
        for (int t = 0; t < 300; t++) { life.EcologyTick(zeros, ones); series.Add(h.Biomass[5]); }
        Assert.True(h.TotalBiomass > 0 && p.TotalBiomass > 0, "оба вида должны выжить");
        // Колебания Лотки–Вольтерры: несколько локальных максимумов жертвы.
        int peaks = 0;
        for (int i = 1; i < series.Count - 1; i++) if (series[i] > series[i - 1] * 1.001f && series[i] > series[i + 1] * 1.001f) peaks++;
        Assert.True(peaks >= 2, $"пиков: {peaks}");
    }
}

public class LanguageTests
{
    [Fact]
    public void PalatalizationExact() => Assert.Equal(new[] { Ch, I, T, I }, SoundLaws.Apply(SoundLaws.Palatalization, new[] { K, I, T, I }, Stress.Initial));

    [Fact]
    public void FinalDevoicing() => Assert.Equal(new[] { B, A, T }, SoundLaws.Apply(SoundLaws.FinalDevoicing, new[] { B, A, D }, Stress.Initial));

    [Fact]
    public void IntervocalicVoicing() => Assert.Equal(new[] { A, B, A }, SoundLaws.Apply(SoundLaws.IntervocalicVoicing, new[] { A, P, A }, Stress.Initial));

    [Fact]
    public void Lenition() => Assert.Equal(new[] { A, F, A }, SoundLaws.Apply(SoundLaws.Lenition, new[] { A, P, A }, Stress.Initial));

    [Fact]
    public void WordNeverDisappears()
    {
        for (int law = 0; law < SoundLaws.Count; law++)
            Assert.NotEmpty(SoundLaws.Apply(law, new[] { A }, Stress.Final));
    }

    private static Species SpeciesWith(float lips, float larynx, ulong seed)
    {
        var s = new Species();
        var r = new Rng(seed);
        for (int t = 0; t < Genome.G; t++) s.Genome[t] = Genome.Min[t] + r.NextFloat() * (Genome.Max[t] - Genome.Min[t]);
        s.Genome[Trait.Lips] = lips;
        s.Genome[Trait.LarynxDepth] = larynx;
        return s;
    }

    [Fact]
    public void NoLipsNoLabials()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var L = ProtoLanguage.Generate(SpeciesWith(0.1f, 0.5f, seed), new Rng(seed));
            foreach (var w in L.Lexicon)
                foreach (var p in w)
                    Assert.False(All[p].Place == Place.Labial && All[p].Manner != Manner.Glide, $"губной звук {All[p].Ipa} у вида без губ");
        }
    }

    [Fact]
    public void ShallowLarynxHasThreeVowels()
    {
        var L = ProtoLanguage.Generate(SpeciesWith(0.8f, 0.1f, 3), new Rng(3));
        Assert.Equal(3, L.Vowels().Count);
    }

    [Fact]
    public void GlottochronologyRetention()
    {
        // За 1000 лет при λ = −ln(0,86)/1000 доля сохранённых корней ≈ 0,86 (Lees 1953).
        var rng = new Rng(11);
        double total = 0;
        int runs = 400, words = 100;
        for (int r = 0; r < runs; r++)
        {
            int kept = 0;
            for (int w = 0; w < words; w++)
            {
                bool alive = true;
                for (int y = 0; y < 1000 && alive; y++) if (rng.NextDouble() < Sim.Simulation.ReplacementRate) alive = false;
                if (alive) kept++;
            }
            total += kept / (double)words;
        }
        Assert.InRange(total / runs, 0.84, 0.88);
    }

    [Fact]
    public void ProtoLanguageDeterministic()
    {
        var s = SpeciesWith(0.6f, 0.6f, 5);
        var a = ProtoLanguage.Generate(s, new Rng(9));
        var b = ProtoLanguage.Generate(s, new Rng(9));
        for (int c = 0; c < a.Lexicon.Length; c++) Assert.Equal(a.Lexicon[c], b.Lexicon[c]);
    }

    [Fact]
    public void DifferentSpeciesDifferentInventories()
    {
        double jac = 0; int n = 0;
        for (ulong i = 0; i < 10; i++)
        {
            var a = ProtoLanguage.Generate(SpeciesWith(i % 2 == 0 ? 0.1f : 0.9f, 0.2f + 0.07f * i, i), new Rng(i));
            var b = ProtoLanguage.Generate(SpeciesWith(i % 3 == 0 ? 0.9f : 0.2f, 0.9f - 0.07f * i, i + 100), new Rng(i + 100));
            int inter = 0, uni = 0;
            for (int p = 0; p < Count; p++) { if (a.Inventory[p] && b.Inventory[p]) inter++; if (a.Inventory[p] || b.Inventory[p]) uni++; }
            jac += 1 - inter / (double)uni; n++;
        }
        Assert.True(jac / n > 0.2, $"Жаккар {jac / n}");
    }

    [Fact]
    public void CyrillicRendering() => Assert.Equal("Тавен", ToCyrillic(new[] { T, A, V, E, N }));
}
