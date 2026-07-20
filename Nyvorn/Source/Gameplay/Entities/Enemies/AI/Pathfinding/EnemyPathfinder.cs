using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI.Pathfinding
{
    // On-demand A* over the tile grid for a single enemy request - deliberately not a precomputed
    // global surface graph, since Nyvorn's world is destructible and a cached graph would need
    // constant invalidation. Bounded by search radius and an expanded-node budget so a far/unreachable
    // target fails fast instead of scanning the whole world. Jump edges are validated with projectile
    // kinematics (matching Fighter-AI locomotion, which keeps applying horizontal velocity while
    // airborne) plus a coarse arc-clearance sample - not pixel-perfect, tune via playtest.
    public sealed class EnemyPathfinder
    {
        private const int MaxSearchRadiusTiles = 40;
        private const int MaxExpandedNodes = 600;
        private const int MaxFallTiles = 12;
        private const int MaxJumpColumns = 5;
        private const float JumpReachSafetyMargin = 0.85f;

        public bool TryFindPath(
            WorldMap worldMap,
            EnemyConfig config,
            Vector2 startWorld,
            Vector2 goalWorld,
            List<PathWaypoint> result)
        {
            result.Clear();

            int tileSize = worldMap.TileSize;
            int heightTiles = Math.Max(1, (int)MathF.Ceiling(config.HurtboxSize.Y / (float)tileSize));

            Vector2 offset = LoopAwareMath.GetOffset(startWorld, goalWorld, worldMap.PixelWidth);
            Vector2 unwrappedGoalWorld = startWorld + offset;

            Point start = ToFeetTile(startWorld, tileSize);
            Point goal = ToFeetTile(unwrappedGoalWorld, tileSize);

            if (Math.Abs(goal.X - start.X) > MaxSearchRadiusTiles || Math.Abs(goal.Y - start.Y) > MaxSearchRadiusTiles)
                return false;

            if (!IsStandable(worldMap, start.X, start.Y, heightTiles) || !IsStandable(worldMap, goal.X, goal.Y, heightTiles))
                return false;

            PriorityQueue<Point, float> open = new();
            Dictionary<Point, (Point Parent, bool ViaJump)> cameFrom = new();
            Dictionary<Point, float> gScore = new() { [start] = 0f };
            HashSet<Point> closed = new();

            open.Enqueue(start, Heuristic(start, goal));
            int expanded = 0;

            while (open.Count > 0)
            {
                Point current = open.Dequeue();
                if (!closed.Add(current))
                    continue;

                if (current == goal)
                {
                    BuildPath(cameFrom, start, goal, tileSize, result);
                    return result.Count > 0;
                }

                if (++expanded > MaxExpandedNodes)
                    return false;

                foreach ((Point neighbor, float cost, bool viaJump) in GetNeighbors(worldMap, config, current, heightTiles, tileSize))
                {
                    if (closed.Contains(neighbor))
                        continue;

                    float tentativeG = gScore[current] + cost;
                    if (gScore.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG)
                        continue;

                    gScore[neighbor] = tentativeG;
                    cameFrom[neighbor] = (current, viaJump);
                    open.Enqueue(neighbor, tentativeG + Heuristic(neighbor, goal));
                }
            }

            return false;
        }

        private static IEnumerable<(Point Neighbor, float Cost, bool ViaJump)> GetNeighbors(
            WorldMap worldMap, EnemyConfig config, Point from, int heightTiles, int tileSize)
        {
            // Walk/step/fall: adjacent column, first standable row found scanning from 1 tile up
            // (step-up) down through a bounded fall distance. One loop covers step-up/level/step-down/
            // fall since they're all just "how far the ground is in this column."
            for (int dx = -1; dx <= 1; dx += 2)
            {
                for (int dy = -1; dy <= MaxFallTiles; dy++)
                {
                    int nx = from.X + dx;
                    int ny = from.Y + dy;
                    if (!IsStandable(worldMap, nx, ny, heightTiles))
                        continue;

                    float cost = 1f + (dy > 1 ? (dy - 1) * 0.1f : 0f);
                    yield return (new Point(nx, ny), cost, false);
                    break;
                }
            }

            // Jump: longer horizontal hops (gaps, ledges), validated by projectile kinematics.
            // Only columns beyond the plain walk/fall reach - those are cheaper and already covered.
            float gravity = PhysicsSettings.WorldGravity * config.GravityScale;
            for (int dx = -MaxJumpColumns; dx <= MaxJumpColumns; dx++)
            {
                if (Math.Abs(dx) < 2)
                    continue;

                for (int dy = -MaxJumpColumns; dy <= 2; dy++)
                {
                    int nx = from.X + dx;
                    int ny = from.Y + dy;
                    if (!IsStandable(worldMap, nx, ny, heightTiles))
                        continue;

                    float worldDeltaX = dx * tileSize;
                    float worldDeltaY = dy * tileSize;
                    if (!TryValidateJump(config, gravity, worldDeltaX, worldDeltaY, out float t))
                        continue;

                    Vector2 startFeet = new((from.X + 0.5f) * tileSize, (from.Y + 1) * tileSize);
                    if (!HasArcClearance(worldMap, config, gravity, startFeet, worldDeltaX, t, heightTiles, tileSize))
                        continue;

                    float cost = 1.5f * MathF.Abs(dx);
                    yield return (new Point(nx, ny), cost, true);
                }
            }
        }

        private static bool TryValidateJump(EnemyConfig config, float gravity, float worldDeltaX, float worldDeltaY, out float t)
        {
            t = 0f;
            float v0 = config.JumpSpeed;
            if (v0 <= 0f || gravity <= 0f)
                return false;

            float discriminant = (v0 * v0) + (2f * gravity * worldDeltaY);
            if (discriminant < 0f)
                return false;

            float sqrtDiscriminant = MathF.Sqrt(discriminant);
            t = worldDeltaY >= 0f ? (v0 + sqrtDiscriminant) / gravity : (v0 - sqrtDiscriminant) / gravity;
            if (t <= 0f)
                return false;

            float horizontalReach = config.ChaseSpeed * t;
            return MathF.Abs(worldDeltaX) <= horizontalReach * JumpReachSafetyMargin;
        }

        private static bool HasArcClearance(
            WorldMap worldMap, EnemyConfig config, float gravity, Vector2 startFeet, float worldDeltaX, float t, int heightTiles, int tileSize)
        {
            float dirX = MathF.Sign(worldDeltaX);
            float v0 = config.JumpSpeed;

            for (int i = 1; i <= 3; i++)
            {
                float sampleT = t * (i / 3f);
                float x = startFeet.X + (dirX * config.ChaseSpeed * sampleT);
                float y = startFeet.Y + ((-v0 * sampleT) + (0.5f * gravity * sampleT * sampleT));

                int tileX = (int)MathF.Floor(x / tileSize);
                int feetTileY = (int)MathF.Floor(y / tileSize);

                for (int h = 0; h < heightTiles; h++)
                {
                    if (worldMap.IsSolidAt(tileX, feetTileY - 1 - h))
                        return false;
                }
            }

            return true;
        }

        private static bool IsStandable(WorldMap worldMap, int x, int y, int heightTiles)
        {
            if (y < 0 || y >= worldMap.Height)
                return false;
            if (!worldMap.IsSolidAt(x, y + 1))
                return false;

            for (int i = 0; i < heightTiles; i++)
            {
                if (worldMap.IsSolidAt(x, y - i))
                    return false;
            }

            return true;
        }

        private static void BuildPath(
            Dictionary<Point, (Point Parent, bool ViaJump)> cameFrom, Point start, Point goal, int tileSize, List<PathWaypoint> result)
        {
            List<(Point Node, bool ViaJump)> chain = new();
            Point node = goal;
            while (node != start)
            {
                if (!cameFrom.TryGetValue(node, out (Point Parent, bool ViaJump) link))
                    break;

                chain.Add((node, link.ViaJump));
                node = link.Parent;
            }

            chain.Reverse();
            foreach ((Point pathNode, bool viaJump) in chain)
            {
                Vector2 worldPosition = new((pathNode.X + 0.5f) * tileSize, (pathNode.Y + 1) * tileSize);
                result.Add(new PathWaypoint(worldPosition, viaJump));
            }
        }

        private static float Heuristic(Point a, Point b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static Point ToFeetTile(Vector2 worldPosition, int tileSize)
        {
            int tileX = (int)MathF.Floor(worldPosition.X / tileSize);
            int tileY = (int)MathF.Floor(worldPosition.Y / tileSize) - 1;
            return new Point(tileX, tileY);
        }
    }
}
