using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Creator;

/// <summary>Кто говорит от имени народа (lore.md, §12.1).</summary>
public enum Voice : byte { Elder, Priest, Ruler, Sage, Prophet, Rebel, Child }

/// <summary>Манера речи, выведенная из культуры и отношения (lore.md, §12.1).</summary>
public enum Manner : byte { Solemn, Business, Poetic, Terse, Defiant, Rational }

/// <summary>
/// Память разговора: какие шаблоны уже звучали (их не повторяем, пока есть другие) и номер реплики.
/// Живёт в зрителе, а не в мире: на ход истории не влияет. Множество — только для проверки «было ли», без обхода.
/// </summary>
public sealed class Conversation
{
    public int Turn;
    public readonly HashSet<string> Used = new();
    public float MoodShift;
}

/// <summary>
/// Генеративная речь (lore.md, §12): ответ собирается из смысловых ходов (Searle 1969) — обращение, реакция, довод из памяти,
/// положение народа, просьба, обещание или угроза, формула завершения. Каждый ход — набор условных шаблонов;
/// выбор — хешем от (народ, год, номер реплики, действие), без RNG симуляции; прозвучавшие шаблоны исключаются.
/// </summary>
public static class Speech
{
    private sealed class Ctx
    {
        public World W = null!;
        public Group G = null!;
        public Lang L = null!;
        public string C = "", CG = "";            // имя Создателя и перевод
        public Voice Voice;
        public Manner Manner;
        public Name Speaker;
        public Need Need;
        public Attitude Att;
        public DialogueAction? Act;
        public string Message = "";
        public Conversation Conv = null!;
        public int Salt;
        public int Repeats;                       // сколько раз Создатель уже делал это с народом
        public string City = "";                  // крупнейший город народа
        public string Enemy = "";                 // держава-обидчик
        public string Neighbor = "";              // соседний народ
        public int UnansweredYear = -1, AnsweredYear = -1;
        public readonly List<(string key, string text)> Memories = new();
        public Petition? Petition;
        public Oath? OpenOath;
    }

    public static DialogueLine Speak(World w, Group g, DialogueAction? action, Conversation? conv = null, string message = "")
    {
        var x = Build(w, g, action, conv ?? new Conversation(), message);
        var parts = new List<string>();
        foreach (var move in Order(x))
        {
            var s = move switch
            {
                0 => Address(x), 1 => Reaction(x), 2 => Memory(x), 3 => Situation(x), 4 => Request(x), _ => Promise(x),
            };
            if (!string.IsNullOrEmpty(s)) parts.Add(s);
        }
        var (native, _) = NativePhrase(x);
        var (prov, provGloss) = Proverb(x);
        bool longSpeech = parts.Count >= 4;
        string nativeLine = $"{x.C}! {native}" + (longSpeech ? " … " : " ") + (x.Manner is Manner.Terse ? "" : prov);
        string translation = string.Join(" ", parts);
        if (longSpeech && x.Manner is not Manner.Terse) translation += $" — «{provGloss}»";
        string narration = Narration(x);
        x.Conv.Turn++;
        float mood = Math.Clamp(Dialogue.Mood(g) + MoodDelta(x), 0f, 1f);
        return new DialogueLine(nativeLine.Trim(), translation, narration, mood, $"{VoiceTitle(x.Voice)} {x.Speaker.Native}");
    }

    // ---------- Контекст ----------

