using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Life;

/// <summary>Индексы признаков генома. Диапазоны — <see cref="Genome.Min"/>/<see cref="Genome.Max"/>. См. docs/design/life.md.</summary>
public static class Trait
{
    public const int Mass = 0;          // log10 кг
    public const int Speed = 1;
    public const int Senses = 2;
    public const int Diet = 3;          // 0 травоядный … 1 хищник
    public const int Fecundity = 4;
    public const int Lifespan = 5;
    public const int ThermalOpt = 6;    // °C
    public const int ThermalWidth = 7;  // °C
    public const int Sociality = 8;
    public const int Cognition = 9;
    public const int Hue = 10;
    public const int Limbs = 11;
    public const int Eyes = 12;
    public const int Lips = 13;
    public const int TongueAgility = 14;
    public const int Nasality = 15;
    public const int LarynxDepth = 16;
    public const int Count = 17;
}

public static class Genome
{
    public static readonly float[] Min = { -2f, 0, 0, 0, 0, 0, -15f, 4f, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    public static readonly float[] Max = { 3.5f, 1, 1, 1, 1, 1, 35f, 20f, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
    /// <summary>Вес признака в генетической дистанции (нормированной на диапазон).</summary>
    public static readonly float[] DistWeight = { 1.5f, 1, 1, 1.5f, 1, 1, 1.2f, 0.6f, 1, 1, 0.8f, 0.6f, 0.6f, 0.5f, 0.5f, 0.5f, 0.5f };
    /// <summary>Относительная сила мутации признака.</summary>
    public static readonly float[] MutScale = { 3f, 1, 1, 0.8f, 1, 1, 1.5f, 1, 1, 1, 1.5f, 0.8f, 0.8f, 0.6f, 0.6f, 0.6f, 0.6f };

    public const int G = Trait.Count;

    /// <summary>Доля локусов, мутирующих у потомка.</summary>
    public const float LocusRate = 0.3f;

    /// <summary>
    /// Мутация: каждый признак с вероятностью 0,3 получает гауссов сдвиг σ = rate·0,02·диапазон·MutScale/√0,3
    /// (дисперсия та же, что при мутации всех признаков, но втрое меньше дорогих гауссианов), с отражением от границ.
    /// </summary>
    public static void Mutate(Span<float> g, Rng rng, float rate)
    {
        const float boost = 1.8257418f; // 1/√0,3
        for (int t = 0; t < G; t++)
        {
            if (rng.NextFloat() >= LocusRate) continue;
            float range = Max[t] - Min[t];
            float v = g[t] + rng.NextGaussian() * rate * 0.02f * boost * range * MutScale[t];
            if (v < Min[t]) v = 2 * Min[t] - v;
            if (v > Max[t]) v = 2 * Max[t] - v;
            g[t] = Math.Clamp(v, Min[t], Max[t]);
        }
    }

    public static float Distance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float sum = 0, wsum = 0;
        for (int t = 0; t < G; t++)
        {
            float d = (a[t] - b[t]) / (Max[t] - Min[t]);
            sum += DistWeight[t] * d * d;
            wsum += DistWeight[t];
        }
        return MathF.Sqrt(sum / wsum) * 2f;
    }

    public static float MassKg(float logMass) => MathF.Pow(10f, logMass);

    /// <summary>Закон Клайбера (Kleiber 1932): базовый метаболизм 3,4·M^0,75 Вт (M в кг).</summary>
    public static float KleiberWatts(float massKg) => 3.4f * MathF.Pow(massKg, 0.75f);

    /// <summary>Темп размножения по метаболической теории (Brown et al. 2004): r ∝ M^−0,25.</summary>
    public static float MaxGrowthRate(float massKg, float fecundity) => 1.2f * (0.3f + fecundity) * MathF.Pow(massKg, -0.25f);

    /// <summary>
    /// Пригодность температуры: гаусс с шириной W. Холодовая ширина растёт с массой
    /// (теплопотеря ∝ M^{−1/3}, правило Бергмана), жаровая — падает (теплоотдача труднее у крупных).
    /// </summary>
    public static float ThermalSuitability(ReadOnlySpan<float> g, float tempC) =>
        ThermalSuitability(g, tempC, MathF.Pow(10f, g[Trait.Mass] / 3f));

    /// <summary>То же с заранее вычисленным M^{1/3} (для горячих циклов).</summary>
    public static float ThermalSuitability(ReadOnlySpan<float> g, float tempC, float m13)
    {
        float m = m13;
        float w = g[Trait.ThermalWidth];
        float dt = tempC - g[Trait.ThermalOpt];
        float width = dt < 0 ? w * (0.6f + 0.25f * m) : w * (1.4f - 0.12f * m);
        width = Math.Max(2f, width);
        return MathF.Exp(-dt * dt / (2 * width * width));
    }
}

public sealed class Species
{
    public int Id;
    public int Parent = -1;
    public readonly float[] Genome = new float[Life.Genome.G];
    public int BornTick;
    public int ExtinctTick = -1;
    public bool Sapient;
    public float[] Biomass = Array.Empty<float>();
    public float TotalBiomass;
    public int AgentCount;
    public int OriginCell;
    public float ThresholdBonus; // «искра» Создателя снижает порог разума

    public bool Alive => ExtinctTick < 0;
    public float Diet => Genome[Trait.Diet];
    public float LogMass => Genome[Trait.Mass];
}
