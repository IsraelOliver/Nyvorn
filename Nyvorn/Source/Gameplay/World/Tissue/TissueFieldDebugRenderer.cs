using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using System.Collections.Generic;

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
                float baseRadius = System.MathF.Max(worldMap.TileSize * 0.48f, 4f);

                DrawDisc(spriteBatch, hub.WorldPosition, baseRadius * 1.9f, hubColor * 0.16f);
                DrawDisc(spriteBatch, hub.WorldPosition, baseRadius * 1.15f, hubColor * 0.36f);
                DrawDisc(spriteBatch, hub.WorldPosition, baseRadius * 0.62f, Color.Lerp(hubColor, Color.White, 0.18f));
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

                List<Vector2> organicPath = BuildOrganicPath(worldMap, link);
                if (organicPath.Count < 2)
                    continue;

                int segmentCount = organicPath.Count - 1;
                for (int pointIndex = 0; pointIndex < segmentCount; pointIndex++)
                {
                    Vector2 start = organicPath[pointIndex];
                    Vector2 end = organicPath[pointIndex + 1];
                    Vector2 midpoint = (start + end) * 0.5f;
                    float distanceSq = Vector2.DistanceSquared(midpoint, focusPosition);
                    if (distanceSq > revealRadiusSq)
                        continue;

                    float normalized = revealRadiusSq <= 0f ? 1f : MathHelper.Clamp(distanceSq / revealRadiusSq, 0f, 1f);
                    float falloff = 1f - (normalized * normalized);
                    float progress = segmentCount <= 1 ? 0.5f : pointIndex / (float)(segmentCount - 1);
                    float thicknessScale = GetNerveThicknessScale(progress, link.LinkType);
                    float alpha = revealStrength * MathHelper.Lerp(0.18f, 0.66f, falloff);

                    Color sheathColor = baseColor * (alpha * 0.42f);
                    Color coreColor = Color.Lerp(baseColor, Color.White, 0.14f) * alpha;

                    DrawLine(spriteBatch, start, end, sheathColor, baseThickness * thicknessScale * 2.15f);
                    DrawLine(spriteBatch, start, end, coreColor, baseThickness * thicknessScale);
                }
            }
        }

        private List<Vector2> BuildOrganicPath(WorldMap worldMap, TissueLink link)
        {
            List<Vector2> controlPoints = ExtractControlPoints(worldMap, link.TilePath);
            if (controlPoints.Count < 2)
                return controlPoints;

            ApplyOrganicOffsets(controlPoints, worldMap.TileSize, link);
            return SampleSpline(controlPoints, worldMap.TileSize);
        }

        private List<Vector2> ExtractControlPoints(WorldMap worldMap, IReadOnlyList<Point> tilePath)
        {
            List<Vector2> controlPoints = new(tilePath.Count);
            Point firstTile = tilePath[0];
            controlPoints.Add(worldMap.GetTileCenter(firstTile.X, firstTile.Y));

            Point lastAcceptedTile = firstTile;

            for (int i = 1; i < tilePath.Count - 1; i++)
            {
                Point previous = tilePath[i - 1];
                Point current = tilePath[i];
                Point next = tilePath[i + 1];

                Point inDirection = new(current.X - previous.X, current.Y - previous.Y);
                Point outDirection = new(next.X - current.X, next.Y - current.Y);

                bool isTurn = inDirection != outDirection;
                int dx = current.X - lastAcceptedTile.X;
                int dy = current.Y - lastAcceptedTile.Y;
                bool spacedEnough = (dx * dx) + (dy * dy) >= 9;

                if (!isTurn && !spacedEnough)
                    continue;

                controlPoints.Add(worldMap.GetTileCenter(current.X, current.Y));
                lastAcceptedTile = current;
            }

            Point lastTile = tilePath[^1];
            controlPoints.Add(worldMap.GetTileCenter(lastTile.X, lastTile.Y));

            if (controlPoints.Count == 2)
            {
                Vector2 start = controlPoints[0];
                Vector2 end = controlPoints[1];
                Vector2 delta = end - start;
                float distance = delta.Length();
                if (distance > 0.001f)
                {
                    Vector2 direction = delta / distance;
                    Vector2 normal = new(-direction.Y, direction.X);
                    float bendSeed = ((firstTile.X * 13f) + (lastTile.Y * 17f) + (lastTile.X * 7f)) * 0.17f;
                    float bend = System.MathF.Sin(bendSeed) * System.MathF.Min(worldMap.TileSize * 1.35f, distance * 0.16f);
                    controlPoints.Insert(1, ((start + end) * 0.5f) + (normal * bend));
                }
            }

            return controlPoints;
        }

        private void ApplyOrganicOffsets(List<Vector2> controlPoints, int tileSize, TissueLink link)
        {
            if (controlPoints.Count <= 2)
                return;

            for (int i = 1; i < controlPoints.Count - 1; i++)
            {
                Vector2 previous = controlPoints[i - 1];
                Vector2 current = controlPoints[i];
                Vector2 next = controlPoints[i + 1];
                Vector2 tangent = next - previous;
                if (tangent.LengthSquared() <= 0.001f)
                    continue;

                tangent.Normalize();
                Vector2 normal = new(-tangent.Y, tangent.X);
                float t = i / (float)(controlPoints.Count - 1);
                float envelope = System.MathF.Sin(t * System.MathF.PI);
                float seed = (link.StartHubIndex * 0.91f) + (link.EndHubIndex * 1.37f) + (i * 0.73f);
                float sway = System.MathF.Sin(seed) * tileSize * 0.42f;
                float drift = System.MathF.Cos(seed * 1.31f) * tileSize * 0.16f;

                controlPoints[i] = current +
                    (normal * sway * envelope) +
                    (tangent * drift * envelope);
            }
        }

        private List<Vector2> SampleSpline(List<Vector2> controlPoints, int tileSize)
        {
            if (controlPoints.Count < 2)
                return controlPoints;

            List<Vector2> sampled = new(controlPoints.Count * 4)
            {
                controlPoints[0]
            };

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                Vector2 p0 = i > 0 ? controlPoints[i - 1] : controlPoints[i];
                Vector2 p1 = controlPoints[i];
                Vector2 p2 = controlPoints[i + 1];
                Vector2 p3 = i + 2 < controlPoints.Count ? controlPoints[i + 2] : controlPoints[i + 1];

                float segmentLength = Vector2.Distance(p1, p2);
                int steps = System.Math.Max(4, (int)System.MathF.Ceiling(segmentLength / System.Math.Max(3f, tileSize * 0.42f)));

                for (int step = 1; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    Vector2 point = Vector2.CatmullRom(p0, p1, p2, p3, t);
                    if (Vector2.DistanceSquared(sampled[^1], point) < 1f)
                        continue;

                    sampled.Add(point);
                }
            }

            return sampled;
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

        private float GetNerveThicknessScale(float progress, TissueLink.TissueLinkType linkType)
        {
            float trunkBulge = 0.88f + (System.MathF.Sin(progress * System.MathF.PI) * 0.28f);

            return linkType switch
            {
                TissueLink.TissueLinkType.Primary => trunkBulge,
                TissueLink.TissueLinkType.Secondary => trunkBulge * 0.92f,
                _ => trunkBulge * 0.84f
            };
        }

        private void DrawDisc(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            if (radius <= 0.5f || color.A <= 0)
                return;

            float radiusSq = radius * radius;
            int minY = (int)System.MathF.Floor(center.Y - radius);
            int maxY = (int)System.MathF.Ceiling(center.Y + radius);

            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y + 0.5f) - center.Y;
                float remaining = radiusSq - (dy * dy);
                if (remaining <= 0f)
                    continue;

                float halfWidth = System.MathF.Sqrt(remaining);
                int x = (int)System.MathF.Floor(center.X - halfWidth);
                int width = System.Math.Max(1, (int)System.MathF.Ceiling(halfWidth * 2f));
                spriteBatch.Draw(pixel, new Rectangle(x, y, width, 1), color);
            }
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;

            float rotation = System.MathF.Atan2(delta.Y, delta.X);
            spriteBatch.Draw(
                pixel,
                start,
                null,
                color,
                rotation,
                new Vector2(0f, 0.5f),
                new Vector2(length, System.MathF.Max(1f, thickness)),
                SpriteEffects.None,
                0f);
        }
    }
}
