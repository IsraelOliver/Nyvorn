namespace Nyvorn.Source.Gameplay.World.Objects
{
    public interface IWorldObjectMovementBlocker
    {
        bool IsMovementBlockingTile(int tileX, int tileY);
    }
}
