using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Events;

namespace Mirozdanie.Game;

/// <summary>
/// Окно «Летопись» (docs/design/lore.md, §8, §11): хронология «тысячелетие → век → событие» со сводками,
/// главы (эпохи, державы, веры, войны), энциклопедия с мифами, дерево причин «Почему?», переход к месту на планете.
/// </summary>
public partial class Main
{
    private Letopis? _letopis;
    private ScreenCrack? _crack;

    /// <summary>События финала → эффекты на планете и экране (god-loop.md §6).</summary>
    private void FinaleEventFx(in WorldEvent e)
    {
        var w = _sim!.World;
        switch (e.Type)
        {
            case EventType.ProbeLaunched:
                _globe.Probe(e.Cell, e.B == 1);
                _sound.Play(e.B == 1 ? "boom" : "whoosh", 0.2, e.B == 1 ? 0.6f : 1.2f);
                if (e.B != 1 && e.A >= 0)
                {
                    var L = w.LangOf(w.Groups[e.A]);
                    Toast($"{L.Cyr(Mirozdanie.Core.Language.Concept.People)} {L.Cyr(Mirozdanie.Core.Language.Concept.See, false)} {L.Cyr(Mirozdanie.Core.Language.Concept.God, false)}! — «Мы видим тебя», — говорит зонд народа {w.Groups[e.A].Name.Native}.", 8);
                }
                break;
            case EventType.CreatorWounded:
                if (_crack == null)
                {
                    var layer = new CanvasLayer { Layer = 50 };
                    AddChild(layer);
                    _crack = new ScreenCrack { MouseFilter = Control.MouseFilterEnum.Ignore };
                    _crack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    layer.AddChild(_crack);
                }
                var size = GetViewport().GetVisibleRect().Size;
                _crack.Crack(size / 2 + new Vector2(60, -40), e.Id);
                _sound.Play("rumble", 0.4);
                Toast("Создатель ранен: ракеты достигли неба. Десять лет чудеса будут даваться тяжелее.", 8);
                break;
            case EventType.GeoglyphMade:
                _globe.Geoglyph(e.Cell);
                _sound.Play("chime", 0.2);
                break;
            case EventType.LightMessage:
                _globe.LightBlink(e.Cell);
                break;
            case EventType.ShieldRaised:
                _sound.Play("gong", 0.2, 1.4f);
                break;
        }
    }
    private RichTextLabel _lDetail = null!;
    private int _lEvent = -1, _lCell = -1;
    private Button _lWhy = null!, _lShow = null!;
    private TabContainer _lTabs = null!;
    private OptionButton _eKind = null!;
    private ItemList _eList = null!;

    /// <summary>Открыть энциклопедию на разделе и записи (для демо и ссылок).</summary>
    private void ShowEncyclopedia(string kind, int item)
    {
        _lTabs.CurrentTab = 2;
        int k = Array.IndexOf(Encyclopedia.Kinds, kind);
        if (k < 0) return;
        _eKind.Select(k);
        _eKind.EmitSignal(OptionButton.SignalName.ItemSelected, k);
        if (item < _eList.ItemCount) { _eList.Select(item); _eList.EmitSignal(ItemList.SignalName.ItemSelected, item); }
    }

    private void OpenLetopis(int focusEvent = -1)
    {
        if (_sim == null) return;
        _speed = 0;
        RefreshTop();
        var w = _sim.World;
        var writer = new ChronicleWriter(w);
        _letopis = new Letopis(w, writer);
        var v = Modal($"Летопись мира {w.WorldName.Native}", new Vector2(1280, 820));
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _lTabs = tabs;
        v.AddChild(tabs);

        // Правая панель подробностей — общая для вкладок.
        _lDetail = new RichTextLabel { BbcodeEnabled = true, SelectionEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _lDetail.AddThemeStyleboxOverride("normal", AtlasTheme.Box(AtlasTheme.PaperDeep, AtlasTheme.GoldDim, 1, 8, 0, 16));
        _lDetail.MetaClicked += meta => OnLetopisLink(meta.AsString());

        tabs.AddChild(BuildTimeline(w));
        tabs.AddChild(BuildChapters());
        tabs.AddChild(BuildEncyclopedia(w, writer));
        tabs.AddChild(BuildChronicleText(w));
        tabs.SetTabTitle(0, "Хронология");
        tabs.SetTabTitle(1, "Главы");
        tabs.SetTabTitle(2, "Энциклопедия");
        tabs.SetTabTitle(3, "Хроника целиком");

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(560, 0) };
        right.AddChild(_lDetail);
        var row = new HBoxContainer();
        _lWhy = new Button { Text = "Почему?", CustomMinimumSize = new Vector2(0, 36), TooltipText = "Дерево причин: что к этому привело и что из этого вышло", Disabled = true };
        _lWhy.Pressed += () => ShowWhy(_lEvent);
        _lShow = new Button { Text = "Показать на планете", CustomMinimumSize = new Vector2(0, 36), Disabled = true };
        _lShow.Pressed += () => { if (_lCell >= 0) { CloseModal(); _globe.Focus(_lCell); } };
        row.AddChild(_lWhy);
        row.AddChild(_lShow);
        right.AddChild(row);
        // Панель подробностей переезжает в активную вкладку.
        void Attach(int tab)
        {
            if (right.GetParent() != null) right.GetParent().RemoveChild(right);
            if (tabs.GetChild(tab) is HSplitContainer hs) hs.AddChild(right);
        }
        tabs.TabChanged += t => Attach((int)t);
        Attach(0);
        if (focusEvent >= 0) ShowEvent(focusEvent);
        else _lDetail.Text = "[i]Выберите тысячелетие, век или событие слева. Значимые события видны и в свёрнутых узлах.[/i]";
    }

