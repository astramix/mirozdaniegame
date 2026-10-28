using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Силы Создателя из книги заклинаний и ответы на молитвы (docs/design/viewer-2.md, §3–4).</summary>
public sealed partial class Simulation
{
    private void ForGroupsNear(int cell, float radius, Action<Group, float> f)
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            float d = w.Planet.EcoDistance(g.Cell, cell);
            if (d <= radius) f(g, d);
        }
    }

    private void ApplySpell(Intervention it, int ev, int cell, int targetGroup, float mag, EventFlags flags, Rng rng)
    {
        var w = World;
        var p = w.Planet;
        var g = targetGroup >= 0 ? w.Groups[targetGroup] : null;
        switch (it.Type)
        {
            case InterventionType.Earthquake:
            {
                float r = it.Radius > 0 ? it.Radius : 3;
                w.Events.Publish(EventType.Earthquake, cell, magnitude: 0.8f * mag, flags: flags, cause: ev);
                ForGroupsNear(cell, r, (o, d) => { float f = 0.09f * mag * (1 - d / (r + 1)); o.Pop *= 1 - f; o.YearLoss += f; });
                break;
            }
            case InterventionType.Flood:
            {
                float r = it.Radius > 0 ? it.Radius : 3;
                int fe = w.Events.Publish(EventType.Flood, cell, magnitude: mag, flags: flags, cause: ev);
                ForGroupsNear(cell, r, (o, d) =>
                {
                    float wet = 0.4f + p.ERiver[o.Cell] + (p.ECoast[o.Cell] ? 0.4f : 0f);
                    float f = Math.Min(0.15f, 0.07f * wet * mag);
                    o.Pop *= 1 - f; o.YearLoss += f;
                });
                // Сначала вода губит урожай, потом ил удобряет землю.
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = r, EndYear = w.Year, Factor = 0.6f, EventId = fe });
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = r, StartYear = w.Year + 1, EndYear = w.Year + 4, Factor = 1.25f, EventId = fe, Override = true });
                break;
            }
            case InterventionType.Winter:
            {
                float r = it.Radius > 0 ? it.Radius : 6;
                int we = w.Events.Publish(EventType.ColdSnap, cell, magnitude: mag, flags: flags, cause: ev);
                for (int c = 0; c < w.TempOffset.Length; c++)
                {
                    float d = p.EcoDistance(c, cell);
                    if (d < r) w.TempOffset[c] -= 7f * (1 - d / r) * mag;
                }
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = r, EndYear = w.Year + 2, Factor = 1f - 0.35f * mag, EventId = we });
                break;
            }
            case InterventionType.Herds:
            {
                float r = it.Radius > 0 ? it.Radius : 4;
                foreach (var s in w.Life.Species)
                {
                    if (!s.Alive || s.Sapient) continue;
                    foreach (var c in w.Life.LandCells)
                        if (p.EcoDistance(c, cell) <= r) s.Biomass[c] = s.Biomass[c] * 2f + (s.Biomass[c] > 0 ? 0.05f : 0f);
                }
                w.Events.Publish(EventType.HerdsReturn, cell, a: targetGroup, magnitude: mag, flags: flags, cause: ev);
                break;
            }
            case InterventionType.Heal:
                if (g == null) break;
                g.Pop *= 1f + 0.1f * mag;
                g.LastPlague = -1000;
                w.Events.Publish(EventType.Healing, g.Cell, a: g.Id, magnitude: mag, flags: flags, cause: ev);
                break;
            case InterventionType.Plague:
            {
                if (g == null) break;
                float f = 0.2f * mag;
                g.Pop *= 1 - f; g.YearLoss += f; g.LastPlague = w.Year;
                w.Events.Publish(EventType.Plague, g.Cell, a: g.Id, magnitude: f, flags: flags, cause: ev);
                break;
            }
            case InterventionType.Inspire:
                if (g == null) break;
                g.Knowledge = Math.Min(1f, g.Knowledge + 0.12f * mag);
                w.Events.Publish(EventType.Inspiration, g.Cell, a: g.Id, magnitude: mag, flags: flags, cause: ev);
                Discoveries(g, rng);
                break;
            case InterventionType.Fertility:
                if (g == null) break;
                g.Pop *= 1f + 0.15f * mag;
                w.Events.Publish(EventType.Blessing, g.Cell, a: g.Id, b: 0, magnitude: mag, flags: flags, cause: ev);
                break;
            case InterventionType.Bless:
            {
                if (g == null) break;
                int be = w.Events.Publish(EventType.Blessing, g.Cell, a: g.Id, b: 1, magnitude: mag, flags: flags, cause: ev);
                g.BlessedUntil = w.Year + 5;
                g.B = Math.Min(1f, g.B + 0.2f * mag);
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = g.Cell, Radius = 2f, EndYear = w.Year + 4, Factor = 1f + 0.5f * mag, EventId = be, Override = true });
                break;
            }
            case InterventionType.Forget:
                if (g == null) break;
                g.MemoryCount = 0; g.MemoryHead = 0; g.Epithets.Clear(); g.AnomalyLog.Clear();
                g.AgentiveCount = 0; g.P *= 0.3f; g.Taboo = -1; g.TabooStrength = 0; g.AnomalyNoticed = false;
                w.Events.Publish(EventType.Forgetting, g.Cell, a: g.Id, magnitude: mag, flags: flags, cause: ev);
                break;
            case InterventionType.Shrine:
            {
                int owner = w.CellOwner[cell];
                int re = w.Events.Publish(EventType.RelicAppeared, cell, a: owner, magnitude: mag, flags: flags, cause: ev);
                if (owner >= 0 && w.Groups[owner].ReligionId >= 0)
                {
                    var og = w.Groups[owner];
                    TryFoundSite(og, w.Events.Get(re));
                    w.Religions[og.ReligionId].Cred += 1f * mag;
                }
                break;
            }
            case InterventionType.CursedPlace:
            {
                int ce = w.Events.Publish(EventType.CursedGround, cell, magnitude: mag, flags: flags, cause: ev);
                w.Effects.Add(new ActiveEffect { Type = it.Type, Cell = cell, Radius = 2f, EndYear = w.Year + 20, Factor = 1f - 0.4f * mag, EventId = ce });
                break;
            }
            case InterventionType.BlessDynasty:
            case InterventionType.CurseDynasty:
            {
                if (g == null || g.PolityId < 0) break;
                var pol = w.Polities[g.PolityId];
                var cap = w.Groups[pol.Capital];
                if (it.Type == InterventionType.BlessDynasty)
                {
                    pol.Curse = 0; pol.MandateLost = false; pol.Treasury = Math.Min(1f, pol.Treasury + 0.3f); pol.Elite *= 0.8f;
                    w.Events.Publish(EventType.DynastyBlessed, cap.Cell, a: cap.Id, b: pol.Id, magnitude: mag, flags: flags, cause: ev);
                }
                else
                {
                    pol.Curse = Math.Min(1f, pol.Curse + 0.6f * mag);
                    w.Events.Publish(EventType.DynastyCursed, cap.Cell, a: cap.Id, b: pol.Id, magnitude: mag, flags: flags, cause: ev);
                }
                break;
            }
            case InterventionType.Peace:
            {
                if (g == null) break;
                if (g.PolityId >= 0) w.Polities[g.PolityId].LastWar = w.Year + 5;
                foreach (var nId in g.Neighbors)
                {
                    var o = w.Groups[nId];
                    if (o.Alive && o.PolityId >= 0) w.Polities[o.PolityId].LastWar = w.Year + 5;
                }
                w.Events.Publish(EventType.PeaceImposed, g.Cell, a: g.Id, b: g.PolityId, magnitude: mag, flags: flags, cause: ev);
                break;
            }
            case InterventionType.Strife:
            {
                if (g == null) break;
                // Самый слабый живой сосед вне державы.
                Group? target = null;
                foreach (var nId in g.Neighbors)
                {
                    var o = w.Groups[nId];
                    if (!o.Alive || (g.PolityId >= 0 && o.PolityId == g.PolityId)) continue;
                    if (target == null || o.Pop < target.Pop) target = o;
                }
                if (target == null) break;
                if (g.PolityId >= 0) Fight(w.Polities[g.PolityId], w.Groups[w.Polities[g.PolityId].Capital], g, target, false, w.Rng[StreamId.Politics]);
                else
                {
                    // Набег без державы: обе стороны теряют людей, слабейший — больше.
                    bool win = g.Pop >= target.Pop;
                    var loser = win ? target : g;
                    g.Pop *= 0.95f; target.Pop *= 0.95f; loser.Pop *= 0.93f;
                    loser.LastDefeat = w.Year;
                    g.Guilt = Math.Min(1f, g.Guilt + 0.3f);
                    w.Events.Publish(EventType.Battle, target.Cell, a: win ? g.Id : target.Id, b: loser.Id, c: -1, magnitude: 0.35f, cause: ev);
                }
                break;
            }
            case InterventionType.Appear:
            {
                if (g == null) break;
                var att = g.Attitude;
                g.P = 1f;
                switch (att)
                {
                    case Attitude.Love: g.B = Math.Min(1f, g.B + 0.4f); g.Culture[CultureTrait.Piety] = Math.Min(1f, g.Piety + 0.1f); break;
                    case Attitude.Fear: g.B -= 0.1f; g.D = Math.Min(1f, g.D + 0.1f); break;
                    case Attitude.Denial: g.N = Math.Max(0f, g.N - 0.4f); break;
                    case Attitude.Rebellion: g.G = Math.Min(1f, g.G + 0.2f); break;
                    default: g.B = Math.Min(1f, g.B + 0.2f); break;
                }
                w.Events.Publish(EventType.CreatorAppeared, g.Cell, a: g.Id, b: (int)att, magnitude: 1f, flags: flags, cause: ev);
                break;
            }
            case InterventionType.Demand:
            {
                if (g == null) break;
                if (Dialogue.Obeys(w, g))
                {
                    float gain = Math.Min(40f, 15f + g.Pop / (500f * Demo.Ref)) * mag;
                    w.CreatorEnergy += gain;
                    g.Pop *= 0.98f;
                    g.Belief[Doctrine.Ritual] = Math.Min(1f, g.Belief[Doctrine.Ritual] + 0.1f);
                    if (g.Attitude == Attitude.Fear) g.G = Math.Min(1f, g.G + 0.03f);
                    w.Events.Publish(EventType.DemandMet, g.Cell, a: g.Id, magnitude: gain, flags: flags, cause: ev);
                }
                else
                {
                    g.G = Math.Min(1f, g.G + 0.2f);
                    w.Events.Publish(EventType.DemandRefused, g.Cell, a: g.Id, magnitude: 0.5f, flags: flags, cause: ev);
                }
                break;
            }
            case InterventionType.Threaten:
                if (g == null) break;
                g.B = Math.Max(-1f, g.B - 0.2f * mag);
                g.P = Math.Min(1f, g.P + 0.2f);
                if (g.Attitude == Attitude.Rebellion) g.G = Math.Min(1f, g.G + 0.15f);
                w.Events.Publish(EventType.Threat, g.Cell, a: g.Id, magnitude: mag, flags: flags, cause: ev);
                break;
            case InterventionType.Teach:
            {
                if (g == null) break;
                string msg = string.IsNullOrEmpty(it.Message) ? "peace" : it.Message;
                if (MessageShifts.TryGetValue(msg, out var shifts))
                    foreach (var (dim, delta) in shifts)
                    {
                        g.Belief[dim] = Math.Clamp(g.Belief[dim] + 0.5f * delta * mag, 0, 1);
                        if (g.ReligionId >= 0)
                        {
                            var r = w.Religions[g.ReligionId];
                            r.Doctrine[dim] = Math.Clamp(r.Doctrine[dim] + 0.3f * delta * mag, 0, 1);
                        }
                    }
                w.Events.Publish(EventType.Teaching, g.Cell, a: g.Id, b: Array.IndexOf(Intervention.Messages, msg), c: g.ReligionId, magnitude: mag, flags: flags, cause: ev);
                break;
            }
        }
    }

    /// <summary>Благая сила, пришедшая к молящемуся народу в срок, — ответ на молитву (viewer-2.md, §4).</summary>
    private void CheckPrayerAnswered(Intervention it, int ev, int cell, int targetGroup)
    {
        var w = World;
        var spell = Spellbook.Get(it.Type);
        if (!spell.Benevolent || it.Type is InterventionType.Vision or InterventionType.Teach or InterventionType.Spark or InterventionType.Inspire) return;
        float radius = spell.Targeting == Targeting.Group ? 0.5f : Math.Max(2f, it.Radius);
        foreach (var g in w.Groups)
        {
            if (!g.Alive || !Dialogue.HasOpenPrayer(w, g)) continue;
            bool hit = g.Id == targetGroup || w.Planet.EcoDistance(g.Cell, cell) <= radius;
            if (!hit) continue;
            g.PrayerAnswered = w.Year;
            g.B = Math.Min(1f, g.B + 0.4f);
            g.P = Math.Min(1f, g.P + 0.2f);
            g.D = 0; g.G *= 0.5f;
            // Услышанная молитва помнится крепче всего: «Внимающий» вытесняет самый старый эпитет.
            if (!g.Epithets.Exists(e => e.Kind == EpithetKind.Hearer))
            {
                if (g.Epithets.Count >= 4) g.Epithets.RemoveAt(0);
                g.Epithets.Add(new Epithet { Kind = EpithetKind.Hearer, Name = EpithetName(w.LangOf(g), EpithetKind.Hearer), Year = w.Year });
            }
            w.CreatorEnergy += 10f;
            w.Events.Publish(EventType.PrayerAnswered, g.Cell, a: g.Id, c: (int)it.Type, magnitude: 0.8f, flags: EventFlags.ByCreator, cause: ev);
        }
    }
}
