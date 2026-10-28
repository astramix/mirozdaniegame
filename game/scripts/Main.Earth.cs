using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

/// <summary>
/// Сфера «Земля» (docs/design/terraforming.md, §1, §6, §7): кисть, силы Земли, лепка мира до жизни,
/// эффекты и звук. Пока ведётся мазок, время стоит.
/// </summary>
public partial class Main
{
    private PanelContainer _topBar = null!, _leftPanel = null!, _book = null!;
    private VBoxContainer _earthBox = null!;
    private PanelContainer _sculptBanner = null!;
    private readonly List<(BrushInfo info, IconButton button)> _brushButtons = new();
    private readonly List<(Spell spell, IconButton button)> _earthSpellButtons = new();
    private HSlider _radiusSlider = null!, _strengthSlider = null!;
    private Label _radiusLabel = null!, _strengthLabel = null!;
    private CheckBox _slowCheck = null!;
    private IconButton _undoButton = null!;

    private BrushInfo? _brush;
    private TerraformStroke? _stroke;
    private float _brushRadius = 8f, _brushStrength = 0.6f;
    private (float x, float y)? _lineStart;
    private Vector2 _lastDab;
    private float _lastDabX, _lastDabY, _brushX, _brushY;
    private bool _brushOnPlanet;
    private double _holdTimer, _previewTimer;
    private bool _heightDirty, _exhaustedShown;
    private bool _rotPressing, _rotDragged;
    private Vector2 _rotPos;
    private float[] _previewElev = Array.Empty<float>();

    // ---------- Построение ----------

