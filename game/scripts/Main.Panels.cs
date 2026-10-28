using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

public partial class Main
{
    // ---------- Народ ----------

    private Portrait _portrait = null!;
    private Label _pName = null!, _pGloss = null!, _pEmpty = null!;
    private RichTextLabel _pInfo = null!;
    private ProgressBar _barMercy = null!, _barPresence = null!, _barGrievance = null!, _barReason = null!;
    private Control _pBody = null!;
    private Button _talk = null!;

    private Control BuildPeopleTab()
    {
        var scroll = new ScrollContainer { Name = "Народ", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(v);
        _pEmpty = AtlasTheme.Small("Кликните по народу на планете (светящиеся точки) или по строке летописи.", AtlasTheme.Ink);
        v.AddChild(_pEmpty);
        _pBody = new VBoxContainer();
        _pBody.AddThemeConstantOverride("separation", 8);
        v.AddChild(_pBody);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        _pBody.AddChild(head);
        _portrait = new Portrait { CustomMinimumSize = new Vector2(150, 170) };
        head.AddChild(_portrait);
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        head.AddChild(names);
        _pName = AtlasTheme.Heading("", 24);
        _pName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pGloss = AtlasTheme.Small("");
        names.AddChild(_pName);
        names.AddChild(_pGloss);
        _talk = new IconButton { Text = "Говорить", Icon = "chat", IconSize = 22, CustomMinimumSize = new Vector2(0, 42) };
        ((IconButton)_talk).ReserveIconSpace();
        _talk.Pressed += () => { if (_selectedGroup >= 0) OpenDialogue(_selectedGroup); };
        names.AddChild(_talk);
        var show = new Button { Text = "Показать на планете" };
        show.Pressed += () => { if (_selectedGroup >= 0) _globe.Focus(_sim!.World.Groups[_selectedGroup].Cell); };
        names.AddChild(show);
        _barMercy = Bar("Милость неба в их глазах", new Color("e8c55a"));
        _barPresence = Bar("Присутствие Создателя", new Color("8fc3ff"));
        _barGrievance = Bar("Обида", AtlasTheme.Blood);
        _barReason = Bar("Натурализм (объясняют природой)", new Color("9ab"));
        _pInfo = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
        _pBody.AddChild(_pInfo);
        _pBody.Visible = false;
        return scroll;
    }

    private ProgressBar Bar(string title, Color color)
    {
        _pBody.AddChild(AtlasTheme.Small(title));
        var b = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
        var fill = new StyleBoxFlat { BgColor = color };
        fill.SetCornerRadiusAll(4);
        b.AddThemeStyleboxOverride("fill", fill);
        _pBody.AddChild(b);
        return b;
    }

    private void Select(int gid, bool switchTab = true)
    {
        _selectedGroup = gid;
        if (switchTab) _tabs.CurrentTab = TabPeople;
        RefreshPeople(true);
        UpdateMarkers();
    }

    private void RefreshPeople(bool full)
    {
        if (_sim == null) return;
        var w = _sim.World;
        bool has = _selectedGroup >= 0 && _selectedGroup < w.Groups.Count;
        _pEmpty.Visible = !has;
        _pBody.Visible = has;
        if (!has) return;
        var g = w.Groups[_selectedGroup];
        if (full) _portrait.Show(w, g);
        _pName.Text = $"Народ {g.Name.Native}";
        _pGloss.Text = g.Alive ? $"«{g.Name.Gloss}» · {Russian.Number(g.Pop)} человек" : $"исчез в {g.DiedYear} году";
        _talk.Disabled = !g.Alive;
        _barMercy.Value = (g.B + 1) * 50;
        _barPresence.Value = g.P * 100;
        _barGrievance.Value = g.G * 100;
        _barReason.Value = g.N * 100;
        var L = w.LangOf(g);
        string rel = g.ReligionId >= 0 ? $"{w.Religions[g.ReligionId].Name.Native} («{w.Religions[g.ReligionId].Name.Gloss}»)" : "своей веры ещё нет";
        string cult = g.CultId >= 0 ? $"\n[color=#a9a28f]Набирает силу:[/color] {w.Religions[g.CultId].Name.Native} — {g.CultShare * 100:0} % людей" : "";
        string pol = g.PolityId >= 0 ? $"{w.Polities[g.PolityId].Name.Native}{(w.Polities[g.PolityId].Capital == g.Id ? " (столица)" : "")}" : "вольный народ";
        string need = Dialogue.NeedOf(w, g) switch
        {
            Need.Hunger => "[color=#ffb070]голод — ждут дождя и хлеба[/color]",
            Need.Plague => "[color=#ffb070]мор — ждут исцеления[/color]",
            Need.War => "[color=#ffb070]разбиты в войне — ждут помощи[/color]",
            _ => "нет",
        };
        string subs = g.Subsistence switch
        {
            Subsistence.Farming => "земледелие", Subsistence.Herding => "скотоводство", Subsistence.Fishing => "рыболовство", _ => "собирательство",
        };
        var ask = Dialogue.AskAbout(w, g);
        string attColor = "#" + Portrait.ToColor(Mirozdanie.Core.Output.WorldMaps.AttitudeColor(g.Attitude)).ToHtml(false);
        _pInfo.Text =
            $"[color=#a9a28f]Отношение к вам:[/color] [color={attColor}][b]{ChronicleWriter.AttitudeName(g.Attitude)}[/b][/color]\n" +
            $"[color=#a9a28f]Зовут вас:[/color] {Dialogue.CreatorName(w, g)} («{Dialogue.CreatorGloss(w, g)}»)\n" +
            $"[color=#a9a28f]Вера:[/color] {rel}{cult}\n" +
            $"[color=#a9a28f]Язык:[/color] {(L.FounderGroup >= 0 ? $"язык народа {w.Groups[L.FounderGroup].Name.Native}" : "праязык")} · «вода» — {L.Cyr(Mirozdanie.Core.Language.Concept.Water, false)}, «бог» — {L.Cyr(Mirozdanie.Core.Language.Concept.God, false)}\n" +
            $"[color=#a9a28f]Держава:[/color] {pol}\n" +
            $"[color=#a9a28f]Хозяйство:[/color] {subs}, знание {g.Knowledge * 100:0}/100\n" +
            $"[color=#a9a28f]Нужда:[/color] {need}\n\n" +
            $"[i]{ask.Narration}[/i]";
    }

    // ---------- Молитвы ----------

    private VBoxContainer _prayerList = null!;

    private Control BuildPrayersTab()
    {
        var scroll = new ScrollContainer { Name = "Молитвы", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _prayerList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _prayerList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_prayerList);
        return scroll;
    }

    private string _prayerKey = "";

    private void RefreshPrayers()
    {
        if (_sim == null) return;
        var w = _sim.World;
        var open = w.Groups.Where(g => Dialogue.HasOpenPrayer(w, g)).OrderByDescending(g => g.Pop).Take(20).ToList();
        string key = string.Join(",", open.Select(g => g.Id)) + "|" + w.Year / 5;
        if (key == _prayerKey && _prayerList.GetChildCount() > 0) return;
        _prayerKey = key;
        foreach (var c in _prayerList.GetChildren()) c.QueueFree();
        _prayerList.AddChild(AtlasTheme.Small("Ответ в срок рождает любовь, эпитет «Внимающий» и +10 силы сразу. Молчание — отчаяние и «Молчащего».", AtlasTheme.Ink));
        if (open.Count == 0) { _prayerList.AddChild(AtlasTheme.Small("Сейчас никто не молится.")); return; }
        foreach (var g in open)
        {
            var gg = g;
            var need = Dialogue.NeedOf(w, g);
            long since = need == Need.Plague ? g.LastPlague : g.LastPrayer;
            long left = Math.Max(0, (need == Need.Plague ? 5 : 10) - (w.Year - since));
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", AtlasTheme.Box(AtlasTheme.PaperLight, AtlasTheme.GoldDim, 1, 6, 0, 8));
            var v = new VBoxContainer();
            card.AddChild(v);
            var line = Dialogue.Opening(w, g);
            var title = new Label { Text = $"Народ {g.Name.Native} · {Russian.Number(g.Pop)} чел." };
            title.AddThemeColorOverride("font_color", AtlasTheme.Gold);
            v.AddChild(title);
            v.AddChild(AtlasTheme.Small($"{line.Native}\n{line.Translation}", AtlasTheme.Ink));
            v.AddChild(AtlasTheme.Small($"Осталось {Russian.Years(left)}."));
            var row = new HBoxContainer();
            var answer = new Button { Text = need == Need.Plague ? "Исцелить" : "Дождь" };
            answer.Pressed += () => AnswerPrayer(gg.Id);
            var bless = new Button { Text = "Благословить" };
            bless.Pressed += () => { CastOnGroup(InterventionType.Bless, gg.Id); RefreshPrayers(); };
            var talk = new Button { Text = "Говорить" };
            talk.Pressed += () => OpenDialogue(gg.Id);
            var show = new Button { Text = "Где?" };
            show.Pressed += () => { _globe.Focus(gg.Cell); Select(gg.Id, false); };
            row.AddChild(answer); row.AddChild(bless); row.AddChild(talk); row.AddChild(show);
            v.AddChild(row);
            _prayerList.AddChild(card);
        }
    }

    private void AnswerPrayer(int gid)
    {
        var w = _sim!.World;
        var g = w.Groups[gid];
        var t = Dialogue.NeedOf(w, g) switch { Need.Plague => InterventionType.Heal, Need.War => InterventionType.Peace, _ => InterventionType.Rain };
        CastOnGroup(t, gid);
        _prayerKey = "";
        RefreshPrayers();
    }

    private void CastOnGroup(InterventionType t, int gid, string message = "")
    {
        var w = _sim!.World;
        var spell = Spellbook.Get(t);
        var g = w.Groups[gid];
        string target = spell.Targeting == Targeting.Group ? $"group:{gid}" : $"cell:{g.Cell % w.Planet.EW},{g.Cell / w.Planet.EW}";
        _sim.ApplyNow(new Intervention { Type = t, Target = target, Message = message });
        SpellFx(spell, g.Cell);
        PumpEvents(false);
        RefreshTop();
        RefreshPeople(false);
    }

    // ---------- Предзнаменования ----------

    private VBoxContainer _omenList = null!;
    private bool _outcomeShown;
    private long _outcomeChecked = -1000;

    /// <summary>Окно «Итог мира» (god-loop.md §5): эпилог, итоги правления, главные события Летописи.</summary>
    private void OpenOutcome()
    {
        if (_sim == null) return;
        var w = _sim.World;
        var o = Mirozdanie.Core.Chronicle.Outcome.Evaluate(w);
        var v = Modal($"Итог мира {w.WorldName.Native}: {o.Title}", new Vector2(900, 640));
        var text = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectionEnabled = true };
        text.AddThemeStyleboxOverride("normal", AtlasTheme.Box(AtlasTheme.PaperDeep, AtlasTheme.GoldDim, 1, 8, 0, 20));
        var sb = new System.Text.StringBuilder($"[font_size=30][color=#d9b45a]{o.Title}[/color][/font_size]\n\n[font_size=19]{o.Epilogue}[/font_size]\n\n");
        foreach (var f in o.Facts) sb.Append($"• {f}\n");
        var writer = new Mirozdanie.Core.Chronicle.ChronicleWriter(w);
        var l = new Mirozdanie.Core.Chronicle.Letopis(w, writer);
        sb.Append("\n[b]Что запомнила Летопись:[/b]\n");
        foreach (var id in l.Top(0, w.Year, 10)) sb.Append($"{Mirozdanie.Core.Chronicle.Letopis.Icon(w.Events.Get(id).Type)} {w.Events.Get(id).Year} — {l.Text(id).Replace("*", "")}\n");
        if (o.Kind == Mirozdanie.Core.Chronicle.OutcomeKind.Ongoing) sb.Append("\n[i]Мир ещё в пути — итог можно посмотреть в любой момент.[/i]");
        else sb.Append("\n[i]История не кончается: можно продолжить и посмотреть, что будет дальше.[/i]");
        text.Text = sb.ToString();
        v.AddChild(text);
        _sound.Play(o.Kind is Mirozdanie.Core.Chronicle.OutcomeKind.Ascension or Mirozdanie.Core.Chronicle.OutcomeKind.Freedom ? "choir" : "gong", 0);
    }
    private readonly HashSet<string> _omensDone = new();

