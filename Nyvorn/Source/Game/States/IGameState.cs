using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Game.States
{
    public interface IGameState
    {
        bool UpdateBelow { get; }
        bool DrawBelow { get; }
        bool BlockInputBelow { get; }

        void OnEnter();
        void OnExit();
        void Update(GameTime gameTime);
        void Draw(GameTime gameTime, SpriteBatch spriteBatch);
    }
}
