using System.Diagnostics;
using System.Globalization;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Diversity;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Output;
using Mirozdanie.Core.Sim;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

static string Arg(string[] a, string name, string def)
{
    int i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
}

static void Usage()
{
    Console.WriteLine("""
        Мироздание — CLI
          run       --seed 42 --years 1000 --preset earth --script examples/x.json --out out/   (Хроника, карты, отчёт)
          maps      --seed 42 --preset earth --out out/                                         (только карты планеты)
          diversity --worlds 30 --years 800 --report docs/diversity-report.md                   (тест разнообразия)
        Пресеты: earth, ice, archipelago, mars, chaos
        """);
}

if (args.Length == 0 || args[0] is "-h" or "--help") { Usage(); return 0; }
if (args[0] == "bergmann")
{
    foreach (var sd in new ulong[] { 1, 2, 3, 4, 5, 6, 42 })
    {
        var sim = Simulation.Create(sd, WorldParams.FromPreset("earth"));
        sim.RunLife();
        var w = sim.World; var ag = w.Life.Agents; int n = w.Life.Species.Count;
        var cold = new double[n]; var warm = new double[n]; var nc = new int[n]; var nw = new int[n];
        double bc = 0, bw = 0, wc = 0, ww = 0;
        for (int i = 0; i < ag.Capacity; i++)
        {
            if (!ag.Alive[i]) continue;
            int s = ag.Species[i]; int c = (int)ag.Y[i] * w.Planet.EW + (int)ag.X[i]; float t = w.Planet.ETemp[c];
            float m = ag.GenesOf(i)[0];
            if (t < 8) { cold[s] += m; nc[s]++; bc += m; wc++; } else if (t > 18) { warm[s] += m; nw[s]++; bw += m; ww++; }
        }
        int cmp = 0, ok = 0;
        for (int s = 0; s < n; s++) { if (nc[s] < 20 || nw[s] < 20) continue; cmp++; if (cold[s] / nc[s] > warm[s] / nw[s]) ok++; }
        // Межвидовой вариант (Bergmann 1847): корреляция «оптимум температуры вида — масса» по живым видам.
        var sp = w.Life.Species.Where(x => x.Alive && !x.Sapient && x.AgentCount > 30).ToList();
        double mo = sp.Average(x => x.Genome[6]), mm = sp.Average(x => x.Genome[0]);
        double cov = sp.Sum(x => (x.Genome[6] - mo) * (x.Genome[0] - mm)), vo = sp.Sum(x => (x.Genome[6] - mo) * (x.Genome[6] - mo)), vm = sp.Sum(x => (x.Genome[0] - mm) * (x.Genome[0] - mm));
        Console.WriteLine($"seed {sd}: within-species {ok}/{cmp}; all agents cold {bc / Math.Max(1, wc):F2} warm {bw / Math.Max(1, ww):F2} (n {wc}/{ww}); r(Topt, mass) = {cov / Math.Sqrt(vo * vm + 1e-9):F2} over {sp.Count}");
    }
    return 0;
}
string cmd = args[0];
try
{
    switch (cmd)
    {
        case "run": return Run(args);
        case "maps": return Maps(args);
        case "diversity": return Diversity(args);
        case "talk": return Talk(args);
        default: Usage(); return 1;
    }
}
catch (FormatException e)
{
    Console.Error.WriteLine("Ошибка: " + e.Message);
    return 2;
}

