using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreeRenderer
    {
        private readonly TreePartAtlas atlas = new();

        private const int TreeCellPixelSize = TreePartAtlas.SmallPartPixelSize;

        public void Draw(
            SpriteBatch spriteBatch,
            Texture2D texture,
            WorldMap worldMap,
            int startTileX,
            int endTileX,
            int startTileY,
            int endTileY)
        {
            if (texture == null || worldMap.Trees.Count == 0)
                return;

            Rectangle visibleTiles = new(
                Math.Min(startTileX, endTileX) - 8,
                Math.Min(startTileY, endTileY) - 12,
                Math.Abs(endTileX - startTileX) + 17,
                Math.Abs(endTileY - startTileY) + 24);

            for (int i = 0; i < worldMap.Trees.Count; i++)
            {
                TreeInstance tree = worldMap.Trees[i];

                if (!visibleTiles.Contains(tree.BaseTile))
                    continue;

                DrawTree(spriteBatch, texture, worldMap, tree);
            }
        }

        private void DrawTree(SpriteBatch spriteBatch, Texture2D texture, WorldMap worldMap, TreeInstance tree)
        {
            for (int i = 0; i < tree.Parts.Count; i++)
            {
                TreePartPlacement placement = tree.Parts[i];
                Rectangle source = atlas.GetSourceRectangle(tree, placement.PartType, i);

                Rectangle destination = GetPixelPerfectDestination(
                    worldMap,
                    tree.BaseTile,
                    placement.OffsetTiles,
                    source.Width,
                    source.Height);

                spriteBatch.Draw(texture, destination, source, Color.White);
            }

            TreePartDefinition canopy = atlas.Get(TreePartType.Canopy);

            Rectangle canopyDestination = GetPixelPerfectDestination(
                worldMap,
                tree.BaseTile,
                tree.Canopy.OffsetTiles,
                canopy.SourceRectangle.Width,
                canopy.SourceRectangle.Height);

            spriteBatch.Draw(texture, canopyDestination, canopy.SourceRectangle, Color.White);
        }

        private static Rectangle GetPixelPerfectDestination(
            WorldMap worldMap,
            Point baseTile,
            Point offsetCells,
            int sourceWidth,
            int sourceHeight)
        {
            int basePixelX = baseTile.X * worldMap.TileSize;
            int basePixelY = baseTile.Y * worldMap.TileSize;

            // Centraliza a árvore no tile de base, porque o tile do mundo é 8px e a peça da árvore é 10px.
            int centerOffsetX = (worldMap.TileSize - TreeCellPixelSize) / 2;

            int x = basePixelX + centerOffsetX + offsetCells.X * TreeCellPixelSize;
            int y = basePixelY + offsetCells.Y * TreeCellPixelSize;

            return new Rectangle(
                x,
                y,
                sourceWidth,
                sourceHeight);
        }
    }
}