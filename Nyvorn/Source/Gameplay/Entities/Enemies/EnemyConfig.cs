using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyConfig
    {
        public static EnemyConfig Default { get; } = new EnemyConfig();

        // "Signature" preset: same GroundChaserBrain as every other enemy, just notices the player
        // from further away.
        public static EnemyConfig Signature { get; } = new EnemyConfig
        {
            PlayerAwarenessRange = 240f,
            PlayerVerticalAwarenessRange = 120f
        };

        // Scopes the day/night spawn+aggro rule (see NightSurfaceSpawnSystem/GroundChaserBrain) to
        // enemies that actually live on the surface - cave enemies will get their own rules later
        // and shouldn't turn passive just because it's daytime up above.
        public EnemyHabitat Habitat { get; init; } = EnemyHabitat.Surface;

        public Point HurtboxSize { get; init; } = new Point(16, 24);
        public float GravityScale { get; init; } = 1f;
        public float KnockbackRecovery { get; init; } = 10f;
        public float AttackVisualDuration { get; init; } = 0.12f;
        public float HurtDuration { get; init; } = 0.15f;
        public int MaxHealth { get; init; } = 100;

        // Damage from the enemy's body touching the player's - see the comment on
        // Enemy.HitDamage for why this is the only damage source right now.
        public int ContactDamage { get; init; } = 10;
        public float ContactKnockbackX { get; init; } = 180f;
        public float ContactKnockbackY { get; init; } = -75f;
        public int FrameWidth { get; init; } = 32;
        public int FrameHeight { get; init; } = 32;
        public float PlayerAwarenessRange { get; init; } = 168f;
        public float PlayerVerticalAwarenessRange { get; init; } = 72f;
        public float ChaseSpeed { get; init; } = 38f;

        // How close counts as "there" - kept small on purpose so the chase only stops once the
        // enemy's hurtbox actually overlaps the player's, not at some earlier "attack range".
        public float ChaseStopDistance { get; init; } = 8f;
        public float JumpSpeed { get; init; } = 220f;
    }
}
