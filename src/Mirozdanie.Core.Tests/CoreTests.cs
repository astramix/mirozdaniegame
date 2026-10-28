using Mirozdanie.Core.Events;
using Mirozdanie.Core.Random;
using Xunit;

namespace Mirozdanie.Core.Tests;

public class RngTests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new Rng(123); var b = new Rng(123);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void SplitMix64KnownValue()
    {
        // Эталон SplitMix64 для x = 0 (Vigna, splitmix64.c): первое значение 0xE220A8397B1DCDAF.
        ulong x = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, Rng.SplitMix64(ref x));
    }

    [Fact]
    public void StreamsAreIndependent()
    {
        var s1 = new RngStreams(42);
        var s2 = new RngStreams(42);
        // Активное использование потока Faith не сдвигает поток Planet.
        for (int i = 0; i < 500; i++) s1[StreamId.Faith].NextULong();
        for (int i = 0; i < 100; i++) Assert.Equal(s2[StreamId.Planet].NextULong(), s1[StreamId.Planet].NextULong());
        Assert.NotEqual(new RngStreams(42)[StreamId.Faith].NextULong(), new RngStreams(42)[StreamId.Planet].NextULong());
    }

    [Fact]
    public void NextIntInRangeAndRoughlyUniform()
    {
        var r = new Rng(7);
        var counts = new int[6];
        for (int i = 0; i < 60_000; i++) counts[r.NextInt(6)]++;
        foreach (var c in counts) Assert.InRange(c, 9_400, 10_600);
    }

    [Fact]
    public void GaussianHasUnitVariance()
    {
        var r = new Rng(9);
        double sum = 0, sq = 0; int n = 50_000;
        for (int i = 0; i < n; i++) { double g = r.NextGaussian(); sum += g; sq += g * g; }
        double mean = sum / n, var = sq / n - mean * mean;
        Assert.InRange(mean, -0.03, 0.03);
        Assert.InRange(var, 0.95, 1.05);
    }
}

public class EventBusTests
{
    [Fact]
    public void SubscribersCalledInOrderAndCascadesDispatched()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Subscribe(EventType.Famine, (in WorldEvent e) => { calls.Add("a"); bus.Publish(EventType.Plague, cause: e.Id); });
        bus.Subscribe(EventType.Famine, (in WorldEvent e) => calls.Add("b"));
        bus.Subscribe(EventType.Plague, (in WorldEvent e) => calls.Add("p" + e.Cause));
        int id = bus.Publish(EventType.Famine, a: 1);
        bus.Dispatch();
        Assert.Equal(new[] { "a", "b", "p0" }, calls);
        Assert.Equal(2, bus.Log.Count);
        Assert.Equal(id, bus.Log[1].Cause);
    }

    [Fact]
    public void EventsCarryYear()
    {
        var bus = new EventBus { CurrentYear = 312 };
        bus.Publish(EventType.Comet);
        Assert.Equal(312, bus.Log[0].Year);
    }
}
