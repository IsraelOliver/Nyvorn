using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerConfig
    {
        public static PlayerConfig Default { get; } = new PlayerConfig();

        public Point HurtboxSize { get; init; } = new Point(10, 23);
        public Point DodgeHurtboxSize { get; init; } = new Point(10, 15);
        public float MoveSpeed { get; init; } = 90f;
        public float JumpSpeed { get; init; } = 280f;
        public float GravityScale { get; init; } = 1f;
        public float KnockbackRecovery { get; init; } = 12f;
        public float DodgeSpeed { get; init; } = 230f;
        public float AttackDuration { get; init; } = 0.3f;
        public int DodgeFrames { get; init; } = 7;
        public float DodgeFrameTime { get; init; } = 0.05f;
        public float DodgeCooldown { get; init; } = 0.30f;
        public float HurtCooldown { get; init; } = 0.35f;
        public int MaxHealth { get; init; } = 100;
        public float WorldInteractionRange { get; init; } = 36f;

        public float DodgeDuration => DodgeFrames * DodgeFrameTime;
    }
}
