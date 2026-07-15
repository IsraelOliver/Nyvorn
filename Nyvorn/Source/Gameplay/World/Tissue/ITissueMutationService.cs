namespace Nyvorn.Source.World.Tissue
{
    public interface ITissueMutationService
    {
        bool DamageTile(int tileX, int tileY, float amount);

        bool RestoreTile(int tileX, int tileY, float amount);

        bool AddCorruption(int tileX, int tileY, float amount);

        bool AddMemory(int tileX, int tileY, float amount);

        bool SetFlow(int tileX, int tileY, float value);

        bool RemoveTissue(int tileX, int tileY);

        bool ResetTile(int tileX, int tileY);
    }
}
