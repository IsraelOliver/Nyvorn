using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    // Sparse per-tile wetness store (only wet tiles occupy memory), keyed the same way
    // LiquidSystem keys its cells. Independent from WeatherState.Wetness (the single global
    // ambient scalar driving sky/fog visuals and grass growth) - this is a separate,
    // tile-resolution layer answering "is THIS tile wet," feeding tile tint and ground traction.
    // Deliberately not persisted: it's a passive, self-regenerating effect of weather, not
    // player-authored content, so it's fine for it to reset to dry on load.
    public sealed class TileWetnessField
    {
        private const float MinWetnessToSpread = 0.35f;
        private const float SpreadAmount = 0.05f;

        private readonly Dictionary<long, float> wetness = new();
        private readonly WorldMap worldMap;

        public TileWetnessField(WorldMap worldMap)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
        }

        public float GetWetness01(int x, int y)
        {
            return wetness.TryGetValue(CreateKey(x, y), out float value) ? value : 0f;
        }

        public void AddWetness(int x, int y, float amount)
        {
            if (amount <= 0f)
                return;

            long key = CreateKey(x, y);
            float current = wetness.TryGetValue(key, out float existing) ? existing : 0f;
            wetness[key] = Math.Clamp(current + amount, 0f, 1f);
        }

        // Called from a random-tile sample pass; dryRatePerSecond should already fold in
        // wind/sun/night modifiers so this method stays a plain decay.
        public void TryRandomDryTick(int x, int y, float dt, float dryRatePerSecond)
        {
            long key = CreateKey(x, y);
            if (!wetness.TryGetValue(key, out float current) || current <= 0f)
                return;

            float next = current - (dt * dryRatePerSecond);
            if (next <= 0.001f)
                wetness.Remove(key);
            else
                wetness[key] = next;
        }

        // Small capillary-style nudge: if this tile is wet enough, it can lend a bit of
        // wetness to a drier orthogonal neighbor. Purely cosmetic spread, not a real fluid sim.
        public void SpreadToNeighborsTick(int x, int y, Random random)
        {
            long key = CreateKey(x, y);
            if (!wetness.TryGetValue(key, out float current) || current < MinWetnessToSpread)
                return;

            (int dx, int dy) = random.Next(4) switch
            {
                0 => (-1, 0),
                1 => (1, 0),
                2 => (0, -1),
                _ => (0, 1)
            };

            int neighborX = x + dx;
            int neighborY = y + dy;
            if (!worldMap.InBounds(neighborX, neighborY) || !worldMap.IsSolidAt(worldMap.WrapTileX(neighborX), neighborY))
                return;

            float neighborWetness = GetWetness01(neighborX, neighborY);
            if (neighborWetness >= current)
                return;

            float transfer = Math.Min(SpreadAmount, (current - neighborWetness) * 0.5f);
            AddWetness(neighborX, neighborY, transfer);
            wetness[key] = current - transfer;
        }

        private long CreateKey(int x, int y)
        {
            int wrappedX = worldMap.WrapTileX(x);
            return ((long)y << 32) | (uint)wrappedX;
        }
    }
}
