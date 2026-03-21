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
        public int FrameWidth { get; init; } = 32;
        public int FrameHeight { get; init; } = 32;
    }
}
