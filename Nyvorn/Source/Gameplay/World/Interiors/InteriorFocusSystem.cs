using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.World;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Interiors
{
    public sealed class InteriorFocusSystem
    {
        private const int MaxInteriorTiles = 900;
        private const int MinInteriorTiles = 6;
        private const float NormalFocusDim = 0.30f;
        private const float ConstructionFocusDim = 0.14f;
        private const float FadeSpeed = 7.5f;
        private const int RoomPaddingPixels = 2;

        private static readonly Point[] NeighborOffsets =
        {
            new Point(1, 0),
            new Point(-1, 0),
            new Point(0, 1),
            new Point(0, -1)
        };

        private readonly Queue<Point> floodQueue = new();
        private readonly HashSet<Point> visitedTiles = new();
        private readonly HashSet<Point> activeTiles = new();

        private Point lastPlayerTile = new Point(int.MinValue, int.MinValue);
        private int lastTileRevision = -1;
        private int lastDoorRevision = -1;
        private float dimAmount;

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required DoorRuntimeSystem DoorRuntimeSystem { get; init; }

        public bool HasActiveInterior { get; private set; }
        public Rectangle RoomBounds { get; private set; }

        public bool TryGetCameraFocus(out Vector2 focus)
        {
            if (!HasActiveInterior)
            {
                focus = Vector2.Zero;
                return false;
            }

            focus = RoomBounds.Center.ToVector2();
            return true;
        }

        public void Update(float dt, bool constructionMode)
        {
            Point playerTile = GetPlayerInteriorSampleTile();
            int wrappedPlayerX = WorldMap.WrapTileX(playerTile.X);
            Point wrappedPlayerTile = new Point(wrappedPlayerX, playerTile.Y);
            bool worldChanged = lastTileRevision != WorldMap.TileRevision || lastDoorRevision != DoorRuntimeSystem.Revision;
            bool playerChangedTile = lastPlayerTile != wrappedPlayerTile;

            if (worldChanged || (playerChangedTile && !activeTiles.Contains(wrappedPlayerTile)))
                RebuildActiveInterior(wrappedPlayerTile);

            lastPlayerTile = wrappedPlayerTile;
            lastTileRevision = WorldMap.TileRevision;
            lastDoorRevision = DoorRuntimeSystem.Revision;

            float targetDim = HasActiveInterior
                ? (constructionMode ? ConstructionFocusDim : NormalFocusDim)
                : 0f;

            dimAmount = MathHelper.Lerp(dimAmount, targetDim, MathHelper.Clamp(dt * FadeSpeed, 0f, 1f));
        }

        public void Draw(SpriteBatch spriteBatch, Camera2D camera, Texture2D pixel, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (pixel == null || dimAmount <= 0.01f || !HasActiveInterior)
                return;

            float viewWidth = screenWidth / camera.Zoom;
            float viewHeight = screenHeight / camera.Zoom;
            int viewLeft = (int)System.MathF.Floor(camera.Position.X - worldOffsetX);
            int viewTop = (int)System.MathF.Floor(camera.Position.Y);
            int viewRight = (int)System.MathF.Ceiling(viewLeft + viewWidth);
            int viewBottom = (int)System.MathF.Ceiling(viewTop + viewHeight);
            Rectangle viewBounds = new Rectangle(viewLeft, viewTop, viewRight - viewLeft, viewBottom - viewTop);
            Color dimColor = new Color((byte)4, (byte)6, (byte)10, (byte)(dimAmount * 255f));

            Rectangle room = RoomBounds;
            room.Inflate(RoomPaddingPixels, RoomPaddingPixels);

            if (!viewBounds.Intersects(room))
            {
                spriteBatch.Draw(pixel, viewBounds, dimColor);
                return;
            }

            DrawOutsideRoom(spriteBatch, pixel, viewBounds, room, dimColor);
        }

        private void RebuildActiveInterior(Point startTile)
        {
            activeTiles.Clear();
            HasActiveInterior = false;
            RoomBounds = Rectangle.Empty;

            if (!CanOccupyInterior(startTile))
                return;

            floodQueue.Clear();
            visitedTiles.Clear();
            floodQueue.Enqueue(startTile);
            visitedTiles.Add(startTile);

            int minX = startTile.X;
            int maxX = startTile.X;
            int minY = startTile.Y;
            int maxY = startTile.Y;
            bool isClosed = true;
            bool hasDoor = false;

            while (floodQueue.Count > 0)
            {
                Point tile = floodQueue.Dequeue();

                if (visitedTiles.Count > MaxInteriorTiles)
                {
                    isClosed = false;
                    break;
                }

                minX = System.Math.Min(minX, tile.X);
                maxX = System.Math.Max(maxX, tile.X);
                minY = System.Math.Min(minY, tile.Y);
                maxY = System.Math.Max(maxY, tile.Y);

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    Point neighbor = new Point(tile.X + NeighborOffsets[i].X, tile.Y + NeighborOffsets[i].Y);
                    neighbor.X = WorldMap.WrapTileX(neighbor.X);

                    if (!WorldMap.InBounds(neighbor.X, neighbor.Y))
                    {
                        isClosed = false;
                        continue;
                    }

                    if (IsDoorBoundary(neighbor))
                    {
                        hasDoor = true;
                        continue;
                    }

                    if (IsInteriorBoundary(neighbor))
                        continue;

                    if (!HasBackgroundWall(neighbor))
                    {
                        isClosed = false;
                        continue;
                    }

                    if (visitedTiles.Add(neighbor))
                        floodQueue.Enqueue(neighbor);
                }
            }

            if (!isClosed || !hasDoor || visitedTiles.Count < MinInteriorTiles)
                return;

            foreach (Point tile in visitedTiles)
                activeTiles.Add(tile);

            HasActiveInterior = true;
            RoomBounds = new Rectangle(
                minX * WorldMap.TileSize,
                minY * WorldMap.TileSize,
                ((maxX - minX) + 1) * WorldMap.TileSize,
                ((maxY - minY) + 1) * WorldMap.TileSize);
        }

        private Point GetPlayerInteriorSampleTile()
        {
            Point center = Player.Hurtbox.Center;
            return WorldMap.WorldToTile(center.ToVector2());
        }

        private bool CanOccupyInterior(Point tile)
        {
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return false;

            if (IsInteriorBoundary(tile))
                return false;

            return HasBackgroundWall(tile);
        }

        private bool IsInteriorBoundary(Point tile)
        {
            return WorldMap.IsSolidAt(tile.X, tile.Y) ||
                   IsDoorBoundary(tile);
        }

        private bool IsDoorBoundary(Point tile)
        {
            return DoorRuntimeSystem.IsObjectOccupyingTile(tile.X, tile.Y);
        }

        private bool HasBackgroundWall(Point tile)
        {
            return WorldMap.IsSolid(WorldMap.GetBackgroundTile(tile.X, tile.Y));
        }

        private static void DrawOutsideRoom(SpriteBatch spriteBatch, Texture2D pixel, Rectangle view, Rectangle room, Color color)
        {
            int topHeight = System.Math.Max(0, room.Top - view.Top);
            if (topHeight > 0)
                spriteBatch.Draw(pixel, new Rectangle(view.Left, view.Top, view.Width, topHeight), color);

            int bottomHeight = System.Math.Max(0, view.Bottom - room.Bottom);
            if (bottomHeight > 0)
                spriteBatch.Draw(pixel, new Rectangle(view.Left, room.Bottom, view.Width, bottomHeight), color);

            int middleTop = System.Math.Max(view.Top, room.Top);
            int middleBottom = System.Math.Min(view.Bottom, room.Bottom);
            int middleHeight = middleBottom - middleTop;
            if (middleHeight <= 0)
                return;

            int leftWidth = System.Math.Max(0, room.Left - view.Left);
            if (leftWidth > 0)
                spriteBatch.Draw(pixel, new Rectangle(view.Left, middleTop, leftWidth, middleHeight), color);

            int rightWidth = System.Math.Max(0, view.Right - room.Right);
            if (rightWidth > 0)
                spriteBatch.Draw(pixel, new Rectangle(room.Right, middleTop, rightWidth, middleHeight), color);
        }
    }
}
