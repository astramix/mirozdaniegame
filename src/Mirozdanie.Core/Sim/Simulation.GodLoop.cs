using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>
/// Игровая петля бога (docs/design/god-loop.md): зависимость от чуда, просьбы народов, обеты Создателя,
/// стиль бога, догадка о симуляции и контакт поздних эпох.
/// </summary>
public sealed partial class Simulation
{
    private int _godCursor;
    private int _terraformActs;

    // ---------- Стадия ----------

    private void GodLoopStage()
    {
        var w = World;
        var rng = w.Rng[StreamId.GodLoop];
        // 1. Новые события: деяния Создателя, битвы, моры — для зависимости, обетов и просьб.
        var log = w.Events.Log;
        for (; _godCursor < log.Count; _godCursor++) OnGodEvent(log[_godCursor]);
        // 2. Зависимость затухает (2 %/год).
        foreach (var g in w.Groups) if (g.Alive && g.Dependence > 0) g.Dependence *= 0.98f;
        // 3. Сроки обетов и просьб.
        foreach (var o in w.Oaths)
        {
            if (o.Status != 0) continue;
            var g = w.Groups[o.Group];
            if (!g.Alive) { o.Status = 2; continue; }
            if (w.Year < o.Deadline) continue;
            // К сроку: «защищу» и «не трону» сдержаны, если не нарушены; прочие — нарушены, если не исполнены.
            if (o.Kind is OathKind.Protect or OathKind.Spare) KeepOath(o); else BreakOath(o);
        }
        foreach (var pt in w.Petitions)
        {
            if (pt.Status != 0) continue;
            var g = w.Groups[pt.Group];
            if (!g.Alive) { pt.Status = 2; continue; }
            if (w.Year < pt.Deadline) continue;
            pt.Status = -1;
            g.D = Math.Min(1f, g.D + 0.1f);
            g.G = Math.Min(1f, g.G + 0.05f);
            w.Events.Publish(EventType.PetitionIgnored, g.Cell, a: g.Id, b: (int)pt.Kind, magnitude: 0.3f, cause: pt.Event);
        }
        // 4. Новые просьбы.
        Petitions(rng);
        // 5. Стиль бога — раз в 25 лет.
        if (w.Year % 25 == 0) UpdateStyle();
        // 6. Догадка о симуляции и контакт.
        Suspicion(rng);
    }

    private static bool Benevolent(EventType t) => t is EventType.Blessing or EventType.Healing or EventType.Rain or EventType.MiracleHarvest
        or EventType.PrayerAnswered or EventType.HerdsReturn;

    private static bool Harmful(EventType t) => t is EventType.LightningStrike or EventType.Drought or EventType.ColdSnap or EventType.Plague
        or EventType.MeteorImpact or EventType.Flood or EventType.CursedGround or EventType.Threat or EventType.Earthquake or EventType.DynastyCursed;

    /// <summary>Народы, которых касается событие: по A (если это народ) или по клетке в радиусе r.</summary>
    private void ForAffected(WorldEvent e, float r, Action<Group> act)
    {
        var w = World;
        bool byGroup = e.Type is EventType.Blessing or EventType.Healing or EventType.PrayerAnswered or EventType.Threat or EventType.Plague
            or EventType.Teaching or EventType.CreatorAppeared;
        if (byGroup)
        {
            if (e.A >= 0 && e.A < w.Groups.Count && w.Groups[e.A].Alive) act(w.Groups[e.A]);
            return;
        }
        if (e.Type == EventType.DynastyCursed)
        {
            if (e.A >= 0 && e.A < w.Groups.Count && w.Groups[e.A].Alive) act(w.Groups[e.A]);
            return;
        }
        if (e.Cell < 0) return;
        foreach (var g in w.Groups)
            if (g.Alive && w.Planet.EcoDistance(g.Cell, e.Cell) <= r) act(g);
    }