    private static Ctx Build(World w, Group g, DialogueAction? action, Conversation conv, string message)
    {
        var x = new Ctx
        {
            W = w, G = g, L = w.LangOf(g), C = Dialogue.CreatorName(w, g), CG = Dialogue.CreatorGloss(w, g), Need = Dialogue.NeedOf(w, g),
            Att = g.Attitude, Act = action, Conv = conv, Message = message,
        };
        x.Salt = (int)(Mix((uint)g.Id, (uint)w.Year, (uint)conv.Turn, (uint)(action.HasValue ? (int)action.Value + 1 : 0)) & 0x7fffffff);
        var rel = g.ReligionId >= 0 ? w.Religions[g.ReligionId] : null;
        Polity? pol = g.PolityId >= 0 ? w.Polities[g.PolityId] : null;
        // Голос.
        Person? prophet = null;
        for (int i = w.People.Count - 1; i >= 0 && i >= w.People.Count - 400; i--)
        {
            var p = w.People[i];
            if (p.Group == g.Id && p.Role == PersonRole.Prophet && p.Died < 0) { prophet = p; break; }
        }
        bool rebel = rel?.Rebel == true || g.Attitude == Attitude.Rebellion;
        if (rebel) x.Voice = Voice.Rebel;
        else if (prophet != null && g.Attitude is not (Attitude.Denial or Attitude.Nihilism)) x.Voice = Voice.Prophet;
        else if (pol != null && pol.Capital == g.Id && pol.Leader >= 0) x.Voice = pol.Form == GovForm.Theocracy ? Voice.Priest : Voice.Ruler;
        else if (g.Attitude is Attitude.Denial or Attitude.Nihilism || g.N > 0.55f) x.Voice = Voice.Sage;
        else if (g.Culture[CultureTrait.Piety] > 0.6f) x.Voice = Voice.Priest;
        else x.Voice = Voice.Elder;
        if (action is DialogueAction.Appear or DialogueAction.Ask && x.Salt % 13 == 0) x.Voice = Voice.Child;
        x.Speaker = x.Voice switch
        {
            Voice.Prophet => prophet!.Name,
            Voice.Ruler => w.People[pol!.Leader].Name,
            _ => SpeakerName(x),
        };
        // Манера.
        float ritual = rel?.Doctrine[Doctrine.Ritual] ?? 0f;
        var pp = w.Planet;
        bool harsh = pp.ETemp[g.Cell] < 0f || pp.EPrecip[g.Cell] < 300f;
        x.Manner = x.Voice == Voice.Rebel || g.Attitude == Attitude.Rebellion ? Manner.Defiant
            : x.Voice == Voice.Sage || g.Attitude is Attitude.Denial or Attitude.Nihilism ? Manner.Rational
            : g.Attitude == Attitude.Fear && (harsh || x.Need != Need.None) || x.Voice == Voice.Child ? Manner.Terse
            : g.Attitude == Attitude.Bargain || g.TradePartners.Count >= 3 && g.Attitude != Attitude.Love ? Manner.Business
            : ritual > 0.55f || x.Voice is Voice.Priest or Voice.Ruler ? Manner.Solemn
            : Manner.Poetic;
        // Город, враг, сосед.
        float best = 0;
        foreach (var c in w.Cities) if (c.Alive && c.Group == g.Id && c.Pop > best) { best = c.Pop; x.City = c.Name.Native; }
        if (pol != null)
            foreach (var r in w.Relations)
            {
                if (r.A != pol.Id && r.B != pol.Id) continue;
                foreach (var gr in r.Grievances)
                    if (gr.Holder == pol.Id) { x.Enemy = w.Polities[r.A == pol.Id ? r.B : r.A].Name.Native; break; }
                if (x.Enemy.Length > 0) break;
            }
        foreach (var n in g.Neighbors) if (w.Groups[n].Alive) { x.Neighbor = w.Groups[n].Name.Native; break; }
        // Молитвы.
        if (g.LastPrayer >= 0 && g.PrayerAnswered < g.LastPrayer) x.UnansweredYear = (int)g.LastPrayer;
        if (g.PrayerAnswered >= 0) x.AnsweredYear = (int)g.PrayerAnswered;
        // Обеты Создателя этому народу — самые сильные доводы (god-loop.md §3).
        foreach (var o in w.Oaths)
        {
            if (o.Group != g.Id || o.Status == 0 || x.Memories.Count >= 3) continue;
            string what = Simulation.OathTitle(o.Kind);
            x.Memories.Add(($"oath{o.Id}", o.Status == 1
                ? $"в {o.Year} году ты сказал нам «{what}» — и сдержал слово"
                : $"в {o.Year} году ты сказал нам «{what}» — и обманул"));
        }
        foreach (var pt in w.Petitions)
            if (pt.Group == g.Id && pt.Status == -1 && x.Memories.Count < 4)
                x.Memories.Add(($"pet{pt.Id}", $"в {pt.Year} году мы просили у тебя {PetGen(pt.Kind)} — и не дождались"));
        x.Petition = null;
        for (int i = w.Petitions.Count - 1; i >= 0; i--) if (w.Petitions[i].Group == g.Id && w.Petitions[i].Status == 0) { x.Petition = w.Petitions[i]; break; }
        x.OpenOath = null;
        foreach (var o in w.Oaths) if (o.Group == g.Id && o.Status == 0) x.OpenOath = o;
        // Что Создатель уже делал с народом (из журнала событий, последние ~6000).
        var want = action switch
        {
            DialogueAction.Bless => EventType.Blessing, DialogueAction.Demand => EventType.DemandMet, DialogueAction.Threaten => EventType.Threat,
            DialogueAction.Appear => EventType.CreatorAppeared, DialogueAction.Teach => EventType.Teaching, DialogueAction.AnswerPrayer => EventType.PrayerAnswered,
            _ => EventType.None,
        };
        var log = w.Events.Log;
        for (int i = log.Count - 1; i >= 0 && i >= log.Count - 6000; i--)
        {
            var e = log[i];
            if (e.A != g.Id) continue;
            if (want != EventType.None && (e.Type == want || want == EventType.DemandMet && e.Type == EventType.DemandRefused)) x.Repeats++;
            if (x.Memories.Count < 6 && (e.ByCreator || e.Type is EventType.LeaderStruck or EventType.Refugees or EventType.GroupDrowned))
            {
                string? m = e.Type switch
                {
                    EventType.Blessing => $"в {e.Year} году ты благословил нашу землю",
                    EventType.Threat => $"в {e.Year} году ты грозил нам с тёмного неба",
                    EventType.DemandMet => $"в {e.Year} году мы отдали тебе лучшее, что имели",
                    EventType.DemandRefused => $"в {e.Year} году мы отказали тебе в дани — и живы",
                    EventType.CreatorAppeared => $"в {e.Year} году ты явился нашим отцам",
                    EventType.Teaching => $"в {e.Year} году ты учил нас",
                    EventType.PrayerAnswered => $"в {e.Year} году ты услышал нашу мольбу",
                    EventType.LightningStrike => $"в {e.Year} году твоя молния ударила в нашу землю",
                    EventType.Healing => $"в {e.Year} году мор отступил по твоему слову",
                    EventType.Refugees => $"в {e.Year} году вода прогнала нас с отчих мест",
                    _ => null,
                };
                if (m != null) x.Memories.Add(($"ev{e.Type}{e.Year}", m));
            }
        }
        // Память народа: по одному самому свежему воспоминанию каждого рода событий — доводы не повторяют друг друга.
        Span<bool> seenEv = stackalloc bool[(int)EventType.Count];
        for (int k = 0; k < g.MemoryCount && x.Memories.Count < 8; k++)
        {
            int idx = (g.MemoryHead - 1 - k + g.Memory.Length) % g.Memory.Length;
            var m = g.Memory[idx];
            var (what, gen) = EventWord(m.Event);
            if (what.Length == 0 || seenEv[(int)m.Event]) continue;
            seenEv[(int)m.Event] = true;
            string was = gen switch { 'f' => "была", 'n' => "было", 'p' => "были", _ => "был" };
            string came = gen switch { 'f' => "пришла", 'n' => "пришло", 'p' => "пришли", _ => "пришёл" };
            string how = m.Kind switch
            {
                InterpretationKind.Punishment => $"{was} твоей карой", InterpretationKind.Wrath => $"{was} твоим гневом", InterpretationKind.Trial => $"{was} твоим испытанием",
                InterpretationKind.Gift => $"{was} твоим даром", InterpretationKind.Bargain => $"{was} платой за наши жертвы", InterpretationKind.Omen => $"{was} знамением",
                InterpretationKind.Favoritism => $"{came} к нам, а милость — к другим", _ => $"{came} сама собой, без тебя".Replace("сама собой", gen switch { 'f' => "сама собой", 'n' => "само собой", 'p' => "сами собой", _ => "сам собой" }),
            };
            x.Memories.Add(($"mem{m.Event}{m.Year}", $"{what} {m.Year} года {how}"));
        }
        return x;
    }

    /// <summary>Слово для события и его род (m, f, n, p — мн. ч.) — для согласования глаголов.</summary>
    private static (string word, char gender) EventWord(EventType t) => t switch
    {
        EventType.Drought or EventType.NaturalDrought => ("засуха", 'f'),
        EventType.Rain or EventType.DroughtEnded => ("дождь", 'm'),
        EventType.LightningStrike => ("молния", 'f'),
        EventType.MiracleHarvest or EventType.Bounty => ("щедрая жатва", 'f'),
        EventType.MeteorImpact => ("огненный камень", 'm'),
        EventType.Plague => ("моровое поветрие", 'n'),
        EventType.Famine => ("голодная зима", 'f'),
        EventType.Earthquake => ("землетрясение", 'n'),
        EventType.Vision => ("ночное видение", 'n'),
        EventType.Battle => ("война", 'f'),
        EventType.Flood or EventType.SeaLevelChanged => ("большая вода", 'f'),
        EventType.Eruption => ("огненная гора", 'f'),
        EventType.ColdSnap => ("долгая зима", 'f'),
        EventType.Comet => ("хвостатая звезда", 'f'),
        _ => ("", 'm'),
    };