    // ---------- Хронология ----------

    private Control BuildTimeline(Mirozdanie.Core.Sim.World w)
    {
        var hs = new HSplitContainer();
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        var filters = new HBoxContainer();
        var theme = new OptionButton();
        string[] themes = { "все темы", "войны", "вера", "природа", "Создатель", "открытия", "держава", "народы" };
        foreach (var t in themes) theme.AddItem(t);
        var search = new LineEdit { PlaceholderText = "поиск по имени…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        filters.AddChild(theme);
        filters.AddChild(search);
        left.AddChild(filters);
        var tree = new Tree { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HideRoot = true };
        left.AddChild(tree);
        hs.AddChild(left);
        var meta = new Dictionary<TreeItem, (string kind, long a, long b)>();
        void Fill()
        {
            tree.Clear();
            meta.Clear();
            var root = tree.CreateItem();
            var th = (EventTheme)theme.Selected;
            string q = search.Text.Trim();
            foreach (var m in _letopis!.Millennia())
            {
                var mi = tree.CreateItem(root);
                mi.SetText(0, m.Title);
                mi.SetCustomColor(0, AtlasTheme.Gold);
                meta[mi] = ("m", m.From, m.To);
                bool current = w.Year >= m.From && w.Year <= m.To;
                mi.Collapsed = !current;
                foreach (var c in _letopis.Centuries(m.From))
                {
                    var ci = tree.CreateItem(mi);
                    meta[ci] = ("c", c.From, c.To);
                    // Значимые события видны и в свёрнутом узле (lore.md §11).
                    var hi = c.Highlights.Take(2).Select(id => $"{Letopis.Icon(w.Events.Get(id).Type)} {w.Events.Get(id).Year}").ToList();
                    ci.SetText(0, c.Title + (hi.Count > 0 ? "   " + string.Join("  ", hi) : ""));
                    ci.Collapsed = !(current && w.Year >= c.From && w.Year <= c.To);
                    var evs = _letopis.Events(c.From, c.To, q.Length > 0 ? 3f : 8f, th);
                    if (q.Length > 0) evs = evs.Where(id => _letopis.Text(id).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                    else if (evs.Count > 40) evs = _letopis.Top(c.From, c.To, 40, th);
                    foreach (var id in evs)
                    {
                        var e = w.Events.Get(id);
                        var ei = tree.CreateItem(ci);
                        string text = _letopis.Text(id);
                        ei.SetText(0, $"{Letopis.Icon(e.Type)} {e.Year} — {Strip(text, 110)}");
                        ei.SetTooltipText(0, text);
                        if (_letopis.Weight(id) >= Letopis.Highlight) ei.SetCustomColor(0, new Color(1f, 0.86f, 0.62f));
                        if (e.ByCreator) ei.SetCustomColor(0, new Color(0.62f, 0.8f, 1f));
                        meta[ei] = ("e", id, 0);
                    }
                    if (q.Length > 0 && evs.Count > 0) { ci.Collapsed = false; mi.Collapsed = false; }
                }
            }
        }
        tree.ItemSelected += () =>
        {
            var it = tree.GetSelected();
            if (it == null || !meta.TryGetValue(it, out var m)) return;
            if (m.kind == "e") ShowEvent((int)m.a);
            else ShowPeriod(m.a, m.b, m.kind == "m");
        };
        theme.ItemSelected += _ => Fill();
        search.TextSubmitted += _ => Fill();
        Fill();
        return hs;
    }

    private static string Strip(string s, int n)
    {
        s = s.Replace("*", "");
        return s.Length > n ? s[..n] + "…" : s;
    }

    private void ShowPeriod(long from, long to, bool millennium)
    {
        var w = _sim!.World;
        string sum = _letopis!.Summary(from, to, millennium);
        var top = _letopis.Top(from, to, millennium ? 8 : 6);
        var sb = new System.Text.StringBuilder();
        sb.Append($"[font_size=22][color=#d9b45a]Годы {from}–{to}[/color][/font_size]\n\n{sum}\n\n[b]Главное:[/b]\n");
        foreach (var id in top) sb.Append($"{Letopis.Icon(w.Events.Get(id).Type)} [url=ev:{id}]{w.Events.Get(id).Year}[/url] — {Md(_letopis.Text(id))}\n");
        _lDetail.Text = sb.ToString();
        _lEvent = -1; _lWhy.Disabled = true;
        _lCell = top.Count > 0 ? w.Events.Get(top[0]).Cell : -1;
        _lShow.Disabled = _lCell < 0;
    }

    private static string Md(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\*\*(.+?)\*\*", "[b]$1[/b]").Replace("*", "");

    private void ShowEvent(int id)
    {
        var w = _sim!.World;
        var e = w.Events.Get(id);
        _lEvent = id;
        _lCell = e.Cell;
        _lWhy.Disabled = false;
        _lShow.Disabled = e.Cell < 0;
        string ctx = _letopis!.Context(id);
        _lDetail.Text = $"[font_size=22][color=#d9b45a]Год {e.Year}[/color][/font_size]\n\n{Md(_letopis.Text(id))}\n\n" +
                        (e.Cause >= 0 ? $"[i]Причина:[/i] [url=ev:{e.Cause}]{Md(_letopis.Text(e.Cause))}[/url]\n\n" : "") +
                        (ctx.Length > 0 ? $"[color=#a9a28f]{ctx}[/color]" : "");
    }

    /// <summary>Дерево причин: от события назад, у каждого звена — прямые последствия.</summary>
    private void ShowWhy(int id)
    {
        if (id < 0) return;
        var node = _letopis!.Why(id);
        var sb = new System.Text.StringBuilder("[font_size=22][color=#d9b45a]Почему?[/color][/font_size]\n\n");
        var chain = new List<CauseNode>();
        for (var n = node; n != null; n = n.Cause) chain.Add(n);
        chain.Reverse();
        for (int k = 0; k < chain.Count; k++)
        {
            var n = chain[k];
            string pad = new string(' ', k * 3);
            sb.Append($"{pad}{(k == 0 ? "◆" : "└→")} [url=ev:{n.Event}]{Md(n.Text)}[/url]\n");
            foreach (var (ev, text) in n.Consequences.Where(c => k + 1 >= chain.Count || c.ev != chain[k + 1].Event).Take(4))
                sb.Append($"{pad}    [color=#a9a28f]· и ещё: [url=ev:{ev}]{Md(Strip(text, 120))}[/url][/color]\n");
        }
        if (chain.Count == 1)
        {
            string ctx = _letopis.Context(id);
            sb.Append(ctx.Length > 0 ? $"\n[i]Явной причины нет в записях. {ctx}[/i]" : "\n[i]Явной причины нет: так сложилось само.[/i]");
        }
        _lDetail.Text = sb.ToString();
    }

    private void OnLetopisLink(string meta)
    {
        if (meta.StartsWith("ev:") && int.TryParse(meta[3..], out int id)) ShowEvent(id);
    }

    // ---------- Главы ----------

    private Control BuildChapters()
    {
        var hs = new HSplitContainer();
        var list = new ItemList { CustomMinimumSize = new Vector2(560, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        hs.AddChild(list);
        var chapters = _letopis!.Chapters();
        foreach (var c in chapters) list.AddItem($"{c.Kind}: {c.Title.Replace(c.Kind + ": ", "")}");
        list.ItemSelected += i =>
        {
            var c = chapters[(int)i];
            var w = _sim!.World;
            var sb = new System.Text.StringBuilder($"[font_size=22][color=#d9b45a]{c.Title}[/color][/font_size]\n[color=#a9a28f]Годы {c.From}–{c.To}[/color]\n\n{Md(c.Intro)}\n\n");
            foreach (var id in c.Events) sb.Append($"{Letopis.Icon(w.Events.Get(id).Type)} [url=ev:{id}]{w.Events.Get(id).Year}[/url] — {Md(_letopis.Text(id))}\n");
            _lDetail.Text = sb.ToString();
            _lCell = c.Cell; _lShow.Disabled = c.Cell < 0; _lEvent = -1; _lWhy.Disabled = true;
        };
        return hs;
    }

    // ---------- Энциклопедия ----------

    private Control BuildEncyclopedia(Mirozdanie.Core.Sim.World w, ChronicleWriter writer)
    {
        var hs = new HSplitContainer();
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        var kind = new OptionButton();
        foreach (var k in Encyclopedia.Kinds) kind.AddItem(k);
        left.AddChild(kind);
        _eKind = kind;
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _eList = list;
        left.AddChild(list);
        hs.AddChild(left);
        List<Entry> entries = new();
        void Fill()
        {
            list.Clear();
            entries = Encyclopedia.List(w, _letopis!, writer, Encyclopedia.Kinds[kind.Selected]);
            foreach (var e in entries) list.AddItem($"{e.Title} — {e.Subtitle}");
        }
        list.ItemSelected += i =>
        {
            var e = entries[(int)i];
            var sb = new System.Text.StringBuilder($"[font_size=22][color=#d9b45a]{e.Title}[/color][/font_size]\n[color=#a9a28f]{e.Subtitle}[/color]\n\n");
            sb.Append(e.Body.Replace("✦", "[color=#d9b45a]✦[/color]").Replace("на самом деле:", "[color=#8fc3ff]на самом деле:[/color]"));
            if (e.Events.Count > 0)
            {
                sb.Append("\n[b]В Летописи:[/b]\n");
                foreach (var id in e.Events) sb.Append($"{Letopis.Icon(w.Events.Get(id).Type)} [url=ev:{id}]{w.Events.Get(id).Year}[/url] — {Md(Strip(_letopis!.Text(id), 160))}\n");
            }
            _lDetail.Text = sb.ToString();
            _lCell = e.Cell; _lShow.Disabled = e.Cell < 0; _lEvent = -1; _lWhy.Disabled = true;
        };
        kind.ItemSelected += _ => Fill();
        Fill();
        return hs;
    }

    // ---------- Хроника целиком (Markdown) ----------

    private Control BuildChronicleText(Mirozdanie.Core.Sim.World w)
    {
        var hs = new HSplitContainer();
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(560, 0) };
        string md = new ChronicleWriter(w).Write();
        var text = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectionEnabled = true };
        text.AddThemeStyleboxOverride("normal", AtlasTheme.Box(AtlasTheme.PaperDeep, AtlasTheme.GoldDim, 1, 8, 0, 18));
        text.Text = MarkdownToBbcode(md);
        v.AddChild(text);
        var save = new Button { Text = "Сохранить в файл (Markdown)", CustomMinimumSize = new Vector2(0, 38) };
        save.Pressed += () =>
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ChroniclesDir));
            string file = $"{ChroniclesDir}/{SafeName(w.WorldName.Native)}_{w.Seed}_y{w.Year}.md";
            using (var f = FileAccess.Open(file, FileAccess.ModeFlags.Write)) f?.StoreString(md);
            _sound.Play("bell", 0);
            Toast("Хроника записана: " + ProjectSettings.GlobalizePath(file));
        };
        v.AddChild(save);
        hs.AddChild(v);
        return hs;
    }
}

