using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;

namespace Mirozdanie.Core.Sim;

/// <summary>Вид правки земли — для толкования, Хроники и эпитетов (docs/design/terraforming.md, §5).</summary>
public enum TerraformKind : byte
{
    Brush, Mountains, Bridge, Strait, Island, Sink, River, Lake, Volcano, Oasis, Desert, Glacier, Thaw,
    Warmer, Colder, SeaRise, SeaFall, Drift,
}

/// <summary>Мазок кисти, который игрок ведёт прямо сейчас (точки копятся, в журнал он попадёт целиком).</summary>
public sealed class TerraformStroke
{
    public readonly BrushMode Mode;
    public readonly float Radius, Strength;
    public readonly bool Slow;
    internal readonly List<(int x, int y)> Points = new();
    internal float FlattenTarget;
    internal Terraform.Layers? Before;
    public float Work { get; internal set; }
    public bool Exhausted { get; internal set; }
    public bool Ended { get; internal set; }
    public int Count => Points.Count;

    internal TerraformStroke(BrushMode mode, float radius, float strength, bool slow)
    {
        Mode = mode; Radius = radius; Strength = strength; Slow = slow;
    }
}

/// <summary>
/// Правка «как природа»: копится и применяется долями раз в <see cref="Interval"/> лет (terraforming.md, §4).
/// Дрейф «как природа» сдвигает сушу на одну эко-клетку за шаг.
/// </summary>
public sealed class GradualEdit
{
    public TerraformKind Kind;
    public int Cell = -1;
    public long NextYear;
    public int StepsLeft;
    public int Interval = 10;
    public int[] Idx = Array.Empty<int>();
    public float[] DElev = Array.Empty<float>(), DPrec = Array.Empty<float>(), DTemp = Array.Empty<float>(),
        DFert = Array.Empty<float>(), DOre = Array.Empty<float>(), DFlow = Array.Empty<float>();
    public float DSea, DGlobalTemp;
    public bool IsDrift;
    public int SeedX, SeedY, LeftDx, LeftDy;
}

public sealed partial class World
{
    /// <summary>Правки «как природа», которые ещё идут.</summary>
    public readonly List<GradualEdit> GradualEdits = new();
    /// <summary>Какие эко-клетки — суша по последнему пересчёту с последствиями (основа для сравнения «до/после»).</summary>
    public bool[] LandMask = Array.Empty<bool>();
}

/// <summary>Терраформирование в симуляции: мазки, силы Земли, последствия для жизни и народов (docs/design/terraforming.md).</summary>
public sealed partial class Simulation
{
    /// <summary>Цена работы кисти: энергия за км·клетку изменённой высоты (terraforming.md, §5).</summary>
    public const float CostPerWork = 0.02f;
    private const int MaxUndo = 16;

    private bool _strokeApplied;
    private TerraformStroke? _activeStroke;
    private readonly List<(Terraform.Layers layers, int log, int events)> _undo = new();

    /// <summary>Лепка до зарождения жизни: правки бесплатны и отменяемы.</summary>
    public bool IsPreLife => World.Era == Era.Life && World.LifeTick == 0;

    /// <summary>Можно ли сейчас менять землю из зрителя (до жизни или в эпоху истории).</summary>
    public bool CanTerraform => IsPreLife || World.Era == Era.History;

    public bool CanUndo => IsPreLife && _undo.Count > 0;

    public static TerraformKind KindOf(InterventionType t) => t switch
    {
        InterventionType.Ridge => TerraformKind.Mountains,
        InterventionType.Bridge => TerraformKind.Bridge,
        InterventionType.Strait => TerraformKind.Strait,
        InterventionType.Island => TerraformKind.Island,
        InterventionType.Sink => TerraformKind.Sink,
        InterventionType.River => TerraformKind.River,
        InterventionType.Lake => TerraformKind.Lake,
        InterventionType.Volcano => TerraformKind.Volcano,
        InterventionType.Oasis => TerraformKind.Oasis,
        InterventionType.Desert => TerraformKind.Desert,
        InterventionType.Glacier => TerraformKind.Glacier,
        InterventionType.Thaw => TerraformKind.Thaw,
        InterventionType.Warmer => TerraformKind.Warmer,
        InterventionType.Colder => TerraformKind.Colder,
        InterventionType.SeaRise => TerraformKind.SeaRise,
        InterventionType.SeaFall => TerraformKind.SeaFall,
        InterventionType.Drift => TerraformKind.Drift,
        _ => TerraformKind.Brush,
    };