    private VBoxContainer BuildEarthBox()
    {
        var box = new VBoxContainer { Visible = false };
        box.AddThemeConstantOverride("separation", 6);

        var brushRow = new HFlowContainer();
        brushRow.AddThemeConstantOverride("h_separation", 4);
        brushRow.AddThemeConstantOverride("v_separation", 4);
        brushRow.AddChild(AtlasTheme.Heading("Кисть", 16));
        foreach (var info in Spellbook.Brushes)
        {
            var bi = info;
            var b = new IconButton
            {
                Text = info.Title, Icon = info.Icon, IconSize = 20, CustomMinimumSize = new Vector2(0, 36), ToggleMode = true,
                TooltipText = info.Hint + "\nЗажмите левую кнопку и ведите по планете. [ и ] — размер кисти.",
                IconColor = BrushColor(info.Mode),
            };
            b.AddThemeFontSizeOverride("font_size", 14);
            b.ReserveIconSpace();
            b.Pressed += () =>
            {
                _brush = _brush == bi ? null : bi;
                _armed = null;
                _lineStart = null;
                _globe.HideLine();
                if (_brush == null) _globe.HideBrush();
                _sound.Play("click");
                _spellHint.Text = _brush == null ? "Выберите кисть или силу Земли." :
                    $"{bi.Title}: {bi.Hint} Ведите по планете с зажатой левой кнопкой; вращать — правой или средней. Ctrl+Z до зарождения жизни — отменить.";
                RefreshEarth();
            };
            brushRow.AddChild(b);
            _brushButtons.Add((info, b));
        }
        box.AddChild(brushRow);

        var sliders = new HBoxContainer();
        sliders.AddThemeConstantOverride("separation", 10);
        sliders.AddChild(new Label { Text = "Радиус" });
        _radiusSlider = new HSlider { MinValue = 1, MaxValue = 24, Step = 0.5, Value = _brushRadius, CustomMinimumSize = new Vector2(140, 24) };
        _radiusLabel = new Label { CustomMinimumSize = new Vector2(70, 0) };
        _radiusSlider.ValueChanged += v => { _brushRadius = (float)v; UpdateBrushLabels(); };
        sliders.AddChild(_radiusSlider);
        sliders.AddChild(_radiusLabel);
        sliders.AddChild(new Label { Text = "Сила" });
        _strengthSlider = new HSlider { MinValue = 0.1, MaxValue = 1, Step = 0.05, Value = _brushStrength, CustomMinimumSize = new Vector2(120, 24) };
        _strengthLabel = new Label { CustomMinimumSize = new Vector2(40, 0) };
        _strengthSlider.ValueChanged += v => { _brushStrength = (float)v; UpdateBrushLabels(); };
        sliders.AddChild(_strengthSlider);
        sliders.AddChild(_strengthLabel);
        _slowCheck = new CheckBox
        {
            Text = "как природа",
            TooltipText = "Перемена растянется на годы и века: горы — 200 лет, реки — 30, море и климат — 300.\nНарод примет это за дело природы, а не за чудо.",
        };
        sliders.AddChild(_slowCheck);
        _undoButton = new IconButton { Text = "Отменить", Icon = "undo", IconSize = 18, TooltipText = "Ctrl+Z — отменить последнюю правку (только до зарождения жизни)" };
        _undoButton.ReserveIconSpace();
        _undoButton.Pressed += UndoEarth;
        sliders.AddChild(_undoButton);
        box.AddChild(sliders);
        UpdateBrushLabels();

        var spells = new HFlowContainer();
        spells.AddThemeConstantOverride("h_separation", 4);
        spells.AddThemeConstantOverride("v_separation", 4);
        spells.AddChild(AtlasTheme.Heading("Силы", 16));
        foreach (var sp in Spellbook.All.Where(x => x.Sphere == Sphere.Earth && x.Targeting != Targeting.Brush))
        {
            var spell = sp;
            var b = new IconButton
            {
                Icon = sp.Icon, IconSize = 20, CustomMinimumSize = new Vector2(0, 36), ToggleMode = sp.Targeting != Targeting.Global,
                IconColor = sp.Benevolent ? AtlasTheme.Gold : new Color("ff9a70"),
            };
            b.AddThemeFontSizeOverride("font_size", 14);
            b.ReserveIconSpace();
            b.Pressed += () => EarthSpellPressed(spell);
            spells.AddChild(b);
            _earthSpellButtons.Add((spell, b));
        }
        box.AddChild(spells);

        // Плашка лепки до жизни.
        _sculptBanner = new PanelContainer { Visible = false };
        _sculptBanner.AnchorLeft = 0.5f; _sculptBanner.AnchorRight = 0.5f;
        _sculptBanner.OffsetLeft = -420; _sculptBanner.OffsetRight = 420; _sculptBanner.OffsetTop = 10;
        var bv = new VBoxContainer();
        _sculptBanner.AddChild(bv);
        var head = new HBoxContainer();
        head.AddChild(AtlasTheme.Heading("Лепка мира", 24));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var back = new Button { Text = "← К сотворению" };
        back.Pressed += () => { _sound.Play("click"); SetMode(Mode.Creation); };
        var life = new IconButton { Text = "Зародить жизнь", Icon = "seed", IconSize = 22, CustomMinimumSize = new Vector2(0, 40) };
        life.AddThemeFontSizeOverride("font_size", 18);
        life.ReserveIconSpace();
        life.Pressed += BeginLife;
        head.AddChild(back);
        head.AddChild(life);
        bv.AddChild(head);
        bv.AddChild(AtlasTheme.Small("Жизни ещё нет: здесь всё бесплатно и обратимо (Ctrl+Z). Поднимайте материки, рисуйте реки и горы, двигайте сушу. " +
                                     "Когда мир готов — зародите жизнь: она прорастёт там, где вы оставили место.", AtlasTheme.Ink));
        _hud.AddChild(_sculptBanner);
        return box;
    }

    private void UpdateBrushLabels()
    {
        if (_radiusLabel == null) return;
        float km = _brushRadius * 40075f / 512f;
        _radiusLabel.Text = $"{km:0} км";
        _strengthLabel.Text = $"{_brushStrength * 100:0}%";
    }

