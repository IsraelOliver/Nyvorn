using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionBlockInteractionSystem
    {
        private const float BlockPlaceInterval = 0.08f;

        private int lastBlockBreakAttackSequence = -1;
        private Point lastBrokenBlockTile = new Point(int.MinValue, int.MinValue);
        private float blockPlaceCooldownTimer;

        public required WorldMap WorldMap { get; init; }
        public SandSystem SandSystem { get; set; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }

        public Rectangle HoveredTileBounds { get; private set; }
        public WorldTilePreviewState HoveredTileState { get; private set; } = WorldTilePreviewState.Hidden;

        public void Update(float dt)
        {
            if (blockPlaceCooldownTimer > 0f)
                blockPlaceCooldownTimer -= dt;
        }

        public void UpdateTilePreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            HoveredTileState = WorldTilePreviewState.Hidden;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (!selectedSlot.IsEmpty && selectedSlot.ItemId == ItemId.SandBlock)
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            HoveredTileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            bool inRange = Vector2.Distance(Player.Position, tileCenter) <= Player.WorldInteractionRange;
            bool inBreakRange = Vector2.Distance(Player.Position, tileCenter) <= Player.WorldBreakRange;

            if (!selectedSlot.IsEmpty && TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType placeTileType))
            {
                bool canPlace = inRange &&
                                placeTileType != TileType.Empty &&
                                WorldMap.CanPlaceTile(tile.X, tile.Y, placeTileType) &&
                                !HoveredTileBounds.Intersects(Player.Hurtbox);

                HoveredTileState = canPlace ? WorldTilePreviewState.PlaceValid : WorldTilePreviewState.PlaceInvalid;
                return;
            }

            TileType targetTile = WorldMap.GetTile(tile.X, tile.Y);
            if (!WorldMap.IsSolid(targetTile))
                return;

            HoveredTileState = inBreakRange && Player.CanBreakTile(targetTile)
                ? WorldTilePreviewState.BreakValid
                : WorldTilePreviewState.BreakInvalid;
        }

        public void TryPlaceSelectedBlock(InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            if (!input.PlacePressed)
            {
                blockPlaceCooldownTimer = 0f;
                return;
            }

            if (blockPlaceCooldownTimer > 0f)
                return;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty)
                return;

            if (selectedSlot.ItemId == ItemId.SandBlock)
            {
                TryPlaceSandPixel(selectedSlot, mouseWorld);
                return;
            }

            if (!TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType tileType))
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            Rectangle tileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            if (tileBounds.Intersects(Player.Hurtbox))
                return;

            if (SandSystem != null && SandSystem.HasSandInRectangle(tileBounds.X, tileBounds.Y, tileBounds.Width, tileBounds.Height))
                return;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldInteractionRange)
                return;

            if (!WorldMap.TryPlaceTile(tile.X, tile.Y, tileType))
                return;

            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        public void TryBreakTargetBlock(Vector2 mouseWorld)
        {
            if (!Player.HasActiveAttackHitbox)
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (Player.AttackSequence == lastBlockBreakAttackSequence && tile == lastBrokenBlockTile)
                return;

            TileType targetTile = WorldMap.GetTile(tile.X, tile.Y);
            if (!Player.CanBreakTile(targetTile))
                return;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldBreakRange)
                return;

            if (!WorldMap.TryBreakTile(tile.X, tile.Y, out TileType removedTile))
                return;

            SandSystem?.WakeAreaAboveTile(tile.X, tile.Y);
            WorldItemRuntimeSystem.SpawnBrokenBlockDrop(removedTile, tileCenter);
            lastBlockBreakAttackSequence = Player.AttackSequence;
            lastBrokenBlockTile = tile;
        }

        private void TryPlaceSandPixel(InventorySlot selectedSlot, Vector2 mouseWorld)
        {
            if (blockPlaceCooldownTimer > 0f || SandSystem == null)
                return;

            int pixelX = WrapPixelX((int)System.MathF.Floor(mouseWorld.X));
            int pixelY = (int)System.MathF.Floor(mouseWorld.Y);
            if (pixelY < 0 || pixelY >= SandSystem.Height)
                return;

            Point tile = WorldMap.WorldToTile(new Vector2(pixelX, pixelY));
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            if (WorldMap.IsSolidAt(tile.X, tile.Y) || SandSystem.HasSandAt(pixelX, pixelY))
                return;

            Vector2 pixelCenter = new Vector2(pixelX + 0.5f, pixelY + 0.5f);
            if (Vector2.Distance(Player.Position, pixelCenter) > Player.WorldInteractionRange)
                return;

            SandSystem.SetSandAt(pixelX, pixelY, true);
            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        private int WrapPixelX(int pixelX)
        {
            int worldWidth = WorldMap.PixelWidth;
            if (worldWidth <= 0)
                return 0;

            int wrapped = pixelX % worldWidth;
            return wrapped < 0 ? wrapped + worldWidth : wrapped;
        }
    }
}
