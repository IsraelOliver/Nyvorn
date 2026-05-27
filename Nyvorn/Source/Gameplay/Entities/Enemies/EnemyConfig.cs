using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyConfig
    {
        public static EnemyConfig Default { get; } = new EnemyConfig();

        public Point HurtboxSize { get; init; } = new Point(16, 24);
        public float GravityScale { get; init; } = 1f;
        public float KnockbackRecovery { get; init; } = 10f;
        public float AttackVisualDuration { get; init; } = 0.12f;
        public float HurtDuration { get; init; } = 0.15f;
        public int MaxHealth { get; init; } = 100;
        public int ContactDamage { get; init; } = 10;
        public float ContactKnockbackX { get; init; } = 180f;
        public float ContactKnockbackY { get; init; } = -75f;
        public int FrameWidth { get; init; } = 32;
        public int FrameHeight { get; init; } = 32;
        public float PlayerAwarenessRange { get; init; } = 168f;
        public float PlayerVerticalAwarenessRange { get; init; } = 72f;
        public float PlayerMemoryDuration { get; init; } = 2.2f;
        public float ChaseSpeed { get; init; } = 38f;
        public float ChaseStopDistance { get; init; } = 18f;
        public float InvestigateSpeed { get; init; } = 24f;
        public float InvestigateStopDistance { get; init; } = 10f;
        public float AttackRange { get; init; } = 22f;
        public float AttackVerticalRange { get; init; } = 20f;
        public float AttackCooldown { get; init; } = 0.85f;
        public float HitRetreatDuration { get; init; } = 0.35f;
        public float RetreatSpeed { get; init; } = 56f;
        public float LowHealthRetreatThreshold { get; init; } = 0.35f;
        public float LowHealthRetreatRange { get; init; } = 72f;
    }
}
