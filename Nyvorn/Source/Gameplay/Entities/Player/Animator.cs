using Microsoft.Xna.Framework;
using System.Collections.Generic;

// esse arquivo é a "máquina de estados" que controla qual frame desenhar baseado no estado atual do player (Idle, Walk, Jump, Fall).
// O PlayerAnimations.cs é o "banco de dados" das animações do player, onde cada estado tem um array de frames (Rectangles) associados a ele.

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Animator
    {
        private readonly Dictionary<AnimationState, Rectangle[]> _animations;

        private AnimationState _state = AnimationState.Idle;
        private AnimationState _prevState = AnimationState.Idle;

        private int _frameIndex = 0;
        private float _timer = 0f;

        public float FrameTime { get; set; } = 0.08f;

        public Animator(Dictionary<AnimationState, Rectangle[]> animations, AnimationState startState = AnimationState.Idle)
        {
            _animations = animations;
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

            _timer += dt; // essa linha faz o timer acumular o tempo desde a última troca de frame

            while (_timer >= FrameTime)
            {
                _timer -= FrameTime;
                _frameIndex++;

                // Evolução natural: ter Loop por estado via AnimationClip.
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

        public void Reset()
        {
            _frameIndex = 0;
            _timer = 0f;
            _prevState = _state;
        }
    }
}
