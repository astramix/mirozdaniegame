using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Output;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

/// <summary>
/// Зритель «Мироздания» (docs/design/viewer-2.md): экран сотворения, вращаемая планета, книга заклинаний,
/// разговор с народами, молитвы, предзнаменования, сохранение миров, процедурный звук.
/// </summary>
public partial class Main : Node
{
    private enum Mode { Creation, Loading, Sculpt, Game }

    private Mode _mode = Mode.Creation;
    private Simulation? _sim;
    private ChronicleWriter? _writer;
    private Globe _globe = null!;
    private Sound _sound = null!;
    private Control _root = null!;
    private Theme _theme = null!;

    // Время: лет в секунду; после сотворения — пауза.
    private static readonly float[] Speeds = { 0f, 1f, 4f, 15f };
    private int _speed;
    private double _acc;
    private bool _pauseOnImportant = true;

    // Выбор и заклинания.
    private int _selectedGroup = -1;
    private Spell? _armed;
    private string _layer = "biomes";
    private bool _mapDirty = true;
    private long _lastMapYear = -1;
    private int _lastEvent;
    private double _markerTimer;
    private Vector2 _pressPos;
    private bool _pressing, _dragging;

    // Аргументы командной строки (для дымового теста и скриншотов).
    private bool _smoke;
    private string _shot = "", _demo = "";
    private int _frames;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--smoke") _smoke = true;
            if (arg.StartsWith("--shot=")) _shot = arg[7..];
            if (arg.StartsWith("--demo=")) _demo = arg[7..];
        }
        _theme = AtlasTheme.Build();
        _sound = new Sound();
        AddChild(_sound);
        _globe = new Globe();
        AddChild(_globe);
        var layer = new CanvasLayer();
        AddChild(layer);
        _root = new Control { Theme = _theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_root);
        BuildHud();
        BuildCreation();
        BuildDialogue();
        BuildMenus();
        SetMode(Mode.Creation);
        if (!_smoke && string.IsNullOrEmpty(_shot)) _sound.StartMusic();
        if (_smoke && string.IsNullOrEmpty(_demo)) _demo = "smoke";
        if (_smoke || !string.IsNullOrEmpty(_demo)) StartWorld(42, WorldParams.FromPreset("earth"), sandbox: true, quick: true, sculpt: _demo == "sculpt");
    }

    private void SetMode(Mode m)
    {
        _mode = m;
        _creation.Visible = m == Mode.Creation;
        _loading.Visible = m == Mode.Loading;
        _hud.Visible = m is Mode.Game or Mode.Sculpt;
        _topBar.Visible = _tabs.Visible = m == Mode.Game;
        _sculptBanner.Visible = m == Mode.Sculpt;
        if (m != Mode.Game && m != Mode.Sculpt) CancelEarth();
        _continueBtn.Visible = _sim != null;
        _toast.Visible = false;
        if (m != Mode.Game) { _dialogue.Visible = false; CloseModal(); }
    }

    // ---------- Сотворение и загрузка ----------

    /// <summary>
    /// Сотворение. С лепкой — сначала только планета (режим лепки, terraforming.md, §1), жизнь зарождается по кнопке.
    /// </summary>
    private void StartWorld(ulong seed, WorldParams p, bool sandbox, bool quick = false, bool sculpt = false)
    {
        SetMode(Mode.Loading);
        _loadingLabel.Text = "Мир сотворяется: плиты, климат, реки…";
        _loadingBar.Value = 0;
        _sound.Play("create", 0);
        if (quick) p.LifeTicks = 200;
        Task.Run(() =>
        {
            try
            {
                var sim = Simulation.Create(seed, p);
                sim.World.Sandbox = sandbox;
                if (sculpt) { _incomingSculpt = sim; return; }
                LifeAndAwakening(sim);
            }
            catch (Exception e)
            {
                CallDeferred(nameof(Progress), "Ошибка сотворения: " + e.Message, 0.0);
            }
        });
    }

    /// <summary>Зародить жизнь на вылепленной планете (из режима лепки).</summary>
    private void BeginLife()
    {
        if (_sim == null) return;
        var sim = _sim;
        CancelEarth();
        SetMode(Mode.Loading);
        _loadingLabel.Text = "Жизнь зарождается в тёплых водах…";
        _loadingBar.Value = 0;
        _sound.Play("create", 0);
        Task.Run(() =>
        {
            try { LifeAndAwakening(sim); }
            catch (Exception e) { CallDeferred(nameof(Progress), "Ошибка: " + e.Message, 0.0); }
        });
    }

    /// <summary>Эпоха жизни и первые годы истории (в фоновом потоке); готовый мир примет _Process.</summary>
    private void LifeAndAwakening(Simulation sim)
    {
        var p = sim.World.Params;
        sim.RunLife(t =>
        {
            if (t % 20 == 0) CallDeferred(nameof(Progress), $"Эпоха жизни: {t} тыс. лет, видов {sim.World.Life.Species.Count}", 90.0 * t / Math.Max(1, p.LifeTicks));
        });
        CallDeferred(nameof(Progress), "Пробуждение: первые народы зажигают костры…", 95.0);
        for (int y = 0; y < 10; y++) sim.Step();
        _incoming = sim;
    }

    /// <summary>Загрузка: мир заново прогоняется по журналу вмешательств.</summary>
    private void LoadWorld(WorldSave save)
    {
        SetMode(Mode.Loading);
        _loadingLabel.Text = $"Мир {save.WorldName} вспоминает свою историю…";
        _loadingBar.Value = 0;
        _sound.Play("create", 0);
        Task.Run(() =>
        {
            try
            {
                var sim = Simulation.Load(save, y =>
                {
                    if (y % 25 == 0) CallDeferred(nameof(Progress), $"История: год {y} из {save.Year}", 100.0 * y / Math.Max(1, save.Year));
                });
                _incoming = sim;
            }
            catch (Exception e)
            {
                CallDeferred(nameof(Progress), "Не удалось загрузить: " + e.Message, 0.0);
            }
        });
    }

    private volatile Simulation? _incoming, _incomingSculpt;

    public void Progress(string text, double value)
    {
        _loadingLabel.Text = text;
        _loadingBar.Value = value;
    }

    /// <summary>Готовый мир из фонового потока принимается здесь, в главном потоке.</summary>
    private void Adopt(Simulation sim)
    {
        _sim = sim;
        _writer = new ChronicleWriter(sim.World, 4000);
        _globe.SetGrid(sim.World.Planet.EW, sim.World.Planet.EH);
        _globe.SetMapSize(sim.World.Planet.W, sim.World.Planet.H);
        if (_layer == "elevation" && _mode == Mode.Loading) _layer = "biomes";
        if (_sphere == Sphere.Earth) _brush = null;
        _selectedGroup = -1;
        _armed = null;
        _speed = 0;
        _lastEvent = Math.Max(0, sim.World.Events.Log.Count - 400);
        _feedQueue.Clear();
        _feedLines.Clear();
        _omensDone.Clear();
        _mapDirty = true;
        SetMode(Mode.Game);
        RefreshAll();
        Toast($"Мир {sim.World.WorldName.Native} («{sim.World.WorldName.Gloss}») сотворён. Время стоит — нажмите ▶, когда будете готовы.");
        if (string.IsNullOrEmpty(_shot)) StartTutorial();
    }

    // ---------- Кадр ----------

    public override void _Process(double delta)
    {
        if (_incoming != null) { var s = _incoming; _incoming = null; Adopt(s); }
        if (_incomingSculpt != null) { var s = _incomingSculpt; _incomingSculpt = null; AdoptSculpt(s); }
        TickToast(delta);
        if (_mode == Mode.Sculpt && _sim != null) { EarthTick(delta); SmokeTick(); return; }
        if (_mode != Mode.Game || _sim == null) { SmokeTick(); return; }
        EarthTick(delta);
        var w = _sim.World;
        // Время: накопитель лет.
        if (_speed > 0 && !_dialogue.Visible && _stroke == null)
        {
            _acc += delta * Speeds[_speed];
            int steps = 0;
            while (_acc >= 1 && steps < 6)
            {
                _acc -= 1;
                _sim.Step();
                steps++;
                if (PumpEvents(true)) { _acc = 0; break; }
            }
        }
        else _acc = 0;
        PumpEvents(false);
        FeedTick(delta);
        _markerTimer -= delta;
        if (_markerTimer <= 0)
        {
            _markerTimer = 0.25;
            if (_layer is not ("biomes" or "elevation") && w.Year - _lastMapYear >= 10) _mapDirty = true;
            if (_mapDirty) RedrawMap();
            UpdateMarkers();
            RefreshTop();
            UpdateAsh();
            UpdateMood();
            if (_tabs.CurrentTab == TabPeople) RefreshPeople(false);
            if (_tabs.CurrentTab == TabPrayers) RefreshPrayers();
            CheckOmens();
        }
        SmokeTick();
    }

    private void RefreshAll()
    {
        RedrawMap();
        UpdateMarkers();
        RefreshTop();
        RefreshSpellbook();
        RefreshPeople(true);
        RefreshPrayers();
        CheckOmens();
    }

    /// <summary>Настроение музыки по миру: ядерная зима и мор — страх; частые битвы — война; контакт, любовь, Кардашёв — чудо.</summary>
    private void UpdateMood()
    {
        var w = _sim!.World;
        var log = w.Events.Log;
        int battles = 0, plagues = 0, wonders = 0;
        for (int i = log.Count - 1; i >= 0 && i >= log.Count - 4000; i--)
        {
            var e = log[i];
            if (w.Year - e.Year > 20) break;
            switch (e.Type)
            {
                case EventType.Battle: case EventType.HolyWar: battles++; break;
                case EventType.Plague when e.Magnitude > 0.15f: plagues++; break;
                case EventType.ContactAnswered or EventType.PrayerAnswered or EventType.CreatorAppeared or EventType.OathKept or EventType.MegaprojectCompleted: wonders++; break;
            }
        }
        var mood = w.Year < w.NuclearWinterUntil + 10 || plagues >= 4 || w.Warming > 2.5f ? Sound.Mood.Dread
            : battles >= 3 ? Sound.Mood.War
            : wonders >= 1 || w.Kardashev >= 0.95f ? Sound.Mood.Wonder
            : Sound.Mood.Calm;
        _sound.SetMood(mood);
    }

    /// <summary>Пепел в небе — по средней «зиме» над сушей (вулкан, метеорит; позже — ядерная зима).</summary>
    private void UpdateAsh()
    {
        var w = _sim!.World;
        float sum = 0; int n = 0;
        foreach (var c in w.Life.LandCells) { sum += w.TempOffset[c]; n++; }
        float mean = n > 0 ? sum / n : 0;
        _globe.SetAsh(Math.Clamp(-mean / 1.5f, 0f, 1f));
    }

    private void RedrawMap()
    {
        if (_sim == null) return;
        var w = _sim.World;
        _globe.SetMap(WorldMaps.Render(w, _layer), w.Planet.W, w.Planet.H, w.Planet);
        foreach (var (id, b) in _layerButtons) b.SetPressedNoSignal(id == _layer);
        _lastMapYear = w.Year;
        _mapDirty = false;
    }

    private Color GroupColor(Group g) => _layer switch
    {
        "religions" => g.ReligionId >= 0 ? Portrait.ToColor(MapRenderer.Palette(g.ReligionId)) : new Color(0.7f, 0.7f, 0.7f),
        "languages" => Portrait.ToColor(MapRenderer.Palette(g.LanguageId)),
        "polities" => g.PolityId >= 0 ? Portrait.ToColor(MapRenderer.Palette(g.PolityId)) : new Color(0.8f, 0.8f, 0.8f),
        _ => Portrait.ToColor(WorldMaps.AttitudeColor(g.Attitude)),
    };

    private void UpdateMarkers()
    {
        if (_sim == null) return;
        var w = _sim.World;
        var list = new List<(int, float, Color, string)>();
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            var c = GroupColor(g);
            if (Dialogue.HasOpenPrayer(w, g)) c = c.Lerp(Colors.White, 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 150f));
            list.Add((g.Cell, g.Pop, c, g.Name.Native));
        }
        int sel = _selectedGroup >= 0 && w.Groups[_selectedGroup].Alive ? w.Groups[_selectedGroup].Cell : -1;
        _globe.SetMarkers(list, sel);
        _globe.SetCivilization(w, _layer);
    }

    // ---------- Ввод ----------

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton or InputEventMouseMotion { ButtonMask: not 0 } or InputEventKey) _lastUserInput = Time.GetTicksMsec() / 1000.0;
        if (_mode == Mode.Creation && ev is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wz)
        {
            _globe.Zoom(wz.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f);
            return;
        }
        if (_mode is not (Mode.Game or Mode.Sculpt) || _sim == null) return;
        bool game = _mode == Mode.Game;
        if (ev is InputEventKey { Pressed: true } k)
        {
            switch (k.Keycode)
            {
                case Key.Space when game: SetSpeed(_speed == 0 ? 1 : 0); break;
                case Key.Key1 when game: SetSpeed(1); break;
                case Key.Key2 when game: SetSpeed(2); break;
                case Key.Key3 when game: SetSpeed(3); break;
                case Key.Escape: Disarm(); break;
                case Key.C when game: _autoCamBox.ButtonPressed = !_autoCamBox.ButtonPressed; Toast(_autoCam ? "Камера-летописец: следит за важными событиями." : "Камера в ваших руках."); break;
                case Key.F1: StartTutorial(force: true); break;
                case Key.N: _globe.ToggleNight(); Toast(_globe.Night ? "Ночь: видны огни городов." : "День."); break;
                case Key.Z when k.CtrlPressed: UndoEarth(); break;
                case Key.Bracketleft: ResizeBrush(1f / 1.25f); break;
                case Key.Bracketright: ResizeBrush(1.25f); break;
            }
        }
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _globe.Zoom(0.9f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _globe.Zoom(1.1f);
            else if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            {
                // Правая или средняя с протяжкой вращает всегда; правый клик без протяжки — снять силу (terraforming.md, §7).
                if (mb.Pressed) { _rotPressing = true; _rotDragged = false; _rotPos = mb.Position; }
                else
                {
                    if (_rotPressing && !_rotDragged && mb.ButtonIndex == MouseButton.Right) Disarm();
                    _rotPressing = false;
                }
            }
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    if (EarthPress(mb.Position)) return;
                    _pressing = true; _dragging = false; _pressPos = mb.Position;
                }
                else
                {
                    if (EarthRelease()) return;
                    if (_pressing && !_dragging) ClickGlobe(mb.Position, mb.ShiftPressed);
                    _pressing = false; _dragging = false;
                }
            }
        }
        else if (ev is InputEventMouseMotion mm)
        {
            if (_rotPressing)
            {
                if ((mm.Position - _rotPos).Length() > 4) _rotDragged = true;
                if (_rotDragged) _globe.Drag(mm.Relative);
            }
            if (EarthMotion(mm.Position)) return;
            if (_pressing && (mm.Position - _pressPos).Length() > 6) _dragging = true;
            if (_dragging) _globe.Drag(mm.Relative);
            UpdateHover(mm.Position);
        }
    }

    private void ClickGlobe(Vector2 pos, bool shift)
    {
        var w = _sim!.World;
        if (_mode != Mode.Game) return;
        int cell = _globe.PickCell(pos);
        if (cell < 0) return;
        if (_armed != null)
        {
            Cast(_armed, cell);
            if (!shift && !_keepArmed.ButtonPressed) Disarm();
            return;
        }
        int g = NearestGroup(cell, 2.5f);
        if (g >= 0)
        {
            Select(g);
            _sound.Play("click");
        }
    }

    private int NearestGroup(int cell, float maxDist)
    {
        var w = _sim!.World;
        int best = -1; float bd = maxDist;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            float d = w.Planet.EcoDistance(g.Cell, cell);
            if (d <= bd) { bd = d; best = g.Id; }
        }
        if (best < 0 && cell >= 0 && w.CellOwner[cell] >= 0) best = w.CellOwner[cell];
        return best;
    }

    private void UpdateHover(Vector2 pos)
    {
        if (_sim == null) return;
        var w = _sim.World;
        int cell = _globe.PickCell(pos);
        if (cell < 0) { _hover.Text = ""; return; }
        var p = w.Planet;
        string place = p.EIsLand(cell) ? Mirozdanie.Core.Planet.Biomes.Russian(p.EBiome[cell]) : "море";
        string who = "";
        int o = w.CellOwner[cell];
        if (o >= 0) who = $" · земли народа {w.Groups[o].Name.Native} ({Russian.Number(w.Groups[o].Pop)} чел.)";
        string t = $"{p.ETemp[cell] + w.TempOffset[cell]:0} °C";
        _hover.Text = $"{place}, {t}{who}" + (_armed != null ? $"   ·   {_armed.Title}: клик — наложить{(_armed.Targeting == Targeting.Group ? " на народ" : "")}, Shift — ещё раз, Esc — отмена" : "");
    }

    // ---------- Заклинания ----------

    private void Cast(Spell spell, int cell)
    {
        var w = _sim!.World;
        string target;
        int gid = -1;
        if (spell.Targeting == Targeting.Group)
        {
            gid = NearestGroup(cell, 3f);
            if (gid < 0) { Toast("Здесь нет народа — эта сила действует на народ."); _sound.Play("click", 0, 0.6f); return; }
            target = $"group:{gid}";
        }
        else target = $"cell:{cell % w.Planet.EW},{cell / w.Planet.EW}";
        float cost = w.Sandbox ? 0 : spell.Cost;
        if (w.CreatorEnergy < cost) Toast($"Силы мало ({w.CreatorEnergy:0} из {cost:0}): знак выйдет слабым.");
        var it = new Intervention { Type = spell.Type, Target = target, Message = spell.Type == InterventionType.Teach || spell.Type == InterventionType.Vision ? _message : "" };
        _sim.ApplyNow(it);
        int at = gid >= 0 ? w.Groups[gid].Cell : cell;
        SpellFx(spell, at);
        PumpEvents(false);
        _mapDirty |= spell.Type is InterventionType.Meteor or InterventionType.Flood;
        RefreshTop();
    }

    private void SpellFx(Spell spell, int cell)
    {
        var color = spell.Sphere switch
        {
            Sphere.Nature => new Color("7fb8ff"), Sphere.Life => new Color("9be07a"), Sphere.Mind => new Color("c9a0ff"),
            Sphere.Shrines => AtlasTheme.Gold, _ => new Color("ff9a6a"),
        };
        if (!spell.Benevolent) color = color.Lerp(AtlasTheme.Blood, 0.45f);
        string kind = spell.Type switch
        {
            InterventionType.Lightning => "bolt",
            InterventionType.Meteor => "meteor",
            InterventionType.Vision or InterventionType.Appear or InterventionType.Shrine or InterventionType.Inspire => "beam",
            _ => "ring",
        };
        _globe.Effect(cell, color, kind);
        _sound.Play(SoundFor(spell.Type), 0.05);
    }

    private static string SoundFor(InterventionType t) => t switch
    {
        InterventionType.Rain or InterventionType.Flood => "rain",
        InterventionType.Drought or InterventionType.Winter => "wind",
        InterventionType.Lightning or InterventionType.Threaten => "thunder",
        InterventionType.Earthquake => "rumble",
        InterventionType.Meteor => "boom",
        InterventionType.Harvest or InterventionType.Fertility or InterventionType.Herds or InterventionType.Bless or InterventionType.BlessDynasty => "chime",
        InterventionType.Heal or InterventionType.Peace or InterventionType.Inspire or InterventionType.Teach => "harp",
        InterventionType.Plague or InterventionType.Forget or InterventionType.CursedPlace => "dark",
        InterventionType.Vision or InterventionType.Appear or InterventionType.Spark => "choir",
        InterventionType.Shrine or InterventionType.Demand => "bell",
        InterventionType.CurseDynasty => "gong",
        InterventionType.Strife => "drum",
        _ => "click",
    };

    private void Disarm()
    {
        _armed = null;
        _lineStart = null;
        _globe.HideLine();
        if (_stroke == null) { _brush = null; _globe.HideBrush(); }
        RefreshSpellbook();
    }

    private void SetSpeed(int s)
    {
        _speed = Math.Clamp(s, 0, Speeds.Length - 1);
        _sound.Play("click");
        RefreshTop();
    }

    // ---------- Дымовой тест и скриншоты ----------

    private void SmokeTick()
    {
        if (!_smoke && string.IsNullOrEmpty(_shot)) return;
        if (_sim == null) return;
        _frames++;
        if (_frames == 5 && !string.IsNullOrEmpty(_demo)) RunDemo(_demo);
        if (_frames == 40 && !string.IsNullOrEmpty(_shot)) GetViewport().GetTexture().GetImage().SavePng(_shot);
        if (_frames >= 42)
        {
            GD.Print($"SMOKE OK: год {_sim!.World.Year}, народов {_sim.World.Groups.FindAll(g => g.Alive).Count}, строк ленты {_feedLines.Count}, сил {Spellbook.All.Length}, правок в журнале {_sim.World.AppliedLog.Count}");
            GetTree().Quit();
        }
    }
}
