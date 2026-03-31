using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;
using System.Linq;
using static Nyvorn.Source.World.Tissue.TissueLink;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueAnalyzer
    {
        private readonly record struct HubCandidate(
            Point TilePosition,
            Vector2 WorldPosition,
            TissueLocalType LocalType,
            byte NeighborCount,
            float Openness,
            float ImportanceScore);

        public TissueAnalysisResult Analyze(TissueField field, WorldMap worldMap)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (field.Width != worldMap.Width || field.Height != worldMap.Height)
                throw new ArgumentException("TissueField e WorldMap precisam ter as mesmas dimensões.");

            TissueAnalysisResult result = new TissueAnalysisResult(field.Width, field.Height);

            for (int y = 0; y < field.Height; y++)
            {
                for (int x = 0; x < field.Width; x++)
                {
                    if (!field.HasTissue(x, y))
                    {
                        result.SetLocalType(x, y, TissueLocalType.None);
                        result.SetNeighborCount(x, y, 0);
                        result.SetOpennessScore(x, y, 0f);
                        continue;
                    }

                    byte neighborCount = (byte)CountTissueNeighbors(field, x, y);
                    float openness = SampleOpenness(worldMap, x, y, radius: 3);
                    TissueLocalType localType = Classify(neighborCount, openness);

                    result.SetNeighborCount(x, y, neighborCount);
                    result.SetOpennessScore(x, y, openness);
                    result.SetLocalType(x, y, localType);
                }
            }

            SelectHubs(result, worldMap, minDistanceTiles: 16);
            BuildLinks(field, result, maxNearestHubs: 3, maxLinkDistanceTiles: 36);
            ClassifyLinks(result);
            UpdateHubConnectivity(result);

            return result;
        }

        private static void SelectHubs(TissueAnalysisResult result, WorldMap worldMap, int minDistanceTiles)
        {
            List<HubCandidate> candidates = new();

            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    TissueLocalType localType = result.GetLocalType(x, y);
                    byte neighborCount = result.GetNeighborCount(x, y);
                    float openness = result.GetOpennessScore(x, y);

                    if (!IsHubCandidate(localType, neighborCount, openness))
                        continue;

                    float importanceScore = ComputeImportanceScore(localType, neighborCount, openness);
                    Vector2 worldPosition = worldMap.GetTileCenter(x, y);

                    candidates.Add(new HubCandidate(
                        new Point(x, y),
                        worldPosition,
                        localType,
                        neighborCount,
                        openness,
                        importanceScore));
                }
            }

            List<HubCandidate> sortedCandidates = candidates
                .OrderByDescending(c => c.ImportanceScore)
                .ThenByDescending(c => c.Openness)
                .ThenByDescending(c => c.NeighborCount)
                .ToList();

            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                HubCandidate candidate = sortedCandidates[i];

                if (!IsFarEnoughFromAcceptedHubs(candidate.TilePosition, result.Hubs, minDistanceTiles))
                    continue;

                result.Hubs.Add(new TissueHub(
                    candidate.TilePosition,
                    candidate.WorldPosition,
                    candidate.LocalType,
                    candidate.NeighborCount,
                    candidate.Openness,
                    candidate.ImportanceScore));
            }
        }

        private static int CountTissueNeighbors(TissueField field, int centerX, int centerY)
        {
            int count = 0;

            for (int y = centerY - 1; y <= centerY + 1; y++)
            {
                for (int x = centerX - 1; x <= centerX + 1; x++)
                {
                    if (x == centerX && y == centerY)
                        continue;

                    if (field.HasTissue(x, y))
                        count++;
                }
            }

            return count;
        }

        private static float SampleOpenness(WorldMap worldMap, int centerX, int centerY, int radius)
        {
            int total = 0;
            int open = 0;

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (y < 0 || y >= worldMap.Height)
                    continue;

                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || x >= worldMap.Width)
                        continue;

                    float dx = x - centerX;
                    float dy = y - centerY;
                    if ((dx * dx) + (dy * dy) > radius * radius)
                        continue;

                    total++;

                    if (!worldMap.IsSolidAt(x, y))
                        open++;
                }
            }

            if (total <= 0)
                return 0f;

            return open / (float)total;
        }

        private static TissueLocalType Classify(byte neighborCount, float openness)
        {
            if (neighborCount <= 1)
                return TissueLocalType.Thin;

            if (neighborCount <= 3)
                return TissueLocalType.Thin;

            if (neighborCount <= 5)
                return TissueLocalType.Normal;

            if (neighborCount == 6)
                return openness >= 0.15f
                    ? TissueLocalType.Junction
                    : TissueLocalType.Normal;

            return openness >= 0.12f
                ? TissueLocalType.Dense
                : TissueLocalType.Junction;
        }

        private static bool IsHubCandidate(TissueLocalType localType, byte neighborCount, float openness)
        {
            if (localType == TissueLocalType.Junction)
                return neighborCount >= 7 && openness >= 0.22f;

            if (localType == TissueLocalType.Dense)
            {
                return neighborCount >= 6 && openness >= 0.18f;
            }

            return false;
        }

        private static float ComputeImportanceScore(TissueLocalType localType, byte neighborCount, float openness)
        {
            float typeScore = localType switch
            {
                TissueLocalType.Dense => 1.00f,
                TissueLocalType.Junction => 0.82f,
                TissueLocalType.Normal => 0.45f,
                TissueLocalType.Thin => 0.20f,
                _ => 0f
            };

            float neighborScore = neighborCount / 8f;
            float opennessScore = openness;

            return (typeScore * 0.45f) +
                   (neighborScore * 0.30f) +
                   (opennessScore * 0.25f);
        }

        private static bool IsFarEnoughFromAcceptedHubs(
            Point tilePosition,
            List<TissueHub> acceptedHubs,
            int minDistanceTiles)
        {
            int minDistanceSq = minDistanceTiles * minDistanceTiles;

            for (int i = 0; i < acceptedHubs.Count; i++)
            {
                Point other = acceptedHubs[i].TilePosition;
                int dx = other.X - tilePosition.X;
                int dy = other.Y - tilePosition.Y;
                int distSq = (dx * dx) + (dy * dy);

                if (distSq < minDistanceSq)
                    return false;
            }

            return true;
        }

        private static void BuildLinks(
            TissueField field,
            TissueAnalysisResult result,
            int maxNearestHubs,
            int maxLinkDistanceTiles)
        {
            if (result.Hubs.Count <= 1)
                return;

            HashSet<long> createdLinks = new();
            int maxLinksPerHub = 3;

            for (int hubIndex = 0; hubIndex < result.Hubs.Count; hubIndex++)
            {
                int linksCreatedForThisHub = 0;

                List<int> nearestHubIndices = GetNearestHubIndices(
                    result.Hubs,
                    hubIndex,
                    maxNearestHubs,
                    maxLinkDistanceTiles);

                for (int i = 0; i < nearestHubIndices.Count; i++)
                {
                    if (linksCreatedForThisHub >= maxLinksPerHub)
                        break;

                    int otherHubIndex = nearestHubIndices[i];

                    long linkKey = GetLinkKey(hubIndex, otherHubIndex);
                    if (!createdLinks.Add(linkKey))
                        continue;

                    Point start = result.Hubs[hubIndex].TilePosition;
                    Point end = result.Hubs[otherHubIndex].TilePosition;

                    List<Point> path = FindPathThroughTissue(field, start, end);
                    if (path == null || path.Count == 0)
                        continue;

                    if (!IsReasonablePath(start, end, path))
                        continue;

                    linksCreatedForThisHub++;

                    result.Links.Add(new TissueLink(
                        hubIndex,
                        otherHubIndex,
                        path,
                        path.Count));
                }
            }
        }

        private static void ClassifyLinks(TissueAnalysisResult result)
        {
            if (result.Links.Count == 0)
                return;

            List<(TissueLink Link, float Score)> scoredLinks = new(result.Links.Count);

            foreach (TissueLink link in result.Links)
            {
                TissueHub hubA = result.Hubs[link.StartHubIndex];
                TissueHub hubB = result.Hubs[link.EndHubIndex];

                float avgImportance = (hubA.ImportanceScore + hubB.ImportanceScore) * 0.5f;
                float pathPenalty = link.PathCost * 0.02f;
                float localTypeBonus = GetLinkTypeBonus(hubA.LocalType, hubB.LocalType);

                float score = avgImportance + localTypeBonus - pathPenalty;
                scoredLinks.Add((link, score));
            }

            List<float> orderedScores = scoredLinks
                .Select(entry => entry.Score)
                .OrderByDescending(score => score)
                .ToList();

            float primaryThreshold = orderedScores[System.Math.Max(0, (int)System.MathF.Floor((orderedScores.Count - 1) * 0.30f))];
            float secondaryThreshold = orderedScores[System.Math.Max(0, (int)System.MathF.Floor((orderedScores.Count - 1) * 0.70f))];

            for (int i = 0; i < scoredLinks.Count; i++)
            {
                TissueLink link = scoredLinks[i].Link;
                float score = scoredLinks[i].Score;

                if (score >= primaryThreshold && score >= 0.28f)
                {
                    link.SetType(TissueLinkType.Primary);
                }
                else if (score >= secondaryThreshold && score >= 0.08f)
                {
                    link.SetType(TissueLinkType.Secondary);
                }
                else
                {
                    link.SetType(TissueLinkType.Weak);
                }
            }
        }

        private static float GetLinkTypeBonus(TissueLocalType localTypeA, TissueLocalType localTypeB)
        {
            int denseCount = 0;
            int junctionCount = 0;

            if (localTypeA == TissueLocalType.Dense)
                denseCount++;
            else if (localTypeA == TissueLocalType.Junction)
                junctionCount++;

            if (localTypeB == TissueLocalType.Dense)
                denseCount++;
            else if (localTypeB == TissueLocalType.Junction)
                junctionCount++;

            return (denseCount * 0.08f) + (junctionCount * 0.04f);
        }

        private static List<int> GetNearestHubIndices(
    List<TissueHub> hubs,
    int sourceHubIndex,
    int maxNearestHubs,
    int maxLinkDistanceTiles)
        {
            List<(int Index, float DistanceSq)> candidates = new();
            Point source = hubs[sourceHubIndex].TilePosition;
            float maxDistanceSq = maxLinkDistanceTiles * maxLinkDistanceTiles;

            for (int i = 0; i < hubs.Count; i++)
            {
                if (i == sourceHubIndex)
                    continue;

                Point target = hubs[i].TilePosition;
                int dx = target.X - source.X;
                int dy = target.Y - source.Y;
                float distanceSq = (dx * dx) + (dy * dy);

                if (distanceSq > maxDistanceSq)
                    continue;

                candidates.Add((i, distanceSq));
            }

            return candidates
                .OrderBy(entry => entry.DistanceSq)
                .Take(maxNearestHubs)
                .Select(entry => entry.Index)
                .ToList();
        }

        private static List<Point> FindPathThroughTissue(TissueField field, Point start, Point end)
        {
            if (!field.HasTissue(start.X, start.Y) || !field.HasTissue(end.X, end.Y))
                return null;

            int width = field.Width;
            int height = field.Height;

            bool[] visited = new bool[width * height];
            Point[] previous = new Point[width * height];
            Queue<Point> queue = new();

            for (int i = 0; i < previous.Length; i++)
                previous[i] = new Point(-1, -1);

            queue.Enqueue(start);
            visited[(start.Y * width) + start.X] = true;

            Point[] directions = new Point[]
            {
        new Point(-1,  0),
        new Point( 1,  0),
        new Point( 0, -1),
        new Point( 0,  1),
        new Point(-1, -1),
        new Point( 1, -1),
        new Point(-1,  1),
        new Point( 1,  1),
            };

            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();

                if (current == end)
                    return ReconstructPath(previous, start, end, width);

                for (int i = 0; i < directions.Length; i++)
                {
                    int nx = current.X + directions[i].X;
                    int ny = current.Y + directions[i].Y;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        continue;

                    if (!field.HasTissue(nx, ny))
                        continue;

                    int nextIndex = (ny * width) + nx;
                    if (visited[nextIndex])
                        continue;

                    visited[nextIndex] = true;
                    previous[nextIndex] = current;
                    queue.Enqueue(new Point(nx, ny));
                }
            }

            return null;
        }

        private static List<Point> ReconstructPath(Point[] previous, Point start, Point end, int width)
        {
            List<Point> path = new();
            Point current = end;

            while (current != start)
            {
                path.Add(current);

                int index = (current.Y * width) + current.X;
                Point prev = previous[index];

                if (prev.X < 0 || prev.Y < 0)
                    return new List<Point>();

                current = prev;
            }

            path.Add(start);
            path.Reverse();
            return path;
        }

        private static bool IsReasonablePath(Point start, Point end, List<Point> path)
        {
            if (path.Count <= 0)
                return false;

            int dx = end.X - start.X;
            int dy = end.Y - start.Y;
            float straightDistance = MathF.Sqrt((dx * dx) + (dy * dy));

            if (straightDistance <= 0.001f)
                return false;

            float pathLength = path.Count;

            // tolerância: caminho pode ser maior que a reta,
            // mas não grotescamente maior
            float ratio = pathLength / straightDistance;

            return ratio <= 2.00f;
        }

        private static long GetLinkKey(int a, int b)
        {
            int min = Math.Min(a, b);
            int max = Math.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private static void UpdateHubConnectivity(TissueAnalysisResult result)
        {
            for (int i = 0; i < result.Links.Count; i++)
            {
                TissueLink link = result.Links[i];

                if (link.StartHubIndex >= 0 && link.StartHubIndex < result.Hubs.Count)
                    result.Hubs[link.StartHubIndex].IncrementLinkCount();

                if (link.EndHubIndex >= 0 && link.EndHubIndex < result.Hubs.Count)
                    result.Hubs[link.EndHubIndex].IncrementLinkCount();
            }
        }
    }
}
