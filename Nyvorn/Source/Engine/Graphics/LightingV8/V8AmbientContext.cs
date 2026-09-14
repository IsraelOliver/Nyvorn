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
    public int SkyRangeTiles { get; set; } = 24;
    public int ShallowFadeTiles { get; set; } = 6;
    // Optional world/generator annotation: false distinguishes an interior decorative void.
    // Null retains save compatibility: empty background revealing exterior in eligible layers.
    public Func<int, int, bool?> ExteriorOverride { get; set; }
    public Vector3 SkyColor => SkyState.AmbientLight.ToVector3() * SkyIntensity;

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
