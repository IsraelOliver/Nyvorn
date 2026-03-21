using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Animator
    {
        private readonly Dictionary<AnimationState, Rectangle[]> _animations;
        private readonly Dictionary<AnimationState, float[]> _frameTimes;

        private AnimationState _state = AnimationState.Idle;
        private AnimationState _prevState = AnimationState.Idle;

        private int _frameIndex = 0;
        private float _timer = 0f;

        public float FrameTime { get; set; } = 0.08f;

        public AnimationState CurrentState => _state;
        public int FrameIndex => _frameIndex;

        public Animator(Dictionary<AnimationState, Rectangle[]> animations, AnimationState startState = AnimationState.Idle)
            : this(animations, null, startState)
        {
        }

        public Animator(Dictionary<AnimationState, Rectangle[]> animations, Dictionary<AnimationState, float[]> frameTimes, AnimationState startState = AnimationState.Idle)
        {
            _animations = animations;
            _frameTimes = frameTimes;
            _state = startState;
            _prevState = startState;
            _frameIndex = 0;
            _timer = 0f;
        }

        public void Play(AnimationState state)
        {
            _state = state;
        }

        public void Update(float dt)
        {
            if (!_animations.ContainsKey(_state))
                return;

            if (_state != _prevState) 
            {
                _frameIndex = 0;
                _timer = 0f;
                _prevState = _state;
            }

            Rectangle[] frames = _animations[_state];
            if (frames == null || frames.Length <= 1)
                return;

            _timer += dt;

            while (_timer >= GetCurrentFrameTime(frames.Length))
            {
                _timer -= GetCurrentFrameTime(frames.Length);
                _frameIndex++;

                // ter Loop por estado via AnimationClip para o futuro, mas por enquanto só o Walk tem loop
                if (_state == AnimationState.Walk)
                    _frameIndex %= frames.Length;
                else
                    _frameIndex = System.Math.Min(_frameIndex, frames.Length - 1);
            }
        }

        public Rectangle CurrentFrame
        {
            get
            {
                if (!_animations.ContainsKey(_state))
                    return Rectangle.Empty;

                Rectangle[] frames = _animations[_state];
                if (frames == null || frames.Length == 0)
                    return Rectangle.Empty;

                int safe = _frameIndex % frames.Length;
                return frames[safe];
            }
        }

        public bool IsFinished
        {
            get
            {
                if (!_animations.ContainsKey(_state)) return true;

                Rectangle[] frames = _animations[_state];
                if (frames == null || frames.Length == 0) return true;

                if (_state == AnimationState.Walk) return false;

                return _frameIndex >= frames.Length - 1;
            }
        }

        public void Reset()
        {
            _frameIndex = 0;
            _timer = 0f;
            _prevState = _state;
        }

        private float GetCurrentFrameTime(int frameCount)
        {
            if (_frameTimes != null &&
                _frameTimes.TryGetValue(_state, out float[] perFrameTimes) &&
                perFrameTimes != null &&
                perFrameTimes.Length == frameCount)
            {
                int safe = _frameIndex;
                if (safe < 0) safe = 0;
                if (safe >= perFrameTimes.Length) safe = perFrameTimes.Length - 1;
                return perFrameTimes[safe];
            }

            return FrameTime;
        }
    }
}
