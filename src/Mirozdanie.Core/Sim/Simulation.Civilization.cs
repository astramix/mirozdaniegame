using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Random;

namespace Mirozdanie.Core.Sim;

/// <summary>
/// Цивилизации на карте (docs/design/civilizations.md, §1, §3, §10): города, формы власти, слияние народов,
/// войска на марше, отношения и договоры держав.
/// </summary>
public sealed partial class Simulation
{
    private int[] _cityAt = Array.Empty<int>();
    private long _territorySig;

    // ---------- Территории: пересчёт, когда народы сдвинулись (§10, «границы») ----------

    /// <summary>Подпись расселения: меняется, когда народ родился, погиб или переселился.</summary>
    private long TerritorySignature()
    {
        long h = 17;
        foreach (var g in World.Groups) h = h * 31 + (g.Alive ? g.Cell + 1 : 0);
        return h;
    }

    private void TerritoriesIfChanged()
    {
        long sig = TerritorySignature();
        if (sig == _territorySig) return;
        UpdateTerritories();
        _territorySig = sig;
    }

    // ---------- Летопись: сводка века ----------

    private void RecordAnnals()
    {
        var w = World;
        double pop = 0; int peoples = 0, polities = 0, religions = 0, cities = 0;
        foreach (var g in w.Groups) if (g.Alive) { pop += g.Pop; peoples++; }
        foreach (var p in w.Polities) if (p.Alive) polities++;
        Span<bool> rel = w.Religions.Count <= 4096 ? stackalloc bool[w.Religions.Count] : new bool[w.Religions.Count];
        foreach (var g in w.Groups) if (g.Alive && g.ReligionId >= 0 && !rel[g.ReligionId]) { rel[g.ReligionId] = true; religions++; }
        int langs = 0;
        foreach (var l in w.Languages) if (l.Alive) langs++;
        foreach (var c in w.Cities) if (c.Alive && c.Rank >= 1) cities++;
        w.Annals.Add(new CenturyStat(w.Year, pop, peoples, polities, religions, langs, w.Kardashev, w.WorldEra, w.Warming, cities));
    }

    // ---------- Города (§10; Zipf 1949; Bairoch 1988) ----------

    /// <summary>
    /// Доля горожан по эпохе: 3 % у первых земледельцев, ~10 % у классических империй, ~25 % в начале
    /// индустрии и 70–75 % у планетарной цивилизации (Bairoch 1988; ООН, World Urbanization Prospects).
    /// </summary>
    public static float UrbanShare(int era) => era switch
    {
        <= 1 => 0.03f, 2 => 0.05f, 3 => 0.08f, 4 => 0.12f, 5 => 0.25f, 6 => 0.45f, 7 => 0.6f, 8 => 0.7f, _ => 0.75f,
    };

    private const float VillageMin = 2000f;

