using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueBiomeRegion
    {
        public TissueBiomeRegion(
            int regionId,
            TissueBiomeType biomeType,
            Rectangle tileBounds,
            Point centerTile,
            WorldLayerType anchorLayer)
        {
            RegionId = regionId;
            BiomeType = biomeType;
            TileBounds = tileBounds;
            CenterTile = centerTile;
            AnchorLayer = anchorLayer;
        }

        public int RegionId { get; }
        public TissueBiomeType BiomeType { get; }
        public Rectangle TileBounds { get; }
        public Point CenterTile { get; }
        public WorldLayerType AnchorLayer { get; }
    }
}
