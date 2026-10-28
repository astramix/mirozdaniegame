using System;
using System.Collections.Generic;
using Godot;

namespace Mirozdanie.Game;

/// <summary>
/// Финал «контакта» на планете (docs/design/god-loop.md §6; kardashev.md §7.1): зонд и ракеты летят прямо в камеру,
/// знак на земле, мигающие огни столицы, купол-щит над державой.
/// </summary>
public partial class Globe
{
    private readonly List<(Node3D node, Vector3 from, double t, double life, bool strike)> _probes = new();
    private readonly List<(MeshInstance3D ring, double t, double life, int step)> _glyphs = new();
    private readonly List<(OmniLight3D light, MeshInstance3D dot, double t, double life)> _blinks = new();
    private readonly Dictionary<int, MeshInstance3D> _shields = new();

    /// <summary>Пролетает ли сейчас что-то в камеру (для оверлея трещины).</summary>
    public event Action<bool>? ProbeArrived;

    /// <summary>Зонд (или ракеты-удар) взлетает из клетки и летит прямо в камеру.</summary>
    public void Probe(int cell, bool strike)
    {
        int n = strike ? 5 : 1;
        for (int k = 0; k < n; k++)
        {
            var p = CellPoint(cell);
            var node = new Node3D();
            var col = strike ? new Color(1f, 0.35f, 0.2f) : new Color(0.75f, 0.9f, 1f);
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = col, EmissionEnabled = true, Emission = col * 2f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            };
            node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f, RadialSegments = 8, Rings = 4 }, MaterialOverride = mat });
            // Хвост — вытянутый конус позади.
            var trail = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.0005f, Height = 0.08f, RadialSegments = 6 }, MaterialOverride = mat, Position = new Vector3(0, -0.04f, 0) };
            node.AddChild(trail);
            AddChild(node); // в мировых координатах: летит к камере, а не вращается с планетой
            var from = _pivot.ToGlobal(p * 1.01f) + new Vector3((k - n / 2) * 0.03f, 0, 0);
            node.Position = from;
            _probes.Add((node, from, -k * 0.25, strike ? 4.0 : 5.0, strike));
        }
    }

    /// <summary>Знак на земле: золотые кольца расходятся и складываются в узор (виден с орбиты).</summary>
    public void Geoglyph(int cell)
    {
        var p = CellPoint(cell);
        var basis = UpBasis(p);
        for (int k = 0; k < 4; k++)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.85f, 0.4f, 0.9f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            };
            var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 48, RingSegments = 4 }, MaterialOverride = mat };
            // Кольца разного размера и поворота: треугольник, вписанный в круги (простая «математика» для неба).
            ring.Transform = new Transform3D((basis * new Basis(Vector3.Up, k * Mathf.Pi / 3)).Scaled(new Vector3(0.012f * (k + 1), 0.012f, 0.012f * (k + 1))), p * 1.004f);
            _pivot.AddChild(ring);
            _glyphs.Add((ring, -k * 0.4, 14.0, k));
        }
    }

    /// <summary>Световое послание: огни столицы мигают простыми числами (2, 3, 5, 7…).</summary>
    public void LightBlink(int cell)
    {
        var p = CellPoint(cell);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.5f), OmniRange = 0.08f, LightEnergy = 0, Position = p * 1.02f };
        var dot = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.008f, Height = 0.016f },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.9f, 0.6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
            Position = p * 1.008f,
        };
        _pivot.AddChild(light);
        _pivot.AddChild(dot);
        _blinks.Add((light, dot, 0, 10));
    }

    /// <summary>Купола-щиты над державами со щитом: центр — середина земель, радиус — по площади.</summary>
    public void SetShields(IReadOnlyList<(int polity, Vector3 center, float radius)> shields)
    {
        var keep = new HashSet<int>();
        foreach (var (pol, center, radius) in shields)
        {
            keep.Add(pol);
            if (!_shields.TryGetValue(pol, out var dome))
            {
                var mat = new ShaderMaterial { Shader = new Shader { Code = ShieldShader } };
                dome = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16, IsHemisphere = true }, MaterialOverride = mat };
                _pivot.AddChild(dome);
                _shields[pol] = dome;
            }
            dome.Transform = new Transform3D(UpBasis(center).Scaled(new Vector3(radius, radius * 0.35f, radius)), center.Normalized() * 1.0f);
        }
        var drop = new List<int>();
        foreach (var (pol, dome) in _shields) if (!keep.Contains(pol)) { dome.QueueFree(); drop.Add(pol); }
        foreach (var pol in drop) _shields.Remove(pol);
    }

    private const string ShieldShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
