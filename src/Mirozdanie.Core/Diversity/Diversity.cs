using System.Text;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Diversity;

/// <summary>Сводные показатели мира для теста разнообразия (PLAN.md, «Тесты»).</summary>
public sealed class WorldMetrics
{
    public ulong Seed;
    public string Preset = "";
    public int AwakeningTick;
    public int Groups, Religions, ReligionsEver, Schisms, Cults, Languages, Polities, Wars, Martyrs, Anomalies;
    public float Population, Kardashev, Energy;
    public readonly float[] AttitudeShare = new float[7];
    public Attitude Dominant;
    public readonly bool[] Inventory = new bool[Phonemes.Count];
    public int InventorySize;
    public readonly int[] Forms = new int[5];
    public readonly float[] Subsistence = new float[4];
    public string CreatorName = "";
    public string WorldName = "";

    public static WorldMetrics Measure(World w)
    {
        var m = new WorldMetrics { Seed = w.Seed, Preset = w.Params.Preset, AwakeningTick = w.AwakeningTick, Kardashev = w.Kardashev, Energy = w.CreatorEnergy, WorldName = w.WorldName.Native };
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            m.Groups++;
            m.Population += g.Pop;
            m.AttitudeShare[(int)g.Attitude] += g.Pop;
            m.Subsistence[(int)g.Subsistence] += g.Pop;
        }
        for (int i = 0; i < 7; i++) m.AttitudeShare[i] /= Math.Max(1f, m.Population);
        for (int i = 0; i < 4; i++) m.Subsistence[i] /= Math.Max(1f, m.Population);
        int dom = 0;
        for (int i = 1; i < 7; i++) if (m.AttitudeShare[i] > m.AttitudeShare[dom]) dom = i;
        m.Dominant = (Attitude)dom;
        m.ReligionsEver = w.Religions.Count;
        float bestPop = -1;
        foreach (var r in w.Religions)
        {
            if (r.Alive && r.Adherents > 0) m.Religions++;
            if (r.MaxAdherents > 0 || r.Converted) m.Forms[(int)r.Form]++;
            if (r.AdherentPop > bestPop && r.Alive) { bestPop = r.AdherentPop; m.CreatorName = r.CreatorName.Native; }
        }
        foreach (var e in w.Events.Log)
        {
            switch (e.Type)
            {
                case EventType.Schism: m.Schisms++; break;
                case EventType.CultFounded: m.Cults++; break;
                case EventType.Battle: m.Wars++; break;
                case EventType.Martyrdom: m.Martyrs++; break;
                case EventType.AnomalyNoticed: m.Anomalies++; break;
            }
        }
        foreach (var L in w.Languages) if (L.Alive) m.Languages++;
        foreach (var p in w.Polities) if (p.Alive) m.Polities++;
        if (w.Languages.Count > 0)
        {
            Array.Copy(w.Languages[0].Inventory, m.Inventory, m.Inventory.Length);
            foreach (var b in m.Inventory) if (b) m.InventorySize++;
        }
        return m;
    }
}

public sealed class DiversityResult
{
    public readonly List<WorldMetrics> Worlds = new();
    public readonly List<(string name, float value, float threshold, bool ok)> Criteria = new();
    public bool Passed;
    public string Report = "";
}

public static class DiversityTest
{
    public static WorldMetrics RunWorld(ulong seed, string preset, int years, InterventionScript? script)
    {
        var p = WorldParams.FromPreset(preset);
        var sim = Simulation.Create(seed, p);
        if (script != null) sim.SetScript(script);
        sim.Run(years);
        return WorldMetrics.Measure(sim.World);
    }

    public static DiversityResult Run(int worlds, int years, InterventionScript? script, Action<int, WorldMetrics>? progress = null)
    {
        var res = new DiversityResult();
        var presets = WorldParams.PresetNames;
        for (int i = 0; i < worlds; i++)
        {
            ulong seed = 1000 + (ulong)i * 7919;
            string preset = presets[i % presets.Length];
            // Половина миров — со сценарием Создателя, половина — без.
            var m = RunWorld(seed, preset, years, i % 2 == 0 ? script : null);
            res.Worlds.Add(m);
            progress?.Invoke(i, m);
        }
        Evaluate(res);
        res.Report = Report(res, years, script);
        return res;
    }

