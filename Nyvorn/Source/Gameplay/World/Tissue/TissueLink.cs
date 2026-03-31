using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueLink
    {
        public TissueLink(
            int startHubIndex,
            int endHubIndex,
            IReadOnlyList<Point> tilePath,
            float pathCost)
        {
            StartHubIndex = startHubIndex;
            EndHubIndex = endHubIndex;
            TilePath = tilePath;
            PathCost = pathCost;
        }

        public int StartHubIndex { get; }
        public int EndHubIndex { get; }
        public IReadOnlyList<Point> TilePath { get; }
        public float PathCost { get; }

        public enum TissueLinkType
        {
            Primary,
            Secondary,
            Weak
        }

        public TissueLinkType LinkType { get; private set; }

        public void SetType(TissueLinkType type)
        {
            LinkType = type;
        }
    }
}