using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.World
{
    public class WorldMap
    {
        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }

        private Texture2D _dirt;
        private Texture2D _sand;
        private Texture2D _stone;

        private readonly TileType[,] _tiles; // grade de tiles (x, y)

        public WorldMap(int width, int height, int tileSize)
        {
            Width = width;
            Height = height;
            TileSize = tileSize;

            _tiles = new TileType[Width, Height];
        }

        public TileType GetTile(int x, int y) // essa função é para acessar o tipo de tile em (x, y)
        {
            if (!InBounds(x, y))
                return TileType.Empty;

            return _tiles[x, y];
        }

        public void SetTile(int x, int y, TileType type) // essa função é para modificar o tipo de tile em (x, y)
        {
            if (!InBounds(x, y))
                return;

            _tiles[x, y] = type;
        }

        public bool InBounds(int x, int y) // verifica se (x, y) está dentro dos limites do mapa
            => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsSolid(TileType t) // verifica se um tipo de tile é sólido (colisível)
        {
            return t == TileType.Dirt
                || t == TileType.Stone
                || t == TileType.Sand;
        }

        public bool IsSolidAt(int x, int y) => IsSolid(GetTile(x, y)); // verifica se o tile em (x, y) é sólido diferente de IsSolid, que recebe o tipo do tile, essa função recebe as coordenadas e já retorna se é sólido ou não

        public Rectangle GetTileBounds(int x, int y) // retorna os limites em pixels do tile em (x, y) (útil pra desenhar e colisão)
            => new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);

        public Point WorldToTile(Vector2 worldPos) // converte uma posição em pixels para coordenadas de tile, ou seja, dado um ponto no mundo, retorna qual tile ele corresponde
            => new Point((int)(worldPos.X / TileSize), (int)(worldPos.Y / TileSize));

        public void GenerateTest()
        {
            // limpa tudo (Empty)
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    _tiles[x, y] = TileType.Empty;

            // cria chão
            int groundY = Height - 3;
            for (int x = 0; x < Width; x++)
                _tiles[x, groundY] = TileType.Dirt;

            // plataforma
            for (int x = 10; x < 20; x++)
                _tiles[x, groundY - 5] = TileType.Stone;

            for (int x = 25; x < 40; x++)
                _tiles[x, groundY - 10] = TileType.Sand;

            for (int x = 45; x < 70; x++)
                _tiles[x, groundY - 15] = TileType.Dirt;

            // parede esquerda e direita (só pra teste)
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
                    var t = GetTile(x, y);
                    if (t == TileType.Empty)
                        continue;

                    Texture2D tex = t switch
                    {
                        TileType.Dirt  => _dirt,
                        TileType.Sand  => _sand,
                        TileType.Stone => _stone,
                        _ => null
                    };

                    if (tex == null) continue;

                    spriteBatch.Draw(tex, GetTileBounds(x, y), Color.White);
                }
            }
        }
    }
}