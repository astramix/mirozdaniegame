using System;
using System.Collections.Generic;
using Godot;

namespace Mirozdanie.Game;

/// <summary>
/// Планета для терраформирования (docs/design/terraforming.md, §6): рельеф светом и тенью, кольцо кисти,
/// предпросмотр линии, пыль и брызги, огонь вулкана, волна, дрожь камеры, пепловое небо, колонны переселенцев.
/// </summary>
public partial class Globe
{
    private DirectionalLight3D _sun = null!;
    private ShaderMaterial _planetMat = null!, _cloudMat = null!, _atmoMat = null!;
    private ImageTexture _height = null!;
    private byte[] _heightBytes = Array.Empty<byte>();
    private MeshInstance3D _brushRing = null!;
    private StandardMaterial3D _brushMat = null!;
    private MeshInstance3D _lineMesh = null!;
    private ImmediateMesh _line = null!;
    private MeshInstance3D _startMarker = null!;
    private CpuParticles3D _brushDust = null!;
    private float _shake, _shakeTime;
    private float _ash, _targetAsh;
    private float _flash;
    private Color _flashTint = new(0.30f, 0.55f, 1f);
    private int _mapW = 512, _mapH = 256;
    private readonly List<(CpuParticles3D p, double t, double life)> _bursts = new();
    private readonly List<(OmniLight3D l, double t, double life)> _lights = new();
    private readonly List<(MeshInstance3D[] dots, Vector3 a, Vector3 b, double t, double life)> _trails = new();
    private readonly System.Random _shakeRng = new(3);

