using System;
using System.Collections.Generic;
using Godot;
using Mirozdanie.Core.Chronicle;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Output;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Game;

/// <summary>
/// Цивилизации на планете (docs/design/civilizations.md, §10): города и столицы, подписи держав,
/// знамёна войск на марше, торговые пути, ночные огни.
/// </summary>
public partial class Globe
{
    private MultiMeshInstance3D _cityMesh = null!, _crownMesh = null!;
    private readonly List<Label3D> _polityLabels = new();
    private readonly List<Label3D> _cityLabels = new();
    private readonly Dictionary<int, ArmyView> _armies = new();
    private MeshInstance3D _routes = null!, _paths = null!;
    private ImageTexture _lightsTex = null!;
    private Godot.Environment _env = null!;
    private bool _politiesLayer;
    private float _nightK, _targetNight, _cloudK = 0.75f, _targetClouds = 0.75f;
    private Vector3 _sunDay;

    private sealed class ArmyView
    {
        public Node3D Node = null!;
        public Label3D Label = null!;
        public MeshInstance3D Flag = null!;
        public Vector3 Target;
        public int Outcome;
        public bool Seen;
        public float Fade = 1f;
    }

    public bool Night => _targetNight > 0.5f;

    /// <summary>Множитель размера значков и подписей: вблизи меньше, издали крупнее — на экране почти постоянно.</summary>
    private float ScreenScale => Math.Clamp((_dist - 1f) / 1.6f, 0.2f, 1.5f);

