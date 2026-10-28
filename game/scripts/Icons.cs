using System;
using Godot;

namespace Mirozdanie.Game;

/// <summary>Иконки сил и интерфейса — рисуются кодом в единой линейной манере (PLAN.md: «иконки рисуются кодом»).</summary>
public static class Icons
{
    private static Vector2 P(Rect2 r, float x, float y) => r.Position + new Vector2(x * r.Size.X, y * r.Size.Y);

    private static void Poly(CanvasItem ci, Rect2 r, Color c, float w, bool closed, params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2 + (closed ? 1 : 0)];
        for (int i = 0; i < xy.Length / 2; i++) pts[i] = P(r, xy[2 * i], xy[2 * i + 1]);
        if (closed) pts[^1] = pts[0];
        ci.DrawPolyline(pts, c, w, true);
    }

    private static void Fill(CanvasItem ci, Rect2 r, Color c, params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = P(r, xy[2 * i], xy[2 * i + 1]);
        ci.DrawColoredPolygon(pts, c);
    }

    public static void Draw(CanvasItem ci, string key, Rect2 r, Color c)
    {
        float w = Math.Max(1.5f, r.Size.X / 16f);
        float s = r.Size.X;
        Vector2 C(float x, float y) => P(r, x, y);
        switch (key)
        {
            case "sun":
                ci.DrawCircle(C(0.5f, 0.5f), s * 0.18f, c);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.Pi / 4;
                    ci.DrawLine(C(0.5f, 0.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.27f, C(0.5f, 0.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.4f, c, w, true);
                }
                break;
            case "rain":
                ci.DrawCircle(C(0.38f, 0.38f), s * 0.15f, c);
                ci.DrawCircle(C(0.6f, 0.35f), s * 0.18f, c);
                ci.DrawRect(new Rect2(C(0.25f, 0.38f), new Vector2(s * 0.52f, s * 0.14f)), c);
                for (int i = 0; i < 3; i++) ci.DrawLine(C(0.33f + i * 0.17f, 0.62f), C(0.28f + i * 0.17f, 0.82f), c, w, true);
                break;
            case "bolt": Fill(ci, r, c, 0.55f, 0.08f, 0.25f, 0.55f, 0.47f, 0.55f, 0.38f, 0.92f, 0.75f, 0.42f, 0.52f, 0.42f, 0.66f, 0.08f); break;
            case "crack":
                ci.DrawLine(C(0.1f, 0.75f), C(0.9f, 0.75f), c, w, true);
                Poly(ci, r, c, w, false, 0.5f, 0.15f, 0.42f, 0.35f, 0.56f, 0.48f, 0.44f, 0.62f, 0.52f, 0.75f);
                Poly(ci, r, c, w, false, 0.2f, 0.75f, 0.3f, 0.6f, 0.25f, 0.5f);
                Poly(ci, r, c, w, false, 0.8f, 0.75f, 0.7f, 0.58f, 0.78f, 0.45f);
                break;
            case "wave":
                for (int k = 0; k < 3; k++)
                {
                    var pts = new Vector2[13];
                    for (int i = 0; i < 13; i++) pts[i] = C(0.1f + i * 0.0667f, 0.3f + k * 0.2f + 0.06f * Mathf.Sin(i * 1.05f));
                    ci.DrawPolyline(pts, c, w, true);
                }
                break;
            case "snow":
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Pi / 3;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.38f;
                    ci.DrawLine(C(0.5f, 0.5f) - d, C(0.5f, 0.5f) + d, c, w, true);
                }
                ci.DrawCircle(C(0.5f, 0.5f), s * 0.06f, c);
                break;
            case "meteor":
                ci.DrawCircle(C(0.66f, 0.66f), s * 0.16f, c);
                for (int i = 0; i < 3; i++) ci.DrawLine(C(0.55f - i * 0.05f, 0.52f + i * 0.09f), C(0.15f - i * 0.03f, 0.12f + i * 0.12f), c, w * 0.8f, true);
                break;
            case "wheat":
                ci.DrawLine(C(0.5f, 0.92f), C(0.5f, 0.15f), c, w, true);
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.22f + i * 0.15f;
                    Fill(ci, r, c, 0.5f, y, 0.3f, y - 0.04f, 0.36f, y + 0.08f);
                    Fill(ci, r, c, 0.5f, y, 0.7f, y - 0.04f, 0.64f, y + 0.08f);
                }
                break;
            case "seed":
                ci.DrawCircle(C(0.5f, 0.65f), s * 0.16f, c);
                Poly(ci, r, c, w, false, 0.5f, 0.5f, 0.5f, 0.3f);
                Fill(ci, r, c, 0.5f, 0.32f, 0.3f, 0.15f, 0.48f, 0.2f);
                Fill(ci, r, c, 0.5f, 0.3f, 0.75f, 0.12f, 0.55f, 0.18f);
                break;
            case "horn": Poly(ci, r, c, w * 1.2f, false, 0.15f, 0.7f, 0.35f, 0.55f, 0.6f, 0.5f, 0.82f, 0.3f, 0.85f, 0.15f); ci.DrawCircle(C(0.15f, 0.72f), s * 0.07f, c); break;
            case "cross":
                ci.DrawRect(new Rect2(C(0.42f, 0.15f), new Vector2(s * 0.16f, s * 0.7f)), c);
                ci.DrawRect(new Rect2(C(0.15f, 0.42f), new Vector2(s * 0.7f, s * 0.16f)), c);
                break;
            case "skull":
                ci.DrawCircle(C(0.5f, 0.42f), s * 0.26f, c);
                ci.DrawRect(new Rect2(C(0.36f, 0.55f), new Vector2(s * 0.28f, s * 0.22f)), c);
                ci.DrawCircle(C(0.4f, 0.42f), s * 0.07f, AtlasTheme.Paper);
                ci.DrawCircle(C(0.6f, 0.42f), s * 0.07f, AtlasTheme.Paper);
                break;
            case "eye":
                Poly(ci, r, c, w, true, 0.1f, 0.5f, 0.3f, 0.3f, 0.5f, 0.25f, 0.7f, 0.3f, 0.9f, 0.5f, 0.7f, 0.7f, 0.5f, 0.75f, 0.3f, 0.7f);
                ci.DrawCircle(C(0.5f, 0.5f), s * 0.13f, c);
                break;
            case "star":
            {
                var pts = new float[20];
                for (int i = 0; i < 10; i++)
                {
                    float rad = i % 2 == 0 ? 0.42f : 0.17f;
                    float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
                    pts[2 * i] = 0.5f + rad * Mathf.Cos(a);
                    pts[2 * i + 1] = 0.52f + rad * Mathf.Sin(a);
                }
                Fill(ci, r, c, pts);
                break;
            }
            case "scroll":
                Poly(ci, r, c, w, true, 0.25f, 0.18f, 0.75f, 0.18f, 0.75f, 0.82f, 0.25f, 0.82f);
                for (int i = 0; i < 4; i++) ci.DrawLine(C(0.33f, 0.32f + i * 0.13f), C(0.67f, 0.32f + i * 0.13f), c, w * 0.7f, true);
                break;
            case "mist":
                for (int k = 0; k < 4; k++) ci.DrawLine(C(0.12f + (k % 2) * 0.1f, 0.25f + k * 0.17f), C(0.88f - (k % 2) * 0.1f, 0.25f + k * 0.17f), c, w, true);
                break;
            case "spark":
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.Pi / 4;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * (i % 2 == 0 ? 0.42f : 0.25f);
                    ci.DrawLine(C(0.5f, 0.5f) - d, C(0.5f, 0.5f) + d, c, w, true);
                }
                ci.DrawCircle(C(0.5f, 0.5f), s * 0.08f, c);
                break;
            case "relic":
                Fill(ci, r, c, 0.5f, 0.12f, 0.78f, 0.45f, 0.5f, 0.88f, 0.22f, 0.45f);
                Poly(ci, r, AtlasTheme.Paper, w * 0.6f, false, 0.22f, 0.45f, 0.78f, 0.45f);
                break;
            case "curse":
                ci.DrawArc(C(0.5f, 0.5f), s * 0.35f, 0, Mathf.Tau, 24, c, w, true);
                ci.DrawLine(C(0.25f, 0.25f), C(0.75f, 0.75f), c, w, true);
                break;
            case "crown": Fill(ci, r, c, 0.15f, 0.75f, 0.15f, 0.35f, 0.33f, 0.55f, 0.5f, 0.25f, 0.67f, 0.55f, 0.85f, 0.35f, 0.85f, 0.75f); break;
            case "halo":
                ci.DrawArc(C(0.5f, 0.28f), s * 0.25f, 0, Mathf.Tau, 24, c, w, true);
                ci.DrawCircle(C(0.5f, 0.6f), s * 0.14f, c);
                Fill(ci, r, c, 0.28f, 0.95f, 0.5f, 0.72f, 0.72f, 0.95f);
                break;
            case "bowl":
                ci.DrawArc(C(0.5f, 0.45f), s * 0.35f, 0, Mathf.Pi, 16, c, w * 1.4f, true);
                ci.DrawLine(C(0.15f, 0.45f), C(0.85f, 0.45f), c, w, true);
                for (int i = 0; i < 3; i++) ci.DrawCircle(C(0.35f + i * 0.15f, 0.38f), s * 0.06f, c);
                break;
            case "fist":
                ci.DrawRect(new Rect2(C(0.25f, 0.35f), new Vector2(s * 0.5f, s * 0.38f)), c);
                for (int i = 0; i < 4; i++) ci.DrawCircle(C(0.31f + i * 0.125f, 0.33f), s * 0.07f, c);
                ci.DrawRect(new Rect2(C(0.35f, 0.72f), new Vector2(s * 0.3f, s * 0.18f)), c);
                break;
            case "sceptre":
                ci.DrawLine(C(0.3f, 0.9f), C(0.65f, 0.3f), c, w * 1.2f, true);
                ci.DrawCircle(C(0.68f, 0.24f), s * 0.11f, c);
                break;
            case "broken":
                ci.DrawLine(C(0.3f, 0.9f), C(0.45f, 0.62f), c, w * 1.2f, true);
                ci.DrawLine(C(0.52f, 0.55f), C(0.65f, 0.3f), c, w * 1.2f, true);
                ci.DrawCircle(C(0.68f, 0.24f), s * 0.11f, c);
                break;
            case "dove":
                Fill(ci, r, c, 0.12f, 0.55f, 0.45f, 0.45f, 0.62f, 0.2f, 0.62f, 0.48f, 0.88f, 0.4f, 0.7f, 0.62f, 0.4f, 0.68f);
                break;
            case "sword":
                ci.DrawLine(C(0.2f, 0.8f), C(0.78f, 0.22f), c, w * 1.3f, true);
                ci.DrawLine(C(0.22f, 0.58f), C(0.42f, 0.78f), c, w * 1.3f, true);
                break;
            // Земля (docs/design/terraforming.md)
            case "mountain":
                Fill(ci, r, c, 0.05f, 0.85f, 0.38f, 0.25f, 0.55f, 0.55f, 0.68f, 0.38f, 0.95f, 0.85f);
                Fill(ci, r, new Color(1, 1, 1, c.A * 0.85f), 0.31f, 0.38f, 0.38f, 0.25f, 0.45f, 0.38f, 0.38f, 0.42f);
                break;
            case "bridge":
                for (int k = 0; k < 2; k++)
                {
                    var pts = new Vector2[9];
                    for (int i = 0; i < 9; i++) pts[i] = C(0.05f + i * 0.1125f, 0.7f + k * 0.14f + 0.04f * Mathf.Sin(i * 1.6f));
                    ci.DrawPolyline(pts, c, w * 0.7f, true);
                }
                ci.DrawArc(C(0.5f, 0.78f), s * 0.36f, Mathf.Pi, Mathf.Tau, 18, c, w * 1.3f, true);
                break;
            case "strait":
                Fill(ci, r, c, 0.05f, 0.2f, 0.42f, 0.2f, 0.36f, 0.8f, 0.05f, 0.8f);
                Fill(ci, r, c, 0.58f, 0.2f, 0.95f, 0.2f, 0.95f, 0.8f, 0.64f, 0.8f);
                for (int i = 0; i < 3; i++) ci.DrawLine(C(0.46f, 0.3f + i * 0.2f), C(0.54f, 0.36f + i * 0.2f), c, w * 0.6f, true);
                break;
            case "island":
                Fill(ci, r, c, 0.2f, 0.62f, 0.35f, 0.42f, 0.55f, 0.38f, 0.78f, 0.5f, 0.82f, 0.62f);
                ci.DrawLine(C(0.55f, 0.4f), C(0.6f, 0.15f), c, w * 0.8f, true);
                Poly(ci, r, c, w * 0.8f, false, 0.42f, 0.22f, 0.6f, 0.15f, 0.76f, 0.24f);
                for (int i = 0; i < 2; i++) ci.DrawLine(C(0.08f + i * 0.05f, 0.75f + i * 0.1f), C(0.92f - i * 0.05f, 0.75f + i * 0.1f), c, w * 0.6f, true);
                break;
            case "sink":
                Fill(ci, r, c, 0.2f, 0.3f, 0.8f, 0.3f, 0.65f, 0.55f, 0.35f, 0.55f);
                ci.DrawLine(C(0.5f, 0.05f), C(0.5f, 0.28f), c, w, true);
                Fill(ci, r, c, 0.4f, 0.2f, 0.6f, 0.2f, 0.5f, 0.3f);
                for (int i = 0; i < 2; i++)
                {
                    var pts = new Vector2[9];
                    for (int k = 0; k < 9; k++) pts[k] = C(0.08f + k * 0.105f, 0.68f + i * 0.15f + 0.04f * Mathf.Sin(k * 1.4f));
                    ci.DrawPolyline(pts, c, w * 0.7f, true);
                }
                break;
            case "river":
            {
                var pts = new Vector2[12];
                for (int i = 0; i < 12; i++) pts[i] = C(0.3f + 0.18f * Mathf.Sin(i * 0.9f), 0.08f + i * 0.078f);
                ci.DrawPolyline(pts, c, w * 1.4f, true);
                ci.DrawCircle(C(0.3f, 0.1f), s * 0.07f, c);
                break;
            }
            case "lake":
                ci.DrawSetTransform(C(0.5f, 0.58f), 0, new Vector2(1f, 0.55f));
                ci.DrawArc(Vector2.Zero, s * 0.36f, 0, Mathf.Tau, 28, c, w, true);
                ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                ci.DrawLine(C(0.36f, 0.58f), C(0.56f, 0.58f), c, w * 0.6f, true);
                break;
            case "volcano":
                Fill(ci, r, c, 0.08f, 0.9f, 0.4f, 0.4f, 0.6f, 0.4f, 0.92f, 0.9f);
                for (int i = 0; i < 3; i++) ci.DrawCircle(C(0.42f + i * 0.08f, 0.28f - i * 0.07f), s * (0.06f + i * 0.02f), c);
                break;
            case "oasis":
                ci.DrawLine(C(0.5f, 0.85f), C(0.55f, 0.35f), c, w, true);
                Poly(ci, r, c, w, false, 0.2f, 0.45f, 0.4f, 0.3f, 0.55f, 0.35f, 0.75f, 0.3f, 0.88f, 0.45f);
                Poly(ci, r, c, w, false, 0.3f, 0.2f, 0.55f, 0.35f, 0.75f, 0.18f);
                ci.DrawLine(C(0.1f, 0.88f), C(0.9f, 0.88f), c, w * 0.6f, true);
                break;
            case "desert":
            {
                ci.DrawCircle(C(0.72f, 0.28f), s * 0.12f, c);
                for (int k = 0; k < 2; k++)
                {
                    var pts = new Vector2[11];
                    for (int i = 0; i < 11; i++) pts[i] = C(0.05f + i * 0.09f, 0.62f + k * 0.18f - 0.08f * Mathf.Sin(i * 0.6f + k));
                    ci.DrawPolyline(pts, c, w, true);
                }
                break;
            }
            case "glacier":
                Fill(ci, r, c, 0.1f, 0.85f, 0.3f, 0.3f, 0.5f, 0.5f, 0.68f, 0.2f, 0.9f, 0.85f);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Pi / 3;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.1f;
                    ci.DrawLine(C(0.5f, 0.68f) - d, C(0.5f, 0.68f) + d, new Color(1, 1, 1, c.A), w * 0.6f, true);
                }
                break;
            case "thaw":
                ci.DrawCircle(C(0.5f, 0.32f), s * 0.14f, c);
                for (int i = 0; i < 3; i++) Fill(ci, r, c, 0.3f + i * 0.2f, 0.6f, 0.36f + i * 0.2f, 0.75f, 0.3f + i * 0.2f, 0.88f, 0.24f + i * 0.2f, 0.75f);
                break;
            case "drift":
                Fill(ci, r, c, 0.08f, 0.35f, 0.4f, 0.25f, 0.45f, 0.6f, 0.15f, 0.72f);
                ci.DrawLine(C(0.52f, 0.5f), C(0.88f, 0.5f), c, w, true);
                Fill(ci, r, c, 0.92f, 0.5f, 0.78f, 0.4f, 0.78f, 0.6f);
                break;
            case "warm":
                ci.DrawLine(C(0.5f, 0.15f), C(0.5f, 0.65f), c, w * 1.3f, true);
                ci.DrawCircle(C(0.5f, 0.75f), s * 0.13f, c);
                for (int i = 0; i < 3; i++) ci.DrawLine(C(0.6f, 0.25f + i * 0.12f), C(0.72f, 0.25f + i * 0.12f), c, w * 0.6f, true);
                break;
            case "cold":
                ci.DrawArc(C(0.5f, 0.15f), s * 0.06f, Mathf.Pi, Mathf.Tau, 8, c, w, true);
                ci.DrawLine(C(0.44f, 0.15f), C(0.44f, 0.65f), c, w * 0.8f, true);
                ci.DrawLine(C(0.56f, 0.15f), C(0.56f, 0.65f), c, w * 0.8f, true);
                ci.DrawArc(C(0.5f, 0.75f), s * 0.13f, 0, Mathf.Tau, 16, c, w * 0.8f, true);
                break;
            case "searise":
            case "seafall":
            {
                bool up = key == "searise";
                for (int k = 0; k < 2; k++)
                {
                    var pts = new Vector2[10];
                    for (int i = 0; i < 10; i++) pts[i] = C(0.08f + i * 0.093f, 0.62f + k * 0.18f + 0.05f * Mathf.Sin(i * 1.3f));
                    ci.DrawPolyline(pts, c, w, true);
                }
                ci.DrawLine(C(0.5f, 0.1f), C(0.5f, 0.45f), c, w, true);
                if (up) Fill(ci, r, c, 0.5f, 0.05f, 0.38f, 0.2f, 0.62f, 0.2f);
                else Fill(ci, r, c, 0.5f, 0.5f, 0.38f, 0.35f, 0.62f, 0.35f);
                break;
            }
            case "brush":
                ci.DrawLine(C(0.85f, 0.12f), C(0.45f, 0.55f), c, w * 1.2f, true);
                Fill(ci, r, c, 0.42f, 0.5f, 0.52f, 0.6f, 0.3f, 0.88f, 0.12f, 0.88f);
                break;
            case "raise":
                Fill(ci, r, c, 0.1f, 0.88f, 0.5f, 0.45f, 0.9f, 0.88f);
                Fill(ci, r, c, 0.5f, 0.05f, 0.35f, 0.25f, 0.65f, 0.25f);
                ci.DrawLine(C(0.5f, 0.22f), C(0.5f, 0.38f), c, w, true);
                break;
            case "lower":
                Fill(ci, r, c, 0.1f, 0.45f, 0.9f, 0.45f, 0.5f, 0.88f);
                Fill(ci, r, c, 0.5f, 0.32f, 0.35f, 0.12f, 0.65f, 0.12f);
                break;
            case "flatten":
                ci.DrawRect(new Rect2(C(0.1f, 0.55f), new Vector2(s * 0.8f, s * 0.3f)), c);
                ci.DrawLine(C(0.1f, 0.35f), C(0.9f, 0.35f), c, w * 0.7f, true);
                break;
            case "smooth":
            {
                var pts = new Vector2[13];
                for (int i = 0; i < 13; i++) pts[i] = C(0.08f + i * 0.07f, 0.7f - 0.3f * Mathf.Exp(-Mathf.Pow((i - 6) / 3f, 2)));
                ci.DrawPolyline(pts, c, w * 1.2f, true);
                break;
            }
            case "roughen":
                Poly(ci, r, c, w, false, 0.05f, 0.8f, 0.2f, 0.4f, 0.32f, 0.65f, 0.45f, 0.25f, 0.58f, 0.6f, 0.72f, 0.35f, 0.82f, 0.62f, 0.95f, 0.5f);
                break;
            case "undo":
                ci.DrawArc(C(0.55f, 0.55f), s * 0.28f, -Mathf.Pi * 0.9f, Mathf.Pi * 0.6f, 18, c, w * 1.1f, true);
                Fill(ci, r, c, 0.12f, 0.42f, 0.36f, 0.3f, 0.32f, 0.58f);
                break;
            case "harp":
                ci.DrawCircle(C(0.35f, 0.75f), s * 0.12f, c);
                ci.DrawLine(C(0.46f, 0.75f), C(0.46f, 0.15f), c, w, true);
                Fill(ci, r, c, 0.46f, 0.15f, 0.78f, 0.28f, 0.78f, 0.4f, 0.46f, 0.28f);
                break;
            // Интерфейс
            case "pause":
                ci.DrawRect(new Rect2(C(0.28f, 0.22f), new Vector2(s * 0.14f, s * 0.56f)), c);
                ci.DrawRect(new Rect2(C(0.58f, 0.22f), new Vector2(s * 0.14f, s * 0.56f)), c);
                break;
            case "play1": Fill(ci, r, c, 0.32f, 0.2f, 0.75f, 0.5f, 0.32f, 0.8f); break;
            case "play2": Fill(ci, r, c, 0.15f, 0.22f, 0.5f, 0.5f, 0.15f, 0.78f); Fill(ci, r, c, 0.5f, 0.22f, 0.85f, 0.5f, 0.5f, 0.78f); break;
            case "play3":
                Fill(ci, r, c, 0.08f, 0.25f, 0.36f, 0.5f, 0.08f, 0.75f); Fill(ci, r, c, 0.36f, 0.25f, 0.64f, 0.5f, 0.36f, 0.75f);
                Fill(ci, r, c, 0.64f, 0.25f, 0.92f, 0.5f, 0.64f, 0.75f);
                break;
            case "chat":
                Fill(ci, r, c, 0.12f, 0.2f, 0.88f, 0.2f, 0.88f, 0.65f, 0.45f, 0.65f, 0.25f, 0.85f, 0.3f, 0.65f, 0.12f, 0.65f);
                break;
            case "globe":
                ci.DrawArc(C(0.5f, 0.5f), s * 0.38f, 0, Mathf.Tau, 28, c, w, true);
                ci.DrawLine(C(0.12f, 0.5f), C(0.88f, 0.5f), c, w * 0.7f, true);
                ci.DrawArc(C(0.5f, 0.5f), s * 0.38f, -Mathf.Pi / 2, Mathf.Pi / 2, 16, c, w * 0.7f, true);
                break;
            default:
                ci.DrawCircle(C(0.5f, 0.5f), s * 0.3f, c);
                break;
        }
    }
}

