using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public class ShortStick : Weapon
    {
        public ShortStick(Texture2D texture)
            : base(texture, frameW: 32, frameH: 32, pivot: new Point(10, 20))
        {
            SetIdle();
        }

        public override void SetIdle()
        {
            SetFrame(0, 1);
        }

        public override void SetWalk()
        {
            SetFrame(1, 1);
        }

        public override void SetAttackFrame(int frameIndex)
        {
            if (frameIndex < 0) frameIndex = 0;
            if (frameIndex > 2) frameIndex = 2;

            SetFrame(frameIndex, 0);
        }
    }
}