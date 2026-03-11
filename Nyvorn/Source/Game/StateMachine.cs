using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Game.States;
using System.Collections.Generic;
using System;

namespace Nyvorn.Source.Game
{
    public class StateMachine
    {
        private readonly List<IGameState> stack = new();
        private readonly Queue<Action> pendingOperations = new();
        private bool isProcessing;

        public int Count => stack.Count;
        public IGameState CurrentState => stack.Count > 0 ? stack[stack.Count - 1] : null;

        public void PushState(IGameState state)
        {
            if (state == null)
                return;

            EnqueueOrRun(() =>
            {
                stack.Add(state);
                state.OnEnter();
            });
        }

        public void PopState()
        {
            EnqueueOrRun(() =>
            {
                if (stack.Count == 0)
                    return;

                IGameState state = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                state.OnExit();
            });
        }

        public void ReplaceState(IGameState state)
        {
            PopState();
            PushState(state);
        }

        public void Clear()
        {
            EnqueueOrRun(() =>
            {
                while (stack.Count > 0)
                {
                    IGameState state = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    state.OnExit();
                }
            });
        }

        public void Update(GameTime gameTime)
        {
            if (stack.Count == 0)
                return;

            isProcessing = true;
            int startIndex = stack.Count - 1;
            while (startIndex > 0 && stack[startIndex].UpdateBelow)
                startIndex--;

            for (int i = startIndex; i < stack.Count; i++)
                stack[i].Update(gameTime);

            isProcessing = false;
            FlushPendingOperations();
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            if (stack.Count == 0)
                return;

            isProcessing = true;
            int startIndex = stack.Count - 1;
            while (startIndex > 0 && stack[startIndex].DrawBelow)
                startIndex--;

            for (int i = startIndex; i < stack.Count; i++)
                stack[i].Draw(gameTime, spriteBatch);

            isProcessing = false;
            FlushPendingOperations();
        }

        private void EnqueueOrRun(Action operation)
        {
            if (isProcessing)
            {
                pendingOperations.Enqueue(operation);
                return;
            }

            operation();
        }

        private void FlushPendingOperations()
        {
            while (pendingOperations.Count > 0)
                pendingOperations.Dequeue().Invoke();
        }
    }
}