/// <summary>Кнопка с нарисованной иконкой слева.</summary>
public partial class IconButton : Button
{
    // Ключ иконки, рисуемой кодом (Icons.Draw); текстура Button.Icon не используется.
    public new string Icon = "";
    public Color IconColor = AtlasTheme.Gold;
    public int IconSize = 26;

    private bool _reserve;

    public override void _Ready()
    {
        if (!string.IsNullOrEmpty(Text)) AddThemeConstantOverride("h_separation", 6);
        Alignment = string.IsNullOrEmpty(Text) ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        if (_reserve) ApplyReserve();
    }

    public override void _Draw()
    {
        if (string.IsNullOrEmpty(Icon)) return;
        // Без текста — иконка по центру, с текстом — у левого края (место под неё оставляет ReserveIconSpace).
        float y = (Size.Y - IconSize) / 2;
        float x = string.IsNullOrEmpty(Text) ? (Size.X - IconSize) / 2 : 9;
        var c = Disabled ? new Color(IconColor, 0.4f) : IconColor;
        Icons.Draw(this, Icon, new Rect2(new Vector2(x, y), new Vector2(IconSize, IconSize)), c);
    }

    /// <summary>Отступ текста от иконки: пробелы проще всего сделать отступом стиля.</summary>
    public void ReserveIconSpace()
    {
        // Стиль темы доступен только в дереве сцены, поэтому отступ ставим в _Ready.
        _reserve = true;
        if (IsInsideTree()) ApplyReserve();
    }

    private void ApplyReserve()
    {
        // Берём стили прямо из темы атласа: во время _Ready кэш темы ещё может быть не готов.
        foreach (var (st, style) in AtlasTheme.ButtonStyles)
        {
            var sb = (StyleBox)style.Duplicate();
            sb.ContentMarginLeft = IconSize + 16;
            AddThemeStyleboxOverride(st, sb);
        }
    }
}
