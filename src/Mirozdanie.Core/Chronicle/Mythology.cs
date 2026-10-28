using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Chronicle;

/// <summary>Сцена мифа: настоящее событие (id), как было и как рассказывают.</summary>
public sealed record MythScene(int EventId, long Year, string Truth, string Told);

/// <summary>Миф веры глазами народа-рассказчика (lore.md, §2).</summary>
public sealed record Myth(int Religion, int Teller, string Title, IReadOnlyList<MythScene> Scenes, int SacredNumber);

/// <summary>
/// Мифы творения и потопа из настоящих событий (docs/design/lore.md, §2). Сцены берутся из журнала событий народа-основателя
/// и его предков; каждая проходит искажение пересказом (Bartlett 1932, серийное воспроизведение): на каждые 100 лет
/// и каждый раскол языка детали теряются, числа тянутся к «священному», природная причина становится волей бога
/// с вероятностью ∝ (1 − N) рассказчика, два пророка сливаются в одного «Отца». Чистая функция: хеш, без RNG симуляции.
/// </summary>
public static class Mythology
{
    public static Myth Tell(World w, Religion r, Group teller, ChronicleWriter? writer = null)
    {
        writer ??= new ChronicleWriter(w);
        var log = w.Events.Log;
        var founder = w.Groups[r.FounderGroup];
        // Священное число — из имени бога, а каждая ветвь языка «слышит» его по-своему (сдвиг на число расколов языка).
        int sacred = SacredNumber(r, LanguageDistance(w, w.Groups[r.FounderGroup].LanguageId, teller.LanguageId));
        // Родословная основателя: народ и его предки (по делению народов).
        var lineage = new HashSet<int>();
        for (var g = founder; g != null && lineage.Add(g.Id); g = g.Parent >= 0 ? w.Groups[g.Parent] : null) { }
        int langSplits = LanguageDistance(w, founder.LanguageId, teller.LanguageId);
        var scenes = new List<MythScene>();
        string Truth(int id)
        {
            var line = writer.Line(log[id]) ?? writer.Describe(log[id]);
            return line.Replace("- Год ", "Год ").Replace("**", "");
        }
        int Retellings(long year) => (int)((w.Year - year) / 100) + langSplits;
        string When(long year)
        {
            int k = Retellings(year);
            return k <= 0 ? $"в {year} году" : k <= 2 ? "при прадедах" : k <= 6 ? "в древние дни" : "в начале времён";
        }
        bool Agentive(int id) => Hash(id, teller.Id, 3) % 1000 < (int)(1000 * Math.Clamp(1f - teller.N, 0.05f, 0.95f) * Math.Min(1f, 0.35f + 0.15f * Retellings(log[id].Year)));
        string god = r.Form == DeityForm.None ? "Пустое небо" : r.CreatorName.Native;

        // 1. Пробуждение — откуда вышли первые люди (по месту Пробуждения).
        int awake = -1;
        for (int i = 0; i < log.Count; i++) if (log[i].Type == EventType.Awakening) { awake = i; break; }
        if (awake >= 0)
        {
            var p = w.Planet;
            int cell = log[awake].Cell >= 0 ? log[awake].Cell : founder.Cell;
            // Рассказчик заменяет незнакомое знакомым: морской народ выводит людей из моря, горный — из горы.
            int home = Retellings(log[awake].Year) >= 3 ? teller.Cell : cell;
            string from = p.ECoast[home] ? "из морской пены" : p.EElev[home] > 1.4f ? "из расщелины горы" : p.ERiver[home] > 0.15f ? "из ила великой реки"
                : p.ETemp[home] < 2f ? "из-подо льда" : p.EPrecip[home] > 1200f ? "из дупла древнего дерева" : "из красной глины";
            scenes.Add(new MythScene(awake, log[awake].Year, Truth(awake),
                $"{Russian.Capitalize(When(log[awake].Year))} {god} вылепил первых людей и вывел их {from}; их было {sacred}, и {(sacred % 2 == 1 ? "каждый" : "все")} знали имя неба."));
        }

        // 2. Первое чудо — первое толкование народа-основателя (или предков), где небо действовало.
        int miracle = -1;
        for (int i = awake + 1; i < log.Count && log[i].Year <= r.Founded; i++)
        {
            var e = log[i];
            if (e.Type == EventType.Interpretation && lineage.Contains(e.A) && (InterpretationKind)e.B != InterpretationKind.Natural && e.Cause >= 0) { miracle = e.Cause; break; }
        }
        if (miracle >= 0)
        {
            var src = log[miracle];
            string what = Thing(src.Type);
            bool agent = Agentive(miracle) || src.ByCreator;
            scenes.Add(new MythScene(miracle, src.Year, Truth(miracle), agent
                ? $"{Russian.Capitalize(When(src.Year))} {god} послал {what}, и голос с неба сказал: «Я вижу вас». {Repeat(sacred)} дня и ночи люди не смели поднять глаз."
                : $"{Russian.Capitalize(When(src.Year))} пришёл {what}, и мудрые поняли: мир живёт по своим законам."));
        }

        // 3. Катастрофа: потоп, огненный камень, извержение, великая засуха рядом с предками.
        int disaster = -1; float worst = 0;
        for (int i = awake + 1; i < log.Count; i++)
        {
            var e = log[i];
            float m = e.Type switch
            {
                EventType.SeaLevelChanged when e.A > 0 => 3f + e.B / 20f,
                EventType.GroupDrowned or EventType.Refugees => lineage.Contains(e.A) || e.A == teller.Id ? 2.5f : 0f,
                EventType.MeteorImpact => 2.5f,
                EventType.Eruption => 1.8f,
                EventType.Flood => 1.5f,
                EventType.NuclearWar => 3.5f,
                EventType.NaturalDrought or EventType.Drought => e.Magnitude,
                _ => 0f,
            };
            if (m <= 0) continue;
            bool near = e.Cell < 0 || w.Planet.EcoDistance(e.Cell, founder.Cell) <= 15f || w.Planet.EcoDistance(e.Cell, teller.Cell) <= 15f || e.Type == EventType.SeaLevelChanged;
            if (near && m > worst) { worst = m; disaster = i; }
        }
        if (disaster >= 0)
        {
            var e = log[disaster];
            bool agent = Agentive(disaster) || e.ByCreator;
            string told = e.Type switch
            {
                EventType.SeaLevelChanged or EventType.GroupDrowned or EventType.Refugees or EventType.Flood =>
                    agent ? $"{Russian.Capitalize(When(e.Year))} люди забыли {god}, и {god} поднял воды. {Repeat(sacred)} дней шёл дождь, и спаслись лишь те, кто ушёл на высокую землю."
                          : $"{Russian.Capitalize(When(e.Year))} пришли великие воды и забрали берега; спаслись те, кто ушёл на высокую землю.",
                EventType.MeteorImpact => agent ? $"{Russian.Capitalize(When(e.Year))} {god} разгневался и бросил с неба огненный камень; земля горела {sacred} лет." : $"{Russian.Capitalize(When(e.Year))} с неба упал огненный камень, и солнце скрылось на {sacred} лет.",
                EventType.Eruption => $"{Russian.Capitalize(When(e.Year))} гора открыла огненный рот{(agent ? $": так {god} наказал гордецов" : "")}.",
                EventType.NuclearWar => $"{Russian.Capitalize(When(e.Year))} люди сами зажгли {sacred} солнц на земле, и наступила долгая зима{(agent ? $"; {god} отвернулся от них" : "")}.",
                _ => agent ? $"{Russian.Capitalize(When(e.Year))} {god} закрыл небо, и {sacred} лет не было дождя, пока люди не принесли лучшее, что имели." : $"{Russian.Capitalize(When(e.Year))} была великая засуха: {sacred} лет без дождя.",
            };
            scenes.Add(new MythScene(disaster, e.Year, Truth(disaster), told));
        }

        // 4. Пророки: через 300 лет два пророка ветви сливаются в одного «Отца».
        var prophets = new List<int>();
        for (int i = awake + 1; i < log.Count; i++)
        {
            var e = log[i];
            if (e.Type == EventType.ProphetBorn && (r.Prophet >= 0 && e.A == r.Prophet || lineage.Contains(e.B) && e.Year <= r.Founded + 50)) prophets.Add(i);
        }
        if (prophets.Count > 0)
        {
            var first = log[prophets[0]];
            bool merged = prophets.Count >= 2 && w.Year - first.Year >= 300;
            string name = w.People[first.A].Name.Native;
            scenes.Add(new MythScene(prophets[0], first.Year, Truth(prophets[0]), merged
                ? $"И пришёл Отец — тот, кого одни зовут {name}, а другие иначе, — и принёс {sacred} заповедей."
                : $"{Russian.Capitalize(When(first.Year))} родился {name}, и {god} говорил его устами."));
        }

        // 5. Основание веры.
        int founded = -1;
        for (int i = awake + 1; i < log.Count; i++) if (log[i].Type is EventType.ReligionFounded or EventType.Schism && log[i].A == r.Id) { founded = i; break; }
        if (founded >= 0)
        {
            var e = log[founded];
            scenes.Add(new MythScene(founded, e.Year, Truth(founded), e.Type == EventType.Schism
                ? (teller.ReligionId == r.Id
                    ? $"{Russian.Capitalize(When(e.Year))} верные отделились от тех, кто исказил слово {god}, и ушли своим путём."
                    : $"{Russian.Capitalize(When(e.Year))} гордецы откололись от истинной веры и назвали свою ложь новым именем.")
                : $"{Russian.Capitalize(When(e.Year))} народ {founder.Name.Native} сложил первые песни {god}, и с тех пор их поют {sacred} раза в год."));
        }

        scenes.Sort((a, b) => a.Year.CompareTo(b.Year));
        // Рассказчик окрашивает конец мифа своим нынешним отношением к Создателю (Bartlett 1932: пересказ подстраивается под слушателя).
        if (scenes.Count > 0)
        {
            string coda = teller.Attitude switch
            {
                Attitude.Love => " Так мы стали его детьми, и с тех пор поём ему.",
                Attitude.Fear => " С тех пор мы боимся его гнева и не поднимаем глаз к небу.",
                Attitude.Bargain => " С тех пор у нас с ним уговор: мы — ему, он — нам.",
                Attitude.Rebellion => " Но мы помним и другое: всё это он сделал для себя, не для нас.",
                Attitude.Denial or Attitude.Nihilism => " Так говорят старики; мудрецы же считают это сказкой.",
                _ => " Так рассказывают; кто знает, как было.",
            };
            var last = scenes[^1];
            scenes[^1] = last with { Told = last.Told + coda };
        }
        string title = scenes.Count > 0 && disaster >= 0 && log[disaster].Type is EventType.SeaLevelChanged or EventType.GroupDrowned or EventType.Refugees or EventType.Flood
            ? $"Миф о потопе и начале — {r.Name.Native}" : $"Миф о начале — {r.Name.Native}";
        return new Myth(r.Id, teller.Id, title, scenes, sacred);
    }

