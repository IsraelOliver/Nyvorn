using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public class shortStick : Weapon
    {
        public shortStick(Texture2D texture)
            : base(texture, 32, 32, new Point(10, 20))
        {
            SetFrame(3);
        }
    }
}