    private void OnGodEvent(WorldEvent e)
    {
        var w = World;
        if (e.Type == EventType.Intervention && Intervention.IsTerraform((InterventionType)e.A)) _terraformActs++;
        // Зависимость от чуда.
        if (e.ByCreator && Benevolent(e.Type))
            ForAffected(e, 5f, g => g.Dependence = Math.Min(1f, g.Dependence + w.Params.DependenceRate));
        // Обеты.
        foreach (var o in w.Oaths)
        {
            if (o.Status != 0 || e.Year < o.Year) continue;
            var g = w.Groups[o.Group];
            if (!g.Alive) continue;
            switch (o.Kind)
            {
                case OathKind.Rain when e.Type is EventType.Rain or EventType.MiracleHarvest or EventType.DroughtEnded or EventType.Bounty
                                        && e.Cell >= 0 && w.Planet.EcoDistance(e.Cell, g.Cell) <= 6f:
                    KeepOath(o); break;
                case OathKind.Victory when e.Type == EventType.Battle && Wins(e, g) && LoserPolity(e) == o.Target:
                    KeepOath(o); break;
                case OathKind.Protect when e.Type == EventType.Battle && e.B == g.Id:
                    BreakOath(o); break;
                case OathKind.Spare when e.ByCreator && Harmful(e.Type) && Touches(e, g):
                    BreakOath(o); break;
                case OathKind.Answer when e.Type is EventType.Teaching or EventType.CreatorAppeared or EventType.ContactAnswered && e.A == g.Id:
                    KeepOath(o); break;
            }
        }
        // Просьбы.
        foreach (var pt in w.Petitions)
        {
            if (pt.Status != 0 || e.Year < pt.Year) continue;
            var g = w.Groups[pt.Group];
            if (!g.Alive) continue;
            bool granted = pt.Kind switch
            {
                PetitionKind.Healing => e.Type == EventType.Healing && e.A == g.Id,
                PetitionKind.Victory => e.Type == EventType.Battle && Wins(e, g) && LoserPolity(e) == pt.Target,
                PetitionKind.Punish => e.ByCreator && Harmful(e.Type) && e.Cell >= 0 && OwnerPolity(e.Cell) == pt.Target
                                       || e.Type == EventType.Battle && LoserPolity(e) == pt.Target,
                PetitionKind.Sign => e.ByCreator && e.Type != EventType.Intervention && Touches(e, g),
                PetitionKind.Answer => e.Type is EventType.Teaching or EventType.CreatorAppeared or EventType.ContactAnswered && e.A == g.Id,
                _ => false,
            };
            if (!granted) continue;
            pt.Status = 1;
            g.B = Math.Min(1f, g.B + 0.15f);
            g.D = Math.Max(0f, g.D - 0.05f);
            w.Events.Publish(EventType.PetitionGranted, g.Cell, a: g.Id, b: (int)pt.Kind, magnitude: 0.4f, cause: e.Id);
        }
    }

    private bool Wins(WorldEvent battle, Group g)
    {
        var w = World;
        var winner = w.Groups[battle.A];
        return battle.A == g.Id || g.PolityId >= 0 && winner.PolityId == g.PolityId;
    }

    private int LoserPolity(WorldEvent battle)
    {
        var loser = World.Groups[battle.B];
        return loser.PolityId;
    }

    private int OwnerPolity(int cell)
    {
        var w = World;
        int gid = w.CellOwner.Length > 0 ? w.CellOwner[cell] : -1;
        return gid >= 0 ? w.Groups[gid].PolityId : -1;
    }

    private bool Touches(WorldEvent e, Group g)
    {
        if (e.A == g.Id && e.Type is not (EventType.Rain or EventType.Drought or EventType.MiracleHarvest)) return true;
        return e.Cell >= 0 && World.Planet.EcoDistance(e.Cell, g.Cell) <= 4f;
    }

    // ---------- Обеты ----------

