using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World.Tissue;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using System.IO;
using System.Collections.Generic;

namespace Nyvorn.Source.World
{
    public class WorldMap
    {
        private const int AutoTileSheetTileSize = 8;
        private const int AutoTileSheetSpacing = 1;
        private const int DefaultChunkTileSize = 32;

        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }
        public int TileRevision { get; private set; }
        public int PixelWidth => Width * TileSize;
        public TissueField TissueField => _tissueField;
        public TissueAnalysisResult TissueAnalysis => _tissueAnalysis;
        public int TissueRevision { get; private set; }
        public int ChunkTileSize => DefaultChunkTileSize;
        public int ChunkCountX => (Width + ChunkTileSize - 1) / ChunkTileSize;
        public int ChunkCountY => (Height + ChunkTileSize - 1) / ChunkTileSize;

        private Texture2D _dirt;
        private Texture2D _grass;
        private Texture2D _sand;
        private Texture2D _stone;
        private TissueField _tissueField;
        private TissueAnalysisResult _tissueAnalysis;

        private readonly TileType[,] _tiles;
        private readonly Dictionary<long, TileType> _trackedTileBaselines = new();
        private readonly Dictionary<long, WorldTileChange> _trackedTileChanges = new();
        private readonly HashSet<long> _grassCandidateKeys = new();
        private readonly Queue<Point> _grassCandidateQueue = new();
        private const int GrassSpreadBatchSize = 4096;

        public WorldMap(int width, int height, int tileSize)
        {
            Width = width;
            Height = height;
            TileSize = tileSize;

            _tiles = new TileType[Width, Height];
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
            TileRevision++;
            EnqueueGrassCandidateArea(wrappedX, y);
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
            ClearGrassCandidates();
            TileRevision++;
        }

        public byte[] ExportTissueSnapshot()
        {
            if (_tissueField == null)
                return null;

            byte[] snapshot = new byte[Width * Height];
            int index = 0;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    snapshot[index++] = _tissueField.HasTissue(x, y) ? (byte)1 : (byte)0;
            }

