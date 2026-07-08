using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Tissue;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.UI
{
    public readonly record struct WorldMinimapInteractionResult(bool ConsumedMouse, bool ToggleTissueMode, int TravelHubIndex);

    public sealed class WorldMinimapRenderer
    {
        private readonly record struct MinimapLayout(Rectangle Panel, Rectangle SourceRect);

        private readonly GraphicsDevice graphicsDevice;
        private readonly Texture2D pixel;
        private Texture2D minimapTexture;
        private Texture2D tissueOverlayTexture;
        private Texture2D tissueDetailTexture;
        private Color[] minimapPixels = System.Array.Empty<Color>();
        private Color[] tissueOverlayPixels = System.Array.Empty<Color>();
        private Color[] tissueDetailPixels = System.Array.Empty<Color>();
        private int cachedTissueSeed = int.MinValue;
        private int cachedTissueNodeCount = -1;
        private int cachedTissueBranchCount = -1;
        private int cachedTileRevision = -1;
        private int cachedSourceWidth;
        private int cachedSourceHeight;
        private float zoom = 1f;
        private Vector2? viewCenterTile;
        private bool isDragging;
        private Point lastDragMousePosition;
        private const float MinZoom = 1f;
        private const float MaxZoom = 18f;
        private const float ZoomStep = 1.2f;
        private static readonly Color DynamicSandMinimapColor = new Color(255, 230, 171);
        private static readonly Color WaterMinimapColor = new Color(34, 128, 205) * 0.78f;

        public WorldMinimapRenderer(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public WorldMinimapInteractionResult HandleInput(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight, Vector2 mouseScreenPosition, int mouseWheelDelta, bool pointerDown, bool pointerJustPressed, bool tissueMode, bool fastTravelEnabled, IReadOnlySet<int> activatedHubKeys)
        {
            MinimapLayout layout = GetLayout(worldMap, playerPosition, screenWidth, screenHeight);
            Point mousePoint = mouseScreenPosition.ToPoint();
            bool pointerOverMap = layout.Panel.Contains(mousePoint);
            bool consumedMouse = pointerOverMap;
            Rectangle tissueToggleButton = GetTissueToggleButton(layout.Panel);

            if (pointerJustPressed && tissueToggleButton.Contains(mousePoint))
            {
                isDragging = false;
                return new WorldMinimapInteractionResult(true, true, -1);
            }

            if (pointerJustPressed &&
                pointerOverMap &&
                tissueMode &&
                fastTravelEnabled &&
                TryGetActivatedHubAtPoint(worldMap, layout, mousePoint, activatedHubKeys, out int travelHubIndex))
            {
                isDragging = false;
                return new WorldMinimapInteractionResult(true, false, travelHubIndex);
            }

            if (mouseWheelDelta != 0)
                AdjustZoomAtPoint(worldMap, playerPosition, screenWidth, screenHeight, layout, mouseScreenPosition, mouseWheelDelta, pointerOverMap);

            if (zoom > MinZoom && pointerDown)
            {
                if (!isDragging && pointerOverMap)
                {
                    isDragging = true;
                    lastDragMousePosition = mousePoint;
                    consumedMouse = true;
                }
                else if (isDragging)
                {
                    PanFromDrag(worldMap, playerPosition, screenWidth, screenHeight, mousePoint);
                    consumedMouse = true;
                }
            }
            else
            {
                isDragging = false;
            }

            return new WorldMinimapInteractionResult(consumedMouse || isDragging, false, -1);
        }

        public void Draw(SpriteBatch spriteBatch, WorldMap worldMap, LiquidSystem liquidSystem, SandSystem sandSystem, TissueNetwork tissueNetwork, Camera2D camera, Vector2 playerPosition, int screenWidth, int screenHeight, bool tissueMode, IReadOnlySet<int> activatedHubKeys)
        {
            EnsureMinimapTexture(worldMap);

            MinimapLayout layout = GetLayout(worldMap, playerPosition, screenWidth, screenHeight);
            Rectangle panel = layout.Panel;
            Rectangle sourceRect = layout.SourceRect;

            Rectangle backdrop = panel;
            backdrop.Inflate(20, 20);
            DrawRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), new Color(3, 8, 12, 180));
            DrawRect(spriteBatch, backdrop, new Color(8, 18, 24, 235));
            DrawRectOutline(spriteBatch, backdrop, 2, new Color(133, 179, 191));

            if (tissueMode)
            {
                spriteBatch.Draw(minimapTexture, panel, sourceRect, Color.White);
                DrawSandOverlay(spriteBatch, worldMap, sandSystem, panel, sourceRect);
                DrawLiquidOverlay(spriteBatch, worldMap, liquidSystem, panel, sourceRect);
                DrawRect(spriteBatch, panel, new Color(5, 8, 10, 120));
                if (tissueNetwork != null && (tissueNetwork.Nodes.Count > 0 || tissueNetwork.Branches.Count > 0))
                {
                    EnsureTissueOverlayTexture(worldMap, tissueNetwork);
                    spriteBatch.Draw(tissueOverlayTexture, panel, sourceRect, Color.White);
                    if (zoom >= TissueConfig.MinimapVisual.MicroDetailZoom)
                        spriteBatch.Draw(tissueDetailTexture, panel, sourceRect, Color.White);
                }
                else
                {
                    DrawTissueOverlay(spriteBatch, worldMap, tissueNetwork, panel, sourceRect, activatedHubKeys);
                }
            }
            else
            {
                spriteBatch.Draw(minimapTexture, panel, sourceRect, Color.White);
                DrawSandOverlay(spriteBatch, worldMap, sandSystem, panel, sourceRect);
                DrawLiquidOverlay(spriteBatch, worldMap, liquidSystem, panel, sourceRect);
            }

            Rectangle cameraRect = GetCameraRect(worldMap, camera, panel, sourceRect);
            DrawRectOutline(spriteBatch, cameraRect, 2, new Color(255, 241, 193));

            Point playerPoint = GetWorldPointOnMinimap(worldMap, playerPosition, panel, sourceRect);
            DrawRect(spriteBatch, new Rectangle(playerPoint.X - 2, playerPoint.Y - 2, 5, 5), new Color(255, 120, 120));

            DrawRect(spriteBatch, new Rectangle(backdrop.X, backdrop.Y - 26, 180, 22), new Color(8, 18, 24, 235));
            DrawRectOutline(spriteBatch, new Rectangle(backdrop.X, backdrop.Y - 26, 180, 22), 1, new Color(133, 179, 191));
            DrawModeButtons(spriteBatch, panel, tissueMode);
        }

        private void DrawSandOverlay(SpriteBatch spriteBatch, WorldMap worldMap, SandSystem sandSystem, Rectangle panel, Rectangle sourceRect)
        {
            if (sandSystem == null || sourceRect.Width <= 0 || sourceRect.Height <= 0)
                return;

            int minPixelX = sourceRect.Left * worldMap.TileSize;
            int maxPixelX = (sourceRect.Right * worldMap.TileSize) - 1;
            int minPixelY = sourceRect.Top * worldMap.TileSize;
            int maxPixelY = (sourceRect.Bottom * worldMap.TileSize) - 1;

            foreach (Rectangle sandSegment in sandSystem.GetVisibleSegments(minPixelX, maxPixelX, minPixelY, maxPixelY))
            {
                Rectangle drawBounds = MapWorldPixelRectToMinimap(worldMap, sandSegment, panel, sourceRect);
                if (drawBounds.Width <= 0 || drawBounds.Height <= 0)
                    continue;

                DrawRect(spriteBatch, drawBounds, DynamicSandMinimapColor);
            }
        }

        private void DrawLiquidOverlay(SpriteBatch spriteBatch, WorldMap worldMap, LiquidSystem liquidSystem, Rectangle panel, Rectangle sourceRect)
        {
            if (liquidSystem == null || sourceRect.Width <= 0 || sourceRect.Height <= 0)
                return;

            int minPixelX = sourceRect.Left * worldMap.TileSize;
            int maxPixelX = (sourceRect.Right * worldMap.TileSize) - 1;
            int minPixelY = sourceRect.Top * worldMap.TileSize;
            int maxPixelY = (sourceRect.Bottom * worldMap.TileSize) - 1;

            foreach (Rectangle liquidSegment in liquidSystem.GetVisibleSegments(minPixelX, maxPixelX, minPixelY, maxPixelY))
            {
                Rectangle drawBounds = MapWorldPixelRectToMinimap(worldMap, liquidSegment, panel, sourceRect);
                if (drawBounds.Width <= 0 || drawBounds.Height <= 0)
                    continue;

                DrawRect(spriteBatch, drawBounds, WaterMinimapColor);
            }
        }

        private static Rectangle MapWorldPixelRectToMinimap(WorldMap worldMap, Rectangle worldRect, Rectangle panel, Rectangle sourceRect)
        {
            float tileSize = worldMap.TileSize;
            float leftTile = worldRect.Left / tileSize;
            float topTile = worldRect.Top / tileSize;
            float rightTile = worldRect.Right / tileSize;
            float bottomTile = worldRect.Bottom / tileSize;

            int x = panel.X + (int)System.MathF.Floor(((leftTile - sourceRect.X) / sourceRect.Width) * panel.Width);
            int y = panel.Y + (int)System.MathF.Floor(((topTile - sourceRect.Y) / sourceRect.Height) * panel.Height);
            int right = panel.X + (int)System.MathF.Ceiling(((rightTile - sourceRect.X) / sourceRect.Width) * panel.Width);
            int bottom = panel.Y + (int)System.MathF.Ceiling(((bottomTile - sourceRect.Y) / sourceRect.Height) * panel.Height);

            x = System.Math.Clamp(x, panel.Left, panel.Right);
            y = System.Math.Clamp(y, panel.Top, panel.Bottom);
            right = System.Math.Clamp(right, panel.Left, panel.Right);
            bottom = System.Math.Clamp(bottom, panel.Top, panel.Bottom);

            if (right <= x && x < panel.Right)
                right = x + 1;
            if (bottom <= y && y < panel.Bottom)
                bottom = y + 1;

            return new Rectangle(x, y, right - x, bottom - y);
        }

        private void EnsureTissueOverlayTexture(WorldMap worldMap, TissueNetwork tissueNetwork)
        {
            int width = worldMap.Width;
            int height = worldMap.Height;
            bool needsResize = tissueOverlayTexture == null || tissueOverlayTexture.Width != width || tissueOverlayTexture.Height != height;
            bool networkChanged = cachedTissueSeed != tissueNetwork.Seed ||
                                  cachedTissueNodeCount != tissueNetwork.Nodes.Count ||
                                  cachedTissueBranchCount != tissueNetwork.Branches.Count;
            if (!needsResize && !networkChanged)
                return;

            if (needsResize)
            {
                tissueOverlayTexture?.Dispose();
                tissueDetailTexture?.Dispose();
                tissueOverlayTexture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
                tissueDetailTexture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
                tissueOverlayPixels = new Color[width * height];
                tissueDetailPixels = new Color[width * height];
            }
            else
            {
                System.Array.Clear(tissueOverlayPixels);
                System.Array.Clear(tissueDetailPixels);
            }

            for (int branchIndex = 0; branchIndex < tissueNetwork.Branches.Count; branchIndex++)
            {
                TissueBranch branch = tissueNetwork.Branches[branchIndex];
                Color[] target = branch.Kind == TissueBranch.TissueBranchKind.Micro ? tissueDetailPixels : tissueOverlayPixels;
                Color color = branch.Kind == TissueBranch.TissueBranchKind.Micro
                    ? TissueConfig.MinimapVisual.MicroColor
                    : Color.Lerp(TissueConfig.MinimapVisual.BranchColorLow, TissueConfig.MinimapVisual.BranchColorHigh, branch.Intensity);
                int thickness = branch.Kind == TissueBranch.TissueBranchKind.Micro
                    ? TissueConfig.MinimapVisual.MicroThickness
                    : (branch.IsPrimary ? TissueConfig.MinimapVisual.PrimaryBranchThickness : TissueConfig.MinimapVisual.BranchThickness);
                for (int pointIndex = 0; pointIndex < branch.Points.Count - 1; pointIndex++)
                {
                    Point start = WorldPixelToWrappedTile(branch.Points[pointIndex], worldMap);
                    Point end = WorldPixelToWrappedTile(branch.Points[pointIndex + 1], worldMap);
                    DrawOverlayLine(target, width, height, start, end, color, thickness);
                }
            }

            for (int nodeIndex = 0; nodeIndex < tissueNetwork.Nodes.Count; nodeIndex++)
            {
                TissueNode node = tissueNetwork.Nodes[nodeIndex];
                if (!node.IsPrimary)
                    continue;
                Point center = WorldPixelToWrappedTile(node.Position, worldMap);
                int radius = System.Math.Clamp(
                    1 + (node.Degree / TissueConfig.MinimapVisual.NodeDegreeDivisor),
                    TissueConfig.MinimapVisual.NodeRadiusMin,
                    TissueConfig.MinimapVisual.NodeRadiusMax);
                DrawOverlayDisc(tissueOverlayPixels, width, height, center, radius, TissueConfig.MinimapVisual.NodeColor);
            }

            tissueOverlayTexture.SetData(tissueOverlayPixels);
            tissueDetailTexture.SetData(tissueDetailPixels);
            cachedTissueSeed = tissueNetwork.Seed;
            cachedTissueNodeCount = tissueNetwork.Nodes.Count;
            cachedTissueBranchCount = tissueNetwork.Branches.Count;
        }

        private static Point WorldPixelToWrappedTile(Vector2 position, WorldMap worldMap)
        {
            int tileX = (int)System.MathF.Floor(position.X / worldMap.TileSize);
            tileX = worldMap.WrapTileX(tileX);
            int tileY = System.Math.Clamp((int)System.MathF.Floor(position.Y / worldMap.TileSize), 0, worldMap.Height - 1);
            return new Point(tileX, tileY);
        }

        private static void DrawOverlayLine(Color[] pixels, int width, int height, Point start, Point end, Color color, int thickness)
        {
            int dxWrapped = end.X - start.X;
            if (dxWrapped > width / 2) end.X -= width;
            else if (dxWrapped < -width / 2) end.X += width;
            int dx = System.Math.Abs(end.X - start.X);
            int sx = start.X < end.X ? 1 : -1;
            int dy = -System.Math.Abs(end.Y - start.Y);
            int sy = start.Y < end.Y ? 1 : -1;
            int error = dx + dy;
            int x = start.X;
            int y = start.Y;

            while (true)
            {
                DrawOverlayDisc(pixels, width, height, new Point(x, y), System.Math.Max(0, thickness - 1), color);
                if (x == end.X && y == end.Y)
                    break;
                int twiceError = error * 2;
                if (twiceError >= dy) { error += dy; x += sx; }
                if (twiceError <= dx) { error += dx; y += sy; }
            }
        }

        private static void DrawOverlayDisc(Color[] pixels, int width, int height, Point center, int radius, Color color)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            {
                if (y < 0 || y >= height)
                    continue;
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    int dx = x - center.X;
                    int dy = y - center.Y;
                    if ((dx * dx) + (dy * dy) > radius * radius)
                        continue;
                    int wrappedX = x % width;
                    if (wrappedX < 0) wrappedX += width;
                    int index = (y * width) + wrappedX;
                    if (pixels[index].A <= color.A)
                        pixels[index] = color;
                }
            }
        }

        private void DrawTissueOverlay(SpriteBatch spriteBatch, WorldMap worldMap, TissueNetwork tissueNetwork, Rectangle panel, Rectangle sourceRect, IReadOnlySet<int> activatedHubKeys)
        {
            if (tissueNetwork == null)
                return;

            Rectangle sourceWorldRect = new Rectangle(
                sourceRect.X * worldMap.TileSize,
                sourceRect.Y * worldMap.TileSize,
                sourceRect.Width * worldMap.TileSize,
                sourceRect.Height * worldMap.TileSize);

            foreach (TissueBranch branch in tissueNetwork.Branches)
            {
                if (!sourceWorldRect.Intersects(branch.Bounds))
                    continue;

                float thickness = branch.IsPrimary ? 2.4f : 1.4f;
                Color branchColor = branch.IsPrimary
                    ? new Color(255, 255, 255)
                    : new Color(210, 210, 210);

                for (int i = 0; i < branch.Points.Count - 1; i++)
                {
                    Vector2 start = MapWorldToMinimap(worldMap, branch.Points[i], panel, sourceRect);
                    Vector2 end = MapWorldToMinimap(worldMap, branch.Points[i + 1], panel, sourceRect);
                    DrawLine(spriteBatch, start, end, branchColor, thickness);
                }
            }

            foreach (TissueNode node in tissueNetwork.Nodes)
            {
                Vector2 nodePosition = node.Position;
                if (nodePosition.X < sourceWorldRect.Left || nodePosition.X > sourceWorldRect.Right ||
                    nodePosition.Y < sourceWorldRect.Top || nodePosition.Y > sourceWorldRect.Bottom)
                    continue;

                Vector2 mapped = MapWorldToMinimap(worldMap, nodePosition, panel, sourceRect);
                float size = node.IsPrimary ? 4f : 2f;
                DrawRect(spriteBatch, new Rectangle((int)System.MathF.Round(mapped.X - (size * 0.5f)), (int)System.MathF.Round(mapped.Y - (size * 0.5f)), (int)size, (int)size), Color.White);
            }
        }

        private void DrawTissueFieldOverlay(SpriteBatch spriteBatch, WorldMap worldMap, TissueField tissueField, Rectangle panel, Rectangle sourceRect, IReadOnlySet<int> activatedHubKeys)
        {
            TissueAnalysisResult analysis = GetTissueAnalysis(worldMap, tissueField);
            if (analysis == null)
                return;
            int minPixelWidth = System.Math.Max(1, (int)System.MathF.Ceiling(panel.Width / (float)sourceRect.Width));
            int minPixelHeight = System.Math.Max(1, (int)System.MathF.Ceiling(panel.Height / (float)sourceRect.Height));

            DrawTissueLinks(spriteBatch, worldMap, analysis, panel, sourceRect);
            DrawTissueHubMarkers(spriteBatch, worldMap, analysis, panel, sourceRect, minPixelWidth, minPixelHeight, activatedHubKeys);
        }

        private TissueAnalysisResult GetTissueAnalysis(WorldMap worldMap, TissueField tissueField)
        {
            return worldMap.GetOrCreateTissueAnalysis();
        }

        private void DrawTissueHubMarkers(SpriteBatch spriteBatch, WorldMap worldMap, TissueAnalysisResult analysis, Rectangle panel, Rectangle sourceRect, int minPixelWidth, int minPixelHeight, IReadOnlySet<int> activatedHubKeys)
        {
            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                if (hub.TilePosition.X < sourceRect.Left || hub.TilePosition.X >= sourceRect.Right ||
                    hub.TilePosition.Y < sourceRect.Top || hub.TilePosition.Y >= sourceRect.Bottom)
                {
                    continue;
                }

                Color hubColor = GetHubColor(worldMap, hub, activatedHubKeys);

                int centerX = panel.X + (int)System.MathF.Round(((hub.TilePosition.X + 0.5f - sourceRect.X) / sourceRect.Width) * panel.Width);
                int centerY = panel.Y + (int)System.MathF.Round(((hub.TilePosition.Y + 0.5f - sourceRect.Y) / sourceRect.Height) * panel.Height);
                int markerSize = System.Math.Max(6, System.Math.Max(minPixelWidth, minPixelHeight) * 4);
                Rectangle markerRect = new Rectangle(
                    centerX - (markerSize / 2),
                    centerY - (markerSize / 2),
                    markerSize,
                    markerSize);

                DrawRectOutline(spriteBatch, markerRect, 1, hubColor);
                DrawRect(spriteBatch, new Rectangle(centerX - 1, markerRect.Y + 1, 3, markerRect.Height - 2), hubColor);
                DrawRect(spriteBatch, new Rectangle(markerRect.X + 1, centerY - 1, markerRect.Width - 2, 3), hubColor);
            }
        }

        private void DrawTissueLinks(SpriteBatch spriteBatch, WorldMap worldMap, TissueAnalysisResult analysis, Rectangle panel, Rectangle sourceRect)
        {
            for (int linkIndex = 0; linkIndex < analysis.Links.Count; linkIndex++)
            {
                TissueLink link = analysis.Links[linkIndex];
                if (link.TilePath == null || link.TilePath.Count < 2)
                    continue;

                if (!TryGetLinkStyle(link, out Color color, out float thickness))
                    continue;

                for (int pointIndex = 0; pointIndex < link.TilePath.Count - 1; pointIndex++)
                {
                    Point startTile = link.TilePath[pointIndex];
                    Point endTile = link.TilePath[pointIndex + 1];

                    if (!IsTileInsideSourceRect(startTile, sourceRect) && !IsTileInsideSourceRect(endTile, sourceRect))
                        continue;

                    Vector2 start = MapWorldToMinimap(worldMap, worldMap.GetTileCenter(startTile.X, startTile.Y), panel, sourceRect);
                    Vector2 end = MapWorldToMinimap(worldMap, worldMap.GetTileCenter(endTile.X, endTile.Y), panel, sourceRect);
                    DrawLine(spriteBatch, start, end, color * 0.8f, thickness);
                }
            }
        }

        private Color GetHubColor(WorldMap worldMap, TissueHub hub, IReadOnlySet<int> activatedHubKeys)
        {
            if (IsHubActivated(worldMap, hub, activatedHubKeys))
                return new Color(255, 205, 58);

            if (hub.IsIsolated)
                return new Color(118, 92, 255);

            if (hub.IsTerminal)
                return new Color(255, 145, 46);

            return new Color(80, 255, 110);
        }

        private bool TryGetActivatedHubAtPoint(WorldMap worldMap, MinimapLayout layout, Point mousePoint, IReadOnlySet<int> activatedHubKeys, out int hubIndex)
        {
            hubIndex = -1;

            if (activatedHubKeys == null || activatedHubKeys.Count == 0)
                return false;

            TissueAnalysisResult analysis = worldMap.GetOrCreateTissueAnalysis();
            if (analysis == null)
                return false;

            int minPixelWidth = System.Math.Max(1, (int)System.MathF.Ceiling(layout.Panel.Width / (float)layout.SourceRect.Width));
            int minPixelHeight = System.Math.Max(1, (int)System.MathF.Ceiling(layout.Panel.Height / (float)layout.SourceRect.Height));
            int markerSize = System.Math.Max(6, System.Math.Max(minPixelWidth, minPixelHeight) * 4);

            for (int i = analysis.Hubs.Count - 1; i >= 0; i--)
            {
                TissueHub hub = analysis.Hubs[i];
                if (!IsHubActivated(worldMap, hub, activatedHubKeys))
                    continue;

                if (hub.TilePosition.X < layout.SourceRect.Left || hub.TilePosition.X >= layout.SourceRect.Right ||
                    hub.TilePosition.Y < layout.SourceRect.Top || hub.TilePosition.Y >= layout.SourceRect.Bottom)
                {
                    continue;
                }

                int centerX = layout.Panel.X + (int)System.MathF.Round(((hub.TilePosition.X + 0.5f - layout.SourceRect.X) / layout.SourceRect.Width) * layout.Panel.Width);
                int centerY = layout.Panel.Y + (int)System.MathF.Round(((hub.TilePosition.Y + 0.5f - layout.SourceRect.Y) / layout.SourceRect.Height) * layout.Panel.Height);
                Rectangle markerRect = new Rectangle(
                    centerX - (markerSize / 2),
                    centerY - (markerSize / 2),
                    markerSize,
                    markerSize);

                if (!markerRect.Contains(mousePoint))
                    continue;

                hubIndex = i;
                return true;
            }

            return false;
        }

        private static bool IsHubActivated(WorldMap worldMap, TissueHub hub, IReadOnlySet<int> activatedHubKeys)
        {
            return activatedHubKeys != null &&
                   activatedHubKeys.Contains(CreateHubKey(worldMap, hub.TilePosition));
        }

        private static int CreateHubKey(WorldMap worldMap, Point tilePosition)
        {
            int wrappedX = worldMap.WrapTileX(tilePosition.X);
            return (tilePosition.Y * worldMap.Width) + wrappedX;
        }

        private bool TryGetLinkStyle(TissueLink link, out Color color, out float thickness)
        {
            switch (link.LinkType)
            {
                case TissueLink.TissueLinkType.Primary:
                    color = new Color(255, 40, 40);
                    thickness = 2.5f;
                    return true;

                case TissueLink.TissueLinkType.Secondary:
                    color = new Color(255, 215, 64);
                    thickness = 1.5f;
                    return true;

                case TissueLink.TissueLinkType.Weak:
                    color = new Color(255, 105, 180);
                    thickness = 1f;
                    return true;

                default:
                    color = Color.Transparent;
                    thickness = 0f;
                    return false;
            }
        }

        private bool IsTileInsideSourceRect(Point tile, Rectangle sourceRect)
        {
            return tile.X >= sourceRect.Left &&
                   tile.X < sourceRect.Right &&
                   tile.Y >= sourceRect.Top &&
                   tile.Y < sourceRect.Bottom;
        }

        private void AdjustZoomAtPoint(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight, MinimapLayout layout, Vector2 mouseScreenPosition, int mouseWheelDelta, bool pointerOverMap)
        {
            float normalizedX = 0.5f;
            float normalizedY = 0.5f;
            float anchorTileX = GetPlayerTileX(worldMap, playerPosition);
            float anchorTileY = GetPlayerTileY(worldMap, playerPosition);

            if (pointerOverMap)
            {
                normalizedX = System.Math.Clamp((mouseScreenPosition.X - layout.Panel.X) / layout.Panel.Width, 0f, 1f);
                normalizedY = System.Math.Clamp((mouseScreenPosition.Y - layout.Panel.Y) / layout.Panel.Height, 0f, 1f);
                anchorTileX = layout.SourceRect.X + (normalizedX * layout.SourceRect.Width);
                anchorTileY = layout.SourceRect.Y + (normalizedY * layout.SourceRect.Height);
            }

            int steps = System.Math.Abs(mouseWheelDelta / 120);
            if (steps == 0)
                steps = 1;

            float factor = System.MathF.Pow(ZoomStep, steps);
            zoom = mouseWheelDelta > 0 ? zoom * factor : zoom / factor;
            zoom = System.Math.Clamp(zoom, MinZoom, MaxZoom);

            if (zoom <= MinZoom)
            {
                viewCenterTile = null;
                isDragging = false;
                return;
            }

            int newSourceWidth = System.Math.Max(1, (int)System.MathF.Round(worldMap.Width / zoom));
            int newSourceHeight = System.Math.Max(1, (int)System.MathF.Round(worldMap.Height / zoom));
            float sourceX = anchorTileX - (normalizedX * newSourceWidth);
            float sourceY = anchorTileY - (normalizedY * newSourceHeight);
            float centerX = sourceX + (newSourceWidth * 0.5f);
            float centerY = sourceY + (newSourceHeight * 0.5f);
            viewCenterTile = ClampViewCenter(worldMap, new Vector2(centerX, centerY), newSourceWidth, newSourceHeight);
        }

        private void EnsureMinimapTexture(WorldMap worldMap)
        {
            int targetWidth = worldMap.Width;
            int targetHeight = worldMap.Height;

            bool needsResize = minimapTexture == null ||
                               cachedSourceWidth != targetWidth ||
                               cachedSourceHeight != targetHeight;

            if (needsResize)
            {
                minimapTexture?.Dispose();
                minimapTexture = new Texture2D(graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.Color);
                minimapPixels = new Color[targetWidth * targetHeight];
                cachedSourceWidth = targetWidth;
                cachedSourceHeight = targetHeight;
                cachedTileRevision = -1;
            }

            if (cachedTileRevision == worldMap.TileRevision)
                return;

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                    minimapPixels[(y * targetWidth) + x] = GetTileColor(worldMap.GetTile(x, y));
            }

            DrawTreesOnMinimapTexture(worldMap, targetWidth, targetHeight);

            minimapTexture.SetData(minimapPixels);
            cachedTileRevision = worldMap.TileRevision;
        }

        private void DrawTreesOnMinimapTexture(WorldMap worldMap, int targetWidth, int targetHeight)
        {
            for (int treeIndex = 0; treeIndex < worldMap.Trees.Count; treeIndex++)
            {
                TreeInstance tree = worldMap.Trees[treeIndex];

                if (tree.HasCanopy)
                    DrawTreeCanopyOnMinimap(worldMap, tree, targetWidth, targetHeight);

                for (int partIndex = 0; partIndex < tree.Parts.Count; partIndex++)
                {
                    TreePartPlacement part = tree.Parts[partIndex];
                    int x = worldMap.WrapTileX(tree.BaseTile.X + part.OffsetTiles.X);
                    int y = tree.BaseTile.Y + part.OffsetTiles.Y;
                    Color color = GetTreePartMinimapColor(part.PartType);
                    SetMinimapPixel(x, y, color, targetWidth, targetHeight);
                }
            }
        }

        private void DrawTreeCanopyOnMinimap(WorldMap worldMap, TreeInstance tree, int targetWidth, int targetHeight)
        {
            const int canopyTileWidth = 6;
            const int canopyTileHeight = 6;

            for (int y = 0; y < canopyTileHeight; y++)
            {
                for (int x = 0; x < canopyTileWidth; x++)
                {
                    int tileX = worldMap.WrapTileX(tree.BaseTile.X + tree.Canopy.OffsetTiles.X + x);
                    int tileY = tree.BaseTile.Y + tree.Canopy.OffsetTiles.Y + y;
                    SetMinimapPixel(tileX, tileY, GetTreeCanopyMinimapColor(x, y), targetWidth, targetHeight);
                }
            }
        }

        private void SetMinimapPixel(int x, int y, Color color, int targetWidth, int targetHeight)
        {
            if (y < 0 || y >= targetHeight)
                return;

            minimapPixels[(y * targetWidth) + x] = color;
        }

        private MinimapLayout GetLayout(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight)
        {
            Rectangle sourceRect = GetSourceRect(worldMap, playerPosition);
            int maxWidth = screenWidth - 120;
            int maxHeight = screenHeight - 120;
            float scale = System.MathF.Min(maxWidth / (float)worldMap.Width, maxHeight / (float)worldMap.Height);
            scale = System.Math.Clamp(scale, 0.05f, 1f);
            int drawWidth = System.Math.Max(1, (int)System.MathF.Round(worldMap.Width * scale));
            int drawHeight = System.Math.Max(1, (int)System.MathF.Round(worldMap.Height * scale));
            Rectangle panel = new Rectangle(
                (screenWidth - drawWidth) / 2,
                (screenHeight - drawHeight) / 2,
                drawWidth,
                drawHeight);
            return new MinimapLayout(panel, sourceRect);
        }

        private Rectangle GetSourceRect(WorldMap worldMap, Vector2 playerPosition)
        {
            int sourceWidth = System.Math.Max(1, (int)System.MathF.Round(worldMap.Width / zoom));
            int sourceHeight = System.Math.Max(1, (int)System.MathF.Round(worldMap.Height / zoom));
            Vector2 centerTile = viewCenterTile ?? new Vector2(GetPlayerTileX(worldMap, playerPosition), GetPlayerTileY(worldMap, playerPosition));
            centerTile = ClampViewCenter(worldMap, centerTile, sourceWidth, sourceHeight);
            int sourceX = (int)System.MathF.Round(centerTile.X - (sourceWidth * 0.5f));
            int sourceY = (int)System.MathF.Round(centerTile.Y - (sourceHeight * 0.5f));
            sourceX = System.Math.Clamp(sourceX, 0, System.Math.Max(0, worldMap.Width - sourceWidth));
            sourceY = System.Math.Clamp(sourceY, 0, System.Math.Max(0, worldMap.Height - sourceHeight));
            return new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);
        }

        private void PanFromDrag(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight, Point mousePoint)
        {
            MinimapLayout layout = GetLayout(worldMap, playerPosition, screenWidth, screenHeight);
            Point delta = mousePoint - lastDragMousePosition;
            lastDragMousePosition = mousePoint;

            if (delta == Point.Zero)
                return;

            float tileDeltaX = delta.X * (layout.SourceRect.Width / (float)layout.Panel.Width);
            float tileDeltaY = delta.Y * (layout.SourceRect.Height / (float)layout.Panel.Height);
            Vector2 centerTile = viewCenterTile ?? new Vector2(GetPlayerTileX(worldMap, playerPosition), GetPlayerTileY(worldMap, playerPosition));
            centerTile.X -= tileDeltaX;
            centerTile.Y -= tileDeltaY;
            viewCenterTile = ClampViewCenter(worldMap, centerTile, layout.SourceRect.Width, layout.SourceRect.Height);
        }

        private Vector2 ClampViewCenter(WorldMap worldMap, Vector2 centerTile, int sourceWidth, int sourceHeight)
        {
            float halfWidth = sourceWidth * 0.5f;
            float halfHeight = sourceHeight * 0.5f;
            float minCenterX = halfWidth;
            float maxCenterX = worldMap.Width - halfWidth;
            float minCenterY = halfHeight;
            float maxCenterY = worldMap.Height - halfHeight;
            return new Vector2(
                System.Math.Clamp(centerTile.X, minCenterX, maxCenterX),
                System.Math.Clamp(centerTile.Y, minCenterY, maxCenterY));
        }

        private float GetPlayerTileX(WorldMap worldMap, Vector2 playerPosition)
        {
            return System.Math.Clamp(playerPosition.X / worldMap.TileSize, 0f, worldMap.Width - 1f);
        }

        private float GetPlayerTileY(WorldMap worldMap, Vector2 playerPosition)
        {
            return System.Math.Clamp(playerPosition.Y / worldMap.TileSize, 0f, worldMap.Height - 1f);
        }

        private Color GetTileColor(TileType tileType)
        {
            return tileType switch
            {
                TileType.Empty => new Color(8, 14, 18),
                TileType.Grass => new Color(46, 126, 74),
                TileType.Stone => new Color(142, 146, 152),
                TileType.Sand => new Color(252, 222, 156),
                _ => new Color(126, 92, 72)
            };
        }

        private static Color GetTreeCanopyMinimapColor(int x, int y)
        {
            return ((x + y) & 1) == 0
                ? new Color(70, 150, 35)
                : new Color(47, 111, 44);
        }

        private static Color GetTreePartMinimapColor(TreePartType partType)
        {
            return partType switch
            {
                TreePartType.RootLeft or
                TreePartType.RootRight or
                TreePartType.RootBothSocket => new Color(94, 62, 42),

                TreePartType.TrunkBaseCut or
                TreePartType.TrunkUpperCut => new Color(150, 94, 62),

                _ => new Color(122, 76, 50)
            };
        }

        private Rectangle GetCameraRect(WorldMap worldMap, Camera2D camera, Rectangle panel, Rectangle sourceRect)
        {
            float leftTile = camera.Position.X / worldMap.TileSize;
            float topTile = camera.Position.Y / worldMap.TileSize;
            float widthTiles = (graphicsDevice.PresentationParameters.BackBufferWidth / camera.Zoom) / worldMap.TileSize;
            float heightTiles = (graphicsDevice.PresentationParameters.BackBufferHeight / camera.Zoom) / worldMap.TileSize;

            int rectX = panel.X + (int)System.MathF.Round(((leftTile - sourceRect.X) / sourceRect.Width) * panel.Width);
            int rectY = panel.Y + (int)System.MathF.Round(((topTile - sourceRect.Y) / sourceRect.Height) * panel.Height);
            int rectW = System.Math.Max(2, (int)System.MathF.Round((widthTiles / sourceRect.Width) * panel.Width));
            int rectH = System.Math.Max(2, (int)System.MathF.Round((heightTiles / sourceRect.Height) * panel.Height));
            return new Rectangle(rectX, rectY, rectW, rectH);
        }

        private Point GetWorldPointOnMinimap(WorldMap worldMap, Vector2 worldPosition, Rectangle panel, Rectangle sourceRect)
        {
            float tileX = worldPosition.X / worldMap.TileSize;
            float tileY = worldPosition.Y / worldMap.TileSize;
            int drawX = panel.X + (int)System.MathF.Round(((tileX - sourceRect.X) / sourceRect.Width) * panel.Width);
            int drawY = panel.Y + (int)System.MathF.Round(((tileY - sourceRect.Y) / sourceRect.Height) * panel.Height);
            return new Point(drawX, drawY);
        }

        private Vector2 MapWorldToMinimap(WorldMap worldMap, Vector2 worldPosition, Rectangle panel, Rectangle sourceRect)
        {
            float tileX = worldPosition.X / worldMap.TileSize;
            float tileY = worldPosition.Y / worldMap.TileSize;
            float drawX = panel.X + (((tileX - sourceRect.X) / sourceRect.Width) * panel.Width);
            float drawY = panel.Y + (((tileY - sourceRect.Y) / sourceRect.Height) * panel.Height);
            return new Vector2(drawX, drawY);
        }

        private void DrawRect(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            spriteBatch.Draw(pixel, rect, color);
        }

        private void DrawModeButtons(SpriteBatch spriteBatch, Rectangle panel, bool tissueMode)
        {
            Rectangle tissueToggleButton = GetTissueToggleButton(panel);

            DrawRect(spriteBatch, tissueToggleButton, tissueMode ? new Color(210, 240, 255) : new Color(22, 34, 42, 220));
            DrawRectOutline(spriteBatch, tissueToggleButton, 1, new Color(160, 210, 225));
            DrawRect(spriteBatch, new Rectangle(tissueToggleButton.X + 5, tissueToggleButton.Y + 11, tissueToggleButton.Width - 10, 2), tissueMode ? new Color(12, 18, 24) : Color.White);
            DrawRect(spriteBatch, new Rectangle(tissueToggleButton.X + 8, tissueToggleButton.Y + 7, tissueToggleButton.Width - 16, 2), tissueMode ? new Color(12, 18, 24) : Color.White);
        }

        private Rectangle GetTissueToggleButton(Rectangle panel)
        {
            return new Rectangle(panel.Right - 28, panel.Bottom - 28, 22, 22);
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;

            float rotation = System.MathF.Atan2(delta.Y, delta.X);
            Rectangle destination = new Rectangle(
                (int)start.X,
                (int)(start.Y - (thickness * 0.5f)),
                System.Math.Max(1, (int)length),
                System.Math.Max(1, (int)thickness));

            spriteBatch.Draw(pixel, destination, null, color, rotation, Vector2.Zero, SpriteEffects.None, 0f);
        }

        private void DrawRectOutline(SpriteBatch spriteBatch, Rectangle rect, int thickness, Color color)
        {
            DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            DrawRect(spriteBatch, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            DrawRect(spriteBatch, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }
    }
}
