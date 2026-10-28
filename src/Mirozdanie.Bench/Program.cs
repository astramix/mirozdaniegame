using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Bench;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--budget")) return Budgets.Run(args.Contains("--quick"));
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}

/// <summary>Готовые миры для бенчмарков (одни и те же для BenchmarkDotNet и режима --budget).</summary>
public static class Worlds
{
    /// <summary>Мир в эпохе жизни, пул особей заполнен до 20 000.</summary>
    public static Simulation LifeWorld()
    {
        var p = WorldParams.FromPreset("earth");
        p.LifeTicks = 100_000; // не пробуждаться во время замера
        p.MindThreshold = 2f;
        var sim = Simulation.Create(42, p);
        sim.World.Events.CurrentYear = 0;
        sim.World.Life.Seed(sim.World.Rng[StreamId.Life], sim.World.Events, p);
        for (int i = 0; i < 60; i++) sim.LifeStep();
        Fill(sim);
        return sim;
    }

    /// <summary>Добивает пул до 20 000 копиями живых особей (замер честно на полном пуле).</summary>
    public static void Fill(Simulation sim)
    {
        var ag = sim.World.Life.Agents;
        var rng = new Rng(1);
        int guard = 0;
        while (ag.Count < ag.Capacity && guard++ < 100_000)
        {
            int i = rng.NextInt(ag.Capacity);
            if (!ag.Alive[i]) continue;
            ag.Spawn(ag.Species[i], ag.X[i], ag.Y[i], ag.GenesOf(i), ag.Energy[i]);
        }
    }

    /// <summary>Мир поздней (для ночи 1) эпохи: 800 лет истории, сотни групп, политии.</summary>
    public static Simulation HistoryWorld()
    {
        var sim = Simulation.Create(42, WorldParams.FromPreset("earth"));
        sim.SetScript(InterventionScript.Parse("""[{"year":100,"type":"drought"},{"year":300,"type":"vision","message":"peace"}]"""));
        sim.Run(800);
        return sim;
    }
}

[MemoryDiagnoser]
public class LifeBench
{
    private Simulation _sim = null!;
    [GlobalSetup] public void Setup() => _sim = Worlds.LifeWorld();
    [IterationSetup] public void Refill() => Worlds.Fill(_sim);
    [Benchmark(Description = "Тик эпохи жизни, 20 000 особей")] public void LifeTick() => _sim.LifeStep();
}

[MemoryDiagnoser]
public class HistoryBench
{
    private Simulation _sim = null!;
    private readonly SnapshotPublisher _pub = new();
    [GlobalSetup] public void Setup() => _sim = Worlds.HistoryWorld();
    [Benchmark(Description = "Год истории (сотни групп, политии)")] public void HistoryYear() => _sim.Step();
    [Benchmark(Description = "Публикация снимка")] public void Snapshot() => _pub.Publish(_sim.World);
}

public class WorldGenBench
{
    [Benchmark(Description = "Генерация мира 512×256")]
    public PlanetData Generate() => PlanetGenerator.Generate(WorldParams.FromPreset("earth"), new RngStreams(7)[StreamId.Planet]);
}

[MemoryDiagnoser]
public class TerraformBench
{
    private PlanetData _p = null!;
    private readonly WorldParams _params = WorldParams.FromPreset("earth");
    [GlobalSetup] public void Setup() => _p = PlanetGenerator.Generate(_params, new RngStreams(7)[StreamId.Planet]);
    [Benchmark(Description = "Пересчёт планеты после правки")] public void Recompute() => PlanetGenerator.Recompute(_p, _params);
    [Benchmark(Description = "Мазок кисти радиусом 12")] public float Dab() => Terraform.Dab(_p, BrushMode.Raise, 200, 120, 12, 0.5f, 0);
}

/// <summary>Сверка с бюджетами ARCHITECTURE.md (для CI). Регрессия больше чем на 20 % роняет сборку.</summary>
public static class Budgets
{
    private static double Median(List<double> xs) { xs.Sort(); return xs[xs.Count / 2]; }

