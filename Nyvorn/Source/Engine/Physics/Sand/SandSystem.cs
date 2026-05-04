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

        private Point TryMoveSandDown(int x, int y)
        {
            int newY = y + 1;

            if (newY >= Height)
                return new Point(x, y);

            int tileX = x / TileSize;
            int tileY = newY / TileSize;

            TileType tileBelow = worldMap.GetTile(tileX, tileY);

            if (worldMap.IsSolid(tileBelow))
                return new Point(x, y);

            if (sandGrid[x, newY])
                return new Point(x, y);

            sandGrid[x, y] = false;
            sandGrid[x, newY] = true;

            return new Point(x, newY);
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