    private void KeepOath(Oath o)
    {
        var w = World;
        var g = w.Groups[o.Group];
        o.Status = 1;
        g.B = Math.Min(1f, g.B + 0.25f);
        g.D = Math.Max(0f, g.D - 0.1f);
        GiveEpithet(g, EpithetKind.TrueWord);
        w.Events.Publish(EventType.OathKept, g.Cell, a: g.Id, b: (int)o.Kind, magnitude: 0.5f, cause: o.Event);
    }

    private void BreakOath(Oath o)
    {
        var w = World;
        var g = w.Groups[o.Group];
        o.Status = -1;
        g.D = Math.Min(1f, g.D + 0.3f);
        g.G = Math.Min(1f, g.G + 0.2f);
        g.B = Math.Max(0f, g.B - 0.15f);
        GiveEpithet(g, EpithetKind.Deceiver);
        w.Events.Publish(EventType.OathBroken, g.Cell, a: g.Id, b: (int)o.Kind, magnitude: 0.6f, cause: o.Event);
    }

    private void GiveEpithet(Group g, EpithetKind k)
    {
        foreach (var e in g.Epithets) if (e.Kind == k) return;
        if (g.Epithets.Count >= 6) g.Epithets.RemoveAt(g.Epithets.Count - 1);
        g.Epithets.Insert(0, new Epithet { Kind = k, Name = EpithetName(World.LangOf(g), k), Year = World.Year });
        World.Events.Publish(EventType.EpithetGained, g.Cell, a: g.Id, b: (int)k, magnitude: 0.5f);
    }

    public static int OathYears(OathKind k) => k switch
    {
        OathKind.Rain => 10, OathKind.Victory => 20, OathKind.Protect => 30, OathKind.Spare => 50, _ => 10,
    };

    public static string OathTitle(OathKind k) => k switch
    {
        OathKind.Rain => "пошлю дождь", OathKind.Victory => "дам победу", OathKind.Protect => "защищу вас", OathKind.Spare => "не трону вас",
        _ => "отвечу вашим мудрецам",
    };

    /// <summary>Слово Создателя: обет («oath», Message = «вид:цель») или ответ на послание («reply», Message = answer | free | silence).</summary>
    private void ApplyWord(Intervention it)
    {
        var w = World;
        var rng = w.Rng[StreamId.Creator];
        int gid = ResolveGroup(it.Target, rng);
        if (gid < 0) return;
        var g = w.Groups[gid];
        float cost = w.Sandbox ? 0f : Intervention.Cost(it.Type);
        w.CreatorEnergy = Math.Max(0f, w.CreatorEnergy - cost);
        int ev = w.Events.Publish(EventType.Intervention, g.Cell, a: (int)it.Type, b: gid, flags: EventFlags.ByCreator);
        w.LastCreatorAct = w.Year;
        g.LastCreatorSeen = w.Year;
        if (it.Type == InterventionType.Oath)
        {
            var parts = it.Message.Split(':');
            if (!Enum.TryParse<OathKind>(parts[0], true, out var kind)) kind = OathKind.Rain;
            int target = parts.Length > 1 && int.TryParse(parts[1], out int t) ? t : -1;
            var o = new Oath { Id = w.Oaths.Count, Group = gid, Kind = kind, Target = target, Year = w.Year, Deadline = w.Year + OathYears(kind) };
            o.Event = w.Events.Publish(EventType.OathSworn, g.Cell, a: gid, b: (int)kind, c: target, magnitude: 0.3f, flags: EventFlags.ByCreator, cause: ev);
            w.Oaths.Add(o);
            // Само обещание уже сближает: народ надеется.
            g.B = Math.Min(1f, g.B + 0.05f);
            return;
        }
        // Ответ на послание учёных (god-loop.md §6).
        switch (it.Message)
        {
            case "free":
                g.Freed = true;
                if (g.PolityId >= 0)
                {
                    var pol = w.Polities[g.PolityId];
                    pol.Shield = true;
                    foreach (var m in pol.Members) w.Groups[m].Freed = true;
                }
                g.G = Math.Max(0f, g.G - 0.5f);
                g.B = Math.Min(1f, g.B + 0.1f);
                w.Events.Publish(EventType.ContactAnswered, g.Cell, a: gid, b: 1, magnitude: 0.8f, flags: EventFlags.ByCreator, cause: ev);
                break;
            case "silence":
                g.Suspicion = Math.Max(0f, g.Suspicion - 0.2f);
                w.Events.Publish(EventType.ContactAnswered, g.Cell, a: gid, b: 2, magnitude: 0.3f, flags: EventFlags.ByCreator, cause: ev);
                break;
            default:
                g.Suspicion = 1f;
                g.B = Math.Min(1f, g.B + 0.2f);
                g.D = Math.Max(0f, g.D - 0.2f);
                w.Events.Publish(EventType.ContactAnswered, g.Cell, a: gid, b: 0, magnitude: 0.8f, flags: EventFlags.ByCreator, cause: ev);
                break;
        }
    }

