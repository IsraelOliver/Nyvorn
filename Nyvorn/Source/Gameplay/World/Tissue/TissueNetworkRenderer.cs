using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNetworkRenderer
    {
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
            padded.Inflate(TissueConfig.WorldVisual.HaloCullingPadding, TissueConfig.WorldVisual.HaloCullingPadding);
            network.CollectVisibleBranches(padded, visibleBranches);
            network.CollectVisibleNodes(padded, visibleNodes);

            for (int i = 0; i < visibleBranches.Count; i++)
            {
                TissueBranch branch = visibleBranches[i];
                float kindScale = branch.Kind == TissueBranch.TissueBranchKind.Micro ? TissueConfig.WorldVisual.MicroHaloScale : 1f;
                DrawPath(
                    spriteBatch,
                    branch,
                    TissueConfig.WorldVisual.HaloViolet * (TissueConfig.WorldVisual.HaloAlphaBase + (branch.Intensity * TissueConfig.WorldVisual.HaloAlphaIntensity)),
                    branch.Thickness * TissueConfig.WorldVisual.HaloThickness * kindScale);
                DrawPath(
                    spriteBatch,
                    branch,
                    TissueConfig.WorldVisual.SheathMagenta * (TissueConfig.WorldVisual.SheathHaloAlphaBase + (branch.Intensity * TissueConfig.WorldVisual.SheathHaloAlphaIntensity)),
                    branch.Thickness * TissueConfig.WorldVisual.SheathHaloThickness * kindScale);
            }

            for (int i = 0; i < visibleNodes.Count; i++)
            {
                TissueNode node = visibleNodes[i];
                if (!node.IsPrimary)
                    continue;
                float depthScale = MathHelper.Lerp(TissueConfig.WorldVisual.NodeDepthScaleMin, TissueConfig.WorldVisual.NodeDepthScaleMax, node.Strength);
                float radius = (
                    TissueConfig.WorldVisual.HaloNodeRadiusBase +
                    (node.Degree * TissueConfig.WorldVisual.HaloNodeDegreeRadius) +
                    (node.NestInfluence * TissueConfig.WorldVisual.HaloNodeNestRadius)) * depthScale;
                DrawDisc(
                    spriteBatch,
                    node.Position,
                    radius * TissueConfig.WorldVisual.HaloNodeOuterScale,
                    TissueConfig.WorldVisual.HaloViolet * (TissueConfig.WorldVisual.HaloNodeOuterAlphaBase + node.Strength * TissueConfig.WorldVisual.HaloNodeOuterAlphaStrength));
                DrawDisc(
                    spriteBatch,
                    node.Position,
                    radius * TissueConfig.WorldVisual.HaloNodeInnerScale,
                    TissueConfig.WorldVisual.SheathMagenta * (TissueConfig.WorldVisual.HaloNodeInnerAlphaBase + node.Strength * TissueConfig.WorldVisual.HaloNodeInnerAlphaStrength));
            }
        }

        public void DrawCore(SpriteBatch spriteBatch, TissueNetwork network, Rectangle visibleBounds)
        {
            if (network == null)
                return;

            Rectangle padded = visibleBounds;
            padded.Inflate(TissueConfig.WorldVisual.CoreCullingPadding, TissueConfig.WorldVisual.CoreCullingPadding);
            network.CollectVisibleBranches(padded, visibleBranches);
            network.CollectVisibleNodes(padded, visibleNodes);

            for (int i = 0; i < visibleBranches.Count; i++)
            {
                TissueBranch branch = visibleBranches[i];
                if (branch.Kind == TissueBranch.TissueBranchKind.Micro)
                {
                    DrawPath(
                        spriteBatch,
                        branch,
                        TissueConfig.WorldVisual.SheathMagenta * TissueConfig.WorldVisual.MicroCoreAlpha,
                        MathF.Max(TissueConfig.WorldVisual.MinimumMicroThickness, branch.Thickness));
                    continue;
                }

                DrawPath(
                    spriteBatch,
                    branch,
                    TissueConfig.WorldVisual.SheathMagenta * (TissueConfig.WorldVisual.CoreSheathAlphaBase + branch.Intensity * TissueConfig.WorldVisual.CoreSheathAlphaIntensity),
                    branch.Thickness * TissueConfig.WorldVisual.CoreSheathThickness);
                Color core = Color.Lerp(
                    TissueConfig.WorldVisual.CorePink,
                    TissueConfig.WorldVisual.CoreGold,
                    MathHelper.Clamp((branch.Intensity - TissueConfig.WorldVisual.CoreGoldBlendStart) * TissueConfig.WorldVisual.CoreGoldBlendScale, 0f, 1f));
                DrawPath(
                    spriteBatch,
                    branch,
                    core * (TissueConfig.WorldVisual.CoreAlphaBase + branch.Intensity * TissueConfig.WorldVisual.CoreAlphaIntensity),
                    MathF.Max(TissueConfig.WorldVisual.MinimumCoreThickness, branch.Thickness * TissueConfig.WorldVisual.CoreThickness));
            }

            for (int i = 0; i < visibleNodes.Count; i++)
            {
                TissueNode node = visibleNodes[i];
                if (!node.IsPrimary)
                    continue;
                float depthScale = MathHelper.Lerp(TissueConfig.WorldVisual.NodeDepthScaleMin, TissueConfig.WorldVisual.NodeDepthScaleMax, node.Strength);
                float radius = (
                    TissueConfig.WorldVisual.CoreNodeRadiusBase +
                    (node.Degree * TissueConfig.WorldVisual.CoreNodeDegreeRadius) +
                    (node.NestInfluence * TissueConfig.WorldVisual.CoreNodeNestRadius)) * depthScale;
                DrawDisc(spriteBatch, node.Position, radius * TissueConfig.WorldVisual.CoreNodeSheathScale, TissueConfig.WorldVisual.SheathMagenta * TissueConfig.WorldVisual.CoreNodeSheathAlpha);
                DrawDisc(spriteBatch, node.Position, radius * TissueConfig.WorldVisual.CoreNodePinkScale, TissueConfig.WorldVisual.CorePink * TissueConfig.WorldVisual.CoreNodePinkAlpha);
                DrawDisc(spriteBatch, node.Position, MathF.Max(TissueConfig.WorldVisual.CoreNodeGoldRadiusMin, radius * TissueConfig.WorldVisual.CoreNodeGoldScale), TissueConfig.WorldVisual.CoreGold);
            }
        }

        public void DrawResonanceHalo(
            SpriteBatch spriteBatch,
            TissueNetwork network,
            Rectangle visibleBounds,
            TissueResonanceState resonance)
        {
            if (network == null || !resonance.IsActive || resonance.Propagation == null || resonance.VisualStrength <= 0.001f)
                return;

            Rectangle padded = GetResonanceCullingBounds(visibleBounds);
            network.CollectVisibleBranches(padded, visibleBranches);
            for (int i = 0; i < visibleBranches.Count; i++)
            {
                TissueBranch branch = visibleBranches[i];
                if (!resonance.Propagation.TryGetBranch(branch.Id, out TissueReachedBranch propagation))
                    continue;

                float kindScale = branch.Kind == TissueBranch.TissueBranchKind.Micro
                    ? TissueConfig.ResonanceVisual.MicroThicknessScale
                    : 1f;
                DrawResonancePath(
                    spriteBatch,
                    branch,
                    propagation,
                    resonance,
                    TissueConfig.ResonanceVisual.HaloColor,
                    TissueConfig.ResonanceVisual.HaloAlpha,
                    TissueConfig.ResonanceVisual.AfterglowColor,
                    TissueConfig.ResonanceVisual.AfterglowHaloAlpha,
                    MathF.Max(1f, branch.Thickness * TissueConfig.ResonanceVisual.HaloThicknessScale * kindScale),
                    branch.Intensity);
            }

            network.CollectVisibleNodes(padded, visibleNodes);
            for (int i = 0; i < visibleNodes.Count; i++)
            {
                TissueNode node = visibleNodes[i];
                if (!resonance.Propagation.TryGetNode(node.Id, out TissueReachedNode propagation) ||
                    propagation.ArrivalDistance > resonance.PulseFront)
                {
                    continue;
                }

                if (TryGetNodeAfterglow(resonance, propagation, out float afterglowIntensity, out float afterglowRadius))
                {
                    DrawDisc(
                        spriteBatch,
                        node.Position,
                        afterglowRadius * TissueConfig.ResonanceVisual.NodeOuterScale,
                        TissueConfig.ResonanceVisual.AfterglowColor *
                            (TissueConfig.ResonanceVisual.AfterglowNodeHaloAlpha * afterglowIntensity));
                }

                if (TryGetNodePulse(resonance, propagation, out float pulseIntensity, out float pulseRadius))
                {
                    DrawDisc(
                        spriteBatch,
                        node.Position,
                        pulseRadius * TissueConfig.ResonanceVisual.NodeOuterScale,
                        TissueConfig.ResonanceVisual.HaloColor *
                            (TissueConfig.ResonanceVisual.NodeHaloAlpha * pulseIntensity));
                }
            }
        }

        public void DrawResonanceCore(
            SpriteBatch spriteBatch,
            TissueNetwork network,
            Rectangle visibleBounds,
            TissueResonanceState resonance)
        {
            if (network == null || !resonance.IsActive || resonance.Propagation == null || resonance.VisualStrength <= 0.001f)
                return;

            Rectangle padded = GetResonanceCullingBounds(visibleBounds);
            network.CollectVisibleBranches(padded, visibleBranches);
            for (int i = 0; i < visibleBranches.Count; i++)
            {
                TissueBranch branch = visibleBranches[i];
                if (!resonance.Propagation.TryGetBranch(branch.Id, out TissueReachedBranch propagation))
                    continue;

                float kindScale = branch.Kind == TissueBranch.TissueBranchKind.Micro
                    ? TissueConfig.ResonanceVisual.MicroThicknessScale
                    : 1f;
                DrawResonancePath(
                    spriteBatch,
                    branch,
                    propagation,
                    resonance,
                    TissueConfig.ResonanceVisual.PulseColor,
                    TissueConfig.ResonanceVisual.CoreAlpha,
                    TissueConfig.ResonanceVisual.AfterglowColor,
                    TissueConfig.ResonanceVisual.AfterglowCoreAlpha,
                    MathF.Max(1f, branch.Thickness * TissueConfig.ResonanceVisual.CoreThicknessScale * kindScale),
                    branch.Intensity);
            }

            network.CollectVisibleNodes(padded, visibleNodes);
            for (int i = 0; i < visibleNodes.Count; i++)
            {
                TissueNode node = visibleNodes[i];
                if (!resonance.Propagation.TryGetNode(node.Id, out TissueReachedNode propagation) ||
                    propagation.ArrivalDistance > resonance.PulseFront)
                {
                    continue;
                }

                if (TryGetNodeAfterglow(resonance, propagation, out float afterglowIntensity, out float afterglowRadius))
                {
                    DrawDisc(
                        spriteBatch,
                        node.Position,
                        afterglowRadius * TissueConfig.ResonanceVisual.NodeInnerScale,
                        TissueConfig.ResonanceVisual.AfterglowColor *
                            (TissueConfig.ResonanceVisual.AfterglowNodeCoreAlpha * afterglowIntensity));
                }

                if (TryGetNodePulse(resonance, propagation, out float pulseIntensity, out float pulseRadius))
                {
                    DrawDisc(
                        spriteBatch,
                        node.Position,
                        pulseRadius * TissueConfig.ResonanceVisual.NodeInnerScale,
                        TissueConfig.ResonanceVisual.PulseColor *
                            (TissueConfig.ResonanceVisual.NodeCoreAlpha * pulseIntensity));
                    DrawDisc(
                        spriteBatch,
                        node.Position,
                        MathF.Max(1f, pulseRadius * 0.46f),
                        TissueConfig.ResonanceVisual.CoreColor * pulseIntensity);
                }
            }
        }

        private void DrawResonancePath(
            SpriteBatch spriteBatch,
            TissueBranch branch,
            TissueReachedBranch propagation,
            TissueResonanceState resonance,
            Color color,
            float baseAlpha,
            Color afterglowColor,
            float afterglowAlpha,
            float thickness,
            float branchStrength)
        {
            if (branch.Points == null || branch.Points.Count < 2 || color.A == 0)
                return;

            int segmentCount = branch.Points.Count - 1;
            float localDistance = 0f;

            for (int logicalIndex = 0; logicalIndex < segmentCount; logicalIndex++)
            {
                int pointIndex = propagation.Reverse
                    ? segmentCount - logicalIndex - 1
                    : logicalIndex;
                Vector2 start = propagation.Reverse ? branch.Points[pointIndex + 1] : branch.Points[pointIndex];
                Vector2 end = propagation.Reverse ? branch.Points[pointIndex] : branch.Points[pointIndex + 1];
                float segmentLength = Vector2.Distance(start, end);
                float pointDistance = propagation.StartDistance + localDistance + (segmentLength * 0.5f);
                localDistance += segmentLength;

                if (pointDistance > resonance.MaximumDistance)
                    break;
                if (pointDistance > resonance.PulseFront)
                    continue;

                float branchProgress = propagation.Length <= 0f
                    ? 0f
                    : MathHelper.Clamp(localDistance / propagation.Length, 0f, 1f);
                float signalStrength = MathHelper.Lerp(
                    propagation.EntryStrength,
                    propagation.ExitStrength,
                    branchProgress);
                float biologicalStrength = MathHelper.Clamp(
                    signalStrength * TissueConfig.Resonance.VisualGain,
                    0f,
                    1f) *
                    MathHelper.Clamp(branchStrength, 0f, 1f);
                if (pointDistance <= resonance.PulseBack)
                {
                    DrawLine(
                        spriteBatch,
                        start,
                        end,
                        afterglowColor * (
                            biologicalStrength *
                            afterglowAlpha *
                            TissueConfig.Resonance.MemoryIntensity *
                            resonance.MemoryStrength),
                        thickness);
                    continue;
                }

                float trailProgress = (resonance.PulseFront - pointDistance) / TissueConfig.Resonance.PulseTrailLength;
                float trailAlpha = 1f - MathHelper.Clamp(trailProgress, 0f, 1f);
                DrawLine(
                    spriteBatch,
                    start,
                    end,
                    color * (biologicalStrength * trailAlpha * baseAlpha),
                    thickness);
            }
        }

        private static bool TryGetNodeAfterglow(
            TissueResonanceState resonance,
            TissueReachedNode propagation,
            out float intensity,
            out float radius)
        {
            intensity = 0f;
            radius = 0f;
            if (propagation.ArrivalDistance > resonance.PulseBack)
                return false;

            intensity = MathHelper.Clamp(
                propagation.ArrivalStrength * TissueConfig.Resonance.VisualGain,
                0f,
                1f) *
                propagation.Node.Strength *
                TissueConfig.Resonance.MemoryIntensity *
                resonance.NodeAfterglowStrength;
            if (intensity <= 0.001f)
                return false;

            radius = GetNodeBaseRadius(propagation.Node) *
                TissueConfig.ResonanceVisual.AfterglowNodeRadiusScale;
            return true;
        }

        private static bool TryGetNodePulse(
            TissueResonanceState resonance,
            TissueReachedNode propagation,
            out float intensity,
            out float radius)
        {
            intensity = 0f;
            radius = 0f;
            float arrivalTime = propagation.ArrivalDistance / TissueConfig.Resonance.PulseSpeed;
            float pulseAge = resonance.ElapsedTime - arrivalTime;
            if (pulseAge < 0f || pulseAge > TissueConfig.Resonance.NodePulseDuration)
                return false;

            float pulseProgress = pulseAge / TissueConfig.Resonance.NodePulseDuration;
            float pulseEnvelope = 1f - pulseProgress;
            intensity = MathHelper.Clamp(
                propagation.ArrivalStrength * TissueConfig.Resonance.VisualGain,
                0f,
                1f) *
                propagation.Node.Strength *
                pulseEnvelope;
            if (intensity <= 0.001f)
                return false;

            float expansion = MathF.Sin(pulseProgress * MathF.PI);
            float baseRadius = GetNodeBaseRadius(propagation.Node);
            radius = baseRadius * (1f + (expansion * TissueConfig.ResonanceVisual.NodePulseScale));
            return true;
        }

        private static float GetNodeBaseRadius(TissueNodeInfo node)
        {
            return TissueConfig.ResonanceVisual.NodeRadiusBase +
                (node.Degree * TissueConfig.ResonanceVisual.NodeDegreeRadius);
        }

        private static Rectangle GetResonanceCullingBounds(Rectangle visibleBounds)
        {
            int horizontalPadding = Math.Max(
                TissueConfig.ResonanceVisual.MinimumCullingPadding,
                (int)MathF.Ceiling(visibleBounds.Width * TissueConfig.ResonanceVisual.CullingMarginRatio));
            int verticalPadding = Math.Max(
                TissueConfig.ResonanceVisual.MinimumCullingPadding,
                (int)MathF.Ceiling(visibleBounds.Height * TissueConfig.ResonanceVisual.CullingMarginRatio));
            Rectangle padded = visibleBounds;
            padded.Inflate(horizontalPadding, verticalPadding);
            return padded;
        }

        private void DrawPath(SpriteBatch spriteBatch, TissueBranch branch, Color color, float baseThickness)
        {
            if (branch.Points == null || branch.Points.Count < 2 || color.A == 0)
                return;

            int segmentCount = branch.Points.Count - 1;
            float depthThicknessScale = branch.Kind == TissueBranch.TissueBranchKind.Micro
                ? 1f
                : MathHelper.Lerp(
                    TissueConfig.WorldVisual.BranchDepthScaleMin,
                    TissueConfig.WorldVisual.BranchDepthScaleMax,
                    MathF.Pow(branch.Intensity, TissueConfig.WorldVisual.BranchDepthCurvePower));
            for (int i = 0; i < segmentCount; i++)
            {
                float t = segmentCount <= 1 ? 0.5f : i / (float)(segmentCount - 1);
                float organicVariation = TissueConfig.WorldVisual.OrganicVariationBase +
                    (MathF.Sin((t * MathF.PI * TissueConfig.WorldVisual.OrganicVariationWaves) + branch.Id * TissueConfig.WorldVisual.OrganicBranchPhase) * TissueConfig.WorldVisual.OrganicVariationAmplitude);
                float endpointBias = 1f + ((1f - MathF.Sin(t * MathF.PI)) *
                    (branch.IsPrimary ? TissueConfig.WorldVisual.PrimaryEndpointBulge : TissueConfig.WorldVisual.SecondaryEndpointBulge));
                DrawLine(
                    spriteBatch,
                    branch.Points[i],
                    branch.Points[i + 1],
                    color,
                    baseThickness * depthThicknessScale * organicVariation * endpointBias);
            }
        }

        private void DrawDisc(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
        {
            if (radius <= 0.5f || color.A == 0)
                return;

            int centerX = (int)MathF.Round(center.X);
            int centerY = (int)MathF.Round(center.Y);
            int pixelRadius = Math.Max(1, (int)MathF.Round(radius));
            int radiusSq = pixelRadius * pixelRadius;
            for (int offsetY = -pixelRadius; offsetY <= pixelRadius; offsetY++)
            {
                int remaining = radiusSq - (offsetY * offsetY);
                if (remaining < 0)
                    continue;

                int halfWidth = (int)MathF.Floor(MathF.Sqrt(remaining));
                spriteBatch.Draw(
                    pixel,
                    new Rectangle(centerX - halfWidth, centerY + offsetY, (halfWidth * 2) + 1, 1),
                    color);
            }
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float thickness)
        {
            int x = (int)MathF.Round(start.X);
            int y = (int)MathF.Round(start.Y);
            int targetX = (int)MathF.Round(end.X);
            int targetY = (int)MathF.Round(end.Y);
            int deltaX = Math.Abs(targetX - x);
            int stepX = x < targetX ? 1 : -1;
            int deltaY = -Math.Abs(targetY - y);
            int stepY = y < targetY ? 1 : -1;
            int error = deltaX + deltaY;
            int radius = Math.Clamp(
                (int)MathF.Round((MathF.Max(1f, thickness) - 1f) * 0.5f),
                0,
                TissueConfig.WorldVisual.MaximumPixelRadius);

            while (true)
            {
                DrawPixelStamp(spriteBatch, x, y, radius, color);
                if (x == targetX && y == targetY)
                    break;

                int twiceError = error * 2;
                if (twiceError >= deltaY)
                {
                    error += deltaY;
                    x += stepX;
                }
                if (twiceError <= deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
            }
        }

        private void DrawPixelStamp(SpriteBatch spriteBatch, int centerX, int centerY, int radius, Color color)
        {
            if (radius <= 0)
            {
                spriteBatch.Draw(pixel, new Rectangle(centerX, centerY, 1, 1), color);
                return;
            }

            int radiusSq = radius * radius;
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                int halfWidth = (int)MathF.Floor(MathF.Sqrt(radiusSq - (offsetY * offsetY)));
                spriteBatch.Draw(
                    pixel,
                    new Rectangle(centerX - halfWidth, centerY + offsetY, (halfWidth * 2) + 1, 1),
                    color);
            }
        }
    }
}
