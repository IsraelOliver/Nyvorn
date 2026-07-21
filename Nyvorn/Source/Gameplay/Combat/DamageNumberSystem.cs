using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Combat
{
    // Floating combat text: a short-lived number that spawns where a hit landed, drifts upward while
    // decelerating, and fades out. Same shape as BlockParticleSystem (Gameplay/World/Particles) - a
    // flat list of independent instances, ticked and drawn once a frame, capped so a flurry of hits
    // can't grow the list unbounded.
    public sealed class DamageNumberSystem
    {
        public static readonly Color EnemyHitColor = Color.White;
        public static readonly Color PlayerHitColor = new(255, 64, 64);

        private const int MaxNumbers = 64;
        private const float Lifetime = 0.75f;
        private const float RiseSpeed = 46f;
        private const float Drag = 0.05f;
        private const float TextScale = 0.65f;

        private readonly List<DamageNumberInstance> numbers = new();

        public void Spawn(Vector2 worldPosition, int damage, Color color)
        {
            if (damage <= 0)
                return;

            if (numbers.Count >= MaxNumbers)
                numbers.RemoveAt(0);

            float horizontalJitter = -10f + (System.Random.Shared.NextSingle() * 20f);
            numbers.Add(new DamageNumberInstance
            {
                Text = "-" + damage,
                Position = worldPosition + new Vector2(horizontalJitter, -16f),
                Velocity = new Vector2(horizontalJitter * 0.3f, -RiseSpeed),
                Color = color
            });
        }

        public void Update(float dt)
        {
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                DamageNumberInstance number = numbers[i];
                number.Age += dt;
                if (number.Age >= Lifetime)
                {
                    numbers.RemoveAt(i);
                    continue;
                }

                number.Position += number.Velocity * dt;
                number.Velocity *= System.MathF.Pow(Drag, dt);
            }
        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font)
        {
            for (int i = 0; i < numbers.Count; i++)
            {
                DamageNumberInstance number = numbers[i];
                float ageRatio = MathHelper.Clamp(number.Age / Lifetime, 0f, 1f);
                float alpha = 1f - (ageRatio * ageRatio);
                Vector2 origin = font.MeasureString(number.Text) * 0.5f;

                spriteBatch.DrawString(font, number.Text, number.Position + Vector2.One, Color.Black * alpha, 0f, origin, TextScale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(font, number.Text, number.Position, number.Color * alpha, 0f, origin, TextScale, SpriteEffects.None, 0f);
            }
        }

        private sealed class DamageNumberInstance
        {
            public string Text { get; init; }
            public Vector2 Position { get; set; }
            public Vector2 Velocity { get; set; }
            public Color Color { get; init; }
            public float Age { get; set; }
        }
    }
}
