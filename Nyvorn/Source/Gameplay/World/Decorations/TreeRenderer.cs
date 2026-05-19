using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreeRenderer
    {
        private readonly TreePartAtlas atlas = new();

        public void Draw(
            SpriteBatch spriteBatch,
            Texture2D texture,
            WorldMap worldMap,
            int startTileX,
            int endTileX,
            int startTileY,
            int endTileY,
            TreeRenderLayer layer)
        {
            if (texture == null || worldMap.Trees.Count == 0)
                return;

            Rectangle visibleTiles = new(
                Math.Min(startTileX, endTileX) - 8,
                Math.Min(startTileY, endTileY) - 16,
                Math.Abs(endTileX - startTileX) + 17,
                Math.Abs(endTileY - startTileY) + 32);

            Rectangle visiblePixels = new(
                visibleTiles.Left * worldMap.TileSize,
                visibleTiles.Top * worldMap.TileSize,
                visibleTiles.Width * worldMap.TileSize,
                visibleTiles.Height * worldMap.TileSize);

            for (int i = 0; i < worldMap.Trees.Count; i++)
            {
                TreeInstance tree = worldMap.Trees[i];

                if (GetTreeRenderLayer(tree) != layer)
                    continue;

                if (!GetTreePixelBounds(worldMap, tree).Intersects(visiblePixels))
                    continue;

                DrawTree(spriteBatch, texture, worldMap, tree);
            }
        }

        private static TreeRenderLayer GetTreeRenderLayer(TreeInstance tree)
        {
            unchecked
            {
                uint hash = (uint)tree.Seed;
                hash ^= (uint)(tree.BaseTile.X * 73856093);
                hash ^= (uint)(tree.BaseTile.Y * 19349663);
                return hash % 4 == 0 ? TreeRenderLayer.Front : TreeRenderLayer.Back;
            }
        }

        private void DrawTree(SpriteBatch spriteBatch, Texture2D texture, WorldMap worldMap, TreeInstance tree)
        {
            for (int i = 0; i < tree.Parts.Count; i++)
            {
                TreePartPlacement placement = tree.Parts[i];
                TreePartDefinition definition = atlas.Get(placement.PartType);
                Rectangle source = atlas.GetSourceRectangle(tree, placement.PartType, i);

                Rectangle destination = GetPixelPerfectDestination(
                    worldMap,
                    tree.BaseTile,
                    placement.OffsetTiles,
                    definition.DrawOffsetPixels,
                    source.Width,
                    source.Height);

                spriteBatch.Draw(texture, destination, source, Color.White);
            }

            TreePartDefinition canopy = atlas.Get(TreePartType.Canopy);

            Rectangle canopyDestination = GetPixelPerfectDestination(
                worldMap,
                tree.BaseTile,
                tree.Canopy.OffsetTiles,
                canopy.DrawOffsetPixels,
                canopy.SourceRectangle.Width,
                canopy.SourceRectangle.Height);

            spriteBatch.Draw(texture, canopyDestination, canopy.SourceRectangle, Color.White);
        }

        private Rectangle GetTreePixelBounds(WorldMap worldMap, TreeInstance tree)
        {
            Rectangle bounds = Rectangle.Empty;
            bool hasBounds = false;

            for (int i = 0; i < tree.Parts.Count; i++)
            {
                TreePartPlacement placement = tree.Parts[i];
                TreePartDefinition definition = atlas.Get(placement.PartType);
                Rectangle source = atlas.GetSourceRectangle(tree, placement.PartType, i);
                Rectangle destination = GetPixelPerfectDestination(
                    worldMap,
                    tree.BaseTile,
                    placement.OffsetTiles,
                    definition.DrawOffsetPixels,
                    source.Width,
                    source.Height);

                bounds = hasBounds ? Rectangle.Union(bounds, destination) : destination;
                hasBounds = true;
            }

            TreePartDefinition canopy = atlas.Get(TreePartType.Canopy);
            Rectangle canopyDestination = GetPixelPerfectDestination(
                worldMap,
                tree.BaseTile,
                tree.Canopy.OffsetTiles,
                canopy.DrawOffsetPixels,
                canopy.SourceRectangle.Width,
                canopy.SourceRectangle.Height);

            return hasBounds ? Rectangle.Union(bounds, canopyDestination) : canopyDestination;
        }

        private static Rectangle GetPixelPerfectDestination(
            WorldMap worldMap,
            Point baseTile,
            Point offsetTiles,
            Point drawOffsetPixels,
            int sourceWidth,
            int sourceHeight)
        {
            int anchorX = (baseTile.X + offsetTiles.X) * worldMap.TileSize;
            int anchorY = (baseTile.Y + offsetTiles.Y) * worldMap.TileSize;

            return new Rectangle(
                anchorX + drawOffsetPixels.X,
                anchorY + drawOffsetPixels.Y,
                sourceWidth,
                sourceHeight);
        }
    }
}
