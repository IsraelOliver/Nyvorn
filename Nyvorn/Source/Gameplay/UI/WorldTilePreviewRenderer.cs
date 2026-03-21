using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.UI
{
    public enum WorldTilePreviewState
    {
        Hidden = 0,
        BreakValid = 1,
        BreakInvalid = 2,
        PlaceValid = 3,
        PlaceInvalid = 4
    }

    public sealed class WorldTilePreviewRenderer
    {
        private readonly Texture2D pixel;

        public WorldTilePreviewRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle tileBounds, WorldTilePreviewState state)
        {
            if (state == WorldTilePreviewState.Hidden)
                return;

            Color fill = GetFillColor(state);
            Color border = GetBorderColor(state);

            spriteBatch.Draw(pixel, tileBounds, fill);

            Rectangle top = new Rectangle(tileBounds.X, tileBounds.Y, tileBounds.Width, 1);
            Rectangle bottom = new Rectangle(tileBounds.X, tileBounds.Bottom - 1, tileBounds.Width, 1);
            Rectangle left = new Rectangle(tileBounds.X, tileBounds.Y, 1, tileBounds.Height);
            Rectangle right = new Rectangle(tileBounds.Right - 1, tileBounds.Y, 1, tileBounds.Height);

            spriteBatch.Draw(pixel, top, border);
            spriteBatch.Draw(pixel, bottom, border);
            spriteBatch.Draw(pixel, left, border);
            spriteBatch.Draw(pixel, right, border);
        }

        private static Color GetFillColor(WorldTilePreviewState state)
        {
            return state switch
            {
                WorldTilePreviewState.BreakValid => new Color(214, 165, 44, 70),
                WorldTilePreviewState.BreakInvalid => new Color(185, 52, 52, 70),
                WorldTilePreviewState.PlaceValid => new Color(61, 179, 103, 70),
                WorldTilePreviewState.PlaceInvalid => new Color(185, 52, 52, 70),
                _ => Color.Transparent
            };
        }

        private static Color GetBorderColor(WorldTilePreviewState state)
        {
            return state switch
            {
                WorldTilePreviewState.BreakValid => new Color(255, 214, 102),
                WorldTilePreviewState.BreakInvalid => new Color(255, 122, 122),
                WorldTilePreviewState.PlaceValid => new Color(153, 255, 173),
                WorldTilePreviewState.PlaceInvalid => new Color(255, 122, 122),
                _ => Color.Transparent
            };
        }
    }
}
