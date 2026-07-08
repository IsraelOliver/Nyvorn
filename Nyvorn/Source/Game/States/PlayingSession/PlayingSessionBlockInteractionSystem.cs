using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Liquids;
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
        private const float AxeTreeChopStepSeconds = 0.3f;
        private const float TreeSupportTileMiningDurationSeconds = TreeChopDurationSeconds;
        private const int PixelSandHarvestSize = 8;
        private const int PixelSandHarvestPixels = PixelSandHarvestSize * PixelSandHarvestSize;

        private Point miningTile = new Point(int.MinValue, int.MinValue);
        private TileType miningTileType = TileType.Empty;
        private bool miningWorldObject;
        private WorldObjectMiningDefinition miningWorldObjectDefinition;
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
        public LiquidSystem LiquidSystem { get; set; }
        public required Player Player { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public WorldObjectRegistry WorldObjectRegistry { get; set; }
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
            if (!constructionMode && !selectedSlot.IsEmpty && (selectedSlot.ItemId == ItemId.Sand || selectedSlot.ItemId == ItemId.Workbench || selectedSlot.ItemId == ItemId.WoodDoor))
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
                                (constructionMode || !HasForegroundPlacementBlocker(HoveredTileBounds)) &&
                                (constructionMode || !HoveredTileBounds.Intersects(Player.Hurtbox));

                HoveredTileState = canPlace ? WorldTilePreviewState.PlaceValid : WorldTilePreviewState.PlaceInvalid;
                return;
            }

            if (IsAxeSelected(selectedSlot) && WorldMap.TryGetTreeAtTile(tile, out _))
            {
                HoveredTileState = inBreakRange
                    ? WorldTilePreviewState.BreakValid
                    : WorldTilePreviewState.BreakInvalid;
                return;
            }

            if (CanUseSelectedSlotForMining(selectedSlot) &&
                WorldObjectRegistry != null &&
                WorldObjectRegistry.TryGetMiningTargetAtTile(tile, out WorldObjectMiningTarget objectMiningTarget))
            {
                bool inObjectBreakRange = Vector2.Distance(Player.Position, objectMiningTarget.Center) <= Player.WorldBreakRange;
                HoveredTileState = inObjectBreakRange && WorldObjectMining.CanMine(Player, objectMiningTarget.MiningDefinition)
                    ? WorldTilePreviewState.BreakValid
                    : WorldTilePreviewState.BreakInvalid;
                return;
            }

            if (CanUseSelectedSlotForMining(selectedSlot) && HasPixelSandInHarvestCell(tile))
            {
                HoveredTileBounds = GetPixelSandHarvestBounds(tile);
                HoveredTileState = inBreakRange && Player.CanBreakTile(TileType.Sand)
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

            if (selectedSlot.ItemId == ItemId.Sand)
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

            if (HasForegroundPlacementBlocker(tileBounds))
                return;

            if (!IsWithinBlockPlacementRange(tile))
                return;

            if (!WorldMap.TryPlaceTile(tile.X, tile.Y, tileType))
                return;

            LiquidSystem?.DisplaceLiquidForPlacedTile(tile.X, tile.Y);
            LiquidSystem?.WakeAreaAroundTile(tile.X, tile.Y);
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

            if (TryMineTargetWorldObject(dt, toolItemId, tile))
                return;

            if (TryMinePixelSand(dt, toolItemId, tile))
                return;

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

            if (miningWorldObject ||
                miningTile != tile ||
                miningTileType != targetTile ||
                miningToolItemId != toolItemId)
            {
                miningWorldObject = false;
                miningTile = tile;
                miningTileType = targetTile;
                miningToolItemId = toolItemId;
                miningProgressSeconds = 0f;
            }

            MiningDurationSeconds = GetForegroundTileMiningDuration(miningDefinition, tile);
            miningProgressSeconds += dt;
            if (miningProgressSeconds < MiningDurationSeconds)
                return;

            bool supportsTree = TryGetSupportedTree(tile, out TreeInstance supportedTree);
            if (!WorldMap.TryBreakTile(tile.X, tile.Y, out TileType removedTile))
            {
                ResetMiningProgress();
                return;
            }

            SandSystem?.WakeAreaAboveTile(tile.X, tile.Y);
            LiquidSystem?.WakeAreaAroundTile(tile.X, tile.Y);
            BlockParticleSystem?.SpawnFromTile(targetTile, tile, background: false);
            WorldItemRuntimeSystem.SpawnBrokenBlockDrop(removedTile, tileCenter);
            if (supportsTree && WorldMap.TryRemoveTree(supportedTree))
                WorldItemRuntimeSystem.SpawnItemDrops(ItemId.RawWood, System.Math.Max(1, supportedTree.Height), WorldMap.GetTileCenter(supportedTree.BaseTile.X, supportedTree.BaseTile.Y));
            WorldObjectRegistry?.NotifyForegroundTileBroken(new ForegroundTileBrokenContext(
                tile,
                removedTile,
                tileCenter,
                WorldItemRuntimeSystem));
            ResetMiningProgress();
        }

        private bool TryMinePixelSand(float dt, ItemId toolItemId, Point tile)
        {
            if (!HasPixelSandInHarvestCell(tile))
                return false;

            Rectangle harvestBounds = GetPixelSandHarvestBounds(tile);
            Vector2 harvestCenter = new Vector2(
                harvestBounds.X + (harvestBounds.Width * 0.5f),
                harvestBounds.Y + (harvestBounds.Height * 0.5f));

            if (!Player.CanBreakTile(TileType.Sand) ||
                Vector2.Distance(Player.Position, harvestCenter) > Player.WorldBreakRange)
            {
                ResetMiningProgress();
                return true;
            }

            if (miningWorldObject ||
                miningTile != tile ||
                miningTileType != TileType.Sand ||
                miningToolItemId != toolItemId)
            {
                miningWorldObject = false;
                miningTile = tile;
                miningTileType = TileType.Sand;
                miningToolItemId = toolItemId;
                miningProgressSeconds = 0f;
            }

            TileMiningDefinition miningDefinition = TileMiningDefinitions.Get(TileType.Sand);
            MiningDurationSeconds = GetMiningDuration(miningDefinition);
            miningProgressSeconds += dt;
            if (miningProgressSeconds < MiningDurationSeconds)
                return true;

            int removedPixels = SandSystem.RemoveSandInRectangle(
                harvestBounds.X,
                harvestBounds.Y,
                harvestBounds.Width,
                harvestBounds.Height,
                PixelSandHarvestPixels);

            if (removedPixels > 0)
            {
                int storedPixels = WorldItemRuntimeSystem.StoreItem(ItemId.Sand, removedPixels, preferInventory: false);
                int droppedPixels = removedPixels - storedPixels;
                if (droppedPixels > 0)
                    WorldItemRuntimeSystem.SpawnItemDrops(ItemId.Sand, droppedPixels, harvestCenter);
            }

            ResetMiningProgress();
            return true;
        }

        private bool TryMineTargetWorldObject(float dt, ItemId toolItemId, Point tile)
        {
            if (WorldObjectRegistry == null ||
                !WorldObjectRegistry.TryGetMiningTargetAtTile(tile, out WorldObjectMiningTarget target))
            {
                return false;
            }

            if (!WorldObjectMining.CanMine(Player, target.MiningDefinition))
            {
                ResetMiningProgress();
                return true;
            }

            if (Vector2.Distance(Player.Position, target.Center) > Player.WorldBreakRange)
            {
                ResetMiningProgress();
                return true;
            }

            if (!miningWorldObject ||
                miningTile != tile ||
                miningWorldObjectDefinition != target.MiningDefinition ||
                miningToolItemId != toolItemId)
            {
                miningWorldObject = true;
                miningTile = tile;
                miningTileType = TileType.Empty;
                miningWorldObjectDefinition = target.MiningDefinition;
                miningToolItemId = toolItemId;
                miningProgressSeconds = 0f;
            }

            MiningDurationSeconds = GetMiningDuration(target.MiningDefinition);
            miningProgressSeconds += dt;
            if (miningProgressSeconds < MiningDurationSeconds)
                return true;

            WorldObjectRegistry.TryMineObjectAtTile(tile, WorldItemRuntimeSystem);
            ResetMiningProgress();
            return true;
        }

        private bool TryChopTree(Point tile, int selectedHotbarIndex)
        {
            InventorySlot selectedSlot = Hotbar.GetSlot(selectedHotbarIndex);
            if (!IsAxeSelected(selectedSlot))
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

            treeChopProgressSeconds += AxeTreeChopStepSeconds;
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

            if (slot.ItemId == ItemId.Sand || slot.ItemId == ItemId.Workbench || slot.ItemId == ItemId.WoodDoor)
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

        private static bool IsAxeSelected(InventorySlot slot)
        {
            return !slot.IsEmpty && slot.ItemId == ItemId.WoodAxe;
        }

        private float GetMiningDuration(TileMiningDefinition miningDefinition)
        {
            return WorldObjectMining.GetMiningDuration(
                miningDefinition.Hardness,
                Player.MiningSpeed,
                MinimumMiningDurationSeconds);
        }

        private float GetForegroundTileMiningDuration(TileMiningDefinition miningDefinition, Point tile)
        {
            float duration = GetMiningDuration(miningDefinition);
            if (IsTreeSupportTile(tile))
                return System.Math.Max(duration, TreeSupportTileMiningDurationSeconds);

            return duration;
        }

        private float GetMiningDuration(WorldObjectMiningDefinition miningDefinition)
        {
            return WorldObjectMining.GetMiningDuration(
                miningDefinition.Hardness,
                Player.MiningSpeed,
                MinimumMiningDurationSeconds);
        }

        private void ResetMiningProgress()
        {
            miningTile = new Point(int.MinValue, int.MinValue);
            miningTileType = TileType.Empty;
            miningWorldObject = false;
            miningWorldObjectDefinition = default;
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

            if (WorldMap.IsSolidAt(tile.X, tile.Y) ||
                SandSystem.HasSandAt(pixelX, pixelY) ||
                LiquidSystem?.HasLiquidAt(pixelX, pixelY) == true)
            {
                return;
            }

            if (!IsWithinBlockPlacementRange(tile))
                return;

            SandSystem.SetSandAt(pixelX, pixelY, true);
            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        private bool HasForegroundDynamicMatter(Rectangle bounds)
        {
            return (SandSystem != null && SandSystem.HasSandInRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)) ||
                   (LiquidSystem != null && LiquidSystem.HasLiquidInRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        private bool HasForegroundPlacementBlocker(Rectangle bounds)
        {
            return SandSystem != null && SandSystem.HasSandInRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        private bool IsTreeSupportTile(Point tile)
        {
            return TryGetSupportedTree(tile, out _);
        }

        private bool TryGetSupportedTree(Point tile, out TreeInstance tree)
        {
            Point above = new Point(tile.X, tile.Y - 1);
            if (WorldMap.InBounds(above.X, above.Y) && WorldMap.TryGetTreeAtTile(above, out tree))
                return true;

            tree = null;
            return false;
        }

        private bool HasPixelSandInHarvestCell(Point tile)
        {
            if (SandSystem == null || !WorldMap.InBounds(tile.X, tile.Y))
                return false;

            Rectangle harvestBounds = GetPixelSandHarvestBounds(tile);
            return SandSystem.HasSandInRectangle(
                harvestBounds.X,
                harvestBounds.Y,
                harvestBounds.Width,
                harvestBounds.Height);
        }

        private Rectangle GetPixelSandHarvestBounds(Point tile)
        {
            Rectangle tileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            return new Rectangle(
                tileBounds.X,
                tileBounds.Y,
                System.Math.Min(PixelSandHarvestSize, tileBounds.Width),
                System.Math.Min(PixelSandHarvestSize, tileBounds.Height));
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
