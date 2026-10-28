using System.Text.Json;
using Mirozdanie.Core.Creator;

namespace Mirozdanie.Core.Sim;

/// <summary>
/// Сохранение мира = сид + законы мира + журнал вмешательств + год (ARCHITECTURE.md, «Детерминизм»;
/// docs/design/viewer-2.md, §2). Загрузка заново прогоняет мир по журналу — детерминизм даёт ту же историю.
/// </summary>
public sealed class WorldSave
{
    public int Version { get; set; } = 1;
    public string WorldName { get; set; } = "";
    public string WorldGloss { get; set; } = "";
    public ulong Seed { get; set; }
    public WorldParams Params { get; set; } = new();
    public long Year { get; set; }
    public bool Sandbox { get; set; }
    /// <summary>Журнал вмешательств в формате сценария (тот же JSON, что `--script`).</summary>
    public string Script { get; set; } = "";
    public string? ScenarioName { get; set; }
    /// <summary>Метка времени сохранения — только для списка в интерфейсе (ставит зритель).</summary>
    public string SavedAt { get; set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static WorldSave FromJson(string json) =>
        JsonSerializer.Deserialize<WorldSave>(json, Options) ?? throw new FormatException("Пустое сохранение");
}

public sealed partial class Simulation
{
    public WorldSave Save(string savedAt = "")
    {
        var w = World;
        return new WorldSave
        {
            WorldName = w.WorldName.Native, WorldGloss = w.WorldName.Gloss, Seed = w.Seed, Params = w.Params, Year = w.Year,
            Sandbox = w.Sandbox, Script = InterventionScript.ToJson("журнал", w.AppliedLog), SavedAt = savedAt, ScenarioName = w.ScenarioName,
        };
    }

    /// <summary>Восстанавливает мир: тот же сид, те же законы, те же вмешательства в те же годы.</summary>
    public static Simulation Load(WorldSave save, Action<long>? progress = null)
    {
        var sim = Create(save.Seed, save.Params);
        sim.World.Sandbox = save.Sandbox;
        if (!string.IsNullOrWhiteSpace(save.Script))
        {
            var script = InterventionScript.Parse(save.Script);
            if (script.Items.Count > 0) sim.SetScript(script, replay: true);
        }
        sim.World.ScenarioName = save.ScenarioName;
        sim.RunLife();
        while (sim.World.Year < save.Year)
        {
            sim.Step();
            progress?.Invoke(sim.World.Year);
        }
        // Заклинания, наложенные «между годами» прямо перед сохранением, применяются сразу — как в оригинале.
        var sc = sim.World.Script;
        while (sc != null && sim._scriptIndex < sc.Items.Count && sc.Items[sim._scriptIndex].Year == sim.World.Year + 1 && sim.World.Era == Era.History)
            sim.ApplyNow(sc.Items[sim._scriptIndex++]);
        return sim;
    }
}
