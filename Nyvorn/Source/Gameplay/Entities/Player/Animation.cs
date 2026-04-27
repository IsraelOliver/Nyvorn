using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class Animation
    {
        private readonly List<AnimFrame> frames;
        private readonly bool loop;
        private int currentFrame;
        private float timer;

        public Animation(IEnumerable<AnimFrame> frames, bool loop = true)
        {
            this.frames = new List<AnimFrame>(frames);
            if (this.frames.Count == 0)
                throw new ArgumentException("Animation precisa ter pelo menos um frame.", nameof(frames));

            this.loop = loop;
        }

        public int CurrentFrameIndex => currentFrame;
        public bool IsFinished => !loop && currentFrame >= frames.Count - 1;

        public void Update(float deltaTime)
        {
            if (frames.Count <= 1)
                return;

            timer += deltaTime;

            while (timer >= GetCurrentDuration())
            {
                timer -= GetCurrentDuration();

                if (loop)
                {
                    currentFrame = (currentFrame + 1) % frames.Count;
                }
                else
                {
                    currentFrame = Math.Min(currentFrame + 1, frames.Count - 1);
                    if (currentFrame == frames.Count - 1)
                    {
                        timer = 0f;
                        break;
                    }
                }
            }
        }

        public AnimFrame GetCurrentFrame()
        {
            return frames[currentFrame];
        }

        public void Reset()
        {
            currentFrame = 0;
            timer = 0f;
        }

        private float GetCurrentDuration()
        {
            return Math.Max(0.001f, frames[currentFrame].Duration);
        }
    }
}