    // ---------- Просьбы ----------

    private void Petitions(Rng rng)
    {
        var w = World;
        foreach (var g in w.Groups)
        {
            // Просят крупные народы (≥ 2 % людей мира), не чаще раза в 50 лет: просьба — событие, а не шум.
            if (!g.Alive || g.ReligionId < 0 || g.Piety < 0.4f || w.Year - g.LastPetition < 50 || g.Pop < 0.02 * w.WorldPopulation) continue;
            if (!rng.Chance(0.02 * w.Params.PetitionRate)) continue;
            PetitionKind? kind = null;
            int target = -1;
            int enemy = EnemyPolity(g);
            if (w.Year - g.LastPlague <= 5) kind = PetitionKind.Healing;
            else if (enemy >= 0 && w.Year - g.LastDefeat <= 10) { kind = PetitionKind.Victory; target = enemy; }
            else if (enemy >= 0) { kind = PetitionKind.Punish; target = enemy; }
            else if (g.EraIdx >= 5 && g.N >= 0.35f) kind = PetitionKind.Answer;
            else if (g.Attitude == Attitude.Love && (g.LastCreatorSeen < 0 || w.Year - g.LastCreatorSeen >= 100)) kind = PetitionKind.Sign;
            if (kind == null) continue;
            g.LastPetition = w.Year;
            var pt = new Petition { Id = w.Petitions.Count, Group = g.Id, Kind = kind.Value, Target = target, Year = w.Year, Deadline = w.Year + 15 };
            pt.Event = w.Events.Publish(EventType.PetitionMade, g.Cell, a: g.Id, b: (int)kind.Value, c: target, magnitude: 0.25f);
            w.Petitions.Add(pt);
        }
    }

    /// <summary>Держава, на которую у державы народа есть обида (первая по порядку отношений).</summary>
    public int EnemyPolity(Group g)
    {
        var w = World;
        if (g.PolityId < 0) return -1;
        foreach (var r in w.Relations)
        {
            if (r.A != g.PolityId && r.B != g.PolityId) continue;
            int other = r.A == g.PolityId ? r.B : r.A;
            if (!w.Polities[other].Alive) continue;
            foreach (var gr in r.Grievances) if (gr.Holder == g.PolityId) return other;
        }
        return -1;
    }

    public static string PetitionTitle(PetitionKind k) => k switch
    {
        PetitionKind.Victory => "победы над врагом", PetitionKind.Punish => "кары для обидчика", PetitionKind.Healing => "исцеления от мора",
        PetitionKind.Sign => "знака, что вы рядом", _ => "ответа своим мудрецам",
    };

    /// <summary>Открытая просьба народа (или null).</summary>
    public Petition? OpenPetition(Group g)
    {
        for (int i = World.Petitions.Count - 1; i >= 0; i--)
        {
            var p = World.Petitions[i];
            if (p.Group == g.Id && p.Status == 0) return p;
        }
        return null;
    }

