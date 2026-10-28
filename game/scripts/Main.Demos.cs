using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

/// <summary>Сохранение и загрузка миров, окно Хроники, настройки звука (docs/design/viewer-2.md, §2, §6).</summary>
/// <summary>Демо-сцены для скриншотов и живых тестов (--demo=…): каждая собирает нужное состояние мира и окна.</summary>
public partial class Main
{
    // ---------- Демонстрации для скриншотов (--demo=…) ----------

    private void RunDemo(string demo)
    {
        if (_sim == null) return;
        var w = _sim.World;
        if (demo is "sculpt" or "smoke") { DemoSculpt(demo == "smoke"); if (demo == "sculpt") return; }
        if (demo is "civ" or "night" or "war") { DemoCivilization(demo); return; }
        for (int i = 0; i < 80; i++) _sim.Step();
        var big = w.Groups.Where(g => g.Alive).OrderByDescending(g => g.Pop).First();
        // Несколько заклинаний, чтобы были видны эффекты и лента.
        foreach (var t in new[] { InterventionType.Rain, InterventionType.Lightning, InterventionType.Bless, InterventionType.Appear })
            CastOnGroup(t, big.Id);
        PumpEvents(false);
        FeedTick(0);
        _globe.Focus(big.Cell);
        switch (demo)
        {
            case "creation": SetMode(Mode.Creation); break;
            case "people": Select(big.Id); break;
            case "prayers": _tabs.CurrentTab = TabPrayers; RefreshPrayers(); break;
            case "spells":
                _sphere = Sphere.Nature; _armed = Spellbook.Get(InterventionType.Lightning);
                for (int i = 0; i < 6; i++) CastOnGroup(InterventionType.Lightning, big.Id);
                RefreshSpellbook();
                break;
            case "dialogue":
                OpenDialogue(big.Id);
                DoDialogue(DialogueAction.Ask);
                DoDialogue(DialogueAction.Bless);
                break;
            case "chronicle": OpenLetopis(); break;
            case "letopis":
            {
                for (int i = 0; i < 900; i++) _sim.Step();
                RefreshAll();
                var log = w.Events.Log;
                // Событие с самой длинной цепочкой причин — для показа «Почему?».
                int battle = -1, best = 0;
                for (int i = log.Count - 1; i >= 0 && i >= log.Count - 20000; i--)
                {
                    int d = 0;
                    for (int c = log[i].Cause; c >= 0 && d < 10; c = log[c].Cause) d++;
                    if (d > best) { best = d; battle = i; }
                }
                OpenLetopis(battle);
                if (battle >= 0) ShowWhy(battle);
                break;
            }
            case "finale":
            {
                for (int i = 0; i < 1300; i++) _sim.Step();
                var pol = w.Polities.Where(p => p.Alive).OrderByDescending(p => p.Members.Sum(m => w.Groups[m].Pop)).First();
                pol.Shield = true;
                var cap = w.Groups[pol.Capital];
                RefreshAll();
                _globe.Focus(cap.Cell);
                _globe.Zoom(1.25f);
                _globe.Probe(cap.Cell, false);
                var other = w.Polities.Where(p => p.Alive && p.Id != pol.Id).Select(p => w.Groups[p.Capital]).FirstOrDefault() ?? cap;
                _globe.Probe(other.Cell, true);
                _globe.Geoglyph(other.Cell);
                _globe.LightBlink(cap.Cell);
                FinaleEventFx(new WorldEvent(w.Events.Log.Count, w.Year, EventType.CreatorWounded, cap.Cell, cap.Id, -1, -1, 0.5f, EventFlags.None, -1));
                break;
            }
            case "contact":
            {
                for (int i = 0; i < 1200; i++) _sim.Step();
                var g = w.Groups.Where(x => x.Alive && x.PolityId >= 0).OrderByDescending(x => x.Pop).First();
                g.Suspicion = 0.9f;
                RefreshAll();
                OpenDialogue(g.Id);
                DoDialogue(DialogueAction.Ask);
                _dOath.Selected = Math.Min(1, _dOath.ItemCount - 1);
                Swear();
                break;
            }
            case "tutorial": RefreshAll(); _tutorialStep = 2; ShowTutorialStep(); break;
            case "outcome": for (int i = 0; i < 1500; i++) _sim.Step(); RefreshAll(); OpenOutcome(); break;
            case "encyclopedia": for (int i = 0; i < 900; i++) _sim.Step(); RefreshAll(); OpenLetopis(); ShowEncyclopedia("Веры", 0); break;
            case "load": SaveWorld(); OpenLoadDialog(); break;
            case "sound": OpenSound(); break;
            case "layers": _layer = "religions"; _mapDirty = true; RedrawMap(); Select(big.Id); break;
            case "terraform":
            case "smoke":
            {
                // Силы Земли у крупнейшего народа: хребет, вулкан, река, перешеек; кисть наготове.
                var p = w.Planet;
                int fx = big.Cell % p.EW * p.Scale + 2, fy = big.Cell / p.EW * p.Scale + 2;
                _sphere = Sphere.Earth;
                ApplyEarth(Spellbook.Get(InterventionType.Ridge), new[] { (fx + 14, fy - 16), (fx + 18, fy + 16) });
                ApplyEarth(Spellbook.Get(InterventionType.Volcano), new[] { (fx - 12, fy + 8) });
                ApplyEarth(Spellbook.Get(InterventionType.River), new[] { (fx + 6, fy - 6) });
                var st = _sim.BeginStroke(BrushMode.Raise, 6, 0.8f, false);
                for (int k = 0; k < 12; k++) _sim.StrokeDab(st, fx - 20 + k, fy - 14);
                _sim.EndStroke(st);
                AfterTerraform();
                _brush = Spellbook.Brushes[0];
                _brushX = fx - 4; _brushY = fy + 2; _brushOnPlanet = true;
                _globe.ShowBrush(_brushX, _brushY, _brushRadius, BrushColor(BrushMode.Raise));
                _globe.BrushDust(_brushX, _brushY, new Color(0.55f, 0.42f, 0.28f, 0.9f), true);
                _armed = null;
                _globe.Focus(big.Cell);
                RefreshSpellbook();
                break;
            }
        }
        RefreshAll();
    }

