using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyAnimator
    {
        private readonly Dictionary<EnemyAnimState, EnemyAnimationClip> clips;

        private EnemyAnimState state;
        private EnemyAnimState prevState;
        private int frameIndex;
        private float timer;

        public EnemyAnimState CurrentState => state;

        public EnemyAnimator(Dictionary<EnemyAnimState, EnemyAnimationClip> clips, EnemyAnimState startState = EnemyAnimState.Idle)
        {
            this.clips = clips;
            state = startState;
            prevState = startState;
            frameIndex = 0;
            timer = 0f;
        }

        public void Play(EnemyAnimState nextState)
        {
            state = nextState;
        }

        public void Reset()
        {
            frameIndex = 0;
            timer = 0f;
            prevState = state;
        }

        public void Update(float dt)
        {
            if (!clips.TryGetValue(state, out EnemyAnimationClip clip))
                return;

            if (state != prevState)
            {
                frameIndex = 0;
                timer = 0f;
                prevState = state;
            }

            if (clip.Frames == null || clip.Frames.Length <= 1)
                return;

            timer += dt;
            while (timer >= clip.FrameTime)
            {
                timer -= clip.FrameTime;
                frameIndex++;

                if (clip.Loop)
                    frameIndex %= clip.Frames.Length;
                else if (frameIndex >= clip.Frames.Length)
                    frameIndex = clip.Frames.Length - 1;
            }
        }

        public Rectangle CurrentFrame
        {
            get
            {
                if (!clips.TryGetValue(state, out EnemyAnimationClip clip))
                    return Rectangle.Empty;

                if (clip.Frames == null || clip.Frames.Length == 0)
                    return Rectangle.Empty;

                int safe = frameIndex;
                if (safe < 0) safe = 0;
                if (safe >= clip.Frames.Length) safe = clip.Frames.Length - 1;
                return clip.Frames[safe];
            }
        }
    }

    public sealed class EnemyAnimationClip
    {
        public Rectangle[] Frames { get; }
        public float FrameTime { get; }
        public bool Loop { get; }

        public EnemyAnimationClip(Rectangle[] frames, float frameTime, bool loop)
        {
            Frames = frames;
            FrameTime = frameTime;
            Loop = loop;
        }
    }
}
