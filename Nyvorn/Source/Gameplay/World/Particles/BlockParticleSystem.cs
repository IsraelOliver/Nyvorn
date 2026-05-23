using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Particles
{
    public sealed class BlockParticleSystem
    {
        private const int MaxParticles = 240;
        private const float Gravity = 230f;

        private readonly List<BlockParticle> particles = new();

        public required WorldMap WorldMap { get; init; }

        public void SpawnFromTile(TileType tileType, Point tile, bool background)
        {
            if (!WorldMap.TryGetTileParticleRenderData(tileType, tile.X, tile.Y, background, out Texture2D texture, out Rectangle source, out Color tint))
                return;

            int count = background ? 7 : 10;
            int wrappedTileX = WorldMap.WrapTileX(tile.X);
            Vector2 tileOrigin = new Vector2(wrappedTileX * WorldMap.TileSize, tile.Y * WorldMap.TileSize);
            float sourceScaleX = WorldMap.TileSize / (float)source.Width;
            float sourceScaleY = WorldMap.TileSize / (float)source.Height;

            for (int i = 0; i < count; i++)
            {
                int shardWidth = RandomShardSize(source.Width);
                int shardHeight = RandomShardSize(source.Height);
                int sourceOffsetX = System.Random.Shared.Next(0, System.Math.Max(1, source.Width - shardWidth + 1));
                int sourceOffsetY = System.Random.Shared.Next(0, System.Math.Max(1, source.Height - shardHeight + 1));
                Rectangle shardSource = new Rectangle(
                    source.X + sourceOffsetX,
                    source.Y + sourceOffsetY,
                    shardWidth,
                    shardHeight);

                Vector2 localPosition = new Vector2(
                    (sourceOffsetX + (shardWidth * 0.5f)) * sourceScaleX,
                    (sourceOffsetY + (shardHeight * 0.5f)) * sourceScaleY);

                float horizontalVelocity = -58f + (System.Random.Shared.NextSingle() * 116f);
                float verticalVelocity = -96f + (System.Random.Shared.NextSingle() * 58f);
                float speedScale = background ? 0.72f : 1f;

                AddParticle(new BlockParticle
                {
                    Texture = texture,
                    Source = shardSource,
                    Position = tileOrigin + localPosition,
                    Velocity = new Vector2(horizontalVelocity * speedScale, verticalVelocity * speedScale),
                    Tint = background ? tint * 0.86f : tint,
                    Lifetime = 0.42f + (System.Random.Shared.NextSingle() * 0.22f),
                    Rotation = -0.25f + (System.Random.Shared.NextSingle() * 0.5f),
                    AngularVelocity = -4.5f + (System.Random.Shared.NextSingle() * 9f),
                    Scale = 0.85f + (System.Random.Shared.NextSingle() * 0.45f)
                });
            }
        }

        public void Update(float dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                BlockParticle particle = particles[i];
                particle.Age += dt;
                if (particle.Age >= particle.Lifetime)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                particle.Velocity = new Vector2(
                    particle.Velocity.X * System.MathF.Pow(0.18f, dt),
                    particle.Velocity.Y + (Gravity * dt));
                particle.Position += particle.Velocity * dt;
                particle.Rotation += particle.AngularVelocity * dt;
                WrapParticleX(particle);
            }
        }

        public void Draw(SpriteBatch spriteBatch, float visibleLeft, float visibleTop, float visibleRight, float visibleBottom)
        {
            for (int i = 0; i < particles.Count; i++)
            {
                BlockParticle particle = particles[i];
                if (particle.Position.X < visibleLeft - 8f ||
                    particle.Position.X > visibleRight + 8f ||
                    particle.Position.Y < visibleTop - 8f ||
                    particle.Position.Y > visibleBottom + 8f)
                {
                    continue;
                }

                float ageRatio = MathHelper.Clamp(particle.Age / particle.Lifetime, 0f, 1f);
                float alpha = 1f - ageRatio;
                Vector2 origin = new Vector2(particle.Source.Width * 0.5f, particle.Source.Height * 0.5f);
                spriteBatch.Draw(
                    particle.Texture,
                    particle.Position,
                    particle.Source,
                    particle.Tint * alpha,
                    particle.Rotation,
                    origin,
                    particle.Scale,
                    SpriteEffects.None,
                    0f);
            }
        }

        private void AddParticle(BlockParticle particle)
        {
            if (particles.Count >= MaxParticles)
                particles.RemoveAt(0);

            particles.Add(particle);
        }

        private void WrapParticleX(BlockParticle particle)
        {
            int worldWidth = WorldMap.PixelWidth;
            if (worldWidth <= 0)
                return;

            if (particle.Position.X < 0f)
                particle.Position = new Vector2(particle.Position.X + worldWidth, particle.Position.Y);
            else if (particle.Position.X >= worldWidth)
                particle.Position = new Vector2(particle.Position.X - worldWidth, particle.Position.Y);
        }

        private static int RandomShardSize(int sourceSize)
        {
            int max = System.Math.Clamp(sourceSize, 2, 4);
            return System.Random.Shared.Next(2, max + 1);
        }

        private sealed class BlockParticle
        {
            public Texture2D Texture { get; init; }
            public Rectangle Source { get; init; }
            public Vector2 Position { get; set; }
            public Vector2 Velocity { get; set; }
            public Color Tint { get; init; }
            public float Lifetime { get; init; }
            public float Age { get; set; }
            public float Rotation { get; set; }
            public float AngularVelocity { get; init; }
            public float Scale { get; init; }
        }
    }
}