static int Run(string[] a)
{
    ulong seed = ulong.Parse(Arg(a, "--seed", "42"));
    int years = int.Parse(Arg(a, "--years", "1000"));
    string preset = Arg(a, "--preset", "earth");
    string outDir = Arg(a, "--out", "out");
    string script = Arg(a, "--script", "");
    var p = WorldParams.FromPreset(preset);
    if (int.TryParse(Arg(a, "--life-ticks", ""), out int lt)) p.LifeTicks = lt;
    var sw = Stopwatch.StartNew();
    var sim = Simulation.Create(seed, p);
    if (script.Length > 0) sim.SetScript(InterventionScript.Load(script));
    long tGen = sw.ElapsedMilliseconds;
    sim.RunLife();
    long tLife = sw.ElapsedMilliseconds;
    sim.Run(years, y => { if (y % 250 == 0) Console.Error.Write($"\rгод {y}…"); });
    long tHist = sw.ElapsedMilliseconds;
    Console.Error.WriteLine();
    var w = sim.World;
    string dir = Path.Combine(outDir, seed.ToString(CultureInfo.InvariantCulture));
    Directory.CreateDirectory(dir);
    string chronicle = new ChronicleWriter(w).Write();
    File.WriteAllText(Path.Combine(dir, "chronicle.md"), chronicle);
    foreach (var layer in WorldMaps.Layers)
        Png.Write(Path.Combine(dir, layer + ".png"), w.Planet.W, w.Planet.H, WorldMaps.Render(w, layer));
    long tOut = sw.ElapsedMilliseconds;
    var metrics = WorldMetrics.Measure(w);
    if (Environment.GetEnvironmentVariable("MIRO_LATENT") == "1")
    {
        var al = w.Groups.Where(g => g.Alive).ToList();
        Console.WriteLine($"B={al.Average(g => g.B):F2} P={al.Average(g => g.P):F2} G={al.Average(g => g.G):F2} N={al.Average(g => g.N):F2} D={al.Average(g => g.D):F2} K={al.Average(g => g.Knowledge):F2}");
        var kinds = new int[8];
        foreach (var e in w.Events.Log) if (e.Type == EventType.Interpretation) kinds[e.B]++;
        Console.WriteLine("interp: " + string.Join(" ", kinds.Select((k, i) => $"{(Mirozdanie.Core.Culture.InterpretationKind)i}={k}")));
    }
    Console.WriteLine($"Мир {seed} ({p.PresetTitle}): генерация {tGen} мс, жизнь {tLife - tGen} мс (Пробуждение на тике {w.AwakeningTick}), история {years} лет {tHist - tLife} мс, вывод {tOut - tHist} мс.");
    Console.WriteLine($"Событий: {w.Events.Log.Count}; народов: {metrics.Groups}; вер: {metrics.Religions}; языков: {metrics.Languages}; держав: {metrics.Polities}; K = {w.Kardashev:0.00}.");
    {
        var cities = w.Cities.Where(c => c.Alive).ToList();
        var forms = w.Polities.Where(x => x.Alive).GroupBy(x => x.Form).OrderBy(g => g.Key).Select(g => $"{Simulation.FormTitle(g.Key)} {g.Count()}");
        if (Environment.GetEnvironmentVariable("MIRO_CULTURE") == "1")
            for (int t = 0; t < 6; t++)
            {
                var hs = w.Groups.Where(x => x.Alive && x.PolityId >= 0).Select(x => x.Culture[t]).OrderBy(x => x).ToList();
                if (hs.Count > 0) Console.WriteLine($"черта {t}: мин {hs[0]:0.00}, четверть {hs[hs.Count / 4]:0.00}, медиана {hs[hs.Count / 2]:0.00}, три четверти {hs[hs.Count * 3 / 4]:0.00}, макс {hs[^1]:0.00}");
            }
        int Count(EventType t) => w.Events.Log.Count(e => e.Type == t);
        Console.WriteLine($"Города: {cities.Count} (деревень {cities.Count(c => c.Rank == 0)}, городов {cities.Count(c => c.Rank == 1)}, больших {cities.Count(c => c.Rank == 2)}, мегаполисов {cities.Count(c => c.Rank == 3)}); " +
                          $"крупнейший {(cities.Count > 0 ? cities.Max(c => c.Pop) : 0):0} чел.");
        Console.WriteLine($"Формы власти: {string.Join(", ", forms)}; войн {w.Wars.Count}, слияний {Count(EventType.Assimilated)}, союзов {Count(EventType.AllianceFormed)}, " +
                          $"нарушенных договоров {Count(EventType.TreatyBroken)}, войск в походе {w.Armies.Count(x => x.Alive && !x.Defender)}, объявлений {Count(EventType.WarDeclared) + Count(EventType.HolyWar)}, битв {Count(EventType.Battle)}");
        Console.WriteLine($"Петля бога: просьб {Count(EventType.PetitionMade)} (исполнено {Count(EventType.PetitionGranted)}, забыто {Count(EventType.PetitionIgnored)}), догадок {Count(EventType.SimulationSuspected)}, " +
                          $"зондов {w.Events.Log.Count(e => e.Type == EventType.ProbeLaunched && e.B == 0)}, ударов {w.Events.Log.Count(e => e.Type == EventType.ProbeLaunched && e.B == 1)}, знаков {Count(EventType.GeoglyphMade)}, " +
                          $"огней {Count(EventType.LightMessage)}, щитов {Count(EventType.ShieldRaised)}, требований свободы {Count(EventType.FreedomDemanded)}; стиль {Simulation.StyleTitle(w.Style)}; итог: {Mirozdanie.Core.Chronicle.Outcome.Evaluate(w).Title}");
    }
    Console.WriteLine($"Хроника: {Path.Combine(dir, "chronicle.md")} ({chronicle.Length / 1024} КБ); карты: {dir}/*.png");
    return 0;
}

