using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Tissue;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World
{
    public class WorldMap
    {
        private const int AutoTileSheetTileSize = 8;
        private const int AutoTileSheetSpacing = 1;
        private const int DefaultChunkTileSize = 32;
        private const int MaxCachedChunks = 96;
        private static readonly Color BackgroundTileTint = new Color(104, 104, 104, 210);

        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }
        public int TileRevision { get; private set; }
        public bool HasUnsavedChanges => TileRevision != _persistedTileRevision || HasUnsavedTissueChanges;
        public int PixelWidth => Width * TileSize;
        public TissueField TissueField => _tissueField;
        public int TissueRevision { get; private set; }
        public bool HasUnsavedTissueChanges => _tissueField?.HasUnsavedChanges ?? false;
        public int ChunkTileSize => DefaultChunkTileSize;
        public int ChunkCountX => (Width + ChunkTileSize - 1) / ChunkTileSize;
        public int ChunkCountY => (Height + ChunkTileSize - 1) / ChunkTileSize;
        public IReadOnlyList<TreeInstance> Trees => _trees;

        internal event Action<TissueChangedEvent> TissueChanged;

        private TileWetnessField _wetnessField;
        private Color _ambientLight = Color.White;
        private Texture2D _dirt;
        private Texture2D _grass;
        private Texture2D _sand;
        private Texture2D _stone;
        private Texture2D _wood;
        private Texture2D _ironOre;
        private Texture2D _treeTexture;
        private TissueField _tissueField;
        private int _persistedTileRevision;
        private SpriteBatch _chunkRenderSpriteBatch;
        private int _chunkRenderTick;

        private readonly TileType[,] _tiles;
        private readonly TileType[,] _backgroundTiles;
        private readonly byte[,] _autoTileVariants;
        private readonly byte[,] _backgroundAutoTileVariants;
        private readonly List<TreeInstance> _trees = new();
        private readonly TreeRenderer _treeRenderer = new();
        private readonly Dictionary<WorldChunkCoord, ChunkRenderCache> _chunkCaches = new();
        private readonly Dictionary<long, TileType> _trackedTileBaselines = new();
        private readonly Dictionary<long, WorldTileChange> _trackedTileChanges = new();
        private Func<int, int, bool> _objectOccupancyQuery;
        private Func<int, int, bool> _movementBlockQuery;

        public WorldMap(int width, int height, int tileSize)
        {
            Width = width;
            Height = height;
            TileSize = tileSize;

            _tiles = new TileType[Width, Height];
            _backgroundTiles = new TileType[Width, Height];
            _autoTileVariants = new byte[Width, Height];
            _backgroundAutoTileVariants = new byte[Width, Height];
        }

        public bool IsTrackingTileChanges { get; private set; }
        public IReadOnlyCollection<WorldTileChange> TrackedTileChanges => _trackedTileChanges.Values;

        public TileType GetTile(int x, int y)
        {
            if (!InBounds(x, y))
                return TileType.Empty;

            return _tiles[WrapTileX(x), y];
        }

        public void SetTile(int x, int y, TileType type)
        {
            if (!InBounds(x, y))
                return;

            int wrappedX = WrapTileX(x);
            TileType currentTile = _tiles[wrappedX, y];
            if (currentTile == type)
                return;

            TrackTileChange(wrappedX, y, currentTile, type);
            _tiles[wrappedX, y] = type;
            HandleTissueTileTransition(wrappedX, y, currentTile, type);
            RefreshAutoTileNeighborhood(wrappedX, y);
            MarkChunkNeighborhoodDirty(wrappedX, y);
            TileRevision++;
        }

        public byte[] ExportTileSnapshot()
        {
            byte[] snapshot = new byte[Width * Height];
            int index = 0;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    snapshot[index++] = (byte)_tiles[x, y];
            }

            return snapshot;
        }

        public void ImportTileSnapshot(byte[] snapshot)
        {
            if (snapshot == null)
                throw new System.ArgumentNullException(nameof(snapshot));

            if (snapshot.Length != Width * Height)
                throw new System.ArgumentException("Tile snapshot size does not match world dimensions.", nameof(snapshot));

            int index = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    _tiles[x, y] = (TileType)snapshot[index++];
            }

            ResetTrackedTileChanges();
            RebuildAutoTileVariants();
            MarkAllChunkCachesDirty();
            TileRevision++;
        }

        public byte[] ExportBackgroundTileSnapshot()
        {
            byte[] snapshot = new byte[Width * Height];
            int index = 0;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    snapshot[index++] = (byte)_backgroundTiles[x, y];
            }

            return snapshot;
        }

        public void ImportBackgroundTileSnapshot(byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length == 0)
            {
                ClearBackgroundTiles();
                return;
            }

            if (snapshot.Length != Width * Height)
                throw new System.ArgumentException("Background tile snapshot size does not match world dimensions.", nameof(snapshot));

            int index = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    _backgroundTiles[x, y] = (TileType)snapshot[index++];
            }

            RebuildBackgroundAutoTileVariants();
            TileRevision++;
        }

        public void ClearBackgroundTiles()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    _backgroundTiles[x, y] = TileType.Empty;
            }

            RebuildBackgroundAutoTileVariants();
        }

        public void SetTissueField(TissueField tissueField)
        {
            if (ReferenceEquals(_tissueField, tissueField))
                return;

            if (_tissueField != null)
                _tissueField.Changed -= HandleTissueFieldChanged;

            _tissueField = tissueField;
            if (_tissueField != null)
            {
                _tissueField.SetOccupancyValidator(IsSolidAt);
                _tissueField.Changed += HandleTissueFieldChanged;
            }
            TissueRevision++;
        }

        public TissueCellState GetTissueState(int x, int y)
        {
            if (_tissueField == null || !InBounds(x, y))
                return TissueCellState.Neutral;

            return _tissueField.GetState(WrapTileX(x), y);
        }

        public bool TrySetTissueState(int x, int y, TissueCellState state)
        {
            if (_tissueField == null || !InBounds(x, y))
                return false;

            int wrappedX = WrapTileX(x);
            if (!state.IsNeutral && !IsSolidAt(wrappedX, y))
                return false;

            return _tissueField.SetState(wrappedX, y, state);
        }

        public bool ClearTissueAt(int x, int y)
        {
            if (_tissueField == null || !InBounds(x, y))
                return false;

            return _tissueField.Clear(WrapTileX(x), y);
        }

        public bool ResetTissueAt(int x, int y)
        {
            if (_tissueField == null || !InBounds(x, y))
                return false;

            int wrappedX = WrapTileX(x);
            if (!IsSolidAt(wrappedX, y))
                return false;

            return _tissueField.ResetOverride(wrappedX, y);
        }

        public void MarkPersisted()
        {
            _persistedTileRevision = TileRevision;
            _tissueField?.MarkPersisted();
        }

        public void MarkTissueDirty()
        {
            TissueRevision++;
        }

        private void HandleTissueTileTransition(int x, int y, TileType previousTile, TileType nextTile)
        {
            if (_tissueField == null)
                return;

            if (IsSolid(previousTile) && !IsSolid(nextTile))
                _tissueField.Clear(x, y);
        }

        private void HandleTissueFieldChanged(TissueFieldChange change)
        {
            TissueRevision++;
            TissueChanged?.Invoke(new TissueChangedEvent(
                new Point(change.X, change.Y),
                change.Previous,
                change.Current,
                TissueRevision));
        }

        public bool InBounds(int x, int y)
            => y >= 0 && y < Height;

        public int WrapTileX(int x)
        {
            int wrapped = x % Width;
            return wrapped < 0 ? wrapped + Width : wrapped;
        }

        public int WrapChunkX(int chunkX)
        {
            int wrapped = chunkX % ChunkCountX;
            return wrapped < 0 ? wrapped + ChunkCountX : wrapped;
        }

        public bool IsSolid(TileType tileType)
        {
            return tileType == TileType.Dirt
                || tileType == TileType.Grass
                || tileType == TileType.Stone
                || tileType == TileType.Sand
                || tileType == TileType.Wood
                || tileType == TileType.IronOre;
        }

        public bool IsSolidAt(int x, int y) => IsSolid(GetTile(x, y));

        // True when nothing solid sits between this tile and the top of the map — used to gate
        // rain wetting/accumulation and weather shelter (audio muffling, hail fairness) to tiles
        // actually exposed to the sky, rather than the whole loaded area.
        public bool HasOpenSkyAbove(int x, int y)
        {
            int wrappedX = WrapTileX(x);
            for (int scanY = y - 1; scanY >= 0; scanY--)
            {
                if (IsSolidAt(wrappedX, scanY))
                    return false;
            }

            return true;
        }

        // Also doubles as a shelter query for the night-overlay tint (PlayingSession.cs), which
        // softens darkening under a roof - not audio-exclusive despite the name.
        public bool IsWeatherAudioMuffled(int x, int y) => !HasOpenSkyAbove(x, y);

        public void SetObjectCollisionQueries(
            Func<int, int, bool> objectOccupancyQuery,
            Func<int, int, bool> movementBlockQuery)
        {
            _objectOccupancyQuery = objectOccupancyQuery;
            _movementBlockQuery = movementBlockQuery;
        }

        public bool IsObjectOccupiedAt(int x, int y)
        {
            if (!InBounds(x, y))
                return false;

            return _objectOccupancyQuery?.Invoke(WrapTileX(x), y) == true;
        }

        public bool IsMovementBlockedAt(int x, int y)
        {
            if (!InBounds(x, y))
                return false;

            return IsSolidAt(x, y) || _movementBlockQuery?.Invoke(WrapTileX(x), y) == true;
        }

        public bool HasAdjacentSolid(int x, int y)
        {
            return IsSolidAt(x - 1, y)
                || IsSolidAt(x + 1, y)
                || IsSolidAt(x, y - 1)
                || IsSolidAt(x, y + 1);
        }

        public Rectangle GetTileBounds(int x, int y)
            => new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);

        public WorldChunkCoord GetChunkCoordForTile(int tileX, int tileY)
        {
            int wrappedTileX = WrapTileX(tileX);
            int clampedTileY = System.Math.Clamp(tileY, 0, Height - 1);
            return new WorldChunkCoord(
                wrappedTileX / ChunkTileSize,
                clampedTileY / ChunkTileSize);
        }

        public Rectangle GetChunkTileBounds(WorldChunkCoord chunkCoord)
        {
            int chunkX = WrapChunkX(chunkCoord.X);
            int chunkY = System.Math.Clamp(chunkCoord.Y, 0, ChunkCountY - 1);
            int tileX = chunkX * ChunkTileSize;
            int tileY = chunkY * ChunkTileSize;
            int width = System.Math.Min(ChunkTileSize, Width - tileX);
            int height = System.Math.Min(ChunkTileSize, Height - tileY);
            return new Rectangle(tileX, tileY, width, height);
        }

        public Rectangle GetChunkWorldBounds(WorldChunkCoord chunkCoord)
        {
            Rectangle tileBounds = GetChunkTileBounds(chunkCoord);
            return new Rectangle(
                tileBounds.X * TileSize,
                tileBounds.Y * TileSize,
                tileBounds.Width * TileSize,
                tileBounds.Height * TileSize);
        }

        public Point WorldToTile(Vector2 worldPos)
            => new Point(
                (int)System.MathF.Floor(worldPos.X / TileSize),
                (int)System.MathF.Floor(worldPos.Y / TileSize));

        public Vector2 GetTileCenter(int x, int y)
            => new Vector2(x * TileSize + (TileSize * 0.5f), y * TileSize + (TileSize * 0.5f));

        public bool TryBreakTile(int x, int y, out TileType removedTile)
        {
            removedTile = TileType.Empty;

            if (!InBounds(x, y))
                return false;

            int wrappedX = WrapTileX(x);
            TileType currentTile = _tiles[wrappedX, y];
            if (!IsSolid(currentTile))
                return false;

            removedTile = currentTile;
            TrackTileChange(wrappedX, y, currentTile, TileType.Empty);
            _tiles[wrappedX, y] = TileType.Empty;
            HandleTissueTileTransition(wrappedX, y, currentTile, TileType.Empty);
            RefreshAutoTileNeighborhood(wrappedX, y);
            MarkChunkNeighborhoodDirty(wrappedX, y);
            TileRevision++;
            return true;
        }

        public bool CanPlaceTile(int x, int y, TileType tileType)
        {
            if (!InBounds(x, y))
                return false;

            if (tileType == TileType.Empty)
                return false;

            if (GetTile(x, y) != TileType.Empty)
                return false;

            if (IsObjectOccupiedAt(x, y))
                return false;

            return HasAdjacentSolid(x, y);
        }

        public bool TryPlaceTile(int x, int y, TileType tileType)
        {
            if (!CanPlaceTile(x, y, tileType))
                return false;

            int wrappedX = WrapTileX(x);
            TrackTileChange(wrappedX, y, TileType.Empty, tileType);
            _tiles[wrappedX, y] = tileType;
            HandleTissueTileTransition(wrappedX, y, TileType.Empty, tileType);
            RefreshAutoTileNeighborhood(wrappedX, y);
            MarkChunkNeighborhoodDirty(wrappedX, y);
            TileRevision++;
            return true;
        }

        public TileType GetBackgroundTile(int x, int y)
        {
            if (!InBounds(x, y))
                return TileType.Empty;

            return _backgroundTiles[WrapTileX(x), y];
        }

        public bool CanPlaceBackgroundTile(int x, int y, TileType tileType)
        {
            if (!InBounds(x, y))
                return false;

            if (tileType == TileType.Empty)
                return false;

            if (!IsSolid(tileType))
                return false;

            return GetBackgroundTile(x, y) == TileType.Empty;
        }

        public bool TryPlaceBackgroundTile(int x, int y, TileType tileType)
        {
            if (!CanPlaceBackgroundTile(x, y, tileType))
                return false;

            int wrappedX = WrapTileX(x);
            _backgroundTiles[wrappedX, y] = tileType;
            RefreshBackgroundAutoTileNeighborhood(wrappedX, y);
            TileRevision++;
            return true;
        }

        public bool TryBreakBackgroundTile(int x, int y, out TileType removedTile)
        {
            removedTile = TileType.Empty;

            if (!InBounds(x, y))
                return false;

            int wrappedX = WrapTileX(x);
            TileType currentTile = _backgroundTiles[wrappedX, y];
            if (!IsSolid(currentTile))
                return false;

            removedTile = currentTile;
            _backgroundTiles[wrappedX, y] = TileType.Empty;
            RefreshBackgroundAutoTileNeighborhood(wrappedX, y);
            TileRevision++;
            return true;
        }

        public bool TryGetTileParticleRenderData(
            TileType tile,
            int x,
            int y,
            bool background,
            out Texture2D texture,
            out Rectangle sourceRectangle,
            out Color tint)
        {
            texture = GetTextureForTile(tile);
            sourceRectangle = Rectangle.Empty;
            tint = background ? BackgroundTileTint : Color.White;

            if (tile == TileType.Empty || texture == null || !InBounds(x, y))
                return false;

            sourceRectangle = tile switch
            {
                TileType.Dirt => GetDirtAutoTileSourceRectangle(x, y, background),
                TileType.Grass => GetDirtAutoTileSourceRectangle(x, y, background),
                TileType.Stone => GetDirtAutoTileSourceRectangle(x, y, background),
                TileType.Sand => background ? GetBackgroundAutoTileSourceRectangle(x, y) : GetAutoTileSourceRectangle(x, y),
                TileType.Wood => GetDirtAutoTileSourceRectangle(x, y, background),
                _ => Rectangle.Empty
            };

            return sourceRectangle != Rectangle.Empty;
        }

        public void BeginTileChangeTracking()
        {
            IsTrackingTileChanges = true;
        }

        public void SuspendTileChangeTracking()
        {
            IsTrackingTileChanges = false;
        }

        public void ResetTrackedTileChanges()
        {
            _trackedTileBaselines.Clear();
            _trackedTileChanges.Clear();
        }

        public void ApplyPersistentTileChanges(IEnumerable<WorldTileChange> tileChanges)
        {
            if (tileChanges == null)
                return;

            bool previousTrackingState = IsTrackingTileChanges;
            SuspendTileChangeTracking();

            foreach (WorldTileChange tileChange in tileChanges)
            {
                if (!InBounds(tileChange.X, tileChange.Y))
                    continue;

                int wrappedX = WrapTileX(tileChange.X);
                TileType baselineTile = _tiles[wrappedX, tileChange.Y];
                if (baselineTile == tileChange.TileType)
                    continue;

                long key = CreateTileKey(wrappedX, tileChange.Y);
                _trackedTileBaselines[key] = baselineTile;
                _trackedTileChanges[key] = new WorldTileChange(wrappedX, tileChange.Y, tileChange.TileType);
                _tiles[wrappedX, tileChange.Y] = tileChange.TileType;
                HandleTissueTileTransition(wrappedX, tileChange.Y, baselineTile, tileChange.TileType);
                RefreshAutoTileNeighborhood(wrappedX, tileChange.Y);
                MarkChunkNeighborhoodDirty(wrappedX, tileChange.Y);
                TileRevision++;
            }

            if (previousTrackingState)
                BeginTileChangeTracking();
        }

        public void SetWetnessField(TileWetnessField wetnessField)
        {
            _wetnessField = wetnessField;
        }

        // Called once per frame from the sky/environment system - SkyState.AmbientLight, tinting
        // sky-exposed tiles/decorations so the ground reads warm at sunset and cool at night
        // instead of always rendering at flat Color.White regardless of time of day.
        public void SetAmbientLight(Color ambientLight)
        {
            _ambientLight = ambientLight;
        }

        public void SetTextures(Texture2D dirt, Texture2D grass, Texture2D sand, Texture2D stone, Texture2D wood, Texture2D ironOre)
        {
            _dirt = dirt;
            _grass = grass;
            _sand = sand;
            _stone = stone;
            _wood = wood;
            _ironOre = ironOre;
            RebuildAutoTileVariants();
            MarkAllChunkCachesDirty();
        }

        public void SetTreeTexture(Texture2D treeTexture)
        {
            _treeTexture = treeTexture;
        }

        public void SetTrees(IEnumerable<TreeInstance> trees)
        {
            _trees.Clear();
            if (trees != null)
                _trees.AddRange(trees);
        }

        public bool TryGetTreeAtTile(Point tile, out TreeInstance tree)
        {
            return TryGetTreePartAtTile(tile, out tree, out _);
        }

        public bool TryChopTreeAtTile(Point tile, out int woodQuantity, out Vector2 dropPosition)
        {
            woodQuantity = 0;
            dropPosition = Vector2.Zero;

            if (!TryGetTreePartAtTile(tile, out TreeInstance tree, out TreePartPlacement cutPart))
                return false;

            if (cutPart.PartType == TreePartType.RootLeft || cutPart.PartType == TreePartType.RootRight)
            {
                ChopTreeRoot(tree, cutPart.PartType);
                woodQuantity = 1;
                dropPosition = GetTileCenter(tile.X, tile.Y);
                TileRevision++;
                return true;
            }

            int cutOffsetY = System.Math.Min(0, cutPart.OffsetTiles.Y);
            if (cutOffsetY == 0)
            {
                woodQuantity = System.Math.Max(1, tree.Height);
                dropPosition = GetTileCenter(tree.BaseTile.X, tree.BaseTile.Y);
                _trees.Remove(tree);
                TileRevision++;
                return true;
            }

            if (cutOffsetY == -1)
            {
                woodQuantity = CountChoppedWood(tree, cutOffsetY);
                dropPosition = GetTileCenter(tile.X, tile.Y);

                tree.Parts.Clear();
                tree.Parts.Add(new TreePartPlacement(TreePartType.TrunkBaseCut, Point.Zero));
                tree.HasCanopy = false;
                TileRevision++;
                return true;
            }

            int stumpOffsetY = cutOffsetY + 1;

            List<TreePartPlacement> remainingParts = new();
            for (int i = 0; i < tree.Parts.Count; i++)
            {
                TreePartPlacement placement = tree.Parts[i];
                if (placement.OffsetTiles.Y > cutOffsetY)
                    remainingParts.Add(placement);
            }

            remainingParts.RemoveAll(part => part.OffsetTiles.Y == stumpOffsetY);
            remainingParts.Add(new TreePartPlacement(TreePartType.TrunkUpperCut, new Point(0, stumpOffsetY)));
            remainingParts.Sort(CompareTreePartPlacementForRendering);

            woodQuantity = CountChoppedWood(tree, cutOffsetY);
            dropPosition = GetTileCenter(tile.X, tile.Y);

            tree.Parts.Clear();
            tree.Parts.AddRange(remainingParts);
            tree.HasCanopy = false;
            TileRevision++;
            return true;
        }

        private static void ChopTreeRoot(TreeInstance tree, TreePartType rootPartType)
        {
            bool cuttingLeftRoot = rootPartType == TreePartType.RootLeft;
            bool hasOtherRoot = HasTreePart(
                tree,
                cuttingLeftRoot ? TreePartType.RootRight : TreePartType.RootLeft);

            tree.Parts.RemoveAll(part =>
                part.PartType == rootPartType ||
                part.OffsetTiles == Point.Zero);

            TreePartType basePartType = TreePartType.TrunkBareBase;
            if (hasOtherRoot)
            {
                basePartType = cuttingLeftRoot
                    ? TreePartType.TrunkBaseRightRootCutSocket
                    : TreePartType.TrunkBaseLeftRootSocket;
            }

            tree.Parts.Add(new TreePartPlacement(basePartType, Point.Zero));
            tree.Parts.Sort(CompareTreePartPlacementForRendering);
        }

        public bool TryRemoveTree(TreeInstance tree)
        {
            if (tree == null)
                return false;

            if (!_trees.Remove(tree))
                return false;

            TileRevision++;
            return true;
        }

        private static bool HasTreePart(TreeInstance tree, TreePartType partType)
        {
            for (int i = 0; i < tree.Parts.Count; i++)
            {
                if (tree.Parts[i].PartType == partType)
                    return true;
            }

            return false;
        }

        private bool TryGetTreePartAtTile(Point tile, out TreeInstance tree, out TreePartPlacement part)
        {
            tree = null;
            part = default;

            if (!InBounds(tile.X, tile.Y))
                return false;

            int wrappedTileX = WrapTileX(tile.X);
            for (int treeIndex = 0; treeIndex < _trees.Count; treeIndex++)
            {
                TreeInstance candidate = _trees[treeIndex];
                for (int partIndex = 0; partIndex < candidate.Parts.Count; partIndex++)
                {
                    TreePartPlacement placement = candidate.Parts[partIndex];
                    int partX = WrapTileX(candidate.BaseTile.X + placement.OffsetTiles.X);
                    int partY = candidate.BaseTile.Y + placement.OffsetTiles.Y;
                    if (partX != wrappedTileX || partY != tile.Y)
                        continue;

                    tree = candidate;
                    part = placement;
                    return true;
                }
            }

            return false;
        }

        private static int CountChoppedWood(TreeInstance tree, int cutOffsetY)
        {
            int woodQuantity = 0;
            for (int i = 0; i < tree.Parts.Count; i++)
            {
                TreePartPlacement placement = tree.Parts[i];
                if (placement.OffsetTiles.X == 0 && placement.OffsetTiles.Y <= cutOffsetY)
                    woodQuantity++;
            }

            return System.Math.Max(1, woodQuantity);
        }

        private static int CompareTreePartPlacementForRendering(TreePartPlacement left, TreePartPlacement right)
        {
            int yComparison = right.OffsetTiles.Y.CompareTo(left.OffsetTiles.Y);
            if (yComparison != 0)
                return yComparison;

            return left.OffsetTiles.X.CompareTo(right.OffsetTiles.X);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Draw(spriteBatch, 0, Width - 1, 0, Height - 1);
        }

        public void Draw(SpriteBatch spriteBatch, int startTileX, int endTileX, int startTileY, int endTileY)
        {
            int minTileX = System.Math.Clamp(startTileX, 0, Width - 1);
            int maxTileX = System.Math.Clamp(endTileX, 0, Width - 1);
            int minTileY = System.Math.Clamp(startTileY, 0, Height - 1);
            int maxTileY = System.Math.Clamp(endTileY, 0, Height - 1);

            if (minTileX > maxTileX || minTileY > maxTileY)
                return;

            if (_chunkCaches.Count > 0)
            {
                DrawCachedChunks(spriteBatch, minTileX, maxTileX, minTileY, maxTileY);
                return;
            }

            DrawTiles(spriteBatch, minTileX, maxTileX, minTileY, maxTileY, 0, 0);
        }

        public void DrawBackground(SpriteBatch spriteBatch, int startTileX, int endTileX, int startTileY, int endTileY)
        {
            int minTileX = System.Math.Clamp(startTileX, 0, Width - 1);
            int maxTileX = System.Math.Clamp(endTileX, 0, Width - 1);
            int minTileY = System.Math.Clamp(startTileY, 0, Height - 1);
            int maxTileY = System.Math.Clamp(endTileY, 0, Height - 1);

            if (minTileX > maxTileX || minTileY > maxTileY)
                return;

            DrawBackgroundTiles(spriteBatch, minTileX, maxTileX, minTileY, maxTileY);
        }

        public void DrawDecorations(SpriteBatch spriteBatch, int startTileX, int endTileX, int startTileY, int endTileY, TreeRenderLayer layer)
        {
            // Trees only ever grow at the surface in this game, so they always take the ambient
            // tint unconditionally - no HasOpenSkyAbove gate needed like tiles/entities.
            _treeRenderer.Draw(spriteBatch, _treeTexture, this, startTileX, endTileX, startTileY, endTileY, layer, _ambientLight);
        }

        public void PrepareVisibleChunkCache(GraphicsDevice graphicsDevice, int startTileX, int endTileX, int startTileY, int endTileY)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));

            int minTileX = System.Math.Clamp(startTileX, 0, Width - 1);
            int maxTileX = System.Math.Clamp(endTileX, 0, Width - 1);
            int minTileY = System.Math.Clamp(startTileY, 0, Height - 1);
            int maxTileY = System.Math.Clamp(endTileY, 0, Height - 1);

            if (minTileX > maxTileX || minTileY > maxTileY)
                return;

            _chunkRenderSpriteBatch ??= new SpriteBatch(graphicsDevice);
            _chunkRenderTick++;

            int startChunkX = minTileX / ChunkTileSize;
            int endChunkX = maxTileX / ChunkTileSize;
            int startChunkY = minTileY / ChunkTileSize;
            int endChunkY = maxTileY / ChunkTileSize;

            for (int chunkY = startChunkY; chunkY <= endChunkY; chunkY++)
            {
                for (int chunkX = startChunkX; chunkX <= endChunkX; chunkX++)
                {
                    WorldChunkCoord chunkCoord = new(chunkX, chunkY);
                    ChunkRenderCache cache = GetOrCreateChunkCache(graphicsDevice, chunkCoord);
                    cache.LastUsedTick = _chunkRenderTick;

                    if (cache.IsDirty)
                        RenderChunkCache(graphicsDevice, chunkCoord, cache);
                }
            }

            PruneChunkCache();
        }

        private void DrawCachedChunks(SpriteBatch spriteBatch, int minTileX, int maxTileX, int minTileY, int maxTileY)
        {
            int startChunkX = minTileX / ChunkTileSize;
            int endChunkX = maxTileX / ChunkTileSize;
            int startChunkY = minTileY / ChunkTileSize;
            int endChunkY = maxTileY / ChunkTileSize;

            for (int chunkY = startChunkY; chunkY <= endChunkY; chunkY++)
            {
                for (int chunkX = startChunkX; chunkX <= endChunkX; chunkX++)
                {
                    WorldChunkCoord chunkCoord = new(chunkX, chunkY);
                    if (!_chunkCaches.TryGetValue(chunkCoord, out ChunkRenderCache cache) || cache.RenderTarget == null)
                    {
                        Rectangle chunkTiles = GetChunkTileBounds(chunkCoord);
                        DrawTiles(spriteBatch, chunkTiles.X, chunkTiles.Right - 1, chunkTiles.Y, chunkTiles.Bottom - 1, 0, 0);
                        continue;
                    }

                    Rectangle worldBounds = GetChunkWorldBounds(chunkCoord);
                    spriteBatch.Draw(cache.RenderTarget, worldBounds, GetChunkAmbientTint(chunkCoord));
                }
            }
        }

        // A cached chunk's RenderTarget is one baked texture for its whole (32-tile-tall) column,
        // so ambient light can only be gated per-chunk here, not per-tile, without re-baking the
        // cache every frame as the sun moves. A chunk counts as "surface" if any tile in its top
        // row has open sky above it; chunks that are entirely buried stay unlit so sunset/sunrise
        // colors don't bleed underground. DrawTiles (below) does the precise per-tile version for
        // the uncached fallback path - a future per-tile skylight system would replace this
        // per-chunk approximation with a real light value baked per pixel instead.
        private Color GetChunkAmbientTint(WorldChunkCoord chunkCoord)
        {
            if (_ambientLight == Color.White)
                return Color.White;

            int topRowY = chunkCoord.Y * ChunkTileSize;
            int startX = chunkCoord.X * ChunkTileSize;
            int endX = System.Math.Min(startX + ChunkTileSize, Width) - 1;

            for (int x = startX; x <= endX; x++)
            {
                if (HasOpenSkyAbove(x, topRowY))
                    return _ambientLight;
            }

            return Color.White;
        }

        private void DrawTiles(SpriteBatch spriteBatch, int minTileX, int maxTileX, int minTileY, int maxTileY, int pixelOffsetX, int pixelOffsetY)
        {
            for (int y = minTileY; y <= maxTileY; y++)
            {
                for (int x = minTileX; x <= maxTileX; x++)
                {
                    if (!TryGetTileSprite(x, y, out Texture2D texture, out Rectangle? sourceRectangle))
                        continue;

                    Rectangle destination = new Rectangle(
                        (x * TileSize) - pixelOffsetX,
                        (y * TileSize) - pixelOffsetY,
                        TileSize,
                        TileSize);
                    // Wetness is NOT applied here - see DrawWetnessOverlay. This draw gets baked into
                    // a chunk's cached RenderTarget and only re-runs when the chunk is marked dirty
                    // (a tile edit), but wetness dries out continuously on its own; baking it here
                    // made tiles show a stale wetness tint until some unrelated edit forced a re-bake.
                    Color tint = Color.White;
                    if (_ambientLight != Color.White && HasOpenSkyAbove(x, y))
                        tint = _ambientLight;

                    spriteBatch.Draw(texture, destination, sourceRectangle, tint);
                }
            }
        }

        // Drawn fresh every frame (never baked into the chunk cache) so it always reflects current
        // wetness instead of going stale - see the comment in DrawTiles. Uses a multiply blend so it
        // darkens whatever's already on screen rather than needing to duplicate the base tile draw.
        public void DrawWetnessOverlay(SpriteBatch spriteBatch, int minTileX, int maxTileX, int minTileY, int maxTileY)
        {
            if (_wetnessField == null)
                return;

            for (int y = minTileY; y <= maxTileY; y++)
            {
                for (int x = minTileX; x <= maxTileX; x++)
                {
                    Color tint = GetWetnessTint(GetTile(x, y), x, y);
                    if (tint == Color.White)
                        continue;

                    if (!TryGetTileSprite(x, y, out Texture2D texture, out Rectangle? sourceRectangle))
                        continue;

                    Rectangle destination = new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);
                    spriteBatch.Draw(texture, destination, sourceRectangle, tint);
                }
            }
        }

        // Exposed so overlays (e.g. the skylight shadow pass in PlayingSessionViewCoordinator) can
        // tint a tile using its own sprite shape - autotile edges have transparent corners/notches,
        // so a flat tinted square over the tile's bounds would paint over those gaps instead of
        // conforming to the tile's actual silhouette.
        public bool TryGetTileSprite(int x, int y, out Texture2D texture, out Rectangle? sourceRectangle)
        {
            TileType tile = GetTile(x, y);
            if (tile == TileType.Empty)
            {
                texture = null;
                sourceRectangle = null;
                return false;
            }

            texture = GetTextureForTile(tile);
            if (texture == null)
            {
                sourceRectangle = null;
                return false;
            }

            sourceRectangle = tile switch
            {
                TileType.Dirt => GetDirtMixAutoTileSourceRectangle(x, y),
                TileType.Grass => GetGrassAutoTileSourceRectangle(x, y),
                TileType.Stone => GetStoneAutoTileSourceRectangle(x, y),
                TileType.Sand => GetAutoTileSourceRectangle(x, y),
                TileType.Wood => GetDirtAutoTileSourceRectangle(x, y),
                TileType.IronOre => GetIronOreAutoTileSourceRectangle(x, y),
                _ => null
            };
            return true;
        }

        private static Color MultiplyColors(Color a, Color b)
        {
            return new Color(
                (a.R * b.R) / 255,
                (a.G * b.G) / 255,
                (a.B * b.B) / 255,
                (a.A * b.A) / 255);
        }

        private Color GetWetnessTint(TileType tile, int x, int y)
        {
            if (_wetnessField == null || (tile != TileType.Dirt && tile != TileType.Grass))
                return Color.White;

            float wetness01 = _wetnessField.GetWetness01(x, y);
            if (wetness01 <= 0f)
                return Color.White;

            return Color.Lerp(Color.White, new Color(120, 112, 96), wetness01 * 0.35f);
        }

        private void DrawBackgroundTiles(SpriteBatch spriteBatch, int minTileX, int maxTileX, int minTileY, int maxTileY)
        {
            for (int y = minTileY; y <= maxTileY; y++)
            {
                for (int x = minTileX; x <= maxTileX; x++)
                {
                    TileType tile = GetBackgroundTile(x, y);
                    if (tile == TileType.Empty)
                        continue;

                    Texture2D texture = GetTextureForTile(tile);
                    if (texture == null)
                        continue;

                    Rectangle? sourceRectangle = tile switch
                    {
                        TileType.Dirt => GetDirtAutoTileSourceRectangle(x, y, background: true),
                        TileType.Grass => GetDirtAutoTileSourceRectangle(x, y, background: true),
                        TileType.Stone => GetDirtAutoTileSourceRectangle(x, y, background: true),
                        TileType.Sand => GetBackgroundAutoTileSourceRectangle(x, y),
                        TileType.Wood => GetDirtAutoTileSourceRectangle(x, y, background: true),
                        TileType.IronOre => GetDirtAutoTileSourceRectangle(x, y, background: true),
                        _ => null
                    };

                    Rectangle destination = new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);
                    spriteBatch.Draw(texture, destination, sourceRectangle, BackgroundTileTint);
                }
            }
        }

        private Texture2D GetTextureForTile(TileType tile)
        {
            return tile switch
            {
                TileType.Dirt => _dirt,
                TileType.Grass => _grass,
                TileType.Sand => _sand,
                TileType.Stone => _stone,
                TileType.Wood => _wood,
                TileType.IronOre => _ironOre,
                _ => null
            };
        }

        private ChunkRenderCache GetOrCreateChunkCache(GraphicsDevice graphicsDevice, WorldChunkCoord chunkCoord)
        {
            if (_chunkCaches.TryGetValue(chunkCoord, out ChunkRenderCache existing))
                return existing;

            Rectangle worldBounds = GetChunkWorldBounds(chunkCoord);
            ChunkRenderCache created = new(new RenderTarget2D(
                graphicsDevice,
                System.Math.Max(1, worldBounds.Width),
                System.Math.Max(1, worldBounds.Height),
                false,
                SurfaceFormat.Color,
                DepthFormat.None));
            _chunkCaches[chunkCoord] = created;
            return created;
        }

        private void RenderChunkCache(GraphicsDevice graphicsDevice, WorldChunkCoord chunkCoord, ChunkRenderCache cache)
        {
            Rectangle chunkTileBounds = GetChunkTileBounds(chunkCoord);
            Rectangle chunkWorldBounds = GetChunkWorldBounds(chunkCoord);

            if (cache.RenderTarget.Width != chunkWorldBounds.Width || cache.RenderTarget.Height != chunkWorldBounds.Height)
            {
                cache.RenderTarget.Dispose();
                cache.RenderTarget = new RenderTarget2D(
                    graphicsDevice,
                    System.Math.Max(1, chunkWorldBounds.Width),
                    System.Math.Max(1, chunkWorldBounds.Height),
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None);
            }

            graphicsDevice.SetRenderTarget(cache.RenderTarget);
            graphicsDevice.Clear(Color.Transparent);

            _chunkRenderSpriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            DrawTiles(
                _chunkRenderSpriteBatch,
                chunkTileBounds.X,
                chunkTileBounds.Right - 1,
                chunkTileBounds.Y,
                chunkTileBounds.Bottom - 1,
                chunkWorldBounds.X,
                chunkWorldBounds.Y);
            _chunkRenderSpriteBatch.End();

            graphicsDevice.SetRenderTarget(null);
            cache.IsDirty = false;
        }

        private void MarkChunkNeighborhoodDirty(int centerX, int centerY)
        {
            WorldChunkCoord centerChunk = GetChunkCoordForTile(centerX, centerY);
            for (int chunkY = centerChunk.Y - 1; chunkY <= centerChunk.Y + 1; chunkY++)
            {
                if (chunkY < 0 || chunkY >= ChunkCountY)
                    continue;

                for (int chunkX = centerChunk.X - 1; chunkX <= centerChunk.X + 1; chunkX++)
                {
                    WorldChunkCoord wrappedChunk = new(WrapChunkX(chunkX), chunkY);
                    if (_chunkCaches.TryGetValue(wrappedChunk, out ChunkRenderCache cache))
                        cache.IsDirty = true;
                }
            }
        }

        private void MarkAllChunkCachesDirty()
        {
            foreach (ChunkRenderCache cache in _chunkCaches.Values)
                cache.IsDirty = true;
        }

        private void PruneChunkCache()
        {
            if (_chunkCaches.Count <= MaxCachedChunks)
                return;

            List<KeyValuePair<WorldChunkCoord, ChunkRenderCache>> entries = new(_chunkCaches);
            entries.Sort((a, b) => a.Value.LastUsedTick.CompareTo(b.Value.LastUsedTick));

            int removeCount = _chunkCaches.Count - MaxCachedChunks;
            for (int i = 0; i < removeCount; i++)
            {
                KeyValuePair<WorldChunkCoord, ChunkRenderCache> entry = entries[i];
                entry.Value.RenderTarget.Dispose();
                _chunkCaches.Remove(entry.Key);
            }
        }

        private Rectangle GetAutoTileSourceRectangle(int x, int y)
        {
            return GetAutoTileSheetCellFromVariant(_autoTileVariants[WrapTileX(x), y], x, y);
        }

        private Rectangle GetBackgroundAutoTileSourceRectangle(int x, int y)
        {
            return GetAutoTileSheetCellFromVariant(_backgroundAutoTileVariants[WrapTileX(x), y], x, y);
        }

        private Rectangle GetDirtAutoTileSourceRectangle(int x, int y)
        {
            return GetDirtAutoTileSourceRectangle(x, y, background: false);
        }

        private Rectangle GetDirtAutoTileSourceRectangle(int x, int y, bool background)
        {
            bool up = IsAutoTileConnectedAt(x, y - 1, background);
            bool right = IsAutoTileConnectedAt(x + 1, y, background);
            bool down = IsAutoTileConnectedAt(x, y + 1, background);
            bool left = IsAutoTileConnectedAt(x - 1, y, background);

            int connectedCount = 0;
            if (up) connectedCount++;
            if (right) connectedCount++;
            if (down) connectedCount++;
            if (left) connectedCount++;

            switch (connectedCount)
            {
                case 0:
                    return GetAutoTileSheetCell(0, 1 + PickTileVariation(x, y, 2));

                case 1:
                    return GetDirtEndSourceRectangle(x, y, up, right, down, left);

                case 2:
                    return GetDirtTwoConnectionSourceRectangle(x, y, up, right, down, left);

                case 3:
                    return GetDirtThreeConnectionSourceRectangle(x, y, up, right, down, left);

                default:
                    if (TryGetDirtInnerCornerSourceRectangle(x, y, background, out Rectangle innerCorner))
                        return innerCorner;

                    return GetAutoTileSheetCell(2 + PickTileVariation(x, y, 3), 1);
            }
        }

        private bool TryGetDirtInnerCornerSourceRectangle(int x, int y, bool background, out Rectangle sourceRectangle)
        {
            bool up = IsAutoTileConnectedAt(x, y - 1, background);
            bool right = IsAutoTileConnectedAt(x + 1, y, background);
            bool down = IsAutoTileConnectedAt(x, y + 1, background);
            bool left = IsAutoTileConnectedAt(x - 1, y, background);

            bool downRightAir = right && down && !IsAutoTileConnectedAt(x + 1, y + 1, background);
            bool downLeftAir = left && down && !IsAutoTileConnectedAt(x - 1, y + 1, background);
            bool upRightAir = up && right && !IsAutoTileConnectedAt(x + 1, y - 1, background);
            bool upLeftAir = up && left && !IsAutoTileConnectedAt(x - 1, y - 1, background);

            if (downRightAir)
            {
                sourceRectangle = GetAutoTileSheetCell(4, 3);
                return true;
            }

            if (downLeftAir)
            {
                sourceRectangle = GetAutoTileSheetCell(5, 3);
                return true;
            }

            if (upRightAir)
            {
                sourceRectangle = GetAutoTileSheetCell(4, 4);
                return true;
            }

            if (upLeftAir)
            {
                sourceRectangle = GetAutoTileSheetCell(5, 4);
                return true;
            }

            sourceRectangle = Rectangle.Empty;
            return false;
        }

        private Rectangle GetDirtEndSourceRectangle(int x, int y, bool up, bool right, bool down, bool left)
        {
            int variationOffset = PickTileVariation(x, y, 2) * 2;

            if (down)
                return GetAutoTileSheetCell(0 + variationOffset, 5);
            if (right)
                return GetAutoTileSheetCell(1 + variationOffset, 5);
            if (up)
                return GetAutoTileSheetCell(0 + variationOffset, 6);

            return GetAutoTileSheetCell(1 + variationOffset, 6);
        }

        private Rectangle GetDirtTwoConnectionSourceRectangle(int x, int y, bool up, bool right, bool down, bool left)
        {
            if (up && down)
                return GetAutoTileSheetCell(6, PickTileVariation(x, y, 3));
            if (left && right)
                return GetAutoTileSheetCell(6, 3 + PickTileVariation(x, y, 3));

            int variationOffset = PickTileVariation(x, y, 2) * 2;

            if (right && down)
                return GetAutoTileSheetCell(0 + variationOffset, 3);
            if (left && down)
                return GetAutoTileSheetCell(1 + variationOffset, 3);
            if (right && up)
                return GetAutoTileSheetCell(0 + variationOffset, 4);

            return GetAutoTileSheetCell(1 + variationOffset, 4);
        }

        private Rectangle GetDirtThreeConnectionSourceRectangle(int x, int y, bool up, bool right, bool down, bool left)
        {
            int variation = PickTileVariation(x, y, 3);

            if (!left)
                return GetAutoTileSheetCell(1, variation);
            if (!up)
                return GetAutoTileSheetCell(2 + variation, 0);
            if (!down)
                return GetAutoTileSheetCell(2 + variation, 2);

            return GetAutoTileSheetCell(5, variation);
        }

        private Rectangle GetGrassAutoTileSourceRectangle(int x, int y)
        {
            return GetDirtAutoTileSourceRectangle(x, y);
        }

        private Rectangle GetStoneAutoTileSourceRectangle(int x, int y)
        {
            return EvaluateAutoTileMixRules(TileType.Stone, TileType.Dirt, x, y, BaseAutoTileMixRules);
        }

        private Rectangle GetIronOreAutoTileSourceRectangle(int x, int y)
        {
            return EvaluateAutoTileMixRules(TileType.IronOre, TileType.Stone, x, y, BaseAutoTileMixRules);
        }

        private Rectangle GetDirtMixAutoTileSourceRectangle(int x, int y)
        {
            // Dirt's mix partner will be a dirt variant tile added later; until that tile exists,
            // every solid neighbor renders as a normal connected edge (no mix cells triggered).
            return EvaluateAutoTileMixRules(TileType.Dirt, null, x, y, BaseAutoTileMixRules);
        }

        private enum NeighborState
        {
            Any,
            Self,
            Other,
            Empty,
            Solid // Self or Other, but not Empty - used when a shape doesn't care which material the neighbor is, only that it's there.
        }

        private static class N
        {
            public const NeighborState A = NeighborState.Any;
            public const NeighborState S = NeighborState.Self;
            public const NeighborState O = NeighborState.Other;
            public const NeighborState E = NeighborState.Empty;
            public const NeighborState X = NeighborState.Solid;
        }

        private readonly struct AutoTileMixRule
        {
            public readonly NeighborState Up;
            public readonly NeighborState Right;
            public readonly NeighborState Down;
            public readonly NeighborState Left;
            public readonly NeighborState UpLeft;
            public readonly NeighborState UpRight;
            public readonly NeighborState DownLeft;
            public readonly NeighborState DownRight;
            public readonly int Column;
            public readonly int Row;
            public readonly int VariantCount;
            public readonly bool VariantsAcrossColumns;
            public readonly int VariantStep;

            public AutoTileMixRule(
                NeighborState up, NeighborState right, NeighborState down, NeighborState left,
                int column, int row, int variantCount, bool variantsAcrossColumns,
                int variantStep = 1,
                NeighborState upLeft = NeighborState.Any, NeighborState upRight = NeighborState.Any,
                NeighborState downLeft = NeighborState.Any, NeighborState downRight = NeighborState.Any)
            {
                Up = up;
                Right = right;
                Down = down;
                Left = left;
                UpLeft = upLeft;
                UpRight = upRight;
                DownLeft = downLeft;
                DownRight = downRight;
                Column = column;
                Row = row;
                VariantCount = variantCount;
                VariantsAcrossColumns = variantsAcrossColumns;
                VariantStep = variantStep;
            }
        }

        // Shared "hard material" autotile rule set (matches the layout used by the stone spritesheet today).
        // Evaluated top-to-bottom, first match wins. Any future material that follows the same sheet layout
        // (ore blocks, etc.) can reuse this array as-is; families with extra rows (e.g. grass variants) can
        // concatenate their own rules after this one.
        private static readonly AutoTileMixRule[] BaseAutoTileMixRules =
        {
            // 0 connections - isolated
            new AutoTileMixRule(N.E, N.E, N.E, N.E, 9, 3, 3, true), // isolado

            // 1 connection - ponta
            new AutoTileMixRule(N.E, N.E, N.O, N.E, 6, 5, 3, false), // ponta-baixo, vizinho=Terra
            new AutoTileMixRule(N.E, N.E, N.S, N.E, 6, 0, 3, true),  // ponta-baixo
            new AutoTileMixRule(N.E, N.O, N.E, N.E, 3, 13, 3, true), // ponta-dir, vizinho=Terra
            new AutoTileMixRule(N.E, N.X, N.E, N.E, 9, 0, 3, false), // ponta-dir
            new AutoTileMixRule(N.O, N.E, N.E, N.E, 6, 8, 3, false), // ponta-cima, vizinho=Terra
            new AutoTileMixRule(N.S, N.E, N.E, N.E, 6, 3, 3, true),  // ponta-cima
            new AutoTileMixRule(N.E, N.E, N.E, N.O, 0, 13, 3, true), // ponta-esq, vizinho=Terra
            new AutoTileMixRule(N.E, N.E, N.E, N.X, 12, 0, 3, false), // ponta-esq

            // 2 connections
            new AutoTileMixRule(N.S, N.E, N.O, N.E, 7, 5, 3, false), // cima=pedra, baixo=terra
            new AutoTileMixRule(N.O, N.E, N.S, N.E, 7, 8, 3, false), // baixo=pedra, cima=terra
            new AutoTileMixRule(N.O, N.E, N.O, N.E, 6, 12, 3, false), // 2conn cima+baixo, ambos terra
            new AutoTileMixRule(N.X, N.E, N.X, N.E, 5, 0, 3, false), // 2conn cima+baixo
            new AutoTileMixRule(N.E, N.O, N.E, N.O, 9, 11, 3, true), // 2conn esq+dir, ambos terra
            new AutoTileMixRule(N.E, N.S, N.E, N.O, 0, 14, 3, true), // dir=pedra, esq=terra
            new AutoTileMixRule(N.E, N.O, N.E, N.S, 3, 14, 3, true), // esq=pedra, dir=terra
            new AutoTileMixRule(N.E, N.X, N.E, N.X, 6, 4, 3, true),  // 2conn esq+dir
            new AutoTileMixRule(N.E, N.X, N.X, N.E, 0, 3, 3, true, 2), // 2conn dir+baixo
            new AutoTileMixRule(N.E, N.E, N.X, N.X, 1, 3, 3, true, 2), // 2conn esq+baixo
            new AutoTileMixRule(N.X, N.X, N.E, N.E, 0, 4, 3, true, 2), // 2conn dir+cima
            new AutoTileMixRule(N.X, N.E, N.E, N.X, 1, 4, 3, true, 2), // 2conn esq+cima

            // 3 connections - falta um lado
            new AutoTileMixRule(N.S, N.O, N.S, N.E, 13, 2, 3, true), // falta-esq, dir=terra (ter-dir)
            new AutoTileMixRule(N.S, N.S, N.O, N.E, 4, 5, 3, false), // falta-esq, baixo=terra
            new AutoTileMixRule(N.O, N.S, N.S, N.E, 4, 8, 3, false), // falta-esq, cima=terra
            new AutoTileMixRule(N.X, N.X, N.X, N.E, 0, 0, 3, false), // falta-esq

            new AutoTileMixRule(N.E, N.S, N.O, N.S, 13, 0, 3, true), // falta-cima, baixo=terra (ter-baixo)
            new AutoTileMixRule(N.E, N.S, N.S, N.O, 0, 11, 3, true), // falta-cima, esq=terra
            new AutoTileMixRule(N.E, N.O, N.S, N.S, 3, 11, 3, true), // falta-cima, dir=terra
            new AutoTileMixRule(N.E, N.X, N.X, N.X, 1, 0, 3, true),  // falta-cima

            new AutoTileMixRule(N.O, N.S, N.E, N.S, 13, 1, 3, true), // falta-baixo, cima=terra (ter-cima)
            new AutoTileMixRule(N.S, N.S, N.E, N.O, 0, 12, 3, true), // falta-baixo, esq=terra
            new AutoTileMixRule(N.S, N.O, N.E, N.S, 3, 12, 3, true), // falta-baixo, dir=terra
            new AutoTileMixRule(N.X, N.X, N.E, N.X, 1, 2, 3, true),  // falta-baixo

            new AutoTileMixRule(N.S, N.E, N.S, N.O, 13, 3, 3, true), // falta-dir, esq=terra (ter-esq)
            new AutoTileMixRule(N.S, N.E, N.O, N.S, 5, 5, 3, false), // falta-dir, baixo=terra
            new AutoTileMixRule(N.O, N.E, N.S, N.S, 5, 8, 3, false), // falta-dir, cima=terra
            new AutoTileMixRule(N.X, N.E, N.X, N.X, 4, 0, 3, false), // falta-dir

            // 4 connections - todos os lados cobertos
            new AutoTileMixRule(N.S, N.S, N.O, N.S, 8, 5, 3, true),  // 1 lado = terra: baixo
            new AutoTileMixRule(N.O, N.S, N.S, N.S, 8, 6, 3, true),  // 1 lado = terra: cima
            new AutoTileMixRule(N.S, N.O, N.S, N.S, 8, 7, 3, false), // 1 lado = terra: direita
            new AutoTileMixRule(N.S, N.S, N.S, N.O, 9, 7, 3, false), // 1 lado = terra: esquerda

            new AutoTileMixRule(N.O, N.O, N.S, N.O, 11, 5, 3, false), // 1 lado = pedra: baixo
            new AutoTileMixRule(N.S, N.O, N.O, N.O, 11, 8, 3, false), // 1 lado = pedra: cima
            new AutoTileMixRule(N.O, N.S, N.O, N.O, 12, 5, 3, false), // 1 lado = pedra: direita
            new AutoTileMixRule(N.O, N.O, N.O, N.S, 12, 8, 3, false), // 1 lado = pedra: esquerda

            new AutoTileMixRule(N.S, N.O, N.S, N.O, 10, 7, 3, false), // 2 lados opostos = terra: esq+dir
            new AutoTileMixRule(N.O, N.S, N.O, N.S, 8, 10, 3, true),  // 2 lados opostos = terra: cima+baixo

            new AutoTileMixRule(N.O, N.S, N.S, N.O, 2, 5, 3, false, 2), // divisao: esq+cima=terra
            new AutoTileMixRule(N.S, N.S, N.O, N.O, 2, 6, 3, false, 2), // divisao: esq+baixo=terra
            new AutoTileMixRule(N.O, N.O, N.S, N.S, 3, 5, 3, false, 2), // divisao: dir+cima=terra
            new AutoTileMixRule(N.S, N.O, N.O, N.S, 3, 6, 3, false, 2), // divisao: dir+baixo=terra

            new AutoTileMixRule(N.S, N.S, N.S, N.S, 0, 5, 3, false, 2, downRight: N.O), // diagonal baixo-dir=terra
            new AutoTileMixRule(N.S, N.S, N.S, N.S, 1, 5, 3, false, 2, downLeft: N.O),  // diagonal baixo-esq=terra
            new AutoTileMixRule(N.S, N.S, N.S, N.S, 0, 6, 3, false, 2, upRight: N.O),   // diagonal cima-dir=terra
            new AutoTileMixRule(N.S, N.S, N.S, N.S, 1, 6, 3, false, 2, upLeft: N.O),    // diagonal cima-esq=terra

            new AutoTileMixRule(N.O, N.O, N.O, N.O, 6, 11, 3, true), // 4 lados = terra

            new AutoTileMixRule(N.A, N.A, N.A, N.A, 10, 0, 3, false, upLeft: N.E, downLeft: N.E),  // canto interno esq
            new AutoTileMixRule(N.A, N.A, N.A, N.A, 11, 0, 3, false, upRight: N.E, downRight: N.E), // canto interno dir
            new AutoTileMixRule(N.A, N.A, N.A, N.A, 6, 1, 3, true, upLeft: N.E, upRight: N.E),      // canto interno cima
            new AutoTileMixRule(N.A, N.A, N.A, N.A, 6, 2, 3, true, downLeft: N.E, downRight: N.E),  // canto interno baixo

            new AutoTileMixRule(N.A, N.A, N.A, N.A, 1, 1, 3, true), // bloco cheio (fallback)
        };

        private Rectangle EvaluateAutoTileMixRules(TileType self, TileType? mixPartner, int x, int y, AutoTileMixRule[] rules)
        {
            NeighborState up = ClassifyAutoTileNeighbor(self, mixPartner, x, y - 1);
            NeighborState right = ClassifyAutoTileNeighbor(self, mixPartner, x + 1, y);
            NeighborState down = ClassifyAutoTileNeighbor(self, mixPartner, x, y + 1);
            NeighborState left = ClassifyAutoTileNeighbor(self, mixPartner, x - 1, y);
            NeighborState upLeft = ClassifyAutoTileNeighbor(self, mixPartner, x - 1, y - 1);
            NeighborState upRight = ClassifyAutoTileNeighbor(self, mixPartner, x + 1, y - 1);
            NeighborState downLeft = ClassifyAutoTileNeighbor(self, mixPartner, x - 1, y + 1);
            NeighborState downRight = ClassifyAutoTileNeighbor(self, mixPartner, x + 1, y + 1);

            for (int i = 0; i < rules.Length; i++)
            {
                AutoTileMixRule rule = rules[i];

                if (!MatchesNeighborState(rule.Up, up)) continue;
                if (!MatchesNeighborState(rule.Right, right)) continue;
                if (!MatchesNeighborState(rule.Down, down)) continue;
                if (!MatchesNeighborState(rule.Left, left)) continue;
                if (!MatchesNeighborState(rule.UpLeft, upLeft)) continue;
                if (!MatchesNeighborState(rule.UpRight, upRight)) continue;
                if (!MatchesNeighborState(rule.DownLeft, downLeft)) continue;
                if (!MatchesNeighborState(rule.DownRight, downRight)) continue;

                int variation = PickTileVariation(x, y, rule.VariantCount) * rule.VariantStep;

                return rule.VariantsAcrossColumns
                    ? GetAutoTileSheetCell(rule.Column + variation, rule.Row)
                    : GetAutoTileSheetCell(rule.Column, rule.Row + variation);
            }

            return GetAutoTileSheetCell(1, 1);
        }

        private NeighborState ClassifyAutoTileNeighbor(TileType self, TileType? mixPartner, int x, int y)
        {
            TileType tile = GetTile(x, y);
            if (!IsSolid(tile))
                return NeighborState.Empty;

            if (tile == self)
                return NeighborState.Self;

            if (mixPartner.HasValue && tile == mixPartner.Value)
                return NeighborState.Other;

            // Solid neighbor we don't have mix art for (no declared mix partner, or a third
            // material entirely): render as a normal connected edge instead of guessing.
            return NeighborState.Self;
        }

        private static bool MatchesNeighborState(NeighborState expected, NeighborState actual)
        {
            if (expected == NeighborState.Any)
                return true;
            if (expected == NeighborState.Solid)
                return actual != NeighborState.Empty;

            return expected == actual;
        }

        private bool IsAutoTileConnectedAt(int x, int y, bool background)
        {
            return background
                ? IsBackgroundSolidAt(x, y)
                : IsSolidAt(x, y);
        }

        private void RebuildAutoTileVariants()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    _autoTileVariants[x, y] = ComputeAutoTileVariant(x, y);
            }
        }

        private void RebuildBackgroundAutoTileVariants()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    _backgroundAutoTileVariants[x, y] = ComputeBackgroundAutoTileVariant(x, y);
            }
        }

        private void RefreshAutoTileNeighborhood(int centerX, int centerY)
        {
            for (int y = centerY - 1; y <= centerY + 1; y++)
            {
                if (y < 0 || y >= Height)
                    continue;

                for (int x = centerX - 1; x <= centerX + 1; x++)
                {
                    int wrappedX = WrapTileX(x);
                    _autoTileVariants[wrappedX, y] = ComputeAutoTileVariant(wrappedX, y);
                }
            }
        }

        private void RefreshBackgroundAutoTileNeighborhood(int centerX, int centerY)
        {
            for (int y = centerY - 1; y <= centerY + 1; y++)
            {
                if (y < 0 || y >= Height)
                    continue;

                for (int x = centerX - 1; x <= centerX + 1; x++)
                {
                    int wrappedX = WrapTileX(x);
                    _backgroundAutoTileVariants[wrappedX, y] = ComputeBackgroundAutoTileVariant(wrappedX, y);
                }
            }
        }

        private byte ComputeAutoTileVariant(int x, int y)
        {
            TileType tile = _tiles[x, y];
            if (!IsSolid(tile))
                return 255;

            bool up = IsSolidAt(x, y - 1);
            bool right = IsSolidAt(x + 1, y);
            bool down = IsSolidAt(x, y + 1);
            bool left = IsSolidAt(x - 1, y);

            int connectedCount = 0;
            if (up) connectedCount++;
            if (right) connectedCount++;
            if (down) connectedCount++;
            if (left) connectedCount++;

            return connectedCount switch
            {
                0 => 0,
                1 when down => 1,
                1 when left => 2,
                1 when right => 3,
                1 => 4,
                2 when left && right => 5,
                2 when up && down => 6,
                2 when right && down => 7,
                2 when left && down => 8,
                2 when up && right => 9,
                2 => 10,
                3 when !up => 11,
                3 when !right => 12,
                3 when !left => 13,
                3 => 14,
                _ => (byte)(((x + y) & 1) == 0 ? 15 : 16)
            };
        }

        private byte ComputeBackgroundAutoTileVariant(int x, int y)
        {
            TileType tile = _backgroundTiles[x, y];
            if (!IsSolid(tile))
                return 255;

            bool up = IsBackgroundSolidAt(x, y - 1);
            bool right = IsBackgroundSolidAt(x + 1, y);
            bool down = IsBackgroundSolidAt(x, y + 1);
            bool left = IsBackgroundSolidAt(x - 1, y);

            int connectedCount = 0;
            if (up) connectedCount++;
            if (right) connectedCount++;
            if (down) connectedCount++;
            if (left) connectedCount++;

            return connectedCount switch
            {
                0 => 0,
                1 when down => 1,
                1 when left => 2,
                1 when right => 3,
                1 => 4,
                2 when left && right => 5,
                2 when up && down => 6,
                2 when right && down => 7,
                2 when left && down => 8,
                2 when up && right => 9,
                2 => 10,
                3 when !up => 11,
                3 when !right => 12,
                3 when !left => 13,
                3 => 14,
                _ => (byte)(((x + y) & 1) == 0 ? 15 : 16)
            };
        }

        private bool IsBackgroundSolidAt(int x, int y)
        {
            return IsSolid(GetBackgroundTile(x, y));
        }

        private Rectangle GetAutoTileSheetCellFromVariant(byte variant, int x, int y)
        {
            return variant switch
            {
                0 => GetAutoTileSheetCell(0, 0),
                1 => GetAutoTileSheetCell(1, 2),
                2 => GetAutoTileSheetCell(2, 2),
                3 => GetAutoTileSheetCell(2, 3),
                4 => GetAutoTileSheetCell(1, 3),
                5 => GetAutoTileSheetCell(5, 0),
                6 => GetAutoTileSheetCell(6, 0),
                7 => GetAutoTileSheetCell(3, 0),
                8 => GetAutoTileSheetCell(4, 0),
                9 => GetAutoTileSheetCell(3, 1),
                10 => GetAutoTileSheetCell(4, 1),
                11 => GetAutoTileSheetCell(1, 0),
                12 => GetAutoTileSheetCell(2, 0),
                13 => GetAutoTileSheetCell(1, 1),
                14 => GetAutoTileSheetCell(2, 1),
                15 => GetAutoTileSheetCell(5, 1),
                16 => GetAutoTileSheetCell(6, 1),
                _ => ((x + y) & 1) == 0
                    ? GetAutoTileSheetCell(5, 1)
                    : GetAutoTileSheetCell(6, 1)
            };
        }

        private Rectangle GetAutoTileSheetCell(int column, int row)
        {
            int x = column * (AutoTileSheetTileSize + AutoTileSheetSpacing);
            int y = row * (AutoTileSheetTileSize + AutoTileSheetSpacing);
            return new Rectangle(x, y, AutoTileSheetTileSize, AutoTileSheetTileSize);
        }

        private static int PickTileVariation(int x, int y, int variationCount)
        {
            if (variationCount <= 1)
                return 0;

            unchecked
            {
                uint hash = (uint)(x * 73856093 ^ y * 19349663);
                hash ^= hash >> 13;
                return (int)(hash % (uint)variationCount);
            }
        }

        private void TrackTileChange(int wrappedX, int y, TileType previousTile, TileType nextTile)
        {
            if (!IsTrackingTileChanges || previousTile == nextTile)
                return;

            long key = CreateTileKey(wrappedX, y);
            if (!_trackedTileBaselines.TryGetValue(key, out TileType baselineTile))
            {
                baselineTile = previousTile;
                _trackedTileBaselines[key] = baselineTile;
            }

            if (nextTile == baselineTile)
            {
                _trackedTileBaselines.Remove(key);
                _trackedTileChanges.Remove(key);
                return;
            }

            _trackedTileChanges[key] = new WorldTileChange(wrappedX, y, nextTile);
        }

        private static long CreateTileKey(int x, int y)
        {
            return ((long)y << 32) | (uint)x;
        }

        private sealed class ChunkRenderCache
        {
            public ChunkRenderCache(RenderTarget2D renderTarget)
            {
                RenderTarget = renderTarget;
                IsDirty = true;
            }

            public RenderTarget2D RenderTarget { get; set; }
            public bool IsDirty { get; set; }
            public int LastUsedTick { get; set; }
        }
    }
}
