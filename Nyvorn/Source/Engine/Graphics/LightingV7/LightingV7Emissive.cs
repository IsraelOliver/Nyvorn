using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV7
{
    /// <summary>
    /// Tile type to emitted RGB. Emission seeds the block channel directly from the tile grid,
    /// with no source object behind it.
    ///
    /// None of the tile types in the game emit today (Dirt, Stone, Sand, Grass, Wood, IronOre,
    /// Platform are all inert), so every entry is currently zero and the lookup costs one switch
    /// per cell. The glowing things that do exist are a decoration (mushroom) and the tissue field,
    /// which are seeded as sources instead. Add lava or glowing ore here when those tiles exist.
    /// </summary>
    public static class LightingV7Emissive
    {
        /// <summary>Emitted colour for a tile type, or zero when it does not glow.</summary>
        public static Vector3 GetEmission(TileType tile)
        {
            return tile switch
            {
                _ => Vector3.Zero
            };
        }

        /// <summary>Fast reject so the per-cell loop can skip the lookup entirely.</summary>
        public static bool AnyTileTypeEmits => false;
    }
}