    private static uint Mix(uint a, uint b, uint c, uint d)
    {
        uint h = 2166136261u;
        foreach (var v in new[] { a, b, c, d }) { h ^= v; h *= 16777619u; h ^= h >> 13; }
        return h;
    }

    private static Name SpeakerName(Ctx x)
    {
        Concept[] pool = { Concept.Old, Concept.Stone, Concept.River, Concept.Wind, Concept.Star, Concept.Tree, Concept.Clear, Concept.Moon, Concept.Bird, Concept.Fire, Concept.Silent, Concept.Heart };
        uint h = Mix((uint)x.G.Id, (uint)x.Voice, 7u, (uint)(x.W.Year / 30));
        var a = pool[h % (uint)pool.Length];
        var b = pool[(h >> 8) % (uint)pool.Length];
        if (b == a) b = Concept.Voice;
        return x.L.MakeName(x.L.WithSuffix(x.L.Compound(a, b), Concept.SuffixPerson), $"{Concepts.Russian[(int)a]}-{Concepts.Russian[(int)b]}");
    }

    /// <summary>Выбрать вариант: по хешу, без уже звучавших, пока есть другие.</summary>
    private static string Pick(Ctx x, string move, IReadOnlyList<(string key, string text)> options)
    {
        if (options.Count == 0) return "";
        int fresh = 0;
        foreach (var o in options) if (!x.Conv.Used.Contains(move + o.key)) fresh++;
        int k = (int)(Mix((uint)x.Salt, (uint)move.Length, (uint)move[0], (uint)options.Count) % (uint)Math.Max(1, fresh > 0 ? fresh : options.Count));
        foreach (var o in options)
        {
            if (fresh > 0 && x.Conv.Used.Contains(move + o.key)) continue;
            if (k-- == 0) { x.Conv.Used.Add(move + o.key); return o.text; }
        }
        return options[0].text;
    }

    private static List<(string, string)> V(params (string, string)[] items) => new(items);

    /// <summary>Порядок ходов по манере (lore.md, §12.2): 0 обращение, 1 реакция, 2 память, 3 положение, 4 просьба, 5 обещание; формула завершения — всегда в конце.</summary>
    private static int[] Order(Ctx x) => x.Manner switch
    {
        Manner.Solemn => new[] { 0, 1, 2, 3, 4, 5 },
        Manner.Business => new[] { 0, 1, 4, 5, 2 },
        Manner.Poetic => new[] { 0, 1, 3, 2, 4 },
        Manner.Terse => x.Act == null ? new[] { 1, 4 } : new[] { 0, 1, 4 },
        Manner.Defiant => new[] { 0, 1, 2, 5 },
        _ => new[] { 0, 1, 2, 3, 4 },
    };

    // ---------- 1. Обращение ----------

    private static string Address(Ctx x)
    {
        string c = x.C, cg = x.CG.ToLowerInvariant();
        string domain = x.W.Planet.ECoast[x.G.Cell] ? "моря и прибоя" : x.W.Planet.EElev[x.G.Cell] > 1.4f ? "гор и снегов" : x.W.Planet.ERiver[x.G.Cell] > 0.15f ? "реки и разлива" : "неба и полей";
        string eps = x.G.Epithets.Count > 1 ? $", {x.G.Epithets[1].Name.Native}, {x.G.Epithets[1].Name.Gloss.ToLowerInvariant()}" : "";
        return x.Manner switch
        {
            Manner.Solemn => Pick(x, "addr", V(
                ("s1", $"О {c}, {cg}{eps}, владыка {domain}!"),
                ("s2", $"Слава тебе, {c}, {cg}, и трижды слава!"),
                ("s3", $"{c}, {cg}, — мы, народ {x.G.Name.Native}, склоняем головы."),
                ("s4", $"Внемли, {c}! Говорит {VoiceTitle(x.Voice)} {x.Speaker.Native} от всего народа {x.G.Name.Native}."))),
            Manner.Business => Pick(x, "addr", V(("b1", $"{c}."), ("b2", $"{c}, выслушай по делу."), ("b3", $"{c}, ты пришёл — значит, будет разговор."))),
            Manner.Poetic => Pick(x, "addr", V(
                ("p1", $"{c}, ты, что ходишь над {(x.W.Planet.ECoast[x.G.Cell] ? "волнами" : "травами")} босиком!"),
                ("p2", $"{c}, ты — свет на воде и тень на холме!"),
                ("p3", $"{c}, {cg}, — тебя поют наши матери над колыбелью."),
                ("p4", $"{c}! Песня наша поднимается к тебе, как дым очага."))),
            Manner.Terse => Pick(x, "addr", V(("t1", $"{c}…"), ("t2", $"{c}. Мы здесь."), ("t3", $"{c}!"))),
            Manner.Defiant => Pick(x, "addr", V(
                ("d1", $"Так это ты, кого наши отцы звали {c}!"),
                ("d2", $"{c}. Ты, кого зовут «{cg}», — мы тебя не звали."),
                ("d3", $"Явился, {c}? Смотри нам в глаза."),
                ("d4", $"{c}, владыка, которому не нужны рабы, — тебе ответят свободные."))),
            _ => Pick(x, "addr", V(
                ("r1", $"Тот, кого простые люди зовут {c}…"),
                ("r2", $"Если ты и правда {c}, о котором поют, — слушай."),
                ("r3", $"Сущность, которую называют «{cg}», — допустим, ты есть."),
                ("r4", $"Мы записываем этот разговор, {c}. Каждое слово."))),
        };
    }

    // ---------- 2. Реакция на действие ----------

