using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.World
{
    public class WorldMap
    {
        private const int DirtSheetTileSize = 8;
        private const int DirtSheetSpacing = 1;

        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }

        private Texture2D _dirt;
        private Texture2D _sand;
        private Texture2D _stone;

        private readonly TileType[,] _tiles;

        public WorldMap(int width, int height, int tileSize)
        {
            Width = width;
            Height = height;
            TileSize = tileSize;

            _tiles = new TileType[Width, Height];
        }

        public TileType GetTile(int x, int y)
        {
            if (!InBounds(x, y))
                return TileType.Empty;

            return _tiles[x, y];
        }

        public void SetTile(int x, int y, TileType type)
        {
            if (!InBounds(x, y))
                return;

            _tiles[x, y] = type;
        }

        public bool InBounds(int x, int y)
            => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsSolid(TileType tileType)
        {
            return tileType == TileType.Dirt
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

        public Point WorldToTile(Vector2 worldPos)
            => new Point((int)(worldPos.X / TileSize), (int)(worldPos.Y / TileSize));

        public Vector2 GetTileCenter(int x, int y)
            => new Vector2(x * TileSize + (TileSize * 0.5f), y * TileSize + (TileSize * 0.5f));

        public bool TryBreakTile(int x, int y, out TileType removedTile)
        {
            removedTile = TileType.Empty;

            if (!InBounds(x, y))
                return false;

            TileType currentTile = _tiles[x, y];
            if (!IsSolid(currentTile))
                return false;

            removedTile = currentTile;
            _tiles[x, y] = TileType.Empty;
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

            _tiles[x, y] = tileType;
            return true;
        }

        public void GenerateTest()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                    _tiles[x, y] = TileType.Empty;
            }

            int groundY = Height - 3;
            for (int x = 0; x < Width; x++)
                _tiles[x, groundY] = TileType.Dirt;

            for (int x = 10; x < 20; x++)
                _tiles[x, groundY - 5] = TileType.Stone;

            for (int x = 25; x < 40; x++)
                _tiles[x, groundY - 10] = TileType.Sand;

            for (int x = 45; x < 70; x++)
                _tiles[x, groundY - 15] = TileType.Dirt;

            for (int y = 0; y < Height; y++)
            {
                _tiles[0, y] = TileType.Stone;
                _tiles[Width - 1, y] = TileType.Stone;
            }
        }

        public void SetTextures(Texture2D dirt, Texture2D sand, Texture2D stone)
        {
            _dirt = dirt;
            _sand = sand;
            _stone = stone;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    TileType tile = GetTile(x, y);
                    if (tile == TileType.Empty)
                        continue;

                    Texture2D texture = tile switch
                    {
                        TileType.Dirt => _dirt,
                        TileType.Sand => _sand,
                        TileType.Stone => _stone,
                        _ => null
                    };

                    if (texture == null)
                        continue;

                    Rectangle? sourceRectangle = tile switch
                    {
                        TileType.Dirt => GetDirtSourceRectangle(x, y),
                        _ => null
                    };

                    spriteBatch.Draw(texture, GetTileBounds(x, y), sourceRectangle, Color.White);
                }
            }
        }

        private Rectangle GetDirtSourceRectangle(int x, int y)
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
                    return GetDirtSheetCell(0, 0);
                case 1:
                    if (down) return GetDirtSheetCell(1, 2);
                    if (left) return GetDirtSheetCell(2, 2);
                    if (right) return GetDirtSheetCell(2, 3);
                    return GetDirtSheetCell(1, 3);
                case 2:
                    if (left && right) return GetDirtSheetCell(5, 0);
                    if (up && down) return GetDirtSheetCell(6, 0);
                    if (right && down) return GetDirtSheetCell(3, 0);
                    if (left && down) return GetDirtSheetCell(4, 0);
                    if (up && right) return GetDirtSheetCell(3, 1);
                    return GetDirtSheetCell(4, 1);
                case 3:
                    if (!up) return GetDirtSheetCell(1, 0);
                    if (!right) return GetDirtSheetCell(2, 0);
                    if (!left) return GetDirtSheetCell(1, 1);
                    return GetDirtSheetCell(2, 1);
                default:
                    return ((x + y) & 1) == 0
                        ? GetDirtSheetCell(5, 1)
                        : GetDirtSheetCell(6, 1);
            }
        }

        private Rectangle GetDirtSheetCell(int column, int row)
        {
            int x = column * (DirtSheetTileSize + DirtSheetSpacing);
            int y = row * (DirtSheetTileSize + DirtSheetSpacing);
            return new Rectangle(x, y, DirtSheetTileSize, DirtSheetTileSize);
        }
    }
}
