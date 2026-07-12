using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerConfig
    {
        public static PlayerConfig Default { get; } = new PlayerConfig();

        public Point HurtboxSize { get; init; } = new Point(13, 23);
        public Point DodgeHurtboxSize { get; init; } = new Point(10, 15);
        public float MoveSpeed { get; init; } = 90f;
        public float JumpSpeed { get; init; } = 280f;
        public float GravityScale { get; init; } = 1f;
        public float WaterShallowThreshold { get; init; } = 0.10f;
        public float WaterPartialThreshold { get; init; } = 0.35f;
        public float WaterDeepThreshold { get; init; } = 0.70f;
        public float WaterHorizontalAcceleration { get; init; } = 500f;
        public float WaterVerticalAcceleration { get; init; } = 420f;
        public float WaterMaxHorizontalSpeed { get; init; } = 85f;
        public float WaterMaxVerticalSpeed { get; init; } = 75f;
        public float WaterDrag { get; init; } = 0.86f;
        public float PartialWaterDrag { get; init; } = 0.92f;
        public float WaterGravityMultiplier { get; init; } = 0.15f;
        public float PartialWaterGravityMultiplier { get; init; } = 0.35f;
        public float IdleSinkSpeed { get; init; } = 18f;
        public float IdleSinkAcceleration { get; init; } = 120f;
        public float WaterSurfaceJumpBoost { get; init; } = 170f;
        public float ShallowMoveMultiplier { get; init; } = 0.82f;
        // Ground horizontal acceleration - high enough that dry ground still snaps to full
        // speed within a frame or two (feels the same as the old instant-assignment). Wet
        // dirt/grass multiplies this down for a mushy, mud-like accel/decel.
        public float GroundAcceleration { get; init; } = 4500f;
        public float WetTractionMultiplier { get; init; } = 0.30f;
        public float ShallowJumpMultiplier { get; init; } = 0.85f;
        public float KnockbackRecovery { get; init; } = 12f;
        public float DodgeSpeed { get; init; } = 230f;
        public int DodgeFrames { get; init; } = 7;
        public float DodgeFrameTime { get; init; } = 0.05f;
        public float DodgeCooldown { get; init; } = 0.30f;
        public float HurtCooldown { get; init; } = 0.35f;
        public int MaxHealth { get; init; } = 100;
        public float WorldInteractionRange { get; init; } = 36f;
        public float FallDamageSafeVelocity { get; init; } = 420f;
        public float FallDamageFatalVelocity { get; init; } = 900f;
        public int FallDamageMax { get; init; } = 80;

        public float DodgeDuration => DodgeFrames * DodgeFrameTime;
    }
}