    private static string Reaction(Ctx x)
    {
        bool again = x.Repeats >= 1;
        string c = x.C;
        switch (x.Act)
        {
            case null: return Opening(x);
            case DialogueAction.Ask:
                return x.Att switch
                {
                    Attitude.Love => Pick(x, "re", V(("l1", "Ты спрашиваешь, что мы думаем о тебе? Мы думаем о тебе, когда сеем и когда жнём."), ("l2", "Что думаем? Что без тебя не было бы ни дождя, ни нас."), ("l3", "Ты — тот, кто дал нам этот берег и эти звёзды. Так учат старики."))),
                    Attitude.Fear => Pick(x, "re", V(("f1", "Мы думаем, что твой гнев близко, и боимся сказать лишнее."), ("f2", "Ты спрашиваешь? Мы не смеем судить небо."), ("f3", "Мы думаем о тебе шёпотом, чтобы ты не услышал."))),
                    Attitude.Bargain => Pick(x, "re", V(("b1", "Что думаем? Что с тобой можно договориться, если платить в срок."), ("b2", "Ты даёшь, когда получаешь. Это честно — мы так и считаем."), ("b3", "Мы думаем, что у нас с тобой счёт, и он пока не закрыт."))),
                    Attitude.Rebellion => Pick(x, "re", V(("r1", "Думаем? Мы думаем, что ты тиран, и это не тайна."), ("r2", "Мы думаем о тебе так же, как раб о хозяине. Только мы больше не рабы."), ("r3", "Ты хочешь услышать похвалу? Ищи её у других."))),
                    Attitude.Denial or Attitude.Nihilism => Pick(x, "re", V(("d1", "Мы думаем, что тебя нет, и этот разговор — наш сон."), ("d2", "Мы думаем, что всё, что приписывали тебе, объясняется без тебя."), ("d3", "Мы думаем, что вопрос «что мы думаем о тебе» стоит задать тебе самому."))),
                    _ => Pick(x, "re", V(("i1", "Мы почти не думаем о тебе. Поля требуют больше внимания."), ("i2", "О тебе рассказывают бабки. Мы слушаем вполуха."), ("i3", "Думаем? Есть ты или нет — хлеб от этого не растёт."))),
                };
            case DialogueAction.Appear:
                return x.Att switch
                {
                    Attitude.Love => Pick(x, "re", V(("l1", "Мы видим тебя! Матери поднимают детей, чтобы и они увидели."), ("l2", "Свет! Свет над нами — и он смотрит на нас!"), ("l3", again ? "Ты снова пришёл! Значит, наши песни долетают до неба." : "Сбылось: мы увидели того, кому пели."))),
                    Attitude.Fear => Pick(x, "re", V(("f1", "Не смотри на нас так! Мы падаем ниц, мы не поднимаем глаз!"), ("f2", "Ты пришёл — значит, кто-то согрешил. Скажи кто, и мы сами его накажем."), ("f3", "Ужас! Скот бьётся в загонах, дети плачут."))),
                    Attitude.Bargain => Pick(x, "re", V(("b1", "Ты пришёл сам — значит, тебе что-то нужно. Говори цену."), ("b2", "Явился лично? Это дорого стоит. Чего ты хочешь взамен?"))),
                    Attitude.Rebellion => Pick(x, "re", V(("r1", "Ты думал, мы упадём на колени? Мы стоим."), ("r2", "Явился, чтобы напугать? Поздно: страх у нас кончился."), ("r3", "Смотри, смотри на нас — мы тебя не боимся."))),
                    Attitude.Denial or Attitude.Nihilism => Pick(x, "re", V(("d1", "Это… невозможно. Наши книги говорили, что небо пусто."), ("d2", "Мы видим свет, но свет — ещё не бог. Или бог?"), ("d3", "Если ты настоящий, то половину нашего знания придётся сжечь."))),
                    _ => Pick(x, "re", V(("i1", "Так это правда! Старики не врали."), ("i2", "Мы думали, тебя нет. Мы ошибались."), ("i3", "Ты есть. Нам нужно время, чтобы это понять."))),
                };
            case DialogueAction.Bless:
                return x.Att switch
                {
                    Attitude.Love => Pick(x, "re", V(("l1", again ? "Опять дар! Ты щедр, как весенний разлив." : "Добрый дар! Поля зашумят, амбары наполнятся."), ("l2", "Твоя милость — как дождь в засуху: мы будем помнить её детям детей."), ("l3", "Благодарим! Сегодня каждый очаг зажжёт для тебя огонь."))),
                    Attitude.Fear => Pick(x, "re", V(("f1", "Милость? За что? Мы не понимаем, чего ты ждёшь."), ("f2", "Ты даришь — а вдруг потом отнимешь вдвое?"), ("f3", "Мы принимаем твой дар с дрожью."))),
                    Attitude.Bargain => Pick(x, "re", V(("b1", again ? "Опять дар — значит, ты чего-то от нас хочешь. Говори прямо." : "Это плата за жертвы прошлой весны? Тогда мы в расчёте."), ("b2", "Дар принят. Запишем его в наш счёт с тобой."), ("b3", "Хорошо. Теперь мы тебе должны — или ты нам был должен?"))),
                    Attitude.Rebellion => Pick(x, "re", V(("r1", "Подачкой нас не купить."), ("r2", "Мы съедим твой хлеб и не скажем спасибо."), ("r3", again ? "Ты снова швыряешь нам кость. Мы не собаки." : "Милость тирана — та же цепь, только золотая."))),
                    Attitude.Denial or Attitude.Nihilism => Pick(x, "re", V(("d1", "Урожай хорош, но при чём тут ты?"), ("d2", "Почва, вода и труд. Мы не видим в этом руки неба."), ("d3", "Если это твой дар, его можно измерить. Мы измерим."))),
                    _ => Pick(x, "re", V(("i1", "Хороший год. Может, и правда твоя заслуга."), ("i2", "Спасибо, если это ты."))),
                };
            case DialogueAction.Demand:
                bool obeys = Dialogue.Obeys(x.W, x.G);
                return obeys
                    ? x.Att switch
                    {
                        Attitude.Love => Pick(x, "re", V(("l1", "Бери! Лучшее зерно, лучший скот — всё тебе."), ("l2", "Мы несём дары с песнями: для тебя ничего не жаль."))),
                        Attitude.Fear => Pick(x, "re", V(("f1", "Возьми, только не гневайся! Мы отдадим больше, чем просишь."), ("f2", again ? "Опять дань… Мы отдадим, но амбары уже пусты." : "Отдаём, отдаём! Только отведи беду."))),
                        _ => Pick(x, "re", V(("b1", "Дань будет. Но и ты помни, что мы платим."), ("b2", again ? $"Это уже {x.Repeats + 1}-я дань. Мы считаем, {c}." : "Хорошо. Мера за меру: мы даём — ты хранишь."), ("b3", "Сделка. Скот к алтарю — дождь к полям."))),
                    }
                    : Pick(x, "re", V(("r1", "Нет. Свободные не платят дани."), ("r2", "Ни зерна, ни ягнёнка. Попробуй взять сам."), ("r3", again ? "Ты снова требуешь? Ответ тот же: нет." : "Дань? Мы платили её веками — хватит."), ("r4", "Мы отдаём хлеб своим детям, а не небу.")));
            case DialogueAction.Threaten:
                return x.Att == Attitude.Rebellion || x.Voice == Voice.Rebel
                    ? Pick(x, "re", V(("r1", "Грози. Наши отцы тоже боялись — и где они?"), ("r2", "Угроза — оружие слабого. Мы это знаем."), ("r3", "Обрушь небо, если можешь. Мы встретим его стоя.")))
                    : x.Att is Attitude.Denial or Attitude.Nihilism
                        ? Pick(x, "re", V(("d1", "Темнеет небо — бывает. Мы запишем, в какой день и час."), ("d2", "Нас пугали грозой и раньше. Гроза прошла.")))
                        : Pick(x, "re", V(("f1", "Пощади! Мы удвоим обряды!"), ("f2", "Чем мы прогневали тебя? Скажи — мы исправим."), ("f3", again ? "Опять гнев! Чем ещё нам заслужить твою милость?" : "Мы трепещем, владыка неба.")));
            case DialogueAction.Teach:
                string lesson = Dialogue.MessageRu(x.Message);
                return x.Att is Attitude.Rebellion or Attitude.Denial or Attitude.Nihilism
                    ? Pick(x, "re", V(("r1", $"«{Russian.Capitalize(lesson)}»? Мы сами решим, чему учиться."), ("r2", $"Ты учишь нас «{lesson}»? У тебя самого с этим как?")))
                    : Pick(x, "re", V(("l1", $"Твоё слово о {LessonPrep(x.Message)} мы вырежем на камне."), ("l2", $"«{Russian.Capitalize(lesson)}» — запомним и передадим детям."), ("l3", $"Жрецы уже спорят, как понимать твоё слово о {LessonPrep(x.Message)}.")));
            case DialogueAction.AnswerPrayer:
                return Pick(x, "re", V(("a1", "Ты услышал! Мы молились — и ты услышал!"), ("a2", "Сбылось! Старики плачут: они дождались."), ("a3", x.UnansweredYear >= 0 && x.UnansweredYear < x.W.Year - 3 ? "Мы ждали долго — но ты пришёл. Мы не забудем." : "Ты ответил в срок. Теперь мы знаем: небо слышит.")));
        }
        return "";
    }