    private Control BuildOmensTab()
    {
        var scroll = new ScrollContainer { Name = "Знаки", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _omenList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _omenList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_omenList);
        return scroll;
    }

    private static readonly (string id, string title, string hint)[] Omens =
    {
        ("site", "Святая земля", "Пусть место вашего знака станет святыней (реликвия, чудо, молния)."),
        ("answer", "Внимающий", "Ответьте на молитву голодающих или больных."),
        ("appear", "Лицом к лицу", "Явитесь народу в разговоре или книгой сил."),
        ("love3", "Возлюбленный", "Добейтесь, чтобы три народа любили вас."),
        ("prophet", "Слово пророка", "Видение должно породить веру, которую примет целый народ."),
        ("polity", "Держава веры", "Пусть возникнет держава, чья столица любит вас."),
        ("anomaly", "Мир сделан", "Учёные народа заметят следы ваших вмешательств."),
        ("rebel", "Тиран", "Доведите народ до мятежа против неба (или избегайте этого)."),
        ("energy", "Полнота силы", "Накопите 1000 божественной энергии."),
        ("millennium", "Тысячелетие", "Проведите мир через тысячу лет истории."),
        ("word5", "Слово держу", "Сдержите пять обетов, данных народам."),
        ("peace100", "Сто лет мира", "Сто лет без единой битвы — когда у мира уже есть державы."),
        ("kardashev", "Кардашёв I", "Цивилизация подчинит себе всю энергию планеты."),
        ("deist", "Без чудес", "Мир дойдёт до Кардашёва I без единого вашего вмешательства после Пробуждения."),
        ("contact", "Контакт", "Ответьте учёным, которые догадались, что мир сделан."),
        ("freedom", "Их выбор", "Отпустите на свободу народ, который этого потребовал."),
    };

