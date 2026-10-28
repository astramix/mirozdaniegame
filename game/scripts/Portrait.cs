using System;
using Godot;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Output;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

/// <summary>
/// Процедурный портрет народа: голова и глаза — из генома разумного вида, одежда — из культуры,
/// знак на груди — из веры, фон — из биома родины. Моргает и дышит.
/// </summary>
public partial class Portrait : Control
{
    private float[] _genome = new float[Genome.G];
    private Color _skin, _cloth, _bg, _bg2, _accent, _eyeColor;
    private int _eyes = 2;
    private float _headW = 1, _headH = 1;
    private bool _beak, _horns, _crown;
    private DeityForm _form = DeityForm.None;
    private bool _rebel;
    private float _mood = 0.5f;
    private double _time;
    private double _nextBlink = 2;

    public static Color ToColor(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    public void Show(World w, Group g)
    {
        var sp = w.SapientSpecies >= 0 ? w.Life.Species[w.SapientSpecies] : w.Life.Species[0];
        Array.Copy(sp.Genome, _genome, Genome.G);
        float hue = _genome[Trait.Hue];
        _skin = Color.FromHsv(hue, 0.35f + 0.25f * _genome[Trait.Diet], 0.55f + 0.3f * _genome[Trait.Senses]);
        _eyeColor = Color.FromHsv((hue + 0.5f) % 1f, 0.6f, 0.95f);
        _eyes = 1 + (int)Math.Round(_genome[Trait.Eyes] * 3);
        _headW = 0.8f + 0.25f * Math.Clamp(_genome[Trait.Mass] / 3.5f, 0, 1);
        _headH = 1.05f - 0.15f * _genome[Trait.Speed];
        _beak = _genome[Trait.Lips] < 0.3f;
        _horns = _genome[Trait.Diet] > 0.6f || _genome[Trait.Limbs] > 0.75f;
        // Одежда: агрессивные — красное, набожные — светлое, остальное — от языка (своя палитра народа).
        float agg = g.Culture[CultureTrait.Aggression], piety = g.Piety;
        var tribe = ToColor(MapRenderer.Palette(g.LanguageId * 3 + 1));
        _cloth = tribe.Lerp(new Color("8b2c2c"), Math.Clamp(agg - 0.4f, 0, 1)).Lerp(new Color("e8dcc0"), Math.Clamp(piety - 0.6f, 0, 1));
        _accent = g.ReligionId >= 0 ? ToColor(MapRenderer.Palette(g.ReligionId)) : AtlasTheme.GoldDim;
        _form = g.ReligionId >= 0 ? w.Religions[g.ReligionId].Form : DeityForm.None;
        _rebel = g.ReligionId >= 0 && w.Religions[g.ReligionId].Rebel || g.Attitude == Attitude.Rebellion;
        _crown = g.PolityId >= 0 && w.Polities[g.PolityId].Capital == g.Id;
        var biome = w.Planet.EBiome[g.Cell];
        _bg = ToColor(MapRenderer.BiomeColor(biome)).Darkened(0.45f);
        _bg2 = _bg.Lightened(0.25f);
        _mood = Mirozdanie.Core.Creator.Dialogue.Mood(g);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    private void Ellipse(Vector2 c, float rx, float ry, Color col)
    {
        DrawSetTransform(c, 0, new Vector2(rx, ry));
        DrawCircle(Vector2.Zero, 1, col);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    public override void _Draw()
    {
        var s = Size;
        float u = Math.Min(s.X, s.Y);
        // Фон — небо и земля родины.
        DrawRect(new Rect2(Vector2.Zero, s), _bg);
        DrawRect(new Rect2(0, s.Y * 0.62f, s.X, s.Y * 0.38f), _bg2);
        for (int i = 0; i < 6; i++) DrawCircle(new Vector2(s.X * (0.1f + 0.17f * i), s.Y * 0.62f), u * (0.08f + 0.03f * (i % 3)), _bg2);
        float breathe = 1 + 0.012f * (float)Math.Sin(_time * 1.6);
        var center = new Vector2(s.X * 0.5f, s.Y * 0.47f);
        // Плечи и одежда.
        var sh = new[]
        {
            new Vector2(s.X * 0.12f, s.Y), new Vector2(s.X * 0.22f, s.Y * 0.74f * breathe), new Vector2(s.X * 0.78f, s.Y * 0.74f * breathe), new Vector2(s.X * 0.88f, s.Y),
        };
        DrawColoredPolygon(sh, _cloth);
        DrawPolyline(new[] { sh[1], sh[2] }, _cloth.Lightened(0.25f), 3, true);
        // Знак веры на груди.
        var chest = new Vector2(s.X * 0.5f, s.Y * 0.86f);
        float cr = u * 0.07f;
        switch (_form)
        {
            case DeityForm.OneCreator: DrawCircle(chest, cr, _accent); DrawCircle(chest, cr * 0.45f, _cloth); break;
            case DeityForm.Pantheon: for (int i = 0; i < 3; i++) DrawCircle(chest + new Vector2((i - 1) * cr * 1.3f, 0), cr * 0.5f, _accent); break;
            case DeityForm.Ancestors: DrawColoredPolygon(new[] { chest + new Vector2(0, -cr), chest + new Vector2(cr, cr), chest + new Vector2(-cr, cr) }, _accent); break;
            case DeityForm.Nature: DrawArc(chest, cr, 0, Mathf.Tau, 16, _accent, 3, true); DrawLine(chest + new Vector2(0, -cr), chest + new Vector2(0, cr), _accent, 3); break;
        }
        if (_rebel) DrawLine(chest + new Vector2(-cr, -cr), chest + new Vector2(cr, cr), AtlasTheme.Blood, 4, true);
        // Шея и голова.
        DrawRect(new Rect2(center.X - u * 0.07f, center.Y + u * 0.12f, u * 0.14f, u * 0.16f), _skin.Darkened(0.15f));
        float hw = u * 0.2f * _headW, hh = u * 0.24f * _headH;
        if (_horns)
        {
            DrawColoredPolygon(new[] { center + new Vector2(-hw * 0.7f, -hh * 0.6f), center + new Vector2(-hw * 1.2f, -hh * 1.45f), center + new Vector2(-hw * 0.3f, -hh * 0.85f) }, _skin.Darkened(0.35f));
            DrawColoredPolygon(new[] { center + new Vector2(hw * 0.7f, -hh * 0.6f), center + new Vector2(hw * 1.2f, -hh * 1.45f), center + new Vector2(hw * 0.3f, -hh * 0.85f) }, _skin.Darkened(0.35f));
        }
        Ellipse(center, hw, hh, _skin);
        Ellipse(center + new Vector2(-hw * 0.25f, -hh * 0.35f), hw * 0.45f, hh * 0.3f, _skin.Lightened(0.08f));
        // Глаза (с морганием).
        bool blink = false;
        if (_time > _nextBlink) { blink = true; if (_time > _nextBlink + 0.15) _nextBlink = _time + 2.5 + (_time * 7.3 % 2.5); }
        float ey = center.Y - hh * 0.1f;
        float span = hw * 1.3f;
        for (int i = 0; i < _eyes; i++)
        {
            float ex = _eyes == 1 ? center.X : center.X - span / 2 + span * i / (_eyes - 1);
            float er = u * (0.045f - 0.006f * (_eyes - 2));
            if (blink) { DrawLine(new Vector2(ex - er, ey), new Vector2(ex + er, ey), AtlasTheme.PaperDeep, 2.5f, true); continue; }
            DrawCircle(new Vector2(ex, ey), er, new Color("f4efe3"));
            DrawCircle(new Vector2(ex + er * 0.15f, ey), er * 0.55f, _eyeColor);
            DrawCircle(new Vector2(ex + er * 0.2f, ey), er * 0.25f, AtlasTheme.PaperDeep);
        }
        // Рот: клюв у безгубых, улыбка или хмурость — по настроению.
        var mouth = center + new Vector2(0, hh * 0.45f);
        if (_beak)
            DrawColoredPolygon(new[] { mouth + new Vector2(-hw * 0.25f, -hh * 0.12f), mouth + new Vector2(hw * 0.25f, -hh * 0.12f), mouth + new Vector2(0, hh * 0.22f) }, new Color("c9a24a"));
        else
        {
            float curve = (_mood - 0.5f) * hh * 0.5f;
            var pts = new Vector2[9];
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f * 2 - 1;
                pts[i] = mouth + new Vector2(t * hw * 0.35f, curve * (1 - t * t));
            }
            DrawPolyline(pts, AtlasTheme.PaperDeep, 3, true);
        }
        if (_crown)
        {
            var top = center + new Vector2(0, -hh * 1.0f);
            DrawColoredPolygon(new[]
            {
                top + new Vector2(-hw * 0.6f, 0), top + new Vector2(-hw * 0.6f, -hh * 0.35f), top + new Vector2(-hw * 0.3f, -hh * 0.15f),
                top + new Vector2(0, -hh * 0.45f), top + new Vector2(hw * 0.3f, -hh * 0.15f), top + new Vector2(hw * 0.6f, -hh * 0.35f), top + new Vector2(hw * 0.6f, 0),
            }, AtlasTheme.Gold);
        }
        // Рамка.
        DrawRect(new Rect2(Vector2.Zero, s), AtlasTheme.Gold, false, 2);
    }
}
