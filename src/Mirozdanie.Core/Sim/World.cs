using Mirozdanie.Core.Events;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

public enum Era { Life, History }

/// <summary>Корень состояния мира. Системы — функции над ним (порядок — в <see cref="Simulation"/>).</summary>
public sealed partial class World
{
    public readonly WorldParams Params;
    public readonly ulong Seed;
    public readonly RngStreams Rng;
    public readonly EventBus Events = new();
    public readonly PlanetData Planet;
    public readonly LifeSystem Life;

    /// <summary>Динамический климат эко-сетки: сдвиг температуры (метеоритная зима) и множитель продуктивности.</summary>
    public readonly float[] TempOffset;
    public readonly float[] NppFactor;
    public readonly float[] PrecipFactor;

    public Era Era = Era.Life;
    public int LifeTick;
    public int AwakeningTick = -1;
    public long Year; // год после Пробуждения (в эпоху жизни — отрицательные тысячелетия не ведём)
    public int SapientSpecies = -1;

    public World(ulong seed, WorldParams p)
    {
        Params = p;
        Seed = seed;
        Rng = new RngStreams(seed);
        Planet = PlanetGenerator.Generate(p, Rng[StreamId.Planet]);
        int en = Planet.EW * Planet.EH;
        TempOffset = new float[en];
        NppFactor = new float[en];
        PrecipFactor = new float[en];
        Array.Fill(NppFactor, 1f);
        Array.Fill(PrecipFactor, 1f);
        Life = new LifeSystem(Planet, p.MaxAgents);
        InitHistory();
    }

    partial void InitHistoryPartial();
    private void InitHistory() => InitHistoryPartial();
}