    private void TerraReady()
    {
        _brushMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = AtlasTheme.Gold, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, NoDepthTest = true, // кольцо кисти видно и поверх гор
        };
        _brushRing = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 64, RingSegments = 4 }, MaterialOverride = _brushMat, Visible = false };
        _pivot.AddChild(_brushRing);
        _line = new ImmediateMesh();
        _lineMesh = new MeshInstance3D
        {
            Mesh = _line,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
        };
        _pivot.AddChild(_lineMesh);
        _startMarker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = AtlasTheme.Gold },
            Visible = false,
        };
        _pivot.AddChild(_startMarker);
        _brushDust = MakeParticles(new Color(0.55f, 0.42f, 0.28f, 0.9f), 90, 1.1f, 0.006f);
        _brushDust.Emitting = false;
        _pivot.AddChild(_brushDust);
    }

    // ---------- Координаты карты ----------

    public void SetMapSize(int w, int h) { _mapW = w; _mapH = h; }

    /// <summary>Точка на единичной сфере для координат карты (дробные клетки), как UV у SphereMesh.</summary>
    public Vector3 MapPoint(float fx, float fy)
    {
        float u = fx / _mapW, v = fy / _mapH;
        float lat = Mathf.Pi / 2 - v * Mathf.Pi;
        return new Vector3(Mathf.Sin(u * Mathf.Tau) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(u * Mathf.Tau) * Mathf.Cos(lat));
    }

    /// <summary>Координаты карты под курсором (false — мимо планеты).</summary>
    public bool PickMap(Vector2 screen, out float fx, out float fy)
    {
        fx = fy = 0;
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        float b = from.Dot(dir), c = from.Dot(from) - 1;
        float disc = b * b - c;
        if (disc < 0) return false;
        float t = -b - Mathf.Sqrt(disc);
        if (t < 0) return false;
        var local = _pivot.GlobalTransform.Basis.Inverse() * (from + dir * t);
        float u = Mathf.Atan2(local.X, local.Z) / Mathf.Tau;
        if (u < 0) u += 1;
        float v = Mathf.Acos(Mathf.Clamp(local.Y, -1, 1)) / Mathf.Pi;
        fx = Math.Clamp(u * _mapW, 0, _mapW - 0.001f);
        fy = Math.Clamp(v * _mapH, 0, _mapH - 0.001f);
        return true;
    }

    /// <summary>Эко-клетка для координат карты (для Focus).</summary>
    public int PickCellFromMap(float fx, float fy) =>
        Math.Clamp((int)(fy / _mapH * _eh), 0, _eh - 1) * _ew + Math.Clamp((int)(fx / _mapW * _ew), 0, _ew - 1);

    private static Basis SurfaceBasis(Vector3 p) => new(new Quaternion(Vector3.Up, p.Normalized()));

    // ---------- Рельеф ----------

    /// <summary>Высоты (км над морем) для света и тени гор. Половинная точность — фильтруемая текстура в GL Compatibility.</summary>
    public void SetHeight(float[] elevation, int w, int h)
    {
        if (_heightBytes.Length != w * h * 2) _heightBytes = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            ushort bits = BitConverter.HalfToUInt16Bits((Half)Math.Max(0f, elevation[i]));
            _heightBytes[2 * i] = (byte)(bits & 0xFF);
            _heightBytes[2 * i + 1] = (byte)(bits >> 8);
        }
        _height.Update(Image.CreateFromData(w, h, false, Image.Format.Rh, _heightBytes));
    }

    // ---------- Кисть и предпросмотр ----------

    /// <summary>Кольцо кисти на поверхности: радиус в клетках карты (одна клетка ≈ π/H радиан).</summary>
    public void ShowBrush(float fx, float fy, float radiusCells, Color color)
    {
        var p = MapPoint(fx, fy);
        float ang = radiusCells * Mathf.Pi / _mapH;
        float pulse = 1f + 0.04f * Mathf.Sin(Time.GetTicksMsec() / 120f);
        float rr = Mathf.Sin(ang) * pulse;
        // Масштаб в собственных осях кольца (Basis.Scaled масштабирует в осях родителя и сплющил бы кольцо).
        _brushRing.Transform = new Transform3D(SurfaceBasis(p) * Basis.FromScale(new Vector3(rr, rr * 0.4f, rr)), p * (Mathf.Cos(ang) + 0.012f));
        _brushMat.AlbedoColor = new Color(color, 0.85f);
        _brushRing.Visible = true;
    }

    public void HideBrush()
    {
        _brushRing.Visible = false;
        _brushDust.Emitting = false;
    }

    /// <summary>Пыль (или брызги) из-под кисти, пока ведётся мазок.</summary>
    public void BrushDust(float fx, float fy, Color color, bool on)
    {
        var p = MapPoint(fx, fy);
        _brushDust.Transform = new Transform3D(SurfaceBasis(p), p * 1.01f);
        _brushDust.Color = color;
        _brushDust.Emitting = on;
    }

    /// <summary>Дуга между двумя точками карты (хребет, перешеек, пролив, дрейф) и маркер начала.</summary>
    public void ShowLine(float ax, float ay, float bx, float by, Color color)
    {
        var a = MapPoint(ax, ay);
        var b = MapPoint(bx, by);
        _line.ClearSurfaces();
        _line.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        for (int i = 0; i <= 48; i++)
        {
            float t = i / 48f;
            var q = a.Slerp(b, t).Normalized() * (1.02f + 0.05f * Mathf.Sin(Mathf.Pi * t));
            _line.SurfaceSetColor(new Color(color, 0.95f));
            _line.SurfaceAddVertex(q);
        }
        _line.SurfaceEnd();
        _startMarker.Position = a * 1.015f;
        _startMarker.Visible = true;
    }

    public void ShowStart(float ax, float ay)
    {
        _line.ClearSurfaces();
        _startMarker.Position = MapPoint(ax, ay) * 1.015f;
        _startMarker.Visible = true;
    }

    public void HideLine()
    {
        _line.ClearSurfaces();
        _startMarker.Visible = false;
    }

    // ---------- Эффекты ----------

    private static CpuParticles3D MakeParticles(Color color, int amount, float life, float size)
    {
        // Размер — в самой сетке: билборд частиц иначе теряет масштаб.
        var mesh = new QuadMesh { Size = new Vector2(size, size) };
        mesh.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, BillboardKeepScale = true,
        };
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1, 1, 1, 1));
        ramp.SetColor(1, new Color(1, 1, 1, 0));
        return new CpuParticles3D
        {
            Mesh = mesh, Amount = amount, Lifetime = life, Explosiveness = 0f, LocalCoords = true,
            Direction = Vector3.Up, Spread = 40, InitialVelocityMin = 0.03f, InitialVelocityMax = 0.12f, Gravity = new Vector3(0, -0.08f, 0),
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.6f, Color = color, ColorRamp = ramp, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.01f,
        };
    }

    /// <summary>Разовый выброс частиц в точке карты: пыль, брызги, огонь, пепел, иней.</summary>
    public void Burst(float fx, float fy, Color color, int amount = 120, float speed = 0.15f, float life = 1.6f, float size = 0.008f, float spread = 0.02f)
    {
        var p = MapPoint(fx, fy);
        var em = MakeParticles(color, amount, life, size);
        em.OneShot = true;
        em.Explosiveness = 0.85f;
        em.InitialVelocityMin = speed * 0.4f;
        em.InitialVelocityMax = speed;
        em.EmissionSphereRadius = spread;
        em.Transform = new Transform3D(SurfaceBasis(p), p * 1.01f);
        _pivot.AddChild(em);
        em.Emitting = true;
        _bursts.Add((em, 0, life + 0.3));
    }

    /// <summary>Огонь вулкана: вспышка, оранжевый свет, столб пепла.</summary>
    public void Fire(float fx, float fy)
    {
        Burst(fx, fy, new Color(1f, 0.55f, 0.15f), 160, 0.22f, 1.2f, 0.01f);
        Burst(fx, fy, new Color(0.3f, 0.27f, 0.25f, 0.9f), 220, 0.12f, 3.5f, 0.02f);
        var p = MapPoint(fx, fy);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.45f, 0.1f), LightEnergy = 3f, OmniRange = 0.4f, Position = p * 1.05f };
        _pivot.AddChild(light);
        _lights.Add((light, 0, 4.0));
    }

    /// <summary>Дрожь камеры: сила и длительность (хребет, вулкан, дрейф, падение метеорита).</summary>
    public void Shake(float amount, float seconds)
    {
        _shake = Math.Max(_shake, amount);
        _shakeTime = Math.Max(_shakeTime, seconds);
    }

    private Vector3 ShakeOffset(float d)
    {
        if (_shakeTime <= 0) return Vector3.Zero;
        _shakeTime -= d;
        float a = _shake * Math.Clamp(_shakeTime, 0f, 1f);
        return new Vector3((float)(_shakeRng.NextDouble() - 0.5), (float)(_shakeRng.NextDouble() - 0.5), 0) * a;
    }

    /// <summary>Вспышка атмосферы — волна по всей планете (море, климат мира).</summary>
    public void AtmosphereFlash(Color tint)
    {
        _flash = 1f;
        _flashTint = tint;
    }

    /// <summary>Пепел в небе 0…1: облака темнеют и густеют, свет звезды тускнеет.</summary>
    public void SetAsh(float ash) => _targetAsh = Math.Clamp(ash, 0f, 1f);

    /// <summary>Колонна огоньков от старого места к новому (деление народа, беженцы).</summary>
    public void Migration(Vector3 from, Vector3 to, Color color)
    {
        var dots = new MeshInstance3D[6];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i] = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color, EmissionEnabled = true, Emission = color },
            };
            _pivot.AddChild(dots[i]);
        }
        _trails.Add((dots, from.Normalized(), to.Normalized(), 0, 3.0));
    }

    private void TerraProcess(double delta)
    {
        float d = (float)delta;
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var (p, t, life) = _bursts[i];
            t += delta;
            if (t >= life) { p.QueueFree(); _bursts.RemoveAt(i); continue; }
            _bursts[i] = (p, t, life);
        }
        for (int i = _lights.Count - 1; i >= 0; i--)
        {
            var (l, t, life) = _lights[i];
            t += delta;
            if (t >= life) { l.QueueFree(); _lights.RemoveAt(i); continue; }
            l.LightEnergy = (float)(3.0 * (1 - t / life) * (0.75 + 0.25 * Math.Sin(t * 37)));
            _lights[i] = (l, t, life);
        }
        for (int i = _trails.Count - 1; i >= 0; i--)
        {
            var (dots, a, b, t, life) = _trails[i];
            t += delta;
            if (t >= life) { foreach (var dot in dots) dot.QueueFree(); _trails.RemoveAt(i); continue; }
            for (int k = 0; k < dots.Length; k++)
            {
                float s = Math.Clamp((float)(t / life) * 1.4f - k * 0.07f, 0f, 1f);
                dots[k].Position = a.Slerp(b, s).Normalized() * (1.02f + 0.03f * Mathf.Sin(Mathf.Pi * s));
                dots[k].Visible = s > 0 && s < 1;
            }
            _trails[i] = (dots, a, b, t, life);
        }
        _ash += (_targetAsh - _ash) * Math.Min(1f, d * 1.5f);
        _cloudMat.SetShaderParameter("ash", _ash);
        _sun.LightEnergy = 1.25f * (1f - 0.55f * _ash);
        if (_flash > 0)
        {
            _flash = Math.Max(0f, _flash - d * 0.8f);
            _atmoMat.SetShaderParameter("flash", _flash);
            var baseTint = new Color(0.30f, 0.55f, 1f).Lerp(new Color(0.55f, 0.5f, 0.42f), _ash);
            _atmoMat.SetShaderParameter("tint", baseTint.Lerp(_flashTint, _flash));
        }
        else _atmoMat.SetShaderParameter("tint", new Color(0.30f, 0.55f, 1f).Lerp(new Color(0.55f, 0.5f, 0.42f), _ash));
    }
}