            return snapshot;
        }

        public void ImportTissueSnapshot(byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length == 0)
            {
                SetTissueField(null);
                return;
            }

            if (snapshot.Length != Width * Height)
                throw new System.ArgumentException("Tissue snapshot size does not match world dimensions.", nameof(snapshot));

            TissueField field = new TissueField(Width, Height);
            int index = 0;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    field.SetTissue(x, y, snapshot[index++] != 0);
            }

            SetTissueField(field);
        }

        public void SetTissueField(TissueField tissueField)
        {
            _tissueField = tissueField;
            _tissueAnalysis = null;
            TissueRevision++;
        }

        public void SetTissueAnalysis(TissueAnalysisResult analysis)
        {
            _tissueAnalysis = analysis;
        }

        public void MarkTissueDirty()
        {
            _tissueAnalysis = null;
            TissueRevision++;
        }

        public TissueAnalysisResult GetOrCreateTissueAnalysis()
        {
            if (_tissueField == null)
                return null;

            if (_tissueAnalysis == null)
                _tissueAnalysis = new TissueAnalyzer().Analyze(_tissueField, this);

            return _tissueAnalysis;
        }

        public TissueAnalysisResult RebuildTissueAnalysis()
        {
            if (_tissueField == null)
            {
                _tissueAnalysis = null;
                return null;
            }

            _tissueAnalysis = new TissueAnalyzer().Analyze(_tissueField, this);
            return _tissueAnalysis;
        }

        public byte[] ExportTissueAnalysisSnapshot()
        {
            if (_tissueAnalysis == null)
                return null;

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            writer.Write(_tissueAnalysis.Width);
            writer.Write(_tissueAnalysis.Height);

            for (int y = 0; y < _tissueAnalysis.Height; y++)
            {
                for (int x = 0; x < _tissueAnalysis.Width; x++)
                {
                    writer.Write(_tissueAnalysis.GetNeighborCount(x, y));
                    writer.Write(_tissueAnalysis.GetOpennessScore(x, y));
                    writer.Write((byte)_tissueAnalysis.GetLocalType(x, y));
                }
            }

            writer.Write(_tissueAnalysis.Hubs.Count);
            for (int i = 0; i < _tissueAnalysis.Hubs.Count; i++)
            {
                TissueHub hub = _tissueAnalysis.Hubs[i];
                writer.Write(hub.TilePosition.X);
                writer.Write(hub.TilePosition.Y);
                writer.Write((byte)hub.LocalType);
                writer.Write(hub.NeighborCount);
                writer.Write(hub.Openness);
                writer.Write(hub.ImportanceScore);
            }

            writer.Write(_tissueAnalysis.Links.Count);
            for (int i = 0; i < _tissueAnalysis.Links.Count; i++)
            {
                TissueLink link = _tissueAnalysis.Links[i];
                writer.Write(link.StartHubIndex);
                writer.Write(link.EndHubIndex);
                writer.Write(link.PathCost);
                writer.Write((byte)link.LinkType);
                writer.Write(link.TilePath.Count);

                for (int pointIndex = 0; pointIndex < link.TilePath.Count; pointIndex++)
                {
                    Point point = link.TilePath[pointIndex];
                    writer.Write(point.X);
                    writer.Write(point.Y);
                }
            }

            writer.Flush();
            return stream.ToArray();
        }

        public void ImportTissueAnalysisSnapshot(byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length == 0)
            {
                _tissueAnalysis = null;
                return;
            }

            if (_tissueField == null)
                throw new System.InvalidOperationException("TissueField precisa ser carregado antes da análise.");

            using MemoryStream stream = new(snapshot);
            using BinaryReader reader = new(stream);

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            if (width != Width || height != Height)
                throw new System.ArgumentException("Tissue analysis snapshot size does not match world dimensions.", nameof(snapshot));

            TissueAnalysisResult analysis = new(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    analysis.SetNeighborCount(x, y, reader.ReadByte());
                    analysis.SetOpennessScore(x, y, reader.ReadSingle());
                    analysis.SetLocalType(x, y, (TissueLocalType)reader.ReadByte());
                }
            }

            int hubCount = reader.ReadInt32();
            for (int i = 0; i < hubCount; i++)
            {
                int tileX = reader.ReadInt32();
                int tileY = reader.ReadInt32();
                TissueLocalType localType = (TissueLocalType)reader.ReadByte();
                byte neighborCount = reader.ReadByte();
                float openness = reader.ReadSingle();
                float importanceScore = reader.ReadSingle();

                analysis.Hubs.Add(new TissueHub(
                    new Point(tileX, tileY),
                    GetTileCenter(tileX, tileY),
                    localType,
                    neighborCount,
                    openness,
                    importanceScore));
            }

            int linkCount = reader.ReadInt32();
            for (int i = 0; i < linkCount; i++)
            {
                int startHubIndex = reader.ReadInt32();
                int endHubIndex = reader.ReadInt32();
                float pathCost = reader.ReadSingle();
                TissueLink.TissueLinkType linkType = (TissueLink.TissueLinkType)reader.ReadByte();
                int pathCount = reader.ReadInt32();
                List<Point> path = new(pathCount);

                for (int pointIndex = 0; pointIndex < pathCount; pointIndex++)
                    path.Add(new Point(reader.ReadInt32(), reader.ReadInt32()));

                TissueLink link = new(startHubIndex, endHubIndex, path, pathCost);
                link.SetType(linkType);
                analysis.Links.Add(link);
            }

            for (int i = 0; i < analysis.Links.Count; i++)
            {
                TissueLink link = analysis.Links[i];
                if (link.StartHubIndex >= 0 && link.StartHubIndex < analysis.Hubs.Count)
                    analysis.Hubs[link.StartHubIndex].IncrementLinkCount();
                if (link.EndHubIndex >= 0 && link.EndHubIndex < analysis.Hubs.Count)
                    analysis.Hubs[link.EndHubIndex].IncrementLinkCount();
            }

            _tissueAnalysis = analysis;
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
                || tileType == TileType.Sand;
        }

        public bool IsSolidAt(int x, int y) => IsSolid(GetTile(x, y));

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

        public WorldChunkCoord GetChunkCoordForWorld(Vector2 worldPos)
        {
            Point tile = WorldToTile(worldPos);
            return GetChunkCoordForTile(tile.X, tile.Y);
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

            return HasAdjacentSolid(x, y);
        }

        public bool TryPlaceTile(int x, int y, TileType tileType)
        {
            if (!CanPlaceTile(x, y, tileType))
                return false;

            int wrappedX = WrapTileX(x);
            TrackTileChange(wrappedX, y, TileType.Empty, tileType);
            _tiles[wrappedX, y] = tileType;
            TileRevision++;
            return true;
        }

        public int UpdateGrassSpread()
        {
            List<(int X, int Y, TileType TileType)> changes = new();
            int processed = 0;

            while (_grassCandidateQueue.Count > 0 && processed < GrassSpreadBatchSize)
            {
                Point point = _grassCandidateQueue.Dequeue();
                long key = CreateTileKey(WrapTileX(point.X), point.Y);
                _grassCandidateKeys.Remove(key);

                if (!InBounds(point.X, point.Y))
                    continue;

                TileType tile = GetTile(point.X, point.Y);
                if (tile == TileType.Dirt)
                {
                    if (HasExposedSide(point.X, point.Y) && HasAdjacentGrass(point.X, point.Y))
                        changes.Add((WrapTileX(point.X), point.Y, TileType.Grass));
                }
                else if (tile == TileType.Grass)
                {
                    if (!HasExposedSide(point.X, point.Y))
                        changes.Add((WrapTileX(point.X), point.Y, TileType.Dirt));
                }

                processed++;
            }

            for (int i = 0; i < changes.Count; i++)
            {
                (int x, int y, TileType tileType) = changes[i];
                SetTile(x, y, tileType);
            }

            return changes.Count;
        }

        public void InitializeGrassSimulation()
        {
            _grassCandidateKeys.Clear();
            _grassCandidateQueue.Clear();

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    TileType tile = _tiles[x, y];
                    if (tile != TileType.Grass && tile != TileType.Dirt)
                        continue;

                    if (tile == TileType.Grass || HasExposedSide(x, y))
                        EnqueueGrassCandidateArea(x, y);
                }
            }
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
                TileRevision++;
            }

            if (previousTrackingState)
                BeginTileChangeTracking();
        }

        public void SetTextures(Texture2D dirt, Texture2D sand, Texture2D stone)
        {
            SetTextures(dirt, dirt, sand, stone);
        }

        public void SetTextures(Texture2D dirt, Texture2D grass, Texture2D sand, Texture2D stone)
        {
            _dirt = dirt;
            _grass = grass;
            _sand = sand;
            _stone = stone;
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

            for (int y = minTileY; y <= maxTileY; y++)
            {
                for (int x = minTileX; x <= maxTileX; x++)
                {
                    TileType tile = GetTile(x, y);
                    if (tile == TileType.Empty)
                        continue;

                    Texture2D texture = tile switch
                    {
                        TileType.Dirt => _dirt,
                        TileType.Grass => _grass,
                        TileType.Sand => _sand,
                        TileType.Stone => _stone,
                        _ => null
                    };

                    if (texture == null)
                        continue;

                    Rectangle? sourceRectangle = tile switch
                    {
                        TileType.Dirt => GetAutoTileSourceRectangle(x, y),
                        TileType.Grass => GetGrassAutoTileSourceRectangle(x, y),
                        TileType.Stone => GetAutoTileSourceRectangle(x, y),
                        TileType.Sand => GetAutoTileSourceRectangle(x, y),
                        _ => null
                    };

                    spriteBatch.Draw(texture, GetTileBounds(x, y), sourceRectangle, Color.White);
                }
            }
        }

        private Rectangle GetAutoTileSourceRectangle(int x, int y)
        {
            bool up = IsSolidAt(x, y - 1);
            bool right = IsSolidAt(x + 1, y);
            bool down = IsSolidAt(x, y + 1);
            bool left = IsSolidAt(x - 1, y);

            int connectedCount = 0;
            if (up) connectedCount++;
            if (right) connectedCount++;
            if (down) connectedCount++;
            if (left) connectedCount++;

            switch (connectedCount)
            {
                case 0:
                    return GetAutoTileSheetCell(0, 0);
                case 1:
                    if (down) return GetAutoTileSheetCell(1, 2);
                    if (left) return GetAutoTileSheetCell(2, 2);
                    if (right) return GetAutoTileSheetCell(2, 3);
                    return GetAutoTileSheetCell(1, 3);
                case 2:
                    if (left && right) return GetAutoTileSheetCell(5, 0);
                    if (up && down) return GetAutoTileSheetCell(6, 0);
                    if (right && down) return GetAutoTileSheetCell(3, 0);
                    if (left && down) return GetAutoTileSheetCell(4, 0);
                    if (up && right) return GetAutoTileSheetCell(3, 1);
                    return GetAutoTileSheetCell(4, 1);
                case 3:
                    if (!up) return GetAutoTileSheetCell(1, 0);
                    if (!right) return GetAutoTileSheetCell(2, 0);
                    if (!left) return GetAutoTileSheetCell(1, 1);
                    return GetAutoTileSheetCell(2, 1);
                default:
                    return ((x + y) & 1) == 0
                        ? GetAutoTileSheetCell(5, 1)
                        : GetAutoTileSheetCell(6, 1);
            }
        }

        private Rectangle GetGrassAutoTileSourceRectangle(int x, int y)
        {
            return GetAutoTileSourceRectangle(x, y);
        }

        private Rectangle GetAutoTileSheetCell(int column, int row)
        {
            int x = column * (AutoTileSheetTileSize + AutoTileSheetSpacing);
            int y = row * (AutoTileSheetTileSize + AutoTileSheetSpacing);
            return new Rectangle(x, y, AutoTileSheetTileSize, AutoTileSheetTileSize);
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

        private bool HasExposedSide(int x, int y)
        {
            return GetTile(x, y - 1) == TileType.Empty
                || GetTile(x - 1, y) == TileType.Empty
                || GetTile(x + 1, y) == TileType.Empty
                || GetTile(x, y + 1) == TileType.Empty;
        }

        private bool HasAdjacentGrass(int x, int y)
        {
            return GetTile(x - 1, y) == TileType.Grass
                || GetTile(x + 1, y) == TileType.Grass
                || GetTile(x, y - 1) == TileType.Grass
                || GetTile(x, y + 1) == TileType.Grass
                || GetTile(x - 1, y - 1) == TileType.Grass
                || GetTile(x + 1, y - 1) == TileType.Grass
                || GetTile(x - 1, y + 1) == TileType.Grass
                || GetTile(x + 1, y + 1) == TileType.Grass;
        }

        private void EnqueueGrassCandidateArea(int centerX, int centerY)
        {
            for (int y = centerY - 1; y <= centerY + 1; y++)
            {
                if (y < 0 || y >= Height)
                    continue;

                for (int x = centerX - 1; x <= centerX + 1; x++)
                    EnqueueGrassCandidate(x, y);
            }
        }

        private void EnqueueGrassCandidate(int x, int y)
        {
            if (!InBounds(x, y))
                return;

            int wrappedX = WrapTileX(x);
            long key = CreateTileKey(wrappedX, y);
            if (_grassCandidateKeys.Add(key))
                _grassCandidateQueue.Enqueue(new Point(wrappedX, y));
        }

        private void ClearGrassCandidates()
        {
            _grassCandidateKeys.Clear();
            _grassCandidateQueue.Clear();
        }
    }
}
