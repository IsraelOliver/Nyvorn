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
        private const float TissueHubActivationRadiusInTiles = 1.35f;
        private const float AmbientLinkPresence = 0.045f;
        private const float AmbientHubPresence = 0.085f;
        private const float AmbientTissueSampleInterval = 0.15f;

        private float ambientTissuePresenceTimer;
        private float ambientTissuePresenceCache;

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required TissueRevealController TissueRevealController { get; init; }
        public required TissueFieldDebugRenderer TissueDebugRenderer { get; init; }
        public required HashSet<int> ActivatedTissueHubKeys { get; init; }

        public bool IsTissueRadarActive => TissueRevealController.IsActive;
        public bool IsPlayerOnActivatedTissueHub => TryGetCurrentActivatedTissueHubIndex(out _);
        public bool CanUseTissueFastTravel => IsPlayerOnActivatedTissueHub;

        public void InitializeRuntimeState()
        {
            ambientTissuePresenceTimer = AmbientTissueSampleInterval;
            ambientTissuePresenceCache = 0f;
        }

        public void Update(float dt, InputState input)
        {
            ambientTissuePresenceTimer -= dt;
            TryActivateTouchedTissueHub();
            TissueRevealController.Update(dt, Player.Position);
        }

        public void TriggerReveal()
        {
            TissueRevealController.Trigger();
        }

        public void EnsureCurrentTissueHubActivated()
        {
            TryActivateTouchedTissueHub();
        }

        public bool TryFastTravelToTissueHub(int hubIndex)
        {
            if (!CanUseTissueFastTravel)
                return false;

            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            if (analysis == null || hubIndex < 0 || hubIndex >= analysis.Hubs.Count)
                return false;

            TissueHub targetHub = analysis.Hubs[hubIndex];
            if (!IsTissueHubActivated(targetHub))
                return false;

            Vector2 targetPosition = GetHubTravelPosition(targetHub);
            Player.TeleportTo(targetPosition);
            return true;
        }

        public void DrawDebug(SpriteBatch spriteBatch)
        {
            (float revealStrength, float revealRadius, float waveProgress) = GetEffectiveTissueVisualState();
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

            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            TissueField tissueField = WorldMap.TissueField;
            if (analysis == null || tissueField == null)
            {
                ambientTissuePresenceCache = 0f;
                ambientTissuePresenceTimer = AmbientTissueSampleInterval;
                return 0f;
            }

            Point centerTile = WorldMap.WorldToTile(Player.Position);
            int radiusTiles = System.Math.Max(2, (int)System.MathF.Round(AmbientTissueRadiusInTiles));
            float bestLinkSignal = 0f;
            float bestHubSignal = 0f;

            for (int y = centerTile.Y - radiusTiles; y <= centerTile.Y + radiusTiles; y++)
            {
                if (y < 0 || y >= WorldMap.Height)
                    continue;

                for (int x = centerTile.X - radiusTiles; x <= centerTile.X + radiusTiles; x++)
                {
                    TissueCellState tissueState = tissueField.GetState(x, y);
                    if (!tissueState.HasBiologicalPresence)
                        continue;

                    Vector2 tileCenter = WorldMap.GetTileCenter(x, y);
                    float distance = Vector2.Distance(tileCenter, Player.Position);
                    float normalized = 1f - MathHelper.Clamp(distance / (radiusTiles * WorldMap.TileSize), 0f, 1f);
                    bestLinkSignal = System.MathF.Max(bestLinkSignal, normalized * tissueState.Presence);
                }
            }

            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                float distance = Vector2.Distance(hub.WorldPosition, Player.Position);
                float normalized = 1f - MathHelper.Clamp(distance / (radiusTiles * WorldMap.TileSize), 0f, 1f);
                if (normalized <= 0f)
                    continue;

                float hubWeight = hub.IsIsolated ? 0.55f : hub.IsTerminal ? 0.78f : 1f;
                bestHubSignal = System.MathF.Max(bestHubSignal, normalized * hubWeight);
            }

            float linkPresence = bestLinkSignal * AmbientLinkPresence;
            float hubPresence = bestHubSignal * AmbientHubPresence;
            ambientTissuePresenceCache = System.MathF.Max(linkPresence, hubPresence);
            ambientTissuePresenceTimer = AmbientTissueSampleInterval;
            return ambientTissuePresenceCache;
        }

        private void TryActivateTouchedTissueHub()
        {
            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            if (analysis == null || analysis.Hubs.Count == 0)
                return;

            float activationRadius = WorldMap.TileSize * TissueHubActivationRadiusInTiles;
            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                if (GetLoopAwareDistance(hub.WorldPosition, Player.Position) > activationRadius)
                    continue;

                ActivatedTissueHubKeys.Add(CreateTissueHubKey(hub.TilePosition));
                return;
            }
        }

        private bool TryGetCurrentActivatedTissueHubIndex(out int hubIndex)
        {
            hubIndex = -1;

            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            if (analysis == null || analysis.Hubs.Count == 0 || ActivatedTissueHubKeys.Count == 0)
                return false;

            float activationRadius = WorldMap.TileSize * TissueHubActivationRadiusInTiles;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                if (!IsTissueHubActivated(hub))
                    continue;

                float distance = GetLoopAwareDistance(hub.WorldPosition, Player.Position);
                if (distance > activationRadius || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                hubIndex = i;
            }

            return hubIndex >= 0;
        }

        private bool IsTissueHubActivated(TissueHub hub)
        {
            return ActivatedTissueHubKeys.Contains(CreateTissueHubKey(hub.TilePosition));
        }

        private int CreateTissueHubKey(Point tilePosition)
        {
            int wrappedX = WorldMap.WrapTileX(tilePosition.X);
            return (tilePosition.Y * WorldMap.Width) + wrappedX;
        }

        private Vector2 GetHubTravelPosition(TissueHub hub)
        {
            float worldWidth = WorldMap.PixelWidth;
            float x = hub.WorldPosition.X;
            if (worldWidth > 0f)
            {
                x %= worldWidth;
                if (x < 0f)
                    x += worldWidth;
            }

            float y = hub.WorldPosition.Y + (WorldMap.TileSize * 0.5f);
            float maxY = WorldMap.Height * WorldMap.TileSize;
            y = MathHelper.Clamp(y, WorldMap.TileSize, System.Math.Max(WorldMap.TileSize, maxY));
            return new Vector2(x, y);
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
