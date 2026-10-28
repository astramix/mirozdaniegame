using System.Reflection;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Цивилизации на карте (docs/design/civilizations.md, §1, §3, §9, §10).</summary>
public class CivilizationTests
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
    public void CitiesFollowZipfWithinPeople()
    {
        foreach (var sim in Worlds.Value)
        {
            var w = sim.World;
            Assert.True(w.Cities.Count(c => c.Alive) > 20, $"сид {w.Seed}: городов мало");
            foreach (var g in w.Groups.Where(g => g.Alive))
            {
                var mine = w.Cities.Where(c => c.Alive && c.Group == g.Id).OrderByDescending(c => c.Pop).ToList();
                if (mine.Count < 3) continue;
                // Дом народа — крупнейший; второй — примерно вдвое меньше (Zipf 1949), с допуском на усыхание лишних.
                Assert.Equal(g.Cell, mine[0].Cell);
                Assert.InRange(mine[0].Pop / mine[1].Pop, 1.6f, 2.6f);
            }
        }
    }

    [Fact]
    public void UrbanShareGrowsWithEra()
    {
        for (int e = 1; e < 9; e++) Assert.True(Simulation.UrbanShare(e + 1) >= Simulation.UrbanShare(e));
        Assert.InRange(Simulation.UrbanShare(1), 0.01f, 0.05f);
        Assert.InRange(Simulation.UrbanShare(9), 0.6f, 0.85f);
    }

    [Fact]
    public void CapitalsAreMarked()
    {
        foreach (var sim in Worlds.Value)
        {
            var w = sim.World;
            foreach (var pol in w.Polities.Where(p => p.Alive))
            {
                var cap = w.Groups[pol.Capital];
                if (cap.Subsistence != Subsistence.Farming) continue;
                Assert.Contains(w.Cities, c => c.Alive && c.Capital && c.Cell == cap.Cell);
            }
        }
    }

    [Fact]
    public void GovernmentFormsVary()
    {
        var forms = new HashSet<GovForm>();
        foreach (var sim in Worlds.Value)
        {
            foreach (var p in sim.World.Polities) forms.Add(p.Form);
            foreach (var e in sim.World.Events.Log.Where(e => e.Type == EventType.GovernmentChanged)) forms.Add((GovForm)e.B);
        }
        Assert.True(forms.Count >= 4, $"форм власти: {string.Join(", ", forms)}");
    }

    [Fact]
    public void ArmiesMarchBeforeBattle()
    {
        int later = 0, battles = 0;
        foreach (var sim in Worlds.Value)
        {
            var log = sim.World.Events.Log;
            foreach (var b in log.Where(e => e.Type == EventType.Battle && e.Cause >= 0))
            {
                var decl = sim.World.Events.Get(b.Cause);
                if (decl.Type is not (EventType.WarDeclared or EventType.HolyWar)) continue;
                battles++;
                Assert.True(b.Year >= decl.Year);
                if (b.Year > decl.Year) later++;
            }
        }
        Assert.True(battles > 10, $"битв {battles}");
        Assert.True(later > battles / 4, $"битвы после марша: {later} из {battles}");
    }

    [Fact]
    public void SmallKindredPeoplesAssimilate()
    {
        foreach (var sim in Worlds.Value)
        {
            var w = sim.World;
            var merged = w.Events.Log.Where(e => e.Type == EventType.Assimilated).ToList();
            Assert.NotEmpty(merged);
            foreach (var e in merged) Assert.False(w.Groups[e.A].Alive);
        }
    }

    [Fact]
    public void PolitiesPersist()
    {
        // civilizations.md, §9: медианная жизнь державы ≥ 150 лет на мир.
        foreach (var sim in Worlds.Value)
        {
            var w = sim.World;
            var lives = w.Polities.Select(p => (p.Alive ? w.Year : p.Ended) - p.Founded).OrderBy(x => x).ToList();
            Assert.True(lives[lives.Count / 2] >= 150, $"сид {w.Seed}: медиана {lives[lives.Count / 2]} лет");
        }
    }

    [Fact]
    public void BrokenTreatyLowersTrustMoreThanPeaceBuildsIt()
    {
        var sim = Simulation.Create(42, WorldParams.FromPreset("earth"));
        sim.Run(900);
        var w = sim.World;
        var pols = w.Polities.Where(p => p.Alive).ToList();
        Assert.True(pols.Count >= 2);
        // Ищем две державы-соседки.
        Polity? a = null; Group? target = null;
        foreach (var p in pols)
            foreach (var m in p.Members)
                foreach (var n in w.Groups[m].Neighbors)
                    if (w.Groups[n].PolityId >= 0 && w.Groups[n].PolityId != p.Id && w.Polities[w.Groups[n].PolityId].Alive) { a = p; target = w.Groups[n]; goto found; }
        found:
        Assert.NotNull(a);
        var r = sim.Rel(a!.Id, target!.PolityId);
        r.PeaceUntil = w.Year + 30;
        r.Trust = 0.6f;
        float before = r.Trust;
        var fight = typeof(Simulation).GetMethod("Fight", BindingFlags.NonPublic | BindingFlags.Instance)!;
        fight.Invoke(sim, new object[] { a, w.Groups[a.Capital], w.Groups[a.Capital], target, false, w.Rng[Mirozdanie.Core.Random.StreamId.Politics] });
        float loss = before - r.Trust;
        Assert.Contains(w.Events.Log, e => e.Type == EventType.TreatyBroken);
        // Соблюдённый договор за 5 лет даёт +0,03; предательство отнимает больше, чем дают 10 лет мира (Axelrod 1984).
        Assert.True(loss >= 2 * 0.03f * 2, $"падение доверия {loss}");
        Assert.Contains(r.Grievances, gr => gr.Holder == target.PolityId);
    }
}