    /// <summary>Есть ли в мифе сцена о потопе.</summary>
    public static bool HasFlood(Myth m) => m.Scenes.Any(s => s.Told.Contains("воды"));

    /// <summary>«Священное» число: процедурная нумерология из имени бога (lore.md §2).</summary>
    public static int SacredNumber(Religion r, int shift = 0)
    {
        int[] nums = { 3, 7, 12, 4, 9 };
        uint h = 0;
        foreach (var ch in r.CreatorName.Native.Length > 0 ? r.CreatorName.Native : r.Name.Native) h = h * 31 + ch;
        return nums[(h + (uint)shift) % (uint)nums.Length];
    }

    private static string Repeat(int n) => n switch { 3 => "Три", 4 => "Четыре", 7 => "Семь", 9 => "Девять", 12 => "Двенадцать", _ => n.ToString() };

    private static string Thing(EventType t) => t switch
    {
        EventType.LightningStrike => "молнию", EventType.Rain or EventType.DroughtEnded => "дождь", EventType.MiracleHarvest or EventType.Bounty => "щедрую жатву",
        EventType.Drought or EventType.NaturalDrought => "засуху", EventType.Vision => "видение", EventType.MeteorImpact => "огненный камень",
        EventType.Earthquake => "дрожь земли", EventType.Comet => "хвостатую звезду", EventType.Plague => "мор", EventType.Famine => "голод",
        EventType.Flood => "разлив", EventType.ColdSnap => "долгую зиму", _ => "знак",
    };

    /// <summary>Сколько разделений языка между языком основателя и языком рассказчика (по дереву языков).</summary>
    public static int LanguageDistance(World w, int a, int b)
    {
        if (a == b) return 0;
        var anc = new Dictionary<int, int>();
        int d = 0;
        for (int x = a; x >= 0 && !anc.ContainsKey(x); x = w.Languages[x].Parent) anc[x] = d++;
        d = 0;
        for (int y = b; y >= 0; y = w.Languages[y].Parent, d++)
            if (anc.TryGetValue(y, out int da)) return da + d;
        return 6;
    }

    private static uint Hash(int a, int b, int c)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)b) * 16777619u; h = (h ^ (uint)c) * 16777619u;
        return h ^ (h >> 15);
    }
}