    private void RefreshEarth()
    {
        if (_sim == null) return;
        var w = _sim.World;
        bool free = w.Sandbox || _sim.IsPreLife;
        foreach (var (info, b) in _brushButtons) b.ButtonPressed = _brush == info;
        foreach (var (spell, b) in _earthSpellButtons)
        {
            b.Text = spell.Title + (free ? "" : $" · {spell.Cost:0}");
            b.ButtonPressed = _armed == spell;
            b.TooltipText = spell.Hint + spell.Targeting switch
            {
                Targeting.Line => "\nДва клика по планете: начало и конец.",
                Targeting.Point => "\nКлик по планете.",
                _ => "\nДействует сразу на весь мир.",
            };
            b.Disabled = !_sim.CanTerraform;
        }
        _slowCheck.Disabled = _sim.IsPreLife;
        if (_sim.IsPreLife) _slowCheck.ButtonPressed = false;
        _undoButton.Visible = _sim.IsPreLife;
        _undoButton.Disabled = !_sim.CanUndo;
    }

    private static Color BrushColor(BrushMode m) => m switch
    {
        BrushMode.Raise or BrushMode.Roughen => new Color("d9a35a"),
        BrushMode.Lower => new Color("5aa8ff"),
        BrushMode.Flatten or BrushMode.Smooth => new Color("e8d9a8"),
        BrushMode.Wet or BrushMode.Fertile => new Color("7fe08a"),
        BrushMode.Dry => new Color("f0c070"),
        BrushMode.Warm => new Color("ff7a4a"),
        BrushMode.Cool => new Color("bfe6ff"),
        _ => new Color("a09080"),
    };

    // ---------- Режим лепки до жизни ----------

    private void AdoptSculpt(Simulation sim)
    {
        _sim = sim;
        _writer = new ChronicleWriter(sim.World, 4000);
        var p = sim.World.Planet;
        _globe.SetGrid(p.EW, p.EH);
        _globe.SetMapSize(p.W, p.H);
        _selectedGroup = -1;
        _armed = null;
        _speed = 0;
        _lastEvent = sim.World.Events.Log.Count;
        _feedQueue.Clear();
        _feedLines.Clear();
        _layer = "elevation";
        _sphere = Sphere.Earth;
        _brush = Spellbook.Brushes[0];
        SetMode(Mode.Sculpt);
        RedrawMap();
        UpdateMarkers();
        RefreshSpellbook();
        _spellHint.Text = "Выберите кисть (Поднять, Опустить…) и ведите по планете с зажатой левой кнопкой. Вращать — правой кнопкой, колесо — масштаб.";
    }

    private void UndoEarth()
    {
        if (_sim == null || !_sim.CanUndo || _stroke != null) return;
        _sim.Undo();
        _sound.Play("whoosh", 0, 1.3f);
        RedrawMap();
        RefreshEarth();
    }

    private void ResizeBrush(float k)
    {
        _brushRadius = Math.Clamp(_brushRadius * k, 1f, 24f);
        _radiusSlider.Value = _brushRadius;
    }

    /// <summary>Снять кисть и силы Земли, закончить незаконченный мазок.</summary>
    private void CancelEarth()
    {
        if (_stroke != null && _sim != null) { _sim.EndStroke(_stroke); _stroke = null; AfterTerraform(); }
        _brush = null;
        _lineStart = null;
        _sound.StopLoop();
        if (_globe != null) { _globe.HideBrush(); _globe.HideLine(); }
    }

    // ---------- Ввод ----------

