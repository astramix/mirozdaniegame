using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Игровая петля бога и финал (docs/design/god-loop.md, §10).</summary>
public class GodLoopTests
{
    private static Simulation Make(ulong seed, int years)
    {
        var sim = Simulation.Create(seed, WorldParams.FromPreset("earth"));
        sim.Run(years);
        sim.World.Sandbox = true;
        return sim;
    }

    private static Group Largest(World w) => w.Groups.Where(g => g.Alive).OrderByDescending(g => g.Pop).First();

    [Fact]
    public void DependenceSlowsKnowledge()
    {
        float Grow(bool dependent)
        {
            var sim = Make(11, 600);
            var g = Largest(sim.World);
            float k0 = g.Knowledge;
            for (int y = 0; y < 60; y++)
            {
                if (dependent) g.Dependence = 1f;
                sim.Step();
            }
            return g.Knowledge - k0;
        }
        float free = Grow(false), dep = Grow(true);
        Assert.True(dep < free, $"знание с зависимостью {dep:0.0000}, без — {free:0.0000}");
    }

    [Fact]
    public void BlessingsBreedDependence()
    {
        var sim = Make(12, 400);
        var g = Largest(sim.World);
        for (int k = 0; k < 4; k++) sim.ApplyNow(new Intervention { Type = InterventionType.Bless, Target = $"group:{g.Id}" });
        sim.Step();
        Assert.True(g.Dependence >= 0.4f, $"зависимость {g.Dependence}");
    }

    [Fact]
    public void KeptOathRaisesLove()
    {
        var sim = Make(13, 400);
        var w = sim.World;
        var g = Largest(w);
        float b0 = g.B;
        sim.ApplyNow(new Intervention { Type = InterventionType.Oath, Target = $"group:{g.Id}", Message = "rain" });
        sim.ApplyNow(new Intervention { Type = InterventionType.Rain, Target = $"group:{g.Id}" });
        sim.Step();
        var oath = w.Oaths.Last();
        Assert.Equal(1, oath.Status);
        Assert.Contains(w.Events.Log, e => e.Type == EventType.OathKept && e.A == g.Id);
        Assert.Contains(g.Epithets, e => e.Kind == EpithetKind.TrueWord);
        Assert.True(g.B > b0);
    }

    [Fact]
    public void BrokenOathGivesDeceiverEpithet()
    {
        var sim = Make(14, 400);
        var w = sim.World;
        var g = Largest(w);
        sim.ApplyNow(new Intervention { Type = InterventionType.Oath, Target = $"group:{g.Id}", Message = "spare" });
        sim.ApplyNow(new Intervention { Type = InterventionType.Lightning, Target = $"group:{g.Id}" });
        sim.Step();
        Assert.Equal(-1, w.Oaths.Last().Status);
        Assert.Contains(g.Epithets, e => e.Kind == EpithetKind.Deceiver);
        // Обет «пошлю дождь» без дождя нарушается к сроку.
        sim.ApplyNow(new Intervention { Type = InterventionType.Oath, Target = $"group:{g.Id}", Message = "answer" });
        sim.Run(Simulation.OathYears(OathKind.Answer) + 2);
        Assert.Equal(-1, w.Oaths.Last().Status);
    }

    [Fact]
    public void PetitionsAreMadeAndResolvedOnce()
    {
        var sim = Make(42, 1500);
        var w = sim.World;
        Assert.NotEmpty(w.Petitions);
        foreach (var p in w.Petitions.Where(p => p.Deadline < w.Year && w.Groups[p.Group].Alive))
            Assert.True(p.Status is 1 or -1 or 2, $"просьба {p.Id} не разрешена к сроку");
        var log = w.Events.Log;
        foreach (var p in w.Petitions)
            Assert.True(log.Count(e => e.Type is EventType.PetitionGranted or EventType.PetitionIgnored && e.Cause == p.Event) <= 1);
    }