    /// <summary>Смысл правки для народов: +1 дар, 0 знамение, −1 вред (terraforming.md, §5).</summary>
    public static int BenefitOf(TerraformKind k, BrushMode brush) => k switch
    {
        TerraformKind.Bridge or TerraformKind.Island or TerraformKind.River or TerraformKind.Lake or TerraformKind.Oasis
            or TerraformKind.Thaw or TerraformKind.SeaFall => 1,
        TerraformKind.Mountains => 0,
        TerraformKind.Brush => Spellbook.BrushOf(brush).Benefit,
        _ => -1,
    };

    /// <summary>«Чудесность» правки для HADD: мгновенно и «как природа» (terraforming.md, §5).</summary>
    public static float AnomalyOf(TerraformKind k, bool slow) => k switch
    {
        TerraformKind.Mountains or TerraformKind.Island or TerraformKind.Bridge or TerraformKind.Strait or TerraformKind.Sink
            => slow ? 0.15f : 1f,
        TerraformKind.River => slow ? 0.1f : 0.8f,
        TerraformKind.Volcano => 0.4f,
        TerraformKind.Drift => slow ? 0.3f : 0.9f,
        TerraformKind.Warmer or TerraformKind.Colder or TerraformKind.SeaRise or TerraformKind.SeaFall => slow ? 0.05f : 0.7f,
        TerraformKind.Brush => slow ? 0.1f : 0.7f,
        _ => slow ? 0.1f : 0.6f,
    };

    /// <summary>Сколько лет идёт правка «как природа».</summary>
    private static int SlowYears(TerraformKind k) => k switch
    {
        TerraformKind.River or TerraformKind.Lake => 30,
        TerraformKind.Oasis or TerraformKind.Desert or TerraformKind.Glacier or TerraformKind.Thaw => 100,
        TerraformKind.Warmer or TerraformKind.Colder or TerraformKind.SeaRise or TerraformKind.SeaFall => 300,
        _ => 200,
    };

    // ---------- Мазки кисти (зритель) ----------

    /// <summary>
    /// Начать мазок. Пока мазок не закончен, время должно стоять: правка применяется к слоям сразу,
    /// а в журнал попадёт целиком в <see cref="EndStroke"/> (terraforming.md, §2, «Детерминизм»).
    /// </summary>
    public TerraformStroke BeginStroke(BrushMode mode, float radius, float strength, bool slow)
    {
        var w = World;
        if (!CanTerraform) throw new InvalidOperationException("Землю можно менять до зарождения жизни или в эпоху истории.");
        var st = new TerraformStroke(mode, radius, strength, slow && w.Era == Era.History);
        if (IsPreLife) PushUndo();
        if (st.Slow) st.Before = Terraform.Snapshot(w.Planet);
        _activeStroke = st;
        return st;
    }

    /// <summary>Один мазок в точке карты. false — сила Создателя иссякла (мазок не записан).</summary>
    public bool StrokeDab(TerraformStroke st, int x, int y)
    {
        var w = World;
        var p = w.Planet;
        if (st.Ended || st.Exhausted) return false;
        if (!CanDab()) { st.Exhausted = true; return false; }
        x = p.WrapX(x);
        y = Math.Clamp(y, 0, p.H - 1);
        if (st.Points.Count == 0) st.FlattenTarget = p.Total(y * p.W + x);
        st.Points.Add((x, y));
        st.Work += SculptDab(st.Mode, x, y, st.Radius, st.Strength, st.FlattenTarget, st.Slow);
        return true;
    }

