using System;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldLayerProfile
    {
        private readonly int[] surfaceHeights;
        private readonly WorldGenSettings settings;

        public WorldLayerProfile(int[] surfaceHeights, WorldGenSettings settings)
        {
            this.surfaceHeights = surfaceHeights ?? throw new ArgumentNullException(nameof(surfaceHeights));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public int GetSurfaceY(int tileX)
        {
            int wrappedX = WrapColumn(tileX, surfaceHeights.Length);
            return surfaceHeights[wrappedX];
        }

        public int GetDepthFromSurface(int tileX, int tileY)
        {
            return tileY - GetSurfaceY(tileX);
        }

        public WorldDepthLayer GetLayerAt(int tileX, int tileY)
        {
            int depth = GetDepthFromSurface(tileX, tileY);
            return settings.GetLayerForDepth(depth);
        }

        private static int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }
    }
}
