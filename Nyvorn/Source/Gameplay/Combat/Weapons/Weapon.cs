using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public class Weapon
    {
        protected readonly Texture2D texture;
        protected readonly int frameW;
        protected readonly int frameH;
        protected readonly Point pivot;

        protected int frameX;
        protected int frameY;

        public Weapon(Texture2D texture, int frameW, int frameH, Point pivot)
        {
            this.texture = texture;
            this.frameW = frameW;
            this.frameH = frameH;
            this.pivot = pivot;

            frameX = 0;
            frameY = 0;
        }

        public virtual bool CanAttack => true;
        public virtual bool IsVisibleInHand => true;
        public virtual bool UsesAttackHandPose => false;
        public virtual bool UsesPlayerAttackUpperPose => false;
        public virtual bool DrawsWithPlayerRoot => false;
        public virtual bool ReplacesPlayerUpperBody => false;
        public virtual Texture2D PlayerUpperBodyTexture => texture;
        public virtual float? WorldBreakRangeOverride => null;
        public virtual int PowerTier => 1;
        public virtual ToolType ToolType => ToolType.None;
        public virtual int MiningPower => 0;
        public virtual float MiningSpeed => 0f;
        public virtual int HitDamage => 1;
        public virtual float HitKnockbackX => 80f;
        public virtual float HitKnockbackY => -35f;
        public virtual float AttackDuration => 0.3f;

        public virtual void SetIdle() { }
        public virtual void SetWalk() { }
        public virtual void SetAttackFrame(int frameIndex) { }
        public virtual bool IsActiveFrame(int frameIndex) => false;

        public virtual bool CanBreakTile(TileType tileType)
        {
            TileMiningDefinition miningDefinition = TileMiningDefinitions.Get(tileType);
            return MiningSpeed > 0f &&
                   miningDefinition.IsMineable &&
                   MiningPower >= miningDefinition.RequiredMiningPower;
        }

        public virtual AnimFrame GetPlayerUpperBodyFrame(
            AnimationState movementState,
            int movementFrameIndex,
            int attackFrameIndex,
            bool isAttacking)
        {
            return new AnimFrame(0, 0);
        }

        public virtual void UpdateAim(Vector2 handWorld, Vector2 mouseWorld)
        {
        }

        public void SetFrame(int x, int y)
        {
            frameX = x;
            frameY = y;
        }

        public virtual Rectangle GetAttackHitbox(Vector2 handWorld, bool facingRight)
        {
            return Rectangle.Empty;
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 handWorld, bool facingRight)
        {
            Rectangle src = new Rectangle(frameX * frameW, frameY * frameH, frameW, frameH);

            int pivotX = pivot.X;
            if (!facingRight)
                pivotX = (frameW - 1) - pivotX;

            Vector2 topLeft = handWorld - new Vector2(pivotX, pivot.Y);
            SpriteEffects fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            spriteBatch.Draw(texture, topLeft, src, Color.White, 0f, Vector2.Zero, 1f, fx, 0f);
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 handWorld, Vector2 playerRootPosition, AnimFrame movementFrame, bool facingRight)
        {
            Draw(spriteBatch, handWorld, facingRight);
        }
    }
}