    /// <summary>Закончить мазок: запись в журнал и последствия (на паузе — сразу, как ApplyNow).</summary>
    public void EndStroke(TerraformStroke st)
    {
        if (st.Ended) return;
        st.Ended = true;
        _activeStroke = null;
        if (st.Points.Count == 0)
        {
            if (IsPreLife && _undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
            return;
        }
        var it = new Intervention
        {
            Type = InterventionType.Sculpt, Target = "map", Brush = Spellbook.BrushOf(st.Mode).Key, Radius = st.Radius, Strength = st.Strength,
            Slow = st.Slow, Path = Intervention.PathOf(st.Points),
        };
        _strokeApplied = true;
        _activeStroke = st;
        try { ApplyNow(it); }
        finally { _strokeApplied = false; _activeStroke = null; }
    }

    /// <summary>Пересчёт планеты без последствий — только для предпросмотра во время мазка.</summary>
    public void RecomputePlanet() => PlanetGenerator.Recompute(World.Planet, World.Params);

    private bool CanDab()
    {
        var w = World;
        return w.Era != Era.History || w.Sandbox || w.CreatorEnergy > 0f;
    }

    private float SculptDab(BrushMode mode, int x, int y, float radius, float strength, float flatten, bool slow)
    {
        var w = World;
        float work = Terraform.Dab(w.Planet, mode, x, y, radius, strength, flatten);
        if (w.Era == Era.History && !w.Sandbox)
            w.CreatorEnergy = Math.Max(0f, w.CreatorEnergy - work * CostPerWork * (slow ? 0.5f : 1f));
        return work;
    }

    /// <summary>Повтор журнала: правки лепки до жизни применяются до зарождения жизни (год <see cref="Intervention.BeforeLife"/>).</summary>
    private void ApplyBeforeLifeScript()
    {
        var script = World.Script;
        if (script == null) return;
        while (_scriptIndex < script.Items.Count && script.Items[_scriptIndex].Year == Intervention.BeforeLife)
            ApplyLifeIntervention(script.Items[_scriptIndex++]);
    }

    // ---------- Отмена (только лепка до жизни) ----------

    private void PushUndo()
    {
        _undo.Add((Terraform.Snapshot(World.Planet), World.AppliedLog.Count, World.Events.Log.Count));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    /// <summary>Отменить последнюю правку лепки до жизни. После зарождения жизни деяние необратимо (terraforming.md, §1).</summary>
    public bool Undo()
    {
        if (!CanUndo) return false;
        var w = World;
        var (layers, log, events) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Terraform.Restore(w.Planet, layers);
        if (w.AppliedLog.Count > log) w.AppliedLog.RemoveRange(log, w.AppliedLog.Count - log);
        w.Events.TruncateTo(events);
        PlanetGenerator.Recompute(w.Planet, w.Params);
        RefreshLifeLand();
        return true;
    }

    // ---------- Применение (живое и при повторе журнала) ----------

    private void ApplyTerraform(Intervention it, int interventionEvent)
    {
        var w = World;
        var p = w.Planet;
        var kind = KindOf(it.Type);
        var pts = it.Points();
        // Без точек сила Земли ничего не делает (глобальным силам точки не нужны).
        if (pts.Count == 0 && kind is not (TerraformKind.Warmer or TerraformKind.Colder or TerraformKind.SeaRise or TerraformKind.SeaFall)) return;
        bool history = w.Era == Era.History;
        bool slow = it.Slow && history;
        float mag = Math.Max(0.1f, it.Magnitude);
        var flags = EventFlags.ByCreator;
        if (it.Type != InterventionType.Sculpt && history && !w.Sandbox)
        {
            // Воля Создателя исполняется, но слабее, если силы мало (faith-creator.md, §8).
            float cost = Intervention.Cost(it.Type) * mag * (slow ? 0.5f : 1f);
            if (w.CreatorEnergy < cost)
            {
                float weak = Math.Max(0.25f, w.CreatorEnergy / cost);
                mag *= weak;
                flags |= EventFlags.Weakened;
                w.Events.Publish(EventType.CreatorEnergyLow, -1, magnitude: weak, cause: interventionEvent);
            }
            w.CreatorEnergy = Math.Max(0f, w.CreatorEnergy - cost);
        }
        int centerFine = pts.Count > 0 ? Math.Clamp(pts[0].y, 0, p.H - 1) * p.W + p.WrapX(pts[0].x) : -1;
        int center = centerFine >= 0 ? EcoOfFine(centerFine) : -1;
        BrushMode brush = Spellbook.BrushByKey(it.Brush)?.Mode ?? BrushMode.Raise;

        if (kind == TerraformKind.Drift && slow)
        {
            if (pts.Count < 2) return;
            var (dx, dy) = DriftSteps(pts[0], pts[1]);
            if (dx == 0 && dy == 0) return;
            w.GradualEdits.Add(new GradualEdit
            {
                Kind = kind, Cell = center, IsDrift = true, SeedX = pts[0].x, SeedY = pts[0].y, LeftDx = dx, LeftDy = dy,
                Interval = 25, NextYear = w.Year + 25, StepsLeft = Math.Max(Math.Abs(dx), Math.Abs(dy)),
            });
            w.Events.Publish(EventType.ContinentDrifted, center, a: dx, b: dy, c: 1, magnitude: 0.15f, cause: interventionEvent);
            return;
        }

        var before = slow ? (_strokeApplied && _activeStroke?.Before != null ? _activeStroke.Before : Terraform.Snapshot(p)) : null;
        float work;
        DriftResult? drift = null;
        if (it.Type == InterventionType.Sculpt && _strokeApplied) work = _activeStroke?.Work ?? 0f;
        else work = Shape(it, pts, mag, slow, brush, ref drift);

        if (slow)
        {
            RegisterGradual(kind, center, before!, SlowYears(kind));
            Terraform.Restore(p, before!);
            int benefit = BenefitOf(kind, brush);
            // Медленная перемена — слабый знак: её силу смертные не чувствуют разом (terraforming.md, §4).
            w.Events.Publish(SlowEventType(kind), center, a: SlowEventA(kind), b: benefit, c: 1, magnitude: 0.15f, cause: interventionEvent);
            return;
        }
        Consequences(kind, brush, center, centerFine, work, mag, drift, flags, interventionEvent);
    }

    /// <summary>Сама форма правки по типу силы. Кисть при повторе журнала проходит те же точки.</summary>
    private float Shape(Intervention it, List<(int x, int y)> pts, float mag, bool slow, BrushMode brush, ref DriftResult? drift)
    {
        var w = World;
        var p = w.Planet;
        (int x, int y) a = pts.Count > 0 ? pts[0] : (0, 0);
        (int x, int y) b = pts.Count > 1 ? pts[1] : a;
        switch (it.Type)
        {
            case InterventionType.Sculpt:
            {
                float radius = it.Radius > 0 ? it.Radius : 6f, strength = it.Strength >= 0 ? it.Strength : 0.6f;
                float flatten = pts.Count > 0 ? p.Total(Math.Clamp(a.y, 0, p.H - 1) * p.W + p.WrapX(a.x)) : 0f;
                float work = 0;
                foreach (var (x, y) in pts)
                {
                    if (!CanDab()) break;
                    work += SculptDab(brush, p.WrapX(x), Math.Clamp(y, 0, p.H - 1), radius, strength, flatten, slow);
                }
                return work;
            }
            case InterventionType.Ridge: return Terraform.Ridge(p, a.x, a.y, b.x, b.y, 3.2f * mag);
            case InterventionType.Bridge: return Terraform.Bridge(p, a.x, a.y, b.x, b.y);
            case InterventionType.Strait: return Terraform.Strait(p, a.x, a.y, b.x, b.y);
            case InterventionType.Island: return Terraform.Island(p, a.x, a.y, 9f * MathF.Sqrt(mag));
            case InterventionType.Sink: return Terraform.Sink(p, a.x, a.y, 9f * MathF.Sqrt(mag));
            case InterventionType.River: return Terraform.River(p, a.x, a.y);
            case InterventionType.Lake: return Terraform.Lake(p, a.x, a.y, 4.5f * MathF.Sqrt(mag));
            case InterventionType.Volcano: return Terraform.Volcano(p, a.x, a.y);
            case InterventionType.Oasis: return Terraform.Moisture(p, a.x, a.y, true);
            case InterventionType.Desert: return Terraform.Moisture(p, a.x, a.y, false);
            case InterventionType.Glacier: return Terraform.Temperature(p, a.x, a.y, -8f * mag);
            case InterventionType.Thaw: return Terraform.Temperature(p, a.x, a.y, 8f * mag);
            case InterventionType.Warmer:
            case InterventionType.Colder:
            {
                // Палеоклимат: −4…−6 °C ледникового максимума ↔ −120 м уровня моря (Clark et al. 2009) — ≈20 м на °C.
                float s = it.Type == InterventionType.Warmer ? 1f : -1f;
                p.GlobalTempDelta += s * mag;
                p.SeaLevel += s * 0.02f * mag;
                return 50f * mag;
            }
            case InterventionType.SeaRise: p.SeaLevel += 0.1f * mag; return 80f * mag;
            case InterventionType.SeaFall: p.SeaLevel -= 0.1f * mag; return 80f * mag;
            case InterventionType.Drift:
            {
                if (pts.Count < 2) return 0;
                var (dx, dy) = DriftSteps(a, b);
                drift = Terraform.Drift(p, a.x, a.y, dx, dy);
                return drift == null ? 0 : drift.FineCells * 0.05f;
            }
        }
        return 0;
    }

    /// <summary>Сдвиг дрейфа в эко-клетках (не больше 16 по каждой оси).</summary>
    private (int dx, int dy) DriftSteps((int x, int y) a, (int x, int y) b)
    {
        var p = World.Planet;
        float fdx = b.x - a.x;
        if (fdx > p.W / 2f) fdx -= p.W; else if (fdx < -p.W / 2f) fdx += p.W;
        int dx = Math.Clamp((int)MathF.Round(fdx / p.Scale), -16, 16);
        int dy = Math.Clamp((int)MathF.Round((b.y - a.y) / (float)p.Scale), -16, 16);
        return (dx, dy);
    }

    /// <summary>Для события вмешательства: эко-клетка первой точки и режим кисти (B), чтобы Хроника знала «где» и «что».</summary>
    private (int cell, int brush) TerraformEventArgs(Intervention it)
    {
        var p = World.Planet;
        var pts = it.Points();
        int cell = pts.Count > 0 ? EcoOfFine(Math.Clamp(pts[0].y, 0, p.H - 1) * p.W + p.WrapX(pts[0].x)) : -1;
        int brush = it.Type == InterventionType.Sculpt ? (int)(Spellbook.BrushByKey(it.Brush)?.Mode ?? BrushMode.Raise) : -1;
        return (cell, brush);
    }

    private int EcoOfFine(int fine)
    {
        var p = World.Planet;
        int x = fine % p.W, y = fine / p.W;
        return Math.Min(p.EH - 1, y / p.Scale) * p.EW + Math.Min(p.EW - 1, x / p.Scale);
    }

    private static EventType SlowEventType(TerraformKind k) => k switch
    {
        TerraformKind.River => EventType.RiverBorn,
        TerraformKind.Warmer or TerraformKind.Colder => EventType.ClimateShifted,
        TerraformKind.SeaRise or TerraformKind.SeaFall => EventType.SeaLevelChanged,
        _ => EventType.Terraformed,
    };

    private static int SlowEventA(TerraformKind k) => k switch
    {
        TerraformKind.Warmer or TerraformKind.SeaRise => 1,
        TerraformKind.Colder or TerraformKind.SeaFall => -1,
        _ => (int)k,
    };

    /// <summary>Разница слоёв «после − до» раскладывается на доли и копится в <see cref="World.GradualEdits"/>.</summary>
    private void RegisterGradual(TerraformKind kind, int center, Terraform.Layers before, int years)
    {
        var w = World;
        var p = w.Planet;
        int steps = Math.Max(1, years / 10);
        var idx = new List<int>();
        int n = p.W * p.H;
        for (int i = 0; i < n; i++)
        {
            if (p.ElevDelta[i] != before.ElevDelta[i] || p.PrecipMul[i] != before.PrecipMul[i] || p.TempDelta[i] != before.TempDelta[i]
                || p.FertDelta[i] != before.FertDelta[i] || p.OreField[i] != before.OreField[i] || p.ExtraFlow[i] != before.ExtraFlow[i])
                idx.Add(i);
        }
        var g = new GradualEdit
        {
            Kind = kind, Cell = center, NextYear = w.Year + 10, StepsLeft = steps, Idx = idx.ToArray(),
            DElev = new float[idx.Count], DPrec = new float[idx.Count], DTemp = new float[idx.Count],
            DFert = new float[idx.Count], DOre = new float[idx.Count], DFlow = new float[idx.Count],
            DSea = (p.SeaLevel - before.SeaLevel) / steps, DGlobalTemp = (p.GlobalTempDelta - before.GlobalTempDelta) / steps,
        };
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k];
            g.DElev[k] = (p.ElevDelta[i] - before.ElevDelta[i]) / steps;
            g.DPrec[k] = (p.PrecipMul[i] - before.PrecipMul[i]) / steps;
            g.DTemp[k] = (p.TempDelta[i] - before.TempDelta[i]) / steps;
            g.DFert[k] = (p.FertDelta[i] - before.FertDelta[i]) / steps;
            g.DOre[k] = (p.OreField[i] - before.OreField[i]) / steps;
            g.DFlow[k] = (p.ExtraFlow[i] - before.ExtraFlow[i]) / steps;
        }
        if (idx.Count > 0 || g.DSea != 0 || g.DGlobalTemp != 0) w.GradualEdits.Add(g);
    }

