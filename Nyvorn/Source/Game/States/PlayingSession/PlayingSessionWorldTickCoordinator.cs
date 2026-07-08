using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionWorldTickCoordinator
    {
        private const int RandomTileSamplesPerChunk = 2;
        private const int MaxRandomTileSamplesPerTick = 128;

        private readonly Random randomTileUpdateRandom = new();
        private float liquidTickAccumulator;

        public required WorldMap WorldMap { get; init; }
        public required PlayingSessionViewCoordinator ViewCoordinator { get; init; }
        public required WorldTickSystem WorldTickSystem { get; init; }
        public WorldEnvironmentSystem EnvironmentSystem { get; set; }
        public LiquidSystem LiquidSystem { get; set; }
        public SandSystem SandSystem { get; set; }

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
            SandSystem?.TickFast();
            TickLiquidSystem();
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
