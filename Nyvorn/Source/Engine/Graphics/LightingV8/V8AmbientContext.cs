using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.Gameplay.World.Simulation;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>World semantics and artistic ambient parameters; never inferred from camera depth.</summary>
public sealed class V8AmbientContext
{
    public IReadOnlyList<WorldLayerDefinition> Layers { get; set; } = Array.Empty<WorldLayerDefinition>();
    public SkyState SkyState { get; set; }
    public bool SkyEnabled { get; set; } = true;
    public bool LocalEnabled { get; set; } = true;
    public float SkyIntensity { get; set; } = .5f;
    /// <summary>Daytime sky intensity for every receiver except the background wall (entities, decorations, water,
    /// foreground faces). Null keeps SkyIntensity. Toward full night it returns to SkyIntensity, driven by NightStrength.</summary>
    public float? ReceiverDaySkyIntensity { get; set; }
    /// <summary>0 in full day, 1 in full night (WorldDayNightCycle.NightStrength); set by the caller every frame.</summary>
    public float NightStrength { get; set; }
    public int SkyRangeTiles { get; set; } = 24;
    /// <summary>Visual sky range of the background wall alone, in tiles. Null keeps SkyRangeTiles. It does not touch the
    /// transport: the receiver remaps the exposure it already has, with cutoff = 1 - value / SkyRangeTiles.</summary>
    public float? BackgroundSkyRangeTiles { get; set; }
    /// <summary>Default since 2026-09-16: the sky propagates on 8 neighbours, diagonal cost sqrt(2), and a diagonal is
    /// taken only when both orthogonal cells of that corner are transitable. False falls back to the former 4 neighbours,
    /// for diagnostics and regression only. The local fill of the sources always keeps 4 neighbours.</summary>
    public bool SkyDiagonalTransport { get; set; } = true;
    /// <summary>Exposure below this reads as zero on the background wall; 0 leaves the exposure untouched.</summary>
    public float BackgroundSkyCutoff => BackgroundSkyRangeTiles is float range && SkyRangeTiles > 0
        ? MathHelper.Clamp(1 - range / SkyRangeTiles, 0, .999f)
        : 0;
    public int ShallowFadeTiles { get; set; } = 6;
    // Optional world/generator annotation: false distinguishes an interior decorative void.
    // Null retains save compatibility: empty background revealing exterior in eligible layers.
    public Func<int, int, bool?> ExteriorOverride { get; set; }
    /// <summary>Background wall sky colour; also the colour the SkyTexture captures resolve.</summary>
    public Vector3 SkyColor => SkyState.AmbientLight.ToVector3() * SkyIntensity;
    public float ReceiverSkyIntensity =>
        MathHelper.Lerp(ReceiverDaySkyIntensity ?? SkyIntensity, SkyIntensity, MathHelper.Clamp(NightStrength, 0, 1));
    /// <summary>Sky colour for every receiver except the background wall, and for the foreground faces.</summary>
    public Vector3 ReceiverSkyColor => SkyState.AmbientLight.ToVector3() * ReceiverSkyIntensity;

    public float SkyWeight(int y)
    {
        if (!SkyEnabled) return 0;
        for (int i = 0; i < Layers.Count; i++)
        {
            var layer = Layers[i];
            if (!layer.Contains(y)) continue;
            return layer.LayerType switch {
                WorldLayerType.Space or WorldLayerType.Surface => 1,
                WorldLayerType.ShallowUnderground => MathHelper.Clamp((layer.EndY - y) /
                    (float)Math.Max(1, Math.Min(ShallowFadeTiles, layer.Height - 1)), 0, 1),
                _ => 0
            };
        }
        return 0; // unknown layer is not automatically Surface
    }

    public bool IsExteriorAperture(WorldMap map, int x, int y)
    {
        if (y < 0 || y >= map.Height || SkyWeight(y) <= 0) return false;
        x = map.WrapTileX(x);
        if (map.GetTile(x, y) != TileType.Empty || map.GetBackgroundTile(x, y) != TileType.Empty) return false;
        // Surface/Shallow fissures deliberately seed sky without a vertical ray to the world top.
        // Space seeds only actual exterior voids, never every cell or an ambient floor.
        return ExteriorOverride?.Invoke(x, y) ?? true;
    }
}
