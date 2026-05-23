using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.Gameplay.World.Particles;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionBlockInteractionSystem
    {
        private const float BlockPlaceInterval = 0.08f;
        private const int BlockPlaceSideRangeTiles = 5;
        private const int BlockPlaceUpRangeTiles = 6;
        private const int BlockPlaceDownRangeTiles = 3;
        private const float MinimumMiningDurationSeconds = 0.15f;
        private const float TreeChopDurationSeconds = 3f;
        private const float PickaxeTreeChopStepSeconds = 0.3f;

        private Point miningTile = new Point(int.MinValue, int.MinValue);
        private TileType miningTileType = TileType.Empty;
        private ItemId miningToolItemId = ItemId.None;
        private float miningProgressSeconds;
        private Point backgroundMiningTile = new Point(int.MinValue, int.MinValue);
        private TileType backgroundMiningTileType = TileType.Empty;
        private ItemId backgroundMiningToolItemId = ItemId.None;
        private float backgroundMiningProgressSeconds;
        private int lastTreeChopAttackSequence = -1;
        private TreeInstance choppingTree;
        private Point choppingTreeTile = new Point(int.MinValue, int.MinValue);
        private float treeChopProgressSeconds;
        private float blockPlaceCooldownTimer;
        private float backgroundPlaceCooldownTimer;

        public required WorldMap WorldMap { get; init; }
        public SandSystem SandSystem { get; set; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public DoorRuntimeSystem DoorRuntimeSystem { get; set; }
        public BlockParticleSystem BlockParticleSystem { get; set; }

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

            if (backgroundPlaceCooldownTimer > 0f)
                backgroundPlaceCooldownTimer -= dt;
        }

        public void UpdateTilePreview(int selectedHotbarIndex, Vector2 mouseWorld, bool constructionMode)
        {
            HoveredTileState = WorldTilePreviewState.Hidden;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (!constructionMode && !selectedSlot.IsEmpty && (selectedSlot.ItemId == ItemId.SandBlock || selectedSlot.ItemId == ItemId.Workbench || selectedSlot.ItemId == ItemId.WoodDoor))
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            HoveredTileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            bool inRange = IsWithinBlockPlacementRange(tile);
            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            bool inBreakRange = Vector2.Distance(Player.Position, tileCenter) <= Player.WorldBreakRange;

            if (!selectedSlot.IsEmpty && TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType placeTileType))
            {
                bool canPlace = inRange &&
                                placeTileType != TileType.Empty &&
                                (constructionMode
                                    ? WorldMap.CanPlaceBackgroundTile(tile.X, tile.Y, placeTileType)
                                    : WorldMap.CanPlaceTile(tile.X, tile.Y, placeTileType)) &&
                                (constructionMode || !HoveredTileBounds.Intersects(Player.Hurtbox));

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

            if (!IsWithinBlockPlacementRange(tile))
                return;

            if (!WorldMap.TryPlaceTile(tile.X, tile.Y, tileType))
                return;

            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        public void TryUseConstructionModeAction(float dt, InputState input, int selectedHotbarIndex, Vector2 mouseWorld)
        {
            if (!input.ActivePowerPressed)
            {
                ResetBackgroundMiningProgress();
                return;
            }

            if (backgroundPlaceCooldownTimer > 0f)
                return;

            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty)
            {
                ResetBackgroundMiningProgress();
                return;
            }

            if (IsPickaxeSelected(selectedSlot))
            {
                TryMineBackgroundTile(dt, selectedSlot.ItemId, mouseWorld);
                return;
            }

            ResetBackgroundMiningProgress();

            if (!TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType tileType))
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!IsWithinBlockPlacementRange(tile))
                return;

            if (!WorldMap.TryPlaceBackgroundTile(tile.X, tile.Y, tileType))
                return;

            selectedSlot.RemoveOne();
            backgroundPlaceCooldownTimer = BlockPlaceInterval;
        }

        public bool ShouldAnimateConstructionPickaxe(int selectedHotbarIndex, Vector2 mouseWorld)
        {
            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (!IsPickaxeSelected(selectedSlot))
                return false;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!IsWithinBlockPlacementRange(tile))
                return false;

            TileType targetTile = WorldMap.GetBackgroundTile(tile.X, tile.Y);
            TileMiningDefinition miningDefinition = TileMiningDefinitions.Get(targetTile);
            return miningDefinition.IsMineable && Player.CanBreakTile(targetTile);
        }

        private void TryMineBackgroundTile(float dt, ItemId toolItemId, Vector2 mouseWorld)
        {
            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!IsWithinBlockPlacementRange(tile))
            {
                ResetBackgroundMiningProgress();
                return;
            }

            TileType targetTile = WorldMap.GetBackgroundTile(tile.X, tile.Y);
            TileMiningDefinition miningDefinition = TileMiningDefinitions.Get(targetTile);
            if (!miningDefinition.IsMineable || !Player.CanBreakTile(targetTile))
            {
                ResetBackgroundMiningProgress();
                return;
            }

            if (backgroundMiningTile != tile ||
                backgroundMiningTileType != targetTile ||
                backgroundMiningToolItemId != toolItemId)
            {
                backgroundMiningTile = tile;
                backgroundMiningTileType = targetTile;
                backgroundMiningToolItemId = toolItemId;
                backgroundMiningProgressSeconds = 0f;
            }

            backgroundMiningProgressSeconds += dt;
            if (backgroundMiningProgressSeconds < GetMiningDuration(miningDefinition))
                return;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (WorldMap.TryBreakBackgroundTile(tile.X, tile.Y, out TileType removedTile))
            {
                BlockParticleSystem?.SpawnFromTile(targetTile, tile, background: true);
                WorldItemRuntimeSystem.SpawnBrokenBlockDrop(removedTile, tileCenter);
            }

            ResetBackgroundMiningProgress();
        }

        private bool IsWithinBlockPlacementRange(Point targetTile)
        {
            Point playerTile = WorldMap.WorldToTile(Player.Position);
            int targetX = WorldMap.WrapTileX(targetTile.X);
            int playerX = WorldMap.WrapTileX(playerTile.X);
            int deltaX = targetX - playerX;
            int halfWorldWidth = WorldMap.Width / 2;

            if (deltaX > halfWorldWidth)
                deltaX -= WorldMap.Width;
            else if (deltaX < -halfWorldWidth)
                deltaX += WorldMap.Width;

            int deltaY = targetTile.Y - playerTile.Y;
            return System.Math.Abs(deltaX) <= BlockPlaceSideRangeTiles &&
                   deltaY >= -BlockPlaceUpRangeTiles &&
                   deltaY <= BlockPlaceDownRangeTiles;
        }

        public void TryBreakTargetBlock(float dt, InputState input, Vector2 mouseWorld, int selectedHotbarIndex)
        {
            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (input.AttackPressed && Player.HasActiveAttackHitbox && TryChopTree(tile, selectedHotbarIndex))
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
            BlockParticleSystem?.SpawnFromTile(targetTile, tile, background: false);
            WorldItemRuntimeSystem.SpawnBrokenBlockDrop(removedTile, tileCenter);
            DoorRuntimeSystem?.RemoveDoorsAffectedByBrokenTile(tile, door =>
                WorldItemRuntimeSystem.SpawnItemDrops(ItemId.WoodDoor, 1, door.InteractionPosition));
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

            if (slot.ItemId == ItemId.SandBlock || slot.ItemId == ItemId.Workbench || slot.ItemId == ItemId.WoodDoor)
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

        private void ResetBackgroundMiningProgress()
        {
            backgroundMiningTile = new Point(int.MinValue, int.MinValue);
            backgroundMiningTileType = TileType.Empty;
            backgroundMiningToolItemId = ItemId.None;
            backgroundMiningProgressSeconds = 0f;
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

            if (!IsWithinBlockPlacementRange(tile))
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