    private void Settlements()
    {
        var w = World;
        var p = w.Planet;
        int en = p.EW * p.EH;
        if (_cityAt.Length != en)
        {
            _cityAt = new int[en];
            Array.Fill(_cityAt, -1);
            foreach (var c in w.Cities) if (c.Alive) _cityAt[c.Cell] = c.Id;
        }
        // 1. Город переходит к тому, чья теперь клетка; без хозяина-земледельца — пустеет.
        foreach (var city in w.Cities)
        {
            if (!city.Alive) continue;
            int owner = w.CellOwner.Length > 0 ? w.CellOwner[city.Cell] : -1;
            if (owner < 0 || !w.Groups[owner].Alive || w.Groups[owner].Subsistence != Subsistence.Farming)
            {
                city.Alive = false;
                _cityAt[city.Cell] = -1;
                continue;
            }
            city.Group = owner;
            city.Capital = false;
        }
        // 2. Размеры по Ципфу: k-й город в 1/k от крупнейшего (Zipf 1949).
        foreach (var g in w.Groups)
        {
            if (!g.Alive || g.Subsistence != Subsistence.Farming) continue;
            float urban = g.Pop * UrbanShare(g.EraIdx);
            // Число поселений n: самое большое, при котором n-й ещё не меньше деревни.
            int n = 1;
            float h = 1f;
            int maxN = Math.Min(8, Math.Max(1, g.Territory.Count / 3));
            while (n < maxN)
            {
                float h2 = h + 1f / (n + 1);
                if (urban / h2 / (n + 1) < VillageMin) break;
                h = h2;
                n++;
            }
            float s1 = urban / h;
            int rank = 0;
            // Первый — дом народа.
            var home = CityAt(g, g.Cell);
            SetCitySize(home, s1);
            rank = 1;
            foreach (var c in g.Territory)
            {
                if (c == g.Cell) continue;
                int id = _cityAt[c];
                if (id < 0) continue;
                var city = w.Cities[id];
                if (rank < n) { rank++; SetCitySize(city, s1 / rank); }
                else if (city.Pop > VillageMin * 0.5f) SetCitySize(city, city.Pop * 0.8f);
                else { city.Alive = false; _cityAt[c] = -1; }
            }
            // Новые поселения — на лучших свободных клетках, не ближе двух клеток к своим.
            while (rank < n)
            {
                int best = -1; float bestScore = 0;
                foreach (var c in g.Territory)
                {
                    if (_cityAt[c] >= 0 || !p.EIsLand(c)) continue;
                    bool near = false;
                    foreach (var o in g.Territory)
                        if (_cityAt[o] >= 0 && p.EcoDistance(o, c) < 2f) { near = true; break; }
                    if (near) continue;
                    float score = p.EFertility[c] * (1f + 2f * p.ERiver[c]) + (p.ECoast[c] ? 0.3f : 0f);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                if (best < 0) break;
                rank++;
                SetCitySize(CityAt(g, best), s1 / rank);
            }
        }
        // 3. Столицы и святые города.
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive) continue;
            int id = _cityAt[w.Groups[pol.Capital].Cell];
            if (id >= 0) w.Cities[id].Capital = true;
        }
        foreach (var site in w.Sites)
        {
            int id = site.Cell >= 0 && site.Cell < en ? _cityAt[site.Cell] : -1;
            if (id >= 0) w.Cities[id].Holy = true;
        }
    }

    private void SetCitySize(City city, float pop)
    {
        int before = city.Rank;
        city.Pop = pop;
        if (city.Rank > before && city.Rank >= 2)
        {
            int nth = city.Rank == 3 ? ++World.MegaCities : 0;
            World.Events.Publish(EventType.CityGrew, city.Cell, a: city.Id, b: city.Rank, c: nth, magnitude: city.Rank == 3 ? 0.6f : 0.3f);
        }
    }

    /// <summary>Город народа в клетке; если его нет — основать (имя на языке народа, по местности).</summary>
    private City CityAt(Group g, int cell)
    {
        var w = World;
        int id = _cityAt[cell];
        if (id >= 0) return w.Cities[id];
        var city = new City { Id = w.Cities.Count, Group = g.Id, Cell = cell, Founded = w.Year, Pop = VillageMin, Name = CityName(g, cell) };
        w.Cities.Add(city);
        _cityAt[cell] = city.Id;
        if (g.Pop * UrbanShare(g.EraIdx) > 1e4f && cell == g.Cell)
            w.Events.Publish(EventType.CityFounded, cell, a: city.Id, b: g.Id, magnitude: 0.2f);
        return city;
    }

    private static readonly (Concept c, string gloss)[] CityRoots =
    {
        (Concept.New, "Новый город"), (Concept.Old, "Старый город"), (Concept.White, "Белый город"), (Concept.Red, "Красный город"),
        (Concept.Sun, "Солнечный город"), (Concept.Stone, "Каменный город"), (Concept.Tree, "Лесной город"), (Concept.Grain, "Хлебный город"),
        (Concept.Star, "Звёздный город"), (Concept.Great, "Великий город"), (Concept.Light, "Светлый город"), (Concept.Wind, "Ветреный город"),
        (Concept.Bird, "Птичий город"), (Concept.Heart, "Город-сердце"), (Concept.Moon, "Лунный город"), (Concept.Gift, "Город дара"),
    };

    /// <summary>Имя города — по местности (река, море, гора, святыня) или по корню из хеша клетки (без расхода RNG).</summary>
    private Name CityName(Group g, int cell)
    {
        var w = World;
        var p = w.Planet;
        var L = w.LangOf(g);
        (Concept c, string gloss) root = p.ERiver[cell] > 0.15f ? (Concept.River, "Речной город")
            : p.ECoast[cell] ? (Concept.Sea, "Морской город")
            : p.EElev[cell] > 1.6f ? (Concept.Mountain, "Горный город")
            : CityRoots[(int)((uint)(cell * 2654435761u) >> 28)];
        foreach (var s in w.Sites) if (s.Cell == cell) { root = (Concept.Holy, "Святой город"); break; }
        return World.Unique(k => k == 0
                ? L.MakeName(L.Compound(root.c, Concept.City), root.gloss)
                : L.MakeName(L.Compound(root.c, World.Distinguishers[(k - 1) % 16], Concept.City), $"{root.gloss} ({World.DistinguisherGloss[(k - 1) % 16]})"),
            n => { foreach (var c in w.Cities) if (c.Alive && c.Name.Native == n) return true; return false; });
    }

    // ---------- Формы власти (§1; Bueno de Mesquita et al. 2003) ----------

    public GovForm DeriveForm(Polity pol)
    {
        var w = World;
        var cap = w.Groups[pol.Capital];
        if (pol.Form == GovForm.Federation) return GovForm.Federation;
        int members = pol.Members.Count;
        int langs = 0;
        float total = 0, sameFaith = 0;
        Span<int> seen = stackalloc int[8];
        foreach (var m in pol.Members)
        {
            var g = w.Groups[m];
            total += g.Pop;
            if (g.ReligionId == cap.ReligionId && cap.ReligionId >= 0) sameFaith += g.Pop;
            bool known = false;
            for (int i = 0; i < Math.Min(langs, 8); i++) if (seen[i] == g.LanguageId) known = true;
            if (!known) { if (langs < 8) seen[langs] = g.LanguageId; langs++; }
        }
        // Иерархия в державах почти всегда у потолка (её растит сама держава), поэтому формы различаются
        // по открытости, общинности, любви к знанию и обрядности (civilizations.md, §1; см. NOTES).
        float open = cap.Culture[CultureTrait.Openness], coll = cap.Culture[CultureTrait.Collectivism];
        float ritual = cap.ReligionId >= 0 ? w.Religions[cap.ReligionId].Doctrine[Doctrine.Ritual] : 0f;
        bool trade = cap.TradePartners.Count >= 3 || cap.TradeBonus > 0.12f;
        // Поздние формы — из идеологий эпохи.
        if (cap.Has(Tech.Ideologies))
        {
            if (coll > 0.57f && open < 0.45f) return GovForm.Ideocracy;
            if (cap.Has(Tech.Computing) && cap.Has(Tech.Networks) && cap.Culture[CultureTrait.KnowledgeLove] > 0.57f) return GovForm.Technocracy;
            return GovForm.Democracy;
        }
        if (total > 0 && sameFaith / total >= 0.7f && ritual > 0.6f && cap.Culture[CultureTrait.Piety] > 0.55f) return GovForm.Theocracy;
        if (members >= 8 || langs >= 3) return GovForm.Empire;
        if (trade && open > 0.5f && coll < 0.55f && cap.Writing) return GovForm.Republic;
        if (members > 2 || w.Year - pol.Founded > 100 || pol.Form != GovForm.Chiefdom) return GovForm.Kingdom;
        return GovForm.Chiefdom;
    }

    public static string FormTitle(GovForm f) => f switch
    {
        GovForm.Chiefdom => "вождество", GovForm.Kingdom => "царство", GovForm.Theocracy => "теократия", GovForm.Empire => "империя",
        GovForm.Republic => "республика", GovForm.Federation => "федерация", GovForm.Ideocracy => "идеократия",
        GovForm.Democracy => "народовластие", _ => "технократия",
    };

    /// <summary>Малая коалиция (вождество, царство, империя, идеократия) легче воюет (Bueno de Mesquita et al. 2003).</summary>
    private static bool SmallCoalition(GovForm f) => f is GovForm.Chiefdom or GovForm.Kingdom or GovForm.Empire or GovForm.Ideocracy or GovForm.Theocracy;

    private void GovernmentForms()
    {
        var w = World;
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive) continue;
            var f = DeriveForm(pol);
            // Форма меняется, только если новая держится два обзора подряд (20 лет) — без «мигания».
            if (f == pol.Form) { pol.PendingForm = -1; continue; }
            if (pol.PendingForm != (int)f) { pol.PendingForm = (int)f; continue; }
            pol.PendingForm = -1;
            var old = pol.Form;
            pol.Form = f;
            // Основание державы — уже событие; первая форма без отдельной записи.
            if (w.Year - pol.Founded > 10)
                w.Events.Publish(EventType.GovernmentChanged, w.Groups[pol.Capital].Cell, a: pol.Id, b: (int)f, c: (int)old, magnitude: 0.35f);
        }
    }

    // ---------- Слияние народов (§10; Abrams & Strogatz 2003) ----------

    /// <summary>
    /// Малый народ (< 20 % соседа) той же державы, веры и почти того же языка (понятность > 0,8) растворяется в соседе.
    /// Это сокращает дробление на сотни мелких народов.
    /// </summary>
    private void Assimilation()
    {
        var w = World;
        int count = w.Groups.Count;
        for (int i = 0; i < count; i++)
        {
            var g = w.Groups[i];
            if (!g.Alive || g.PolityId < 0 || w.Polities[g.PolityId].Capital == g.Id) continue;
            Group? into = null;
            foreach (var nId in g.Neighbors)
            {
                var o = w.Groups[nId];
                if (!o.Alive || o.PolityId != g.PolityId || o.ReligionId != g.ReligionId || o.Pop < 5f * g.Pop) continue;
                if (w.Planet.EcoDistance(g.Cell, o.Cell) > 4.5f || w.Intelligibility(g.LanguageId, o.LanguageId) <= 0.8f) continue;
                if (into == null || o.Pop > into.Pop) into = o;
            }
            if (into == null) continue;
            into.Pop += g.Pop;
            into.Knowledge = Math.Max(into.Knowledge, g.Knowledge);
            // Знания малого народа не пропадают (кроме земледелия там, где оно невозможно).
            into.Techs |= into.Has(Tech.Agriculture) ? g.Techs : g.Techs & ~(1ul << (int)Tech.Agriculture);
            int cell = g.Cell;
            KillGroup(g, announce: false);
            w.Events.Publish(EventType.Assimilated, cell, a: g.Id, b: into.Id, magnitude: 0.15f);
        }
    }

    // ---------- Отношения держав (§3; Axelrod 1984) ----------

    public Relation Rel(int a, int b)
    {
        var w = World;
        if (a > b) (a, b) = (b, a);
        long key = ((long)a << 32) | (uint)b;
        if (w.RelationIndex.TryGetValue(key, out int id)) return w.Relations[id];
        var r = new Relation { A = a, B = b };
        w.RelationIndex[key] = w.Relations.Count;
        w.Relations.Add(r);
        return r;
    }

    public Relation? FindRel(int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        return World.RelationIndex.TryGetValue(((long)a << 32) | (uint)b, out int id) ? World.Relations[id] : null;
    }

    /// <summary>Раз в 5 лет: мнение затухает (3 %/год), доверие растёт от соблюдённого мира, торговля сближает; союзы и федерации.</summary>
    private void Diplomacy(Rng rng)
    {
        var w = World;
        // Знакомство: у соседних держав появляются отношения.
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive) continue;
            foreach (var m in pol.Members)
                foreach (var nId in w.Groups[m].Neighbors)
                {
                    int other = w.Groups[nId].PolityId;
                    if (other >= 0 && other != pol.Id && w.Polities[other].Alive && FindRel(pol.Id, other) == null) Rel(pol.Id, other);
                }
        }
        foreach (var r in w.Relations)
        {
            var a = w.Polities[r.A];
            var b = w.Polities[r.B];
            if (!a.Alive || !b.Alive) continue;
            r.Opinion *= 0.86f; // 0,97⁵
            // Доверие копится в мире: быстрее под договором (Axelrod 1984).
            if (r.Grievances.Count == 0) r.Trust = Math.Min(1f, r.Trust + (r.PeaceUntil >= w.Year ? 0.03f : 0.01f));
            // Торговля, общая вера и понятная речь сближают (Russett & Oneal 2001).
            var ca = w.Groups[a.Capital];
            var cb = w.Groups[b.Capital];
            float warm = 0f;
            if (ca.TradePartners.Contains(b.Capital)) warm += 0.06f;
            if (ca.ReligionId >= 0 && ca.ReligionId == cb.ReligionId) warm += 0.06f;
            if (w.Intelligibility(ca.LanguageId, cb.LanguageId) > 0.4f) warm += 0.03f;
            r.Opinion = Math.Min(1f, r.Opinion + warm);
            // Старые обиды забываются за 200 лет.
            for (int k = r.Grievances.Count - 1; k >= 0; k--) if (w.Year - r.Grievances[k].Year > 200) r.Grievances.RemoveAt(k);
            if (r.AllianceSince < 0 && r.Opinion > 0.45f && r.Trust > 0.55f && r.Grievances.Count == 0 && rng.Chance(0.2 * w.Params.CooperationBias + 0.05))
            {
                r.AllianceSince = w.Year;
                w.Events.Publish(EventType.AllianceFormed, ca.Cell, a: a.Id, b: b.Id, magnitude: 0.4f);
            }
            else if (r.AllianceSince >= 0 && (r.Opinion < 0f || r.Grievances.Count > 0)) r.AllianceSince = -1;
            // Федерация: вековой союз двух держав с широкой коалицией (§4, путь «федерация»).
            if (r.AllianceSince >= 0 && w.Year - r.AllianceSince >= 100 && !SmallCoalition(a.Form) && !SmallCoalition(b.Form) && rng.Chance(0.1))
                Federate(PolityPop(a) >= PolityPop(b) ? a : b, PolityPop(a) >= PolityPop(b) ? b : a);
        }
    }

    private void Federate(Polity big, Polity small)
    {
        var w = World;
        var moving = small.Members.ToArray();
        foreach (var m in moving)
        {
            w.Groups[m].PolityId = big.Id;
            big.Members.Add(m);
        }
        small.Members.Clear();
        small.Alive = false;
        small.Ended = w.Year;
        big.Form = GovForm.Federation;
        w.Events.Publish(EventType.AllianceFormed, w.Groups[big.Capital].Cell, a: big.Id, b: small.Id, c: 1, magnitude: 0.8f);
    }

    /// <summary>Поправка к напряжённости из отношений: мнение, обиды (реваншизм), мирный договор, союз, «демократический мир».</summary>
    private float RelationTension(Polity pol, Group target, out bool treaty, out bool ally)
    {
        treaty = ally = false;
        if (target.PolityId < 0) return 0f;
        var w = World;
        var r = FindRel(pol.Id, target.PolityId);
        var other = w.Polities[target.PolityId];
        float t = 0f;
        if (!SmallCoalition(pol.Form) && !SmallCoalition(other.Form)) t -= 0.3f; // Russett & Oneal 2001
        else if (SmallCoalition(pol.Form)) t += 0.05f;
        if (r == null) return t;
        t -= 0.25f * r.Opinion;
        int mine = 0;
        foreach (var gr in r.Grievances) if (gr.Holder == pol.Id) mine++;
        t += 0.12f * Math.Min(3, mine);
        treaty = r.PeaceUntil >= w.Year;
        ally = r.AllianceSince >= 0;
        return t;
    }

    // ---------- Войска (§10) ----------

    private float ArmySpeed(Group g) =>
        g.Has(Tech.Combustion) ? 6f : g.Has(Tech.Railroad) ? 3.5f : g.Has(Tech.Roads) ? 2f : 1.3f;

    private Army MakeArmy(int polity, Group from, Group target, int fromCell, float soldiers, bool defender, bool holy, int decl)
    {
        var w = World;
        var p = w.Planet;
        float x = fromCell % p.EW, y = fromCell / p.EW;
        float tx = target.Cell % p.EW, ty = target.Cell / p.EW;
        // Кратчайший путь по долготе (с заворотом).
        if (tx - x > p.EW / 2f) tx -= p.EW;
        else if (x - tx > p.EW / 2f) tx += p.EW;
        var army = new Army
        {
            Id = w.Armies.Count == 0 ? 0 : w.Armies[^1].Id + 1, Polity = polity, Group = from.Id, Target = target.Id,
            X = x, Y = y, TX = tx, TY = ty, Soldiers = soldiers, Defender = defender, Holy = holy, Declaration = decl, Started = w.Year,
        };
        w.Armies.Add(army);
        return army;
    }

    /// <summary>Шаг войск: марш к цели (снабжение тает: −2 % в год), бой по прибытии.</summary>
    private void MarchArmies(Rng rng)
    {
        var w = World;
        var p = w.Planet;
        int n = w.Armies.Count;
        for (int i = 0; i < n; i++)
        {
            var a = w.Armies[i];
            if (!a.Alive || a.Defender) continue;
            var target = w.Groups[a.Target];
            var from = w.Groups[a.Group];
            if (!target.Alive || !from.Alive || a.Polity < 0 || !w.Polities[a.Polity].Alive || target.PolityId == a.Polity)
            {
                EndArmy(a, 2);
                continue;
            }
            float dx = a.TX - a.X, dy = a.TY - a.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float v = ArmySpeed(from);
            // Горы и пустыни замедляют марш.
            int cell = p.EcoIndex((int)MathF.Round(a.X), Math.Clamp((int)MathF.Round(a.Y), 0, p.EH - 1));
            if (p.EElev[cell] > 1.5f || p.EPrecip[cell] < 250f) v *= 0.6f;
            if (d > v)
            {
                a.X += dx / d * v;
                a.Y += dy / d * v;
                a.Soldiers *= 0.98f;
                continue;
            }
            a.X = a.TX; a.Y = a.TY;
            var pol = w.Polities[a.Polity];
            bool win = ResolveBattle(pol, from, target, a.Soldiers, a.Holy, a.Declaration, rng);
            EndArmy(a, win ? 1 : -1);
            foreach (var o in w.Armies)
                if (o.Alive && o.Defender && o.Declaration == a.Declaration) EndArmy(o, win ? -1 : 1);
        }
        // Защитники идут навстречу, к атакованному народу.
        foreach (var a in w.Armies)
        {
            if (!a.Alive || !a.Defender) continue;
            float dx = a.TX - a.X, dy = a.TY - a.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float v = ArmySpeed(w.Groups[a.Group]);
            if (d > v) { a.X += dx / d * v; a.Y += dy / d * v; }
            else { a.X = a.TX; a.Y = a.TY; }
            if (w.Year - a.Started > 40) EndArmy(a, 2);
        }
        // Память о закончившихся походах — 8 лет (зритель показывает павшие знамёна), потом удаляются.
        for (int i = w.Armies.Count - 1; i >= 0; i--) if (!w.Armies[i].Alive && w.Year - w.Armies[i].Ended > 8) w.Armies.RemoveAt(i);
    }

    private void EndArmy(Army a, int outcome)
    {
        a.Alive = false;
        a.Outcome = outcome;
        a.Ended = World.Year;
    }
}