    [Fact]
    public void StyleFollowsDeeds()
    {
        var sim = Make(15, 300);
        var w = sim.World;
        var g = Largest(w);
        for (int k = 0; k < 20; k++) sim.ApplyNow(new Intervention { Type = InterventionType.Lightning, Target = $"group:{g.Id}" });
        Assert.Equal(CreatorStyle.Wrathful, sim.ComputeStyle());
        var sim2 = Make(15, 300);
        var g2 = Largest(sim2.World);
        for (int k = 0; k < 20; k++) sim2.ApplyNow(new Intervention { Type = InterventionType.Bless, Target = $"group:{g2.Id}" });
        Assert.Equal(CreatorStyle.Merciful, sim2.ComputeStyle());
    }

    [Fact]
    public void ContactAppearsInLateErasWhenCreatorActs()
    {
        var sim = Simulation.Create(3, WorldParams.FromPreset("earth"));
        sim.Run(1300);
        var w = sim.World;
        w.Sandbox = true;
        // Создатель деятелен в позднюю эпоху: каждые 8 лет — знак крупнейшему народу.
        for (int y = 0; y < 700; y++)
        {
            if (y % 8 == 0)
            {
                var g = Largest(w);
                sim.ApplyNow(new Intervention { Type = y % 16 == 0 ? InterventionType.Lightning : InterventionType.Rain, Target = $"group:{g.Id}" });
            }
            sim.Step();
        }
        Assert.Contains(w.Events.Log, e => e.Type == EventType.SimulationSuspected);
        Assert.Contains(w.Events.Log, e => e.Type is EventType.ProbeLaunched or EventType.GeoglyphMade or EventType.LightMessage or EventType.ShieldRaised or EventType.FreedomDemanded);
    }

    [Fact]
    public void ShieldWeakensMiracles()
    {
        var sim = Make(42, 900);
        var w = sim.World;
        var g = w.Groups.First(x => x.Alive && x.PolityId >= 0);
        w.Polities[g.PolityId].Shield = true;
        Assert.Equal(0.3f, sim.ShieldFactor(g.Cell));
        sim.ApplyNow(new Intervention { Type = InterventionType.Rain, Target = $"group:{g.Id}" });
        var ev = w.Events.Log.Last(e => e.Type == EventType.Intervention);
        Assert.True(ev.Magnitude <= 0.31f, $"сила дождя под щитом {ev.Magnitude}");
    }

    [Fact]
    public void ReplyAndFreedomAreRecorded()
    {
        var sim = Make(42, 900);
        var w = sim.World;
        var g = w.Groups.First(x => x.Alive && x.PolityId >= 0);
        sim.ApplyNow(new Intervention { Type = InterventionType.Reply, Target = $"group:{g.Id}", Message = "answer" });
        Assert.Equal(1f, g.Suspicion);
        sim.ApplyNow(new Intervention { Type = InterventionType.Reply, Target = $"group:{g.Id}", Message = "free" });
        Assert.True(g.Freed);
        Assert.True(w.Polities[g.PolityId].Shield);
    }

    [Fact]
    public void OutcomeIsAlwaysDefined()
    {
        foreach (ulong seed in new ulong[] { 1, 2 })
        {
            var sim = Make(seed, 600);
            var o = Outcome.Evaluate(sim.World);
            Assert.False(string.IsNullOrWhiteSpace(o.Title));
            Assert.False(string.IsNullOrWhiteSpace(o.Epilogue));
            Assert.Equal(OutcomeKind.Ongoing, o.Kind);
            Assert.NotEmpty(o.Facts);
        }
    }

    [Fact]
    public void OathsReplayFromSave()
    {
        var sim = Make(18, 300);
        var w = sim.World;
        var g = Largest(w);
        sim.ApplyNow(new Intervention { Type = InterventionType.Oath, Target = $"group:{g.Id}", Message = "protect" });
        sim.Run(40);
        var save = WorldSave.FromJson(sim.Save().ToJson());
        var loaded = Simulation.Load(save);
        Assert.Equal(w.Oaths.Count, loaded.World.Oaths.Count);
        Assert.Equal(w.Oaths.Last().Status, loaded.World.Oaths.Last().Status);
        Assert.Equal(w.Events.Log.Count, loaded.World.Events.Log.Count);
    }
}