static int Talk(string[] a)
{
    // Образцы речи народов (lore.md, §12): несколько народов, все действия Создателя.
    ulong seed = ulong.Parse(Arg(a, "--seed", "42"));
    int years = int.Parse(Arg(a, "--years", "1200"));
    int count = int.Parse(Arg(a, "--groups", "4"));
    var sim = Simulation.Create(seed, WorldParams.FromPreset(Arg(a, "--preset", "earth")));
    sim.Run(years);
    var w = sim.World;
    var groups = w.Groups.Where(g => g.Alive).OrderByDescending(g => g.Pop).Take(count * 3).Where((g, i) => i % 3 == 0).ToList();
    foreach (var g in groups)
    {
        var conv = new Conversation();
        Console.WriteLine($"\n=== Народ {g.Name.Native} ({ChronicleWriter.AttitudeName(g.Attitude)}), эпоха {g.EraIdx} ===");
        void Show(string you, DialogueLine l) => Console.WriteLine($"> {you}\n  [{l.Speaker}] {l.Native}\n  {l.Translation}\n  ({l.Narration})\n");
        Show("(взор)", Dialogue.Opening(w, g, conv));
        foreach (var act in new[] { DialogueAction.Ask, DialogueAction.Bless, DialogueAction.Bless, DialogueAction.Demand, DialogueAction.Threaten, DialogueAction.Appear, DialogueAction.Teach })
            Show(act.ToString(), Dialogue.Reply(w, g, act, "peace", conv));
    }
    return 0;
}

static int Maps(string[] a)
{
    ulong seed = ulong.Parse(Arg(a, "--seed", "42"));
    string preset = Arg(a, "--preset", "earth");
    string outDir = Arg(a, "--out", "out");
    var p = WorldParams.FromPreset(preset);
    var sw = Stopwatch.StartNew();
    var w = new World(seed, p);
    Console.WriteLine($"Планета {p.Width}×{p.Height} за {sw.ElapsedMilliseconds} мс; материков: {w.Planet.ContinentCount}.");
    string dir = Path.Combine(outDir, seed.ToString(CultureInfo.InvariantCulture));
    Directory.CreateDirectory(dir);
    foreach (var layer in WorldMaps.Layers.Take(6))
        Png.Write(Path.Combine(dir, layer + ".png"), w.Planet.W, w.Planet.H, WorldMaps.Render(w, layer));
    Console.WriteLine($"Карты: {dir}");
    return 0;
}

static int Diversity(string[] a)
{
    int worlds = int.Parse(Arg(a, "--worlds", "30"));
    int years = int.Parse(Arg(a, "--years", "800"));
    string report = Arg(a, "--report", "docs/diversity-report.md");
    string script = Arg(a, "--script", "examples/drought-prophet.json");
    var sw = Stopwatch.StartNew();
    var result = DiversityTest.Run(worlds, years, script.Length > 0 && File.Exists(script) ? InterventionScript.Load(script) : null,
        (i, m) => Console.Error.WriteLine($"мир {i + 1}/{worlds}: {m.Preset} сид {m.Seed} — вер {m.Religions}, языков {m.Languages}, держав {m.Polities}"));
    File.WriteAllText(report, result.Report);
    Console.WriteLine($"Отчёт: {report} ({sw.Elapsed.TotalSeconds:0} с). Итог: {(result.Passed ? "РАЗНООБРАЗИЕ ДОСТАТОЧНО" : "МИРЫ СЛИШКОМ ПОХОЖИ")}");
    return result.Passed ? 0 : 3;
}