    // ---------- Стиль бога ----------

    public static string StyleTitle(CreatorStyle s) => s switch
    {
        CreatorStyle.Merciful => "Милостивый", CreatorStyle.Wrathful => "Грозный", CreatorStyle.Silent => "Молчаливый",
        CreatorStyle.Teacher => "Учитель", CreatorStyle.Architect => "Зодчий", CreatorStyle.Deceiver => "Обманщик",
        CreatorStyle.Fickle => "Переменчивый", _ => "Неведомый",
    };

    /// <summary>«Зовут вас …» — творительный падеж.</summary>
    public static string StyleTitleIns(CreatorStyle s) => s switch
    {
        CreatorStyle.Merciful => "Милостивым", CreatorStyle.Wrathful => "Грозным", CreatorStyle.Silent => "Молчаливым",
        CreatorStyle.Teacher => "Учителем", CreatorStyle.Architect => "Зодчим", CreatorStyle.Deceiver => "Обманщиком",
        CreatorStyle.Fickle => "Переменчивым", _ => "Неведомым",
    };

    public CreatorStyle ComputeStyle()
    {
        var w = World;
        int kind = 0, wrath = 0, teach = 0, earth = 0, total = 0;
        foreach (var it in w.AppliedLog)
        {
            if (it.Year < 0) continue;
            total++;
            if (Intervention.IsTerraform(it.Type)) { earth++; continue; }
            if (Intervention.IsWord(it.Type) || it.Type is InterventionType.Teach or InterventionType.Appear or InterventionType.Vision or InterventionType.Inspire) { teach++; continue; }
            if (Spellbook.Get(it.Type).Benevolent) kind++; else wrath++;
        }
        int broken = 0, sworn = 0;
        foreach (var o in w.Oaths) { if (o.Status == -1) broken++; if (o.Status is 1 or -1) sworn++; }
        if (broken >= 2 && broken * 2 > sworn) return CreatorStyle.Deceiver;
        if (total == 0 || total * 50 < w.Year) return CreatorStyle.Silent;
        if (wrath * 2 >= total) return CreatorStyle.Wrathful;
        if (kind * 10 >= total * 6) return CreatorStyle.Merciful;
        if (teach * 10 >= total * 3) return CreatorStyle.Teacher;
        if (earth * 2 >= total) return CreatorStyle.Architect;
        return CreatorStyle.Fickle;
    }

    private void UpdateStyle()
    {
        var w = World;
        var s = ComputeStyle();
        if (s == w.Style) return;
        var old = w.Style;
        w.Style = s;
        if (w.Year >= 100) w.Events.Publish(EventType.StyleNamed, -1, a: (int)s, b: (int)old, magnitude: 0.6f);
    }

    // ---------- Догадка о симуляции и контакт (kardashev.md §7) ----------

