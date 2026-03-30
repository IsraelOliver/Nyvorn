using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Animator
    {
        private readonly Dictionary<AnimationState, Rectangle[]> animations;
        private readonly Dictionary<AnimationState, float[]> frameTimes;
        private AnimationState state = AnimationState.Idle;
        private AnimationState previousState = AnimationState.Idle;
        private int frameIndex;
        private float timer;

        public Animator(Dictionary<AnimationState, Rectangle[]> animations, AnimationState startState = AnimationState.Idle)
            : this(animations, null, startState)
        {
        }

        public Animator(
            Dictionary<AnimationState, Rectangle[]> animations,
            Dictionary<AnimationState, float[]> frameTimes,
            AnimationState startState = AnimationState.Idle)
        {
            this.animations = animations;
            this.frameTimes = frameTimes;
            state = startState;
            previousState = startState;
        }

        public float FrameTime { get; set; } = 0.08f;
        public AnimationState CurrentState => state;
        public int FrameIndex => frameIndex;

        public Rectangle CurrentFrame
        {
            get
            {
                if (!animations.TryGetValue(state, out Rectangle[] frames) || frames == null || frames.Length == 0)
                    return Rectangle.Empty;

                return frames[frameIndex % frames.Length];
            }
        }

        public bool IsFinished
        {
            get
            {
                if (!animations.TryGetValue(state, out Rectangle[] frames) || frames == null || frames.Length == 0)
                    return true;

                if (state == AnimationState.Walk)
                    return false;

                return frameIndex >= frames.Length - 1;
            }
        }

        public void Play(AnimationState nextState)
        {
            state = nextState;
        }

        public void Update(float dt)
        {
            if (!animations.TryGetValue(state, out Rectangle[] frames) || frames == null || frames.Length <= 1)
                return;

            if (state != previousState)
            {
                frameIndex = 0;
                timer = 0f;
                previousState = state;
            }

            timer += dt;

            while (true)
            {
                float currentFrameTime = GetCurrentFrameTime(frames.Length);
                if (timer < currentFrameTime)
                    break;

                timer -= currentFrameTime;
                frameIndex++;

                if (state == AnimationState.Walk)
                    frameIndex %= frames.Length;
                else
                    frameIndex = System.Math.Min(frameIndex, frames.Length - 1);
            }
        }

        public void Reset()
        {
            frameIndex = 0;
            timer = 0f;
            previousState = state;
        }

        private float GetCurrentFrameTime(int frameCount)
        {
            if (frameTimes != null &&
                frameTimes.TryGetValue(state, out float[] perFrameTimes) &&
                perFrameTimes != null &&
                perFrameTimes.Length == frameCount)
            {
                int safeIndex = frameIndex;
                if (safeIndex < 0)
                    safeIndex = 0;
                if (safeIndex >= perFrameTimes.Length)
                    safeIndex = perFrameTimes.Length - 1;

                return perFrameTimes[safeIndex];
            }

            return FrameTime;
        }
    }
}
