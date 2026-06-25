using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public sealed class Axe : Weapon
    {
        private readonly Texture2D playerUpperBodyMoveset;
        private readonly int powerTier;
        private readonly int hitDamage;
        private readonly float hitKnockbackX;
        private readonly float hitKnockbackY;
        private readonly float useSpeed;

        public Axe(
            Texture2D texture,
            Texture2D playerUpperBodyMoveset,
            int powerTier = 1,
            int hitDamage = 6,
            float hitKnockbackX = 170f,
            float hitKnockbackY = -40f,
            float useSpeed = 1f)
            : base(texture, frameW: 32, frameH: 33, pivot: CreatePivotFromBaseAnchor(33, new Point(9, 13)))
        {
            this.playerUpperBodyMoveset = playerUpperBodyMoveset ??
                throw new System.ArgumentNullException(nameof(playerUpperBodyMoveset));
            this.powerTier = powerTier;
            this.hitDamage = hitDamage;
            this.hitKnockbackX = hitKnockbackX;
            this.hitKnockbackY = hitKnockbackY;
            this.useSpeed = useSpeed;
            SetIdle();
        }

        public override bool UsesAttackHandPose => true;
        public override bool UsesPlayerAttackUpperPose => true;
        public override bool ReplacesPlayerUpperBody => true;
        public override Texture2D PlayerUpperBodyTexture => playerUpperBodyMoveset;
        public override bool DrawsPlayerUpperBodyOverlay => true;
        public override Texture2D PlayerUpperBodyOverlayTexture => texture;
        public override float? WorldBreakRangeOverride => 56f;
        public override int PowerTier => powerTier;
        public override ToolType ToolType => ToolType.Axe;
        public override int HitDamage => hitDamage;
        public override float HitKnockbackX => hitKnockbackX;
        public override float HitKnockbackY => hitKnockbackY;
        public override float UseSpeed => useSpeed;

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
            int width = 20;
            int height = 16;
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
