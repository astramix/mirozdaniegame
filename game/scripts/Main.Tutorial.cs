using System;
using Godot;

namespace Mirozdanie.Game;

/// <summary>
/// Обучение (этап F): шесть карточек при первом мире — что видно, как говорить с народами, чем платить за чудеса,
/// где Летопись, как пустить время. Можно пропустить; пройденное запоминается в user://settings.cfg.
/// </summary>
public partial class Main
{
    private const string SettingsPath = "user://settings.cfg";
    private PanelContainer? _tutorial;
    private int _tutorialStep;

    private static readonly (string title, string text, Vector2 anchor)[] TutorialSteps =
    {
        ("Вы — Создатель",
         "Перед вами мир, где только что пробудился разум. Время стоит, пока вы не решите иначе.\n\nВращайте планету левой кнопкой мыши, приближайте колесом. Клавиша N — ночь: видны огни городов.",
         new Vector2(0.5f, 0.42f)),
        ("Народы и их взгляд на вас",
         "Точки на планете — народы. Цвет — их отношение к вам: любовь, страх, сделка, отрицание, мятеж. Мерцающие — молятся прямо сейчас.\n\nКликните по народу: справа откроется карточка, а кнопка «Говорить» — разговор. У каждого народа свой голос, манера и память о ваших делах.",
         new Vector2(0.62f, 0.4f)),
        ("Силы и их цена",
         "Внизу — Силы Создателя: природа, жизнь, разум, святыни, история, земля. Каждая стоит веры, а вера — ваша сила: она растёт, когда вас любят или боятся.\n\nНо чудо имеет цену: кому всё дают, тот меньше ищет сам. Щедрый бог любим — но его мир медленнее идёт к звёздам.",
         new Vector2(0.4f, 0.7f)),
        ("Слово дороже чуда",
         "В разговоре можно дать обет: «пошлю дождь», «дам победу», «не трону вас». Сдержанный обет рождает любовь и имя «Верный слову». Нарушенный — «Обманщик», и этого не забудут.\n\nНароды и сами просят: победы, исцеления, знака. Их просьбы видны в разговоре и в Летописи.",
         new Vector2(0.5f, 0.5f)),
        ("Летопись",
         "Справа — живая лента событий. Значок » у строки открывает Летопись на этом месте и отвечает на вопрос «почему?» — цепочкой причин.\n\nКнопка «Летопись» сверху — вся история по тысячелетиям и векам, главы, энциклопедия народов, вер и языков, мифы глазами разных народов.",
         new Vector2(0.7f, 0.3f)),
        ("Пусть идёт время",
         "▶ — время пошло. Пробел — пауза, 1–3 — скорость, C — камера-летописец, Esc — отменить силу.\n\nЦели во вкладке «Знаки» необязательны: мир не требует их, но запоминает. Итог мира — кнопка ★ сверху. Удачи, Создатель.",
         new Vector2(0.35f, 0.2f)),
    };

    private bool TutorialDone()
    {
        var cfg = new ConfigFile();
        return cfg.Load(SettingsPath) == Error.Ok && (bool)cfg.GetValue("ui", "tutorial_done", false);
    }

    private void MarkTutorialDone()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue("ui", "tutorial_done", true);
        cfg.Save(SettingsPath);
    }

    /// <summary>Показать обучение (если его ещё не проходили, или принудительно).</summary>
    private void StartTutorial(bool force = false)
    {
        if (!force && (TutorialDone() || _smoke)) return;
        _tutorialStep = 0;
        ShowTutorialStep();
    }

    private void ShowTutorialStep()
    {
        _tutorial?.QueueFree();
        if (_tutorialStep >= TutorialSteps.Length) { _tutorial = null; MarkTutorialDone(); return; }
        var (title, text, anchor) = TutorialSteps[_tutorialStep];
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", AtlasTheme.Box(new Color(AtlasTheme.Paper, 0.97f), AtlasTheme.Gold, 2, 12, 18, 16));
        var size = GetViewport().GetVisibleRect().Size;
        var box = new Vector2(520, 0);
        panel.Position = new Vector2(Math.Clamp(anchor.X * size.X - box.X / 2, 16, size.X - box.X - 16), Math.Clamp(anchor.Y * size.Y - 120, 70, size.Y - 320));
        panel.CustomMinimumSize = box;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        v.AddChild(AtlasTheme.Heading($"{title}  ·  {_tutorialStep + 1}/{TutorialSteps.Length}", 22));
        var body = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) };
        v.AddChild(body);
        var row = new HBoxContainer();
        var skip = new Button { Text = "Пропустить обучение" };
        skip.Pressed += () => { _tutorialStep = TutorialSteps.Length; ShowTutorialStep(); _sound.Play("click"); };
        row.AddChild(skip);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var next = new Button { Text = _tutorialStep == TutorialSteps.Length - 1 ? "Начать" : "Дальше →", CustomMinimumSize = new Vector2(140, 38) };
        next.Pressed += () => { _tutorialStep++; ShowTutorialStep(); _sound.Play("chime", 0.05); };
        row.AddChild(next);
        v.AddChild(row);
        _root.AddChild(panel);
        _tutorial = panel;
    }
}
