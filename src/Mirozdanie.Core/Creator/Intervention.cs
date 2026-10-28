using System.Text.Json;

namespace Mirozdanie.Core.Creator;

public enum InterventionType : byte
{
    Drought, Rain, Lightning, Harvest, Vision, Meteor, Spark,
    // Зритель 2 (docs/design/viewer-2.md)
    Earthquake, Heal, Plague, Inspire, Fertility, Winter, Flood, Herds, Forget, Shrine, CursedPlace,
    BlessDynasty, CurseDynasty, Peace, Strife, Appear, Bless, Demand, Threaten, Teach,
    // Терраформирование (docs/design/terraforming.md). Sculpt — мазок кисти, остальные — силы сферы «Земля».
    Sculpt, Ridge, Bridge, Strait, Island, Sink, River, Lake, Volcano, Oasis, Desert, Glacier, Thaw,
    Warmer, Colder, SeaRise, SeaFall, Drift,
    // Слово Создателя (docs/design/god-loop.md): обет и ответ на послание.
    Oath, Reply,
}

/// <summary>Одно вмешательство Создателя (docs/design/interventions-chronicle.md).</summary>
public sealed class Intervention
{
    public long Year;
    public InterventionType Type;
    public string Target = "largest_group";
    public float Radius = -1;
    public int Duration = -1;
    public float Magnitude = 1f;
    public string Message = "";
    public int Order;
    // Терраформирование: точки на карте «x,y;x,y…» (клетки карты), режим кисти, сила, «как природа».
    public string Path = "";
    public string Brush = "";
    public float Strength = -1;
    public bool Slow;

    /// <summary>Год записи для правок до зарождения жизни (лепка мира на экране сотворения).</summary>
    public const long BeforeLife = -1_000_000_000;

    public static bool IsTerraform(InterventionType t) => t >= InterventionType.Sculpt && t <= InterventionType.Drift;

    /// <summary>Слово Создателя: обет или ответ (бесплатно по силе, но память народов дороже).</summary>
    public static bool IsWord(InterventionType t) => t is InterventionType.Oath or InterventionType.Reply;

    /// <summary>Точки пути как пары клеток карты.</summary>
    public List<(int x, int y)> Points()
    {
        var res = new List<(int, int)>();
        if (string.IsNullOrEmpty(Path)) return res;
        foreach (var part in Path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = part.Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) res.Add((x, y));
        }
        return res;
    }

    public static string PathOf(IEnumerable<(int x, int y)> pts) => string.Join(";", pts.Select(p => $"{p.x},{p.y}"));

    public static float Cost(InterventionType t) => Spellbook.Get(t).Cost;

    public static string Russian(InterventionType t) => Spellbook.Get(t).Title.ToLowerInvariant();

    public Intervention Clone() => (Intervention)MemberwiseClone();

    public static readonly string[] Messages = { "peace", "war", "love", "wrath", "knowledge", "obedience", "freedom" };
}

public sealed class InterventionScript
{
    public string Name = "";
    public readonly List<Intervention> Items = new();