    private bool EarthPress(Vector2 pos)
    {
        if (_sim == null) return false;
        if (_brush != null)
        {
            if (!_sim.CanTerraform || !_globe.PickMap(pos, out float fx, out float fy)) return false;
            _stroke = _sim.BeginStroke(_brush.Mode, _brushRadius, _brushStrength, _slowCheck.ButtonPressed);
            _exhaustedShown = false;
            Dab(fx, fy);
            return true;
        }
        if (_armed is { Sphere: Sphere.Earth } spell)
        {
            if (!_sim.CanTerraform || !_globe.PickMap(pos, out float fx, out float fy)) return false;
            if (spell.Targeting == Targeting.Point)
            {
                ApplyEarth(spell, new[] { ((int)fx, (int)fy) });
                if (!Input.IsKeyPressed(Key.Shift) && !_keepArmed.ButtonPressed) Disarm();
            }
            else if (spell.Targeting == Targeting.Line)
            {
                if (_lineStart == null)
                {
                    _lineStart = (fx, fy);
                    _globe.ShowStart(fx, fy);
                    _sound.Play("click");
                    _spellHint.Text = $"{spell.Title}: теперь кликните, где {(spell.Type == InterventionType.Drift ? "окажется эта земля" : "закончить")}. Esc — отмена.";
                }
                else
                {
                    var a = _lineStart.Value;
                    _lineStart = null;
                    _globe.HideLine();
                    ApplyEarth(spell, new[] { ((int)a.x, (int)a.y), ((int)fx, (int)fy) });
                    if (!Input.IsKeyPressed(Key.Shift) && !_keepArmed.ButtonPressed) Disarm();
                }
            }
            return true;
        }
        return false;
    }

    private bool EarthRelease()
    {
        if (_stroke == null || _sim == null) return false;
        var st = _stroke;
        _stroke = null;
        _globe.BrushDust(_brushX, _brushY, Colors.White, false);
        _sound.StopLoop();
        bool slow = st.Slow;
        _sim.EndStroke(st);
        if (st.Count > 0)
        {
            if (slow) Toast("Земля изменится постепенно, за века — так, что народ примет это за дело природы.", 4);
            else if (st.Work > 30f && _brush?.Mode is BrushMode.Raise or BrushMode.Lower or BrushMode.Roughen)
            {
                _sound.Play(_brush.Mode == BrushMode.Lower ? "splash" : "rumble", 0.2, 1.1f);
                _globe.Shake(Math.Min(0.03f, st.Work / 4000f), 0.6f);
            }
        }
        AfterTerraform();
        return true;
    }

    private bool EarthMotion(Vector2 pos)
    {
        if (_sim == null) return false;
        bool hit = _globe.PickMap(pos, out float fx, out float fy);
        if (_brush != null && hit)
        {
            _brushX = fx; _brushY = fy; _brushOnPlanet = true;
            _globe.ShowBrush(fx, fy, _brushRadius, BrushColor(_brush.Mode));
            UpdateTerraHover(fx, fy);
        }
        else if (_brush != null) { _brushOnPlanet = false; _globe.HideBrush(); }
        if (_armed is { Sphere: Sphere.Earth, Targeting: Targeting.Line } && _lineStart is { } a && hit)
            _globe.ShowLine(a.x, a.y, fx, fy, _armed.Type == InterventionType.Strait ? new Color("5aa8ff") : AtlasTheme.Gold);
        if (_armed is { Sphere: Sphere.Earth } && hit && _brush == null) UpdateTerraHover(fx, fy);
        if (_stroke == null || !hit) return _stroke != null;
        // Мазки через шаг ~трети радиуса — ровный след без «бусин».
        float step = Math.Max(1f, _brushRadius * 0.35f);
        float dx = fx - _lastDabX, dy = fy - _lastDabY;
        if (dx > _sim.World.Planet.W / 2f) dx -= _sim.World.Planet.W; else if (dx < -_sim.World.Planet.W / 2f) dx += _sim.World.Planet.W;
        if (dx * dx + dy * dy >= step * step) Dab(fx, fy);
        return true;
    }

