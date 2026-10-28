using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Лестница Кардашёва (docs/design/kardashev.md, §10).</summary>
public class KardashevTests
{
    private sealed record Run(Simulation Sim, float[] K, double[] Fossil);

    // Три мира по 4000 лет (у медленного мира пик ископаемых — к 3000-му): K и ископаемая мощность каждый год.
    private static readonly Lazy<List<Run>> Worlds = new(() =>
    {
        var list = new List<Run>();
        foreach (ulong seed in new ulong[] { 42, 7, 1000 })
        {
            var sim = Simulation.Create(seed, WorldParams.FromPreset("earth"));
            var k = new float[4000];
            var f = new double[4000];
            for (int y = 0; y < 4000; y++)
            {
                sim.Step();
                k[y] = sim.World.Kardashev;
                f[y] = sim.World.FossilWatts;
            }
            list.Add(new Run(sim, k, f));
        }
        return list;
    });

    [Fact]
    public void TechTableMatchesEnum()
    {
        Assert.Equal((int)Tech.Count, Technology.All.Length);
        for (int i = 0; i < Technology.All.Length; i++)
        {
            Assert.Equal((Tech)i, Technology.All[i].Id);
            foreach (var r in Technology.All[i].Requires)
                Assert.True((int)r < i, $"{Technology.All[i].Id} требует {r}, который стоит позже в таблице");
        }
        Assert.True((int)Tech.Count <= 64, "битовая маска ulong");
    }

    [Fact]
    public void GreenhouseDoublingMatchesForcing()
    {
        // 2×CO₂: ΔT = λ·5,35·ln 2 (Myhre et al. 1998); λ = 0,8 → ≈ 2,97 °C.
        Assert.InRange(Simulation.GreenhouseWarming(560, 0.8f), 2.87f, 3.07f);
        Assert.Equal(0f, Simulation.GreenhouseWarming(280, 0.8f));
        Assert.Equal(0f, Simulation.GreenhouseWarming(200, 0.8f));
    }

    [Fact]
    public void DemographicTransitionSlowsGrowth()
    {
        float pre = Simulation.GrowthRate(Subsistence.Farming, 1260f);
        float rich = Simulation.GrowthRate(Subsistence.Farming, 7000f);
        Assert.InRange(pre, 0.019f, 0.021f);
        Assert.True(rich < 0.005f, $"при 7 кВт прирост {rich:P2}");
        for (float w = 1000; w < 9000; w += 500)
            Assert.True(Simulation.GrowthRate(Subsistence.Farming, w + 500) <= Simulation.GrowthRate(Subsistence.Farming, w));
    }

    [Fact]
    public void EnergyLadderClimbs()
    {
        foreach (var run in Worlds.Value)
        {
            float k1 = run.K[999], k2 = run.K[1999], k3 = run.K[2999];
            Assert.True(k2 > k1 && k3 >= k2, $"сид {run.Sim.World.Seed}: K {k1:0.00} → {k2:0.00} → {k3:0.00}");
        }
    }

    [Fact]
    public void FossilUsePeaksAndDeclines()
    {
        // Кривая Хабберта (Hubbert 1956): добыча растёт, проходит пик и спадает, когда запасы и переход на чистое сокращают её.
        foreach (var run in Worlds.Value)
        {
            int peak = 0;
            for (int y = 1; y < run.Fossil.Length; y++) if (run.Fossil[y] > run.Fossil[peak]) peak = y;
            double max = run.Fossil[peak];
            Assert.True(max > 1e11, $"сид {run.Sim.World.Seed}: ископаемых не было ({max:0.0e0} Вт)");
            Assert.True(peak < run.Fossil.Length - 100, $"сид {run.Sim.World.Seed}: пик в самом конце ({peak})");
            Assert.True(run.Fossil[^1] < 0.5 * max, $"сид {run.Sim.World.Seed}: после пика не спало");
        }
    }

    [Fact]
    public void WorldsWarmButNotRunaway()
    {
        foreach (var run in Worlds.Value)
            Assert.InRange(run.Sim.World.Planet.GlobalTempDelta, -1f, 6f);
    }

    [Fact]
    public void NoCoalNoSteam()
    {
        var p = WorldParams.FromPreset("earth");
        p.ResourceRichness = 0f;
        var sim = Simulation.Create(42, p);
        sim.Run(2500);
        foreach (var g in sim.World.Groups)
        {
            Assert.False(g.Has(Tech.CoalMining), $"народ {g.Id} добывает уголь на мире без угля");
            Assert.False(g.Has(Tech.SteamEngine), $"народ {g.Id} построил паровую машину без угля");
        }
        Assert.True(sim.World.FossilWatts == 0);
        // Без ископаемых мир не застывает: вода и ветер у него остаются.
        Assert.Contains(sim.World.Groups, g => g.Has(Tech.WaterWheel) || g.Has(Tech.Windmill));
    }
}
