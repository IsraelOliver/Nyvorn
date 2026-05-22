using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionBlockInteractionSystem
    {
        private const float BlockPlaceInterval = 0.08f;
        private const float MinimumMiningDurationSeconds = 0.15f;
        private const float TreeChopDurationSeconds = 3f;
        private const float PickaxeTreeChopStepSeconds = 0.3f;

        private Point miningTile = new Point(int.MinValue, int.MinValue);
        private TileType miningTileType = TileType.Empty;
        private ItemId miningToolItemId = ItemId.None;
        private float miningProgressSeconds;
        private int lastTreeChopAttackSequence = -1;
        private TreeInstance choppingTree;
        private Point choppingTreeTile = new Point(int.MinValue, int.MinValue);
        private float treeChopProgressSeconds;
        private float blockPlaceCooldownTimer;

        public required WorldMap WorldMap { get; init; }
        public SandSystem SandSystem { get; set; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }

        public Rectangle HoveredTileBounds { get; private set; }
        public WorldTilePreviewState HoveredTileState { get; private set; } = WorldTilePreviewState.Hidden;
        public Point ActiveMiningTile => miningTile;
        public float MiningProgressSeconds => miningProgressSeconds;
        public float MiningDurationSeconds { get; private set; }
        public float MiningProgressRatio => MiningDurationSeconds <= 0f
            ? 0f
            : MathHelper.Clamp(miningProgressSeconds / MiningDurationSeconds, 0f, 1f);

        public void Update(float dt)
        {
            if (blockPlaceCooldownTimer > 0f)
                blockPlaceCooldownTimer -= dt;
        }

        public void UpdateTilePreview(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            HoveredTileState = WorldTilePreviewState.Hidden;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (!selectedSlot.IsEmpty && (selectedSlot.ItemId == ItemId.SandBlock || selectedSlot.ItemId == ItemId.Workbench))
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

            if (IsPickaxeSelected(selectedSlot) && WorldMap.TryGetTreeAtTile(tile, out _))
            {
                HoveredTileState = inBreakRange
                    ? WorldTilePreviewState.BreakValid
                    : WorldTilePreviewState.BreakInvalid;
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

        public void TryBreakTargetBlock(float dt, InputState input, Vector2 mouseWorld, int selectedHotbarIndex)
        {
            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (Player.HasActiveAttackHitbox && TryChopTree(tile, selectedHotbarIndex))
                return;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (CanUseSelectedSlotForMining(selectedSlot))
            {
                TryMineTargetBlock(dt, input, selectedSlot.ItemId, tile);
                return;
            }

            ResetMiningProgress();
        }

        private void TryMineTargetBlock(float dt, InputState input, ItemId toolItemId, Point tile)
        {
            if (!input.AttackPressed)
            {
                ResetMiningProgress();
                return;
            }

            TileType targetTile = WorldMap.GetTile(tile.X, tile.Y);
            TileMiningDefinition miningDefinition = TileMiningDefinitions.Get(targetTile);
            if (!miningDefinition.IsMineable || !Player.CanBreakTile(targetTile))
            {
                ResetMiningProgress();
                return;
            }

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldBreakRange)
            {
                ResetMiningProgress();
                return;
            }

            if (miningTile != tile || miningTileType != targetTile || miningToolItemId != toolItemId)
            {
                miningTile = tile;
                miningTileType = targetTile;
                miningToolItemId = toolItemId;
                miningProgressSeconds = 0f;
            }

            MiningDurationSeconds = GetMiningDuration(miningDefinition);
            miningProgressSeconds += dt;
            if (miningProgressSeconds < MiningDurationSeconds)
                return;

            if (!WorldMap.TryBreakTile(tile.X, tile.Y, out TileType removedTile))
            {
                ResetMiningProgress();
                return;
            }

            SandSystem?.WakeAreaAboveTile(tile.X, tile.Y);
            WorldItemRuntimeSystem.SpawnBrokenBlockDrop(removedTile, tileCenter);
            ResetMiningProgress();
        }

        private bool TryChopTree(Point tile, int selectedHotbarIndex)
        {
            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (!IsPickaxeSelected(selectedSlot))
                return false;

            if (!WorldMap.TryGetTreeAtTile(tile, out TreeInstance tree))
                return false;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldBreakRange)
                return true;

            if (Player.AttackSequence == lastTreeChopAttackSequence)
                return true;

            if (!ReferenceEquals(choppingTree, tree) || choppingTreeTile != tile)
            {
                choppingTree = tree;
                choppingTreeTile = tile;
                treeChopProgressSeconds = 0f;
            }

            treeChopProgressSeconds += PickaxeTreeChopStepSeconds;
            lastTreeChopAttackSequence = Player.AttackSequence;

            if (treeChopProgressSeconds < TreeChopDurationSeconds)
                return true;

            if (WorldMap.TryChopTreeAtTile(tile, out int woodQuantity, out Vector2 dropPosition))
                WorldItemRuntimeSystem.SpawnItemDrops(ItemId.RawWood, woodQuantity, dropPosition);

            ResetTreeChopProgress();
            return true;
        }

        private static bool CanUseSelectedSlotForMining(InventorySlot slot)
        {
            if (slot.IsEmpty)
                return true;

            if (slot.ItemId == ItemId.SandBlock || slot.ItemId == ItemId.Workbench)
                return false;

            return !TileItemMapper.TryGetTileType(slot.ItemId, out _);
        }

        private void ResetTreeChopProgress()
        {
            choppingTree = null;
            choppingTreeTile = new Point(int.MinValue, int.MinValue);
            treeChopProgressSeconds = 0f;
        }

        private static bool IsPickaxeSelected(InventorySlot slot)
        {
            return !slot.IsEmpty && IsPickaxe(slot.ItemId);
        }

        private static bool IsPickaxe(ItemId itemId)
        {
            return itemId == ItemId.WoodPickaxe ||
                   itemId == ItemId.StonePickaxe ||
                   itemId == ItemId.IronPickaxe;
        }

        private float GetMiningDuration(TileMiningDefinition miningDefinition)
        {
            float miningSpeed = System.MathF.Max(0.001f, Player.MiningSpeed);
            return System.MathF.Max(MinimumMiningDurationSeconds, miningDefinition.Hardness / miningSpeed);
        }

        private void ResetMiningProgress()
        {
            miningTile = new Point(int.MinValue, int.MinValue);
            miningTileType = TileType.Empty;
            miningToolItemId = ItemId.None;
            miningProgressSeconds = 0f;
            MiningDurationSeconds = 0f;
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
