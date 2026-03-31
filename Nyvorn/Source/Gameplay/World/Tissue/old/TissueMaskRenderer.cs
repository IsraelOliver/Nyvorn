using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueMaskRenderer
    {
        private readonly Texture2D circle;
        private readonly Effect compositeEffect;
        private RenderTarget2D renderTarget;

        public TissueMaskRenderer(GraphicsDevice graphicsDevice, Effect compositeEffect)
        {
            circle = CreateCircleTexture(graphicsDevice, 64);
            this.compositeEffect = compositeEffect;
        }

        public Effect CompositeEffect => compositeEffect;
        public Texture2D MaskTexture => renderTarget;

        public void EnsureTarget(GraphicsDevice graphicsDevice, int width, int height)
        {
            if (renderTarget != null &&
                renderTarget.Width == width &&
                renderTarget.Height == height)
                return;

            renderTarget?.Dispose();
            renderTarget = new RenderTarget2D(
                graphicsDevice,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        public void DrawMask(SpriteBatch spriteBatch, TissueNetwork tissueNetwork, float revealStrength, Vector2 focusPosition, float revealRadius)
        {
            DrawMask(spriteBatch, tissueNetwork, revealStrength, focusPosition, revealRadius, null);
        }

        public void DrawMask(SpriteBatch spriteBatch, WorldMap worldMap, TissueField tissueField, float revealStrength, Vector2 focusPosition, float revealRadius)
        {
            DrawMask(spriteBatch, worldMap, tissueField, revealStrength, focusPosition, revealRadius, null);
        }

        public void DrawMask(
            SpriteBatch spriteBatch,
            WorldMap worldMap,
            TissueField tissueField,
            float revealStrength,
            Vector2 focusPosition,
            float revealRadius,
            Rectangle? worldCullBounds)
        {
            if (worldMap == null || tissueField == null || revealStrength <= 0.001f)
                return;

            TissueAnalysisResult analysis = worldMap.GetOrCreateTissueAnalysis();
            if (analysis == null)
                return;

            Rectangle cull = worldCullBounds ?? new Rectangle(0, 0, worldMap.PixelWidth, worldMap.Height * worldMap.TileSize);

            DrawAnalyzedLinks(spriteBatch, worldMap, analysis, focusPosition, revealRadius, revealStrength, cull);
            DrawAnalyzedHubs(spriteBatch, analysis, focusPosition, revealRadius, revealStrength, cull);
        }

        private void DrawAnalyzedLinks(
            SpriteBatch spriteBatch,
            WorldMap worldMap,
            TissueAnalysisResult analysis,
            Vector2 focusPosition,
            float revealRadius,
            float revealStrength,
            Rectangle worldCullBounds)
        {
            for (int linkIndex = 0; linkIndex < analysis.Links.Count; linkIndex++)
            {
                TissueLink link = analysis.Links[linkIndex];
                if (link.TilePath == null || link.TilePath.Count < 2)
                    continue;

                float baseThickness = GetAnalyzedLinkThickness(link);
                if (baseThickness <= 0f)
                    continue;

                for (int pointIndex = 0; pointIndex < link.TilePath.Count - 1; pointIndex++)
                {
                    Point startTile = link.TilePath[pointIndex];
                    Point endTile = link.TilePath[pointIndex + 1];
                    Vector2 start = worldMap.GetTileCenter(startTile.X, startTile.Y);
                    Vector2 end = worldMap.GetTileCenter(endTile.X, endTile.Y);

                    if (!SegmentTouchesBounds(start, end, worldCullBounds))
                        continue;

                    Vector2 midpoint = (start + end) * 0.5f;
                    float focusFalloff = GetFocusFalloff(midpoint, focusPosition, revealRadius);
                    float alphaScale = revealStrength * focusFalloff;
                    if (alphaScale <= 0.01f)
                        continue;

                    float auraThickness = baseThickness * 1.9f;
                    Color auraColor = Color.White * (0.10f * alphaScale);
                    Color maskColor = Color.White * (0.34f * alphaScale);

                    DrawLine(spriteBatch, start, end, auraColor, auraThickness);
                    DrawLine(spriteBatch, start, end, maskColor, baseThickness);
                }
            }
        }

        private void DrawAnalyzedHubs(
            SpriteBatch spriteBatch,
            TissueAnalysisResult analysis,
            Vector2 focusPosition,
            float revealRadius,
            float revealStrength,
            Rectangle worldCullBounds)
        {
            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                if (!Contains(worldCullBounds, hub.WorldPosition))
                    continue;

                float focusFalloff = GetFocusFalloff(hub.WorldPosition, focusPosition, revealRadius);
                float alphaScale = revealStrength * focusFalloff;
                if (alphaScale <= 0.01f)
                    continue;

                float size = GetHubCoreSize(hub);
                float outerHaloSize = size * 3.0f;
                float midHaloSize = size * 1.9f;

                DrawPoint(spriteBatch, hub.WorldPosition, outerHaloSize, Color.White * (0.12f * alphaScale));
                DrawPoint(spriteBatch, hub.WorldPosition, midHaloSize, Color.White * (0.24f * alphaScale));
                DrawPoint(spriteBatch, hub.WorldPosition, size, Color.White * (0.62f * alphaScale));
            }
        }

        public void DrawMask(
            SpriteBatch spriteBatch,
            TissueNetwork tissueNetwork,
            float revealStrength,
            Vector2 focusPosition,
            float revealRadius,
            Rectangle? worldCullBounds)
        {
            if (tissueNetwork == null || revealStrength <= 0.001f)
                return;

            foreach (TissueBranch branch in tissueNetwork.Branches)
            {
                if (worldCullBounds.HasValue && !worldCullBounds.Value.Intersects(branch.Bounds))
                    continue;

                Vector2 midpoint = branch.Points[branch.Points.Count / 2];
                float focusFalloff = GetFocusFalloff(midpoint, focusPosition, revealRadius);
                float alphaScale = revealStrength * focusFalloff;
                if (alphaScale <= 0.01f)
                    continue;

                float thickness = branch.IsPrimary
                    ? 2.5f + branch.Thickness
                    : 1.25f + branch.Thickness;

                float auraThickness = branch.IsPrimary
                    ? thickness * 1.85f
                    : thickness * 1.45f;

                Color auraColor = Color.White * (branch.IsPrimary
                    ? 0.18f * alphaScale
                    : 0.10f * alphaScale);

                Color maskColor = Color.White * (branch.IsPrimary
                    ? 0.65f * alphaScale
                    : 0.42f * alphaScale);

                for (int i = 0; i < branch.Points.Count - 1; i++)
                {
                    DrawLine(spriteBatch, branch.Points[i], branch.Points[i + 1], auraColor, auraThickness);
                    DrawLine(spriteBatch, branch.Points[i], branch.Points[i + 1], maskColor, thickness);
                }
            }

            foreach (TissueNode node in tissueNetwork.Nodes)
            {
                if (worldCullBounds.HasValue && !Contains(worldCullBounds.Value, node.Position))
                    continue;

                float focusFalloff = GetFocusFalloff(node.Position, focusPosition, revealRadius);
                float alphaScale = revealStrength * focusFalloff;
                if (alphaScale <= 0.01f)
                    continue;

                float size = node.IsPrimary ? 7f : 4f;
                float haloSize = node.IsPrimary ? size * 3.5f : size * 2.8f;
                float midHaloSize = node.IsPrimary ? size * 2.35f : size * 1.9f;
                float coreSize = node.IsPrimary ? size * 0.62f : size * 0.58f;
                Color outerHaloColor = Color.White * (node.IsPrimary
                    ? 0.20f * alphaScale
                    : 0.12f * alphaScale);
                Color midHaloColor = Color.White * (node.IsPrimary
                    ? 0.36f * alphaScale
                    : 0.22f * alphaScale);
                Color maskColor = Color.White * (node.IsPrimary
                    ? 0.95f * alphaScale
                    : 0.68f * alphaScale);
                Color coreColor = Color.White * (node.IsPrimary
                    ? 1.00f * alphaScale
                    : 0.82f * alphaScale);

                DrawPoint(spriteBatch, node.Position, haloSize, outerHaloColor);
                DrawPoint(spriteBatch, node.Position, midHaloSize, midHaloColor);
                DrawPoint(spriteBatch, node.Position, size, maskColor);
                DrawPoint(spriteBatch, node.Position, coreSize, coreColor);
            }
        }

        public void DrawComposite(
            SpriteBatch spriteBatch,
            float revealStrength,
            float timeSeconds,
            Vector2 focusScreenPosition,
            float revealRadiusPixels,
            float waveProgress,
            float layerMode,
            float layerOpacity)
        {
            if (renderTarget == null || revealStrength <= 0.001f || compositeEffect == null)
                return;

            compositeEffect.Parameters["Time"]?.SetValue(timeSeconds);
            compositeEffect.Parameters["RevealStrength"]?.SetValue(revealStrength);
            compositeEffect.Parameters["ScreenSize"]?.SetValue(new Vector2(renderTarget.Width, renderTarget.Height));
            compositeEffect.Parameters["FocusScreenPosition"]?.SetValue(focusScreenPosition);
            compositeEffect.Parameters["RevealRadiusPixels"]?.SetValue(revealRadiusPixels);
            compositeEffect.Parameters["WaveProgress"]?.SetValue(waveProgress);
            compositeEffect.Parameters["LayerMode"]?.SetValue(layerMode);
            compositeEffect.Parameters["LayerOpacity"]?.SetValue(layerOpacity);

            Rectangle destination = new Rectangle(0, 0, renderTarget.Width, renderTarget.Height);
            spriteBatch.Draw(renderTarget, destination, Color.White);
        }

        public void DrawRawOverlay(SpriteBatch spriteBatch, Color color)
        {
            if (renderTarget == null)
                return;

            Rectangle destination = new Rectangle(0, 0, renderTarget.Width, renderTarget.Height);
            spriteBatch.Draw(renderTarget, destination, color);
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;

            float spacing = System.MathF.Max(0.85f, thickness * 0.32f);
            int steps = System.Math.Max(1, (int)System.MathF.Ceiling(length / spacing));
            float diameter = System.MathF.Max(1.5f, thickness * 1.18f);

            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                Vector2 position = Vector2.Lerp(start, end, t);
                DrawDisc(spriteBatch, position, diameter, color);
            }
        }

        private void DrawPoint(SpriteBatch spriteBatch, Vector2 position, float size, Color color)
        {
            DrawDisc(spriteBatch, position, size, color);
        }

        private static float GetAnalyzedLinkThickness(TissueLink link)
        {
            switch (link.LinkType)
            {
                case TissueLink.TissueLinkType.Primary:
                    return 5.2f;

                case TissueLink.TissueLinkType.Secondary:
                    return 2.8f;

                default:
                    return 0f;
            }
        }

        private static float GetHubCoreSize(TissueHub hub)
        {
            if (hub.IsIsolated)
                return 8f;

            if (hub.IsTerminal)
                return 11f;

            return 15f;
        }

        private static bool Contains(Rectangle rectangle, Vector2 point)
        {
            return point.X >= rectangle.Left &&
                   point.X <= rectangle.Right &&
                   point.Y >= rectangle.Top &&
                   point.Y <= rectangle.Bottom;
        }

        private static bool SegmentTouchesBounds(Vector2 start, Vector2 end, Rectangle bounds)
        {
            return Contains(bounds, start) ||
                   Contains(bounds, end) ||
                   bounds.Contains(((start + end) * 0.5f).ToPoint());
        }

        private float GetFocusFalloff(Vector2 worldPosition, Vector2 focusPosition, float revealRadius)
        {
            if (revealRadius <= 0f)
                return 1f;

            float distance = Vector2.Distance(worldPosition, focusPosition);
            float normalized = MathHelper.Clamp(distance / revealRadius, 0f, 1f);
            float strongNear = 1f - (normalized * normalized);
            return MathHelper.Lerp(0.18f, 1f, strongNear);
        }

        private static Texture2D CreateCircleTexture(GraphicsDevice graphicsDevice, int diameter)
        {
            Texture2D texture = new Texture2D(graphicsDevice, diameter, diameter);
            Color[] data = new Color[diameter * diameter];
            float radius = diameter * 0.5f;
            Vector2 center = new Vector2(radius - 0.5f, radius - 0.5f);

            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float normalized = MathHelper.Clamp(1f - (distance / radius), 0f, 1f);
                    float alpha = normalized * normalized;
                    data[(y * diameter) + x] = Color.White * alpha;
                }
            }

            texture.SetData(data);
            return texture;
        }

        private void DrawDisc(SpriteBatch spriteBatch, Vector2 position, float diameter, Color color)
        {
            int roundedDiameter = System.Math.Max(1, (int)System.MathF.Ceiling(diameter));
            Rectangle rectangle = new Rectangle(
                (int)System.MathF.Round(position.X - (roundedDiameter * 0.5f)),
                (int)System.MathF.Round(position.Y - (roundedDiameter * 0.5f)),
                roundedDiameter,
                roundedDiameter);

            spriteBatch.Draw(circle, rectangle, color);
        }
    }
}
