namespace Mirozdanie.Core.Sim;

/// <summary>Законы мира (экран сотворения). См. docs/design/planet.md, life.md, faith-creator.md.</summary>
public sealed class WorldParams
{
    public string Preset { get; set; } = "earth";
    public string PresetTitle { get; set; } = "Земля";
    public int Width { get; set; } = 512;
    public int Height { get; set; } = 256;
    public int EcoScale { get; set; } = 4;

    // Физика планеты
    public float OceanFraction { get; set; } = 0.62f;
    public float MeanTemperature { get; set; } = 14f;
    public float AxialTilt { get; set; } = 23.4f;
    public float Tectonics { get; set; } = 1f;
    public float StarBrightness { get; set; } = 1f;
    public float PrecipitationScale { get; set; } = 1f;
    public int PlateCount { get; set; } = 9;

    // Жизнь
    public float MutationRate { get; set; } = 1f;
    public float CatastropheRate { get; set; } = 1f;
    public int LifeTicks { get; set; } = 900;
    public int MaxAgents { get; set; } = 20_000;

    // Разум
    /// <summary>Порог когнитивности для Пробуждения (life.md).</summary>
    public float MindThreshold { get; set; } = 0.6f;
    public float SocialThreshold { get; set; } = 0.5f;
    /// <summary>−1 … 1: базовая склонность к конфликту (−) или кооперации (+).</summary>
    public float CooperationBias { get; set; } = 0f;

    // Создатель
    /// <summary>0 — скрытый (чудеса неоднозначны), 1 — явный.</summary>
    public float CreatorPresence { get; set; } = 0.5f;
    public float StartEnergy { get; set; } = 100f;

    // Лестница Кардашёва (docs/design/kardashev.md, §9)
    /// <summary>Общий темп открытий.</summary>
    public float TechPace { get; set; } = 1f;
    /// <summary>Множитель запасов угля, нефти и урана.</summary>
    public float ResourceRichness { get; set; } = 1f;
    /// <summary>Чувствительность климата λ, К/(Вт/м²) (IPCC AR5: 0,5–1,2).</summary>
    public float CarbonSensitivity { get; set; } = 0.8f;
    /// <summary>Петля бога (god-loop.md §9): зависимость от чуда за доброе деяние, частота просьб, рост догадки о симуляции.</summary>
    public float DependenceRate { get; set; } = 0.15f;
    public float PetitionRate { get; set; } = 1f;
    public float SuspicionRate { get; set; } = 1f;
    /// <summary>Склонность держав к ядерной войне.</summary>
    public float NuclearRisk { get; set; } = 1f;

    public static readonly string[] PresetNames = { "earth", "ice", "archipelago", "mars", "chaos" };

    public static WorldParams FromPreset(string name)
    {
        var p = new WorldParams { Preset = name };
        switch (name)
        {
            case "earth":
                break;
            case "ice":
                p.PresetTitle = "Ледяной мир";
                p.MeanTemperature = -8f; p.OceanFraction = 0.5f; p.PrecipitationScale = 0.8f;
                break;
            case "archipelago":
                p.PresetTitle = "Архипелаг";
                p.OceanFraction = 0.8f; p.Tectonics = 1.4f; p.PlateCount = 14; p.MeanTemperature = 18f;
                break;
            case "mars":
                p.PresetTitle = "Пустынный Марс";
                p.OceanFraction = 0.25f; p.PrecipitationScale = 0.35f; p.MeanTemperature = 16f; p.ResourceRichness = 0.4f;
                break;
            case "chaos":
                p.PresetTitle = "Хаос";
                p.Tectonics = 2f; p.CatastropheRate = 3f; p.AxialTilt = 40f; p.MutationRate = 2f;
                p.CooperationBias = -0.4f; p.CreatorPresence = 0.9f; p.NuclearRisk = 2.5f;
                break;
            default:
                throw new ArgumentException($"Неизвестный пресет «{name}». Есть: {string.Join(", ", PresetNames)}");
        }
        return p;
    }

    public int EcoWidth => Width / EcoScale;
    public int EcoHeight => Height / EcoScale;
}
