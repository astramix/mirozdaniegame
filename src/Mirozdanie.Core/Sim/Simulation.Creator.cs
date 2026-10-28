using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Вмешательства Создателя, природные события и климат года (interventions-chronicle.md, planet.md).</summary>
public sealed partial class Simulation
{
    private int _scriptIndex;

    /// <param name="replay">Журнал из сохранения: не меняет «сценарий» в заголовке Хроники.</param>
    public void SetScript(InterventionScript? script, bool replay = false)
    {
        World.Script = script;
        if (!replay) World.ScenarioName = script == null ? null : string.IsNullOrEmpty(script.Name) ? "без названия" : script.Name;
        _scriptIndex = 0;
    }

    /// <summary>Команда из зрителя: применится в начале следующего года.</summary>
    public void Enqueue(Intervention it) => World.Commands.Enqueue(it);

    /// <summary>
    /// Применить вмешательство сразу (даже на паузе). Оно считается сделанным в начале следующего года:
    /// так журнал при загрузке повторяет историю байт-в-байт (см. тест ApplyNowReplaysExactly).
    /// </summary>
    public void ApplyNow(Intervention it)
    {
        var w = World;
        if (IsPreLife)
        {
            // Лепка мира до зарождения жизни (terraforming.md, §1): бесплатно, без последствий для народов, отменяемо.
            if (!Intervention.IsTerraform(it.Type)) return;
            if (!_strokeApplied) PushUndo();
            ApplyLifeIntervention(it);
            w.Events.Dispatch();
            return;
        }
        if (w.Era != Era.History) { Enqueue(it); return; }
        long y = w.Year;
        w.Year = y + 1;
        w.Events.CurrentYear = y + 1;
        ApplyIntervention(it);
        w.Events.Dispatch();
        w.Year = y;
        w.Events.CurrentYear = y;
    }

    partial void BeforeLifeTickPartial()
    {
        var w = World;
        var script = w.Script;
        if (script == null) return;
        while (_scriptIndex < script.Items.Count && script.Items[_scriptIndex].Year < 0)
        {
            var it = script.Items[_scriptIndex];
            if (-it.Year > w.LifeTick) break;
            _scriptIndex++;
            ApplyLifeIntervention(it);
        }
    }

    private void ApplyLifeIntervention(Intervention it)
    {
        var w = World;
        var logged = it.Clone();
        logged.Year = w.LifeTick == 0 ? Intervention.BeforeLife : -w.LifeTick;
        logged.Order = w.AppliedLog.Count;
        w.AppliedLog.Add(logged);
        var rng = w.Rng[StreamId.Creator];
        if (Intervention.IsTerraform(it.Type))
        {
            var (tc, tb) = TerraformEventArgs(it);
            ApplyTerraform(it, w.Events.Publish(EventType.Intervention, tc, a: (int)it.Type, b: tb, flags: EventFlags.ByCreator));
            return;
        }
        int ev = w.Events.Publish(EventType.Intervention, -1, a: (int)it.Type, flags: EventFlags.ByCreator);
        if (it.Type == InterventionType.Spark)
        {
            var sp = w.Life.BestMindCandidate();
            sp.ThresholdBonus += 0.15f * it.Magnitude;
            w.Events.Publish(EventType.Spark, sp.OriginCell, a: sp.Id, flags: EventFlags.ByCreator, cause: ev);
        }
        else if (it.Type == InterventionType.Meteor)
        {
            int cell = ResolveCell(it.Target, rng);
            var p = w.Planet;
            int ag = 0;
            var agents = w.Life.Agents;
            for (int i = 0; i < agents.Capacity; i++)
            {
                if (!agents.Alive[i]) continue;
                int c = (int)agents.Y[i] * p.EW + (int)agents.X[i];
                if (p.EcoDistance(c, cell) < 8) { agents.Kill(i); ag++; }
            }
            foreach (var s in w.Life.Species)
                foreach (var c in w.Life.LandCells)
                    if (p.EcoDistance(c, cell) < 8) s.Biomass[c] = 0;
            for (int c = 0; c < w.TempOffset.Length; c++)
            {
                float d = p.EcoDistance(c, cell);
                if (d < 20) w.TempOffset[c] -= 10f * (1 - d / 20f) * it.Magnitude;
            }
            w.Events.Publish(EventType.MeteorImpact, cell, a: ag, magnitude: it.Magnitude, flags: EventFlags.ByCreator, cause: ev);
        }
    }

