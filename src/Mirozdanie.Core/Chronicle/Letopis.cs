using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;
using static Mirozdanie.Core.Chronicle.Russian;

namespace Mirozdanie.Core.Chronicle;

/// <summary>Отрезок хронологии: тысячелетие, век или год — со сводкой и главными событиями.</summary>
public sealed record Period(long From, long To, string Title, string Summary, IReadOnlyList<int> Highlights);

/// <summary>Глава Летописи: эпоха, жизнь державы, путь веры, долгая война (lore.md §11).</summary>
public sealed record Chapter(string Kind, string Title, string Intro, long From, long To, IReadOnlyList<int> Events, int Cell);

/// <summary>Узел дерева причин: событие, его причина (рекурсивно) и прямые последствия.</summary>
public sealed record CauseNode(int Event, string Text, CauseNode? Cause, IReadOnlyList<(int ev, string text)> Consequences);

/// <summary>Тема события — для фильтров Летописи.</summary>
public enum EventTheme : byte { All, War, Faith, Nature, Creator, Discovery, Politics, People }

/// <summary>
/// Единая Летопись (docs/design/lore.md, §11): хронология «тысячелетие → век → год → событие» со сводками,
/// главы (эпохи, державы, веры, войны), дерево причин «почему», фильтры. Строится лениво из журнала событий, без копий.
/// </summary>
public sealed class Letopis
{
    private readonly World _w;
    private readonly ChronicleWriter _writer;
    private readonly int _start;
    private List<int>[]? _byCause;

    /// <summary>Порог значимости: события ниже не показываются в свёрнутых узлах.</summary>
    public const float Highlight = 14f;

    public Letopis(World w, ChronicleWriter writer)
    {
        _w = w;
        _writer = writer;
        var log = w.Events.Log;
        for (int i = 0; i < log.Count; i++) if (log[i].Type == EventType.Awakening) { _start = i + 1; break; }
    }

    public float Weight(int id) => _writer.Significance(_w.Events.Get(id));

    /// <summary>Текст события без даты (для Летописи и «почему»).</summary>
    public string Text(int id)
    {
        var e = _w.Events.Get(id);
        string? line;
        try { line = _writer.Line(e) ?? _writer.Describe(e); }
        catch (Exception) { line = null; }
        if (line == null) return $"{e.Type}";
        string pre = $"- Год {e.Year}. ";
        return line.StartsWith(pre, StringComparison.Ordinal) ? line[pre.Length..] : line.TrimStart('-', ' ');
    }

    public static string Icon(EventType t) => ThemeOf(t) switch
    {
        EventTheme.War => "⚔", EventTheme.Faith => "✦", EventTheme.Nature => "☁", EventTheme.Creator => "☀", EventTheme.Discovery => "⚙", EventTheme.Politics => "♜", _ => "•",
    };

    public static EventTheme ThemeOf(EventType t) => t switch
    {
        EventType.Battle or EventType.WarDeclared or EventType.HolyWar or EventType.NuclearWar or EventType.ArmyMarched or EventType.PeaceMade or EventType.TreatyBroken => EventTheme.War,
        EventType.ReligionFounded or EventType.Schism or EventType.CultFounded or EventType.ProphetBorn or EventType.Martyrdom or EventType.Conversion
            or EventType.SacredSiteFounded or EventType.ProphecyMade or EventType.ProphecyFulfilled or EventType.ProphecyFailed or EventType.CreatorNamed
            or EventType.EpithetGained or EventType.AttitudeShift or EventType.Interpretation or EventType.TempleBuilt or EventType.CultFaded => EventTheme.Faith,
        EventType.NaturalDrought or EventType.DroughtEnded or EventType.Earthquake or EventType.Comet or EventType.Plague or EventType.Famine or EventType.Eruption
            or EventType.MegafaunaGone or EventType.ClimateWarming or EventType.EnergyCrisis or EventType.Bounty => EventTheme.Nature,
        EventType.Discovery or EventType.EraBegan or EventType.KardashevMilestone or EventType.MegaprojectStarted or EventType.MegaprojectCompleted
            or EventType.AgricultureAdopted => EventTheme.Discovery,
        EventType.PolityFounded or EventType.LeaderDied or EventType.LeaderRose or EventType.LeaderStruck or EventType.Annexation or EventType.Revolt
            or EventType.PolitySplit or EventType.Collapse or EventType.MandateLost or EventType.GovernmentChanged or EventType.AllianceFormed => EventTheme.Politics,
        EventType.Intervention or EventType.LightningStrike or EventType.MiracleHarvest or EventType.Rain or EventType.Drought or EventType.Vision
            or EventType.MeteorImpact or EventType.CreatorAppeared or EventType.PrayerAnswered or EventType.Teaching or EventType.Healing or EventType.Blessing
            or EventType.Threat or EventType.DemandMet or EventType.DemandRefused or EventType.Terraformed => EventTheme.Creator,
        _ => EventTheme.People,
    };