    private void Dab(float fx, float fy)
    {
        if (_sim == null || _stroke == null) return;
        _lastDabX = fx; _lastDabY = fy;
        _brushX = fx; _brushY = fy;
        _holdTimer = 0;
        bool ok = _sim.StrokeDab(_stroke, (int)fx, (int)fy);
        if (!ok)
        {
            if (!_exhaustedShown) Toast("Сила Создателя иссякла. Она копится от веры народов — ответьте на их молитвы.", 5);
            _exhaustedShown = true;
            _sound.StopLoop();
            _globe.BrushDust(fx, fy, Colors.White, false);
            return;
        }
        _heightDirty = true;
        var p = _sim.World.Planet;
        int i = Math.Clamp((int)fy, 0, p.H - 1) * p.W + Math.Clamp((int)fx, 0, p.W - 1);
        bool water = p.Total(i) < p.SeaLevel;
        var mode = _stroke.Mode;
        var dust = mode switch
        {
            BrushMode.Lower when water => new Color(0.6f, 0.85f, 1f, 0.9f),
            BrushMode.Raise or BrushMode.Roughen or BrushMode.Lower => water ? new Color(0.7f, 0.85f, 1f, 0.9f) : new Color(0.55f, 0.42f, 0.28f, 0.9f),
            _ => new Color(BrushColor(mode), 0.8f),
        };
        _globe.BrushDust(fx, fy, dust, true);
        bool earthy = mode is BrushMode.Raise or BrushMode.Lower or BrushMode.Flatten or BrushMode.Smooth or BrushMode.Roughen;
        _sound.Loop(earthy ? "earth" : "air", 0.35f + 0.6f * _brushStrength, earthy ? 0.8f + 0.4f * (1f - _brushRadius / 24f) : 1f);
    }

    /// <summary>Каждый кадр: удержание кисти на месте наращивает правку, высоты обновляются сразу, карта — 4 раза в секунду.</summary>
    private void EarthTick(double delta)
    {
        if (_sim == null) return;
        if (_stroke != null && _brushOnPlanet)
        {
            _holdTimer += delta;
            if (_holdTimer > 0.08) Dab(_brushX, _brushY);
            _previewTimer -= delta;
            if (_previewTimer <= 0)
            {
                _previewTimer = 0.25;
                _sim.RecomputePlanet();
                var w = _sim.World;
                _globe.SetMap(WorldMaps_Render(), w.Planet.W, w.Planet.H, w.Planet);
                _heightDirty = false;
            }
        }
        if (_heightDirty) PreviewHeight();
        if (_brush != null && _brushOnPlanet && _stroke == null) _globe.ShowBrush(_brushX, _brushY, _brushRadius, BrushColor(_brush.Mode));
    }

    private byte[] WorldMaps_Render() => Mirozdanie.Core.Output.WorldMaps.Render(_sim!.World, _layer);

    /// <summary>Высоты прямо из слоёв правок (без пересчёта климата) — чтобы горы росли под кистью без задержки.</summary>
    private void PreviewHeight()
    {
        _heightDirty = false;
        var p = _sim!.World.Planet;
        int n = p.W * p.H;
        if (_previewElev.Length != n) _previewElev = new float[n];
        for (int i = 0; i < n; i++) _previewElev[i] = p.BaseElevation[i] + p.ElevDelta[i] - p.SeaLevel;
        _globe.SetHeight(_previewElev, p.W, p.H);
    }

    private void UpdateTerraHover(float fx, float fy)
    {
        var p = _sim!.World.Planet;
        int i = Math.Clamp((int)fy, 0, p.H - 1) * p.W + Math.Clamp((int)fx, 0, p.W - 1);
        float e = p.Elevation[i];
        string where = e < 0 ? $"море, глубина {-e * 1000:0} м" : $"{Mirozdanie.Core.Planet.Biomes.Russian(p.Biome[i])}, высота {e * 1000:0} м";
        string tool = _brush != null ? $"   ·   кисть «{_brush.Title}», радиус {_brushRadius * 40075f / 512f:0} км" :
            _armed != null ? $"   ·   {_armed.Title}" : "";
        _hover.Text = $"{where}, {p.Temperature[i]:0} °C, осадки {p.Precip[i]:0} мм/год{tool}";
    }

    // ---------- Силы Земли ----------