    private static double Measure(Action a, int warmup, int runs)
    {
        for (int i = 0; i < warmup; i++) a();
        var t = new List<double>();
        var sw = new Stopwatch();
        for (int i = 0; i < runs; i++) { sw.Restart(); a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        return Median(t);
    }

    private static double MeasureWithSetup(Action setup, Action a, int warmup, int runs)
    {
        for (int i = 0; i < warmup; i++) { setup(); a(); }
        var t = new List<double>();
        var sw = new Stopwatch();
        for (int i = 0; i < runs; i++) { setup(); sw.Restart(); a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        return Median(t);
    }

    public static int Run(bool quick)
    {
        var rows = new List<(string name, double ms, double budget, string note)>();

        var life = Worlds.LifeWorld();
        int agents = life.World.Life.Agents.Count;
        double lifeMs = MeasureWithSetup(() => Worlds.Fill(life), () => life.LifeStep(), 5, quick ? 10 : 40);
        rows.Add(("Тик с 20 000 особей (экология + особи)", lifeMs, 8, $"особей в начале тика: {agents}"));
        var lw = life.World;
        double ecoMs = Measure(() => lw.Life.EcologyTick(lw.TempOffset, lw.NppFactor), 3, quick ? 10 : 30);
        rows.Add(("  в т. ч. экология популяций", ecoMs, 8, $"видов: {lw.Life.Species.Count(s => s.Alive)}"));
        double agMs = MeasureWithSetup(() => Worlds.Fill(life), () => lw.Life.Agents.Tick(lw.Life, lw.Planet, lw.TempOffset, lw.Rng[StreamId.Agents], 1f), 3, quick ? 10 : 30);
        rows.Add(("  в т. ч. особи", agMs, 8, ""));

        var hist = Worlds.HistoryWorld();
        int groups = hist.World.Groups.Count(g => g.Alive);
        double histMs = Measure(() => hist.Step(), 5, quick ? 20 : 100);
        rows.Add(("Год поздней эпохи", histMs, 8, $"групп: {groups}, политий: {hist.World.Polities.Count(p => p.Alive)}"));

        var pub = new SnapshotPublisher();
        double snapMs = Measure(() => pub.Publish(hist.World), 5, 50);
        rows.Add(("Публикация снимка", snapMs, 1, ""));

        double genMs = Measure(() => PlanetGenerator.Generate(WorldParams.FromPreset("earth"), new RngStreams(7)[StreamId.Planet]), 1, quick ? 2 : 5);
        rows.Add(("Генерация мира 512×256", genMs, 3000, ""));

        // Терраформирование (docs/design/terraforming.md, §2): пересчёт планеты и мазок кисти.
        var tp = PlanetGenerator.Generate(WorldParams.FromPreset("earth"), new RngStreams(7)[StreamId.Planet]);
        var tparams = WorldParams.FromPreset("earth");
        double recMs = Measure(() => PlanetGenerator.Recompute(tp, tparams), 2, quick ? 5 : 20);
        rows.Add(("Пересчёт планеты после правки 512×256", recMs, 120, "климат, реки, биомы, эко-сетка"));
        int dabX = 0;
        double dabMs = Measure(() => Terraform.Dab(tp, BrushMode.Raise, 100 + (dabX++ % 200), 120, 12, 0.5f, 0), 5, quick ? 50 : 200);
        rows.Add(("Мазок кисти радиусом 12", dabMs, 2, ""));
        var hw = hist.World;
        var big = hw.Groups.Where(g => g.Alive).OrderByDescending(g => g.Pop).First();
        hw.Sandbox = true;
        int bx = big.Cell % hw.Planet.EW * hw.Planet.Scale, by = big.Cell / hw.Planet.EW * hw.Planet.Scale;
        double ridgeMs = Measure(() => hist.ApplyNow(new Intervention { Type = InterventionType.Ridge, Target = "map", Path = $"{bx},{by - 10};{bx},{by + 10}" }), 1, quick ? 3 : 10);
        rows.Add(("Хребет с последствиями (история)", ridgeMs, 250, "пересчёт, жизнь, народы, территории"));

        var sw = Stopwatch.StartNew();
        var full = Simulation.Create(1, WorldParams.FromPreset("earth"));
        full.Run(1000);
        rows.Add(("Прогон: эпоха жизни + 1000 лет истории", sw.Elapsed.TotalMilliseconds, 60_000, $"Пробуждение на тике {full.World.AwakeningTick}"));

        bool ok = true;
        Console.WriteLine("| Операция | Медиана, мс | Бюджет, мс | Статус | Примечание |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var (name, ms, budget, note) in rows)
        {
            bool pass = ms <= budget * 1.2;
            ok &= pass;
            Console.WriteLine($"| {name} | {ms:0.###} | {budget} | {(pass ? "✓" : "✗ превышен")} | {note} |");
        }
        return ok ? 0 : 1;
    }
}
