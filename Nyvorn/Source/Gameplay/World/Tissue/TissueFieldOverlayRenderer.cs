using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueFieldOverlayRenderer
    {
        private readonly Texture2D pixel;

        public TissueFieldOverlayRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(
            SpriteBatch spriteBatch,
            WorldMap worldMap,
            int startTileX,
            int endTileX,
            int startTileY,
            int endTileY)
        {
            TissueField field = worldMap?.TissueField;
            if (field == null)
                return;

            int minY = System.Math.Max(0, startTileY);
            int maxY = System.Math.Min(worldMap.Height - 1, endTileY);
            int inset = System.Math.Clamp(TissueConfig.FieldDebugVisual.TileInset, 0, worldMap.TileSize / 2);
            int size = System.Math.Max(1, worldMap.TileSize - (inset * 2));

            for (int tileY = minY; tileY <= maxY; tileY++)
            {
                for (int displayTileX = startTileX; displayTileX <= endTileX; displayTileX++)
                {
                    int fieldTileX = worldMap.WrapTileX(displayTileX);
                    TissueCellState state = field.GetState(fieldTileX, tileY);
                    if (!state.HasBiologicalPresence)
                        continue;

                    Color color = Color.Lerp(
                        TissueConfig.FieldDebugVisual.LowPresenceColor,
                        TissueConfig.FieldDebugVisual.HighPresenceColor,
                        state.Presence) * TissueConfig.FieldDebugVisual.Alpha;
                    spriteBatch.Draw(
                        pixel,
                        new Rectangle(
                            (displayTileX * worldMap.TileSize) + inset,
                            (tileY * worldMap.TileSize) + inset,
                            size,
                            size),
                        color);
                }
            }
        }
    }
}
