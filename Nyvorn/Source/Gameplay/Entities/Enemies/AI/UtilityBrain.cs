using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Enemies.AI.Pathfinding;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    // IAUS-lite brain for a small number of "signature" enemies. Instead of EnemyBrain's fixed
    // priority chain, each action gets a [0,1] score from its considerations (Dave Mark's
    // compensated-product formula, so several considerations don't crush the score toward 0 the way
    // a plain product would), the highest-scoring action wins, and the previous winner gets a
    // momentum bonus so it doesn't flicker between two near-tied actions frame to frame.
    public sealed class UtilityBrain : IEnemyBrain
    {
        private const float MomentumBonus = 1.25f;
        private const float RepathInterval = 0.75f;
        private const float RepathTargetMoveThreshold = 24f;
        private const float WaypointArrivalDistance = 6f;

        private enum Action { Idle, Chase, Attack, Retreat }

        private readonly EnemyConfig config;
        private readonly EnemyPathfinder pathfinder = new();
        private readonly List<PathWaypoint> path = new();

        private Vector2 lastKnownPlayerPosition;
        private float memoryTimer;
        private float retreatTimer;
        private float attackCooldownTimer;
        private float hitRetreatDirection;
        private Action lastChosenAction = Action.Idle;

        private int waypointIndex;
        private float repathTimer;
        private Vector2 pathTargetSnapshot;
        private bool hasPath;

        public UtilityBrain(EnemyConfig config)
        {
            this.config = config;
            CurrentIntent = EnemyIntent.Idle;
        }

        public EnemyIntent CurrentIntent { get; private set; }

        public void NotifyHit(float knockbackX)
        {
            retreatTimer = config.HitRetreatDuration;
            hitRetreatDirection = MathF.Sign(knockbackX);
            if (hitRetreatDirection == 0f)
                hitRetreatDirection = 1f;
        }

        public EnemyBrainDecision Update(
            float dt,
            in Perception perception,
            Vector2 selfPosition,
            float worldWidth,
            int health,
            int maxHealth,
            WorldMap worldMap)
        {
            if (attackCooldownTimer > 0f) attackCooldownTimer -= dt;
            if (memoryTimer > 0f) memoryTimer -= dt;
            if (retreatTimer > 0f) retreatTimer -= dt;
            if (repathTimer > 0f) repathTimer -= dt;

            bool seesPlayer = perception.SeesTarget;
            if (seesPlayer)
            {
                lastKnownPlayerPosition = selfPosition + perception.TargetOffset;
                memoryTimer = config.PlayerMemoryDuration;
            }

            // Hit-reaction retreat is a reflex, not a considered decision - short-circuits scoring
            // exactly like EnemyBrain's, kept consistent across both brain implementations.
            if (retreatTimer > 0f)
                return Decide(Action.Retreat, EnemyIntent.Retreat, hitRetreatDirection * config.RetreatSpeed, false);

            bool hasMemory = memoryTimer > 0f;
            bool hasTarget = seesPlayer || hasMemory;
            Vector2 targetOffset = seesPlayer
                ? perception.TargetOffset
                : LoopAwareMath.GetOffset(selfPosition, lastKnownPlayerPosition, worldWidth);
            float distance = hasTarget ? targetOffset.Length() : float.MaxValue;
            float healthPercent = maxHealth <= 0 ? 1f : health / (float)maxHealth;

            float idleScore = ScoreIdle();
            float chaseScore = ScoreChase(hasTarget, seesPlayer, distance);
            float attackScore = ScoreAttack(seesPlayer, targetOffset);
            float retreatScore = ScoreRetreat(healthPercent, seesPlayer, distance);

            Action best = PickBest(idleScore, chaseScore, attackScore, retreatScore);

            switch (best)
            {
                case Action.Attack:
                    return Decide(Action.Attack, EnemyIntent.Attack, 0f, TryStartAttack());

                case Action.Retreat:
                {
                    float retreatDirection = -MathF.Sign(targetOffset.X);
                    if (retreatDirection == 0f)
                        retreatDirection = 1f;
                    return Decide(Action.Retreat, EnemyIntent.Retreat, retreatDirection * config.RetreatSpeed, false);
                }

                case Action.Chase:
                    return DecideChase(seesPlayer, targetOffset, selfPosition, worldWidth, worldMap, perception);

                default:
                    return Decide(Action.Idle, EnemyIntent.Idle, 0f, false);
            }
        }

        private static float ScoreIdle() => 0.15f;

        private float ScoreChase(bool hasTarget, bool seesPlayer, float distance)
        {
            if (!hasTarget)
                return 0f;

            float awareness = seesPlayer ? 1f : Clamp01(memoryTimer / config.PlayerMemoryDuration);
            float notAlreadyInRange = Clamp01((distance - config.AttackRange) / MathF.Max(1f, config.PlayerAwarenessRange - config.AttackRange));
            return ScoreConsiderations(awareness, notAlreadyInRange);
        }

        private float ScoreAttack(bool seesPlayer, Vector2 targetOffset)
        {
            if (!seesPlayer)
                return 0f;

            bool inRange = MathF.Abs(targetOffset.X) <= config.AttackRange && MathF.Abs(targetOffset.Y) <= config.AttackVerticalRange;
            float cooldownReady = attackCooldownTimer <= 0f ? 1f : 0f;
            return ScoreConsiderations(inRange ? 1f : 0f, cooldownReady);
        }

        private float ScoreRetreat(float healthPercent, bool seesPlayer, float distance)
        {
            if (!seesPlayer || distance > config.LowHealthRetreatRange)
                return 0f;

            float lowHealth = Clamp01(1f - (healthPercent / config.LowHealthRetreatThreshold));
            return ScoreConsiderations(lowHealth, 1f);
        }

        private Action PickBest(float idle, float chase, float attack, float retreat)
        {
            Span<float> scores = stackalloc float[4];
            scores[(int)Action.Idle] = idle;
            scores[(int)Action.Chase] = chase;
            scores[(int)Action.Attack] = attack;
            scores[(int)Action.Retreat] = retreat;
            scores[(int)lastChosenAction] *= MomentumBonus;

            int bestIndex = 0;
            for (int i = 1; i < scores.Length; i++)
            {
                if (scores[i] > scores[bestIndex])
                    bestIndex = i;
            }

            return (Action)bestIndex;
        }

        private EnemyBrainDecision Decide(Action action, EnemyIntent intent, float moveVelocityX, bool triggerAttackVisual, bool wantsJump = false)
        {
            lastChosenAction = action;
            CurrentIntent = intent;
            return new EnemyBrainDecision(intent, moveVelocityX, triggerAttackVisual, wantsJump);
        }

        private bool TryStartAttack()
        {
            if (attackCooldownTimer > 0f)
                return false;

            attackCooldownTimer = config.AttackCooldown;
            return true;
        }

        private EnemyBrainDecision DecideChase(
            bool seesPlayer, Vector2 targetOffset, Vector2 selfPosition, float worldWidth, WorldMap worldMap, in Perception perception)
        {
            EnemyIntent intent = seesPlayer ? EnemyIntent.Chase : EnemyIntent.Investigate;

            if (!config.UsesPathfinding || worldMap == null)
                return Decide(Action.Chase, intent, DirectApproachVelocity(targetOffset), false);

            Vector2 targetWorldPosition = selfPosition + targetOffset;
            bool followedPath = TryFollowPath(selfPosition, targetWorldPosition, worldWidth, worldMap, perception, out Vector2 steeringOffset, out bool wantsJump);

            Vector2 finalOffset = followedPath ? steeringOffset : targetOffset;
            return Decide(Action.Chase, intent, DirectApproachVelocity(finalOffset), false, followedPath && wantsJump);
        }

        private bool TryFollowPath(
            Vector2 selfPosition, Vector2 targetWorldPosition, float worldWidth, WorldMap worldMap,
            in Perception perception, out Vector2 steeringOffset, out bool wantsJump)
        {
            steeringOffset = Vector2.Zero;
            wantsJump = false;

            bool needsRepath = !hasPath
                || repathTimer <= 0f
                || Vector2.DistanceSquared(pathTargetSnapshot, targetWorldPosition) > RepathTargetMoveThreshold * RepathTargetMoveThreshold;

            if (needsRepath)
            {
                repathTimer = RepathInterval;
                pathTargetSnapshot = targetWorldPosition;
                hasPath = pathfinder.TryFindPath(worldMap, config, selfPosition, targetWorldPosition, path);
                waypointIndex = 0;
            }

            if (!hasPath || path.Count == 0 || waypointIndex >= path.Count)
                return false;

            PathWaypoint waypoint = path[waypointIndex];
            Vector2 offset = LoopAwareMath.GetOffset(selfPosition, waypoint.WorldPosition, worldWidth);

            bool isFinalWaypoint = waypointIndex == path.Count - 1;
            float arrivalDistance = isFinalWaypoint ? config.ChaseStopDistance : WaypointArrivalDistance;
            if (MathF.Abs(offset.X) <= arrivalDistance && MathF.Abs(offset.Y) <= worldMap.TileSize)
            {
                waypointIndex++;
                if (waypointIndex >= path.Count)
                {
                    hasPath = false;
                    return false;
                }

                waypoint = path[waypointIndex];
                offset = LoopAwareMath.GetOffset(selfPosition, waypoint.WorldPosition, worldWidth);
            }

            steeringOffset = offset;
            wantsJump = perception.OnGround && waypoint.RequiresJump;
            return true;
        }

        private float DirectApproachVelocity(Vector2 offset)
        {
            if (MathF.Abs(offset.X) <= config.ChaseStopDistance)
                return 0f;

            return MathF.Sign(offset.X) * config.ChaseSpeed;
        }

        private static float ScoreConsiderations(params float[] considerations)
        {
            if (considerations.Length == 0)
                return 0f;

            float product = 1f;
            for (int i = 0; i < considerations.Length; i++)
                product *= Clamp01(considerations[i]);

            float modificationFactor = 1f - (1f / considerations.Length);
            float makeUpValue = (1f - product) * modificationFactor;
            return Clamp01(product + (makeUpValue * product));
        }

        private static float Clamp01(float value) => MathHelper.Clamp(value, 0f, 1f);
    }
}