    private void CreatorStage()
    {
        var w = World;
        var script = w.Script;
        if (script != null)
        {
            while (_scriptIndex < script.Items.Count && script.Items[_scriptIndex].Year <= w.Year)
            {
                var it = script.Items[_scriptIndex++];
                if (it.Year < 0) continue; // эпоха жизни уже прошла
                ApplyIntervention(it);
            }
        }
        while (w.Commands.Count > 0) ApplyIntervention(w.Commands.Dequeue());
    }

    /// <summary>Разбор цели сценария: largest_group, random_group, group:N, polity_capital:N, religion_center:N, cell:x,y.</summary>
    public int ResolveGroup(string target, Rng rng)
    {
        var w = World;
        Group? Largest() { Group? b = null; foreach (var g in w.Groups) if (g.Alive && (b == null || g.Pop > b.Pop)) b = g; return b; }
        if (target.StartsWith("group:") && int.TryParse(target.AsSpan(6), out int n))
        {
            if (n >= 0 && n < w.Groups.Count)
            {
                var g = w.Groups[n];
                if (g.Alive) return g.Id;
                // Ближайшая живая к месту исчезнувшей.
                Group? best = null; float bd = float.MaxValue;
                foreach (var o in w.Groups)
                {
                    if (!o.Alive) continue;
                    float d = w.Planet.EcoDistance(o.Cell, g.Cell);
                    if (d < bd) { bd = d; best = o; }
                }
                return best?.Id ?? -1;
            }
        }
        if (target.StartsWith("polity_capital:") && int.TryParse(target.AsSpan(15), out int pn))
        {
            int k = 0;
            foreach (var p in w.Polities)
            {
                if (!p.Alive) continue;
                if (k++ == pn) return p.Capital;
            }
            // Политий ещё нет — самая крупная группа.
            return Largest()?.Id ?? -1;
        }
        if (target.StartsWith("religion_center:") && int.TryParse(target.AsSpan(16), out int rn))
        {
            if (rn < w.Religions.Count)
            {
                var r = w.Religions[rn];
                Group? best = null;
                foreach (var g in w.Groups) if (g.Alive && g.ReligionId == r.Id && (best == null || g.Pop > best.Pop)) best = g;
                if (best != null) return best.Id;
            }
            return Largest()?.Id ?? -1;
        }
        if (target == "random_group")
        {
            var alive = new List<int>();
            foreach (var g in w.Groups) if (g.Alive) alive.Add(g.Id);
            return alive.Count > 0 ? alive[rng.NextInt(alive.Count)] : -1;
        }
        if (target.StartsWith("cell:"))
        {
            int c = ResolveCell(target, rng);
            return w.CellOwner[c];
        }
        return Largest()?.Id ?? -1;
    }

    public int ResolveCell(string target, Rng rng)
    {
        var w = World;
        if (target.StartsWith("cell:"))
        {
            var parts = target.Substring(5).Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                return w.Planet.EcoIndex(x, Math.Clamp(y, 0, w.Planet.EH - 1));
        }
        if (w.Era == Era.Life)
        {
            var land = w.Life.LandCells;
            return land[rng.NextInt(land.Count)];
        }
        int g = ResolveGroup(target, rng);
        return g >= 0 ? w.Groups[g].Cell : w.Life.LandCells[rng.NextInt(w.Life.LandCells.Count)];
    }