    private void EarthSpellPressed(Spell spell)
    {
        if (_sim == null) return;
        _brush = null;
        _globe.HideBrush();
        _lineStart = null;
        _globe.HideLine();
        if (spell.Targeting == Targeting.Global)
        {
            _armed = null;
            ApplyEarth(spell, Array.Empty<(int, int)>());
            RefreshEarth();
            return;
        }
        _armed = _armed == spell ? null : spell;
        _sound.Play("click");
        _spellHint.Text = _armed == null ? "Выберите кисть или силу Земли." :
            $"{spell.Title}: {spell.Hint}{(spell.Targeting == Targeting.Line ? " Два клика: начало и конец." : " Кликните по планете.")} Shift — ещё раз, Esc — отмена.";
        RefreshEarth();
    }

    private void ApplyEarth(Spell spell, (int x, int y)[] pts)
    {
        if (_sim == null || !_sim.CanTerraform) return;
        var w = _sim.World;
        bool slow = _slowCheck.ButtonPressed && w.Era == Era.History;
        float cost = w.Sandbox || _sim.IsPreLife ? 0 : spell.Cost * (slow ? 0.5f : 1f);
        if (w.Era == Era.History && w.CreatorEnergy < cost) Toast($"Силы мало ({w.CreatorEnergy:0} из {cost:0}): земля изменится слабее.");
        _sim.ApplyNow(new Intervention { Type = spell.Type, Target = "map", Path = Intervention.PathOf(pts), Slow = slow });
        EarthFx(spell.Type, pts, slow);
        AfterTerraform();
        if (slow) Toast($"{spell.Title}: перемена растянется на годы и века.", 4);
    }

    /// <summary>Перерисовать мир после правки: карта, маркеры, лента (утонувшие и беженцы), кнопки.</summary>
    private void AfterTerraform()
    {
        if (_sim == null) return;
        _heightDirty = false;
        if (_mode == Mode.Game) PumpEvents(false);
        RedrawMap();
        UpdateMarkers();
        if (_mode == Mode.Game) { RefreshTop(); RefreshPeople(false); }
        RefreshEarth();
    }

    private void EarthFx((int x, int y)[] pts, int k, Color c, int amount = 120, float speed = 0.15f, float life = 1.6f, float size = 0.008f)
    {
        if (k < pts.Length) _globe.Burst(pts[k].x, pts[k].y, c, amount, speed, life, size);
    }

