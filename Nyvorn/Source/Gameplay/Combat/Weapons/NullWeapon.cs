using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public sealed class NullWeapon : Weapon
    {
        public NullWeapon(Texture2D texture)
            : base(texture, 1, 1, Point.Zero)
        {
        }

        public override bool CanAttack => false;
        public override bool IsVisibleInHand => false;

        public override void Draw(SpriteBatch spriteBatch, Vector2 handWorld, bool facingRight)
        {
        }
    }
}
