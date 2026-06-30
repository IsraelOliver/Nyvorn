namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IWorldObjectOccupancyProvider
    {
        bool IsObjectOccupyingTile(int tileX, int tileY);
    }
}
