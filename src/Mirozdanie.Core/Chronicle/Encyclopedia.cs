using System.Text;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Sim;
using static Mirozdanie.Core.Chronicle.Russian;

namespace Mirozdanie.Core.Chronicle;

/// <summary>Запись энциклопедии: заголовок, подзаголовок, текст, где на планете, связанные события.</summary>
public sealed record Entry(string Kind, int Id, string Title, string Subtitle, string Body, int Cell, IReadOnlyList<int> Events);

/// <summary>
/// «Летопись мира» как энциклопедия (docs/design/lore.md, §8): народы, веры (с мифами глазами разных народов),
/// языки (с эволюцией слов), державы (правители, договоры, обиды), герои, места, эпохи, открытия.
/// </summary>
public static class Encyclopedia
{
    public static readonly string[] Kinds = { "Народы", "Веры", "Языки", "Державы", "Герои", "Места", "Эпохи", "Открытия" };

    public static List<Entry> List(World w, Letopis l, ChronicleWriter writer, string kind) => kind switch
    {
        "Народы" => Peoples(w, l),
        "Веры" => Faiths(w, l, writer),
        "Языки" => Languages(w),
        "Державы" => Polities(w, l),
        "Герои" => Heroes(w, l),
        "Места" => Places(w, l),
        "Эпохи" => Eras(w, l),
        _ => Discoveries(w, l),
    };

    private static List<int> Related(World w, Letopis l, char k, int id, long from, int n)
    {
        var evs = l.Events(from, w.Year, 4f, EventTheme.All, e => l.Involves(e, k, id));
        if (evs.Count <= n) return evs;
        evs.Sort((a, b) => { int c = l.Weight(b).CompareTo(l.Weight(a)); return c != 0 ? c : a.CompareTo(b); });
        var top = evs.Take(n).ToList();
        top.Sort();
        return top;
    }

    private static string Subs(Subsistence s) => s switch
    {
        Subsistence.Farming => "земледельцы", Subsistence.Herding => "скотоводы", Subsistence.Fishing => "рыболовы", _ => "охотники и собиратели",
    };

