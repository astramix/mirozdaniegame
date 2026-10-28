using System.Security.Cryptography;
using System.Text;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Терраформирование: docs/design/terraforming.md, §9.</summary>
public class TerraformTests
{
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static Simulation Small(ulong seed)
    {
        var p = WorldParams.FromPreset("earth");
        p.LifeTicks = 250;
        return Simulation.Create(seed, p);
    }

    private static PlanetData Planet(ulong seed = 7) => PlanetGenerator.Generate(WorldParams.FromPreset("earth"), new RngStreams(seed)[StreamId.Planet]);

    /// <summary>Высота e (над уровнем моря) в клетке через слой правок.</summary>
    private static void SetElevation(PlanetData d, int i, float e) => d.ElevDelta[i] = e + d.SeaLevel - d.BaseElevation[i];

    [Fact]
    public void RecomputeWithoutEditsMatchesGeneration()
    {
        var d = Planet();
        var elev = (float[])d.Elevation.Clone();
        var temp = (float[])d.Temperature.Clone();
        var prec = (float[])d.Precip.Clone();
        var biome = (Biome[])d.Biome.Clone();
        var river = (bool[])d.River.Clone();
        var fert = (float[])d.Fertility.Clone();
        var ore = (float[])d.Ore.Clone();
        var enpp = (float[])d.ENpp.Clone();
        var eland = (float[])d.ELand.Clone();
        int continents = d.ContinentCount;
        PlanetGenerator.Recompute(d, WorldParams.FromPreset("earth"));
        Assert.Equal(elev, d.Elevation);
        Assert.Equal(temp, d.Temperature);
        Assert.Equal(prec, d.Precip);
        Assert.Equal(biome, d.Biome);
        Assert.Equal(river, d.River);
        Assert.Equal(fert, d.Fertility);
        Assert.Equal(ore, d.Ore);
        Assert.Equal(enpp, d.ENpp);
        Assert.Equal(eland, d.ELand);
        Assert.Equal(continents, d.ContinentCount);
    }

    [Fact]
    public void RidgeMakesRainShadow()
    {
        // Синтетический мир: океан и материк-полоса по долготам 100–300, ровная суша 0,3 км.
        var p = WorldParams.FromPreset("earth");
        var d = Planet();
        for (int i = 0; i < d.W * d.H; i++)
        {
            int x = i % d.W;
            SetElevation(d, i, x >= 100 && x < 300 ? 0.3f : -3f);
        }
        PlanetGenerator.Recompute(d, p);
        // Пояс западных ветров: 33–55° с. ш. (строки 50–78 при H = 256). Хребет с севера на юг на x = 150.
        float Lee(PlanetData pd)
        {
            float s = 0; int n = 0;
            // Полоса сразу за подветренным склоном (хребет шириной ±7 клеток кончается на x = 157).
            for (int y = 55; y <= 72; y++) for (int x = 157; x <= 176; x++) { s += pd.Precip[y * pd.W + x]; n++; }
            return s / n;
        }
        float before = Lee(d);
        Terraform.Ridge(d, 150, 48, 150, 80, 4f);
        PlanetGenerator.Recompute(d, p);
        float after = Lee(d);
        Assert.True(after < 0.7f * before, $"осадки за хребтом {before:0} → {after:0} мм");
    }

    [Fact]
    public void CarvedRiverReachesSea()
    {
        var p = WorldParams.FromPreset("earth");
        var d = Planet();
        // Исток: самая высокая суша не на реке и не у полюса.
        int src = -1;
        for (int i = 0; i < d.W * d.H; i++)
        {
            int y = i / d.W;
            if (y < 20 || y > d.H - 20 || d.Elevation[i] < 0.3f || d.River[i]) continue;
            if (src < 0 || d.Elevation[i] > d.Elevation[src]) src = i;
        }
        Assert.True(src >= 0);
        Terraform.River(d, src % d.W, src / d.W);
        PlanetGenerator.Recompute(d, p);
        int c = src, steps = 0;
        while (c >= 0 && d.Elevation[c] >= 0 && steps++ < 10_000)
        {
            Assert.True(d.River[c], $"разрыв реки на шаге {steps}");
            c = d.Downstream[c];
        }
        Assert.True(c < 0 || d.Elevation[c] < 0, "река не дошла до моря");
        Assert.True(steps > 3);
    }

