using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public sealed class HandWeapon : Weapon
    {
        public HandWeapon(Texture2D texture)
            : base(texture, 1, 1, Point.Zero)
        {
        }

        public override bool IsVisibleInHand => false;
        public override bool UsesAttackHandPose => true;

        public override bool IsActiveFrame(int frameIndex)
        {
            return frameIndex == 1 || frameIndex == 2;
        }

        public override Rectangle GetAttackHitbox(Vector2 handWorld, bool facingRight)
        {
            const int width = 8;
            const int height = 8;
            const int offset = 4;

            int x = facingRight
                ? (int)handWorld.X + offset
                : (int)handWorld.X - offset - width;

            int y = (int)handWorld.Y - (height / 2);
            return new Rectangle(x, y, width, height);
        }

        public override void Draw(SpriteBatch spriteBatch, Vector2 handWorld, bool facingRight)
        {
        }

        public override bool CanBreakTile(TileType tileType)
        {
            return tileType == TileType.Dirt || tileType == TileType.Sand;
        }
    }
}