    /// <summary>Демо цивилизаций: держав, городов, войск (civ/war) и ночных огней (night).</summary>
    private void DemoCivilization(string demo)
    {
        var w = _sim!.World;
        int years = demo == "night" ? 1900 : 1300;
        for (int i = 0; i < years; i++) _sim.Step();
        // Для «войны» — дошагать до года, когда войско в походе.
        if (demo == "war")
            for (int i = 0; i < 400 && !w.Armies.Any(a => a.Alive && !a.Defender); i++) _sim.Step();
        var army = w.Armies.FirstOrDefault(a => a.Alive && !a.Defender);
        var pol = w.Polities.Where(p => p.Alive).OrderByDescending(p => p.Members.Sum(m => w.Groups[m].Pop)).FirstOrDefault();
        int cell = army != null ? w.Groups[army.Group].Cell : pol != null ? w.Groups[pol.Capital].Cell : w.Groups.First(g => g.Alive).Cell;
        _layer = demo == "night" ? "biomes" : "polities";
        _mapDirty = true;
        RefreshAll();
        _globe.Focus(cell);
        if (demo == "night") _globe.ToggleNight();
    }

    /// <summary>Демо лепки до жизни: материк кистью, хребет, остров, предпросмотр перешейка.</summary>
    private void DemoSculpt(bool quick)
    {
        var p = _sim!.World.Planet;
        int cx = p.W / 2, cy = p.H / 2;
        var st = _sim.BeginStroke(BrushMode.Raise, 12, 1f, false);
        for (int k = 0; k < 40; k++) _sim.StrokeDab(st, cx - 40 + k * 2, cy - 10 + (int)(8 * Math.Sin(k * 0.3)));
        _sim.EndStroke(st);
        ApplyEarth(Spellbook.Get(InterventionType.Ridge), new[] { (cx - 30, cy - 20), (cx + 20, cy + 5) });
        ApplyEarth(Spellbook.Get(InterventionType.Island), new[] { (cx + 50, cy + 20) });
        if (quick) { _sim.Undo(); return; }
        _armed = Spellbook.Get(InterventionType.Bridge);
        _lineStart = (cx + 30, cy + 10);
        _globe.ShowLine(cx + 30, cy + 10, cx + 50, cy + 20, AtlasTheme.Gold);
        _globe.Focus(_globe.PickCellFromMap(cx, cy));
        _layer = "elevation";
        RedrawMap();
        RefreshSpellbook();
    }
}
