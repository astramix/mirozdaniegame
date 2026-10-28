using System;
using System.Collections.Generic;
using Godot;
using Mirozdanie.Core.Planet;

namespace Mirozdanie.Game;

/// <summary>
/// Вращаемая планета: карта мира — текстура сферы (равнопромежуточная проекция 2:1), облака,
/// свечение атмосферы, звёздное небо, маркеры народов, подписи, выбор клетки и эффекты заклинаний.
/// Всё строится кодом и шейдерами, внешних ассетов нет.
/// </summary>
public partial class Globe : Node3D
{
    private Node3D _pivot = null!;
    private Camera3D _camera = null!;
    private MeshInstance3D _planet = null!;
    private MeshInstance3D _clouds = null!;
    private ImageTexture _albedo = null!;
    private ImageTexture _water = null!;
    private MultiMeshInstance3D _markers = null!;
    private MeshInstance3D _selection = null!;
    private readonly List<Label3D> _labels = new();
    private readonly List<(Node3D node, double t, double life, string kind)> _fx = new();
    private float _yaw, _pitch = 0.35f, _targetYaw, _targetPitch = 0.35f;
    private float _dist = 3.2f, _targetDist = 3.2f;
    private int _ew = 128, _eh = 64;
    public bool AutoRotate = true;
    public float CloudOpacity = 0.75f;

    private const string PlanetShader = @"
shader_type spatial;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap;
uniform sampler2D water_tex : filter_linear;
uniform sampler2D detail_tex : repeat_enable, filter_linear_mipmap;
uniform sampler2D height_tex : filter_linear;
uniform vec2 texel = vec2(0.001953125, 0.00390625);
uniform float relief = 1.0;
uniform sampler2D lights_tex : filter_linear;
uniform vec3 sun_view = vec3(0.0, 0.0, 1.0);
float h(vec2 uv) { return max(texture(height_tex, uv).r, 0.0); }
// Бикубическая выборка за 4 билинейных (Sigg & Hadwiger 2005, GPU Gems 2, гл. 20): мягче «лесенка» карты вблизи.
vec4 bicubic(sampler2D tex, vec2 uv, vec2 size) {
    vec2 st = uv * size - 0.5;
    vec2 i = floor(st);
    vec2 f = st - i;
    vec2 w0 = (1.0 / 6.0) * f * (f * (-f + 3.0) - 3.0) + 1.0 / 6.0;
    vec2 w1 = (1.0 / 6.0) * f * f * (3.0 * f - 6.0) + 4.0 / 6.0;
    vec2 w2 = (1.0 / 6.0) * f * (f * (-3.0 * f + 3.0) + 3.0) + 1.0 / 6.0;
    vec2 w3 = (1.0 / 6.0) * f * f * f;
    vec2 g0 = w0 + w1, g1 = w2 + w3;
    vec2 h0 = (w1 / g0 - 1.5 + i) / size, h1 = (w3 / g1 + 0.5 + i) / size;
    return g0.y * (g0.x * texture(tex, vec2(h0.x, h0.y)) + g1.x * texture(tex, vec2(h1.x, h0.y)))
         + g1.y * (g0.x * texture(tex, vec2(h0.x, h1.y)) + g1.x * texture(tex, vec2(h1.x, h1.y)));
}
void vertex() {
    // Горы чуть выступают над сферой (рельеф преувеличен, иначе его не видно с орбиты).
    VERTEX += NORMAL * h(UV) * 0.0045 * relief;
}
void fragment() {
    // Лёгкое искажение координат шумом ломает «пиксельные» берега карты в органичные,
    // мелкий шум добавляет фактуру суше (виден только вблизи).
    vec2 w = vec2(texture(detail_tex, UV * vec2(5.0, 2.5)).r, texture(detail_tex, UV * vec2(5.0, 2.5) + vec2(0.37, 0.61)).r) - 0.5;
    vec2 uv = UV + w * vec2(0.007, 0.014);
    // Вблизи — бикубическая выборка (резче и без ромбов билинейной), издали — обычная с мипмапами.
    vec4 c = mix(bicubic(albedo_tex, uv, 1.0 / texel), texture(albedo_tex, uv), smoothstep(0.0005, 0.002, fwidth(UV.x)));
    float water = texture(water_tex, uv).r;
    float d = texture(detail_tex, UV * vec2(24.0, 12.0)).r;
    ALBEDO = c.rgb * mix(0.82 + 0.36 * d, 0.94 + 0.12 * d, water);
    ROUGHNESS = mix(0.92, 0.22, water);
    SPECULAR = mix(0.15, 0.8, water);
    // Свет и тень гор: нормаль из градиента высот (км).
    float hl = h(uv - vec2(texel.x, 0.0)), hr = h(uv + vec2(texel.x, 0.0));
    float hu = h(uv - vec2(0.0, texel.y)), hd = h(uv + vec2(0.0, texel.y));
    vec3 n = normalize(vec3((hl - hr) * 1.6 * relief, (hu - hd) * 1.6 * relief, 1.0));
    NORMAL_MAP = n * 0.5 + 0.5;
    // Ночные огни городов (civilizations.md, §10): только на ночной стороне, тёплый натриевый свет.
    float night = smoothstep(0.12, -0.28, dot(NORMAL, sun_view));
    float lights = texture(lights_tex, uv).r;
    EMISSION = vec3(1.0, 0.74, 0.38) * lights * lights * night * 1.8;
}";

