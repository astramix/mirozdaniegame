using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Круг истории не замыкается (docs/design/civilizations.md, §5, §9).</summary>
public class HistoryLoopTests
{
    private static readonly Lazy<List<Simulation>> Worlds = new(() =>
    {
        var list = new List<Simulation>();
        foreach (ulong seed in new ulong[] { 42, 7, 1000 })
        {
            var sim = Simulation.Create(seed, WorldParams.FromPreset("earth"));
            sim.Run(2500);
            list.Add(sim);
        }
        return list;
    });

    [Fact]
    public void NaturalLightningRarelyKillsRulers()
    {
        foreach (var sim in Worlds.Value)
        {
            int natural = sim.World.Events.Log.Count(e => e.Type == EventType.LeaderStruck && !e.ByCreator);
            Assert.True(natural <= 15, $"сид {sim.World.Seed}: природных смертей от молнии {natural}");
        }
    }

    [Fact]
    public void NaturalismSpreadsNotRespawns()
    {
        foreach (var sim in Worlds.Value)
        {
            int godless = sim.World.Religions.Count(r => r.Form == DeityForm.None && r.CreatorName.Gloss == "Пустое небо");
            Assert.True(godless <= 8, $"сид {sim.World.Seed}: учений «пустого неба» {godless}");
        }
    }

    [Fact]
    public void NoDoubleSuffixNames()
    {
        foreach (var sim in Worlds.Value)
            foreach (var p in sim.World.Polities)
            {
                var n = p.Name.Native;
                for (int len = 3; len <= n.Length / 2; len++)
                    Assert.False(n.EndsWith(n.Substring(n.Length - len) + n.Substring(n.Length - len), StringComparison.Ordinal) && len >= 4,
                        $"повтор суффикса в имени державы {n}");
            }
    }

    [Fact]
    public void RevoltsDoNotChurn()
    {
        foreach (var sim in Worlds.Value)
        {
            int splits = sim.World.Events.Log.Count(e => e.Type == EventType.PolitySplit);
            Assert.True(splits <= 60, $"сид {sim.World.Seed}: отделений {splits} за 2500 лет");
        }
    }
}
