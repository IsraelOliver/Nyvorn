using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueFieldDebugRenderer
    {
        private readonly Texture2D pixel;

        public TissueFieldDebugRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, WorldMap worldMap, float revealStrength, Vector2 focusPosition, float revealRadius)
        {
            if (worldMap?.TissueField == null || revealStrength <= 0.001f)
                return;

            TissueAnalysisResult analysis = GetAnalysis(worldMap);
            float revealRadiusSq = revealRadius * revealRadius;
            DrawLinkPaths(spriteBatch, worldMap, analysis, focusPosition, revealRadiusSq, revealStrength);
            DrawHubMarkers(spriteBatch, worldMap, analysis, focusPosition, revealRadiusSq, revealStrength);
        }

        private TissueAnalysisResult GetAnalysis(WorldMap worldMap)
        {
            return worldMap.GetOrCreateTissueAnalysis();
        }

        private void DrawHubMarkers(SpriteBatch spriteBatch, WorldMap worldMap, TissueAnalysisResult analysis, Vector2 focusPosition, float revealRadiusSq, float revealStrength)
        {
            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                float distanceSq = Vector2.DistanceSquared(hub.WorldPosition, focusPosition);
                if (distanceSq > revealRadiusSq)
                    continue;

                float normalized = revealRadiusSq <= 0f ? 1f : MathHelper.Clamp(distanceSq / revealRadiusSq, 0f, 1f);
                float falloff = 1f - (normalized * normalized);
                Color hubColor = GetHubColor(hub) * (revealStrength * MathHelper.Lerp(0.35f, 1f, falloff));

                int markerSize = System.Math.Max(10, (int)System.MathF.Round(worldMap.TileSize * 1.6f));
                Rectangle markerRect = new Rectangle(
                    (int)System.MathF.Round(hub.WorldPosition.X - (markerSize * 0.5f)),
                    (int)System.MathF.Round(hub.WorldPosition.Y - (markerSize * 0.5f)),
                    markerSize,
                    markerSize);

                DrawRectOutline(spriteBatch, markerRect, 2, hubColor);
                DrawCross(spriteBatch, markerRect, hubColor);
            }
        }

        private void DrawLinkPaths(SpriteBatch spriteBatch, WorldMap worldMap, TissueAnalysisResult analysis, Vector2 focusPosition, float revealRadiusSq, float revealStrength)
        {
            for (int linkIndex = 0; linkIndex < analysis.Links.Count; linkIndex++)
            {
                TissueLink link = analysis.Links[linkIndex];
                if (link.TilePath == null || link.TilePath.Count < 2)
                    continue;

                if (!TryGetLinkStyle(link, out Color baseColor, out float baseThickness))
                    continue;

                for (int pointIndex = 0; pointIndex < link.TilePath.Count - 1; pointIndex++)
                {
                    Point startTile = link.TilePath[pointIndex];
                    Point endTile = link.TilePath[pointIndex + 1];
                    Vector2 start = worldMap.GetTileCenter(startTile.X, startTile.Y);
                    Vector2 end = worldMap.GetTileCenter(endTile.X, endTile.Y);
                    Vector2 midpoint = (start + end) * 0.5f;
                    float distanceSq = Vector2.DistanceSquared(midpoint, focusPosition);
                    if (distanceSq > revealRadiusSq)
                        continue;

                    float normalized = revealRadiusSq <= 0f ? 1f : MathHelper.Clamp(distanceSq / revealRadiusSq, 0f, 1f);
                    float falloff = 1f - (normalized * normalized);
                    Color linkColor = baseColor;

                    linkColor *= revealStrength * MathHelper.Lerp(0.18f, 0.66f, falloff);
                    DrawLine(spriteBatch, start, end, linkColor, baseThickness);
                }
            }
        }

        private Color GetHubColor(TissueHub hub)
        {
            if (hub.IsIsolated)
                return new Color(118, 92, 255);

            if (hub.IsTerminal)
                return new Color(255, 145, 46);

            return new Color(80, 255, 110);
        }

        private bool TryGetLinkStyle(TissueLink link, out Color color, out float thickness)
        {
            switch (link.LinkType)
            {
                case TissueLink.TissueLinkType.Primary:
                    color = new Color(255, 40, 40);
                    thickness = 4f;
                    return true;

                case TissueLink.TissueLinkType.Secondary:
                    color = new Color(255, 215, 64);
                    thickness = 2.5f;
                    return true;

                case TissueLink.TissueLinkType.Weak:
                    color = new Color(255, 105, 180);
                    thickness = 1.5f;
                    return true;

                default:
                    color = Color.Transparent;
                    thickness = 0f;
                    return false;
            }
        }

        private void DrawCross(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            int centerX = rect.X + (rect.Width / 2);
            int centerY = rect.Y + (rect.Height / 2);
            spriteBatch.Draw(pixel, new Rectangle(centerX - 1, rect.Y + 2, 3, rect.Height - 4), color);
            spriteBatch.Draw(pixel, new Rectangle(rect.X + 2, centerY - 1, rect.Width - 4, 3), color);
        }

        private void DrawRectOutline(SpriteBatch spriteBatch, Rectangle rect, int thickness, Color color)
        {
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
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
    }
}
