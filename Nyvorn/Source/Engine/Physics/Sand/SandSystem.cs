using System;
using Nyvorn.Source.World;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Physics.Sand
{
    public class SandSystem
    {
        private readonly WorldMap worldMap;

        private readonly bool[,] sandGrid;

        private readonly List<Point> activeSand = new();

        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }

        private readonly Random random = new();

        public SandSystem(WorldMap worldMap)
        {
            this.worldMap = worldMap;

            TileSize = worldMap.TileSize;
            Width = worldMap.Width * TileSize;
            Height = worldMap.Height * TileSize;

            sandGrid = new bool[Width, Height];
        }

        public bool HasSandAt(int pixelX, int pixelY)
        {
            if (pixelX < 0 || pixelY < 0 || pixelX >= Width || pixelY >= Height)
                return false;

            return sandGrid[pixelX, pixelY];
        }

        public void SetSandAt(int pixelX, int pixelY, bool value)
        {
            if (pixelX < 0 || pixelY < 0 || pixelX >= Width || pixelY >= Height)
                return;

            bool oldValue = sandGrid[pixelX, pixelY];
            sandGrid[pixelX, pixelY] = value;

            if (value && !oldValue)
                activeSand.Add(new Point(pixelX, pixelY));
        }

        private bool CanMoveTo(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return false;

            if (sandGrid[x, y])
                return false;

            int tileX = x / TileSize;
            int tileY = y / TileSize;

            TileType tile = worldMap.GetTile(tileX, tileY);

            TileType tileBelow = worldMap.GetTile(tileX, tileY);

            return !worldMap.IsSolid(tileBelow);
        }

        private Point MoveSand(int fromX, int fromY, int toX, int toY)
        {
            sandGrid[fromX, fromY] = false;
            sandGrid[toX, toY] = true;

            return new Point(toX, toY);
        }

        private Point TryMoveSandDown(int x, int y)
        {
            // 1. Tenta cair reto
            if (CanMoveTo(x, y + 1))
                return MoveSand(x, y, x, y + 1);

            // 2. Randomiza qual diagonal testar primeiro
            bool tryLeftFirst = random.Next(2) == 0;

            int firstDx = tryLeftFirst ? -1 : 1;
            int secondDx = tryLeftFirst ? 1 : -1;

            // 3. Tenta primeira diagonal
            if (CanMoveTo(x + firstDx, y + 1))
                return MoveSand(x, y, x + firstDx, y + 1);

            // 4. Tenta segunda diagonal
            if (CanMoveTo(x + secondDx, y + 1))
                return MoveSand(x, y, x + secondDx, y + 1);

            // 5. Não conseguiu mover
            return new Point(x, y);
        }
        public void Update(float dt)
        {
            for (int i = activeSand.Count - 1; i >= 0; i--)
            {
                Point current = activeSand[i];

                if (!sandGrid[current.X, current.Y])
                {
                    activeSand.RemoveAt(i);
                    continue;
                }

                Point newPosition = TryMoveSandDown(current.X, current.Y);

                activeSand[i] = newPosition;
            }
        }
    }
}