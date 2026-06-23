using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Tissue;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionTissueSystem
    {
        private const float AmbientTissueRadiusInTiles = 7f;
        private const float AmbientTissuePresence = 0.045f;
        private const float AmbientTissueSampleInterval = 0.15f;

        private float ambientTissuePresenceTimer;
        private float ambientTissuePresenceCache;

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required ITissueQueryService TissueQueries { get; init; }
        public required TissueEnvironmentSensor EnvironmentSensor { get; init; }
        public required TissueResonanceController ResonanceController { get; init; }
        public required TissueRevealController TissueRevealController { get; init; }
        public required TissueFieldDebugRenderer TissueDebugRenderer { get; init; }
        public required HashSet<int> ActivatedTissueHubKeys { get; init; }

        public bool IsTissueRadarActive => TissueRevealController.IsActive;
        public bool IsPlayerOnActivatedTissueHub => false;
        public bool CanUseTissueFastTravel => false;
        public TissueEnvironmentState EnvironmentState => EnvironmentSensor.CurrentState;
        public TissueResonanceState ResonanceState => ResonanceController.CurrentState;

        public void InitializeRuntimeState()
        {
            ambientTissuePresenceTimer = AmbientTissueSampleInterval;
            ambientTissuePresenceCache = 0f;
            EnvironmentSensor.Initialize(Player.Position);
            ResonanceController.Clear();
        }

        public void Update(float dt, InputState input)
        {
            ambientTissuePresenceTimer -= dt;
            EnvironmentSensor.Update(dt, Player.Position);
            TissueRevealController.Update(dt, Player.Position);
            ResonanceController.Update(dt);
        }

        public void SetPropagationViewport(float viewWidth, float viewHeight)
        {
            ResonanceController.SetViewport(viewWidth, viewHeight);
        }

        public void TriggerReveal()
        {
            TissueRevealController.Trigger();
            ResonanceController.Trigger(Player.Position);
        }

        public void EnsureCurrentTissueHubActivated()
        {
        }

        public bool TryFastTravelToTissueHub(int hubIndex)
        {
            return false;
        }

        public void DrawDebug(SpriteBatch spriteBatch)
        {
            (float revealStrength, float revealRadius, _) = GetEffectiveTissueVisualState();
            if (revealStrength <= 0.001f)
                return;

            TissueDebugRenderer.Draw(
                spriteBatch,
                WorldMap,
                revealStrength,
                TissueRevealController.FocusPosition,
                revealRadius);
        }

        private (float Strength, float Radius, float WaveProgress) GetEffectiveTissueVisualState()
        {
            if (TissueRevealController.CurrentStrength > 0.001f)
            {
                return (
                    TissueRevealController.CurrentStrength,
                    TissueRevealController.RevealRadius,
                    TissueRevealController.WaveProgress);
            }

            float ambientPresence = GetAmbientTissuePresence();
            if (ambientPresence <= 0.001f)
                return (0f, TissueRevealController.RevealRadius, 1f);

            float ambientRadius = AmbientTissueRadiusInTiles * WorldMap.TileSize;
            return (ambientPresence, ambientRadius, 1f);
        }

        private float GetAmbientTissuePresence()
        {
            if (ambientTissuePresenceTimer > 0f)
                return ambientTissuePresenceCache;

            Point centerTile = WorldMap.WorldToTile(Player.Position);
            int radiusTiles = System.Math.Max(2, (int)System.MathF.Round(AmbientTissueRadiusInTiles));
            float bestSignal = 0f;

            for (int y = centerTile.Y - radiusTiles; y <= centerTile.Y + radiusTiles; y++)
            {
                if (y < 0 || y >= WorldMap.Height)
                    continue;

                for (int x = centerTile.X - radiusTiles; x <= centerTile.X + radiusTiles; x++)
                {
                    int wrappedX = WorldMap.WrapTileX(x);
                    TissueCellState tissueState = TissueQueries.GetState(x, y);
                    if (!tissueState.HasBiologicalPresence)
                        continue;

                    Vector2 tileCenter = WorldMap.GetTileCenter(wrappedX, y);
                    float distance = GetLoopAwareDistance(tileCenter, Player.Position);
                    float normalized = 1f - MathHelper.Clamp(distance / (radiusTiles * WorldMap.TileSize), 0f, 1f);
                    bestSignal = System.MathF.Max(bestSignal, normalized * tissueState.Presence);
                }
            }

            ambientTissuePresenceCache = bestSignal * AmbientTissuePresence;
            ambientTissuePresenceTimer = AmbientTissueSampleInterval;
            return ambientTissuePresenceCache;
        }

        private float GetLoopAwareDistance(Vector2 a, Vector2 b)
        {
            float worldWidth = WorldMap.PixelWidth;
            float deltaX = a.X - b.X;

            if (deltaX > worldWidth * 0.5f)
                deltaX -= worldWidth;
            else if (deltaX < -worldWidth * 0.5f)
                deltaX += worldWidth;

            float deltaY = a.Y - b.Y;
            return System.MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }
    }
}