/// <summary>Трещина на экране после ракетного удара по Создателю (god-loop.md §6) — тает за несколько секунд.</summary>
public partial class ScreenCrack : Control
{
    private readonly List<(Vector2 a, Vector2 b, float w)> _lines = new();
    private double _t;
    public double Life = 7;

    public void Crack(Vector2 center, int seed)
    {
        _lines.Clear();
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        for (int k = 0; k < 9; k++)
        {
            float ang = k * Mathf.Tau / 9 + rng.RandfRange(-0.25f, 0.25f);
            var p = center;
            float len = rng.RandfRange(120, 420);
            int steps = 7;
            for (int s = 0; s < steps; s++)
            {
                var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (len / steps) + new Vector2(rng.RandfRange(-14, 14), rng.RandfRange(-14, 14));
                _lines.Add((p, q, 3.5f - s * 0.4f));
                if (rng.Randf() < 0.3f) _lines.Add((q, q + new Vector2(rng.RandfRange(-60, 60), rng.RandfRange(-60, 60)), 1.2f));
                p = q;
            }
        }
        _t = 0;
        Visible = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += delta;
        if (_t >= Life) { Visible = false; return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float a = (float)Math.Clamp(1.0 - _t / Life, 0, 1);
        foreach (var (p, q, w) in _lines)
        {
            DrawLine(p, q, new Color(1f, 1f, 1f, 0.75f * a), w + 1.5f, true);
            DrawLine(p, q, new Color(0.15f, 0.05f, 0.05f, 0.9f * a), w * 0.5f, true);
        }
    }
}