    private static string LessonPrep(string m) => m switch
    {
        "peace" => "мире", "war" => "войне", "love" => "любви", "wrath" => "гневе и покаянии", "knowledge" => "знании",
        "obedience" => "покорности", "freedom" => "свободе", _ => m,
    };

    /// <summary>Первая реплика, когда Создатель обращает взор (без действия).</summary>
    private static string Opening(Ctx x)
    {
        switch (x.Need)
        {
            case Need.Hunger:
                return Pick(x, "op", V(("h1", x.G.FamineYears > 1 ? $"Мы голодаем уже {Russian.Years(x.G.FamineYears)}." : "Нам нечего есть."), ("h2", "Колосья пусты, и дети пухнут от голода."), ("h3", "Мы съели семенное зерно. Весной нечего будет сеять.")));
            case Need.Plague:
                return Pick(x, "op", V(("p1", "По нашим селениям идёт мор."), ("p2", "Мёртвых больше, чем живых, кто может их хоронить."), ("p3", "Болезнь пришла с торговыми лодками и не уходит.")));
            case Need.War:
                return Pick(x, "op", V(("w1", "Нас разбили враги."), ("w2", x.Enemy.Length > 0 ? $"Держава {x.Enemy} топчет наши поля." : "Чужие воины жгут наши поля."), ("w3", "Наши юноши лежат в поле, и вороны кружат над ними.")));
        }
        return x.Att switch
        {
            Attitude.Love => Pick(x, "op", V(("l1", "Велик ты, добр и светел!"), ("l2", "Мы поём тебе и утром, и вечером."), ("l3", x.City.Length > 0 ? $"Весь город {x.City} вышел на площади, чтобы встретить твой взгляд." : "Весь народ вышел из домов, чтобы встретить твой взгляд."))),
            Attitude.Fear => Pick(x, "op", V(("f1", "Удержи гневную руку!"), ("f2", "Мы помним твой гнев и не смеем поднять глаз."), ("f3", "Мы ничего не просим — только не гневайся."))),
            Attitude.Bargain => Pick(x, "op", V(("b1", "Мы дали дар — дай и ты!"), ("b2", "Алтари полны. Ждём ответа."), ("b3", "Обычай соблюдён: жертва принесена. Твоя очередь."))),
            Attitude.Rebellion => Pick(x, "op", V(("r1", "Злой владыка! Мы — свободный народ!"), ("r2", "Нам не о чем говорить с тираном."), ("r3", "Ты пришёл посмотреть на тех, кто тебя отверг?"))),
            Attitude.Denial => Pick(x, "op", V(("d1", "Небо молчит. Ясен разум."), ("d2", "Мы не верим в тебя — но выслушаем, раз уж ты говоришь."), ("d3", "Странно. Наши мудрецы учили, что говорить с небом бессмысленно."))),
            Attitude.Nihilism => Pick(x, "op", V(("n1", "Конец. Тьма. Зачем ты здесь?"), ("n2", "Ничто не имеет смысла — и ты тоже."))),
            _ => Pick(x, "op", V(("i1", "Скрытый бог? Мы почти забыли о тебе."), ("i2", "Кто-то зовёт с неба? Странно."), ("i3", "Мы заняты. Говори, если есть что сказать."))),
        };
    }

    // ---------- 3. Довод из памяти ----------

    private static string Memory(Ctx x)
    {
        var opts = new List<(string, string)>();
        if (x.UnansweredYear >= 0 && x.Act != DialogueAction.AnswerPrayer)
            opts.Add(("unans", x.Manner switch
            {
                Manner.Defiant => $"В {x.UnansweredYear} году мы молили тебя — и ты молчал. Ты всегда молчишь, когда нужен.",
                Manner.Rational => $"Мы молились в {x.UnansweredYear} году. Ответа не было. Это тоже ответ.",
                Manner.Business => $"Мы молились в {x.UnansweredYear} году и до сих пор ждём. Долг за тобой.",
                _ => $"В {x.UnansweredYear} году мы молили тебя, а ты молчал.",
            }));
        if (x.AnsweredYear >= 0) opts.Add(("ans", $"Мы помним: в {x.AnsweredYear} году ты услышал нас."));
        int v = 0;
        foreach (var (key, text) in x.Memories)
        {
            string T = Russian.Capitalize(text);
            string[] forms = x.Manner switch
            {
                Manner.Defiant => new[] { $"Мы помним: {text}. Мы ничего не забыли.", $"{T} — думаешь, мы забыли?", $"Не забывай и ты: {text}." },
                Manner.Rational => new[] { $"Наши записи говорят: {text}. Мы проверяли.", $"По нашим летописям, {text}.", $"Вот факт: {text}.", $"{T}, — так записано у нас." },
                Manner.Business => new[] { $"Запиши и ты: {text}.", $"В нашем счёте значится: {text}.", $"Напомню: {text}." },
                Manner.Poetic => new[] { $"Старики поют, что {text}.", $"Есть песня о том, как {text}.", $"Матери рассказывают детям: {text}." },
                Manner.Terse => new[] { $"{T}.", $"Помним: {text}." },
                _ => new[] { $"Мы помним: {text}.", $"Так было: {text}.", $"Наши отцы видели: {text}." },
            };
            opts.Add((key, forms[(v++ + x.Salt) % forms.Length]));
        }
        if (opts.Count == 0) return "";
        return Pick(x, "mem", opts);
    }