    /// <summary>Касается ли событие народа / державы / веры (для фильтров и глав).</summary>
    public bool Involves(WorldEvent e, char kind, int id)
    {
        switch (kind)
        {
            case 'g':
                return e.Type switch
                {
                    EventType.Battle => e.A == id || e.B == id,
                    EventType.WarDeclared or EventType.HolyWar => e.A == id || e.B == id,
                    EventType.Annexation or EventType.Revolt => e.B == id,
                    EventType.ReligionFounded or EventType.CultFounded or EventType.ProphetBorn or EventType.Martyrdom => e.B == id,
                    EventType.PolityFounded => e.B == id,
                    EventType.Assimilated or EventType.GroupFissioned => e.A == id || e.B == id,
                    EventType.CityFounded => e.B == id,
                    EventType.LanguageSplit => e.C == id,
                    EventType.Interpretation or EventType.EpithetGained or EventType.AttitudeShift or EventType.Conversion or EventType.Plague or EventType.Famine
                        or EventType.Discovery or EventType.AgricultureAdopted or EventType.EnergyCrisis or EventType.Healing or EventType.Blessing or EventType.Threat
                        or EventType.DemandMet or EventType.DemandRefused or EventType.Teaching or EventType.PrayerAnswered or EventType.PrayerUnanswered
                        or EventType.CreatorAppeared or EventType.Forgetting or EventType.GroupFormed or EventType.GroupDestroyed or EventType.Refugees
                        or EventType.GroupDrowned or EventType.LightningStrike or EventType.Inspiration or EventType.TradeRoute => e.A == id || e.Type == EventType.TradeRoute && e.B == id,
                    _ => false,
                };
            case 'p':
                return e.Type switch
                {
                    EventType.PolityFounded or EventType.LeaderDied or EventType.LeaderStruck or EventType.LeaderRose or EventType.Annexation or EventType.Revolt
                        or EventType.Collapse or EventType.MandateLost or EventType.GovernmentChanged => e.A == id,
                    EventType.PolitySplit or EventType.PeaceMade or EventType.TreatyBroken or EventType.AllianceFormed or EventType.NuclearWar => e.A == id || e.B == id,
                    EventType.Battle => e.C == id,
                    EventType.WarDeclared or EventType.HolyWar => e.C == id || _w.Groups[e.A].PolityId == id,
                    EventType.DynastyBlessed or EventType.DynastyCursed => e.B == id,
                    EventType.ArmyMarched => e.B == id,
                    _ => false,
                };
            case 'r':
                return e.Type switch
                {
                    EventType.ReligionFounded or EventType.CreatorNamed or EventType.ProphecyMade or EventType.ProphecyFulfilled or EventType.ProphecyFailed
                        or EventType.CultFaded => e.A == id,
                    EventType.Schism => e.A == id || e.B == id,
                    EventType.Conversion => e.B == id || e.C == id,
                    EventType.SacredSiteFounded or EventType.CultFounded => e.B == id || e.A == id && e.Type == EventType.CultFounded,
                    EventType.Teaching => e.C == id,
                    _ => false,
                };
        }
        return false;
    }

    // ---------- Хронология ----------

