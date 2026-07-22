using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class TorchRuntimeSystem : IForegroundTileBreakListener, IWorldObjectMiningProvider
    {
        public const int TorchWidth = 8;
        public const int TorchHeight = 8;

        private const int PoleFrameSize = 8;
        private const int FlameFrameSize = 8;
        private const int FlameFrameCount = 7;
        private const float FlameFrameDuration = 0.12f;
        private const int GroundPoleVariantCount = 2;
        private const int WallLeftPoleFrameIndex = 2;
        private const int WallRightPoleFrameIndex = 3;

        // Local pixel anchor inside each 8x8 pole frame (torch-Sheet.png): 0/1 are the two ground
        // variants, 2/3 are wall-mounted left/right. The flame's own anchor (below) is constant
        // across all of its frames - the flame is positioned so its anchor lands on the pole's.
        private static readonly Point[] PoleAnchors = { new Point(3, 2), new Point(4, 2), new Point(3, 2), new Point(4, 2) };
        private static readonly Point FlameAnchor = new Point(3, 5);

        private static readonly WorldObjectMiningDefinition MiningDefinition = new(true, 0.5f, 1);
        private static readonly Color ValidPreviewTint = new Color(92, 255, 128, 140);
        private static readonly Color InvalidPreviewTint = new Color(255, 64, 64, 140);

        private readonly List<TorchInstance> torches = new();
        private readonly System.Random random = new();
        private Rectangle previewBounds;
        private bool previewVisible;
        private bool previewValid;
        private int previewPoleFrameIndex;
        private readonly RevisionTracker revisions = new();

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Texture2D PoleTexture { get; init; }
        public required Texture2D FlameTexture { get; init; }

        public IReadOnlyList<TorchInstance> Torches => torches;
        public bool HasUnsavedChanges => revisions.HasUnsavedChanges;

        // Consumed by WorldLightingSystem each frame - see PlayingSession.UpdateFrame. Falloff is
        // whatever the normal BFS decay produces from here, same as a sky opening - no separate
        // radius concept needed.
        public IEnumerable<Vector2> GetLightSourcePositions()
        {
            for (int i = 0; i < torches.Count; i++)
                yield return torches[i].LightOrigin;
        }

        public void Restore(IEnumerable<TorchSaveData> savedTorches)
        {
            torches.Clear();
            if (savedTorches != null)
            {
                foreach (TorchSaveData savedTorch in savedTorches)
                {
                    if (savedTorch != null)
                        torches.Add(new TorchInstance(new Vector2(savedTorch.PositionX, savedTorch.PositionY), savedTorch.PoleFrameIndex));
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public void MarkPersisted()
        {
            revisions.MarkPersisted();
        }

        public bool TryPlaceSelectedTorch(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdatePlacementPreview(selectedHotbarIndex, mouseWorld);

            if (!input.PlacePressed)
                return false;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Torch)
                return false;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return true;

            if (!previewValid)
                return true;

            torches.Add(new TorchInstance(new Vector2(previewBounds.X, previewBounds.Y), previewPoleFrameIndex));
            revisions.MarkChanged();
            selectedSlot.RemoveOne();
            return true;
        }

        public void Draw(SpriteBatch spriteBatch, float visualTimeSeconds)
        {
            for (int i = 0; i < torches.Count; i++)
            {
                TorchInstance torch = torches[i];
                Rectangle poleSource = new Rectangle(torch.PoleFrameIndex * PoleFrameSize, 0, PoleFrameSize, PoleFrameSize);
                spriteBatch.Draw(PoleTexture, torch.Bounds, poleSource, Color.White);

                Point poleAnchor = PoleAnchors[torch.PoleFrameIndex];
                int flameFrame = GetFlameFrame(torch, visualTimeSeconds);
                Rectangle flameSource = new Rectangle(flameFrame * FlameFrameSize, 0, FlameFrameSize, FlameFrameSize);
                Vector2 flamePosition = new Vector2(
                    torch.Bounds.X + poleAnchor.X - FlameAnchor.X,
                    torch.Bounds.Y + poleAnchor.Y - FlameAnchor.Y);
                spriteBatch.Draw(FlameTexture, flamePosition, flameSource, Color.White);
            }

            if (previewVisible)
            {
                Rectangle poleSource = new Rectangle(previewPoleFrameIndex * PoleFrameSize, 0, PoleFrameSize, PoleFrameSize);
                spriteBatch.Draw(PoleTexture, previewBounds, poleSource, previewValid ? ValidPreviewTint : InvalidPreviewTint);
            }
        }

        // Deterministic per-position phase (not saved, doesn't need to be) so torches placed at
        // different spots don't all flicker in perfect unison.
        private static int GetFlameFrame(TorchInstance torch, float visualTimeSeconds)
        {
            float cycleDuration = FlameFrameDuration * FlameFrameCount;
            float phase = ((torch.Position.X * 13f) + (torch.Position.Y * 7f)) % cycleDuration;
            float t = (visualTimeSeconds + phase) % cycleDuration;
            if (t < 0f)
                t += cycleDuration;

            return (int)(t / FlameFrameDuration);
        }

        public bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target)
        {
            if (TryGetTorchIndexAtTile(tile, out int index))
            {
                target = new WorldObjectMiningTarget(MiningDefinition, torches[index].Bounds);
                return true;
            }

            target = default;
            return false;
        }

        public bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            if (worldItemRuntimeSystem == null || !TryGetTorchIndexAtTile(tile, out int index))
                return false;

            TorchInstance torch = torches[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Torch, 1, torch.Bounds.Center.ToVector2());
            torches.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        // Ground-mounted torches use the generic base-support check; wall-mounted ones aren't
        // resting on anything below, so breaking their side wall has to be checked separately -
        // otherwise a torch would keep floating in mid-air after its mount is mined out.
        public void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            Point wrappedBrokenTile = new Point(WorldMap.WrapTileX(context.Tile.X), context.Tile.Y);
            bool removedAny = false;

            for (int i = torches.Count - 1; i >= 0; i--)
            {
                TorchInstance torch = torches[i];
                if (!IsSupportTile(torch, wrappedBrokenTile))
                    continue;

                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Torch, 1, torch.Bounds.Center.ToVector2());
                torches.RemoveAt(i);
                removedAny = true;
            }

            if (removedAny)
                revisions.MarkChanged();
        }

        private bool IsSupportTile(TorchInstance torch, Point wrappedBrokenTile)
        {
            int torchTileX = torch.Bounds.Left / WorldMap.TileSize;
            int torchTileY = torch.Bounds.Top / WorldMap.TileSize;

            switch (torch.PoleFrameIndex)
            {
                case WallLeftPoleFrameIndex:
                    return wrappedBrokenTile.X == WorldMap.WrapTileX(torchTileX - 1) && wrappedBrokenTile.Y == torchTileY;
                case WallRightPoleFrameIndex:
                    return wrappedBrokenTile.X == WorldMap.WrapTileX(torchTileX + 1) && wrappedBrokenTile.Y == torchTileY;
                default:
                    return WorldObjectSupport.IsBaseSupportTile(torch.Bounds, wrappedBrokenTile, WorldMap.TileSize);
            }
        }

        private Rectangle GetSnappedPlacementBounds(Point tile)
        {
            int x = (WorldMap.WrapTileX(tile.X) * WorldMap.TileSize) + ((WorldMap.TileSize - TorchWidth) / 2);
            int y = ((tile.Y + 1) * WorldMap.TileSize) - TorchHeight;
            return new Rectangle(x, y, TorchWidth, TorchHeight);
        }

        private void UpdatePlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            previewVisible = false;
            previewValid = false;
            previewPoleFrameIndex = 0;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Torch)
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            previewBounds = GetSnappedPlacementBounds(tile);
            previewVisible = true;
            previewValid = WorldObjectPlacementValidator.CanPlaceObject(WorldMap, Player, previewBounds) &&
                            TryResolvePoleFrame(previewBounds, out previewPoleFrameIndex) &&
                            !IntersectsExistingTorch(previewBounds);
        }

        // Priority: standing on solid floor (frame 0/1) -> a solid foreground tile or tree trunk to
        // either side (frame 2/3, leaning out of that surface) -> a background wall placed directly
        // behind this same tile, as a fallback when there's no side wall (frame 0/1, same upright
        // look as the ground since the flame still points straight up). Roots/branches/canopy don't
        // count as a trunk: nailing a torch into a leaf or a root running along the ground wouldn't
        // look right.
        private bool TryResolvePoleFrame(Rectangle bounds, out int poleFrameIndex)
        {
            if (WorldObjectSupport.HasFullBaseSupport(WorldMap, bounds))
            {
                poleFrameIndex = random.Next(0, GroundPoleVariantCount);
                return true;
            }

            int tileX = bounds.Left / WorldMap.TileSize;
            int tileY = bounds.Top / WorldMap.TileSize;

            if (IsMountableWallAt(tileX - 1, tileY))
            {
                poleFrameIndex = WallLeftPoleFrameIndex;
                return true;
            }

            if (IsMountableWallAt(tileX + 1, tileY))
            {
                poleFrameIndex = WallRightPoleFrameIndex;
                return true;
            }

            if (WorldMap.IsBackgroundSolidAt(tileX, tileY))
            {
                poleFrameIndex = random.Next(0, GroundPoleVariantCount);
                return true;
            }

            poleFrameIndex = 0;
            return false;
        }

        private bool IsMountableWallAt(int tileX, int tileY)
        {
            if (WorldMap.IsSolidAt(tileX, tileY))
                return true;

            return WorldMap.TryGetTreePartTypeAtTile(new Point(tileX, tileY), out TreePartType partType) &&
                   IsTrunkPart(partType);
        }

        private static bool IsTrunkPart(TreePartType partType)
        {
            switch (partType)
            {
                case TreePartType.TrunkStraight:
                case TreePartType.TrunkBaseRightRootSocket:
                case TreePartType.TrunkBaseLeftRootSocket:
                case TreePartType.TrunkCutSupport:
                case TreePartType.TrunkContinuation:
                case TreePartType.TrunkBaseCut:
                case TreePartType.TrunkUpperCut:
                case TreePartType.TrunkBareBase:
                case TreePartType.TrunkBaseRightRootCutSocket:
                    return true;
                default:
                    return false;
            }
        }

        private bool IntersectsExistingTorch(Rectangle bounds)
        {
            for (int i = 0; i < torches.Count; i++)
            {
                if (torches[i].Bounds.Intersects(bounds))
                    return true;
            }

            return false;
        }

        private bool TryGetTorchIndexAtTile(Point tile, out int index)
        {
            index = -1;
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return false;

            Rectangle tileBounds = WorldMap.GetTileBounds(WorldMap.WrapTileX(tile.X), tile.Y);
            for (int i = 0; i < torches.Count; i++)
            {
                if (!torches[i].Bounds.Intersects(tileBounds))
                    continue;

                index = i;
                return true;
            }

            return false;
        }
    }
}