    private static List<Entry> Peoples(World w, Letopis l)
    {
        var res = new List<Entry>();
        foreach (var g in w.Groups.Where(g => g.Alive).OrderByDescending(g => g.Pop).Take(80))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"«{g.Name.Gloss}». {Capitalize(Subs(g.Subsistence))}, {Number(g.Pop)} человек. Живут с {g.Founded} года.");
            sb.AppendLine($"Язык — {w.Languages[g.LanguageId].Autonym.Native}; вера — {(g.ReligionId >= 0 ? w.Religions[g.ReligionId].Name.Native : "нет")}; к Создателю — {ChronicleWriter.AttitudeName(g.Attitude)}.");
            if (g.PolityId >= 0) sb.AppendLine($"Держава: {w.Polities[g.PolityId].Name.Native}{(w.Polities[g.PolityId].Capital == g.Id ? " (столица)" : "")}.");
            sb.AppendLine($"Эпоха: {Technology.EraNames[Math.Clamp(g.EraIdx, 1, 9)]}; энергии на душу {g.Watts:0} Вт; открытий {Technology.CountOf(g.Techs)}.");
            var cities = w.Cities.Where(c => c.Alive && c.Group == g.Id).OrderByDescending(c => c.Pop).Take(4).ToList();
            if (cities.Count > 0) sb.AppendLine($"Города: {string.Join(", ", cities.Select(c => $"{c.Name.Native} («{c.Name.Gloss}», {Number(c.Pop)})"))}.");
            if (g.Epithets.Count > 0) sb.AppendLine($"Зовут Создателя: {string.Join(", ", g.Epithets.Select(e => $"{e.Name.Native} («{e.Name.Gloss}»)"))}.");
            res.Add(new Entry("Народы", g.Id, g.Name.Native, $"{Subs(g.Subsistence)}, {Number(g.Pop)}", sb.ToString(), g.Cell, Related(w, l, 'g', g.Id, g.Founded, 12)));
        }
        return res;
    }

    private static List<Entry> Faiths(World w, Letopis l, ChronicleWriter writer)
    {
        var res = new List<Entry>();
        var followers = new double[w.Religions.Count];
        foreach (var g in w.Groups) if (g.Alive && g.ReligionId >= 0) followers[g.ReligionId] += g.Pop;
        foreach (var r in w.Religions.OrderByDescending(r => followers[r.Id]).Take(60))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"«{r.Name.Gloss}». {ChronicleWriter.FormName(r.Form)}; Создателя зовут {r.CreatorName.Native} («{r.CreatorName.Gloss}»).");
            sb.AppendLine($"Сложилась в {r.Founded} году у народа {w.Groups[r.FounderGroup].Name.Native}{(r.Parent >= 0 ? $", отколовшись от веры {w.Religions[r.Parent].Name.Native}" : "")}." +
                          (followers[r.Id] > 0 ? $" Верующих: {Number(followers[r.Id])}." : " Ныне без верующих."));
            if (r.Prophet >= 0) sb.AppendLine($"Пророк: {w.People[r.Prophet].Name.Native}.");
            if (r.Rebel) sb.AppendLine("Мятежная вера: Создатель для неё — тиран.");
            // Миф глазами основателей и двух других народов этой веры.
            var tellers = new List<Group> { w.Groups[r.FounderGroup] };
            foreach (var g in w.Groups.Where(g => g.Alive && g.ReligionId == r.Id && g.Id != r.FounderGroup).OrderBy(g => g.LanguageId == w.Groups[r.FounderGroup].LanguageId ? 1 : 0).Take(2)) tellers.Add(g);
            foreach (var t in tellers)
            {
                var myth = Mythology.Tell(w, r, t, writer);
                if (myth.Scenes.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine($"{myth.Title} — как рассказывает народ {t.Name.Native}{(t.Alive ? "" : " (давно исчезнувший)")} (священное число — {myth.SacredNumber}):");
                foreach (var s in myth.Scenes) sb.AppendLine($"  ✦ {s.Told}\n      на самом деле: {s.Truth}");
            }
            res.Add(new Entry("Веры", r.Id, r.Name.Native, followers[r.Id] > 0 ? $"{Number(followers[r.Id])} верующих" : "угасла", sb.ToString(), w.Groups[r.FounderGroup].Cell, Related(w, l, 'r', r.Id, r.Founded, 12)));
        }
        return res;
    }

    private static List<Entry> Languages(World w)
    {
        var res = new List<Entry>();
        var speakers = new double[w.Languages.Count];
        int[] cell = Enumerable.Repeat(-1, w.Languages.Count).ToArray();
        foreach (var g in w.Groups) if (g.Alive) { speakers[g.LanguageId] += g.Pop; if (cell[g.LanguageId] < 0) cell[g.LanguageId] = g.Cell; }
        Concept[] sample = { Concept.Water, Concept.Fire, Concept.Sun, Concept.God, Concept.People, Concept.Mother };
        foreach (var L in w.Languages.Where(x => speakers[x.Id] > 0).OrderByDescending(x => speakers[x.Id]).Take(40))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Самоназвание: {L.Autonym.Native}. Говорящих: {Number(speakers[L.Id])}. Порядок слов: {L.Order}.");
            if (L.Parent >= 0) sb.AppendLine($"Отделился от языка {w.Languages[L.Parent].Autonym.Native} в {L.BornYear} году.");
            // Эволюция слов: от праязыка через предков к этому языку.
            var chain = new List<Lang>();
            for (var x = L; x != null; x = x.Parent >= 0 ? w.Languages[x.Parent] : null) chain.Insert(0, x);
            sb.AppendLine("Как менялись слова (от праязыка):");
            foreach (var c in sample)
                sb.AppendLine($"  {Concepts.Russian[(int)c]}: {string.Join(" → ", chain.Select(x => x.Cyr(c, false)).Distinct())}");
            if (L.History.Count > 0) sb.AppendLine($"Звуковые законы: {string.Join("; ", L.History.TakeLast(5))}.");
            res.Add(new Entry("Языки", L.Id, L.Autonym.Native, $"{Number(speakers[L.Id])} говорящих", sb.ToString(), cell[L.Id], Array.Empty<int>()));
        }
        return res;
    }

    private static List<Entry> Polities(World w, Letopis l)
    {
        var res = new List<Entry>();
        foreach (var pol in w.Polities.OrderByDescending(p => p.Alive).ThenByDescending(p => (p.Alive ? w.Year : p.Ended) - p.Founded).Take(80))
        {
            var sb = new StringBuilder();
            long end = pol.Alive ? w.Year : pol.Ended;
            sb.AppendLine($"«{pol.Name.Gloss}». {Capitalize(Simulation.FormTitle(pol.Form))}. Основана в {pol.Founded} году{(pol.Alive ? "" : $", пала в {pol.Ended}")} — {Years(end - pol.Founded)}.");
            var cap = w.Groups[pol.Capital];
            sb.AppendLine($"Столица — народ {cap.Name.Native}; народов в державе: {pol.Members.Count}; людей {Number(pol.Members.Sum(m => (double)w.Groups[m].Pop))}.");
            if (pol.Leader >= 0) sb.AppendLine($"Правитель: {w.People[pol.Leader].Name.Native}{(pol.Dynasty.Native.Length > 0 ? $", династия {pol.Dynasty.Native}" : "")}.");
            var allies = new List<string>(); var foes = new List<string>();
            foreach (var r in w.Relations)
            {
                if (r.A != pol.Id && r.B != pol.Id) continue;
                var other = w.Polities[r.A == pol.Id ? r.B : r.A];
                if (!other.Alive) continue;
                if (r.AllianceSince >= 0) allies.Add(other.Name.Native);
                int grudges = r.Grievances.Count(g => g.Holder == pol.Id);
                if (grudges > 0) foes.Add($"{other.Name.Native} ({grudges} {Plural(grudges, "обида", "обиды", "обид")})");
            }
            if (allies.Count > 0) sb.AppendLine($"Союзники: {string.Join(", ", allies)}.");
            if (foes.Count > 0) sb.AppendLine($"Помнит обиды: {string.Join(", ", foes)}.");
            res.Add(new Entry("Державы", pol.Id, pol.Name.Native, $"{Simulation.FormTitle(pol.Form)}, {pol.Founded}–{(pol.Alive ? "…" : pol.Ended.ToString())}", sb.ToString(), cap.Cell, Related(w, l, 'p', pol.Id, pol.Founded, 14)));
        }
        return res;
    }

    private static List<Entry> Heroes(World w, Letopis l)
    {
        var res = new List<Entry>();
        // Пророки — все; правители и мудрецы — последние.
        var people = w.People.Where(p => p.Role == PersonRole.Prophet).Concat(w.People.Where(p => p.Role != PersonRole.Prophet).Reverse().Take(60)).ToList();
        var log = w.Events.Log;
        foreach (var p in people.Take(120))
        {
            var sb = new StringBuilder();
            string role = p.Role switch { PersonRole.Prophet => "пророк", PersonRole.Leader => "правитель", _ => "мудрец" };
            sb.AppendLine($"«{p.Name.Gloss}». {Capitalize(role)} народа {w.Groups[p.Group].Name.Native}; родился в {p.Born} году{(p.Died >= 0 ? $", умер в {p.Died}" : "")}.");
            if (p.Polity >= 0) sb.AppendLine($"Держава: {w.Polities[p.Polity].Name.Native}.");
            if (p.Message.Length > 0) sb.AppendLine($"Учил: {p.Message}.");
            if (p.Martyr) sb.AppendLine("Принял мученическую смерть.");
            sb.AppendLine($"Черты: честолюбие {p.Ambition:0.0}, набожность {p.Piety:0.0}, жестокость {p.Cruelty:0.0}, мудрость {p.Wisdom:0.0}.");
            var evs = new List<int>();
            for (int i = 0; i < log.Count && evs.Count < 10; i++)
            {
                var e = log[i];
                if (e.Year < p.Born) continue;
                if (p.Died >= 0 && e.Year > p.Died + 1) break;
                if (e.Type is EventType.ProphetBorn or EventType.Martyrdom or EventType.Vision && (e.A == p.Id || e.B == p.Id)
                    || e.Type is EventType.LeaderRose or EventType.LeaderDied or EventType.LeaderStruck && e.B == p.Id
                    || e.Type == EventType.CultFounded && e.C == p.Id) evs.Add(i);
            }
            res.Add(new Entry("Герои", p.Id, p.Name.Native, $"{role}, {p.Born}–{(p.Died >= 0 ? p.Died.ToString() : "…")}", sb.ToString(), w.Groups[p.Group].Cell, evs));
        }
        return res;
    }

    private static List<Entry> Places(World w, Letopis l)
    {
        var res = new List<Entry>();
        foreach (var c in w.Cities.Where(c => c.Alive && c.Rank >= 1).OrderByDescending(c => c.Pop).Take(60))
        {
            var g = w.Groups[c.Group];
            string rank = c.Rank switch { 3 => "мегаполис", 2 => "большой город", _ => "город" };
            string body = $"«{c.Name.Gloss}». {Capitalize(rank)} народа {g.Name.Native}, {Number(c.Pop)} жителей. Заложен в {c.Founded} году." +
                          (c.Capital ? " Столица державы." : "") + (c.Holy ? " Святой город: рядом святыня." : "");
            res.Add(new Entry("Места", c.Id, c.Name.Native, rank, body, c.Cell, Array.Empty<int>()));
        }
        foreach (var s in w.Sites.Take(40))
        {
            string body = $"«{s.Name.Gloss}». Святыня веры {w.Religions[s.Religion].Name.Native} с {s.Founded} года{(s.Temple ? "; стоит храм" : "")}.";
            res.Add(new Entry("Места", 100000 + s.Id, s.Name.Native, "святыня", body, s.Cell, Array.Empty<int>()));
        }
        return res;
    }

    private static List<Entry> Eras(World w, Letopis l)
    {
        var res = new List<Entry>();
        foreach (var ch in l.Chapters().Where(c => c.Kind == "Эпоха"))
            res.Add(new Entry("Эпохи", (int)ch.From, ch.Title.Replace("Эпоха: ", ""), $"{ch.From}–{ch.To}", ch.Intro + "\n\n" + l.Summary(ch.From, ch.To, false), ch.Cell, ch.Events));
        return res;
    }

    private static List<Entry> Discoveries(World w, Letopis l)
    {
        var res = new List<Entry>();
        var first = new int[(int)Tech.Count];
        Array.Fill(first, -1);
        var log = w.Events.Log;
        for (int i = 0; i < log.Count; i++)
        {
            var e = log[i];
            if (e.Type == EventType.Discovery && e.C < 0 && e.B >= 0 && e.B < first.Length && first[e.B] < 0) first[e.B] = i;
        }
        foreach (var info in Technology.All)
        {
            int id = first[(int)info.Id];
            if (id < 0) continue;
            var e = log[id];
            int holders = w.Groups.Count(g => g.Alive && g.Has(info.Id));
            string body = $"Впервые — {e.Year} год, народ {w.Groups[e.A].Name.Native}: {info.Phrase}.\nЭпоха открытия: {Technology.EraNames[Math.Clamp(info.Era, 1, 9)]}. Сейчас знают {holders} {Plural(holders, "народ", "народа", "народов")}.";
            res.Add(new Entry("Открытия", (int)info.Id, Capitalize(info.Title), $"{e.Year}", body, e.Cell, new[] { id }));
        }
        return res;
    }
}
