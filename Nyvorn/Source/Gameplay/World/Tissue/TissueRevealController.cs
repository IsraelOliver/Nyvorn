using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueRevealController
    {
        private readonly float fadeDuration;
        private readonly float activeDuration;
        private readonly float waveDuration;

        private float revealTimer;
        private float waveTimer;

        public TissueRevealController(float revealRadius, float fadeDuration = 0.22f, float activeDuration = 2.2f, float waveDuration = 0.6f)
        {
            RevealRadius = revealRadius;
            this.fadeDuration = fadeDuration;
            this.activeDuration = activeDuration;
            this.waveDuration = waveDuration;
            FocusPosition = Vector2.Zero;
        }

        public float CurrentStrength { get; private set; }
        public float RevealRadius { get; }
        public Vector2 FocusPosition { get; private set; }
        public float WaveProgress { get; private set; }
        public bool IsActive => CurrentStrength > 0.001f;

        public void Update(float dt, InputState input, Vector2 focusPosition)
        {
            FocusPosition = focusPosition;

            if (input.TissueRevealPressed)
            {
                revealTimer = activeDuration;
                waveTimer = 0f;
            }

            if (revealTimer > 0f)
                revealTimer = Math.Max(0f, revealTimer - dt);

            if (waveTimer < waveDuration)
                waveTimer = Math.Min(waveDuration, waveTimer + dt);

            if (revealTimer <= 0f)
            {
                CurrentStrength = 0f;
                WaveProgress = 1f;
                return;
            }

            CurrentStrength = revealTimer >= fadeDuration || fadeDuration <= 0f
                ? 1f
                : MathHelper.Clamp(revealTimer / fadeDuration, 0f, 1f);

            WaveProgress = waveDuration <= 0f
                ? 1f
                : MathHelper.Clamp(waveTimer / waveDuration, 0f, 1f);
        }
    }
}