    private (int first, int end) Range(long from, long to)
    {
        var log = _w.Events.Log;
        int lo = _start, hi = log.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (log[mid].Year < from) lo = mid + 1; else hi = mid; }
        int first = lo;
        hi = log.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (log[mid].Year <= to) lo = mid + 1; else hi = mid; }
        return (first, lo);
    }

    /// <summary>События отрезка с весом ≥ minWeight (и по фильтру темы), по порядку.</summary>
    public List<int> Events(long from, long to, float minWeight, EventTheme theme = EventTheme.All, Func<WorldEvent, bool>? filter = null)
    {
        var (a, b) = Range(from, to);
        var res = new List<int>();
        var log = _w.Events.Log;
        for (int i = a; i < b; i++)
        {
            var e = log[i];
            if (theme != EventTheme.All && ThemeOf(e.Type) != theme) continue;
            if (filter != null && !filter(e)) continue;
            if (_writer.Significance(e) < minWeight && !(e.Type == EventType.Intervention && minWeight <= Highlight)) continue;
            res.Add(i);
        }
        return res;
    }

    /// <summary>Главные события отрезка: до n самых весомых, по порядку лет.</summary>
    public List<int> Top(long from, long to, int n, EventTheme theme = EventTheme.All, Func<WorldEvent, bool>? filter = null)
    {
        var all = Events(from, to, 6f, theme, filter);
        all.Sort((x, y) => { int c = Weight(y).CompareTo(Weight(x)); return c != 0 ? c : x.CompareTo(y); });
        // Один тип события — не больше двух строк в сводке.
        var used = new Dictionary<EventType, int>();
        var res = new List<int>();
        foreach (var i in all)
        {
            var t = _w.Events.Get(i).Type;
            used.TryGetValue(t, out int u);
            if (u >= 2) continue;
            used[t] = u + 1;
            res.Add(i);
            if (res.Count >= n) break;
        }
        res.Sort();
        return res;
    }

    public List<Period> Millennia()
    {
        var res = new List<Period>();
        for (long m = 0; m * 1000 <= _w.Year; m++)
        {
            long from = m * 1000, to = Math.Min(_w.Year, from + 999);
            var top = Top(from, to, 5);
            res.Add(new Period(from, to, $"{Ordinal(m + 1)} тысячелетие (годы {from}–{to})", Summary(from, to, true), top));
        }
        return res;
    }

    public List<Period> Centuries(long millenniumFrom)
    {
        var res = new List<Period>();
        for (long c = millenniumFrom; c < millenniumFrom + 1000 && c <= _w.Year; c += 100)
        {
            long to = Math.Min(_w.Year, c + 99);
            res.Add(new Period(c, to, $"{Roman((int)(c / 100) + 1)} век (годы {c}–{to})", Summary(c, to, false), Top(c, to, 3)));
        }
        return res;
    }

    /// <summary>Сводка отрезка (lore.md §7.4): население, эпоха, державы и веры, что изменилось.</summary>
    public string Summary(long from, long to, bool millennium)
    {
        var w = _w;
        CenturyStat? a = null, b = null;
        // Начало — последняя сводка не позже начала отрезка; конец — последняя не позже конца.
        foreach (var s in w.Annals)
        {
            if (s.Year <= from || a == null && s.Year <= to) a = s;
            if (s.Year <= to + 1) b = s;
        }
        var parts = new List<string>();
        if (b is { } end)
        {
            string pop = end.Population >= 1e6 ? $"{Number(end.Population)} людей" : $"{Number(end.Population)} душ";
            parts.Add($"К концу — {pop}, {Plural(end.Peoples, "народ", "народа", "народов")} {end.Peoples}, " +
                      $"{end.Polities} {Plural(end.Polities, "держава", "державы", "держав")}, {end.Religions} {Plural(end.Religions, "вера", "веры", "вер")}" +
                      (end.Cities > 0 ? $", {end.Cities} {Plural(end.Cities, "город", "города", "городов")}" : "") + ".");
            parts.Add($"Эпоха мира — {Technology.EraNames[Math.Clamp(end.Era, 1, 9)]}; Кардашёв {Math.Max(0, end.Kardashev):0.00}." +
                      (end.Warming > 0.3f ? $" Потепление +{end.Warming:0.0} °C." : ""));
            if (a is { } start && start.Year != end.Year && start.Population > 0)
            {
                double k = end.Population / start.Population;
                if (k > 1.5) parts.Add($"Людей стало в {k:0.#} раза больше.");
                else if (k < 0.8) parts.Add($"Людей стало меньше на {Percent((float)(1 - k))}: время бед.");
            }
        }
        // Счёт войн, вер, открытий.
        var (i0, i1) = Range(from, to);
        int wars = 0, faiths = 0, finds = 0, splits = 0, creator = 0;
        var log = w.Events.Log;
        for (int i = i0; i < i1; i++)
        {
            switch (log[i].Type)
            {
                case EventType.Battle: wars++; break;
                case EventType.ReligionFounded: case EventType.Schism: faiths++; break;
                case EventType.Discovery when log[i].C < 0: finds++; break;
                case EventType.PolitySplit: case EventType.Collapse: splits++; break;
                case EventType.Intervention: creator++; break;
            }
        }
        var counts = new List<string>();
        if (wars > 0) counts.Add($"{wars} {Plural(wars, "битва", "битвы", "битв")}");
        if (faiths > 0) counts.Add($"{faiths} {Plural(faiths, "новая вера или раскол", "новые веры и расколы", "новых вер и расколов")}");
        if (finds > 0) counts.Add($"{finds} {Plural(finds, "открытие", "открытия", "открытий")}");
        if (splits > 0) counts.Add($"{splits} {Plural(splits, "падение держав", "падения держав", "падений держав")}");
        if (creator > 0) counts.Add($"{creator} {Plural(creator, "деяние Создателя", "деяния Создателя", "деяний Создателя")}");
        if (counts.Count > 0) parts.Add(Capitalize(List(counts)) + ".");
        return string.Join(" ", parts);
    }

    private static string Ordinal(long n) => n switch
    {
        1 => "Первое", 2 => "Второе", 3 => "Третье", 4 => "Четвёртое", 5 => "Пятое", 6 => "Шестое", 7 => "Седьмое", 8 => "Восьмое", 9 => "Девятое", 10 => "Десятое",
        _ => $"{n}-е",
    };

    public static string Roman(int n)
    {
        if (n <= 0) return n.ToString();
        (int v, string s)[] map = { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in map) while (n >= v) { sb.Append(s); n -= v; }
        return sb.ToString();
    }

    // ---------- Почему ----------

    private List<int>[] ByCause()
    {
        if (_byCause != null) return _byCause;
        var log = _w.Events.Log;
        _byCause = new List<int>[log.Count];
        for (int i = 0; i < log.Count; i++)
        {
            int c = log[i].Cause;
            if (c >= 0 && c < log.Count) (_byCause[c] ??= new List<int>()).Add(i);
        }
        return _byCause;
    }

    /// <summary>Дерево причин назад (до глубины 8) и прямые последствия каждого звена (до 6).</summary>
    public CauseNode Why(int id, int depth = 0)
    {
        var e = _w.Events.Get(id);
        var by = ByCause();
        var cons = new List<(int, string)>();
        if (by[id] != null)
            foreach (var c in by[id])
            {
                if (cons.Count >= 6) break;
                cons.Add((c, $"{_w.Events.Get(c).Year}: {Text(c)}"));
            }
        CauseNode? cause = e.Cause >= 0 && depth < 8 ? Why(e.Cause, depth + 1) : null;
        return new CauseNode(id, $"{e.Year}: {Text(id)}", cause, cons);
    }

    /// <summary>«Почему» без явной причины: объяснение из обстоятельств (голод, война, вера, отношение).</summary>
    public string Context(int id)
    {
        var w = _w;
        var e = w.Events.Get(id);
        int gid = e.Type switch
        {
            EventType.Battle or EventType.Interpretation or EventType.AttitudeShift or EventType.Plague or EventType.Famine or EventType.Discovery => e.A,
            EventType.Revolt or EventType.Annexation => e.B,
            EventType.WarDeclared or EventType.HolyWar => e.A,
            _ => -1,
        };
        if (gid < 0 || gid >= w.Groups.Count) return "";
        var g = w.Groups[gid];
        var why = new List<string>();
        // Что случилось с народом за 30 лет до события.
        var (a, b) = Range(e.Year - 30, e.Year);
        var log = w.Events.Log;
        for (int i = b - 1; i >= a && why.Count < 4; i--)
        {
            if (i == id) continue;
            var x = log[i];
            if (!Involves(x, 'g', gid) || _writer.Significance(x) < 6f) continue;
            why.Add($"{x.Year}: {Text(i)}");
        }
        return why.Count > 0 ? $"Что было с народом {g.Name.Native} перед этим:\n" + string.Join("\n", why.Select(s => "— " + s)) : "";
    }

    // ---------- Главы ----------

    public List<Chapter> Chapters()
    {
        var w = _w;
        var res = new List<Chapter>();
        var log = w.Events.Log;
        // Эпохи мира.
        var eras = new List<int>();
        for (int i = _start; i < log.Count; i++) if (log[i].Type == EventType.EraBegan) eras.Add(i);
        long prev = 0;
        string prevTitle = Technology.EraNames[1];
        int prevCell = -1;
        for (int k = 0; k <= eras.Count; k++)
        {
            long to = k < eras.Count ? log[eras[k]].Year - 1 : w.Year;
            if (to >= prev)
                res.Add(new Chapter("Эпоха", $"Эпоха: {prevTitle}", k == 0 ? "От Пробуждения до первых великих открытий." : Text(eras[k - 1]), prev, to, Top(prev, to, 30), prevCell));
            if (k < eras.Count)
            {
                var e = log[eras[k]];
                prev = e.Year;
                prevTitle = Technology.EraNames[Math.Clamp(e.A, 1, 9)];
                prevCell = e.Cell;
            }
        }
        // Державы, прожившие ≥ 150 лет.
        foreach (var pol in w.Polities)
        {
            long end = pol.Alive ? w.Year : pol.Ended;
            if (end - pol.Founded < 150) continue;
            var evs = Events(pol.Founded, end, 3f, EventTheme.All, e => Involves(e, 'p', pol.Id));
            if (evs.Count < 3) continue;
            if (evs.Count > 40) evs = Thin(evs, 40);
            string fate = pol.Alive ? $"стоит и ныне, {Simulation.FormTitle(pol.Form)}" : $"пала в {pol.Ended} году";
            res.Add(new Chapter("Держава", $"Держава {pol.Name.Native} ({pol.Founded}–{(pol.Alive ? "…" : pol.Ended.ToString())})",
                $"«{pol.Name.Gloss}». Основана в {pol.Founded} году, {fate}.", pol.Founded, end, evs, w.Groups[pol.Capital].Cell));
        }
        // Веры с историей.
        foreach (var r in w.Religions)
        {
            var evs = Events(r.Founded, w.Year, 3f, EventTheme.All, e => Involves(e, 'r', r.Id));
            if (evs.Count < 4) continue;
            if (evs.Count > 40) evs = Thin(evs, 40);
            res.Add(new Chapter("Вера", $"Вера {r.Name.Native}: от пророка до наших дней", $"«{r.Name.Gloss}». Сложилась в {r.Founded} году у народа {w.Groups[r.FounderGroup].Name.Native}.",
                r.Founded, w.Year, evs, w.Groups[r.FounderGroup].Cell));
        }
        // Долгие войны: пары держав с тремя и больше войнами.
        foreach (var rel in w.Relations)
        {
            if (rel.Wars < 3) continue;
            var a = w.Polities[rel.A]; var b = w.Polities[rel.B];
            var evs = Events(Math.Max(a.Founded, b.Founded), w.Year, 3f, EventTheme.War, e => (Involves(e, 'p', a.Id) && Involves(e, 'p', b.Id)) || e.Type == EventType.Battle && (e.C == a.Id || e.C == b.Id) && OtherSide(e, a.Id, b.Id));
            if (evs.Count < 3) continue;
            res.Add(new Chapter("Война", $"Вековая вражда: {a.Name.Native} и {b.Name.Native}", $"{rel.Wars} {Plural(rel.Wars, "война", "войны", "войн")}; обид в памяти: {rel.Grievances.Count}.",
                w.Events.Get(evs[0]).Year, w.Events.Get(evs[^1]).Year, evs, w.Groups[a.Capital].Cell));
        }
        return res;
    }

    private bool OtherSide(WorldEvent e, int a, int b)
    {
        int other = e.C == a ? b : a;
        int loser = e.B, winner = e.A;
        return _w.Groups[loser].PolityId == other || _w.Groups[winner].PolityId == other;
    }

    private List<int> Thin(List<int> evs, int n)
    {
        var sorted = new List<int>(evs);
        sorted.Sort((x, y) => { int c = Weight(y).CompareTo(Weight(x)); return c != 0 ? c : x.CompareTo(y); });
        var res = sorted.Take(n).ToList();
        res.Sort();
        return res;
    }
}
