using System;
using Godot;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;

namespace Mirozdanie.Game;

/// <summary>
/// Окно разговора с народом (docs/design/viewer-2.md, §3), как встреча с племенем в Spore:
/// портрет, реплики на их языке с переводом, шкала настроения и действия Создателя.
/// Пока окно открыто, время стоит.
/// </summary>
public partial class Main
{
    private Control _dialogue = null!;
    private Portrait _dPortrait = null!;
    private Label _dTitle = null!, _dSub = null!;
    private RichTextLabel _dLog = null!;
    private ProgressBar _dMood = null!;
    private Button _dAnswer = null!;
    private OptionButton _dMessage = null!;
    private OptionButton _dOath = null!;
    private Button _dSwear = null!;
    private HBoxContainer _dContact = null!;
    private VBoxContainer _dContactBox = null!;
    private Label _dPromises = null!;
    private int _dGroup = -1;
    // Память разговоров с каждым народом (lore.md §12.3): прозвучавшие обороты не повторяются.
    private readonly System.Collections.Generic.Dictionary<int, Conversation> _conversations = new();
    private Conversation Conv(int gid) => _conversations.TryGetValue(gid, out var c) ? c : _conversations[gid] = new Conversation();

    private void BuildDialogue()
    {
        _dialogue = new Control { Visible = false };
        _dialogue.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_dialogue);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _dialogue.AddChild(dim);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", AtlasTheme.Box(new Color(AtlasTheme.Paper, 0.97f), AtlasTheme.Gold, 2, 14, 18, 18));
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -560; panel.OffsetRight = 560; panel.OffsetTop = -380; panel.OffsetBottom = 380;
        _dialogue.AddChild(panel);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 18);
        panel.AddChild(h);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        _dPortrait = new Portrait { CustomMinimumSize = new Vector2(300, 340) };
        left.AddChild(_dPortrait);
        _dTitle = AtlasTheme.Heading("", 24);
        _dTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(_dTitle);
        _dSub = AtlasTheme.Small("");
        left.AddChild(_dSub);
        left.AddChild(AtlasTheme.Small("Отношение к вам", AtlasTheme.Ink));
        _dMood = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 16) };
        left.AddChild(_dMood);
        var moodScale = new HBoxContainer();
        var enmity = AtlasTheme.Small("вражда");
        var love = AtlasTheme.Small("любовь");
        enmity.AutowrapMode = love.AutowrapMode = TextServer.AutowrapMode.Off;
        moodScale.AddChild(enmity);
        moodScale.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        moodScale.AddChild(love);
        left.AddChild(moodScale);
        h.AddChild(left);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 10);
        h.AddChild(right);
        var head = new HBoxContainer();
        head.AddChild(AtlasTheme.Heading("Разговор", 22));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "Уйти ✕" };
        close.Pressed += CloseDialogue;
        head.AddChild(close);
        right.AddChild(head);
        _dLog = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 230) };
        _dLog.AddThemeStyleboxOverride("normal", AtlasTheme.Box(AtlasTheme.PaperDeep, AtlasTheme.GoldDim, 1, 8, 0, 12));
        right.AddChild(_dLog);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        right.AddChild(grid);
        void Act(string title, string icon, string tip, Action a)
        {
            var b = new IconButton { Text = title, Icon = icon, IconSize = 22, CustomMinimumSize = new Vector2(0, 44), TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.ReserveIconSpace();
            b.Pressed += a;
            grid.AddChild(b);
        }
        Act("Спросить о себе", "chat", "Что они думают о вас и почему", () => DoDialogue(DialogueAction.Ask));
        Act("Явиться", Spellbook.Get(InterventionType.Appear).Icon, Spellbook.Get(InterventionType.Appear).Hint, () => DoDialogue(DialogueAction.Appear));
        Act("Благословить", Spellbook.Get(InterventionType.Bless).Icon, Spellbook.Get(InterventionType.Bless).Hint, () => DoDialogue(DialogueAction.Bless));
        Act("Потребовать дар", Spellbook.Get(InterventionType.Demand).Icon, Spellbook.Get(InterventionType.Demand).Hint, () => DoDialogue(DialogueAction.Demand));
        Act("Пригрозить", Spellbook.Get(InterventionType.Threaten).Icon, Spellbook.Get(InterventionType.Threaten).Hint, () => DoDialogue(DialogueAction.Threaten));
        Act("Наставить", Spellbook.Get(InterventionType.Teach).Icon, Spellbook.Get(InterventionType.Teach).Hint, () => DoDialogue(DialogueAction.Teach));
        _dMessage = new OptionButton { TooltipText = "Чему наставить" };
        foreach (var m in Intervention.Messages) _dMessage.AddItem("учение: " + Dialogue.MessageRu(m));
        grid.AddChild(_dMessage);
        _dAnswer = new Button { Text = "Ответить на молитву", CustomMinimumSize = new Vector2(0, 44) };
        _dAnswer.AddThemeColorOverride("font_color", AtlasTheme.Leaf);
        _dAnswer.Pressed += () => DoDialogue(DialogueAction.AnswerPrayer);
        grid.AddChild(_dAnswer);

        // Слово Создателя: обеты (god-loop.md §3).
        var oathRow = new HBoxContainer();
        oathRow.AddThemeConstantOverride("separation", 8);
        var oathLabel = AtlasTheme.Small("Обет:", AtlasTheme.Ink);
        oathLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        oathLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        oathRow.AddChild(oathLabel);
        _dOath = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 38), TooltipText = "Сдержанный обет рождает любовь и имя «Верный слову», нарушенный — «Обманщик»" };
        oathRow.AddChild(_dOath);
        _dSwear = new Button { Text = "Пообещать", CustomMinimumSize = new Vector2(0, 38) };
        _dSwear.Pressed += Swear;
        oathRow.AddChild(_dSwear);
        right.AddChild(oathRow);
        // Ответ на послание учёных (god-loop.md §6) — виден, когда народ догадался о вас.
        _dContactBox = new VBoxContainer { Visible = false };
        _dContactBox.AddChild(AtlasTheme.Small("Они знают, что мир сделан. Ваш ответ:", AtlasTheme.Gold));
        _dContact = new HBoxContainer();
        _dContact.AddThemeConstantOverride("separation", 8);
        _dContactBox.AddChild(_dContact);
        void Reply(string title, string msg, string tip)
        {
            var b = new Button { Text = title, TooltipText = tip, CustomMinimumSize = new Vector2(0, 36), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            b.Pressed += () => ReplyContact(msg, title);
            _dContact.AddChild(b);
        }
        Reply("Ответить", "answer", "Контакт: учёные и жрецы впервые говорят об одном");
        Reply("Отпустить на свободу", "free", "Их земля станет их собственной: щит без боя, ваши чудеса там ослабнут");
        Reply("Промолчать", "silence", "Догадка ослабнет: «ответа нет — нет и Создателя»");
        right.AddChild(_dContactBox);
        _dPromises = AtlasTheme.Small("");
        right.AddChild(_dPromises);
    }

    private readonly System.Collections.Generic.List<string> _oathKeys = new();

    private void FillOaths()
    {
        var w = _sim!.World;
        var g = w.Groups[_dGroup];
        _dOath.Clear();
        _oathKeys.Clear();
        void Add(string key, string title) { _oathKeys.Add(key); _dOath.AddItem(title); }
        Add("rain", "«Пошлю дождь» — 10 лет");
        int enemy = _sim.EnemyPolity(g);
        if (enemy >= 0) Add($"victory:{enemy}", $"«Дам победу над {w.Polities[enemy].Name.Native}» — 20 лет");
        Add("protect", "«Защищу вас» — 30 лет без поражений");
        Add("spare", "«Не трону вас» — 50 лет без гнева");
        Add("answer", "«Отвечу вашим мудрецам» — 10 лет");
    }

    private void Swear()
    {
        if (_sim == null || _dGroup < 0 || _dOath.Selected < 0) return;
        var w = _sim.World;
        string key = _oathKeys[_dOath.Selected];
        _dLog.AppendText($"[right][color=#8fc3ff][i]Вы обещаете: {_dOath.GetItemText(_dOath.Selected).Split('—')[0].Trim()}.[/i][/color][/right]\n");
        CastOnGroup(InterventionType.Oath, _dGroup, key);
        var g = w.Groups[_dGroup];
        AppendLine(Dialogue.Reply(w, g, DialogueAction.Ask, "", Conv(_dGroup)) with { Narration = "Ваше слово записано. Его будут помнить — и сверять с делами." });
        _sound.Play("bell", 0.1);
        RefreshDialogueHeader();
    }

    private void ReplyContact(string msg, string title)
    {
        if (_sim == null || _dGroup < 0) return;
        var w = _sim.World;
        _dLog.AppendText($"[right][color=#8fc3ff][i]{title}.[/i][/color][/right]\n");
        CastOnGroup(InterventionType.Reply, _dGroup, msg);
        var g = w.Groups[_dGroup];
        string text = msg switch
        {
            "free" => "Народ замирает. Потом — тишина, как после долгой грозы. Они свободны, и знают это.",
            "silence" => "Учёные ждут ответа. Ответа нет. «Значит, мы ошиблись», — пишут они.",
            _ => "Сигнал принят. В обсерваториях и храмах плачут одни и те же люди.",
        };
        _dLog.AppendText($"[i][color=#a9a28f]{text}[/color][/i]\n\n");
        _sound.Play(msg == "answer" ? "choir" : msg == "free" ? "chime" : "click", 0.1);
        RefreshDialogueHeader();
    }

    private void OpenDialogue(int gid)
    {
        if (_sim == null || gid < 0) return;
        var w = _sim.World;
        var g = w.Groups[gid];
        if (!g.Alive) return;
        _dGroup = gid;
        _selectedGroup = gid;
        _speed = 0;
        RefreshTop();
        _dLog.Text = "";
        _dialogue.Visible = true;
        _globe.Focus(g.Cell);
        _sound.Play("whoosh", 0);
        _dLog.AppendText($"[i][color=#a9a28f]Вы обращаете взор на народ {g.Name.Native}. Они зовут вас {Dialogue.CreatorName(w, g)} — «{Dialogue.CreatorGloss(w, g)}».[/color][/i]\n\n");
        AppendLine(Dialogue.Opening(w, g, Conv(gid)));
        RefreshDialogueHeader();
    }

    private void CloseDialogue()
    {
        _dialogue.Visible = false;
        _sound.Play("click");
        RefreshPeople(true);
    }

    private void RefreshDialogueHeader()
    {
        var w = _sim!.World;
        var g = w.Groups[_dGroup];
        _dPortrait.Show(w, g);
        _dTitle.Text = $"Народ {g.Name.Native}";
        string faith = g.ReligionId >= 0 ? $"вера {w.Religions[g.ReligionId].Name.Native}" : "без веры";
        _dSub.Text = $"«{g.Name.Gloss}» · {Russian.Number(g.Pop)} чел. · {faith} · {Mirozdanie.Core.Chronicle.ChronicleWriter.AttitudeName(g.Attitude)}";
        _dMood.Value = Dialogue.Mood(g);
        _dAnswer.Disabled = !Dialogue.HasOpenPrayer(w, g);
        FillOaths();
        bool asked = g.PolityId >= 0 && w.Polities[g.PolityId].FreedomAsked;
        _dContactBox.Visible = !g.Freed && (g.Suspicion >= 0.6f || asked);
        var lines = new System.Collections.Generic.List<string>();
        var pet = _sim.OpenPetition(g);
        if (pet != null) lines.Add($"Просят: {Mirozdanie.Core.Sim.Simulation.PetitionTitle(pet.Kind)}{(pet.Target >= 0 ? $" (держава {w.Polities[pet.Target].Name.Native})" : "")} — до {pet.Deadline} года.");
        foreach (var o in w.Oaths)
            if (o.Group == g.Id)
                lines.Add($"Ваш обет «{Mirozdanie.Core.Sim.Simulation.OathTitle(o.Kind)}» ({o.Year}): {(o.Status == 0 ? $"до {o.Deadline} года" : o.Status == 1 ? "сдержан" : o.Status == -1 ? "нарушен" : "снят")}.");
        if (g.Dependence > 0.3f) lines.Add($"Зависимость от чуда: {g.Dependence:P0} — сами ищут меньше.");
        if (g.Suspicion > 0.2f) lines.Add($"Догадка, что мир сделан: {g.Suspicion:P0}.");
        _dPromises.Text = string.Join("\n", lines);
        _dAnswer.TooltipText = _dAnswer.Disabled ? "Народ сейчас ни о чём не молит" : "Ответ в срок: любовь, эпитет «Внимающий», +10 силы";
    }

    private void AppendLine(DialogueLine line)
    {
        if (line.Speaker.Length > 0) _dLog.AppendText($"[font_size=14][color=#a9a28f]{line.Speaker}:[/color][/font_size]\n");
        _dLog.AppendText($"[font_size=20][color=#d9b45a]{line.Native}[/color][/font_size]\n");
        _dLog.AppendText($"[color=#ece3cc]{line.Translation}[/color]\n");
        _dLog.AppendText($"[i][color=#a9a28f]{line.Narration}[/color][/i]\n\n");
    }

    private void DoDialogue(DialogueAction a)
    {
        if (_sim == null || _dGroup < 0) return;
        var w = _sim.World;
        var g = w.Groups[_dGroup];
        if (!g.Alive) { CloseDialogue(); return; }
        string message = Intervention.Messages[Math.Max(0, _dMessage.Selected)];
        string you = a switch
        {
            DialogueAction.Ask => "Что вы думаете обо мне?",
            DialogueAction.Appear => "Вы являетесь им во всём сиянии.",
            DialogueAction.Bless => "Вы благословляете их землю.",
            DialogueAction.Demand => "Вы требуете дара.",
            DialogueAction.Threaten => "Небо темнеет: вы грозите им.",
            DialogueAction.Teach => $"Вы наставляете их: {Dialogue.MessageRu(message)}.",
            _ => "Вы отвечаете на их мольбу.",
        };
        _dLog.AppendText($"[right][color=#8fc3ff][i]{you}[/i][/color][/right]\n");
        // Ответ считается по состоянию до действия: народ реагирует на сам поступок.
        var reply = Dialogue.Reply(w, g, a, message, Conv(_dGroup));
        if (a == DialogueAction.AnswerPrayer) AnswerPrayer(_dGroup);
        else if (a != DialogueAction.Ask)
        {
            var t = a switch
            {
                DialogueAction.Appear => InterventionType.Appear, DialogueAction.Bless => InterventionType.Bless,
                DialogueAction.Demand => InterventionType.Demand, DialogueAction.Threaten => InterventionType.Threaten,
                _ => InterventionType.Teach,
            };
            CastOnGroup(t, _dGroup, t == InterventionType.Teach ? message : "");
        }
        else _sound.Play("click");
        AppendLine(reply);
        RefreshDialogueHeader();
    }
}
