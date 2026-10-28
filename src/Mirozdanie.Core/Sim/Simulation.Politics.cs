using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>Политика (скелет Э6): политии, лидеры, войны по Ланчестеру, нестабильность по Турчину. docs/design/politics.md.</summary>
public sealed partial class Simulation
{
    private bool _politicsSubscribed;

    private void EnsurePoliticsSubscriptions()
    {
        if (_politicsSubscribed) return;
        _politicsSubscribed = true;
        // Кара на династию: толкование молнии, убившей лидера, как кары/гнева (core-loop.md, цепочка 2).
        World.Events.Subscribe(EventType.Interpretation, OnInterpretationForPolitics);
    }

    private void OnInterpretationForPolitics(in WorldEvent e)
    {
        var w = World;
        if ((EventType)e.C != EventType.LightningStrike) return;
        var kind = (InterpretationKind)e.B;
        if (kind is not (InterpretationKind.Punishment or InterpretationKind.Wrath)) return;
        var g = w.Groups[e.A];
        if (g.PolityId < 0) return;
        var src = w.Events.Get(e.Cause);
        if (src.B < 0) return; // молния не убила лидера
        var pol = w.Polities[g.PolityId];
        pol.Curse = Math.Min(1f, pol.Curse + 0.25f * e.Magnitude + 0.1f);
    }

