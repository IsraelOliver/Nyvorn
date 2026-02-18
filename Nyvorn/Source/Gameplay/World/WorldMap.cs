using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.World
{
    public class WorldMap
    {
        // Etapa 3: tamanho do mapa em tiles e tamanho do tile em pixels
        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }

        private Texture2D _dirt;
        private Texture2D _sand;
        private Texture2D _stone;

        // Etapa 2: a grade que guarda o mundo
        private readonly TileType[,] _tiles;

        // Etapa 3: construtor cria a matriz
        public WorldMap(int width, int height, int tileSize)
        {
            Width = width;
            Height = height;
            TileSize = tileSize;

            _tiles = new TileType[Width, Height];
        }

        // Etapa 4.1: pegar tile (com segurança)
        public TileType GetTile(int x, int y)
        {
            if (!InBounds(x, y))
                return TileType.Empty;

            return _tiles[x, y];
        }

        // Etapa 4.2: setar tile
        public void SetTile(int x, int y, TileType type)
        {
            if (!InBounds(x, y))
                return;

            _tiles[x, y] = type;
        }

        // Etapa 4: utilitário
        public bool InBounds(int x, int y)
            => x >= 0 && y >= 0 && x < Width && y < Height;

        // Etapa 4.3: regra de colisão (você decide aqui)
        public bool IsSolid(TileType t)
        {
            return t == TileType.Dirt
                || t == TileType.Stone
                || t == TileType.Sand;
        }

        public bool IsSolidAt(int x, int y) => IsSolid(GetTile(x, y));

        // Etapa 4.4: tile -> pixels (útil pra desenhar e depurar)
        public Rectangle GetTileBounds(int x, int y)
            => new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);

        // Etapa 4.4: pixels -> tile (útil pra colisão e raycast)
        public Point WorldToTile(Vector2 worldPos)
            => new Point((int)(worldPos.X / TileSize), (int)(worldPos.Y / TileSize));

        // Etapa 5: mapa de teste (fixo) para validar tudo
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