    [Fact]
    public void BridgeLetsPeopleCross()
    {
        var sim = Small(3);
        sim.Run(10);
        var w = sim.World;
        var p = w.Planet;
        var g = w.Groups.First(x => x.Alive);
        g.Subsistence = Subsistence.Farming; // пешие: море им не переплыть
        int ex = g.Cell % p.EW, ey = g.Cell / p.EW;
        // Остров народа (радиус 1,5 эко-клетки), кольцо моря шириной 2 клетки, за ним — суша.
        for (int y = 0; y < p.H; y++)
        for (int x = 0; x < p.W; x++)
        {
            float fx = (x + 0.5f) / p.Scale - (ex + 0.5f), fy = (y + 0.5f) / p.Scale - (ey + 0.5f);
            if (fx > p.EW / 2f) fx -= p.EW; else if (fx < -p.EW / 2f) fx += p.EW;
            float dist = MathF.Max(MathF.Abs(fx), MathF.Abs(fy));
            if (dist > 6f) continue;
            SetElevation(p, y * p.W + x, dist <= 1.5f ? 0.4f : dist <= 3.5f ? -1.5f : 0.4f);
        }
        sim.RecomputePlanet();
        int outer = p.EcoIndex(ex + 4, ey);
        Assert.True(p.EIsLand(outer));
        Assert.False(sim.IsReachable(g, outer), "через два моря пешком не пройти");
        // Перешеек от острова на восток.
        int fx0 = ex * p.Scale + p.Scale / 2, fy0 = ey * p.Scale + p.Scale / 2;
        Terraform.Bridge(p, fx0, fy0, fx0 + 4 * p.Scale + 2, fy0);
        sim.RecomputePlanet();
        Assert.True(sim.IsReachable(g, outer), "по перешейку можно дойти");
    }

    [Fact]
    public void SeaRiseFloodsCoast()
    {
        var sim = Small(5);
        sim.Run(40);
        var w = sim.World;
        w.Sandbox = true;
        int alive = w.Groups.Count(g => g.Alive);
        // Море +800 м: береговые народы бегут или тонут.
        sim.ApplyNow(new Intervention { Type = InterventionType.SeaRise, Target = "map", Magnitude = 8f });
        var log = w.Events.Log;
        Assert.Contains(log, e => e.Type == EventType.SeaLevelChanged);
        Assert.Contains(log, e => e.Type is EventType.Refugees or EventType.GroupDrowned);
        foreach (var g in w.Groups.Where(x => x.Alive)) Assert.True(w.Planet.EIsLand(g.Cell), $"народ {g.Id} остался в море");
        Assert.True(w.Groups.Count(g => g.Alive) <= alive);
        // Жизнь видит новую береговую линию.
        foreach (var c in w.Life.LandCells) Assert.True(w.Planet.EIsLand(c));
    }

    [Fact]
    public void DriftCarriesPeople()
    {
        var sim = Small(9);
        sim.Run(30);
        var w = sim.World;
        var p = w.Planet;
        w.Sandbox = true;
        var g = w.Groups.Where(x => x.Alive).OrderByDescending(x => x.Pop).First();
        int ex = g.Cell % p.EW, ey = g.Cell / p.EW;
        int fx = ex * p.Scale + p.Scale / 2, fy = ey * p.Scale + p.Scale / 2;
        // Сдвиг на 2 эко-клетки к востоку.
        sim.ApplyNow(new Intervention { Type = InterventionType.Drift, Target = "map", Path = $"{fx},{fy};{fx + 2 * p.Scale},{fy}" });
        Assert.Contains(w.Events.Log, e => e.Type == EventType.ContinentDrifted);
        if (g.Alive)
            Assert.Equal(p.EcoIndex(ex + 2, ey), g.Cell);
        else
            Assert.Contains(w.Events.Log, e => e.Type == EventType.GroupDrowned && e.A == g.Id);
    }

