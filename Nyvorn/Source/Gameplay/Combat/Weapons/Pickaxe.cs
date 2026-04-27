using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public sealed class Pickaxe : Weapon
    {
        public Pickaxe(Texture2D texture)
            : base(texture, frameW: 32, frameH: 32, pivot: new Point(9, 19))
        {
            SetIdle();
        }

        public override bool UsesAttackHandPose => true;
        public override bool UsesPlayerAttackUpperPose => true;
        public override bool ReplacesPlayerUpperBody => true;
        public override float? WorldBreakRangeOverride => 56f;
        public override int PowerTier => 1;
        public override int HitDamage => 8;
        public override float HitKnockbackX => 190f;
        public override float HitKnockbackY => -45f;
        public override float AttackDuration => 0.3f;

        public override void SetIdle()
        {
            SetFrame(0, 2);
        }

        public override void SetWalk()
        {
            SetFrame(0, 0);
        }

        public override void SetAttackFrame(int frameIndex)
        {
            if (frameIndex < 0) frameIndex = 0;
            if (frameIndex > 3) frameIndex = 3;

            SetFrame(frameIndex, 1);
        }

        public override AnimFrame GetPlayerUpperBodyFrame(
            AnimationState movementState,
            int movementFrameIndex,
            int attackFrameIndex,
            bool isAttacking)
        {
            if (isAttacking)
                return new AnimFrame(row: 1, col: Clamp(attackFrameIndex, 0, 3));

            switch (movementState)
            {
                case AnimationState.Walk:
                    return new AnimFrame(row: 0, col: Clamp(movementFrameIndex, 0, 5));

                case AnimationState.Jump:
                    return new AnimFrame(row: 2, col: 1);

                case AnimationState.Fall:
                    return new AnimFrame(row: 2, col: 2);

                case AnimationState.Idle:
                default:
                    return new AnimFrame(row: 2, col: 0);
            }
        }

        public override Rectangle GetAttackHitbox(Vector2 handWorld, bool facingRight)
        {
            int width = 18;
            int height = 14;
            int offset = 4;

            int x = facingRight
                ? (int)handWorld.X + offset
                : (int)handWorld.X - offset - width;

            int y = (int)handWorld.Y - (height / 2);
            return new Rectangle(x, y, width, height);
        }

        public override bool IsActiveFrame(int frameIndex)
        {
            return frameIndex == 2;
        }

        public override bool CanBreakTile(TileType tileType)
        {
            return tileType == TileType.Dirt
                || tileType == TileType.Grass
                || tileType == TileType.Sand
                || tileType == TileType.Stone;
        }

        public override void Draw(SpriteBatch spriteBatch, Vector2 handWorld, Vector2 playerRootPosition, AnimFrame movementFrame, bool facingRight)
        {
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
