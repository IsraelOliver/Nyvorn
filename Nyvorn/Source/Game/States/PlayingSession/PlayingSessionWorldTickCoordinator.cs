using Microsoft.Xna.Framework;
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

        public required WorldMap WorldMap { get; init; }
        public required PlayingSessionViewCoordinator ViewCoordinator { get; init; }
        public required WorldTickSystem WorldTickSystem { get; init; }
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
            SandSystem?.TickFast();
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
                    if (GrassSimulation.TryRandomUpdate(WorldMap, tile.X, tile.Y, randomTileUpdateRandom))
                        grassGrowthCount++;
                });

            LastGrassGrowthCount = grassGrowthCount;
            return grassGrowthCount;
        }
    }
}