    // ---------- 4. Положение народа ----------

    private static string Situation(Ctx x)
    {
        var w = x.W;
        var g = x.G;
        var opts = new List<(string, string)>();
        if (x.Need == Need.Hunger) opts.Add(("hunger", x.Manner == Manner.Poetic ? "Земля наша треснула, как старый горшок, и колос не держит зерна." : "Поля не родят, и амбары пусты."));
        if (x.Need == Need.Plague) opts.Add(("plague", "Мор ходит от дома к дому, и костры у святилищ не гаснут."));
        if (x.Need == Need.War || x.Enemy.Length > 0) opts.Add(("war", x.Enemy.Length > 0 ? $"Держава {x.Enemy} помнит нас врагами — и мы её тоже." : "Враги ходят по нашим полям."));
        if (x.City.Length > 0) opts.Add(("city", g.EraIdx >= 6 ? $"Наш город {x.City} светится ночью так, что звёзд не видно." : $"Наш город {x.City} растёт: что ни год — новая улица."));
        if (g.EraIdx >= 5) opts.Add(("machines", g.EraIdx >= 7 ? "Наши машины думают быстрее нас, и мы уже не знаем, кто кого учит." : "Наши машины дымят день и ночь, а реки стали чёрными."));
        if (w.Warming > 0.8f) opts.Add(("warm", $"Лето стало длиннее, а море подступает к порогам — на {w.Warming:0.0} градуса теплее, говорят учёные."));
        if (w.LastNuclearWar > 0 && w.Year - w.LastNuclearWar < 150) opts.Add(("nuke", "Пепел той войны ещё не осел у нас в памяти."));
        if (g.TradePartners.Count >= 2 && x.Neighbor.Length > 0) opts.Add(("trade", $"Лодки ходят от нас к народу {x.Neighbor}, и в домах есть чужие ткани."));
        if (g.Has(Tech.Writing)) opts.Add(("writing", "Наши писцы записывают всё, что ты делаешь."));
        // Край и быт — всегда есть о чём сказать.
        var p = w.Planet;
        if (p.ECoast[g.Cell]) opts.Add(("coast", "Море кормит нас рыбой, но каждый год забирает лодки."));
        if (p.EElev[g.Cell] > 1.4f) opts.Add(("mount", "Наши козы ходят по самым кручам, а мы — за ними."));
        if (p.ERiver[g.Cell] > 0.15f) opts.Add(("river", "Река разлилась вовремя, и ил лёг на поля."));
        if (p.EPrecip[g.Cell] < 350f) opts.Add(("dry", "Пески подходят к колодцам ближе с каждым годом."));
        if (p.ETemp[g.Cell] < 3f) opts.Add(("cold", "Зима у нас длинная, и снег лежит почти до лета."));
        if (g.Subsistence == Subsistence.Herding) opts.Add(("herd", "Стада наши тучны, но пастбища всё дальше."));
        if (g.Subsistence == Subsistence.Foraging) opts.Add(("forage", "Мы идём за зверем, куда идёт он, и нигде не живём долго."));
        if (g.Has(Tech.Copper)) opts.Add(("metal", "Наши кузнецы куют медь, и огонь в горнах не гаснет."));
        opts.Add(("calm", x.Manner == Manner.Poetic ? "У нас тихо: стада на холмах, дым над крышами." : "Живём, как жили отцы."));
        opts.Add(("kids", $"Детей у нас нынче много: {Russian.Number(g.Pop)} душ, и всем нужен хлеб."));
        return Pick(x, "sit", opts);
    }

    // ---------- 5. Просьба или требование ----------