    public static InterventionScript Parse(string json)
    {
        var script = new InterventionScript();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new FormatException($"Сценарий: неверный JSON ({e.Message})", e); }
        using (doc)
        {
            JsonElement list;
            if (doc.RootElement.ValueKind == JsonValueKind.Array) list = doc.RootElement;
            else
            {
                if (doc.RootElement.TryGetProperty("name", out var n)) script.Name = n.GetString() ?? "";
                if (!doc.RootElement.TryGetProperty("interventions", out list))
                    throw new FormatException("Сценарий: нужен массив или объект с полем \"interventions\"");
            }
            int order = 0;
            foreach (var el in list.EnumerateArray())
            {
                var it = new Intervention { Order = order++ };
                if (!el.TryGetProperty("year", out var y) || !y.TryGetInt64(out it.Year))
                    throw new FormatException($"Сценарий, запись {it.Order}: нет целого поля \"year\"");
                if (!el.TryGetProperty("type", out var t)) throw new FormatException($"Сценарий, запись {it.Order}: нет поля \"type\"");
                var key = t.GetString() ?? "";
                var spell = Spellbook.ByKey(key)
                    ?? throw new FormatException($"Сценарий, запись {it.Order}: неизвестный тип «{key}». Есть: {string.Join(", ", Spellbook.All.Select(x => x.Key))}");
                it.Type = spell.Type;
                if (el.TryGetProperty("target", out var tg)) it.Target = tg.GetString() ?? it.Target;
                if (el.TryGetProperty("radius", out var r)) it.Radius = r.GetSingle();
                if (el.TryGetProperty("duration", out var d)) it.Duration = d.GetInt32();
                if (el.TryGetProperty("magnitude", out var m)) it.Magnitude = m.GetSingle();
                if (el.TryGetProperty("message", out var msg) && !string.IsNullOrEmpty(msg.GetString()))
                {
                    it.Message = msg.GetString() ?? "";
                    // Для обета и ответа послание — их собственный ключ («rain», «victory:3», «answer», «free»).
                    if (!Intervention.IsWord(it.Type) && Array.IndexOf(Intervention.Messages, it.Message) < 0)
                        throw new FormatException($"Сценарий, запись {it.Order}: неизвестное послание «{it.Message}». Есть: {string.Join(", ", Intervention.Messages)}");
                }
                if (el.TryGetProperty("path", out var path)) it.Path = path.GetString() ?? "";
                if (el.TryGetProperty("brush", out var brush)) it.Brush = brush.GetString() ?? "";
                if (el.TryGetProperty("strength", out var st)) it.Strength = st.GetSingle();
                if (el.TryGetProperty("slow", out var slow)) it.Slow = slow.GetBoolean();
                if (it.Type == InterventionType.Sculpt && Spellbook.BrushByKey(it.Brush) == null)
                    throw new FormatException($"Сценарий, запись {it.Order}: неизвестный режим кисти «{it.Brush}». Есть: {string.Join(", ", Spellbook.Brushes.Select(b => b.Key))}");
                if (IsTerraformNeedingPath(it.Type) && it.Points().Count == 0)
                    throw new FormatException($"Сценарий, запись {it.Order}: силе «{key}» нужны точки \"path\": \"x,y;x,y\" (клетки карты)");
                if (it.Year < 0 && it.Type is not (InterventionType.Spark or InterventionType.Meteor) && !Intervention.IsTerraform(it.Type))
                    throw new FormatException($"Сценарий, запись {it.Order}: до Пробуждения возможны только spark, meteor и силы Земли");
                script.Items.Add(it);
            }
        }
        // Стабильная сортировка: по году, затем по порядку в файле.
        script.Items.Sort((a, b) => a.Year != b.Year ? a.Year.CompareTo(b.Year) : a.Order.CompareTo(b.Order));
        return script;
    }

    private static bool IsTerraformNeedingPath(InterventionType t) =>
        Intervention.IsTerraform(t) && t is not (InterventionType.Warmer or InterventionType.Colder or InterventionType.SeaRise or InterventionType.SeaFall);

    public static InterventionScript Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>JSON того же формата, что читает <see cref="Parse"/> (для сохранений).</summary>
    public static string ToJson(string name, IEnumerable<Intervention> items)
    {
        var list = items.Select(i =>
        {
            var d = new Dictionary<string, object>
            {
                ["year"] = i.Year, ["type"] = Spellbook.Get(i.Type).Key, ["target"] = i.Target, ["radius"] = i.Radius,
                ["duration"] = i.Duration, ["magnitude"] = i.Magnitude, ["message"] = i.Message,
            };
            if (!string.IsNullOrEmpty(i.Path)) d["path"] = i.Path;
            if (!string.IsNullOrEmpty(i.Brush)) d["brush"] = i.Brush;
            if (i.Strength >= 0) d["strength"] = i.Strength;
            if (i.Slow) d["slow"] = true;
            return d;
        }).ToList();
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = name, ["interventions"] = list },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