void fragment() {
    // Шестиугольная сетка на куполе, мерцает.
    vec2 uv = UV * vec2(24.0, 12.0);
    vec2 g = abs(fract(uv + vec2(0.5 * floor(uv.y), 0.0)) - 0.5);
    float line = smoothstep(0.42, 0.5, max(g.x, g.y));
    float rim = pow(1.0 - abs(dot(NORMAL, VIEW)), 2.0);
    float a = (0.015 + 0.16 * line + 0.22 * rim) * (0.75 + 0.25 * sin(TIME * 2.0 + UV.x * 20.0));
    ALBEDO = vec3(0.45, 0.75, 1.0) * a;
}";

    private void FinaleProcess(double delta)
    {
        var cam = _camera.GlobalPosition;
        for (int i = _probes.Count - 1; i >= 0; i--)
        {
            var (node, from, t, life, strike) = _probes[i];
            t += delta;
            if (t < 0) { _probes[i] = (node, from, t, life, strike); continue; }
            float a = (float)(t / life);
            if (a >= 1)
            {
                node.QueueFree();
                _probes.RemoveAt(i);
                AtmosphereFlash(strike ? new Color(1f, 0.3f, 0.2f) : new Color(0.8f, 0.9f, 1f));
                if (strike) Shake(0.08f, 1.6f);
                ProbeArrived?.Invoke(strike);
                continue;
            }
            // Сначала вверх от поверхности, потом разгон прямо в камеру.
            var up = from.Normalized();
            var mid = from + up * 0.5f;
            var to = cam - (cam - mid).Normalized() * 0.05f;
            float e = a * a;
            var pos = from.Lerp(mid, Math.Min(1f, a * 2.2f)).Lerp(to, e);
            var dir = (pos - node.Position).Normalized();
            if (dir.LengthSquared() > 0.5f) node.Basis = new Basis(new Quaternion(Vector3.Up, dir));
            node.Position = pos;
            // Ближе к камере зонд и так растёт — перспектива; масштаб не трогаем.
            _probes[i] = (node, from, t, life, strike);
        }
        for (int i = _glyphs.Count - 1; i >= 0; i--)
        {
            var (ring, t, life, step) = _glyphs[i];
            t += delta;
            if (t >= life) { ring.QueueFree(); _glyphs.RemoveAt(i); continue; }
            var mat = (StandardMaterial3D)ring.MaterialOverride;
            float vis = t < 0 ? 0f : (float)Math.Min(1.0, t * 1.5) * (float)Math.Min(1.0, (life - t) / 3.0);
            mat.AlbedoColor = new Color(mat.AlbedoColor, 0.9f * vis * (0.7f + 0.3f * Mathf.Sin((float)t * 3f + step)));
            _glyphs[i] = (ring, t, life, step);
        }
        for (int i = _blinks.Count - 1; i >= 0; i--)
        {
            var (light, dot, t, life) = _blinks[i];
            t += delta;
            if (t >= life) { light.QueueFree(); dot.QueueFree(); _blinks.RemoveAt(i); continue; }
            // Пачки вспышек: 2, 3, 5, 7 — простые числа.
            int[] primes = { 2, 3, 5, 7 };
            double cycle = t % 2.5;
            int burst = (int)(t / 2.5) % primes.Length;
            bool on = cycle < primes[burst] * 0.25 && (cycle % 0.25) < 0.13;
            light.LightEnergy = on ? 3f : 0f;
            ((StandardMaterial3D)dot.MaterialOverride).AlbedoColor = new Color(1f, 0.9f, 0.6f, on ? 1f : 0.15f);
            _blinks[i] = (light, dot, t, life);
        }
    }
}
