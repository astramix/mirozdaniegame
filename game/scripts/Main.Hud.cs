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
    private Control _hud = null!;
    private Control _loading = null!;
    private Label _loadingLabel = null!;
    private ProgressBar _loadingBar = null!;
    private Label _worldTitle = null!, _yearLabel = null!, _energyLabel = null!, _hover = null!;
    private readonly List<IconButton> _speedButtons = new();
    private CheckBox _pauseImportant = null!, _keepArmed = null!;
    private TabContainer _tabs = null!;
    private readonly List<(string id, Button button)> _layerButtons = new();
    private const int TabFeed = 0, TabPeople = 1, TabPrayers = 2, TabOmens = 3;
    private RichTextLabel _feed = null!;
    private CheckBox _autoCamBox = null!;
    private bool _autoCam;
    private double _lastCamMove, _lastUserInput;
    private readonly Queue<string> _feedQueue = new();
    private readonly List<string> _feedLines = new();
    private double _feedTimer;
    private Sphere _sphere = Sphere.Nature;
    private readonly List<Button> _sphereButtons = new();
    private HFlowContainer _spellRow = null!;
    private Label _spellHint = null!;
    private string _message = "peace";
    private PanelContainer _toast = null!;
    private Label _toastLabel = null!;
    private double _toastTime;

    private static PanelContainer Panel(Control parent)
    {
        var p = new PanelContainer();
        parent.AddChild(p);
        return p;
    }

    private void BuildHud()
    {
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_hud);

        // ---- Верхняя панель ----
        var top = Panel(_hud);
        _topBar = top;
        top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        top.OffsetLeft = 10; top.OffsetRight = -10; top.OffsetTop = 8;
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 14);
        top.AddChild(topRow);
        var titles = new VBoxContainer();
        titles.AddThemeConstantOverride("separation", -4);
        _worldTitle = AtlasTheme.Heading("Мир", 22);
        _yearLabel = AtlasTheme.Small("");
        _yearLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        titles.AddChild(_worldTitle);
        titles.AddChild(_yearLabel);
        topRow.AddChild(titles);
        topRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        string[] speedIcons = { "pause", "play1", "play2", "play3" };
        string[] speedTips = { "Пауза (пробел)", "1 год в секунду (1)", "4 года в секунду (2)", "15 лет в секунду (3)" };
        for (int i = 0; i < speedIcons.Length; i++)
        {
            int k = i;
            var b = new IconButton { Icon = speedIcons[i], IconSize = 22, CustomMinimumSize = new Vector2(44, 40), TooltipText = speedTips[i], ToggleMode = true, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            b.Pressed += () => SetSpeed(k);
            topRow.AddChild(b);
            _speedButtons.Add(b);
        }
        _pauseImportant = new CheckBox { Text = "автопауза", ButtonPressed = true, TooltipText = "Время остановится само: пророк, молитва, раскол, война, метеорит" };
        _pauseImportant.Toggled += on => _pauseOnImportant = on;
        topRow.AddChild(_pauseImportant);
        _autoCamBox = new CheckBox { Text = "камера", ButtonPressed = false, TooltipText = "Камера-летописец (C): сама поворачивает планету к важным событиям, пока вы не трогаете мышь" };
        _autoCamBox.Toggled += on => _autoCam = on;
        topRow.AddChild(_autoCamBox);
        topRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var energyBox = new HBoxContainer();
        energyBox.AddChild(new IconBadge { Icon = "star", CustomMinimumSize = new Vector2(30, 30) });
        _energyLabel = new Label();
        _energyLabel.AddThemeFontSizeOverride("font_size", 18);
        _energyLabel.AddThemeColorOverride("font_color", AtlasTheme.Gold);
        energyBox.AddChild(_energyLabel);
        topRow.AddChild(energyBox);
        foreach (var (text, icon, action) in new (string, string, Action)[]
                 {
                     ("Летопись", "scroll", () => OpenLetopis()), ("Итог", "star", OpenOutcome), ("Сохранить", "relic", SaveWorld), ("Загрузить", "globe", OpenLoadDialog),
                     ("Новый мир", "spark", () => SetMode(Mode.Creation)), ("Звук", "harp", OpenSound),
                 })
        {
            // Редкие кнопки — только значком, с подсказкой: верхняя строка не должна вылезать за экран.
            bool iconOnly = text is "Итог" or "Звук" or "Загрузить";
            var b = new IconButton { Text = iconOnly ? "" : text, TooltipText = text, Icon = icon, IconSize = 18, CustomMinimumSize = new Vector2(iconOnly ? 44 : 0, 40), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            b.AddThemeFontSizeOverride("font_size", 15);
            b.ReserveIconSpace();
            b.Pressed += () => { _sound.Play("click"); action(); };
            topRow.AddChild(b);
        }

        // ---- Слои (слева) ----
        var left = Panel(_hud);
        _leftPanel = left;
        left.Position = new Vector2(10, 82);
        var lv = new VBoxContainer();
        left.AddChild(lv);
        lv.AddChild(AtlasTheme.Heading("Слои", 17));
        foreach (var (id, title) in new[] { ("biomes", "Природа"), ("elevation", "Высоты"), ("temperature", "Тепло"), ("precipitation", "Осадки"), ("languages", "Языки"), ("religions", "Веры"), ("polities", "Державы"), ("attitude", "Отношение") })
        {
            var b = new Button { Text = title, ToggleMode = true, ButtonPressed = id == _layer, CustomMinimumSize = new Vector2(130, 34) };
            _layerButtons.Add((id, b));
            b.Pressed += () =>
            {
                _layer = id; _mapDirty = true; _sound.Play("click");
                foreach (var c in lv.GetChildren()) if (c is Button ob) ob.ButtonPressed = ob == b;
            };
            lv.AddChild(b);
        }
        lv.AddChild(new Control { CustomMinimumSize = new Vector2(150, 4) });
        lv.AddChild(AtlasTheme.Small("Маркеры народов:\nжёлтый — любовь\nбордовый — страх\nзелёный — сделка\nсиний — отрицание\nкрасный — мятеж\nмерцает — молится"));

        // ---- Правая панель с вкладками ----
        _tabs = new TabContainer();
        _hud.AddChild(_tabs);
        _tabs.AnchorLeft = 1; _tabs.AnchorRight = 1; _tabs.AnchorBottom = 1;
        _tabs.OffsetLeft = -440; _tabs.OffsetRight = -10; _tabs.OffsetTop = 82; _tabs.OffsetBottom = -10;
        _feed = new RichTextLabel { Name = "Летопись", BbcodeEnabled = true, MetaUnderlined = false, ScrollFollowing = true, SelectionEnabled = false };
        _feed.MetaClicked += meta => OnMeta(meta.AsString());
        _tabs.AddChild(_feed);
        _tabs.AddChild(BuildPeopleTab());
        _tabs.AddChild(BuildPrayersTab());
        _tabs.AddChild(BuildOmensTab());
        _tabs.TabChanged += _ => { _sound.Play("click"); if (_tabs.CurrentTab == TabPrayers) RefreshPrayers(); if (_tabs.CurrentTab == TabPeople) RefreshPeople(true); };

        // ---- Книга заклинаний (внизу) ----
        var book = Panel(_hud);
        _book = book;
        book.AnchorTop = 1; book.AnchorBottom = 1; book.AnchorLeft = 0; book.AnchorRight = 1;
        book.OffsetLeft = 160; book.OffsetRight = -460; book.OffsetBottom = -10; book.OffsetTop = -170;
        var bv = new VBoxContainer();
        book.AddChild(bv);
        var sphereRow = new HBoxContainer();
        sphereRow.AddChild(AtlasTheme.Heading("Силы Создателя", 18));
        sphereRow.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });
        foreach (Sphere sp in Enum.GetValues(typeof(Sphere)))
        {
            if (sp == Sphere.Word) continue; // слово — только в разговоре
            var b = new Button { Text = Spellbook.SphereTitle(sp), ToggleMode = true, ButtonPressed = sp == _sphere };
            b.Pressed += () => { _sphere = sp; _sound.Play("click"); RefreshSpellbook(); };
            sphereRow.AddChild(b);
            _sphereButtons.Add(b);
        }
        sphereRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _keepArmed = new CheckBox { Text = "держать силу", TooltipText = "Не снимать выбранную силу после наложения (то же, что Shift)" };
        sphereRow.AddChild(_keepArmed);
        bv.AddChild(sphereRow);
        _spellRow = new HFlowContainer();
        _spellRow.AddThemeConstantOverride("h_separation", 6);
        _spellRow.AddThemeConstantOverride("v_separation", 6);
        bv.AddChild(_spellRow);
        _earthBox = BuildEarthBox();
        bv.AddChild(_earthBox);
        _spellHint = AtlasTheme.Small("Выберите силу, затем кликните по планете. Левая кнопка с протяжкой — вращать, колесо — приблизить.");
        bv.AddChild(_spellHint);

        // ---- Подсказка под курсором и всплывающее сообщение ----
        _hover = new Label { Position = new Vector2(170, 0) };
        _hover.AnchorTop = 1; _hover.AnchorBottom = 1; _hover.OffsetTop = -198; _hover.OffsetLeft = 170;
        _hover.AddThemeColorOverride("font_color", AtlasTheme.Ink);
        _hover.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hover.AddThemeConstantOverride("outline_size", 6);
        _hud.AddChild(_hover);
        _toast = new PanelContainer { Visible = false };
        _toast.AddThemeStyleboxOverride("panel", AtlasTheme.Box(new Color(AtlasTheme.PaperDeep, 0.95f), AtlasTheme.Gold, 1, 8, 8, 12));
        _toast.AnchorLeft = 0.5f; _toast.AnchorRight = 0.5f; _toast.OffsetLeft = -330; _toast.OffsetRight = 230; _toast.OffsetTop = 84;
        _toastLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(540, 0) };
        _toast.AddChild(_toastLabel);
        _root.AddChild(_toast);

        // ---- Экран загрузки ----
        _loading = new CenterContainer { Visible = false };
        _loading.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_loading);
        var lp = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        _loading.AddChild(lp);
        var lpv = new VBoxContainer();
        lp.AddChild(lpv);
        lpv.AddChild(AtlasTheme.Heading("Сотворение", 24));
        _loadingLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        lpv.AddChild(_loadingLabel);
        _loadingBar = new ProgressBar { CustomMinimumSize = new Vector2(0, 22), ShowPercentage = false };
        lpv.AddChild(_loadingBar);
    }

    private void TickToast(double delta)
    {
        if (!_toast.Visible) return;
        _toastTime -= delta;
        if (_toastTime <= 0) _toast.Visible = false;
    }

    private void Toast(string text, double seconds = 5)
    {
        if (_mode != Mode.Game) return;
        if (_mode == Mode.Creation) return;
        _toastLabel.Text = text;
        _toast.Visible = true;
        _toastTime = seconds;
    }

    private void RefreshTop()
    {
        if (_sim == null) return;
        var w = _sim.World;
        _worldTitle.Text = $"Мир {w.WorldName.Native}";
        int alive = w.Groups.Count(g => g.Alive);
        int kept = w.Oaths.Count(o => o.Status == 1), broken = w.Oaths.Count(o => o.Status == -1);
        string style = w.Style == Mirozdanie.Core.Culture.CreatorStyle.Unknown ? "" : $" · вас зовут {Mirozdanie.Core.Sim.Simulation.StyleTitleIns(w.Style)}";
        string oaths = kept + broken > 0 ? $" · обетов {kept}✓ {broken}✗" : "";
        _yearLabel.Text = $"«{w.WorldName.Gloss}» · год {w.Year} после Пробуждения\nнародов {alive} · держав {w.Polities.Count(p => p.Alive)} · Кардашёв {Math.Max(0, w.Kardashev):0.00}{style}{oaths}";
        _energyLabel.Text = w.Sandbox ? "∞" : $"{w.CreatorEnergy:0} ({(w.EnergyIncome >= 0 ? "+" : "")}{w.EnergyIncome:0.0}/год)";
        _energyLabel.TooltipText = w.Sandbox ? "Песочница: силы бесконечны" : "Сила Создателя: копится от веры, тратится на заклинания";
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].ButtonPressed = i == _speed;
    }

    // ---------- Книга заклинаний ----------

    private void RefreshSpellbook()
    {
        for (int i = 0; i < _sphereButtons.Count; i++)
        {
            _sphereButtons[i].ButtonPressed = (Sphere)i == _sphere;
            _sphereButtons[i].Disabled = _mode == Mode.Sculpt && (Sphere)i != Sphere.Earth;
        }
        foreach (var c in _spellRow.GetChildren()) c.QueueFree();
        bool earth = _sphere == Sphere.Earth;
        _spellRow.Visible = !earth;
        _earthBox.Visible = earth;
        _book.OffsetTop = earth ? -300 : -170;
        if (earth) { RefreshEarth(); return; }
        foreach (var s in Spellbook.All.Where(x => x.Sphere == _sphere))
        {
            var spell = s;
            string cost = _sim != null && _sim.World.Sandbox ? "" : $" · {s.Cost:0}";
            var b = new IconButton
            {
                Text = s.Title + cost, Icon = s.Icon, IconSize = 26, CustomMinimumSize = new Vector2(0, 44), ToggleMode = true,
                ButtonPressed = _armed == s, TooltipText = s.Hint + (s.Targeting == Targeting.Group ? "\nЦель — народ." : "\nЦель — место."),
                IconColor = s.Benevolent ? AtlasTheme.Gold : new Color("ff8a70"),
            };
            b.ReserveIconSpace();
            b.Pressed += () =>
            {
                _armed = _armed == spell ? null : spell;
                _brush = null;
                _sound.Play("click");
                _spellHint.Text = _armed == null ? "Выберите силу, затем кликните по планете." : $"{spell.Title}: {spell.Hint} Кликните по планете{(spell.Targeting == Targeting.Group ? " рядом с народом" : "")}. Shift или «держать силу» — накладывать раз за разом.";
                RefreshSpellbook();
            };
            _spellRow.AddChild(b);
        }
        if (_sphere == Sphere.Mind)
        {
            var opt = new OptionButton { TooltipText = "Послание для видения и наставления" };
            foreach (var m in Intervention.Messages) opt.AddItem("послание: " + Dialogue.MessageRu(m));
            opt.Selected = Array.IndexOf(Intervention.Messages, _message);
            opt.ItemSelected += i => _message = Intervention.Messages[i];
            _spellRow.AddChild(opt);
        }
    }

    // ---------- Летопись ----------

    /// <summary>Строка Хроники → BBCode: год золотом, Создатель — голубым курсивом, ссылка на место.</summary>
    private string ToBbcode(string line, WorldEvent e)
    {
        string s = line.TrimStart('-', ' ');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\*\*(.+?)\*\*", "[b]$1[/b]");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\*(.+?)\*", "[i][color=#8fc3ff]$1[/color][/i]");
        s = s.Replace("\n  - ", "\n   • ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^\[b\]Год (\d+)\.\[/b\]", "[color=#d9b45a]Год $1.[/color]");
        string color = e.Type switch
        {
            EventType.Schism or EventType.HolyWar or EventType.Battle or EventType.Revolt or EventType.Martyrdom or EventType.MeteorImpact => "#f0b0a0",
            EventType.AnomalyNoticed or EventType.ProphetBorn or EventType.ReligionFounded or EventType.CultFounded => "#e8d9a8",
            EventType.PrayerUnanswered => "#ffd0a0",
            EventType.PrayerAnswered or EventType.CreatorAppeared => "#b8f0b0",
            _ => "#ece3cc",
        };
        if (e.Cell >= 0) s = $"[url=c:{e.Cell}]{s}[/url]";
        // «»» — открыть это событие в Летописи (с деревом причин).
        return $"[color={color}]{s}[/color] [url=ev:{e.Id}][color=#8f8a7a][hint=Открыть в Летописи: почему так вышло]»[/hint][/color][/url]";
    }

    /// <summary>Новые события → очередь ленты, звуки, эффекты. Возвращает true, если стоит остановить время.</summary>
    private bool PumpEvents(bool duringStep)
    {
        var w = _sim!.World;
        var log = w.Events.Log;
        bool pause = false;
        for (; _lastEvent < log.Count; _lastEvent++)
        {
            var e = log[_lastEvent];
            if (e.Type == EventType.Intervention) continue; // заклинание уже озвучено при наложении
            TerraEventFx(e);
            FinaleEventFx(e);
            float sig = _writer!.Significance(e);
            if (sig < 7f) continue;
            string? line = null;
            try { line = _writer.Line(e); } catch (Exception) { }
            if (line == null) continue;
            _feedQueue.Enqueue(ToBbcode(line, e));
            string snd = e.Type switch
            {
                EventType.Battle or EventType.HolyWar => "drum",
                EventType.Schism or EventType.Collapse => "gong",
                EventType.ProphetBorn or EventType.PrayerUnanswered or EventType.ReligionFounded => "bell",
                EventType.PrayerAnswered => "chime",
                EventType.AnomalyNoticed or EventType.CreatorAppeared => "choir",
                EventType.Earthquake => "rumble",
                EventType.MeteorImpact => "boom",
                EventType.Plague => "dark",
                EventType.PolityFounded => "harp",
                _ => "",
            };
            if (snd != "") _sound.Play(snd, 0.4);
            if (e.Cell >= 0 && !e.ByCreator && sig >= 14f) _globe.Effect(e.Cell, new Color(1f, 0.85f, 0.6f, 0.8f), "ring");
            // Камера-летописец: к значимому событию, не чаще раза в 4 с и только если игрок 8 с не трогал мышь.
            double nowS = Time.GetTicksMsec() / 1000.0;
            if (_autoCam && e.Cell >= 0 && sig >= 28f && nowS - _lastCamMove >= 4 && nowS - _lastUserInput >= 8)
            {
                _lastCamMove = nowS;
                _globe.Focus(e.Cell, zoomIn: false);
            }
            if (duringStep && _pauseOnImportant && e.Type is EventType.ProphetBorn or EventType.PrayerUnanswered or EventType.Schism
                    or EventType.HolyWar or EventType.MeteorImpact or EventType.AnomalyNoticed or EventType.Martyrdom)
            {
                pause = true;
                Toast("Время остановлено: " + System.Text.RegularExpressions.Regex.Replace(line, @"[\*\-]", "").Trim(), 6);
            }
        }
        if (pause) { _speed = 0; RefreshTop(); }
        return pause;
    }

    /// <summary>Лента не мечется: на ходу — не больше трёх строк в секунду, на паузе — всё сразу.</summary>
    private void FeedTick(double delta)
    {
        _feedTimer -= delta;
        bool changed = false;
        while (_feedQueue.Count > 0 && (_speed == 0 || _feedTimer <= 0))
        {
            _feedLines.Add(_feedQueue.Dequeue());
            _feedTimer = 0.33;
            changed = true;
        }
        // На большой скорости старое из очереди отбрасываем, чтобы лента не отставала от времени.
        while (_feedQueue.Count > 30) _feedQueue.Dequeue();
        if (_feedLines.Count > 120) { _feedLines.RemoveRange(0, _feedLines.Count - 120); changed = true; }
        if (changed) _feed.Text = string.Join("\n\n", _feedLines);
    }

    private void OnMeta(string meta)
    {
        if (meta.StartsWith("ev:") && int.TryParse(meta[3..], out int ev)) { OpenLetopis(ev); return; }
        if (meta.StartsWith("c:") && int.TryParse(meta[2..], out int cell))
        {
            _globe.Focus(cell);
            int g = NearestGroup(cell, 2.5f);
            if (g >= 0) Select(g, switchTab: false);
            _sound.Play("click");
        }
    }
}

/// <summary>Просто иконка без кнопки.</summary>
public partial class IconBadge : Control
{
    public string Icon = "";
    public Color Color = AtlasTheme.Gold;
    public override void _Draw() => Icons.Draw(this, Icon, new Rect2(Vector2.Zero, Size), Color);
}
