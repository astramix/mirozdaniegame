using Godot;

namespace Mirozdanie.Game;

/// <summary>
/// Тема «Живой атлас» (PLAN.md, «Визуальная идентичность»): тёмно-синяя бумага, тонкие золотые рамки,
/// шрифты с засечками из системы. Всё строится кодом, внешних ассетов нет.
/// </summary>
public static class AtlasTheme
{
    public static readonly Color Paper = new("101a33");
    public static readonly Color PaperLight = new("1a2850");
    public static readonly Color PaperDeep = new("0a1124");
    public static readonly Color Gold = new("d9b45a");
    public static readonly Color GoldDim = new("8a7440");
    public static readonly Color Ink = new("ece3cc");
    public static readonly Color InkDim = new("a9a28f");
    public static readonly Color Sky = new("8fc3ff");
    public static readonly Color Blood = new("e0634f");
    public static readonly Color Leaf = new("8bc47a");

    public static Font Serif = null!;
    /// <summary>Стили кнопок по состояниям — для кнопок с иконкой, которым нужен свой отступ слева.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, StyleBox> ButtonStyles = new();
    public static Font Title = null!;

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int radius = 8, int shadow = 6, int pad = 10)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg, BorderColor = border, ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = shadow,
            ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad * 0.7f, ContentMarginBottom = pad * 0.7f,
            AntiAliasing = true,
        };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(radius);
        return s;
    }

    public static Theme Build()
    {
        Serif = new SystemFont { FontNames = new[] { "PT Serif", "Georgia", "Noto Serif", "DejaVu Serif", "Liberation Serif", "Times New Roman", "serif" }, Antialiasing = TextServer.FontAntialiasing.Gray };
        Title = new SystemFont { FontNames = new[] { "PT Serif Caption", "Georgia", "Noto Serif Display", "DejaVu Serif", "serif" }, FontWeight = 700 };
        var t = new Theme { DefaultFont = Serif, DefaultFontSize = 16 };

        var panel = Box(new Color(Paper, 0.94f), GoldDim, 1, 10, 10, 12);
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "Panel", panel);

        var btn = Box(PaperLight, GoldDim, 1, 6, 2, 8);
        var btnHover = Box(new Color("26386a"), Gold, 1, 6, 4, 8);
        var btnPressed = Box(new Color("314a8a"), Gold, 2, 6, 2, 8);
        var btnDisabled = Box(new Color(PaperLight, 0.5f), new Color(GoldDim, 0.4f), 1, 6, 0, 8);
        ButtonStyles["normal"] = btn; ButtonStyles["hover"] = btnHover; ButtonStyles["pressed"] = btnPressed;
        ButtonStyles["hover_pressed"] = btnPressed; ButtonStyles["disabled"] = btnDisabled;
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton", "CheckBox" })
        {
            t.SetStylebox("normal", type, btn);
            t.SetStylebox("hover", type, btnHover);
            t.SetStylebox("pressed", type, btnPressed);
            t.SetStylebox("hover_pressed", type, btnPressed);
            t.SetStylebox("disabled", type, btnDisabled);
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_hover_color", type, new Color("fff3cf"));
            t.SetColor("font_pressed_color", type, Gold);
            t.SetColor("font_disabled_color", type, InkDim);
        }
        t.SetStylebox("normal", "CheckBox", new StyleBoxEmpty());
        t.SetStylebox("hover", "CheckBox", new StyleBoxEmpty());
        t.SetStylebox("pressed", "CheckBox", new StyleBoxEmpty());

        t.SetColor("font_color", "Label", Ink);
        t.SetColor("default_color", "RichTextLabel", Ink);
        t.SetFont("normal_font", "RichTextLabel", Serif);
        t.SetFontSize("normal_font_size", "RichTextLabel", 15);
        t.SetFont("italics_font", "RichTextLabel", new FontVariation { BaseFont = Serif, VariationTransform = new Transform2D(1, 0.2f, 0, 1, 0, 0) });
        t.SetFont("bold_font", "RichTextLabel", new FontVariation { BaseFont = Serif, VariationEmbolden = 0.8f });

        var field = Box(PaperDeep, GoldDim, 1, 5, 0, 8);
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", Box(PaperDeep, Gold, 1, 5, 0, 8));
        t.SetColor("font_color", "LineEdit", Ink);

        var tab = Box(Paper, GoldDim, 1, 6, 0, 10);
        t.SetStylebox("tab_selected", "TabContainer", Box(PaperLight, Gold, 1, 6, 0, 10));
        t.SetStylebox("tab_unselected", "TabContainer", tab);
        t.SetStylebox("tab_hovered", "TabContainer", Box(new Color("26386a"), Gold, 1, 6, 0, 10));
        t.SetStylebox("panel", "TabContainer", Box(new Color(Paper, 0.94f), GoldDim, 1, 8, 8, 10));
        t.SetColor("font_selected_color", "TabContainer", Gold);
        t.SetColor("font_unselected_color", "TabContainer", InkDim);
        t.SetColor("font_hovered_color", "TabContainer", Ink);

        var slider = new StyleBoxFlat { BgColor = PaperDeep, ContentMarginTop = 3, ContentMarginBottom = 3 };
        slider.SetCornerRadiusAll(3);
        t.SetStylebox("slider", "HSlider", slider);
        var fill = new StyleBoxFlat { BgColor = GoldDim, ContentMarginTop = 3, ContentMarginBottom = 3 };
        fill.SetCornerRadiusAll(3);
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", fill);

        t.SetStylebox("panel", "ItemList", Box(PaperDeep, GoldDim, 1, 6, 0, 6));
        t.SetColor("font_color", "ItemList", Ink);
        t.SetStylebox("selected", "ItemList", Box(new Color("314a8a"), Gold, 1, 4, 0, 4));
        t.SetStylebox("selected_focus", "ItemList", Box(new Color("314a8a"), Gold, 1, 4, 0, 4));

        var bar = new StyleBoxFlat { BgColor = PaperDeep };
        bar.SetCornerRadiusAll(4);
        t.SetStylebox("background", "ProgressBar", bar);
        var barFill = new StyleBoxFlat { BgColor = Gold };
        barFill.SetCornerRadiusAll(4);
        t.SetStylebox("fill", "ProgressBar", barFill);
        t.SetColor("font_color", "ProgressBar", Ink);

        t.SetStylebox("panel", "PopupMenu", Box(Paper, Gold, 1, 6, 6, 6));
        t.SetColor("font_color", "PopupMenu", Ink);
        t.SetStylebox("panel", "TooltipPanel", Box(PaperDeep, Gold, 1, 6, 4, 8));
        t.SetColor("font_color", "TooltipLabel", Ink);
        t.SetStylebox("grabber", "VScrollBar", Box(GoldDim, GoldDim, 0, 4, 0, 2));
        t.SetStylebox("scroll", "VScrollBar", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.15f) });
        return t;
    }

    /// <summary>Заголовок раздела: золотой шрифт с засечками.</summary>
    public static Label Heading(string text, int size = 20)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", Title);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Gold);
        return l;
    }

    public static Label Small(string text, Color? color = null)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", color ?? InkDim);
        return l;
    }
}