    private static string Request(Ctx x)
    {
        var opts = new List<(string, string)>();
        bool proud = x.Manner is Manner.Defiant;
        // Открытая просьба — прежде всего (god-loop.md §2).
        if (x.Petition is { } pt && !proud)
        {
            string target = pt.Target < 0 ? "" : pt.Kind == PetitionKind.Punish ? $" — державе {x.W.Polities[pt.Target].Name.Native}" : $" над державой {x.W.Polities[pt.Target].Name.Native}";
            return Pick(x, "req", V(("pet1", $"Мы просили у тебя {PetGen(pt.Kind)}{target} — и ждём."),
                ("pet2", $"Мы всё ещё ждём {PetGen(pt.Kind)}{target}. Срок — до {pt.Deadline} года."),
                ("pet3", $"Дай нам {PetAcc(pt.Kind)}{target}!")));
        }
        if (x.OpenOath is { } oo && x.Manner is not Manner.Terse)
            return Pick(x, "req", V(("oath1", $"Ты обещал: «{Simulation.OathTitle(oo.Kind)}». Мы помним и ждём."), ("oath2", $"Твоё слово «{Simulation.OathTitle(oo.Kind)}» — в наших летописях. Не забудь его.")));
        // Учёные, догадавшиеся о симуляции, говорят о главном (god-loop.md §6).
        if (x.G.Suspicion >= 0.6f)
            return Pick(x, "req", V(("sim1", "Мы знаем, что мир сделан. Скажи нам — зачем?"), ("sim2", "Ответь нам: кто ты и есть ли у нас свобода?"),
                ("sim3", x.G.Attitude == Attitude.Rebellion ? "Отпусти нас. Мы больше не твои." : "Мы послали тебе знак. Ответь на него.")));
        if (proud) return Pick(x, "req", V(("p1", "Нам ничего от тебя не нужно. Оставь нас."), ("p2", "Единственное, о чём мы просим, — не вмешивайся."), ("p3", "Требуем одного: отойди от нашего неба.")));
        switch (x.Need)
        {
            case Need.Hunger: opts.Add(("rain", "Дай дождь, дай хлеб!")); opts.Add(("rain2", "Пошли воду на поля, пока не поздно.")); break;
            case Need.Plague: opts.Add(("heal", "Отведи мор от наших детей!")); opts.Add(("heal2", "Останови болезнь — мы отдадим что угодно.")); break;
            case Need.War:
                opts.Add(("victory", "Дай нам победу!"));
                if (x.Enemy.Length > 0) opts.Add(("punish", $"Накажи державу {x.Enemy}! Пусть её поля засохнут, как наши."));
                opts.Add(("peace", "Или дай мир — мы устали хоронить."));
                break;
        }
        if (x.Manner == Manner.Rational)
        {
            float ocean = x.W.Params.OceanFraction * 100f;
            int oc = (int)MathF.Round(ocean);
            opts.Add(("q1", $"Ответь нашим учёным: почему океан занимает ровно {oc} {Russian.Plural(oc, "часть", "части", "частей")} из ста?"));
            opts.Add(("q2", "Ответь: зачем ты создал горы, если по ним нельзя пахать?"));
            opts.Add(("q3", "Скажи, если знаешь: есть ли другие миры, кроме нашего?"));
        }
        if (x.Manner == Manner.Business) { opts.Add(("deal", "Дай нам урожай на три года вперёд — и получишь втрое.")); opts.Add(("deal2", "Назови цену хорошей погоды. Мы торгуемся честно.")); }
        if (x.Att == Attitude.Love)
        {
            opts.Add(("stay", "Не покидай нас."));
            opts.Add(("sign", "Подай нам знак, что ты рядом."));
            opts.Add(("harvest", "Пошли нам доброго урожая — и мы разделим его с тобой."));
            opts.Add(("life", "Дай нашим детям долгую жизнь."));
            if (x.City.Length > 0) opts.Add(("keep", $"Сохрани наш город {x.City} от огня и воды."));
            opts.Add(("teach", "Научи нас тому, чего мы ещё не знаем."));
        }
        if (x.Att == Attitude.Fear) { opts.Add(("spare", "Пощади наших детей.")); opts.Add(("calm", "Скажи, чем умилостивить тебя, — и мы сделаем.")); opts.Add(("away", "Отврати лицо своё от наших грехов.")); }
        if (x.Att == Attitude.Indifference) { opts.Add(("leave", "Не мешай — и мы не помешаем тебе.")); opts.Add(("rain", "Если можешь, пошли дождь в срок. Больше ничего.")); }
        if (opts.Count == 0) opts.Add(("none", "Будь к нам добр."));
        return Pick(x, "req", opts);
    }

    private static string PetGen(PetitionKind k) => k switch
    {
        PetitionKind.Victory => "победы", PetitionKind.Punish => "кары обидчику", PetitionKind.Healing => "исцеления от мора",
        PetitionKind.Sign => "знака, что ты рядом", _ => "ответа нашим мудрецам",
    };

    private static string PetAcc(PetitionKind k) => k switch
    {
        PetitionKind.Victory => "победу", PetitionKind.Punish => "кару обидчику", PetitionKind.Healing => "исцеление от мора",
        PetitionKind.Sign => "знак, что ты рядом", _ => "ответ нашим мудрецам",
    };

    // ---------- 6. Обещание или угроза ----------

    private static string Promise(Ctx x)
    {
        bool late = x.G.EraIdx >= 7 || x.G.Has(Tech.Rockets);
        return x.Att switch
        {
            Attitude.Love => Pick(x, "pro", V(("l1", x.City.Length > 0 ? $"Мы построим тебе храм в {Russian.Decline(x.City, Russian.Case.Prep)} — выше всех крыш." : "Мы построим тебе храм выше всех крыш."), ("l2", "Мы будем верны тебе, пока горит последний очаг."), ("l3", "Наши дети будут петь тебе, как пели мы."))),
            Attitude.Fear => Pick(x, "pro", V(("f1", "Мы удвоим жертвы."), ("f2", "Мы накажем всякого, кто тебя прогневает."))),
            Attitude.Bargain => Pick(x, "pro", V(("b1", "Сто голов скота к осеннему празднику — если будет урожай."), ("b2", "Ты — нам, мы — тебе. Как всегда."), ("b3", "Мы заплатим, но и счёт будем вести."))),
            Attitude.Rebellion => Pick(x, "pro", V(("r1", late ? "Придёт день — наши ракеты долетят и до тебя." : "Придёт день, и мы поднимемся к твоему небу."), ("r2", "Мы не склонимся. Ни мы, ни наши дети."), ("r3", "Каждый твой удар делает нас сильнее."), ("r4", late ? "Наши телескопы уже ищут тебя. И найдут." : "Мы разобьём твои алтари, если понадобится."))),
            Attitude.Denial or Attitude.Nihilism => Pick(x, "pro", V(("d1", "Мы проверим каждое твоё слово."), ("d2", "Мы опишем этот разговор и найдём ему объяснение."))),
            _ => Pick(x, "pro", V(("i1", "Посмотрим, что из этого выйдет."), ("i2", "Если сдержишь слово — может, и мы вспомним о тебе."))),
        };
    }

    // ---------- На их языке ----------

    private static string W(Lang L, Concept c) => L.Cyr(c, false);

    /// <summary>Короткая формула на их языке (2–4 слова) — по сути ответа.</summary>
    private static (string native, string gloss) NativePhrase(Ctx x)
    {
        var L = x.L;
        Concept[] cs = x.Act switch
        {
            null => x.Need switch
            {
                Need.Hunger => new[] { Concept.Give, Concept.Rain, Concept.Give, Concept.Bread },
                Need.Plague => new[] { Concept.Blood, Concept.Death, Concept.Child },
                Need.War => new[] { Concept.Give, Concept.Victory },
                _ => x.Att switch
                {
                    Attitude.Love => new[] { Concept.Great, Concept.Good, Concept.Light },
                    Attitude.Fear => new[] { Concept.Hold, Concept.Angry, Concept.Hand },
                    Attitude.Bargain => new[] { Concept.Gift, Concept.Give },
                    Attitude.Rebellion => new[] { Concept.Bad, Concept.Chief, Concept.Free, Concept.People },
                    Attitude.Denial => new[] { Concept.Sky, Concept.Silent, Concept.Clear, Concept.Mind },
                    Attitude.Nihilism => new[] { Concept.End, Concept.Dark },
                    _ => new[] { Concept.Hidden, Concept.God },
                },
            },
            DialogueAction.Appear => x.Att == Attitude.Rebellion ? new[] { Concept.Free, Concept.People } : x.Att == Attitude.Fear ? new[] { Concept.Hold, Concept.Hand } : new[] { Concept.See, Concept.Light },
            DialogueAction.Bless => x.Att == Attitude.Rebellion ? new[] { Concept.Bad, Concept.Gift } : new[] { Concept.Good, Concept.Gift },
            DialogueAction.Demand => Dialogue.Obeys(x.W, x.G) ? new[] { Concept.Give, Concept.Gift } : new[] { Concept.Free, Concept.People },
            DialogueAction.Threaten => x.Att == Attitude.Rebellion ? new[] { Concept.Bad, Concept.Chief } : new[] { Concept.Hold, Concept.Angry, Concept.Hand },
            DialogueAction.Teach => new[] { Concept.Word, Concept.Heart },
            DialogueAction.AnswerPrayer => new[] { Concept.Voice, Concept.Hear },
            _ => x.Att == Attitude.Love ? new[] { Concept.Good, Concept.Give } : new[] { Concept.Give, Concept.Hold },
        };
        var words = new List<string>();
        foreach (var c in cs) words.Add(W(L, c));
        string s = string.Join(" ", words);
        return (char.ToUpperInvariant(s[0]) + s[1..] + "!", string.Join(" ", cs.Select(c => Concepts.Russian[(int)c])));
    }

