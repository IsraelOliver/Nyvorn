using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueFieldDebugRenderer
    {
        private readonly Texture2D pixel;

        public TissueFieldDebugRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, WorldMap worldMap, float revealStrength, Vector2 focusPosition, float revealRadius)
        {
            TissueField field = worldMap?.TissueField;
            if (field == null || revealStrength <= 0.001f || revealRadius <= 0f)
                return;

            Point centerTile = worldMap.WorldToTile(focusPosition);
            int radiusTiles = System.Math.Max(1, (int)System.MathF.Ceiling(revealRadius / worldMap.TileSize));
            int minY = System.Math.Max(0, centerTile.Y - radiusTiles);
            int maxY = System.Math.Min(worldMap.Height - 1, centerTile.Y + radiusTiles);
            int inset = System.Math.Clamp(TissueConfig.RevealVisual.TileInset, 0, worldMap.TileSize / 2);
            int size = System.Math.Max(1, worldMap.TileSize - (inset * 2));

            for (int y = minY; y <= maxY; y++)
            {
                for (int displayX = centerTile.X - radiusTiles; displayX <= centerTile.X + radiusTiles; displayX++)
                {
                    int fieldX = worldMap.WrapTileX(displayX);
                    TissueCellState state = field.GetState(fieldX, y);
                    if (!state.HasBiologicalPresence)
                        continue;

                    Vector2 tileCenter = worldMap.GetTileCenter(fieldX, y);
                    float distance = GetLoopAwareDistance(tileCenter, focusPosition, worldMap.PixelWidth);
                    if (distance > revealRadius)
                        continue;

                    float falloff = 1f - MathHelper.Clamp(distance / revealRadius, 0f, 1f);
                    float vitality = (state.Vitality * 0.7f) + (state.Flow * 0.3f);
                    Color color = Color.Lerp(
                        TissueConfig.RevealVisual.LowPresenceColor,
                        TissueConfig.RevealVisual.HighVitalityColor,
                        vitality);
                    color = Color.Lerp(color, TissueConfig.RevealVisual.CorruptionColor, state.Corruption);
                    color = Color.Lerp(
                        color,
                        TissueConfig.RevealVisual.MemoryColor,
                        state.MemoryDensity * TissueConfig.RevealVisual.MemoryColorWeight);

                    float biologicalAlpha = MathHelper.Lerp(
                        TissueConfig.RevealVisual.MinimumAlpha,
                        TissueConfig.RevealVisual.MaximumAlpha,
                        state.Presence * (0.35f + (state.Vitality * 0.65f)));
                    spriteBatch.Draw(
                        pixel,
                        new Rectangle(
                            (displayX * worldMap.TileSize) + inset,
                            (y * worldMap.TileSize) + inset,
                            size,
                            size),
                        color * (revealStrength * falloff * biologicalAlpha));
                }
            }
        }

        private static float GetLoopAwareDistance(Vector2 a, Vector2 b, float worldWidth)
        {
            float deltaX = a.X - b.X;
            if (deltaX > worldWidth * 0.5f)
                deltaX -= worldWidth;
            else if (deltaX < -worldWidth * 0.5f)
                deltaX += worldWidth;

            float deltaY = a.Y - b.Y;
            return System.MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }
    }
}
