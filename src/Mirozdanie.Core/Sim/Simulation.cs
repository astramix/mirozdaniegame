using Mirozdanie.Core.Events;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Порядок систем задан здесь и только здесь (docs/design/core-loop.md, «Шаг времени»).</summary>
public sealed partial class Simulation
{
    public readonly World World;

    public Simulation(World world)
    {
        World = world;
        InitPartial();
    }

    partial void InitPartial();

    public static Simulation Create(ulong seed, WorldParams p) => new(new World(seed, p));

    /// <summary>Эпоха жизни целиком: до Пробуждения или до конца отведённых тиков.</summary>
    public void RunLife(Action<int>? progress = null)
    {
        var w = World;
        if (w.LifeTick == 0)
        {
            w.Events.CurrentYear = 0;
            ApplyBeforeLifeScript();
            _undo.Clear();
            w.Events.Publish(EventType.PlanetFormed, w.Planet.HighestCell, a: w.Planet.ContinentCount);
            w.Life.Seed(w.Rng[StreamId.Life], w.Events, w.Params);
            w.Events.Dispatch();
        }
        while (w.Era == Era.Life)
        {
            LifeStep();
            progress?.Invoke(w.LifeTick);
        }
    }

    public void LifeStep()
    {
        var w = World;
        w.LifeTick++;
        w.Events.CurrentYear = w.LifeTick; // в эпоху жизни «год» события = номер тика (переводится Хроникой)
        BeforeLifeTickPartial();
        w.Life.EcologyTick(w.TempOffset, w.NppFactor);
        w.Life.Agents.Tick(w.Life, w.Planet, w.TempOffset, w.Rng[StreamId.Agents], w.Params.MutationRate);
        if (w.LifeTick % 10 == 0)
        {
            w.Life.UpdateSpeciesGenomes();
            w.Life.Reseed(w.Rng[StreamId.Life]);
            w.Life.CheckExtinctions(w.LifeTick, w.Events);
        }
        if (w.LifeTick % 25 == 0)
        {
            int n = w.Life.Species.Count;
            for (int s = 0; s < n; s++) w.Life.TrySpeciation(w.Life.Species[s], w.LifeTick, w.Events);
        }
        // Восстановление метеоритной зимы.
        for (int i = 0; i < w.TempOffset.Length; i++) w.TempOffset[i] *= 0.7f;
        if (w.LifeTick % 10 == 0 && w.LifeTick >= 200)
        {
            var sp = w.Life.CheckAwakening(w.Params);
            if (sp != null) Awaken(sp, forced: false);
        }
        if (w.Era == Era.Life && w.LifeTick >= w.Params.LifeTicks)
            Awaken(w.Life.BestMindCandidate(), forced: true);
        w.Events.Dispatch();
    }

    partial void BeforeLifeTickPartial();
}