    private const string CloudShader = @"
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;
uniform sampler2D noise_tex : repeat_enable, filter_linear;
uniform float opacity = 0.75;
uniform float ash = 0.0;
void fragment() {
    vec2 uv = UV + vec2(TIME * 0.0035, 0.0);
    float n = texture(noise_tex, uv).r * 0.7 + texture(noise_tex, uv * 2.3 + vec2(0.3, 0.1)).r * 0.3;
    // Пепловая (позже — ядерная) зима: облака гуще и темнее (kardashev.md, §7.2).
    float a = smoothstep(0.52 - 0.35 * ash, 0.78 - 0.2 * ash, n);
    ALBEDO = mix(vec3(1.0), vec3(0.36, 0.33, 0.31), ash);
    ROUGHNESS = 1.0;
    ALPHA = clamp(a * opacity + ash * 0.25, 0.0, 1.0);
}";

    private const string AtmosphereShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
uniform vec3 tint = vec3(0.30, 0.55, 1.0);
uniform float flash = 0.0;
void fragment() {
    float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0 - 1.5 * flash);
    ALBEDO = tint * f * (1.4 + 3.0 * flash);
}";

    private const string SkyShader = @"
shader_type sky;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
void sky() {
    vec3 d = EYEDIR;
    vec3 g = floor(d * 260.0);
    float h = hash(g);
    float star = step(0.9972, h) * (0.4 + 0.6 * hash(g + 3.1));
    float band = exp(-pow(dot(d, normalize(vec3(0.3, 0.9, 0.2))) * 3.5, 2.0));
    vec3 base = mix(vec3(0.006, 0.01, 0.03), vec3(0.03, 0.02, 0.07), band);
    COLOR = base + vec3(star) * vec3(0.9, 0.95, 1.0) + vec3(0.035, 0.03, 0.07) * band * band;
}";

    public override void _Ready()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ShaderMaterial { Shader = new Shader { Code = SkyShader } } },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.32f, 0.36f, 0.5f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });
        _env = env;

        _camera = new Camera3D { Position = new Vector3(0, 0, _dist), Fov = 40, Current = true };
        AddChild(_camera);
        // Свет «держится» за камерой сбоку: видимая сторона освещена, у края — мягкий терминатор.
        _sun = new DirectionalLight3D { LightEnergy = 1.25f, LightColor = new Color(1f, 0.96f, 0.88f) };
        _camera.AddChild(_sun);
        _sun.Rotation = new Vector3(-0.35f, -0.55f, 0);

        _pivot = new Node3D();
        AddChild(_pivot);

        _albedo = ImageTexture.CreateFromImage(Image.CreateEmpty(512, 256, false, Image.Format.Rgba8));
        _water = ImageTexture.CreateFromImage(Image.CreateEmpty(512, 256, false, Image.Format.L8));
        var pm = new ShaderMaterial { Shader = new Shader { Code = PlanetShader } };
        pm.SetShaderParameter("albedo_tex", _albedo);
        pm.SetShaderParameter("water_tex", _water);
        _height = ImageTexture.CreateFromImage(Image.CreateEmpty(512, 256, false, Image.Format.Rh));
        pm.SetShaderParameter("height_tex", _height);
        _planetMat = pm;
        pm.SetShaderParameter("detail_tex", new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f, FractalOctaves = 4, Seed = 7 },
        });
        _planet = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 128, Rings = 64 }, MaterialOverride = pm };
        _pivot.AddChild(_planet);

        var noise = new NoiseTexture2D
        {
            Width = 512, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f, FractalOctaves = 5 },
        };
        var cm = new ShaderMaterial { Shader = new Shader { Code = CloudShader } };
        _cloudMat = cm;
        cm.SetShaderParameter("noise_tex", noise);
        _clouds = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.015f, Height = 2.03f, RadialSegments = 96, Rings = 48 }, MaterialOverride = cm };
        _pivot.AddChild(_clouds);

        var am = new ShaderMaterial { Shader = new Shader { Code = AtmosphereShader } };
        _atmoMat = am;
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.07f, Height = 2.14f, RadialSegments = 96, Rings = 48 }, MaterialOverride = am });

        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new SphereMesh { Radius = 0.014f, Height = 0.028f, RadialSegments = 10, Rings = 5 } };
        _markers = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true },
        };
        _pivot.AddChild(_markers);

        _selection = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.026f, OuterRadius = 0.034f, Rings = 24, RingSegments = 8 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = AtlasTheme.Gold },
            Visible = false,
        };
        _pivot.AddChild(_selection);
        TerraReady();
        CivReady(env);
    }

    // ---------- Геометрия ----------

    public void SetGrid(int ew, int eh) { _ew = ew; _eh = eh; }

    /// <summary>Центр эко-клетки на единичной сфере в координатах планеты (совпадает с UV SphereMesh).</summary>
    public Vector3 CellPoint(int cell)
    {
        float u = (cell % _ew + 0.5f) / _ew, v = (cell / _ew + 0.5f) / _eh;
        float lat = Mathf.Pi / 2 - v * Mathf.Pi;
        return new Vector3(Mathf.Sin(u * Mathf.Tau) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(u * Mathf.Tau) * Mathf.Cos(lat));
    }

    /// <summary>Клетка под курсором (или −1, если мимо планеты).</summary>
    public int PickCell(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        // Пересечение луча со сферой радиуса 1 в центре.
        float b = from.Dot(dir), c = from.Dot(from) - 1;
        float disc = b * b - c;
        if (disc < 0) return -1;
        float t = -b - Mathf.Sqrt(disc);
        if (t < 0) return -1;
        var hit = from + dir * t;
        var local = _pivot.GlobalTransform.Basis.Inverse() * hit;
        float u = Mathf.Atan2(local.X, local.Z) / Mathf.Tau;
        if (u < 0) u += 1;
        float v = Mathf.Acos(Mathf.Clamp(local.Y, -1, 1)) / Mathf.Pi;
        int ex = Math.Clamp((int)(u * _ew), 0, _ew - 1), ey = Math.Clamp((int)(v * _eh), 0, _eh - 1);
        return ey * _ew + ex;
    }

    /// <summary>Повернуть планету так, чтобы клетка оказалась перед камерой.</summary>
    public void Focus(int cell, bool zoomIn = true)
    {
        float u = (cell % _ew + 0.5f) / _ew, v = (cell / _ew + 0.5f) / _eh;
        _targetYaw = -u * Mathf.Tau;
        _targetPitch = Mathf.Pi / 2 - v * Mathf.Pi;
        // Кратчайший путь по долготе.
        while (_targetYaw - _yaw > Mathf.Pi) _targetYaw -= Mathf.Tau;
        while (_targetYaw - _yaw < -Mathf.Pi) _targetYaw += Mathf.Tau;
        if (zoomIn) _targetDist = Math.Min(_targetDist, 2.5f);
        AutoRotate = false;
    }

    // ---------- Данные ----------

    public void SetMap(byte[] rgba, int w, int h, PlanetData planet)
    {
        _albedo.Update(Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba));
        var mask = new byte[w * h];
        for (int i = 0; i < w * h; i++) mask[i] = planet.Elevation[i] < 0 ? (byte)255 : (byte)0;
        _water.Update(Image.CreateFromData(w, h, false, Image.Format.L8, mask));
        SetHeight(planet.Elevation, w, h);
    }

    public void SetMarkers(IReadOnlyList<(int cell, float pop, Color color, string label)> groups, int selectedCell)
    {
        var mm = _markers.Multimesh;
        mm.InstanceCount = groups.Count;
        for (int i = 0; i < groups.Count; i++)
        {
            var (cell, pop, color, _) = groups[i];
            var p = CellPoint(cell);
            // Население в настоящих людях (kardashev.md): 10 тыс. → 0,4, 10 млн → 0,85; вблизи значки мельче.
            float scale = (0.6f + 0.15f * Math.Max(0f, Mathf.Log(Math.Max(1f, pop / 1e4f)) / Mathf.Log(10))) * Math.Clamp((_dist - 1f) / 1.6f, 0.3f, 1.2f);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), p * 1.018f));
            mm.SetInstanceColor(i, color);
        }
        // Подписи крупнейших народов.
        var top = new List<(int cell, float pop, Color color, string label)>(groups);
        top.Sort((a, b) => b.pop.CompareTo(a.pop));
        int n = Math.Min(24, top.Count);
        while (_labels.Count < n)
        {
            var l = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0009f, FontSize = 26, OutlineSize = 10,
                Modulate = AtlasTheme.Ink, OutlineModulate = new Color(0, 0, 0, 0.85f), Font = AtlasTheme.Serif,
            };
            _pivot.AddChild(l);
            _labels.Add(l);
        }
        for (int i = 0; i < _labels.Count; i++)
        {
            if (i >= n) { _labels[i].Visible = false; continue; }
            _labels[i].Text = top[i].label;
            _labels[i].Position = CellPoint(top[i].cell) * 1.03f;
            _labels[i].Visible = true;
        }
        if (selectedCell >= 0)
        {
            var p = CellPoint(selectedCell);
            // Тор лежит в плоскости XZ; поворачиваем его ось Y к нормали поверхности.
            var basis = new Basis(new Quaternion(Vector3.Up, p.Normalized()));
            _selection.Transform = new Transform3D(basis, p * 1.012f);
            _selection.Visible = true;
        }
        else _selection.Visible = false;
    }

    // ---------- Эффекты заклинаний ----------

    public void Effect(int cell, Color color, string kind)
    {
        var p = CellPoint(cell);
        var up = p.Normalized();
        var side = Math.Abs(up.Y) > 0.95f ? Vector3.Right : up.Cross(Vector3.Up).Normalized();
        var basis = new Basis(side, up, side.Cross(up)).Orthonormalized();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        // Кольцо, расходящееся по поверхности.
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 48, RingSegments = 6 }, MaterialOverride = mat };
        ring.Transform = new Transform3D(basis.Scaled(Vector3.One * 0.01f), p * 1.008f);
        _pivot.AddChild(ring);
        _fx.Add((ring, 0, 1.8, "ring"));
        if (kind is "beam" or "bolt" or "meteor")
        {
            // Луч с неба (молния, видение) или падающий камень.
            var beamMat = (StandardMaterial3D)mat.Duplicate();
            var beam = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = kind == "meteor" ? 0.03f : 0.004f, BottomRadius = kind == "meteor" ? 0.03f : 0.008f, Height = 0.5f }, MaterialOverride = beamMat };
            beam.Transform = new Transform3D(basis, p * 1.25f);
            _pivot.AddChild(beam);
            _fx.Add((beam, 0, kind == "meteor" ? 1.0 : 0.7, kind));
        }
    }

    // ---------- Ввод и анимация ----------

    public void Drag(Vector2 delta)
    {
        float k = 0.005f * _dist / 3f;
        _targetYaw += delta.X * k;
        _targetPitch = Math.Clamp(_targetPitch + delta.Y * k, -1.45f, 1.45f);
        AutoRotate = false;
    }

    public void Zoom(float factor) => _targetDist = Math.Clamp(_targetDist * factor, 1.6f, 6f);

    public float Distance => _dist;

    public override void _Process(double delta)
    {
        float d = (float)delta;
        if (AutoRotate) _targetYaw += d * 0.05f;
        float k = 1f - Mathf.Exp(-d * 8f);
        _yaw += (_targetYaw - _yaw) * k;
        _pitch += (_targetPitch - _pitch) * k;
        _dist += (_targetDist - _dist) * k;
        _pivot.Basis = new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Up, _yaw);
        _camera.Position = new Vector3(0, 0, _dist) + ShakeOffset(d);
        TerraProcess(delta);
        CivProcess(delta);
        FinaleProcess(delta);
        // Подписи видны только вблизи.
        bool near = _dist < 2.6f;
        // На слое держав подписи народов уступают место державам.
        float groupA = _politiesLayer ? Math.Clamp((2.0f - _dist) / 0.3f, 0f, 1f) : near ? 1f : Math.Clamp((3.2f - _dist) / 0.6f, 0f, 1f);
        foreach (var l in _labels) { float f = groupA * Facing(l); l.Modulate = new Color(AtlasTheme.Ink, f); l.OutlineModulate = new Color(0, 0, 0, 0.85f * f); l.PixelSize = 0.0009f * Math.Clamp((_dist - 1f) / 1.6f, 0.2f, 1.5f); }
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var (node, t, life, kind) = _fx[i];
            t += delta;
            float a = (float)(t / life);
            if (a >= 1) { node.QueueFree(); _fx.RemoveAt(i); continue; }
            var mi = (MeshInstance3D)node;
            var mat = (StandardMaterial3D)mi.MaterialOverride;
            var c = mat.AlbedoColor;
            mat.AlbedoColor = new Color(c, (1 - a) * (kind == "bolt" ? (Mathf.Sin((float)t * 60) > 0 ? 1f : 0.3f) : 1f));
            if (kind == "ring")
            {
                var tr = mi.Transform;
                float s = 0.01f + a * 0.16f;
                mi.Transform = new Transform3D(tr.Basis.Orthonormalized().Scaled(Vector3.One * s), tr.Origin);
            }
            else if (kind == "meteor")
            {
                var tr = mi.Transform;
                var up = tr.Origin.Normalized();
                mi.Transform = new Transform3D(tr.Basis, up * (1.6f - 0.6f * Math.Min(1f, a * 1.3f)));
            }
            _fx[i] = (node, t, life, kind);
        }
    }
}