    private static readonly (Concept[] words, string gloss)[] Proverbs =
    {
        (new[] { Concept.Water, Concept.Give, Concept.Sky, Concept.Hold }, "Вода даёт, небо держит"),
        (new[] { Concept.Old, Concept.Tree, Concept.Strong, Concept.Earth }, "Старое дерево — крепкая земля"),
        (new[] { Concept.Fire, Concept.Hot, Concept.Word, Concept.Cold }, "Огонь горяч, слово холодно"),
        (new[] { Concept.Bread, Concept.Day, Concept.Rain, Concept.Night }, "Хлеб днём, дождь ночью"),
        (new[] { Concept.Small, Concept.River, Concept.Great, Concept.Sea }, "Малая река — великое море"),
        (new[] { Concept.Silent, Concept.Sky, Concept.Hear }, "Молчащее небо слышит"),
        (new[] { Concept.Gift, Concept.Give, Concept.Gift, Concept.Hold }, "Дар даёшь — дар держишь"),
        (new[] { Concept.Stone, Concept.Fall, Concept.Path, Concept.Clear }, "Камень упал — путь ясен"),
        (new[] { Concept.Father, Concept.Path, Concept.Child, Concept.Path }, "Путь отца — путь сына"),
        (new[] { Concept.Star, Concept.See, Concept.Night, Concept.Hidden }, "Звезда видит то, что скрыла ночь"),
        (new[] { Concept.Free, Concept.Hand, Concept.Strong, Concept.Heart }, "Свободная рука — сильное сердце"),
        (new[] { Concept.Mind, Concept.Light, Concept.Dark, Concept.End }, "Разум светит — тьме конец"),
    };

    /// <summary>Формула завершения (поговорка народа, lore.md §4): выбор — по культуре и вере.</summary>
    private static (string native, string gloss) Proverb(Ctx x)
    {
        int k = x.Att switch
        {
            Attitude.Rebellion => (x.Salt & 1) == 0 ? 10 : 7,
            Attitude.Denial or Attitude.Nihilism => 11,
            Attitude.Bargain => (x.Salt & 1) == 0 ? 6 : 3,
            Attitude.Fear => (x.Salt & 1) == 0 ? 5 : 2,
            _ => (int)((uint)(x.G.Id * 7 + x.G.LanguageId) % 10u),
        };
        var (words, gloss) = Proverbs[Math.Clamp(k, 0, Proverbs.Length - 1)];
        var parts = new List<string>();
        foreach (var c in words) parts.Add(W(x.L, c));
        string s = string.Join(" ", parts);
        return (char.ToUpperInvariant(s[0]) + s[1..] + ".", gloss + ".");
    }

    // ---------- Рассказчик ----------

    public static string VoiceTitle(Voice v) => v switch
    {
        Voice.Elder => "старейшина", Voice.Priest => "жрец", Voice.Ruler => "правитель", Voice.Sage => "мудрец",
        Voice.Prophet => "пророк", Voice.Rebel => "вожак мятежников", _ => "ребёнок",
    };

    public static string MannerTitle(Manner m) => m switch
    {
        Manner.Solemn => "торжественно", Manner.Business => "по-деловому", Manner.Poetic => "нараспев", Manner.Terse => "коротко",
        Manner.Defiant => "дерзко", _ => "рассудительно",
    };

    private static string Narration(Ctx x)
    {
        string who = $"Говорит {VoiceTitle(x.Voice)} {x.Speaker.Native} («{x.Speaker.Gloss}»), {MannerTitle(x.Manner)}.";
        string gesture = x.Voice switch
        {
            Voice.Priest => "Он поднимает чашу к небу.",
            Voice.Ruler => "Свита стоит за его спиной.",
            Voice.Sage => "Он держит в руках исписанные таблички.",
            Voice.Prophet => "Глаза его горят, толпа ловит каждое слово.",
            Voice.Rebel => "За ним — молчаливая толпа с оружием.",
            Voice.Child => "Взрослые не успели его остановить.",
            _ => "Старики садятся в круг у огня.",
        };
        string effect = x.Act switch
        {
            DialogueAction.Appear => x.Att == Attitude.Rebellion ? "Явление их только озлобит." : "О вашем явлении будут рассказывать века.",
            DialogueAction.Bless => "Земли народа пять лет будут родить вдвое.",
            DialogueAction.AnswerPrayer => "Молитва услышана. Такое не забывают: здесь вас назовут Внимающим.",
            DialogueAction.Demand => Dialogue.Obeys(x.W, x.G) ? "Народ несёт к алтарям зерно и скот. Вы получите энергию." : "Народ отказывается платить. Обида растёт.",
            DialogueAction.Threaten => x.Att == Attitude.Rebellion ? "Угроза лишь укрепила мятежников." : "Народ трепещет и умножает обряды.",
            DialogueAction.Teach => $"Ваше наставление ({Dialogue.MessageRu(x.Message)}) войдёт в учение их веры.",
            _ => "",
        };
        return $"{who} {gesture}{(effect.Length > 0 ? " " + effect : "")}";
    }

    private static float MoodDelta(Ctx x) => x.Act switch
    {
        DialogueAction.Appear => x.Att == Attitude.Rebellion ? -0.15f : 0.2f,
        DialogueAction.Bless => 0.2f,
        DialogueAction.AnswerPrayer => 0.35f,
        DialogueAction.Demand => Dialogue.Obeys(x.W, x.G) ? 0f : -0.2f,
        DialogueAction.Threaten => -0.15f,
        _ => 0f,
    };
}