    /// <summary>Геология года: доли правок «как природа» (в эпоху истории, после вмешательств).</summary>
    private void GeologyStage()
    {
        var w = World;
        if (w.GradualEdits.Count == 0) return;
        var p = w.Planet;
        bool changed = false;
        foreach (var g in w.GradualEdits)
        {
            if (w.Year < g.NextYear || g.StepsLeft <= 0) continue;
            g.NextYear += g.Interval;
            g.StepsLeft--;
            changed = true;
            if (g.IsDrift)
            {
                int sx = Math.Sign(g.LeftDx), sy = Math.Sign(g.LeftDy);
                var res = Terraform.Drift(p, g.SeedX, g.SeedY, sx, sy);
                if (res == null) { g.StepsLeft = 0; continue; }
                ShiftWorld(res, slow: true);
                g.SeedX = p.WrapX(g.SeedX + sx * p.Scale);
                g.SeedY = Math.Clamp(g.SeedY + sy * p.Scale, 0, p.H - 1);
                g.LeftDx -= sx; g.LeftDy -= sy;
                if (g.LeftDx == 0 && g.LeftDy == 0) g.StepsLeft = 0;
                continue;
            }
            for (int k = 0; k < g.Idx.Length; k++)
            {
                int i = g.Idx[k];
                p.ElevDelta[i] += g.DElev[k];
                p.PrecipMul[i] = Math.Clamp(p.PrecipMul[i] + g.DPrec[k], 0.05f, 6f);
                p.TempDelta[i] += g.DTemp[k];
                p.FertDelta[i] = Math.Clamp(p.FertDelta[i] + g.DFert[k], -1f, 1f);
                p.OreField[i] = Math.Clamp(p.OreField[i] + g.DOre[k], 0f, 1f);
                p.ExtraFlow[i] = Math.Max(0f, p.ExtraFlow[i] + g.DFlow[k]);
            }
            p.SeaLevel += g.DSea;
            p.GlobalTempDelta += g.DGlobalTemp;
        }
        w.GradualEdits.RemoveAll(g => g.StepsLeft <= 0);
        if (!changed) return;
        var wasLand = (bool[])w.LandMask.Clone();
        PlanetGenerator.Recompute(p, w.Params);
        RefreshLifeLand(wasLand);
        ReconcileGroups(slow: true, cause: -1);
        UpdateTerritories();
    }

