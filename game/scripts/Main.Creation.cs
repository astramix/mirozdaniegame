using System;
using Godot;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

/// <summary>Экран сотворения (docs/design/viewer-2.md, §2): зерно, пресет, ползунки, песочница, загрузка.</summary>
public partial class Main
{
    private Control _creation = null!;
    private LineEdit _seedEdit = null!;
    private OptionButton _presetOpt = null!;
    private CheckBox _sandboxCheck = null!, _sculptCheck = null!;
    private Button _continueBtn = null!;
    private HSlider _sOcean = null!, _sTemp = null!, _sMut = null!, _sMind = null!, _sPresence = null!, _sLife = null!;
    private Label _creationNote = null!;

    private HSlider Slider(VBoxContainer parent, string title, double min, double max, double step, double value, Func<double, string> fmt)
    {
        var row = new HBoxContainer();
        var name = new Label { Text = title, CustomMinimumSize = new Vector2(190, 0) };
        var val = new Label { CustomMinimumSize = new Vector2(110, 0), HorizontalAlignment = HorizontalAlignment.Right };
        val.AddThemeColorOverride("font_color", AtlasTheme.Gold);
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 24) };
        s.ValueChanged += v => val.Text = fmt(v);
        val.Text = fmt(value);
        row.AddChild(name); row.AddChild(s); row.AddChild(val);
        parent.AddChild(row);
        return s;
    }

    private void BuildCreation()
    {
        _creation = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _creation.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_creation);

        var title = AtlasTheme.Heading("Мироздание", 54);
        title.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        title.OffsetTop = 28; title.OffsetLeft = -300; title.OffsetRight = 300;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        title.AddThemeConstantOverride("outline_size", 10);
        _creation.AddChild(title);
        var sub = AtlasTheme.Small("Сотворите мир. Дождитесь разума. Станьте его богом — или его загадкой.", AtlasTheme.Ink);
        sub.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        sub.OffsetTop = 100; sub.OffsetLeft = -420; sub.OffsetRight = 420;
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AddThemeFontSizeOverride("font_size", 17);
        sub.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        sub.AddThemeConstantOverride("outline_size", 8);
        _creation.AddChild(sub);

        var panel = new PanelContainer();
        panel.AnchorTop = 0.5f; panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = 40; panel.OffsetRight = 640; panel.OffsetTop = -250; panel.OffsetBottom = 250;
        _creation.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 9);
        panel.AddChild(v);
        v.AddChild(AtlasTheme.Heading("Новый мир", 26));

        var seedRow = new HBoxContainer();
        seedRow.AddChild(new Label { Text = "Зерно мира", CustomMinimumSize = new Vector2(190, 0) });
        _seedEdit = new LineEdit { Text = "42", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Одно и то же зерно с теми же настройками даёт тот же мир" };
        seedRow.AddChild(_seedEdit);
        var dice = new IconButton { Icon = "spark", IconSize = 20, CustomMinimumSize = new Vector2(40, 34), TooltipText = "Случайное зерно" };
        dice.Pressed += () => { _seedEdit.Text = (Time.GetTicksUsec() % 1_000_000_007UL).ToString(); _sound.Play("whoosh"); };
        seedRow.AddChild(dice);
        v.AddChild(seedRow);

        var presetRow = new HBoxContainer();
        presetRow.AddChild(new Label { Text = "Тип планеты", CustomMinimumSize = new Vector2(190, 0) });
        _presetOpt = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var n in WorldParams.PresetNames) _presetOpt.AddItem(WorldParams.FromPreset(n).PresetTitle);
        _presetOpt.ItemSelected += i => { ApplyPresetToSliders(WorldParams.FromPreset(WorldParams.PresetNames[i])); _sound.Play("click"); };
        presetRow.AddChild(_presetOpt);
        v.AddChild(presetRow);

        var d = WorldParams.FromPreset("earth");
        _sOcean = Slider(v, "Океаны", 0.1, 0.9, 0.01, d.OceanFraction, x => $"{x * 100:0}% воды");
        _sTemp = Slider(v, "Средняя температура", -15, 30, 0.5, d.MeanTemperature, x => $"{x:0.#} °C");
        _sMut = Slider(v, "Изменчивость жизни", 0.3, 3, 0.05, d.MutationRate, x => $"×{x:0.00}");
        _sMind = Slider(v, "Трудность разума", 0.3, 0.9, 0.01, d.MindThreshold, x => x < 0.5 ? "легко" : x < 0.7 ? "обычно" : "редкость");
        _sPresence = Slider(v, "Заметность Создателя", 0, 1, 0.05, d.CreatorPresence, x => x < 0.3 ? "незаметен" : x < 0.7 ? "ощутим" : "очевиден");
        _sLife = Slider(v, "Эпоха жизни", 200, 1500, 50, d.LifeTicks, x => $"{x:0} тыс. лет");

        _sandboxCheck = new CheckBox { Text = "Песочница: силы бесконечны, заклинания бесплатны", ButtonPressed = true };
        v.AddChild(_sandboxCheck);
        _sculptCheck = new CheckBox { Text = "Вылепить мир своими руками, прежде чем зародится жизнь", ButtonPressed = true };
        v.AddChild(_sculptCheck);
        _creationNote = AtlasTheme.Small("Без песочницы сила копится от веры народов: ответы на молитвы и любовь дают больше всего.");
        v.AddChild(_creationNote);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        var create = new IconButton { Text = "Сотворить", Icon = "spark", IconSize = 24, CustomMinimumSize = new Vector2(0, 48) };
        create.ReserveIconSpace();
        create.AddThemeFontSizeOverride("font_size", 20);
        create.Pressed += CreateFromScreen;
        var load = new IconButton { Text = "Загрузить мир…", Icon = "globe", IconSize = 22, CustomMinimumSize = new Vector2(0, 48) };
        load.ReserveIconSpace();
        load.Pressed += () => { _sound.Play("click"); OpenLoadDialog(); };
        _continueBtn = new Button { Text = "Вернуться в мир", CustomMinimumSize = new Vector2(0, 48), Visible = false };
        _continueBtn.Pressed += () => { _sound.Play("click"); SetMode(_sim != null && _sim.IsPreLife ? Mode.Sculpt : Mode.Game); };
        buttons.AddChild(create); buttons.AddChild(load); buttons.AddChild(_continueBtn);
        v.AddChild(buttons);
    }

    private void ApplyPresetToSliders(WorldParams p)
    {
        _sOcean.Value = p.OceanFraction;
        _sTemp.Value = p.MeanTemperature;
        _sMut.Value = p.MutationRate;
        _sMind.Value = p.MindThreshold;
        _sPresence.Value = p.CreatorPresence;
        _sLife.Value = p.LifeTicks;
    }

    private void CreateFromScreen()
    {
        string text = _seedEdit.Text.Trim();
        // Зерно — число или любое слово (слово превращается в число стабильным хешем FNV-1a).
        if (!ulong.TryParse(text, out ulong seed))
        {
            seed = 14695981039346656037UL;
            foreach (char ch in text) { seed ^= ch; seed *= 1099511628211UL; }
        }
        var p = WorldParams.FromPreset(WorldParams.PresetNames[Math.Max(0, _presetOpt.Selected)]);
        p.OceanFraction = (float)_sOcean.Value;
        p.MeanTemperature = (float)_sTemp.Value;
        p.MutationRate = (float)_sMut.Value;
        p.MindThreshold = (float)_sMind.Value;
        p.CreatorPresence = (float)_sPresence.Value;
        p.LifeTicks = (int)_sLife.Value;
        StartWorld(seed, p, _sandboxCheck.ButtonPressed, sculpt: _sculptCheck.ButtonPressed);
    }
}
