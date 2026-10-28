using System.Text.RegularExpressions;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Летопись, мифы и русский текст (docs/design/lore.md, §2, §7, §10, §11).</summary>
public class LetopisTests
{
    private static readonly Lazy<Simulation> World1 = new(() =>
    {
        var sim = Simulation.Create(42, WorldParams.FromPreset("earth"));
        sim.Run(1500);
        return sim;
    });

    [Fact]
    public void MythUsesRealEvents()
    {
        var w = World1.Value.World;
        var writer = new ChronicleWriter(w);
        int told = 0;
        foreach (var r in w.Religions.Take(12))
        {
            var myth = Mythology.Tell(w, r, w.Groups[r.FounderGroup], writer);
            foreach (var s in myth.Scenes)
            {
                Assert.InRange(s.EventId, 0, w.Events.Log.Count - 1);
                Assert.Equal(w.Events.Get(s.EventId).Year, s.Year);
                Assert.False(string.IsNullOrWhiteSpace(s.Told));
                told++;
            }
        }
        Assert.True(told > 10, $"сцен мифов: {told}");
    }

    [Fact]
    public void MythsDivergeAcrossSchism()
    {
        var w = World1.Value.World;
        var writer = new ChronicleWriter(w);
        int pairs = 0, diverged = 0;
        foreach (var child in w.Religions.Where(r => r.Parent >= 0 && w.Year - r.Founded >= 300))
        {
            var parent = w.Religions[child.Parent];
            var a = Mythology.Tell(w, parent, w.Groups[parent.FounderGroup], writer);
            var b = Mythology.Tell(w, child, w.Groups[child.FounderGroup], writer);
            pairs++;
            if (a.Scenes.Select(s => s.Told).Except(b.Scenes.Select(s => s.Told)).Any()) diverged++;
        }
        if (pairs == 0) return; // в этом мире нет старых расколов
        Assert.Equal(pairs, diverged);
    }

    [Fact]
    public void FloodBreedsFloodMyth()
    {
        var sim = Simulation.Create(5, WorldParams.FromPreset("earth"));
        sim.Run(700);
        var w = sim.World;
        w.Sandbox = true;
        sim.ApplyNow(new Intervention { Type = InterventionType.SeaRise, Target = "map", Magnitude = 1f });
        sim.Run(5);
        var writer = new ChronicleWriter(w);
        int peoples = 0, flood = 0;
        foreach (var g in w.Groups.Where(g => g.Alive && g.ReligionId >= 0))
        {
            peoples++;
            if (Mythology.HasFlood(Mythology.Tell(w, w.Religions[g.ReligionId], g, writer))) flood++;
        }
        Assert.True(peoples > 0);
        Assert.True(flood >= 0.7 * peoples, $"миф о потопе у {flood} из {peoples}");
    }

    [Theory]
    [InlineData("Хогагоѳ", Russian.Case.Gen, "Хогагоѳа")]
    [InlineData("Хогагоѳ", Russian.Case.Ins, "Хогагоѳом")]
    [InlineData("Нукача", Russian.Case.Gen, "Нукачи")]
    [InlineData("Нукача", Russian.Case.Ins, "Нукачей")]
    [InlineData("Мапа", Russian.Case.Gen, "Мапы")]
    [InlineData("Мапа", Russian.Case.Acc, "Мапу")]
    [InlineData("Лули", Russian.Case.Gen, "Лули")]
    [InlineData("Погоѳухулѳи", Russian.Case.Ins, "Погоѳухулѳи")]
    [InlineData("Хуниўйы", Russian.Case.Prep, "Хуниўйы")]
    [InlineData("Гокай", Russian.Case.Gen, "Гокая")]
    [InlineData("Кугохеўаў", Russian.Case.Prep, "Кугохеўаў")]
    [InlineData("Мыѳегош", Russian.Case.Ins, "Мыѳегошем")]
    public void RussianDeclension(string name, Russian.Case c, string expected) => Assert.Equal(expected, Russian.Decline(name, c));

    [Fact]
    public void ChronicleNoBulkRepeats()
    {
        var md = new ChronicleWriter(World1.Value.World).Write();
        var byYear = new Dictionary<string, Dictionary<string, int>>();
        foreach (var line in md.Split('\n'))
        {
            var m = Regex.Match(line, @"^- \*\*Год (\d+)\.\*\* (.*)$");
            if (!m.Success) continue;
            // Скелет строки: имена (слова с заглавной) → X, числа → N.
            string skel = Regex.Replace(Regex.Replace(m.Groups[2].Value, @"\*.*$", ""), @"[А-ЯЁЎѲA-Z][^\s,.:;«»()]*", "X");
            skel = Regex.Replace(skel, @"\d+", "N");
            if (!byYear.TryGetValue(m.Groups[1].Value, out var d)) byYear[m.Groups[1].Value] = d = new();
            d[skel] = d.GetValueOrDefault(skel) + 1;
        }
        foreach (var (year, d) in byYear)
            foreach (var (skel, n) in d)
                Assert.True(n <= 2, $"год {year}: {n} одинаковых строк «{skel}»");
    }

    [Fact]
    public void TimelineCoversHistoryWithSummaries()
    {
        var sim = World1.Value;
        var w = sim.World;
        var l = new Letopis(w, new ChronicleWriter(w));
        var mil = l.Millennia();
        Assert.Equal((int)(w.Year / 1000) + 1, mil.Count);
        foreach (var m in mil) Assert.False(string.IsNullOrWhiteSpace(m.Summary));
        var cen = l.Centuries(0);
        Assert.Equal(10, cen.Count);
        Assert.Contains(cen, c => c.Highlights.Count > 0);
        Assert.Contains(l.Chapters(), c => c.Kind == "Держава" || c.Kind == "Вера");
    }

    [Fact]
    public void WhyFollowsCauses()
    {
        var w = World1.Value.World;
        var l = new Letopis(w, new ChronicleWriter(w));
        var log = w.Events.Log;
        int id = -1;
        for (int i = log.Count - 1; i >= 0; i--) if (log[i].Cause >= 0 && log[log[i].Cause].Cause >= 0) { id = i; break; }
        Assert.True(id >= 0);
        var node = l.Why(id);
        Assert.NotNull(node.Cause);
        Assert.Equal(log[id].Cause, node.Cause!.Event);
        Assert.DoesNotContain(node.Text, Enum.GetNames<EventType>().Select(n => n + "."));
    }

    [Fact]
    public void EncyclopediaHasAllKinds()
    {
        var w = World1.Value.World;
        var writer = new ChronicleWriter(w);
        var l = new Letopis(w, writer);
        foreach (var k in Encyclopedia.Kinds)
        {
            var list = Encyclopedia.List(w, l, writer, k);
            if (k is "Эпохи" or "Открытия" or "Народы" or "Веры" or "Языки") Assert.NotEmpty(list);
            foreach (var e in list) Assert.False(string.IsNullOrWhiteSpace(e.Body));
        }
    }
}
