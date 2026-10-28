using System.Diagnostics;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;
using Xunit;

namespace Mirozdanie.Core.Tests;

public class PlanetTests
{
    private static readonly Lazy<PlanetData> Earth42 = new(() => PlanetGenerator.Generate(WorldParams.FromPreset("earth"), new RngStreams(42)[StreamId.Planet]));

    [Theory]
    // Контрольные точки диаграммы Уиттекера (planet.md).
    [InlineData(25f, 3000f, Biome.TropicalRainforest)]
    [InlineData(25f, 1800f, Biome.TropicalSeasonalForest)]
    [InlineData(25f, 800f, Biome.Savanna)]
    [InlineData(25f, 100f, Biome.Desert)]
    [InlineData(12f, 1200f, Biome.TemperateForest)]
    [InlineData(12f, 2500f, Biome.TemperateRainforest)]
    [InlineData(15f, 350f, Biome.Grassland)]
    [InlineData(10f, 700f, Biome.Shrubland)]
    [InlineData(-5f, 300f, Biome.Taiga)]
    [InlineData(0f, 150f, Biome.ColdDesert)]
    [InlineData(-6f, 200f, Biome.Tundra)]
    [InlineData(-20f, 100f, Biome.Ice)]
    public void WhittakerControlPoints(float t, float p, Biome expected) => Assert.Equal(expected, Biomes.Whittaker(t, p));

    [Fact]
    public void LapseRateIs6_5PerKm()
    {
        float t0 = PlanetGenerator.BaseTemperature(45f, 0f, 14f);
        float t2 = PlanetGenerator.BaseTemperature(45f, 2f, 14f);
        Approx.Equal(13f, t0 - t2, 3);
    }

    [Fact]
    public void EquatorWarmerThanPoles()
    {
        Assert.InRange(PlanetGenerator.BaseTemperature(0, 0, 14), 25, 29);
        Assert.InRange(PlanetGenerator.BaseTemperature(60, 0, 14), -5, 3);
        Assert.True(PlanetGenerator.BaseTemperature(85, 0, 14) < -10);
    }

    [Fact]
    public void RainShadowBehindRidge()
    {
        // Синтетическая строка: океан, затем хребет 3 км поперёк западных ветров (dir = +1), затем равнина.
        int w = 120;
        var elev = new float[w]; var temp = new float[w];
        for (int x = 0; x < w; x++)
        {
            elev[x] = x < 40 ? -2f : x is >= 60 and < 66 ? 3f : 0.2f;
            temp[x] = 15f;
        }
        var p = PlanetGenerator.PrecipitationRow(elev, temp, +1, 1000f);
        float windward = p[55], leeward = p[75];
        Assert.True(windward > leeward * 1.4f, $"наветренный {windward}, подветренный {leeward}");
    }

    [Fact]
    public void MiamiModelKnownValues()
    {
        // Lieth 1975: при 30 °C и 3000 мм NPP близка к 2900 г/м²/год; в пустыне — мала.
        Assert.InRange(Biomes.MiamiNpp(30, 3000), 2400, 3000);
        Assert.InRange(Biomes.MiamiNpp(25, 100), 0, 250);
    }

    [Fact]
    public void OceanFractionMatchesParameter()
    {
        var d = Earth42.Value;
        int ocean = 0;
        foreach (var e in d.Elevation) if (e < 0) ocean++;
        Assert.InRange(ocean / (float)d.Elevation.Length, 0.61f, 0.63f);
    }

    [Fact]
    public void RiversFlowDownhill()
    {
        var d = Earth42.Value;
        int rivers = 0;
        for (int i = 0; i < d.Elevation.Length; i++)
        {
            if (!d.River[i]) continue;
            rivers++;
            int dn = d.Downstream[i];
            if (dn >= 0) Assert.True(d.Filled[i] >= d.Filled[dn] - 1e-6f);
        }
        Assert.True(rivers > 100);
    }

    [Fact]
    public void HasSeveralBiomes()
    {
        var d = Earth42.Value;
        var seen = new bool[(int)Biome.Count];
        foreach (var b in d.Biome) seen[(int)b] = true;
        Assert.True(seen.Count(x => x) >= 9);
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var p = WorldParams.FromPreset("archipelago");
        p.Width = 256; p.Height = 128;
        var a = PlanetGenerator.Generate(p, new RngStreams(5)[StreamId.Planet]);
        var b = PlanetGenerator.Generate(p, new RngStreams(5)[StreamId.Planet]);
        Assert.Equal(a.Elevation, b.Elevation);
        Assert.Equal(a.Biome, b.Biome);
    }

    [Fact]
    public void Generation512x256UnderBudget()
    {
        var p = WorldParams.FromPreset("earth");
        PlanetGenerator.Generate(p, new RngStreams(1)[StreamId.Planet]); // прогрев JIT
        var sw = Stopwatch.StartNew();
        PlanetGenerator.Generate(p, new RngStreams(2)[StreamId.Planet]);
        // Бюджет ARCHITECTURE.md — 3 с; в тестах (Debug) допускаем ×2.
        Assert.True(sw.ElapsedMilliseconds < 6000, $"{sw.ElapsedMilliseconds} мс");
    }
}
