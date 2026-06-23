using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNetworkRenderer
    {
        private static readonly Color HaloViolet = new(100, 28, 190);
        private static readonly Color SheathMagenta = new(218, 58, 205);
        private static readonly Color CorePink = new(255, 132, 158);
        private static readonly Color CoreGold = new(255, 202, 92);
        private readonly Texture2D pixel;
        private readonly List<TissueBranch> visibleBranches = new();
        private readonly List<TissueNode> visibleNodes = new();

        public TissueNetworkRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void DrawHalo(SpriteBatch spriteBatch, TissueNetwork network, Rectangle visibleBounds)
        {
            if (network == null)
                return;

            Rectangle padded = visibleBounds;
            padded.Inflate(48, 48);
            network.CollectVisibleBranches(padded, visibleBranches);
            network.CollectVisibleNodes(padded, visibleNodes);

            for (int i = 0; i < visibleBranches.Count; i++)
            {
                TissueBranch branch = visibleBranches[i];
                float kindScale = branch.Kind == TissueBranch.TissueBranchKind.Micro ? 0.55f : 1f;
                DrawPath(spriteBatch, branch, HaloViolet * (0.055f + (branch.Intensity * 0.055f)), branch.Thickness * 8f * kindScale);
                DrawPath(spriteBatch, branch, SheathMagenta * (0.07f + (branch.Intensity * 0.07f)), branch.Thickness * 4f * kindScale);
            }

            for (int i = 0; i < visibleNodes.Count; i++)
            {
                TissueNode node = visibleNodes[i];
                if (!node.IsPrimary)
                    continue;
                float radius = 4f + (node.Degree * 1.15f) + (node.NestInfluence * 1.8f);
                DrawDisc(spriteBatch, node.Position, radius * 3.1f, HaloViolet * (0.08f + node.Strength * 0.08f));
                DrawDisc(spriteBatch, node.Position, radius * 1.8f, SheathMagenta * (0.12f + node.Strength * 0.10f));
            }
        }

        public void DrawCore(SpriteBatch spriteBatch, TissueNetwork network, Rectangle visibleBounds)
        {
            if (network == null)
                return;

            Rectangle padded = visibleBounds;
            padded.Inflate(24, 24);
            network.CollectVisibleBranches(padded, visibleBranches);
            network.CollectVisibleNodes(padded, visibleNodes);

            for (int i = 0; i < visibleBranches.Count; i++)
            {
                TissueBranch branch = visibleBranches[i];
                if (branch.Kind == TissueBranch.TissueBranchKind.Micro)
                {
                    DrawPath(spriteBatch, branch, SheathMagenta * 0.22f, MathF.Max(0.45f, branch.Thickness));
                    continue;
                }

                DrawPath(spriteBatch, branch, SheathMagenta * (0.30f + branch.Intensity * 0.22f), branch.Thickness * 1.9f);
                Color core = Color.Lerp(CorePink, CoreGold, MathHelper.Clamp((branch.Intensity - 0.68f) * 2.2f, 0f, 1f));
                DrawPath(spriteBatch, branch, core * (0.58f + branch.Intensity * 0.32f), MathF.Max(0.65f, branch.Thickness * 0.62f));
            }

            for (int i = 0; i < visibleNodes.Count; i++)
            {
                TissueNode node = visibleNodes[i];
                if (!node.IsPrimary)
                    continue;
                float radius = 3f + (node.Degree * 0.72f) + node.NestInfluence;
                DrawDisc(spriteBatch, node.Position, radius * 1.35f, SheathMagenta * 0.48f);
                DrawDisc(spriteBatch, node.Position, radius * 0.72f, CorePink * 0.86f);
                DrawDisc(spriteBatch, node.Position, MathF.Max(1.2f, radius * 0.28f), CoreGold);
            }
        }

        private void DrawPath(SpriteBatch spriteBatch, TissueBranch branch, Color color, float baseThickness)
        {
            if (branch.Points == null || branch.Points.Count < 2 || color.A == 0)
                return;

            int segmentCount = branch.Points.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                float t = segmentCount <= 1 ? 0.5f : i / (float)(segmentCount - 1);
                float organicVariation = 0.88f + (MathF.Sin((t * MathF.PI * 4f) + branch.Id * 0.37f) * 0.12f);
                float endpointBias = 1f + ((1f - MathF.Sin(t * MathF.PI)) * (branch.IsPrimary ? 0.24f : 0.08f));
                DrawLine(spriteBatch, branch.Points[i], branch.Points[i + 1], color, baseThickness * organicVariation * endpointBias);
            }
        }

        private void DrawDisc(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            if (radius <= 0.5f || color.A == 0)
                return;
            float radiusSq = radius * radius;
            int minY = (int)MathF.Floor(center.Y - radius);
            int maxY = (int)MathF.Ceiling(center.Y + radius);
            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y + 0.5f) - center.Y;
                float remaining = radiusSq - (dy * dy);
                if (remaining <= 0f)
                    continue;
                float halfWidth = MathF.Sqrt(remaining);
                spriteBatch.Draw(pixel, new Rectangle((int)MathF.Floor(center.X - halfWidth), y, Math.Max(1, (int)MathF.Ceiling(halfWidth * 2f)), 1), color);
            }
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;
            spriteBatch.Draw(
                pixel,
                start,
                null,
                color,
                MathF.Atan2(delta.Y, delta.X),
                new Vector2(0f, 0.5f),
                new Vector2(length, MathF.Max(0.5f, thickness)),
                SpriteEffects.None,
                0f);
        }
    }
}
