using System;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>Sky Backplane, the shipped natural model (V9Gameplay): empty FG + empty BG reveals a depth-weighted sky behind
/// the scene. No envelope, surface connectivity or opening point sources. Background transport stays in the existing glow;
/// a neighbouring BG receiver may feed only the unchanged terminal solid section, never transport through rock.</summary>
internal static class V9ProbeBackplane
{
    public const float GlowPeak = .35f;
    public const float ForegroundGlowResponse = .25f;
    /// <summary>Rows over which direct sky fades to zero at the end of the Shallow (10 tiles = 80 px).</summary>
    public const float DepthTransitionTiles = 10f;

    // Full strength through Surface and Shallow; direct sky fades over the last 10 Shallow tiles (80 px) by smoothstep,
    // zero from the start of the Cavern. Geometric spill remains separate.
    public static float DepthWeight(float row, int surfaceStart, int cavernStart)
    {
        float t = Math.Clamp((row - (cavernStart - DepthTransitionTiles)) / DepthTransitionTiles, 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    public static Vector3 Evaluate(Vector2 p, V9ProbeSkyGlow glow, Func<int, int, bool> solid,
        Func<float, float> depth, bool foreground, out float terminalDepth, out float fromGlow)
    {
        int cx = V9ProbeSky.FloorDiv((int)MathF.Floor(p.X), 8), cy = V9ProbeSky.FloorDiv((int)MathF.Floor(p.Y), 8);
        terminalDepth = fromGlow = 0;
        if (glow.WeightAt(cx, cy) > 0) return V9LabSettings.SkyLinearRgb * (V9ProbeField.OpenSky * depth(p.Y / 8));
        if (!solid(cx, cy)) return Sample(glow, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
        if (!foreground) return Vector3.Zero;
        float best = 0, bestDepth = 0, bestGlow = 0;
        Face(cx, cy - 1, p.Y - cy * 8);
        Face(cx - 1, cy, p.X - cx * 8);
        Face(cx + 1, cy, cx * 8 + 8 - p.X);
        Face(cx, cy + 1, cy * 8 + 8 - p.Y);
        terminalDepth = bestDepth;
        fromGlow = bestGlow;
        return V9LabSettings.SkyLinearRgb * best;

        void Face(int nx, int ny, float faceDepth)
        {
            if (solid(nx, ny)) return;
            var from = new Vector2(nx * 8 + 4, ny * 8 + 4);
            bool sky = glow.WeightAt(nx, ny) > 0;
            float input = sky ? V9ProbeField.OpenSky * depth(from.Y / 8)
                : Sample(glow, nx * 8 + 4, ny * 8 + 4).Z * (glow.ClassAt(nx, ny) == V9SkyClass.Void ? 1f : ForegroundGlowResponse);
            float value = input * V9LightMath.Visibility(from, p, solid);
            if (value <= best) return;
            best = value; bestDepth = faceDepth; bestGlow = sky ? 0 : value;
        }
    }

    private static Vector3 Sample(V9ProbeSkyGlow glow, int x, int y)
    {
        return glow.BackplaneBackgroundAt(x, y);
    }
}