    private void PoliticsStage()
    {
        EnsurePoliticsSubscriptions();
        var w = World;
        var rng = w.Rng[StreamId.Politics];
        // Лидеры.
        int pc = w.Polities.Count;
        for (int i = 0; i < pc; i++)
        {
            var pol = w.Polities[i];
            if (!pol.Alive) continue;
            if (!w.Groups[pol.Capital].Alive || pol.Members.Count == 0) { CollapsePolity(pol, -1); continue; }
            if (pol.Leader < 0 || w.People[pol.Leader].Died >= 0) NewLeader(pol, -1);
            else if (w.Year - pol.LeaderSince >= pol.LeaderLifespan) KillLeader(pol, EventType.LeaderDied, -1, EventFlags.None);
        }
        // Рождение политий.
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.PolityId >= 0 || g.Subsistence != Subsistence.Farming) continue;
            if (g.Pop > 1500f * Demo.Ref && g.Culture[CultureTrait.Hierarchy] > 0.4f && g.Knowledge > 0.18f && rng.Chance(0.04)) NewPolity(g);
        }
        if (w.Year % 5 == 0) { Diplomacy(rng); Expansion(rng); }
        MarchArmies(rng);
        Instability(rng);
        if (w.Year % 10 == 0) GovernmentForms();
    }

    private Name PolityName(Lang L, int cell, Rng rng)
    {
        var p = World.Planet;
        (Concept c, string gloss) = p.ERiver[cell] > 0.15f ? (Concept.River, "Речная страна")
            : p.EElev[cell] > 1.6f ? (Concept.Mountain, "Горная страна")
            : p.ECoast[cell] ? (Concept.Sea, "Морская страна")
            : p.ETemp[cell] < 2f ? (Concept.Cold, "Холодная страна")
            : p.EPrecip[cell] < 400f ? (Concept.Hot, "Знойная страна")
            : rng.Chance(0.5) ? (Concept.Great, "Великая страна") : (Concept.Sun, "Солнечная страна");
        return L.MakeName(L.WithSuffix(L.Compound(c, Concept.Land), Concept.SuffixPlace), gloss);
    }

    /// <summary>
    /// Имя державы — «земля народа X» на языке основателей; при повторе — с различающим понятием.
    /// Если самоназвание народа уже кончается словом «земля», второй раз его не добавляем (civilizations.md, §5).
    /// </summary>
    private Name UniquePolityName(Lang L, Group founder)
    {
        var w = World;
        var stem = founder.Name.Word ?? L.Word(Concept.People);
        var land = L.Word(Concept.Land);
        var landPlace = L.WithSuffix(land, Concept.SuffixPlace);
        bool already = EndsWith(stem, landPlace) || EndsWith(stem, land) || EndsWith(stem, L.Word(Concept.SuffixPlace));
        return World.Unique(k =>
            {
                if (already)
                    return k == 0 ? L.MakeName(stem, $"держава народа {founder.Name.Native}")
                        : L.MakeName(L.WithSuffix(Concat(L, stem, L.Word(World.Distinguishers[(k - 1) % 16])), Concept.SuffixPlace),
                            $"держава народа {founder.Name.Native} ({World.DistinguisherGloss[(k - 1) % 16]})");
                return k == 0
                    ? L.MakeName(L.WithSuffix(Concat(L, stem, land), Concept.SuffixPlace), $"земля народа {founder.Name.Native}")
                    : L.MakeName(L.WithSuffix(Concat(L, stem, L.Compound(World.Distinguishers[(k - 1) % 16], Concept.Land)), Concept.SuffixPlace),
                        $"земля народа {founder.Name.Native} ({World.DistinguisherGloss[(k - 1) % 16]})");
            },
            n => { foreach (var p in w.Polities) if (p.Alive && p.Name.Native == n) return true; return false; });
    }

    /// <summary>Имя отколовшейся державы — своё, а не снова «земля народа X»: «Вольная земля», «Новая страна» и т. п.</summary>
    private Name RebelPolityName(Lang L, Group rebel, Rng rng)
    {
        var w = World;
        (Concept c, string gloss)[] roots =
        {
            (Concept.Free, "Вольная земля"), (Concept.New, "Новая земля"), (Concept.Red, "Красная земля"), (Concept.Strong, "Сильная земля"),
            (Concept.Clear, "Ясная земля"), (Concept.White, "Белая земля"), (Concept.Sun, "Солнечная земля"), (Concept.Stone, "Каменная земля"),
        };
        var (root, gl) = roots[rng.NextInt(roots.Length)];
        return World.Unique(k => k == 0
                ? L.MakeName(L.WithSuffix(L.Compound(root, Concept.Land), Concept.SuffixPlace), gl)
                : L.MakeName(L.WithSuffix(L.Compound(root, World.Distinguishers[(k - 1) % 16], Concept.Land), Concept.SuffixPlace), $"{gl} ({World.DistinguisherGloss[(k - 1) % 16]})"),
            n => { foreach (var p in w.Polities) if (p.Alive && p.Name.Native == n) return true; return false; });
    }

    private static bool EndsWith(int[] word, int[] tail)
    {
        if (tail.Length == 0 || word.Length < tail.Length) return false;
        for (int i = 0; i < tail.Length; i++) if (word[word.Length - tail.Length + i] != tail[i]) return false;
        return true;
    }

    private static int[] Concat(Lang L, int[] a, int[] b)
    {
        var r = new List<int>(a);
        if (r.Count > 0 && b.Length > 0 && !Phonemes.IsVowel(r[^1]) && !Phonemes.IsVowel(b[0])) r.Add(L.LinkVowel());
        r.AddRange(b);
        return r.ToArray();
    }

    private Polity NewPolity(Group capital)
    {
        var w = World;
        var L = w.LangOf(capital);
        var pol = new Polity { Id = w.Polities.Count, Capital = capital.Id, Founded = w.Year, Name = UniquePolityName(L, capital) };
        pol.Members.Add(capital.Id);
        capital.PolityId = pol.Id;
        w.Polities.Add(pol);
        w.Events.Publish(EventType.PolityFounded, capital.Cell, a: pol.Id, b: capital.Id);
        NewLeader(pol, -1);
        return pol;
    }

    private void NewLeader(Polity pol, int cause)
    {
        var w = World;
        var rng = w.Rng[StreamId.Politics];
        var cap = w.Groups[pol.Capital];
        var person = NewPerson(cap, PersonRole.Leader);
        person.Polity = pol.Id;
        bool newDynasty = pol.Leader < 0 || pol.Curse > 0.35f || rng.Chance(0.15);
        if (newDynasty)
        {
            var L = w.LangOf(cap);
            pol.Dynasty = L.MakeName(L.WithSuffix(L.Word(person.Meaning), Concept.SuffixPeople), $"род {person.Name.Native}");
            pol.Curse *= 0.3f;
        }
        pol.Leader = person.Id;
        pol.LeaderSince = w.Year;
        pol.LeaderLifespan = 15 + rng.NextInt(26);
        w.Events.Publish(EventType.LeaderRose, cap.Cell, a: pol.Id, b: person.Id, c: newDynasty ? 1 : 0, cause: cause);
    }

    public void KillLeader(Polity pol, EventType type, int cause, EventFlags flags)
    {
        var w = World;
        if (pol.Leader < 0) return;
        var person = w.People[pol.Leader];
        if (person.Died < 0) person.Died = w.Year;
        int ev = w.Events.Publish(type, w.Groups[pol.Capital].Cell, a: pol.Id, b: person.Id, magnitude: type == EventType.LeaderStruck ? 0.8f : 0.3f, flags: flags, cause: cause);
        if (type == EventType.LeaderStruck) pol.Elite = Math.Min(0.5f, pol.Elite + 0.05f);
        NewLeader(pol, ev);
    }

    private void CollapsePolity(Polity pol, int cause)
    {
        var w = World;
        pol.Alive = false;
        pol.Ended = w.Year;
        foreach (var m in pol.Members) if (w.Groups[m].PolityId == pol.Id) w.Groups[m].PolityId = -1;
        pol.Members.Clear();
        w.Events.Publish(EventType.Collapse, w.Groups[pol.Capital].Cell, a: pol.Id, cause: cause);
    }

    private float PolityPop(Polity pol)
    {
        float s = 0;
        foreach (var m in pol.Members) s += World.Groups[m].Pop;
        return s;
    }

    private void Expansion(Rng rng)
    {
        var w = World;
        int pc = w.Polities.Count;
        for (int i = 0; i < pc; i++)
        {
            var pol = w.Polities[i];
            if (!pol.Alive) continue;
            var cap = w.Groups[pol.Capital];
            var leader = pol.Leader >= 0 ? w.People[pol.Leader] : null;
            // Кандидаты: соседи членов политии вне её.
            Group? target = null, front = null; float bestScore = float.MinValue;
            foreach (var mId in pol.Members)
            {
                var m = w.Groups[mId];
                foreach (var nId in m.Neighbors)
                {
                    var o = w.Groups[nId];
                    if (!o.Alive || o.PolityId == pol.Id) continue;
                    float score = 1f / (1f + w.Planet.EcoDistance(m.Cell, o.Cell)) + MathF.Log10(1f + o.Capacity / Demo.Ref / 5000f) - MathF.Log10(1f + o.Pop / Demo.Ref / 8000f);
                    if (score > bestScore) { bestScore = score; target = o; front = m; }
                }
            }
            if (target == null) continue;
            float relDist = 0;
            if (cap.ReligionId != target.ReligionId)
                relDist = cap.ReligionId >= 0 && target.ReligionId >= 0
                    ? Doctrine.Distance(w.Religions[cap.ReligionId].Doctrine, w.Religions[target.ReligionId].Doctrine) + 0.2f : 0.25f;
            float intel = w.Intelligibility(cap.LanguageId, target.LanguageId);
            if (target.PolityId < 0 && relDist == 0 && intel > 0.4f && target.Pop < PolityPop(pol) * 0.9f && rng.Chance(0.3))
            {
                Annex(pol, target, -1, peaceful: true);
                continue;
            }
            // Напряжённость (politics.md).
            float stress = Math.Max(0f, 1f - cap.FoodRatio);
            float zeal = cap.ReligionId >= 0 ? w.Religions[cap.ReligionId].Zeal : 1f;
            bool trade = cap.TradePartners.Contains(target.Id);
            float landHunger = Math.Clamp(cap.Pop / Math.Max(1f, cap.Capacity) - 0.5f, 0f, 0.5f);
            float tension = 0.4f * stress + 0.5f * relDist * zeal + 0.45f * (leader?.Ambition ?? 0.5f) - (trade ? 0.3f : 0f) + 0.4f * landHunger
                            + 0.25f * cap.Culture[CultureTrait.Aggression] + 0.2f * cap.Belief[Doctrine.Violence] - 0.15f * w.Params.CooperationBias
                            + RelationTension(pol, target, out bool treaty, out bool ally);
            // Союзника не трогают; мирный договор нарушают только при очень сильной напряжённости (civilizations.md, §3).
            if (ally || treaty && tension < 0.8f) continue;
            // После войны держава 15 лет зализывает раны.
            if (w.Year - pol.LastWar < 15) continue;
            if (tension > 0.55f && rng.Chance(0.2))
            {
                // Взаимное сдерживание: ядерные державы воюют реже, но не никогда (kardashev.md, §6).
                var defCap = target.PolityId >= 0 ? w.Groups[w.Polities[target.PolityId].Capital] : target;
                if (cap.Has(Tech.NuclearWeapons) && defCap.Has(Tech.NuclearWeapons) && rng.Chance(0.85)) continue;
                Fight(pol, cap, front ?? cap, target, relDist > 0.4f && zeal >= 0.75f, rng);
            }
        }
    }

    private void Annex(Polity pol, Group target, int cause, bool peaceful)
    {
        var w = World;
        if (target.PolityId >= 0)
        {
            var old = w.Polities[target.PolityId];
            old.Members.Remove(target.Id);
            if (old.Capital == target.Id) CollapsePolity(old, cause);
        }
        target.PolityId = pol.Id;
        pol.Members.Add(target.Id);
        w.Events.Publish(EventType.Annexation, target.Cell, a: pol.Id, b: target.Id, c: peaceful ? 1 : 0, cause: cause);
    }

    /// <summary>Сражение по квадратичному закону Ланчестера (Lanchester 1916): сила F = N²·q.</summary>
    public static (bool attackerWins, float attackerLoss, float defenderLoss) Lanchester(float nA, float qA, float nD, float qD)
    {
        float fA = nA * nA * qA, fD = nD * nD * qD;
        if (fA >= fD)
        {
            float loss = nA * (1f - MathF.Sqrt(Math.Max(0f, 1f - fD / fA)));
            return (true, loss, nD);
        }
        else
        {
            float loss = nD * (1f - MathF.Sqrt(Math.Max(0f, 1f - fA / fD)));
            return (false, nA, loss);
        }
    }

    /// <summary>
    /// Объявление войны (Fearon 1995 — модель торга): ядерный обмен решается сразу, иначе войско выходит
    /// из пограничного народа <paramref name="front"/> и идёт к цели; бой — по прибытии (civilizations.md, §10).
    /// </summary>
    private void Fight(Polity pol, Group cap, Group front, Group target, bool holy, Rng rng)
    {
        var w = World;
        int defPolity = target.PolityId;
        int decl = w.Events.Publish(holy ? EventType.HolyWar : EventType.WarDeclared, target.Cell, a: cap.Id, b: target.Id, c: defPolity);
        var defCapital = defPolity >= 0 ? w.Groups[w.Polities[defPolity].Capital] : target;
        pol.LastWar = w.Year;
        if (defPolity >= 0)
        {
            var r = Rel(pol.Id, defPolity);
            r.Wars++;
            r.Opinion = Math.Max(-1f, r.Opinion - 0.5f);
            r.AllianceSince = -1;
            r.Grievances.Add(new Grievance(w.Year, 0, defPolity, target.Id, decl));
            if (r.PeaceUntil >= w.Year)
            {
                // Предательство рушит доверие вдвое быстрее, чем его копит соблюдённый мир (Axelrod 1984).
                r.Trust = Math.Max(0f, r.Trust - 0.4f);
                r.PeaceUntil = -1;
                r.Grievances.Add(new Grievance(w.Year, 2, defPolity, target.Id, decl));
                w.Events.Publish(EventType.TreatyBroken, target.Cell, a: pol.Id, b: defPolity, magnitude: 0.5f, cause: decl);
            }
        }
        // Ядерный обмен: две ядерные державы в войне — малый, но не нулевой шанс (Robock et al. 2007 — последствия).
        if (cap.Has(Tech.NuclearWeapons) && defCapital.Has(Tech.NuclearWeapons) && defPolity >= 0
            && rng.Chance(0.1 * w.Params.NuclearRisk * (holy ? 1.5 : 1.0) * (w.Year - w.LastNuclearWar < 200 ? 0.15 : 1.0)))
        {
            NuclearExchange(pol, w.Polities[defPolity], decl, rng);
            return;
        }
        // Мобилизация и качество войска растут с эпохой (kardashev.md, §3, ветвь «Война»).
        float nA = Mobilization(cap) * PolityPop(pol);
        var army = MakeArmy(pol.Id, cap, target, front.Cell, nA, defender: false, holy, decl);
        float nD = defPolity >= 0 ? Mobilization(defCapital) * 1.2f * PolityPop(w.Polities[defPolity]) : 2f * Mobilization(target) * target.Pop;
        MakeArmy(defPolity, defCapital, target, defCapital.Cell, nD, defender: true, holy, decl);
        w.Events.Publish(EventType.ArmyMarched, front.Cell, a: army.Id, b: pol.Id, c: target.Id, magnitude: 0.1f, cause: decl);
    }

    /// <summary>Бой у цели по квадратичному закону Ланчестера; soldiers — сколько дошло.</summary>
    private bool ResolveBattle(Polity pol, Group cap, Group target, float soldiers, bool holy, int decl, Rng rng)
    {
        var w = World;
        int defPolity = target.PolityId;
        var defCapital = defPolity >= 0 ? w.Groups[w.Polities[defPolity].Capital] : target;
        float nA = soldiers;
        float nD = defPolity >= 0 ? Mobilization(defCapital) * 1.2f * PolityPop(w.Polities[defPolity]) : 2f * Mobilization(target) * target.Pop;
        // Союзники защитника приходят на помощь (половина их ополчения).
        if (defPolity >= 0)
            foreach (var r in w.Relations)
            {
                if (r.AllianceSince < 0 || (r.A != defPolity && r.B != defPolity)) continue;
                int ally = r.A == defPolity ? r.B : r.A;
                if (ally == pol.Id || !w.Polities[ally].Alive) continue;
                nD += 0.5f * Mobilization(w.Groups[w.Polities[ally].Capital]) * PolityPop(w.Polities[ally]);
            }
        float qA = (0.5f + cap.Knowledge) * (1f + 0.3f * cap.Culture[CultureTrait.Aggression]) * (holy ? 1.2f : 1f) * ArmsQuality(cap) * (0.8f + 0.4f * rng.NextFloat());
        float qD = (0.5f + target.Knowledge) * (1f + 0.3f * target.Culture[CultureTrait.Aggression]) * (w.Planet.EElev[target.Cell] > 1.5f ? 1.5f : 1f)
                   * ArmsQuality(defCapital) * (target.B > 0.2f ? 1.1f : 1f) * (0.8f + 0.4f * rng.NextFloat());
        var (win, lossA, lossD) = Lanchester(nA, qA, nD, qD);
        cap.Pop = Math.Max(20f * Demo.Ref, cap.Pop - lossA * 0.6f);
        target.Pop = Math.Max(20f * Demo.Ref, target.Pop - lossD * 0.6f);
        cap.Guilt = Math.Min(1f, cap.Guilt + 0.3f);
        cap.LastCrisis = target.LastCrisis = w.Year;
        pol.Treasury = Math.Max(0f, pol.Treasury - 0.15f);
        var winner = win ? cap : target;
        var loser = win ? target : cap;
        loser.LastDefeat = w.Year;
        int battle = w.Events.Publish(EventType.Battle, target.Cell, a: winner.Id, b: loser.Id, c: pol.Id, flags: holy ? EventFlags.Crisis : EventFlags.None, magnitude: Math.Min(1f, (lossA + lossD) / (2000f * Demo.Ref) + 0.3f), cause: decl);
        w.Wars.Add(new War { Attacker = cap.Id, Defender = target.Id, AttackerPolity = pol.Id, DefenderPolity = defPolity, Holy = holy, Year = w.Year, Winner = winner.Id });
        if (defPolity >= 0 && w.Polities[defPolity].Alive)
        {
            // Мир с условиями: 20–50 лет; побеждённый помнит отнятое (реваншизм).
            var r = Rel(pol.Id, defPolity);
            int years = 20 + rng.NextInt(31);
            r.PeaceUntil = w.Year + years;
            if (win) r.Grievances.Add(new Grievance(w.Year, 1, defPolity, target.Id, battle));
            w.Events.Publish(EventType.PeaceMade, target.Cell, a: pol.Id, b: defPolity, c: years, magnitude: 0.2f, cause: battle);
        }
        if (win)
        {
            Annex(pol, target, battle, peaceful: false);
            // Завоевание несёт веру победителя (засев культа) и слова (заимствования).
            if (cap.ReligionId >= 0 && target.ReligionId != cap.ReligionId && target.CultId < 0)
            {
                target.CultId = cap.ReligionId;
                target.CultShare = 0.15f;
            }
            if (cap.LanguageId != target.LanguageId) Borrow(w.Languages[target.LanguageId], w.Languages[cap.LanguageId], Concept.Chief, rng);
        }
        return win;
    }

    /// <summary>Доля народа под оружием: ополчение 2 %, железный век 2,5 %, массовые армии 6 % (порядки по Turchin 2006, «War and Peace and War»).</summary>
    private static float Mobilization(Group g) =>
        g.Has(Tech.MassArmies) ? 0.06f : g.Has(Tech.Iron) ? 0.025f : 0.02f;

    /// <summary>Качество войска по открытиям: бронза, железо, сталь, порох, моторы, авиация, вычисления.</summary>
    private static float ArmsQuality(Group g)
    {
        float q = 1f;
        if (g.Has(Tech.Bronze)) q *= 1.15f;
        if (g.Has(Tech.Iron)) q *= 1.25f;
        if (g.Has(Tech.Steel)) q *= 1.15f;
        if (g.Has(Tech.Gunpowder)) q *= 1.5f;
        if (g.Has(Tech.Combustion)) q *= 1.3f;
        if (g.Has(Tech.Aviation)) q *= 1.4f;
        if (g.Has(Tech.Computing)) q *= 1.2f;
        return q;
    }

    /// <summary>
    /// Ядерная война: обе державы теряют 20–35 % людей, по всей планете — ядерная зима:
    /// −6 °C, затухает за годы; неурожаи шесть лет (Robock, Oman, Stenchikov 2007).
    /// </summary>
    private void NuclearExchange(Polity a, Polity b, int cause, Rng rng)
    {
        var w = World;
        float frac = rng.Range(0.2f, 0.35f);
        foreach (var pol in new[] { a, b })
            foreach (var m in pol.Members)
            {
                var g = w.Groups[m];
                if (!g.Alive) continue;
                g.Pop *= 1f - frac;
                g.YearLoss += frac;
                g.LastCrisis = w.Year;
                g.Guilt = Math.Min(1f, g.Guilt + 0.4f);
            }
        a.LastWar = b.LastWar = w.Year;
        for (int c = 0; c < w.TempOffset.Length; c++) w.TempOffset[c] -= 6f;
        w.NuclearWinterUntil = w.Year + 6;
        w.LastNuclearWar = w.Year; // память о пепле удерживает мир от новой войны веками
        w.Effects.Add(new ActiveEffect { Type = Creator.InterventionType.Winter, Cell = w.Groups[a.Capital].Cell, Radius = 1000f, EndYear = w.Year + 6, Factor = 0.6f });
        w.Events.Publish(EventType.NuclearWar, w.Groups[b.Capital].Cell, a: a.Id, b: b.Id, magnitude: frac, flags: EventFlags.Crisis, cause: cause);
    }

    private void Instability(Rng rng)
    {
        var w = World;
        int pc = w.Polities.Count;
        for (int i = 0; i < pc; i++)
        {
            var pol = w.Polities[i];
            if (!pol.Alive || pol.Members.Count < 3) continue;
            var cap = w.Groups[pol.Capital];
            // Структурно-демографическая теория (Turchin 2003; Goldstone 1991): PSI = MMP·EMP·SFD.
            float mmp = 0, popSum = 0, sameFaith = 0;
            foreach (var m in pol.Members)
            {
                var g = w.Groups[m];
                mmp += g.Pop / Math.Max(1f, g.Capacity * g.Yield) * g.Pop;
                popSum += g.Pop;
                if (g.ReligionId == cap.ReligionId) sameFaith += g.Pop;
            }
            mmp /= Math.Max(1f, popSum);
            if (mmp < 0.9f) pol.Elite = Math.Min(0.4f, pol.Elite + 0.004f); else pol.Elite = Math.Max(0.02f, pol.Elite - 0.002f);
            pol.Treasury = Math.Clamp(pol.Treasury + 0.03f * (1f - mmp) - 0.02f * pol.Elite * 5f + 0.01f, 0f, 1f);
            // Перепроизводство элит в долях от «нормы» 20 % (было 10 %: модель насыщалась и мятежи шли непрерывно).
            float emp = pol.Elite / 0.2f;
            float sfd = 1f - pol.Treasury;
            pol.Psi = mmp * emp * (0.5f + sfd);
            pol.Curse *= 0.97f;
            pol.Legitimacy = Math.Clamp(0.4f + 0.3f * sameFaith / Math.Max(1f, popSum) + 0.2f * Math.Clamp(cap.B, -1, 1) - pol.Curse, 0f, 1f);
            // Порог выше прежнего и «иммунитет» после основания и мятежа: держава не рассыпается каждые 10–30 лет (civilizations.md, §5).
            float pRevolt = 0.012f / (1f + MathF.Exp(-6f * (pol.Psi * (1f - pol.Legitimacy) * 0.5f - 0.65f)));
            if (!rng.Chance(pRevolt)) continue;
            if (w.Year - pol.Founded < 40 || w.Year - pol.LastRevolt < 40) continue;
            Revolt(pol, rng);
        }
    }

    private void Revolt(Polity pol, Rng rng)
    {
        var w = World;
        var cap = w.Groups[pol.Capital];
        Group? rebel = null; float best = float.MinValue;
        foreach (var m in pol.Members)
        {
            if (m == pol.Capital) continue;
            var g = w.Groups[m];
            float score = w.Planet.EcoDistance(g.Cell, cap.Cell) * 0.1f + (g.ReligionId != cap.ReligionId ? 1f : 0f)
                          + (g.Attitude == Attitude.Rebellion ? 1f : 0f) + Demo.Weight(g.Pop) + rng.NextFloat() * 0.3f;
            if (score > best) { best = score; rebel = g; }
        }
        if (rebel == null) return;
        int ev = w.Events.Publish(EventType.Revolt, rebel.Cell, a: pol.Id, b: rebel.Id, magnitude: Math.Min(1f, pol.Psi / 2f));
        pol.LastRevolt = w.Year;
        pol.Elite *= 0.6f;
        rebel.Guilt = Math.Min(1f, rebel.Guilt + 0.2f);
        rebel.LastCrisis = cap.LastCrisis = w.Year;
        var rebels = new List<int>();
        foreach (var m in pol.Members)
        {
            if (m == pol.Capital) continue;
            if (w.Planet.EcoDistance(w.Groups[m].Cell, rebel.Cell) <= 4f) rebels.Add(m);
        }
        foreach (var m in rebels) pol.Members.Remove(m);
        var np = new Polity { Id = w.Polities.Count, Capital = rebel.Id, Founded = w.Year, Parent = pol.Id,
            Name = RebelPolityName(w.LangOf(rebel), rebel, rng) };
        foreach (var m in rebels) { np.Members.Add(m); w.Groups[m].PolityId = np.Id; }
        w.Polities.Add(np);
        w.Events.Publish(EventType.PolitySplit, rebel.Cell, a: np.Id, b: pol.Id, c: rebel.Id, cause: ev);
        NewLeader(np, ev);
    }
}
