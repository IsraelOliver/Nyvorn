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
    public sealed class TorchRuntimeSystem : FurnitureRuntimeSystem<TorchInstance>
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

        private readonly System.Random random = new();
        private int previewPoleFrameIndex;

        public required Texture2D PoleTexture { get; init; }
        public required Texture2D FlameTexture { get; init; }

        public IReadOnlyList<TorchInstance> Torches => FurnitureItems;

        public IEnumerable<Vector2> GetLightSourcePositions()
        {
            for (int i = 0; i < furnitureItems.Count; i++)
                yield return furnitureItems[i].LightOrigin;
        }

        public void Restore(IEnumerable<TorchSaveData> savedTorches)
        {
            furnitureItems.Clear();
            if (savedTorches != null)
            {
                foreach (TorchSaveData savedTorch in savedTorches)
                {
                    if (savedTorch != null)
                    {
                        int offset = (WorldMap.TileSize - TorchWidth) / 2;
                        Point tile = new Point(WorldMap.WrapTileX((int)System.Math.Round((savedTorch.PositionX - offset) / WorldMap.TileSize)),
                                               (int)System.Math.Round(((savedTorch.PositionY + TorchHeight) / WorldMap.TileSize) - 1));
                        bool facingLeft = savedTorch.FacingLeft;
                        furnitureItems.Add(new TorchInstance(tile, savedTorch.PoleFrameIndex, WorldMap.TileSize, facingLeft));
                    }
                }
            }

            revisions.MarkChanged();
            MarkPersisted();
        }

        public bool TryPlaceSelectedTorch(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            UpdateTorchPlacementPreview(selectedHotbarIndex, mouseWorld);

            return TryPlaceFurniture(input, selectedHotbarIndex, mouseWorld, ItemId.Torch, tile =>
            {
                furnitureItems.Add(new TorchInstance(tile, previewPoleFrameIndex, WorldMap.TileSize, previewFacingLeft));
            });
        }

        // P1E: Separate body and flame for world-space lighting vs emissive rendering
        public void DrawBody(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                TorchInstance torch = furnitureItems[i];
                Rectangle poleSource = new Rectangle(torch.PoleFrameIndex * PoleFrameSize, 0, PoleFrameSize, PoleFrameSize);
                SpriteEffects effects = torch.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(PoleTexture, torch.Bounds, poleSource, Color.White, 0f, Vector2.Zero, effects, 0f);
            }

            if (previewVisible)
            {
                TorchInstance preview = new TorchInstance(previewBaseTile, previewPoleFrameIndex, WorldMap.TileSize, previewFacingLeft);
                Rectangle poleSource = new Rectangle(previewPoleFrameIndex * PoleFrameSize, 0, PoleFrameSize, PoleFrameSize);
                SpriteEffects effects = previewFacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(PoleTexture, preview.Bounds, poleSource, previewValid ? ValidPreviewTint : InvalidPreviewTint, 0f, Vector2.Zero, effects, 0f);
            }
        }

        public void DrawFlames(SpriteBatch spriteBatch, float visualTimeSeconds)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                TorchInstance torch = furnitureItems[i];
                Point poleAnchor = PoleAnchors[torch.PoleFrameIndex];
                int flameFrame = GetFlameFrame(torch, visualTimeSeconds);
                Rectangle flameSource = new Rectangle(flameFrame * FlameFrameSize, 0, FlameFrameSize, FlameFrameSize);
                Vector2 flamePosition = new Vector2(
                    torch.Bounds.X + poleAnchor.X - FlameAnchor.X,
                    torch.Bounds.Y + poleAnchor.Y - FlameAnchor.Y);
                spriteBatch.Draw(FlameTexture, flamePosition, flameSource, Color.White);
            }
        }

        public void Draw(SpriteBatch spriteBatch, float visualTimeSeconds)
        {
            // P1E: Compatibility wrapper - draws both body and flames together (TILE mode)
            DrawBody(spriteBatch);
            DrawFlames(spriteBatch, visualTimeSeconds);
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

        public override bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target)
        {
            if (TryGetFurnitureIndexAtTile(tile, _ => true, out int index))
            {
                target = new WorldObjectMiningTarget(MiningDefinition, furnitureItems[index].Bounds);
                return true;
            }

            target = default;
            return false;
        }

        public override bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem)
        {
            if (worldItemRuntimeSystem == null || !TryGetFurnitureIndexAtTile(tile, _ => true, out int index))
                return false;

            TorchInstance torch = furnitureItems[index];
            worldItemRuntimeSystem.SpawnItemDrops(ItemId.Torch, 1, torch.Bounds.Center.ToVector2());
            furnitureItems.RemoveAt(index);
            revisions.MarkChanged();
            return true;
        }

        public override void OnForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            Point wrappedBrokenTile = new Point(WorldMap.WrapTileX(context.Tile.X), context.Tile.Y);
            bool removedAny = false;

            for (int i = furnitureItems.Count - 1; i >= 0; i--)
            {
                TorchInstance torch = furnitureItems[i];
                if (!IsSupportTile(torch, wrappedBrokenTile))
                    continue;

                context.WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Torch, 1, torch.Bounds.Center.ToVector2());
                furnitureItems.RemoveAt(i);
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

        private void UpdateTorchPlacementPreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            previewVisible = false;
            previewValid = false;
            previewPoleFrameIndex = 0;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || selectedSlot.ItemId != ItemId.Torch)
                return;

            Point baseTile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(baseTile.X, baseTile.Y))
                return;

            previewBaseTile = new Point(WorldMap.WrapTileX(baseTile.X), baseTile.Y);
            previewVisible = true;

            TorchInstance preview = new TorchInstance(previewBaseTile, 0, WorldMap.TileSize);
            Rectangle previewBounds = preview.Bounds;
            previewValid = ValidateTorchPlacement(previewBounds);
        }

        private bool ValidateTorchPlacement(Rectangle bounds)
        {
            return WorldObjectPlacementValidator.CanPlaceObject(WorldMap, Player, bounds) &&
                   TryResolvePoleFrame(bounds, out previewPoleFrameIndex) &&
                   !IntersectsExisting(bounds);
        }

        protected override bool ValidateSupport(Rectangle bounds)
        {
            return TryResolvePoleFrame(bounds, out _);
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

        protected override bool IntersectsExisting(Rectangle bounds)
        {
            for (int i = 0; i < furnitureItems.Count; i++)
            {
                if (furnitureItems[i].Bounds.Intersects(bounds))
                    return true;
            }

            return false;
        }

        public override bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            return false;
        }
    }
}