    private void CheckOmens()
    {
        if (_sim == null) return;
        var w = _sim.World;
        var log = w.Events.Log;
        bool Has(EventType t) { for (int i = log.Count - 1; i >= 0; i--) if (log[i].Type == t) return true; return false; }
        bool HasB(EventType t, int b) { for (int i = log.Count - 1; i >= 0; i--) if (log[i].Type == t && log[i].B == b) return true; return false; }
        long lastBattle = -1;
        for (int i = log.Count - 1; i >= 0; i--) if (log[i].Type == EventType.Battle) { lastBattle = log[i].Year; break; }
        int acts = w.AppliedLog.Count(a => a.Year >= 0);
        var state = new Dictionary<string, bool>
        {
            ["site"] = w.Sites.Count > 0,
            ["answer"] = Has(EventType.PrayerAnswered),
            ["appear"] = Has(EventType.CreatorAppeared),
            ["love3"] = w.Groups.Count(g => g.Alive && g.Attitude == Attitude.Love) >= 3,
            ["prophet"] = w.Religions.Any(r => r.Converted && !string.IsNullOrEmpty(r.Message)),
            ["polity"] = w.Polities.Any(p => p.Alive && w.Groups[p.Capital].Attitude == Attitude.Love),
            ["anomaly"] = w.FirstAnomalyGroup >= 0,
            ["rebel"] = w.Religions.Any(r => r.Rebel),
            ["energy"] = w.CreatorEnergy >= 1000 && !w.Sandbox,
            ["millennium"] = w.Year >= 1000,
            ["word5"] = w.Oaths.Count(o => o.Status == 1) >= 5,
            ["peace100"] = w.WorldEra >= 3 && w.Polities.Any(p => p.Alive) && w.Year - Math.Max(lastBattle, 0) >= 100,
            ["kardashev"] = w.Kardashev >= 0.999f,
            ["deist"] = w.Kardashev >= 0.999f && acts == 0,
            ["contact"] = HasB(EventType.ContactAnswered, 0),
            ["freedom"] = HasB(EventType.ContactAnswered, 1),
        };
        // Итог мира: открывается сам, когда мир достиг конца пути (god-loop.md §5).
        if (!_outcomeShown && w.Year - _outcomeChecked >= 10)
        {
            _outcomeChecked = w.Year;
            var o = Mirozdanie.Core.Chronicle.Outcome.Evaluate(w);
            if (o.Kind != Mirozdanie.Core.Chronicle.OutcomeKind.Ongoing) { _outcomeShown = true; _speed = 0; RefreshTop(); OpenOutcome(); }
        }
        bool changed = _omenList.GetChildCount() == 0;
        foreach (var (id, title, _) in Omens)
            if (state[id] && _omensDone.Add(id))
            {
                changed = true;
                Toast($"Предзнаменование исполнилось: «{title}».");
                _sound.Play("chime", 0.3);
            }
        if (!changed) return;
        foreach (var c in _omenList.GetChildren()) c.QueueFree();
        _omenList.AddChild(AtlasTheme.Small($"Исполнено {_omensDone.Count} из {Omens.Length}. Мягкие цели — мир не требует их, но запоминает.", AtlasTheme.Ink));
        foreach (var (id, title, hint) in Omens)
        {
            bool done = _omensDone.Contains(id);
            var row = new HBoxContainer();
            row.AddChild(new IconBadge { Icon = done ? "star" : "mist", Color = done ? AtlasTheme.Gold : AtlasTheme.GoldDim, CustomMinimumSize = new Vector2(24, 24) });
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var t = new Label { Text = title };
            t.AddThemeColorOverride("font_color", done ? AtlasTheme.Gold : AtlasTheme.Ink);
            v.AddChild(t);
            v.AddChild(AtlasTheme.Small(hint));
            row.AddChild(v);
            _omenList.AddChild(row);
        }
    }
}
