using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Sim;
using Xunit;

namespace Mirozdanie.Core.Tests;

public class FaithTests
{
    [Fact]
    public void HadOverdetectsAnomalousEvents()
    {
        // Молния с ясного неба у набожной группы — почти всегда чья-то воля.
        Assert.True(Simulation.AgentProbability(0.6f, 0.2f, 0.8f, 0.9f, 0.05f) > 0.9f);
        // Учёная группа и обычное природное событие — редко.
        Assert.True(Simulation.AgentProbability(0.3f, 0.2f, 0.3f, 0.1f, 0.9f) < 0.2f);
    }

    [Fact]
    public void NegativityBias()
    {
        // При равной силе беда приписывается агенту чаще, чем удача (Morewedge 2009).
        Assert.True(Simulation.AgentProbability(0.6f, 0.5f, 0.5f, 0.2f, 0.2f) > Simulation.AgentProbability(0.6f, -0.5f, 0.5f, 0.2f, 0.2f));
    }

    private static float[] Vec(float v) => Enumerable.Repeat(v, Doctrine.Count).ToArray();

    [Fact]
    public void BoundedConfidenceFarDoNotConverge()
    {
        var a = Vec(0.2f); var b = Vec(0.6f);
        for (int t = 0; t < 100; t++) { Simulation.BoundedConfidenceStep(a, b, 0.15f, 0.05f); Simulation.BoundedConfidenceStep(b, a, 0.15f, 0.05f); }
        Assert.True(Doctrine.Distance(a, b) > 0.39f);
    }

    [Fact]
    public void BoundedConfidenceNearConverge()
    {
        var a = Vec(0.4f); var b = Vec(0.5f);
        for (int t = 0; t < 100; t++) { Simulation.BoundedConfidenceStep(a, b, 0.15f, 0.05f); Simulation.BoundedConfidenceStep(b, a, 0.15f, 0.05f); }
        Assert.True(Doctrine.Distance(a, b) < 0.01f);
    }

    [Fact]
    public void SirCompatibleCultGrows()
    {
        float s = 0.05f;
        for (int t = 0; t < 100; t++) s = Simulation.SirStep(s, 0.12f, 0.03f, 1f, 0f);
        Assert.True(s > 0.5f);
    }

    [Fact]
    public void SirIncompatibleCultFades()
    {
        float s = 0.05f;
        for (int t = 0; t < 100; t++) s = Simulation.SirStep(s, 0.12f, 0.03f, 0f, 0f);
        Assert.True(s < 0.01f);
    }

    [Fact]
    public void ConformityAmplifiesMajority()
    {
        float up = 0.3f, down = 0.3f;
        for (int t = 0; t < 30; t++) { up = Simulation.SirStep(up, 0.05f, 0.03f, 0.5f, 0.03f); down = Simulation.SirStep(down, 0.05f, 0.03f, 0.5f, -0.03f); }
        Assert.True(up > down);
    }

    [Fact]
    public void CompatibilityFromDistance()
    {
        Approx.Equal(1f, Simulation.Compatibility(Vec(0.5f), Vec(0.5f)), 3);
        Approx.Equal(0f, Simulation.Compatibility(Vec(0.1f), Vec(0.9f)), 3);
    }

    [Theory]
    [InlineData(0.5f, 0.6f, 0.0f, 0.1f, 0.0f, Attitude.Love)]
    [InlineData(-0.4f, 0.6f, 0.0f, 0.1f, 0.0f, Attitude.Fear)]
    [InlineData(0.0f, 0.6f, 0.0f, 0.1f, 0.0f, Attitude.Bargain)]
    [InlineData(0.0f, 0.05f, 0.0f, 0.1f, 0.0f, Attitude.Indifference)]
    [InlineData(0.0f, 0.3f, 0.0f, 0.8f, 0.0f, Attitude.Denial)]
    [InlineData(-0.2f, 0.6f, 0.6f, 0.1f, 0.0f, Attitude.Rebellion)]
    [InlineData(-0.7f, 0.6f, 0.0f, 0.1f, 0.8f, Attitude.Nihilism)]
    public void AttitudeScale(float b, float p, float g, float n, float d, Attitude expected)
    {
        var grp = new Group { B = b, P = p, G = g, N = n, D = d };
        Assert.Equal(expected, Simulation.Classify(grp));
    }

    [Fact]
    public void RebellionDrainsCreator()
    {
        Assert.True(Simulation.AttitudeWeight(Attitude.Rebellion) < 0);
        Assert.True(Simulation.AttitudeWeight(Attitude.Nihilism) < 0);
        Assert.True(Simulation.AttitudeWeight(Attitude.Love) > Simulation.AttitudeWeight(Attitude.Fear));
        Assert.Equal(0f, Simulation.AttitudeWeight(Attitude.Denial));
    }
}

public class PoliticsEconomyTests
{
    [Fact]
    public void LanchesterSquareLaw()
    {
        // Вдвое больше воинов побеждают втрое лучших: 2²·1 = 4 > 1²·3 = 3.
        var (win, lossA, lossD) = Simulation.Lanchester(2000, 1f, 1000, 3f);
        Assert.True(win);
        // Потери победителя: N·(1 − √(1 − F_l/F_w)) = 2000·(1 − √0,25) = 1000.
        Approx.Equal(1000f, lossA, 1);
        Approx.Equal(1000f, lossD, 1);
    }

    [Fact]
    public void LanchesterQualityCanWin()
    {
        var (win, _, _) = Simulation.Lanchester(1000, 1f, 900, 2f);
        Assert.False(win);
    }

    [Fact]
    public void KardashevSaganFormula()
    {
        Approx.Equal(1f, Simulation.Kardashev(1e16), 4);
        Approx.Equal(0f, Simulation.Kardashev(1e6), 4);
        // Земля ~2·10¹³ Вт → K ≈ 0,73 (Саган).
        Assert.InRange(Simulation.Kardashev(2e13), 0.72f, 0.74f);
    }

    [Fact]
    public void RussianPlural()
    {
        Assert.Equal("1 год", Chronicle.Russian.Years(1));
        Assert.Equal("3 года", Chronicle.Russian.Years(3));
        Assert.Equal("11 лет", Chronicle.Russian.Years(11));
        Assert.Equal("21 год", Chronicle.Russian.Years(21));
        Assert.Equal("25 лет", Chronicle.Russian.Years(25));
    }
}
