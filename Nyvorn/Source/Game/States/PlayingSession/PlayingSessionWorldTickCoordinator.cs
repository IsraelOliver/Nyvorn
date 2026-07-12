using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionWorldTickCoordinator
    {
        private const int RandomTileSamplesPerChunk = 2;
        private const int MaxRandomTileSamplesPerTick = 128;
        private const int RainPuddleSamplesPerChunk = 1;
        // Deliberately sparse - Starbound's rain spawns individual falling drop projectiles at a
        // low rate (ratePerX ~0.1) rather than sweeping every exposed tile every tick. A small,
        // infrequent trickle here lets LiquidSystem's own flow/settle physics decide the outcome:
        // it disperses across open flat ground and only visibly pools where walls trap it - no
        // "is this an enclosed depression" check needed.
        private const int MaxRainPuddleSamplesPerTick = 8;
        // Decoupled from growth's sample count: evaporation processes the (much smaller, sparse-
        // fed) tracked-puddle dictionary directly rather than the random chunk sampler, so it can
        // afford a bigger per-tick batch without re-sweeping the whole active area.
        private const int MaxRainPuddleEvaporationsPerTick = 32;

        private readonly Random randomTileUpdateRandom = new();
        private float liquidTickAccumulator;

        // Tracks only the portion of each puddle's liquid that THIS system added from rain, so
        // evaporation during Residue only ever removes rain water - never player-placed water or
        // hydrology-pass lakes/ponds that happen to also be open to the sky. Not persisted:
        // ephemeral bookkeeping for a passive, self-regenerating effect (same rationale as
        // TileWetnessField skipping save/load).
        private readonly Dictionary<long, int> rainAddedLiquid = new();

        public required WorldMap WorldMap { get; init; }
        public required PlayingSessionViewCoordinator ViewCoordinator { get; init; }
        public required WorldTickSystem WorldTickSystem { get; init; }
        public WorldEnvironmentSystem EnvironmentSystem { get; set; }
        public WorldDayNightCycle DayNightCycle { get; set; }
        public LiquidSystem LiquidSystem { get; set; }
        public SandSystem SandSystem { get; set; }
        public TileWetnessField WetnessField { get; set; }

        public int LastRandomTileSampleCount { get; private set; }
        public int LastGrassGrowthCount { get; private set; }
        public float WorldTickTimeScale => WorldTickSystem.TimeScale;
        public bool WorldTicksPaused => WorldTickSystem.IsPaused;
        public long FastTickCount => WorldTickSystem.FastTickCount;
        public long MediumTickCount => WorldTickSystem.MediumTickCount;
        public long SlowTickCount => WorldTickSystem.SlowTickCount;

        public void SetWorldTickTimeScale(float timeScale)
        {
            WorldTickSystem.SetTimeScale(MathHelper.Clamp(timeScale, 0.1f, 16f));
        }

        public void SetWorldTicksPaused(bool isPaused)
        {
            WorldTickSystem.SetPaused(isPaused);
        }

        public int ForceGrassGrowthSamples(int sampleCount)
        {
            return RunGrassRandomUpdates(Math.Max(1, sampleCount), Math.Max(1, sampleCount));
        }

        public void StepWorldTicks(int cycles)
        {
            int safeCycles = Math.Clamp(cycles, 1, 600);
            for (int i = 0; i < safeCycles; i++)
            {
                OnFastTick();
                OnMediumTick();
                OnSlowTick();
            }

            WorldTickSystem.RecordManualDispatch(new WorldTickDispatch(
                safeCycles,
                safeCycles,
                safeCycles,
                FastOverflowed: false,
                MediumOverflowed: false,
                SlowOverflowed: false));
        }

        public void Advance(float dt)
        {
            WorldTickDispatch dispatch = WorldTickSystem.Advance(dt);

            // Reset once per rendered frame, before any catch-up fast ticks run, so a lag spike
            // that dispatches several ticks in one Advance() can't multiply the total sand work
            // done this frame — see SandSystem.ResetFrameBudget.
            SandSystem?.ResetFrameBudget();

            for (int i = 0; i < dispatch.FastTicks; i++)
                OnFastTick();

            for (int i = 0; i < dispatch.MediumTicks; i++)
                OnMediumTick();

            for (int i = 0; i < dispatch.SlowTicks; i++)
                OnSlowTick();
        }

        private void OnFastTick()
        {
            LiquidSystem?.SetActiveSimulationChunks(ViewCoordinator.ActiveSimulationChunks);
            WakeOpenSandInActiveChunks();
            SandSystem?.ProcessPendingSandWakes();
            SandSystem?.TickFast();
            TickLiquidSystem();
            RunRainWettingUpdates();
            RunRainAccumulationUpdates();
        }

        private void RunRainAccumulationUpdates()
        {
            if (LiquidSystem == null)
                return;

            float rainIntensity = EnvironmentSystem?.WeatherState.RainIntensity ?? 0f;
            if (rainIntensity > 0.05f)
                RunRainPuddleGrowth(rainIntensity);
            else
                RunRainPuddleEvaporation();
        }

        private void RunRainPuddleGrowth(float rainIntensity)
        {
            // Rain gets denser (more drops), not each drop bigger - a fixed tiny amount per drop,
            // rolled against intensity as a per-tile chance instead of a guaranteed top-up.
            const int dropAmount = 1;
            RandomTileUpdateHelper.VisitRandomTiles(
                WorldMap,
                ViewCoordinator.ActiveSimulationChunks,
                RainPuddleSamplesPerChunk,
                MaxRainPuddleSamplesPerTick,
                randomTileUpdateRandom,
                tile =>
                {
                    if (WorldMap.IsSolidAt(tile.X, tile.Y) || !WorldMap.HasOpenSkyAbove(tile.X, tile.Y))
                        return;

                    if (randomTileUpdateRandom.NextDouble() > rainIntensity)
                        return;

                    if (!LiquidSystem.AddLiquid(tile.X, tile.Y, LiquidType.Water, dropAmount))
                        return;

                    long key = CreateWetTickKey(tile.X, tile.Y);
                    rainAddedLiquid[key] = (rainAddedLiquid.TryGetValue(key, out int existing) ? existing : 0) + dropAmount;
                });
        }

        private void RunRainPuddleEvaporation()
        {
            if (rainAddedLiquid.Count == 0)
                return;

            List<long> keysToClear = null;
            int processed = 0;
            foreach (KeyValuePair<long, int> entry in rainAddedLiquid)
            {
                if (processed++ >= MaxRainPuddleEvaporationsPerTick)
                    break;

                DecodeWetTickKey(entry.Key, out int tileX, out int tileY);
                if (!WorldMap.HasOpenSkyAbove(tileX, tileY))
                    continue;

                int removeAmount = Math.Max(1, entry.Value / 30);
                bool removed = LiquidSystem.RemoveLiquid(tileX, tileY, removeAmount);
                int remaining = entry.Value - removeAmount;
                if (!removed || remaining <= 0)
                {
                    keysToClear ??= new List<long>();
                    keysToClear.Add(entry.Key);
                }
                else
                {
                    rainAddedLiquid[entry.Key] = remaining;
                }
            }

            if (keysToClear != null)
            {
                for (int i = 0; i < keysToClear.Count; i++)
                    rainAddedLiquid.Remove(keysToClear[i]);
            }
        }

        private long CreateWetTickKey(int x, int y)
        {
            return ((long)y << 32) | (uint)WorldMap.WrapTileX(x);
        }

        private void DecodeWetTickKey(long key, out int x, out int y)
        {
            x = (int)(key & 0xFFFFFFFF);
            y = (int)(key >> 32);
        }

        private void RunRainWettingUpdates()
        {
            if (WetnessField == null)
                return;

            float rainIntensity = EnvironmentSystem?.WeatherState.RainIntensity ?? 0f;
            if (rainIntensity <= 0.05f)
                return;

            // Per-fast-tick amount (this method isn't handed a dt - fast ticks are their own
            // fixed-rate unit here, same as TickLiquidSystem/RunGrassRandomUpdates below).
            const float WettingAmountPerTickAtFullIntensity = 0.01f;
            RandomTileUpdateHelper.VisitRandomTiles(
                WorldMap,
                ViewCoordinator.ActiveSimulationChunks,
                RandomTileSamplesPerChunk,
                MaxRandomTileSamplesPerTick,
                randomTileUpdateRandom,
                tile =>
                {
                    if (!WorldMap.IsSolidAt(tile.X, tile.Y) || !WorldMap.HasOpenSkyAbove(tile.X, tile.Y))
                        return;

                    WetnessField.AddWetness(tile.X, tile.Y, rainIntensity * WettingAmountPerTickAtFullIntensity);
                });
        }

        private void WakeOpenSandInActiveChunks()
        {
            if (SandSystem == null)
                return;

            for (int i = 0; i < ViewCoordinator.ActiveSimulationChunks.Count; i++)
            {
                WorldChunkCoord chunk = ViewCoordinator.ActiveSimulationChunks[i];
                SandSystem.WakeOpenSandInChunk(chunk.X, chunk.Y);
            }
        }

        private void TickLiquidSystem()
        {
            if (LiquidSystem == null)
            {
                liquidTickAccumulator = 0f;
                return;
            }

            float fastTickRate = Math.Max(1f, WorldTickSystem.Config.FastTickRate);
            float liquidTicksPerFastTick = Math.Clamp(LiquidSystem.Rules.LiquidTicksPerSecond, 1, 240) / fastTickRate;
            liquidTickAccumulator += liquidTicksPerFastTick;

            int ticks = 0;
            while (liquidTickAccumulator >= 1f && ticks < 4)
            {
                LiquidSystem.TickFast();
                liquidTickAccumulator -= 1f;
                ticks++;
            }

            if (liquidTickAccumulator >= 1f)
                liquidTickAccumulator %= 1f;
        }

        private void OnMediumTick()
        {
            RunGrassRandomUpdates(RandomTileSamplesPerChunk, MaxRandomTileSamplesPerTick);
            RunWetnessDryingUpdates();
        }

        private void RunWetnessDryingUpdates()
        {
            if (WetnessField == null)
                return;

            // While it's actively raining, wet tiles shouldn't fight the wetting pass by drying
            // at the same time - they dry once rain intensity drops (matches the Residue stage
            // being the "drying tail" of a rain event).
            float rainIntensity = EnvironmentSystem?.WeatherState.RainIntensity ?? 0f;
            if (rainIntensity > 0.05f)
                return;

            float wind = EnvironmentSystem?.WeatherState.Wind ?? 0f;
            float nightStrength = DayNightCycle?.NightStrength ?? 0f;
            float sunFactor = 1f - nightStrength; // dries faster in daylight than at night
            float dryRatePerSecond = 0.015f * (0.6f + (wind * 0.8f) + (sunFactor * 0.6f));
            float mediumTickDt = 1f / Math.Max(1f, WorldTickSystem.Config.MediumTickRate);

            RandomTileUpdateHelper.VisitRandomTiles(
                WorldMap,
                ViewCoordinator.ActiveSimulationChunks,
                RandomTileSamplesPerChunk,
                MaxRandomTileSamplesPerTick,
                randomTileUpdateRandom,
                tile =>
                {
                    WetnessField.TryRandomDryTick(tile.X, tile.Y, mediumTickDt, dryRatePerSecond);
                    WetnessField.SpreadToNeighborsTick(tile.X, tile.Y, randomTileUpdateRandom);
                });
        }

        private void OnSlowTick()
        {
        }

        private int RunGrassRandomUpdates(int samplesPerChunk, int maxSamples)
        {
            int grassGrowthCount = 0;
            LastRandomTileSampleCount = RandomTileUpdateHelper.VisitRandomTiles(
                WorldMap,
                ViewCoordinator.ActiveSimulationChunks,
                samplesPerChunk,
                maxSamples,
                randomTileUpdateRandom,
                tile =>
                {
                    float multiplier = EnvironmentSystem?.GrassGrowthChanceMultiplier ?? 1f;
                    if (GrassSimulation.TryRandomUpdate(WorldMap, tile.X, tile.Y, randomTileUpdateRandom, multiplier))
                        grassGrowthCount++;
                });

            LastGrassGrowthCount = grassGrowthCount;
            return grassGrowthCount;
        }
    }
}
