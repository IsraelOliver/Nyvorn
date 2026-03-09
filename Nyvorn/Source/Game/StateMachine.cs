using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Game.States;

namespace Nyvorn.Source.Game
{
    public class StateMachine
    {
        public IGameState CurrentState { get; private set; }

        public void ChangeState(IGameState newState)
        {
            if (newState == null || newState == CurrentState)
                return;

            CurrentState?.OnExit();
            CurrentState = newState;
            CurrentState.OnEnter();
        }

        public void Update(GameTime gameTime)
        {
            CurrentState?.Update(gameTime);
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            CurrentState?.Draw(gameTime, spriteBatch);
        }
    }
}