    public static void Evaluate(DiversityResult res)
    {
        var ws = res.Worlds;
        // 1. Фонемные инвентари: средняя попарная дистанция Жаккара.
        double jac = 0; int pairs = 0;
        for (int i = 0; i < ws.Count; i++)
            for (int j = i + 1; j < ws.Count; j++)
            {
                int inter = 0, uni = 0;
                for (int k = 0; k < Phonemes.Count; k++)
                {
                    if (ws[i].Inventory[k] && ws[j].Inventory[k]) inter++;
                    if (ws[i].Inventory[k] || ws[j].Inventory[k]) uni++;
                }
                jac += uni > 0 ? 1.0 - inter / (double)uni : 0;
                pairs++;
            }
        float meanJac = pairs > 0 ? (float)(jac / pairs) : 0;
        res.Criteria.Add(("Средняя дистанция Жаккара фонемных инвентарей", meanJac, 0.2f, meanJac >= 0.2f));
        // 2. Разброс числа вер (коэффициент вариации).
        float cv = Cv(ws.Select(w => (float)w.ReligionsEver));
        res.Criteria.Add(("Коэффициент вариации числа возникших вер", cv, 0.2f, cv >= 0.2f));
        // 3. Разные преобладающие позиции к Создателю.
        int doms = ws.Select(w => w.Dominant).Distinct().Count();
        res.Criteria.Add(("Число разных преобладающих позиций к Создателю", doms, 3, doms >= 3));
        // 4. Уникальность имени Создателя главной веры.
        var names = ws.Where(w => w.CreatorName.Length > 0).Select(w => w.CreatorName).ToList();
        float uniq = names.Count > 0 ? names.Distinct().Count() / (float)names.Count : 0;
        res.Criteria.Add(("Доля уникальных имён Создателя (главная вера мира)", uniq, 0.8f, uniq >= 0.8f));
        // 5. Разные образы божественного во всех мирах вместе.
        int forms = 0;
        for (int f = 0; f < 5; f++) if (ws.Any(w => w.Forms[f] > 0)) forms++;
        res.Criteria.Add(("Число разных образов божественного", forms, 3, forms >= 3));
        // 6. Разброс итогового уровня Кардашёва.
        float kStd = Std(ws.Select(w => w.Kardashev));
        res.Criteria.Add(("Стандартное отклонение уровня Кардашёва", kStd, 0.01f, kStd >= 0.01f));
        // 7. Разброс числа держав.
        float pcv = Cv(ws.Select(w => (float)w.Polities));
        res.Criteria.Add(("Коэффициент вариации числа держав", pcv, 0.2f, pcv >= 0.2f));
        res.Passed = res.Criteria.All(c => c.ok);
    }

    private static float Std(IEnumerable<float> xs)
    {
        var l = xs.ToList();
        if (l.Count < 2) return 0;
        float mean = l.Average();
        return MathF.Sqrt(l.Sum(x => (x - mean) * (x - mean)) / (l.Count - 1));
    }

    private static float Cv(IEnumerable<float> xs)
    {
        var l = xs.ToList();
        float mean = l.Count > 0 ? l.Average() : 0;
        return mean > 0 ? Std(l) / mean : 0;
    }

    private static string Report(DiversityResult res, int years, InterventionScript? script)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Отчёт о разнообразии миров");
        sb.AppendLine();
        sb.AppendLine($"Миров: {res.Worlds.Count}, по {years} лет истории после Пробуждения; пресеты по кругу ({string.Join(", ", WorldParams.PresetNames)}); " +
                      $"чётные миры — со сценарием «{script?.Name ?? "нет"}», нечётные — без вмешательств. Команда: `dotnet run -c Release --project src/Mirozdanie.Cli -- diversity --worlds {res.Worlds.Count} --years {years}`.");
        sb.AppendLine();
        sb.AppendLine($"**Итог: {(res.Passed ? "разнообразие достаточно — все критерии выполнены" : "миры слишком похожи — см. невыполненные критерии")}.**");
        sb.AppendLine();
        sb.AppendLine("| Критерий | Значение | Порог | |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var (name, value, thr, ok) in res.Criteria) sb.AppendLine($"| {name} | {value:0.###} | ≥ {thr:0.###} | {(ok ? "✓" : "✗")} |");
        sb.AppendLine();
        sb.AppendLine("| Сид | Пресет | Мир | Пробуждение (тик) | Народов | Население | Вер (живых/всего) | Расколов | Культов | Языков | Держав | Битв | Мучеников | Аномалий | Фонем | Главное имя Создателя | Преобладает | K |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var m in res.Worlds)
            sb.AppendLine($"| {m.Seed} | {m.Preset} | {m.WorldName} | {m.AwakeningTick} | {m.Groups} | {m.Population:0} | {m.Religions}/{m.ReligionsEver} | {m.Schisms} | {m.Cults} | {m.Languages} | {m.Polities} | {m.Wars} | {m.Martyrs} | {m.Anomalies} | {m.InventorySize} | {m.CreatorName} | {Chronicle.ChronicleWriter.AttitudeName(m.Dominant)} | {m.Kardashev:0.00} |");
        sb.AppendLine();
        sb.AppendLine("Доли позиций к Создателю (по населению), в среднем по мирам:");
        sb.AppendLine();
        for (int a = 0; a < 7; a++)
            sb.AppendLine($"- {Chronicle.ChronicleWriter.AttitudeName((Attitude)a)}: {res.Worlds.Average(w => w.AttitudeShare[a]) * 100:0.0} %");
        return sb.ToString();
    }
}
