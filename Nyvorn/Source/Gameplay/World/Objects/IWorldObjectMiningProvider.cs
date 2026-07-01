using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IWorldObjectMiningProvider
    {
        bool TryGetMiningTargetAtTile(Point tile, out WorldObjectMiningTarget target);
        bool TryMineObjectAtTile(Point tile, WorldItemRuntimeSystem worldItemRuntimeSystem);
    }
}