    private void CivReady(Godot.Environment env)
    {
        _env = env;
        _sunDay = _sun.Rotation;
        _lightsTex = ImageTexture.CreateFromImage(Image.CreateEmpty(512, 256, false, Image.Format.L8));
        _planetMat.SetShaderParameter("lights_tex", _lightsTex);

        _cityMesh = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = new Vector3(0.0035f, 0.007f, 0.0035f) } },
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.6f },
        };
        _pivot.AddChild(_cityMesh);
        // Корона столицы: пятигранный венец, золото.
        _crownMesh = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new CylinderMesh { TopRadius = 0.011f, BottomRadius = 0.007f, Height = 0.008f, RadialSegments = 5, Rings = 1 } },
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = 0.9f, Roughness = 0.25f, EmissionEnabled = true, Emission = new Color(0.35f, 0.25f, 0.05f) },
        };
        _pivot.AddChild(_crownMesh);
        var lineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _routes = new MeshInstance3D { Mesh = new ImmediateMesh(), MaterialOverride = lineMat };
        _pivot.AddChild(_routes);
        _paths = new MeshInstance3D { Mesh = new ImmediateMesh(), MaterialOverride = (Material)lineMat.Duplicate() };
        _pivot.AddChild(_paths);
    }

    /// <summary>Переключить ночь: свет уходит за планету, видны огни городов.</summary>
    public void ToggleNight() => _targetNight = _targetNight > 0.5f ? 0f : 1f;

    /// <summary>Подписи у края диска и за ним гаснут: 1 — смотрит на камеру, 0 — у лимба и дальше.</summary>
    private float Facing(Node3D n)
    {
        var gp = n.GlobalPosition;
        return Math.Clamp(((_camera.GlobalPosition - gp).Normalized().Dot(gp.Normalized()) - 0.15f) / 0.2f, 0f, 1f);
    }

    /// <summary>Точка на сфере по дробной позиции в эко-клетках.</summary>
    private Vector3 FracPoint(float x, float y)
    {
        float u = (x + 0.5f) / _ew, v = Math.Clamp((y + 0.5f) / _eh, 0.001f, 0.999f);
        u -= Mathf.Floor(u);
        float lat = Mathf.Pi / 2 - v * Mathf.Pi;
        return new Vector3(Mathf.Sin(u * Mathf.Tau) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(u * Mathf.Tau) * Mathf.Cos(lat));
    }

    private static Basis UpBasis(Vector3 up)
    {
        up = up.Normalized();
        var side = Math.Abs(up.Y) > 0.95f ? Vector3.Right : up.Cross(Vector3.Up).Normalized();
        return new Basis(side, up, side.Cross(up)).Orthonormalized();
    }

    public void SetCivilization(World w, string layer)
    {
        _politiesLayer = layer == "polities";
        // На политических слоях облака редеют, чтобы не закрывать карту.
        _targetClouds = layer is "polities" or "religions" or "languages" or "attitude" ? 0.22f : CloudOpacity;
        UpdateCities(w);
        UpdatePolityLabels(w);
        UpdateArmies(w);
        UpdateRoutes(w);
        // Купола-щиты над державами (god-loop.md §6).
        var shields = new List<(int, Vector3, float)>();
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive || !pol.Shield) continue;
            var sum = Vector3.Zero; var pts = new List<Vector3>();
            foreach (var m in pol.Members) foreach (var c in w.Groups[m].Territory) { var pt = CellPoint(c); sum += pt; pts.Add(pt); }
            if (pts.Count == 0) continue;
            var center = sum.Normalized();
            float r = 0.03f;
            foreach (var pt in pts) r = Math.Max(r, pt.DistanceTo(center));
            shields.Add((pol.Id, center, r + 0.02f));
        }
        SetShields(shields);
        var lights = WorldMaps.NightLights(w);
        _lightsTex.Update(Image.CreateFromData(w.Planet.W, w.Planet.H, false, Image.Format.L8, lights));
    }

    private void UpdateCities(World w)
    {
        var alive = new List<City>();
        foreach (var c in w.Cities) if (c.Alive && c.Pop >= 5000f) alive.Add(c);
        var mm = _cityMesh.Multimesh;
        mm.InstanceCount = alive.Count;
        int crowns = 0;
        foreach (var c in alive) if (c.Capital) crowns++;
        _crownMesh.Multimesh.InstanceCount = crowns;
        int ci = 0;
        for (int i = 0; i < alive.Count; i++)
        {
            var c = alive[i];
            var p = CellPoint(c.Cell);
            // Стиль — из культуры: иерархичные народы строят выше, открытые — шире.
            var g = w.Groups[c.Group];
            float h = 0.6f + c.Rank * 0.9f + (c.Rank == 3 ? 1.2f : 0f);
            float wdt = 0.8f + 0.5f * g.Culture[CultureTrait.Openness] + 0.3f * c.Rank;
            var b = UpBasis(p).Scaled(new Vector3(wdt, h, wdt));
            mm.SetInstanceTransform(i, new Transform3D(b, p * (1.003f + 0.005f * h * 0.5f)));
            var col = new Color(0.62f, 0.55f, 0.46f).Lerp(new Color(0.85f, 0.8f, 0.7f), c.Rank / 3f);
            if (c.Holy) col = new Color(0.95f, 0.9f, 1f);
            mm.SetInstanceColor(i, col);
            if (c.Capital)
            {
                var cb = UpBasis(p);
                _crownMesh.Multimesh.SetInstanceTransform(ci, new Transform3D(cb, p * (1.012f + 0.004f * h)));
                _crownMesh.Multimesh.SetInstanceColor(ci, g.PolityId >= 0 && w.Polities[g.PolityId].Form == GovForm.Theocracy ? new Color(0.95f, 0.95f, 1f) : AtlasTheme.Gold);
                ci++;
            }
        }
        // Подписи крупнейших городов (видны вблизи); столицы — золотом.
        alive.Sort((a, b) => b.Pop.CompareTo(a.Pop));
        var shown = new List<City>();
        foreach (var c in alive)
        {
            if (shown.Count >= 30) break;
            bool clash = false;
            foreach (var o in shown) if (CellPoint(o.Cell).AngleTo(CellPoint(c.Cell)) < 0.05f * ScreenScale) { clash = true; break; }
            if (!clash) shown.Add(c);
        }
        alive = shown;
        int n = alive.Count;
        while (_cityLabels.Count < n)
        {
            var l = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0008f, FontSize = 26, OutlineSize = 8,
                Font = AtlasTheme.Serif, OutlineModulate = new Color(0, 0, 0, 0.85f),
            };
            _pivot.AddChild(l);
            _cityLabels.Add(l);
        }
        for (int i = 0; i < _cityLabels.Count; i++)
        {
            var l = _cityLabels[i];
            if (i >= n) { l.Visible = false; continue; }
            var c = alive[i];
            l.Text = c.Name.Native;
            l.Position = CellPoint(c.Cell) * 1.025f + Vector3.Up * 0.0f;
            l.Modulate = c.Capital ? AtlasTheme.Gold : new Color(0.95f, 0.92f, 0.85f);
            l.Visible = true;
        }
    }

    private void UpdatePolityLabels(World w)
    {
        var list = new List<(Polity pol, Vector3 at, int cells)>();
        foreach (var pol in w.Polities)
        {
            if (!pol.Alive) continue;
            var sum = Vector3.Zero;
            int cells = 0;
            foreach (var m in pol.Members)
                foreach (var c in w.Groups[m].Territory) { sum += CellPoint(c); cells++; }
            if (cells < 3 || sum.LengthSquared() < 1e-6f) continue;
            list.Add((pol, sum.Normalized(), cells));
        }
        // Крупные державы подписываются первыми; мелкие, что легли бы поверх, — скрыты.
        list.Sort((a, b) => b.cells.CompareTo(a.cells));
        float minAngle = 0.09f * ScreenScale;
        for (int i = list.Count - 1; i >= 0; i--)
            for (int j = 0; j < i; j++)
                if (list[i].at.AngleTo(list[j].at) < minAngle) { list.RemoveAt(i); break; }
        while (_polityLabels.Count < list.Count)
        {
            var l = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0009f, OutlineSize = 12, Font = AtlasTheme.Serif,
                OutlineModulate = new Color(0.02f, 0.02f, 0.04f, 0.8f), NoDepthTest = false,
            };
            _pivot.AddChild(l);
            _polityLabels.Add(l);
        }
        for (int i = 0; i < _polityLabels.Count; i++)
        {
            var l = _polityLabels[i];
            if (i >= list.Count) { l.Visible = false; continue; }
            var (pol, at, cells) = list[i];
            // Разрядка: буквы через тонкий пробел; размер — по площади (civilizations.md, §10).
            l.Text = string.Join("\u2009", pol.Name.Native.ToUpperInvariant().ToCharArray());
            l.FontSize = (int)Math.Clamp(14 + Mathf.Sqrt(cells) * 3f, 18f, 48f);
            l.Position = at * 1.035f;
            var pc = Portrait.ToColor(MapRenderer.Palette(pol.Id));
            l.Modulate = pc.Lerp(Colors.White, 0.55f);
            l.Visible = true;
        }
    }

    private void UpdateArmies(World w)
    {
        foreach (var v in _armies.Values) v.Seen = false;
        foreach (var a in w.Armies)
        {
            if (!_armies.TryGetValue(a.Id, out var v))
            {
                v = MakeArmyView(a, w);
                _armies[a.Id] = v;
                v.Node.Position = FracPoint(a.X, a.Y) * 1.004f;
            }
            v.Seen = true;
            v.Target = FracPoint(a.X, a.Y) * 1.004f;
            v.Label.Text = Russian.Number(a.Soldiers) + (a.Alive ? "" : a.Outcome == 1 ? " ✓" : "");
            if (!a.Alive && v.Outcome == 0)
            {
                v.Outcome = a.Outcome == 0 ? 2 : a.Outcome;
                if (a.Outcome != 2) Effect(w.Groups[a.Target].Cell, a.Outcome == 1 ? new Color(1f, 0.5f, 0.3f, 0.9f) : new Color(0.6f, 0.6f, 0.7f, 0.8f), "ring");
            }
        }
        var dead = new List<int>();
        foreach (var (id, v) in _armies) if (!v.Seen) { v.Node.QueueFree(); dead.Add(id); }
        foreach (var id in dead) _armies.Remove(id);
        // Пути: пунктир от войска к цели.
        var im = (ImmediateMesh)_paths.Mesh;
        im.ClearSurfaces();
        bool any = false;
        foreach (var a in w.Armies)
        {
            if (!a.Alive || a.Defender) continue;
            if (!any) { im.SurfaceBegin(Mesh.PrimitiveType.Lines); any = true; }
            var from = FracPoint(a.X, a.Y);
            var to = FracPoint(a.TX, a.TY);
            int segs = 16;
            var col = Portrait.ToColor(MapRenderer.Palette(Math.Max(0, a.Polity))).Lerp(Colors.White, 0.3f);
            for (int s = 0; s < segs; s += 2)
            {
                im.SurfaceSetColor(new Color(col, 0.85f));
                im.SurfaceAddVertex(from.Slerp(to, s / (float)segs).Normalized() * 1.006f);
                im.SurfaceSetColor(new Color(col, 0.85f));
                im.SurfaceAddVertex(from.Slerp(to, (s + 1) / (float)segs).Normalized() * 1.006f);
            }
        }
        if (any) im.SurfaceEnd();
    }

    private ArmyView MakeArmyView(Army a, World w)
    {
        var node = new Node3D();
        _pivot.AddChild(node);
        var col = a.Polity >= 0 ? Portrait.ToColor(MapRenderer.Palette(a.Polity)) : new Color(0.75f, 0.72f, 0.65f);
        float s = a.Defender ? 0.75f : 1f;
        var pole = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.0008f * s, BottomRadius = 0.0008f * s, Height = 0.03f * s, RadialSegments = 6, Rings = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.2f, 0.15f) },
            Position = new Vector3(0, 0.015f * s, 0),
        };
        var flagMat = new StandardMaterial3D
        {
            AlbedoColor = col, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = col * 0.35f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var flag = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.016f * s, 0.011f * s) }, MaterialOverride = flagMat, Position = new Vector3(0.008f * s, 0.024f * s, 0) };
        var label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0007f, FontSize = 24, OutlineSize = 8, Font = AtlasTheme.Serif,
            Modulate = col.Lerp(Colors.White, 0.6f), OutlineModulate = new Color(0, 0, 0, 0.9f), Position = new Vector3(0, 0.04f * s, 0),
        };
        node.AddChild(pole);
        node.AddChild(flag);
        node.AddChild(label);
        return new ArmyView { Node = node, Label = label, Flag = flag };
    }

    private void UpdateRoutes(World w)
    {
        var im = (ImmediateMesh)_routes.Mesh;
        im.ClearSurfaces();
        if (w.TradeRoutes.Count == 0) return;
        float maxV = 0;
        foreach (var r in w.TradeRoutes) maxV = Math.Max(maxV, r.volume);
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (var (a, b, vol) in w.TradeRoutes)
        {
            var ga = w.Groups[a]; var gb = w.Groups[b];
            if (!ga.Alive || !gb.Alive) continue;
            var pa = CellPoint(ga.Cell); var pb = CellPoint(gb.Cell);
            float alpha = 0.15f + 0.5f * (maxV > 0 ? vol / maxV : 0f);
            var col = new Color(0.95f, 0.8f, 0.4f, alpha);
            int segs = Math.Clamp((int)(pa.AngleTo(pb) * 40f), 4, 32);
            for (int s = 0; s < segs; s++)
            {
                float t0 = s / (float)segs, t1 = (s + 1) / (float)segs;
                float h0 = 1.004f + 0.02f * pa.AngleTo(pb) * Mathf.Sin(Mathf.Pi * t0);
                float h1 = 1.004f + 0.02f * pa.AngleTo(pb) * Mathf.Sin(Mathf.Pi * t1);
                im.SurfaceSetColor(col);
                im.SurfaceAddVertex(pa.Slerp(pb, t0).Normalized() * h0);
                im.SurfaceSetColor(col);
                im.SurfaceAddVertex(pa.Slerp(pb, t1).Normalized() * h1);
            }
        }
        im.SurfaceEnd();
    }

    private void CivProcess(double delta)
    {
        float d = (float)delta;
        // Знамёна плавно идут к своим позициям; павшие — кренятся и тускнеют.
        foreach (var v in _armies.Values)
        {
            var pos = v.Node.Position.Lerp(v.Target, Math.Min(1f, d * 2.5f));
            var basis = UpBasis(pos);
            if (v.Outcome == -1) basis = basis * new Basis(Vector3.Forward, 1.3f);
            if (v.Outcome != 0) v.Fade = Math.Max(0.15f, v.Fade - d * 0.15f);
            v.Node.Transform = new Transform3D(basis.Scaled(Vector3.One * (0.6f + ScreenScale)), pos);
            var fm = (StandardMaterial3D)v.Flag.MaterialOverride;
            fm.AlbedoColor = new Color(fm.AlbedoColor, v.Fade);
            v.Label.Modulate = new Color(v.Label.Modulate, v.Fade);
            // Флаг колышется.
            v.Flag.Rotation = new Vector3(0, 0.25f * Mathf.Sin(Time.GetTicksMsec() / 260f + v.Node.GetInstanceId() % 7), 0);
        }
        // Подписи: держав — всегда на слое «Державы», иначе вблизи; городов — только вблизи.
        float polA = _politiesLayer ? 0.95f : Math.Clamp((2.9f - _dist) / 0.5f, 0f, 0.85f);
        float ss = ScreenScale;
        foreach (var l in _polityLabels) { float f = polA * Facing(l); l.Modulate = new Color(l.Modulate, f); l.OutlineModulate = new Color(0.02f, 0.02f, 0.04f, 0.8f * f); l.PixelSize = 0.0011f * ss; }
        foreach (var l in _cityLabels) l.PixelSize = 0.0009f * ss;
        float cityA = Math.Clamp((2.4f - _dist) / 0.4f, 0f, 1f);
        foreach (var l in _cityLabels) { float f = cityA * Facing(l); l.Modulate = new Color(l.Modulate, f); l.OutlineModulate = new Color(0, 0, 0, 0.85f * f); }
        _cloudK += (_targetClouds - _cloudK) * Math.Min(1f, d * 2f);
        _cloudMat.SetShaderParameter("opacity", _cloudK);
        // Ночь: свет уходит за планету, огни городов загораются.
        _nightK += (_targetNight - _nightK) * Math.Min(1f, d * 1.2f);
        _sun.Rotation = new Vector3(_sunDay.X, _sunDay.Y + _nightK * 2.4f, _sunDay.Z);
        _env.AmbientLightEnergy = 0.55f * (1f - 0.75f * _nightK);
        _planetMat.SetShaderParameter("sun_view", _sun.Transform.Basis.Z.Normalized());
    }
}