    private void Suspicion(Rng rng)
    {
        var w = World;
        bool silent = w.Year - w.LastCreatorAct > 50;
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.EraIdx < 6 || !g.Has(Tech.ScientificMethod)) continue;
            // Догадку питают свежие аномалии (за 300 лет), увиденные деяния и правки Земли; молчание неба её гасит.
            int recent = 0;
            foreach (var a in g.AnomalyLog) if (w.Year - a.year <= 300) recent++;
            float inc = 0.002f * Math.Min(16, recent) + (g.LastCreatorSeen >= 0 && w.Year - g.LastCreatorSeen <= 10 ? 0.01f : 0f)
                        + 0.003f * Math.Min(10, _terraformActs / 10f);
            inc *= (0.5f + g.N) * w.Params.SuspicionRate;
            if (silent) inc -= 0.01f;
            float before = g.Suspicion;
            g.Suspicion = Math.Clamp(g.Suspicion + inc, 0f, 1f);
            if (before < 0.6f && g.Suspicion >= 0.6f)
                w.Events.Publish(EventType.SimulationSuspected, g.Cell, a: g.Id, magnitude: g.Suspicion);
        }
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive || w.Year - pol.LastContact < 50) continue;
            var cap = w.Groups[pol.Capital];
            if (cap.Suspicion < 0.6f || cap.Freed) continue;
            pol.LastContact = w.Year;
            ContactAct(pol, cap, rng);
        }
    }

    private void ContactAct(Polity pol, Group cap, Rng rng)
    {
        var w = World;
        bool rockets = cap.Has(Tech.Rockets), electric = cap.Has(Tech.Electricity), science = cap.Has(Tech.ScientificMethod);
        switch (cap.Attitude)
        {
            case Attitude.Rebellion:
                if (rockets && rng.Chance(0.6)) RocketStrike(cap);
                else if (science && !pol.Shield) RaiseShield(pol, cap);
                else DemandFreedom(pol, cap, 0.8f);
                break;
            case Attitude.Denial or Attitude.Nihilism:
                if (cap.Suspicion >= 0.8f && !pol.FreedomAsked) DemandFreedom(pol, cap, 0.7f);
                else if (rockets) w.Events.Publish(EventType.ProbeLaunched, cap.Cell, a: cap.Id, b: 0, magnitude: 0.6f);
                else if (electric) w.Events.Publish(EventType.LightMessage, cap.Cell, a: cap.Id, magnitude: 0.5f);
                break;
            case Attitude.Fear:
                if (!pol.Shield && science) RaiseShield(pol, cap);
                else if (electric) w.Events.Publish(EventType.LightMessage, cap.Cell, a: cap.Id, magnitude: 0.5f);
                break;
            case Attitude.Love when cap.EraIdx >= 8 && rockets:
                w.CreatorEnergy += 50f;
                w.Events.Publish(EventType.ProbeLaunched, cap.Cell, a: cap.Id, b: 2, magnitude: 0.7f);
                break;
            default:
                if (rockets && rng.Chance(0.5)) w.Events.Publish(EventType.ProbeLaunched, cap.Cell, a: cap.Id, b: 0, magnitude: 0.6f);
                else if (cap.EraIdx >= 5 && rng.Chance(0.5)) w.Events.Publish(EventType.GeoglyphMade, cap.Cell, a: cap.Id, magnitude: 0.6f);
                else if (electric) w.Events.Publish(EventType.LightMessage, cap.Cell, a: cap.Id, magnitude: 0.5f);
                break;
        }
    }

    private void DemandFreedom(Polity pol, Group cap, float mag)
    {
        if (pol.FreedomAsked) return;
        pol.FreedomAsked = true;
        World.Events.Publish(EventType.FreedomDemanded, cap.Cell, a: cap.Id, b: pol.Id, magnitude: mag);
    }

    private void RocketStrike(Group cap)
    {
        var w = World;
        float lost = w.CreatorEnergy * 0.3f;
        w.CreatorEnergy -= lost;
        w.CreatorWoundedUntil = w.Year + 10;
        int ev = w.Events.Publish(EventType.ProbeLaunched, cap.Cell, a: cap.Id, b: 1, magnitude: 0.9f);
        w.Events.Publish(EventType.CreatorWounded, cap.Cell, a: cap.Id, magnitude: Math.Min(1f, 0.3f + lost / 300f), cause: ev);
    }

    private void RaiseShield(Polity pol, Group cap)
    {
        pol.Shield = true;
        World.Events.Publish(EventType.ShieldRaised, cap.Cell, a: pol.Id, magnitude: 0.7f);
    }

    /// <summary>Множитель силы деяния в клетке: щит державы гасит чудеса (×0,3).</summary>
    public float ShieldFactor(int cell)
    {
        int pol = cell >= 0 ? OwnerPolity(cell) : -1;
        return pol >= 0 && World.Polities[pol].Shield ? 0.3f : 1f;
    }
}
