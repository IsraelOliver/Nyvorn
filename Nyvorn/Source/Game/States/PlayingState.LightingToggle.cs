using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.Engine.Graphics.LightingV8;

namespace Nyvorn.Source.Game.States;

/// <summary>Runtime V7 &lt;-&gt; V8 switch (F4) without reloading the world. Only the active pipeline computes and
/// draws lighting; the inactive one keeps its resources (V8 is created once, on first activation) and reads the
/// current world again when reactivated. Frame statistics restart on every activation and skip the transition.</summary>
public partial class PlayingState
{
    public const Keys LightingToggleKey = Keys.F4;

    private bool v8Active;
    private bool lightingSwitchPending;
    private string frameLabel;
    private float frameCpuMs = -1f;
    private long drawStartTimestamp;
    private double v8InitMs = -1;
    private readonly Dictionary<string, LightingFrameStats> lightingStats = new();
    private string lightingStatsText = string.Empty;
    private string lightingPreviousStatsText = string.Empty;
    private int lightingStatsRefresh;
    // Frames in which each pipeline actually computed lighting; used by the toggle validation.
    private long v7ComputeFrames, v6ComputeFrames, v8RenderFrames;

    /// <summary>Pipeline that draws the next frame: "V8", "V7" or "V6 Legacy" (F9 still selects V7/Legacy when V8 is off).</summary>
    public string ActiveLightingLabel => v8Active ? "V8" : lightingMode == LightingPipelineMode.V7 ? "V7" : "V6 Legacy";
    private string InactiveLightingLabel => v8Active ? (lightingMode == LightingPipelineMode.V7 ? "V7" : "V6 Legacy") : "V8";
    // Set when this state stops drawing (pause/death overlay pushed): the resume frame is not a pipeline frame.
    private bool lightingFrameInterrupted;
    private double pendingUpdateMs;
    private int pendingUpdates;
    private float lastFrameUpdateMs; // Update CPU of the frame that just ended (for the V8 perf bench)
    private double lastPresentMs;
    private bool lastWindowActive = true;

    /// <summary>Called by Game1 after Present (EndDraw) of the frame this state just drew.</summary>
    public void RecordPresent(double milliseconds, bool windowActive)
    {
        lastPresentMs = milliseconds;
        lastWindowActive = windowActive;
    }

    /// <summary>Called by Game1 around each state-machine Update; attributed to the frame drawn next.</summary>
    public void RecordUpdateCpu(double milliseconds)
    {
        pendingUpdateMs += milliseconds;
        pendingUpdates++;
    }

    /// <summary>Edge-triggered: a held key does not repeat.</summary>
    public static bool IsLightingTogglePress(KeyboardState current, KeyboardState previous) =>
        current.IsKeyDown(LightingToggleKey) && !previous.IsKeyDown(LightingToggleKey);

    private void SetLightingPipeline(bool useV8)
    {
        if (useV8 == v8Active)
            return;

        string from = ActiveLightingLabel;
        if (useV8)
            EnsureV8Renderer();

        v8Active = useV8;
        // Presentation state owned by a pipeline follows the active one: V8 expects neutral background albedo,
        // V7 keeps the dimmed background tint. Never both.
        session.WorldMap.NeutralLightingAlbedo = useV8;
        // Mining, doors, sand or restores may have happened through any path while V8 was inactive.
        if (useV8)
            v8Renderer.Geometry.Invalidate();

        lightingSwitchPending = true;
        Console.WriteLine($"[Lighting] switch {from} -> {ActiveLightingLabel}");
    }

    private LightingFrameStats LightingStats(string label)
    {
        if (!lightingStats.TryGetValue(label, out var stats))
            lightingStats[label] = stats = new LightingFrameStats(label);
        return stats;
    }

    private void BeginLightingFrame(float drawDt, int width, int height)
    {
        if (V8GameplayOptions.ToggleBench)
            PrepareLightingToggleBench(width, height);
        if (V8GameplayOptions.SkyCompare)
            PrepareSkyCompare(width, height);

        drawStartTimestamp = Stopwatch.GetTimestamp();
        string label = ActiveLightingLabel;
        if (label != frameLabel || lightingSwitchPending)
        {
            // The frame spanning the switch belongs to neither pipeline: discarded, never averaged.
            if (frameLabel != null && lightingStats.TryGetValue(frameLabel, out var previous))
            {
                lightingPreviousStatsText = previous.Summary();
                Console.WriteLine($"[Lighting] {frameLabel} -> {label}; {frameLabel} run: {lightingPreviousStatsText}");
            }

            LightingStats(label).Reset();
            lightingStatsText = LightingStats(label).Summary();
            lightingSwitchPending = false;
        }
        else if (frameCpuMs >= 0f && !lightingFrameInterrupted)
        {
            // drawDt spans the previous frame, which this same pipeline drew.
            LightingStats(label).Add(drawDt * 1000f, frameCpuMs, (float)pendingUpdateMs, pendingUpdates);
        }

        lastFrameUpdateMs = (float)pendingUpdateMs;
        pendingUpdateMs = 0;
        pendingUpdates = 0;
        lightingFrameInterrupted = false;
        frameLabel = label;
    }

    private void EndLightingFrame()
    {
        frameCpuMs = (float)Stopwatch.GetElapsedTime(drawStartTimestamp).TotalMilliseconds;
        if (++lightingStatsRefresh % 15 == 0)
            lightingStatsText = LightingStats(frameLabel).Summary();

        if (V8GameplayOptions.ToggleBench)
            FinishLightingToggleBenchFrame();
        if (V8GameplayOptions.SkyCompare)
            FinishSkyCompareFrame();
    }

    private void DrawLightingStatsHud(SpriteBatch batch, float y)
    {
        batch.DrawString(consoleFont, $"FRAME {lightingStatsText}", new Vector2(10, y), Color.Orange);
        string previous = lightingPreviousStatsText.Length > 0 ? "previous run: " + lightingPreviousStatsText : "previous run: none";
        string init = v8InitMs >= 0 ? $" | V8 init {v8InitMs:0.0} ms (once, excluded)" : string.Empty;
        batch.DrawString(consoleFont, previous + init, new Vector2(10, y + 22), Color.Orange);
    }
}
