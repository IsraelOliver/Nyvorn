namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// A sky portal: a location where ambient light from the sky enters the cavern system.
    /// In this prototype, portals are manually placed by the debugger.
    /// </summary>
    public readonly struct V5SkyPortal
    {
        public int TileX { get; }
        public int TileY { get; }

        public V5SkyPortal(int tileX, int tileY)
        {
            TileX = tileX;
            TileY = tileY;
        }

        public override bool Equals(object obj)
        {
            if (obj is not V5SkyPortal other)
                return false;
            return TileX == other.TileX && TileY == other.TileY;
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(TileX, TileY);
        }

        public static bool operator ==(V5SkyPortal left, V5SkyPortal right) => left.Equals(right);
        public static bool operator !=(V5SkyPortal left, V5SkyPortal right) => !left.Equals(right);
    }
}
