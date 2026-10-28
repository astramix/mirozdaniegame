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
public partial class Main
{
    private const string WorldsDir = "user://worlds";
    private const string ChroniclesDir = "user://chronicles";
    private Control? _modal;

    private void BuildMenus() { }

    /// <summary>Модальное окно поверх всего: затемнение, панель с заголовком и крестиком.</summary>
    private VBoxContainer Modal(string title, Vector2 size)
    {
        CloseModal();
        var overlay = new Control();
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true }) CloseModal(); };
        overlay.AddChild(dim);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", AtlasTheme.Box(new Color(AtlasTheme.Paper, 0.98f), AtlasTheme.Gold, 2, 12, 16, 16));
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -size.X / 2; panel.OffsetRight = size.X / 2; panel.OffsetTop = -size.Y / 2; panel.OffsetBottom = size.Y / 2;
        overlay.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        var head = new HBoxContainer();
        head.AddChild(AtlasTheme.Heading(title, 24));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(36, 32) };
        close.Pressed += () => { _sound.Play("click"); CloseModal(); };
        head.AddChild(close);
        v.AddChild(head);
        _root.AddChild(overlay);
        _modal = overlay;
        _sound.Play("whoosh", 0);
        return v;
    }

    private void CloseModal()
    {
        _modal?.QueueFree();
        _modal = null;
    }

    // ---------- Сохранение ----------

    private static string SafeName(string s)
    {
        var clean = Regex.Replace(s, @"[^\p{L}\p{Nd}_-]+", "_").Trim('_');
        return string.IsNullOrEmpty(clean) ? "world" : clean;
    }

    private void SaveWorld()
    {
        if (_sim == null) return;
        var w = _sim.World;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(WorldsDir));
        // Время сохранения — только для списка в зрителе; в симуляцию оно не попадает.
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var save = _sim.Save(stamp);
        string file = $"{WorldsDir}/{SafeName(w.WorldName.Native)}_{w.Seed}_y{w.Year}.json";
        using (var f = FileAccess.Open(file, FileAccess.ModeFlags.Write))
        {
            if (f == null) { Toast("Не удалось сохранить: " + FileAccess.GetOpenError()); return; }
            f.StoreString(save.ToJson());
        }
        _sound.Play("bell", 0);
        Toast($"Мир {w.WorldName.Native}, год {w.Year}, сохранён.\n{ProjectSettings.GlobalizePath(file)}");
    }

    private static List<(string path, WorldSave save)> ListSaves()
    {
        var list = new List<(string, WorldSave)>();
        string abs = ProjectSettings.GlobalizePath(WorldsDir);
        if (!DirAccess.DirExistsAbsolute(abs)) return list;
        foreach (var name in DirAccess.GetFilesAt(abs))
        {
            if (!name.EndsWith(".json")) continue;
            string path = $"{WorldsDir}/{name}";
            try { list.Add((path, WorldSave.FromJson(FileAccess.GetFileAsString(path)))); }
            catch (Exception) { /* битый файл просто не показываем */ }
        }
        return list.OrderByDescending(x => x.Item2.SavedAt, StringComparer.Ordinal).ToList();
    }

    private void OpenLoadDialog()
    {
        var v = Modal("Сохранённые миры", new Vector2(720, 520));
        var saves = ListSaves();
        if (saves.Count == 0)
        {
            v.AddChild(AtlasTheme.Small("Пока нет сохранений. В мире нажмите «Сохранить» вверху — мир запишется сюда:\n" + ProjectSettings.GlobalizePath(WorldsDir), AtlasTheme.Ink));
            return;
        }
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 340) };
        foreach (var (_, s) in saves)
            list.AddItem($"{s.WorldName} «{s.WorldGloss}» · год {s.Year} · {s.Params.PresetTitle} · зерно {s.Seed}{(s.Sandbox ? " · песочница" : "")} · {s.SavedAt}");
        list.Select(0);
        v.AddChild(list);
        v.AddChild(AtlasTheme.Small("Мир хранится как зерно, настройки и журнал ваших вмешательств; при загрузке история прогоняется заново и выходит той же самой."));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var load = new Button { Text = "Загрузить", CustomMinimumSize = new Vector2(140, 40) };
        load.Pressed += () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0) return;
            var save = saves[sel[0]].save;
            CloseModal();
            LoadWorld(save);
        };
        list.ItemActivated += i => { CloseModal(); LoadWorld(saves[(int)i].save); };
        var del = new Button { Text = "Удалить", CustomMinimumSize = new Vector2(120, 40) };
        del.Pressed += () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0) return;
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(saves[sel[0]].path));
            _sound.Play("dark", 0);
            OpenLoadDialog();
        };
        var folder = new Button { Text = "Открыть папку", CustomMinimumSize = new Vector2(150, 40) };
        folder.Pressed += () => OS.ShellOpen(ProjectSettings.GlobalizePath(WorldsDir));
        row.AddChild(load); row.AddChild(del); row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); row.AddChild(folder);
        v.AddChild(row);
    }

    // ---------- Хроника ----------

    private static string MarkdownToBbcode(string md)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in md.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            line = line.Replace("[", "[lb]");
            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "[b]$1[/b]");
            line = Regex.Replace(line, @"\*(.+?)\*", "[i][color=#8fc3ff]$1[/color][/i]");
            if (line.StartsWith("# ")) line = $"[font_size=30][color=#d9b45a]{line[2..]}[/color][/font_size]";
            else if (line.StartsWith("## ")) line = $"\n[font_size=24][color=#d9b45a]{line[3..]}[/color][/font_size]";
            else if (line.StartsWith("### ")) line = $"[font_size=19][color=#e8d9a8]{line[4..]}[/color][/font_size]";
            else if (line.StartsWith("- ")) line = "• " + line[2..];
            else if (line.StartsWith("  - ")) line = "     ◦ " + line[4..];
            else if (line.StartsWith("> ")) line = $"[i][color=#a9a28f]{line[2..]}[/color][/i]";
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private void OpenChronicle()
    {
        if (_sim == null) return;
        _speed = 0;
        RefreshTop();
        string md = _writer!.Write();
        var v = Modal("Хроника мира", new Vector2(980, 760));
        var text = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectionEnabled = true };
        text.AddThemeStyleboxOverride("normal", AtlasTheme.Box(AtlasTheme.PaperDeep, AtlasTheme.GoldDim, 1, 8, 0, 18));
        text.Text = MarkdownToBbcode(md);
        v.AddChild(text);
        var row = new HBoxContainer();
        var save = new Button { Text = "Сохранить в файл (Markdown)", CustomMinimumSize = new Vector2(0, 38) };
        save.Pressed += () =>
        {
            var w = _sim.World;
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ChroniclesDir));
            string file = $"{ChroniclesDir}/{SafeName(w.WorldName.Native)}_{w.Seed}_y{w.Year}.md";
            using (var f = FileAccess.Open(file, FileAccess.ModeFlags.Write)) f?.StoreString(md);
            _sound.Play("bell", 0);
            Toast("Хроника записана: " + ProjectSettings.GlobalizePath(file));
        };
        row.AddChild(save);
        v.AddChild(row);
    }

    // ---------- Звук ----------

    private void OpenSound()
    {
        var v = Modal("Звук", new Vector2(520, 280));
        HSlider Vol(string title, float value, Action<float> set)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = title, CustomMinimumSize = new Vector2(140, 0) });
            var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 24) };
            s.ValueChanged += x => { set((float)x); _sound.ApplyVolumes(); };
            row.AddChild(s);
            v.AddChild(row);
            return s;
        }
        Vol("Музыка", _sound.MusicVolume, x => _sound.MusicVolume = x);
        Vol("Эффекты", _sound.EffectsVolume, x => { _sound.EffectsVolume = x; _sound.Play("chime", 0.2); });
        var mute = new CheckBox { Text = "Выключить весь звук", ButtonPressed = _sound.Muted };
        mute.Toggled += on => { _sound.Muted = on; _sound.ApplyVolumes(); };
        v.AddChild(mute);
        v.AddChild(AtlasTheme.Small("Все звуки и музыка синтезируются кодом при запуске — внешних файлов нет."));
    }
}