    public void ApplyIntervention(Intervention it)
    {
        var w = World;
        var rng = w.Rng[StreamId.Creator];
        if (w.Era == Era.Life) { ApplyLifeIntervention(it); return; }
        var logged = it.Clone();
        logged.Year = w.Year;
        logged.Order = w.AppliedLog.Count;
        w.AppliedLog.Add(logged);
        if (Intervention.IsTerraform(it.Type))
        {
            var (tc, tb) = TerraformEventArgs(it);
            int tev = w.Events.Publish(EventType.Intervention, tc, a: (int)it.Type, b: tb, c: it.Slow ? 1 : 0, magnitude: it.Magnitude, flags: EventFlags.ByCreator);
            ApplyTerraform(it, tev);
            return;
        }
        if (Intervention.IsWord(it.Type)) { ApplyWord(it); return; }
        // Раненый ракетами Создатель платит за силы на 30 % больше (god-loop.md §6).
        float cost = w.Sandbox ? 0f : Intervention.Cost(it.Type) * it.Magnitude * (w.Year < w.CreatorWoundedUntil ? 1.3f : 1f);
        float weak = 1f;
        var flags = EventFlags.ByCreator;
        if (w.CreatorEnergy < cost)
        {
            // Воля Создателя исполняется, но слабее (faith-creator.md, §8).
            weak = Math.Max(0.25f, w.CreatorEnergy / cost);
            flags |= EventFlags.Weakened;
        }
        w.CreatorEnergy = Math.Max(0f, w.CreatorEnergy - cost);
        float mag = it.Magnitude * weak;
        int targetGroup = it.Type is InterventionType.Meteor or InterventionType.Flood or InterventionType.Winter or InterventionType.Earthquake
                          or InterventionType.CursedPlace && it.Target.StartsWith("cell:") ? -1 : ResolveGroup(it.Target, rng);
        int cell = it.Target.StartsWith("cell:") ? ResolveCell(it.Target, rng) : targetGroup >= 0 ? w.Groups[targetGroup].Cell : ResolveCell(it.Target, rng);
        // Щит державы гасит чудеса в её землях (god-loop.md §6).
        mag *= ShieldFactor(cell);
        int ev = w.Events.Publish(EventType.Intervention, cell, a: (int)it.Type, b: targetGroup, magnitude: mag, flags: flags);
        if ((flags & EventFlags.Weakened) != 0) w.Events.Publish(EventType.CreatorEnergyLow, cell, magnitude: weak, cause: ev);
        w.LastCreatorAct = w.Year;
        var p = w.Planet;

        switch (it.Type)
        {
            case InterventionType.Drought:
            {
                float r = it.Radius > 0 ? it.Radius : 4;
                int dur = it.Duration > 0 ? it.Duration : 3;
                int de = w.Events.Publish(EventType.Drought, cell, a: targetGroup, b: dur, magnitude: mag, flags: flags, cause: ev);
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = r, EndYear = w.Year + dur - 1, Factor = 1f - 0.7f * mag, EventId = de });
                break;
            }
            case InterventionType.Rain:
            {
                float r = it.Radius > 0 ? it.Radius : 4;
                int dur = it.Duration > 0 ? it.Duration : 1;
                // Дождь снимает засухи в радиусе.
                w.Effects.RemoveAll(e => e.Type == InterventionType.Drought && p.EcoDistance(e.Cell, cell) <= r + e.Radius);
                int region = w.RegionOf(cell);
                w.RegionAnomaly[region] = Math.Max(w.RegionAnomaly[region], 0f);
                int re = w.Events.Publish(EventType.Rain, cell, a: targetGroup, magnitude: mag, flags: flags, cause: ev);
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = r, EndYear = w.Year + dur - 1, Factor = 1f + 0.6f * mag, EventId = re });
                break;
            }
            case InterventionType.Harvest:
            {
                float r = it.Radius > 0 ? it.Radius : 1.5f;
                int he = w.Events.Publish(EventType.MiracleHarvest, cell, a: targetGroup, magnitude: mag, flags: flags, cause: ev);
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = r, EndYear = w.Year, Factor = 1f + 1.2f * mag, EventId = he, Override = true });
                break;
            }
            case InterventionType.Lightning:
            {
                var g = targetGroup >= 0 ? w.Groups[targetGroup] : null;
                if (g != null && g.PolityId >= 0)
                {
                    var pol = w.Polities[g.PolityId];
                    if (pol.Capital == g.Id && pol.Leader >= 0 && w.People[pol.Leader].Died < 0)
                    {
                        int le = w.Events.Publish(EventType.LightningStrike, cell, a: g.Id, b: pol.Leader, magnitude: mag, flags: flags, cause: ev);
                        KillLeader(pol, EventType.LeaderStruck, le, flags);
                        break;
                    }
                }
                if (g != null)
                {
                    float frac = 0.02f * mag;
                    g.Pop *= 1 - frac;
                    g.YearLoss += frac;
                }
                w.Events.Publish(EventType.LightningStrike, cell, a: targetGroup, b: -1, magnitude: mag, flags: flags, cause: ev);
                break;
            }
            case InterventionType.Vision:
            {
                if (targetGroup < 0) break;
                var g = w.Groups[targetGroup];
                var prophet = NewPerson(g, PersonRole.Prophet);
                prophet.Message = string.IsNullOrEmpty(it.Message) ? Intervention.Messages[rng.NextInt(Intervention.Messages.Length)] : it.Message;
                w.Events.Publish(EventType.Vision, cell, a: g.Id, b: prophet.Id, magnitude: mag, flags: flags, cause: ev);
                break;
            }
            case InterventionType.Meteor:
            {
                float killR = 2f * mag, hurtR = 6f, winterR = 12f;
                int me = w.Events.Publish(EventType.MeteorImpact, cell, a: targetGroup, magnitude: mag, flags: flags, cause: ev);
                foreach (var g in w.Groups)
                {
                    if (!g.Alive) continue;
                    float d = p.EcoDistance(g.Cell, cell);
                    if (d <= killR) { g.YearLoss = 1f; KillGroup(g); }
                    else if (d <= hurtR)
                    {
                        float frac = 0.45f * (1 - d / hurtR) * mag;
                        g.Pop *= 1 - frac;
                        g.YearLoss += frac;
                    }
                }
                foreach (var s in w.Life.Species)
                    if (!s.Sapient)
                        foreach (var c in w.Life.LandCells)
                            if (p.EcoDistance(c, cell) < 3) s.Biomass[c] = 0;
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = winterR, EndYear = w.Year + 4, Factor = 1f - 0.4f * mag, EventId = me });
                for (int c = 0; c < w.TempOffset.Length; c++)
                {
                    float d = p.EcoDistance(c, cell);
                    if (d < winterR) w.TempOffset[c] -= 3f * (1 - d / winterR) * mag;
                }
                // Кратер на карте — правка слоя высот, чтобы её не стёр следующий пересчёт (terraforming.md, §2).
                int cx = cell % p.EW * p.Scale + p.Scale / 2, cy = cell / p.EW * p.Scale + p.Scale / 2;
                for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= p.H || dx * dx + dy * dy > 9) continue;
                    int i = y * p.W + p.WrapX(cx + dx);
                    if (p.Elevation[i] > 0) p.ElevDelta[i] -= p.Elevation[i] - Math.Max(0.01f, p.Elevation[i] - 0.3f);
                }
                Mirozdanie.Core.Planet.PlanetGenerator.Recompute(p, w.Params);
                RefreshLifeLand();
                break;
            }
            case InterventionType.Spark:
            {
                if (targetGroup < 0) break;
                var g = w.Groups[targetGroup];
                g.Knowledge = Math.Min(1f, g.Knowledge + 0.15f * mag);
                w.Events.Publish(EventType.Spark, cell, a: g.Id, magnitude: mag, flags: flags, cause: ev);
                break;
            }
            default:
                ApplySpell(it, ev, cell, targetGroup, mag, flags, rng);
                break;
        }
        CheckPrayerAnswered(it, ev, cell, targetGroup);
    }

    // ---------- Природа ----------

    private void NaturalStage()
    {
        var w = World;
        var rng = w.Rng[StreamId.Natural];
        var p = w.Planet;
        float threshold = -1.6f + 0.2f * (w.Params.CatastropheRate - 1f);
        for (int r = 0; r < w.RegionAnomaly.Length; r++)
        {
            // AR(1) аномалия осадков: a_t = 0,6·a_{t−1} + 0,8·ε (planet.md).
            w.RegionAnomaly[r] = 0.6f * w.RegionAnomaly[r] + 0.8f * rng.NextGaussian();
            float a = w.RegionAnomaly[r];
            if (!w.RegionDrought[r] && a < threshold)
            {
                int center = RegionFocus(r);
                if (center < 0) continue;
                w.RegionDrought[r] = true;
                w.RegionDroughtEvent[r] = w.Events.Publish(EventType.NaturalDrought, center, a: r, magnitude: Math.Min(1f, -a / 3f));
            }
            else if (w.RegionDrought[r] && a > -0.8f)
            {
                w.RegionDrought[r] = false;
                w.Events.Publish(EventType.DroughtEnded, RegionFocus(r), a: r, magnitude: 0.6f, cause: w.RegionDroughtEvent[r]);
            }
            else if (!w.RegionDrought[r] && a > 2.2f)
            {
                int center = RegionFocus(r);
                if (center >= 0) w.Events.Publish(EventType.Bounty, center, a: r, magnitude: Math.Min(0.7f, a / 4f));
            }
        }
        // Землетрясения на границах плит.
        for (int r = 0; r < w.RegionAnomaly.Length; r++)
        {
            if (!rng.Chance(0.0035 * w.Params.Tectonics * w.Params.CatastropheRate)) continue;
            int focus = -1; float bv = 0.25f;
            foreach (var g in w.Groups)
                if (g.Alive && w.RegionOf(g.Cell) == r && p.EBoundary[g.Cell] > bv) { bv = p.EBoundary[g.Cell]; focus = g.Cell; }
            if (focus < 0) continue;
            int qe = w.Events.Publish(EventType.Earthquake, focus, magnitude: bv);
            foreach (var g in w.Groups)
            {
                if (!g.Alive) continue;
                float d = p.EcoDistance(g.Cell, focus);
                if (d > 4) continue;
                float frac = 0.08f * bv * (1 - d / 5f);
                g.Pop *= 1 - frac;
                g.YearLoss += frac;
            }
            _ = qe;
        }
        foreach (var c in w.Comets)
        {
            if (w.Year < c.NextYear) continue;
            c.Appearances++;
            c.NextYear += c.Period;
            w.Events.Publish(EventType.Comet, -1, a: c.Id, b: c.Period, c: c.Appearances, magnitude: 0.4f);
        }
        // Природные молнии изредка убивают вождей.
        foreach (var pol in w.Polities)
        {
            // ×0,1 к прежней частоте: природная смерть от молнии — редкость раз в века, кара Создателя — нет (civilizations.md, §5).
            if (!pol.Alive || pol.Leader < 0 || !rng.Chance(0.00012)) continue;
            int cap = w.Groups[pol.Capital].Cell;
            int le = w.Events.Publish(EventType.LightningStrike, cap, a: pol.Capital, b: pol.Leader, magnitude: 0.5f);
            KillLeader(pol, EventType.LeaderStruck, le, EventFlags.None);
        }
    }

    private int RegionFocus(int r)
    {
        var w = World;
        Group? best = null;
        foreach (var g in w.Groups) if (g.Alive && w.RegionOf(g.Cell) == r && (best == null || g.Pop > best.Pop)) best = g;
        return best?.Cell ?? -1;
    }

    private void ClimateStage()
    {
        var w = World;
        var p = w.Planet;
        w.Effects.RemoveAll(e => e.EndYear < w.Year);
        for (int c = 0; c < w.NppFactor.Length; c++)
        {
            int r = w.RegionOf(c);
            float a = w.RegionAnomaly[r];
            float f = Math.Clamp(1f + 0.12f * a, 0.5f, 1.3f);
            if (w.RegionDrought[r]) f *= 0.6f;
            w.NppFactor[c] = f;
            w.TempOffset[c] *= 0.8f;
            // Ядерная зима держится годами, пока сажа не осядет (Robock et al. 2007).
            if (w.Year < w.NuclearWinterUntil) w.TempOffset[c] = Math.Min(w.TempOffset[c], -4f);
        }
        foreach (var e in w.Effects)
        {
            if (w.Year < e.StartYear) continue;
            int ex = e.Cell % p.EW, ey = e.Cell / p.EW;
            int R = (int)MathF.Ceiling(e.Radius);
            // Каждая клетка — один раз: при большом радиусе (глобальная зима) окно не должно обходить долготу по кругу.
            int Ry = Math.Min(R, p.EH), x0 = -Math.Min(R, p.EW / 2), x1 = Math.Min(R, p.EW - 1 + x0);
            for (int dy = -Ry; dy <= Ry; dy++)
            for (int dx = x0; dx <= x1; dx++)
            {
                int y = ey + dy;
                if (y < 0 || y >= p.EH) continue;
                int c = p.EcoIndex(ex + dx, y);
                if (MathF.Sqrt(dx * dx + dy * dy) > e.Radius) continue;
                if (e.Override) w.NppFactor[c] = Math.Max(1f, w.NppFactor[c]) * e.Factor;
                else w.NppFactor[c] *= e.Factor;
            }
        }
    }
}
