using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
            return frameIndex == 1 || frameIndex == 2;
        }

        public override bool CanBreakTile(TileType tileType)
        {
            return tileType == TileType.Dirt
                || tileType == TileType.Grass
                || tileType == TileType.Sand
                || tileType == TileType.Stone;
        }
    }
}
