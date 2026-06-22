using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public enum TissueState
    {
        Normal,
        Dense,
        Corrupted,
        Dead
    }

    public struct TissueCell
    {
        public byte Density;
        public TissueState State;

        public TissueCell(byte density, TissueState state)
        {
            Density = density;
            State = state;
        }
    }

    public readonly struct TissueCellPosition
    {
        public int PixelX { get; }
        public int PixelY { get; }
        public TissueCell Cell { get; }

        public TissueCellPosition(int pixelX, int pixelY, TissueCell cell)
        {
            PixelX = pixelX;
            PixelY = pixelY;
            Cell = cell;
        }
    }

    public sealed class TissueSystem
    {
        private const float SurfaceStartDepthPercent = 0.18f;
        private const float DeepStartDepthPercent = 0.68f;
        private const int MainFlowCount = 24;
        private const int SurfaceReachingFlowCount = 5;
        private const int MainFlowSpacing = 360;
        private const float MainFlowWanderStrength = 0.72f;
        private const float HorizontalConnectionChance = 0.68f;
        private const float BranchChance = 0.035f;
        private const int NestCount = 3;
        private const int NestRadiusMin = 140;
        private const int NestRadiusMax = 280;
        private const byte DensityMin = 30;
        private const byte DensityMax = 230;
        private const int ThicknessMin = 1;
        private const int ThicknessMax = 4;
        private const float DensityCurvePower = 2.35f;

        private const int MainFlowControlStepMin = 18;
        private const int MainFlowControlStepMax = 30;
        private const int HorizontalConnectionVerticalSpacing = 120;

        private sealed class MainFlow
        {
            public int StartX { get; init; }
            public List<Point> Points { get; } = new();
        }

        private readonly WorldMap worldMap;
        private readonly Dictionary<long, TissueCell> cells = new();

        public int TileSize { get; }
        public int Width { get; }
        public int Height { get; }

        public TissueSystem(WorldMap worldMap)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));

            TileSize = worldMap.TileSize;
            Width = worldMap.Width * TileSize;
            Height = worldMap.Height * TileSize;
        }

        public bool HasTissueAt(int pixelX, int pixelY)
        {
            return IsInBounds(pixelX, pixelY) && cells.ContainsKey(CreatePixelKey(pixelX, pixelY));
        }

        public bool TryGetTissueAt(int pixelX, int pixelY, out TissueCell cell)
        {
            if (!IsInBounds(pixelX, pixelY))
            {
                cell = default;
                return false;
            }

            return cells.TryGetValue(CreatePixelKey(pixelX, pixelY), out cell);
        }

        public void SetTissueAt(int pixelX, int pixelY, byte density, TissueState state = TissueState.Normal)
        {
            if (!IsInBounds(pixelX, pixelY))
                return;

            cells[CreatePixelKey(pixelX, pixelY)] = new TissueCell(density, state);
        }

        public void Clear()
        {
            cells.Clear();
        }

        public IEnumerable<TissueCellPosition> EnumerateCells()
        {
            foreach (KeyValuePair<long, TissueCell> entry in cells)
            {
                int pixelX = (int)(uint)entry.Key;
                int pixelY = (int)(entry.Key >> 32);
                yield return new TissueCellPosition(pixelX, pixelY, entry.Value);
            }
        }

        public void GenerateDebugPlanetaryNetwork(int seed)
        {
            Clear();

            Random random = new(seed);
            int flowCount = Math.Max(MainFlowCount, Width / MainFlowSpacing);
            int surfaceFlowCount = Math.Min(
                flowCount,
                Math.Max(SurfaceReachingFlowCount, flowCount / 16));
            HashSet<int> surfaceFlowIndices = SelectSurfaceFlowIndices(random, flowCount, surfaceFlowCount);
            List<MainFlow> flows = new(flowCount);

            for (int flowIndex = 0; flowIndex < flowCount; flowIndex++)
            {
                float flowCellWidth = Width / (float)flowCount;
                int centerX = (int)MathF.Round((flowIndex + 0.5f) * flowCellWidth);
                int jitter = (int)MathF.Round(flowCellWidth * 0.28f);
                int startX = Math.Clamp(centerX + random.Next(-jitter, jitter + 1), 1, Width - 2);
                int startY = Math.Clamp(
                    (int)MathF.Round(Height * (0.94f + ((float)random.NextDouble() * 0.05f))),
                    1,
                    Height - 2);

                bool reachesSurface = surfaceFlowIndices.Contains(flowIndex);
                float targetDepth = reachesSurface
                    ? SurfaceStartDepthPercent + ((float)random.NextDouble() * 0.12f)
                    : 0.34f + ((float)random.NextDouble() * (DeepStartDepthPercent - 0.34f));
                int targetY = Math.Clamp((int)MathF.Round(Height * targetDepth), 1, Height - 2);

                flows.Add(GenerateMainFlow(random, startX, startY, targetY));
            }

            flows.Sort((left, right) => left.StartX.CompareTo(right.StartX));
            GenerateDepthConnections(random, flows);
            GenerateFlowBranches(random, flows);

            int scaledNestCount = NestCount + Math.Max(0, Width / 24000);
            for (int nestIndex = 0; nestIndex < scaledNestCount; nestIndex++)
                GenerateNest(random, flows);
        }

        private MainFlow GenerateMainFlow(Random random, int startX, int startY, int targetY)
        {
            MainFlow flow = new() { StartX = startX };
            Point current = new(startX, startY);
            flow.Points.Add(current);
            float lateralVelocity = ((float)random.NextDouble() - 0.5f) * MainFlowWanderStrength;

            while (current.Y > targetY)
            {
                int verticalStep = random.Next(MainFlowControlStepMin, MainFlowControlStepMax + 1);
                lateralVelocity = (lateralVelocity * 0.82f) +
                    (((float)random.NextDouble() - 0.5f) * MainFlowWanderStrength);
                lateralVelocity = Math.Clamp(lateralVelocity, -1.35f, 1.35f);

                int nextX = Math.Clamp(
                    current.X + (int)MathF.Round(lateralVelocity * verticalStep * 0.72f),
                    1,
                    Width - 2);
                int nextY = Math.Max(targetY, current.Y - verticalStep);
                Point next = new(nextX, nextY);
                float depth = GetDepth((current.Y + next.Y) / 2);

                DrawOrganicLine(
                    random,
                    current,
                    next,
                    GetThickness(depth, 1f),
                    GetDensity(depth, 1f));

                flow.Points.Add(next);
                current = next;
            }

            return flow;
        }

        private void GenerateDepthConnections(Random random, List<MainFlow> flows)
        {
            int minY = Math.Clamp((int)MathF.Round(Height * SurfaceStartDepthPercent), 1, Height - 2);

            for (int bandY = minY; bandY < Height; bandY += HorizontalConnectionVerticalSpacing)
            {
                float depth = GetDepth(bandY);
                float densityFactor = GetDensityFactor(depth);
                float connectionChance = 0.035f + (HorizontalConnectionChance * densityFactor);
                if (depth >= 0.82f)
                    connectionChance = Math.Max(connectionChance, 0.72f);

                for (int flowIndex = 0; flowIndex < flows.Count - 1; flowIndex++)
                {
                    int localY = Math.Clamp(
                        bandY + random.Next(-HorizontalConnectionVerticalSpacing / 4, (HorizontalConnectionVerticalSpacing / 4) + 1),
                        minY,
                        Height - 2);
                    if (!TryGetFlowPointAtY(flows[flowIndex], localY, out Point start) ||
                        !TryGetFlowPointAtY(flows[flowIndex + 1], localY, out Point end))
                    {
                        continue;
                    }

                    if (random.NextDouble() <= connectionChance)
                        GenerateHorizontalConnection(random, start, end, depth);
                }
            }
        }

        private void GenerateHorizontalConnection(Random random, Point start, Point end, float depth)
        {
            float distance = Vector2.Distance(start.ToVector2(), end.ToVector2());
            if (distance > MainFlowSpacing * 2.4f)
                return;

            int bendY = (start.Y + end.Y) / 2 + random.Next(-34, 35);
            Point bend = new(
                (start.X + end.X) / 2 + random.Next(-20, 21),
                Math.Clamp(bendY, 1, Height - 2));
            int thickness = GetThickness(depth, 0.82f);
            byte density = GetDensity(depth, 0.86f);

            DrawOrganicLine(random, start, bend, thickness, density);
            DrawOrganicLine(random, bend, end, thickness, density);
        }

        private void GenerateFlowBranches(Random random, List<MainFlow> flows)
        {
            foreach (MainFlow flow in flows)
            {
                for (int pointIndex = 2; pointIndex < flow.Points.Count - 2; pointIndex++)
                {
                    Point origin = flow.Points[pointIndex];
                    float depth = GetDepth(origin.Y);
                    float chance = 0.003f + (BranchChance * GetDensityFactor(depth));
                    if (random.NextDouble() <= chance)
                        GenerateShortBranch(random, origin, depth);
                }
            }
        }

        private void GenerateShortBranch(Random random, Point origin, float depth)
        {
            float side = random.Next(2) == 0 ? -1f : 1f;
            int length = (int)MathF.Round(18f + (GetDensityFactor(depth) * 58f) + random.Next(0, 24));
            int endX = Math.Clamp(origin.X + (int)MathF.Round(side * length), 1, Width - 2);
            int endY = Math.Clamp(origin.Y + random.Next(-length / 2, (length / 2) + 1), 1, Height - 2);
            Point bend = new(
                (origin.X + endX) / 2,
                Math.Clamp((origin.Y + endY) / 2 + random.Next(-12, 13), 1, Height - 2));
            Point end = new(endX, endY);
            int thickness = Math.Max(ThicknessMin, GetThickness(depth, 0.55f));
            byte density = GetDensity(depth, 0.62f);

            DrawOrganicLine(random, origin, bend, thickness, density);
            DrawOrganicLine(random, bend, end, thickness, density);
        }

        private void GenerateNest(Random random, List<MainFlow> flows)
        {
            if (flows.Count == 0)
                return;

            MainFlow anchorFlow = flows[random.Next(flows.Count)];
            int targetY = (int)MathF.Round(Height * (0.78f + ((float)random.NextDouble() * 0.17f)));
            if (!TryGetFlowPointAtY(anchorFlow, targetY, out Point center))
                return;

            int radius = random.Next(NestRadiusMin, NestRadiusMax + 1);
            int strandCount = random.Next(20, 33);
            float depth = GetDepth(center.Y);

            for (int strand = 0; strand < strandCount; strand++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                float startRadius = radius * (0.12f + ((float)random.NextDouble() * 0.78f));
                Point current = new(
                    Math.Clamp(center.X + (int)MathF.Round(MathF.Cos(angle) * startRadius), 1, Width - 2),
                    Math.Clamp(center.Y + (int)MathF.Round(MathF.Sin(angle) * startRadius * 0.62f), 1, Height - 2));
                int segmentCount = random.Next(3, 7);

                for (int segment = 0; segment < segmentCount; segment++)
                {
                    float pullAngle = MathF.Atan2(center.Y - current.Y, center.X - current.X);
                    float direction = pullAngle + (((float)random.NextDouble() - 0.5f) * 2.5f);
                    int segmentLength = random.Next(18, 55);
                    Point next = new(
                        Math.Clamp(current.X + (int)MathF.Round(MathF.Cos(direction) * segmentLength), center.X - radius, center.X + radius),
                        Math.Clamp(current.Y + (int)MathF.Round(MathF.Sin(direction) * segmentLength), center.Y - radius, center.Y + radius));
                    next.X = Math.Clamp(next.X, 1, Width - 2);
                    next.Y = Math.Clamp(next.Y, 1, Height - 2);

                    DrawOrganicLine(
                        random,
                        current,
                        next,
                        GetThickness(depth, 0.72f),
                        GetDensity(depth, 1.05f));
                    current = next;
                }
            }
        }

        private void DrawOrganicLine(Random random, Point start, Point end, int thickness, byte density)
        {
            Vector2 delta = end.ToVector2() - start.ToVector2();
            float distance = delta.Length();
            if (distance < 0.5f)
            {
                DrawTissueBlob(start.X, start.Y, thickness, density);
                return;
            }

            Vector2 direction = delta / distance;
            Vector2 perpendicular = new(-direction.Y, direction.X);
            int steps = Math.Max(1, (int)MathF.Ceiling(distance / 1.75f));
            float noiseOffset = 0f;
            float noiseVelocity = 0f;
            float noiseAmplitude = 2.2f + (thickness * 1.35f);

            for (int step = 0; step <= steps; step++)
            {
                float t = step / (float)steps;
                noiseVelocity = (noiseVelocity * 0.78f) +
                    (((float)random.NextDouble() - 0.5f) * MainFlowWanderStrength);
                noiseOffset = Math.Clamp(noiseOffset + noiseVelocity, -noiseAmplitude, noiseAmplitude);
                float envelope = MathF.Sin(t * MathF.PI);
                Vector2 point = Vector2.Lerp(start.ToVector2(), end.ToVector2(), t) +
                    (perpendicular * noiseOffset * envelope);
                byte localDensity = VaryDensity(random, density, 10);

                DrawTissueBlob(
                    (int)MathF.Round(point.X),
                    (int)MathF.Round(point.Y),
                    thickness,
                    localDensity);
            }
        }

        private void DrawTissueBlob(int centerX, int centerY, int radius, byte density)
        {
            int extent = radius + 1;
            float densityRadius = radius + 0.75f;

            for (int offsetY = -extent; offsetY <= extent; offsetY++)
            {
                for (int offsetX = -extent; offsetX <= extent; offsetX++)
                {
                    int pixelX = centerX + offsetX;
                    int pixelY = centerY + offsetY;
                    if (!IsSolidPixel(pixelX, pixelY))
                        continue;

                    float distance = MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
                    float irregularRadius = radius + (SampleBlobNoise(pixelX, pixelY) * 0.55f);
                    if (distance > irregularRadius)
                        continue;

                    float edgeFactor = Math.Clamp(1f - (distance / densityRadius), 0f, 1f);
                    byte localDensity = (byte)Math.Clamp(
                        (int)MathF.Round(density * (0.35f + (edgeFactor * 0.65f)) + (SampleBlobNoise(pixelY, pixelX) * 9f)),
                        1,
                        255);

                    if (TryGetTissueAt(pixelX, pixelY, out TissueCell existing) && existing.Density >= localDensity)
                        continue;

                    SetTissueAt(pixelX, pixelY, localDensity);
                }
            }
        }

        private HashSet<int> SelectSurfaceFlowIndices(Random random, int flowCount, int surfaceFlowCount)
        {
            HashSet<int> selected = new();
            for (int index = 0; index < surfaceFlowCount; index++)
            {
                int sectionStart = (index * flowCount) / surfaceFlowCount;
                int sectionEnd = Math.Max(sectionStart + 1, ((index + 1) * flowCount) / surfaceFlowCount);
                selected.Add(random.Next(sectionStart, sectionEnd));
            }

            return selected;
        }

        private static bool TryGetFlowPointAtY(MainFlow flow, int targetY, out Point point)
        {
            point = default;
            if (flow.Points.Count == 0)
                return false;

            int bottomY = flow.Points[0].Y;
            int topY = flow.Points[^1].Y;
            if (targetY < topY || targetY > bottomY)
                return false;

            int bestDistance = int.MaxValue;
            for (int index = 0; index < flow.Points.Count; index++)
            {
                int distance = Math.Abs(flow.Points[index].Y - targetY);
                if (distance >= bestDistance)
                    continue;

                point = flow.Points[index];
                bestDistance = distance;
            }

            return true;
        }

        private float GetDepth(int pixelY)
        {
            return Height <= 1 ? 1f : Math.Clamp(pixelY / (float)Height, 0f, 1f);
        }

        private static float GetDensityFactor(float depth)
        {
            return MathF.Pow(Math.Clamp(depth, 0f, 1f), DensityCurvePower);
        }

        private static byte GetDensity(float depth, float multiplier)
        {
            float density = MathHelper.Lerp(DensityMin, DensityMax, GetDensityFactor(depth)) * multiplier;
            return (byte)Math.Clamp((int)MathF.Round(density), 1, 255);
        }

        private static int GetThickness(float depth, float multiplier)
        {
            float thickness = MathHelper.Lerp(ThicknessMin, ThicknessMax, GetDensityFactor(depth)) * multiplier;
            return Math.Clamp((int)MathF.Round(thickness), ThicknessMin, ThicknessMax);
        }

        private bool IsSolidPixel(int pixelX, int pixelY)
        {
            if (!IsInBounds(pixelX, pixelY))
                return false;

            int tileX = pixelX / TileSize;
            int tileY = pixelY / TileSize;
            return worldMap.IsSolidAt(tileX, tileY);
        }

        private static byte VaryDensity(Random random, byte density, int variation)
        {
            return (byte)Math.Clamp(density + random.Next(-variation, variation + 1), 1, 255);
        }

        private static float SampleBlobNoise(int x, int y)
        {
            uint hash = (uint)(x * 374761393) + (uint)(y * 668265263);
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            return ((hash & 1023u) / 511.5f) - 1f;
        }

        private bool IsInBounds(int pixelX, int pixelY)
        {
            return pixelX >= 0 && pixelX < Width && pixelY >= 0 && pixelY < Height;
        }

        private static long CreatePixelKey(int pixelX, int pixelY)
        {
            return ((long)pixelY << 32) | (uint)pixelX;
        }
    }
}