    // ---------- Последствия ----------

    /// <summary>Пересчитать список суши у жизни и маску суши мира.</summary>
    private void RefreshLifeLand(bool[]? wasLand = null)
    {
        var w = World;
        var p = w.Planet;
        wasLand ??= w.LandMask;
        w.Life.RefreshLand(wasLand);
        for (int c = 0; c < w.LandMask.Length; c++) w.LandMask[c] = p.EIsLand(c);
    }

    private void Consequences(TerraformKind kind, BrushMode brush, int center, int centerFine, float work, float mag, DriftResult? drift,
        EventFlags flags, int cause)
    {
        var w = World;
        var p = w.Planet;
        var wasLand = (bool[])w.LandMask.Clone();
        PlanetGenerator.Recompute(p, w.Params);
        if (drift != null)
        {
            ShiftWorld(drift, slow: false);
            wasLand = (bool[])w.LandMask.Clone(); // маска уже сдвинута вместе с сушей
        }
        int killedAgents = 0;
        if (w.Era == Era.Life)
        {
            // Особи в ушедших под воду клетках гибнут (в эпоху жизни их считает Хроника).
            var agents = w.Life.Agents;
            for (int i = 0; i < agents.Capacity; i++)
            {
                if (!agents.Alive[i]) continue;
                int c = (int)agents.Y[i] * p.EW + Math.Min(p.EW - 1, (int)agents.X[i]);
                if (!p.EIsLand(c)) { agents.Kill(i); killedAgents++; }
            }
        }
        RefreshLifeLand(wasLand);
        if (w.Era == Era.History) w.LastCreatorAct = w.Year;
        int benefit = BenefitOf(kind, brush);
        float m = kind == TerraformKind.Brush ? Math.Clamp(work / 40f, 0.15f, 1f) : Math.Min(1f, mag);

        int ev;
        switch (kind)
        {
            case TerraformKind.River:
                ev = w.Events.Publish(EventType.RiverBorn, center, b: benefit, magnitude: m, flags: flags, cause: cause);
                break;
            case TerraformKind.Volcano:
                ev = Eruption(center, centerFine, m, flags, cause);
                break;
            case TerraformKind.SeaRise:
            case TerraformKind.SeaFall:
                ev = w.Events.Publish(EventType.SeaLevelChanged, -1, a: kind == TerraformKind.SeaRise ? 1 : -1, b: (int)MathF.Round(100f * mag),
                    magnitude: m, flags: flags, cause: cause);
                break;
            case TerraformKind.Warmer:
            case TerraformKind.Colder:
                ev = w.Events.Publish(EventType.ClimateShifted, -1, a: kind == TerraformKind.Warmer ? 1 : -1, magnitude: m, flags: flags, cause: cause);
                break;
            case TerraformKind.Drift:
            {
                int at = center;
                if (drift != null && center >= 0)
                {
                    int y = center / p.EW + drift.Dy;
                    if (y >= 0 && y < p.EH) at = p.EcoIndex(center % p.EW + drift.Dx, y);
                }
                ev = w.Events.Publish(EventType.ContinentDrifted, at, a: drift?.Dx ?? 0, b: drift?.Dy ?? 0, magnitude: m, flags: flags, cause: cause);
                break;
            }
            default:
                ev = w.Events.Publish(EventType.Terraformed, center, a: (int)kind, b: benefit, magnitude: m, flags: flags, cause: cause);
                break;
        }
        if (w.Era == Era.Life)
        {
            if (killedAgents > 0) w.Events.Publish(EventType.LandSank, center, a: killedAgents, flags: flags, cause: ev);
            return;
        }
        ReconcileGroups(slow: false, cause: ev);
        UpdateTerritories();
    }

