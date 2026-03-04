using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public class ShortStick : Weapon
    {
        public ShortStick(Texture2D texture)
            : base(texture, 32, 32, new Point(10, 20))
        {
            SetFrame(0, 1);
        }
    }
}