    /// <summary>Эффект и звук силы Земли (terraforming.md, §6). «Как природа» — тихо, без дрожи.</summary>
    private void EarthFx(InterventionType t, (int x, int y)[] pts, bool slow)
    {
        var dust = new Color(0.58f, 0.45f, 0.3f, 0.95f);
        var spray = new Color(0.65f, 0.85f, 1f, 0.9f);
        if (slow)
        {
            if (pts.Length > 0) _globe.Burst(pts[0].x, pts[0].y, new Color(1f, 0.9f, 0.6f, 0.6f), 40, 0.05f, 2f, 0.006f);
            _sound.Play("whoosh", 0, 0.7f);
            return;
        }
        void Along(Color c, int bursts, int amount)
        {
            if (pts.Length < 2) return;
            for (int i = 0; i <= bursts; i++)
            {
                float f = i / (float)bursts;
                float dx = pts[1].x - pts[0].x;
                if (dx > 256) dx -= 512; else if (dx < -256) dx += 512;
                _globe.Burst(pts[0].x + dx * f, pts[0].y + (pts[1].y - pts[0].y) * f, c, amount, 0.18f, 1.8f, 0.01f);
            }
        }
        switch (t)
        {
            case InterventionType.Ridge:
                Along(dust, 8, 80); _globe.Shake(0.05f, 1.6f); _sound.Play("mountain", 0); break;
            case InterventionType.Bridge:
                Along(new Color(0.8f, 0.7f, 0.5f, 0.9f), 6, 50); Along(spray, 6, 30); _globe.Shake(0.02f, 0.8f); _sound.Play("rumble", 0, 1.2f); break;
            case InterventionType.Strait:
                Along(spray, 8, 70); _globe.Shake(0.03f, 1f); _sound.Play("wave", 0, 1.2f); break;
            case InterventionType.Island:
                EarthFx(pts, 0, dust, 140, 0.2f); EarthFx(pts, 0, spray, 120, 0.25f); _globe.Shake(0.03f, 1f); _sound.Play("mountain", 0, 1.15f); break;
            case InterventionType.Sink:
                EarthFx(pts, 0, spray, 260, 0.28f, 2.2f, 0.012f); _globe.Shake(0.04f, 1.4f); _sound.Play("wave", 0); break;
            case InterventionType.River:
                EarthFx(pts, 0, spray, 80, 0.1f); _sound.Play("splash", 0); break;
            case InterventionType.Lake:
                EarthFx(pts, 0, spray, 100, 0.12f); _sound.Play("splash", 0, 0.8f); break;
            case InterventionType.Volcano:
                if (pts.Length > 0) _globe.Fire(pts[0].x, pts[0].y);
                _globe.Shake(0.07f, 2.2f); _sound.Play("volcano", 0); break;
            case InterventionType.Oasis:
                EarthFx(pts, 0, new Color(0.5f, 0.95f, 0.55f, 0.9f), 120, 0.1f, 2f); _sound.Play("growth", 0); break;
            case InterventionType.Desert:
                EarthFx(pts, 0, new Color(0.95f, 0.8f, 0.5f, 0.9f), 140, 0.14f, 2.2f); _sound.Play("wind", 0); break;
            case InterventionType.Glacier:
                EarthFx(pts, 0, new Color(0.9f, 0.97f, 1f, 0.95f), 160, 0.1f, 2.2f); _sound.Play("frost", 0); break;
            case InterventionType.Thaw:
                EarthFx(pts, 0, new Color(1f, 0.7f, 0.4f, 0.9f), 120, 0.12f, 1.8f); _sound.Play("heat", 0); break;
            case InterventionType.Drift:
                EarthFx(pts, 0, dust, 200, 0.2f, 2f, 0.012f); EarthFx(pts, 1, dust, 200, 0.2f, 2f, 0.012f);
                _globe.Shake(0.09f, 2.6f); _sound.Play("drift", 0); break;
            case InterventionType.Warmer:
                _globe.AtmosphereFlash(new Color(1f, 0.55f, 0.25f)); _sound.Play("heat", 0); break;
            case InterventionType.Colder:
                _globe.AtmosphereFlash(new Color(0.8f, 0.95f, 1f)); _sound.Play("frost", 0); break;
            case InterventionType.SeaRise:
                _globe.AtmosphereFlash(new Color(0.2f, 0.5f, 1f)); _globe.Shake(0.03f, 1.5f); _sound.Play("wave", 0); break;
            case InterventionType.SeaFall:
                _globe.AtmosphereFlash(new Color(0.3f, 0.9f, 0.8f)); _sound.Play("wave", 0, 0.8f); break;
        }
    }

    /// <summary>Переселение и гибель на планете: колонны огоньков, брызги над утонувшими (из PumpEvents).</summary>
    private void TerraEventFx(in WorldEvent e)
    {
        var w = _sim!.World;
        switch (e.Type)
        {
            case EventType.GroupFissioned:
                if (e.B >= 0 && e.B < w.Groups.Count)
                    _globe.Migration(_globe.CellPoint(w.Groups[e.B].Cell), _globe.CellPoint(e.Cell), GroupColor(w.Groups[e.B]).Lightened(0.3f));
                break;
            case EventType.Refugees:
                _globe.Migration(_globe.CellPoint(e.B), _globe.CellPoint(e.Cell), new Color(0.7f, 0.85f, 1f));
                break;
            case EventType.GroupDrowned:
            {
                int ex = e.Cell % w.Planet.EW, ey = e.Cell / w.Planet.EW;
                _globe.Burst(ex * w.Planet.Scale + 2, ey * w.Planet.Scale + 2, new Color(0.6f, 0.8f, 1f, 0.9f), 140, 0.2f);
                _sound.Play("splash", 0.3, 0.7f);
                break;
            }
        }
    }
}