    [Fact]
    public void TerraformIsDeterministic()
    {
        // Оригинал: лепка до жизни (кисть и силы), затем силы и мазки в истории, «как природа», сохранение и повтор.
        var a = Small(13);
        var p = a.World.Planet;
        Assert.True(a.IsPreLife);
        var st = a.BeginStroke(BrushMode.Raise, 8, 0.8f, false);
        for (int k = 0; k < 20; k++) a.StrokeDab(st, 200 + k * 2, 100 + k);
        a.RecomputePlanet(); // предпросмотр посреди мазка не должен влиять на итог
        for (int k = 0; k < 10; k++) a.StrokeDab(st, 240 + k, 120);
        a.EndStroke(st);
        a.ApplyNow(new Intervention { Type = InterventionType.Island, Target = "map", Path = "60,120" });
        a.ApplyNow(new Intervention { Type = InterventionType.Ridge, Target = "map", Path = "300,60;330,90" });
        // Отменённая правка не попадает в журнал.
        a.ApplyNow(new Intervention { Type = InterventionType.Sink, Target = "map", Path = "220,110" });
        Assert.True(a.Undo());
        a.Run(30);
        a.World.Sandbox = true;
        var g = a.World.Groups.First(x => x.Alive);
        int gx = g.Cell % p.EW * p.Scale, gy = g.Cell / p.EW * p.Scale;
        a.ApplyNow(new Intervention { Type = InterventionType.River, Target = "map", Path = $"{gx},{gy}" });
        var st2 = a.BeginStroke(BrushMode.Lower, 6, 1f, false);
        for (int k = 0; k < 15; k++) a.StrokeDab(st2, gx + k, gy + 3);
        a.EndStroke(st2);
        a.ApplyNow(new Intervention { Type = InterventionType.Volcano, Target = "map", Path = $"{gx + 30},{gy}" });
        a.ApplyNow(new Intervention { Type = InterventionType.Ridge, Target = "map", Path = $"{gx - 20},{gy - 20};{gx - 20},{gy + 20}", Slow = true });
        a.ApplyNow(new Intervention { Type = InterventionType.Drift, Target = "map", Path = $"{gx},{gy};{gx + 12},{gy}", Slow = true });
        a.Run(40);
        a.ApplyNow(new Intervention { Type = InterventionType.SeaRise, Target = "map" });
        a.Run(20);
        var save = WorldSave.FromJson(a.Save("тест").ToJson());
        a.Run(60);
        var b = Simulation.Load(save);
        Assert.Equal(a.World.AppliedLog.Count, b.World.AppliedLog.Count);
        b.Run(60);
        Assert.Equal(a.World.Planet.Elevation, b.World.Planet.Elevation);
        Assert.Equal(Hash(new ChronicleWriter(a.World).Write()), Hash(new ChronicleWriter(b.World).Write()));
    }

    [Fact]
    public void InstantMountainIsMiracleSlowIsNatural()
    {
        // Один и тот же хребет у крупнейшего народа: мгновенно и «как природа». Доля «чудесных» толкований различается.
        float AgentShare(bool slow)
        {
            float agent = 0, all = 0;
            foreach (ulong seed in new ulong[] { 2, 4, 6 })
            {
                var sim = Small(seed);
                sim.Run(60);
                var w = sim.World;
                w.Sandbox = true;
                var g = w.Groups.Where(x => x.Alive).OrderByDescending(x => x.Pop).First();
                int fx = g.Cell % w.Planet.EW * w.Planet.Scale, fy = g.Cell / w.Planet.EW * w.Planet.Scale;
                int from = w.Events.Log.Count;
                sim.ApplyNow(new Intervention { Type = InterventionType.Ridge, Target = "map", Path = $"{fx + 6},{fy - 14};{fx + 6},{fy + 14}", Slow = slow });
                sim.Run(3);
                var log = w.Events.Log;
                for (int i = from; i < log.Count; i++)
                {
                    var e = log[i];
                    if (e.Type != EventType.Interpretation || e.C != (int)EventType.Terraformed) continue;
                    all++;
                    if ((InterpretationKind)e.B != InterpretationKind.Natural) agent++;
                }
            }
            return all > 0 ? agent / all : 0;
        }
        float instant = AgentShare(false), slowShare = AgentShare(true);
        Assert.True(instant > slowShare + 0.15f, $"мгновенно {instant:0.00}, как природа {slowShare:0.00}");
    }

    [Fact]
    public void ScriptAcceptsTerraformAndRejectsMissingPath()
    {
        var s = InterventionScript.Parse("""[{"year":-1000000000,"type":"sculpt","brush":"raise","path":"10,10;12,12","radius":5,"strength":0.5},{"year":5,"type":"sea_rise"}]""");
        Assert.Equal(2, s.Items.Count);
        Assert.Equal(InterventionType.Sculpt, s.Items[0].Type);
        Assert.Equal(2, s.Items[0].Points().Count);
        var ex = Assert.Throws<FormatException>(() => InterventionScript.Parse("""[{"year":5,"type":"ridge"}]"""));
        Assert.Contains("path", ex.Message);
        Assert.Throws<FormatException>(() => InterventionScript.Parse("""[{"year":5,"type":"sculpt","brush":"melt","path":"1,1"}]"""));
    }
}