    /// <summary>
    /// Извержение: гибель рядом, пепловая зима (−2,5 °C, затухает), неурожай два года,
    /// через 20 лет — плодородные пеплы (+0,2 к плодородию в радиусе 16 клеток карты).
    /// </summary>
    private int Eruption(int center, int centerFine, float m, EventFlags flags, int cause)
    {
        var w = World;
        var p = w.Planet;
        float dead = 0;
        if (w.Era == Era.History)
        {
            foreach (var g in w.Groups)
            {
                if (!g.Alive) continue;
                float d = p.EcoDistance(g.Cell, center);
                if (d > 2.5f) continue;
                float frac = 0.45f * (1f - d / 2.5f) * m;
                dead += g.Pop * frac;
                g.Pop *= 1f - frac;
                g.YearLoss += frac;
            }
            w.Effects.Add(new ActiveEffect { Type = InterventionType.Volcano, Cell = center, Radius = 6f, EndYear = w.Year + 2, Factor = 1f - 0.3f * m });
            // Плодородный пепел — правка с отсрочкой.
            if (centerFine >= 0)
            {
                int cx = centerFine % p.W, cy = centerFine / p.W;
                var idx = new List<int>();
                var df = new List<float>();
                for (int dy = -16; dy <= 16; dy++)
                for (int dx = -24; dx <= 24; dx++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= p.H) continue;
                    float dd = Terraform.Distance(p, cx, cy, cx + dx, y);
                    if (dd > 16f) continue;
                    idx.Add(y * p.W + p.WrapX(cx + dx));
                    df.Add(0.2f * (1f - dd / 16f));
                }
                int k = idx.Count;
                w.GradualEdits.Add(new GradualEdit
                {
                    Kind = TerraformKind.Volcano, Cell = center, NextYear = w.Year + 20, StepsLeft = 1, Idx = idx.ToArray(), DFert = df.ToArray(),
                    DElev = new float[k], DPrec = new float[k], DTemp = new float[k], DOre = new float[k], DFlow = new float[k],
                });
            }
        }
        else
        {
            foreach (var s in w.Life.Species)
                foreach (var c in w.Life.LandCells)
                    if (p.EcoDistance(c, center) < 1.5f) s.Biomass[c] = 0;
        }
        for (int c = 0; c < w.TempOffset.Length; c++)
        {
            float d = p.EcoDistance(c, center);
            if (d < 10f) w.TempOffset[c] -= 2.5f * (1f - d / 10f) * m;
        }
        return w.Events.Publish(EventType.Eruption, center, a: (int)(dead / 100f), magnitude: m, flags: flags, cause: cause);
    }

    /// <summary>
    /// Народы на ушедшей под воду земле бегут на ближайшую сушу в пределах 6 клеток (на лодках, даже через море)
    /// и теряют 20–60 % людей; если бежать некуда — гибнут: «утопит — и это конец» (terraforming.md, §4).
    /// </summary>
    private void ReconcileGroups(bool slow, int cause)
    {
        var w = World;
        var p = w.Planet;
        var flags = slow ? EventFlags.None : EventFlags.ByCreator;
        int count = w.Groups.Count;
        for (int gi = 0; gi < count; gi++)
        {
            var g = w.Groups[gi];
            if (!g.Alive || p.EIsLand(g.Cell)) continue;
            var (refuge, steps) = FindRefuge(g);
            if (refuge < 0)
            {
                g.YearLoss = 1f;
                w.Events.Publish(EventType.GroupDrowned, g.Cell, a: g.Id, magnitude: 1f, flags: flags, cause: cause);
                KillGroup(g, announce: false);
                continue;
            }
            float loss = Math.Clamp(0.15f + 0.07f * steps + (slow ? 0f : 0.1f), 0.2f, 0.6f);
            g.Pop *= 1f - loss;
            g.YearLoss += loss;
            int from = g.Cell;
            g.Cell = refuge;
            w.Events.Publish(EventType.Refugees, refuge, a: g.Id, b: from, magnitude: loss, flags: flags, cause: cause);
            if (g.Pop < 15f * Demo.Ref) KillGroup(g);
        }
    }

    /// <summary>Ближайшая суша за ≤ 6 шагов (8-соседство): сначала ничья или своя, потом любая. Ничьи — к меньшему индексу.</summary>
    private (int cell, int steps) FindRefuge(Group g)
    {
        var w = World;
        var p = w.Planet;
        const int R = 6;
        int bestFree = -1, bestFreeSteps = int.MaxValue, bestAny = -1, bestAnySteps = int.MaxValue;
        int gx = g.Cell % p.EW, gy = g.Cell / p.EW;
        for (int dy = -R; dy <= R; dy++)
        for (int dx = -R; dx <= R; dx++)
        {
            int y = gy + dy;
            if (y < 0 || y >= p.EH) continue;
            int c = p.EcoIndex(gx + dx, y);
            if (!p.EIsLand(c)) continue;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int owner = w.CellOwner[c];
            bool free = owner < 0 || owner == g.Id || !w.Groups[owner].Alive;
            if (free && (steps < bestFreeSteps || steps == bestFreeSteps && c < bestFree)) { bestFree = c; bestFreeSteps = steps; }
            if (steps < bestAnySteps || steps == bestAnySteps && c < bestAny) { bestAny = c; bestAnySteps = steps; }
        }
        return bestFree >= 0 ? (bestFree, bestFreeSteps) : (bestAny, bestAny >= 0 ? bestAnySteps : 0);
    }

    /// <summary>Дрейф: народы, святыни, звери и растения едут вместе с сушей; на линии удара народы гибнут частично.</summary>
    private void ShiftWorld(DriftResult r, bool slow)
    {
        var w = World;
        var p = w.Planet;
        var moved = r.EcoMoved;
        int ew = p.EW, eh = p.EH;
        int Dest(int c)
        {
            int y = c / ew + r.Dy;
            return y < 0 || y >= eh ? -1 : y * ew + (((c % ew) + r.Dx) % ew + ew) % ew;
        }
        var dest = new bool[moved.Length];
        for (int c = 0; c < moved.Length; c++) if (moved[c]) { int d = Dest(c); if (d >= 0) dest[d] = true; }
        var flags = slow ? EventFlags.None : EventFlags.ByCreator;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            if (moved[g.Cell])
            {
                int d = Dest(g.Cell);
                if (d < 0)
                {
                    w.Events.Publish(EventType.GroupDrowned, g.Cell, a: g.Id, magnitude: 1f, flags: flags);
                    KillGroup(g, announce: false);
                }
                else g.Cell = d;
            }
            else if (dest[g.Cell])
            {
                // Встреча материков: земля вздымается горами под ногами.
                float frac = slow ? 0.08f : 0.25f;
                g.Pop *= 1f - frac;
                g.YearLoss += frac;
            }
        }
        foreach (var s in w.Sites) if (moved[s.Cell]) { int d = Dest(s.Cell); if (d >= 0) s.Cell = d; }
        w.Life.ShiftCells(moved, r.Dx, r.Dy);
        w.Life.Agents.Shift(moved, r.Dx, r.Dy, p);
        if (w.InitialFauna.Length == moved.Length)
        {
            var src = (float[])w.InitialFauna.Clone();
            for (int c = 0; c < moved.Length; c++) if (moved[c]) w.InitialFauna[c] = 0;
            for (int c = 0; c < moved.Length; c++) if (moved[c]) { int d = Dest(c); if (d >= 0) w.InitialFauna[d] = src[c]; }
        }
        // Маска суши едет вместе с сушей, чтобы новая земля на новом месте не считалась «поднявшейся из моря».
        var mask = (bool[])w.LandMask.Clone();
        for (int c = 0; c < moved.Length; c++) if (moved[c]) mask[c] = false;
        for (int c = 0; c < moved.Length; c++) if (moved[c]) { int d = Dest(c); if (d >= 0) mask[d] = w.LandMask[c] || mask[d]; }
        Array.Copy(mask, w.LandMask, mask.Length);
    }
}
