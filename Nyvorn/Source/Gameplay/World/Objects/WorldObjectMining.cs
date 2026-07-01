using Nyvorn.Source.Gameplay.Entities.Player;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public static class WorldObjectMining
    {
        public static bool CanMine(Player player, WorldObjectMiningDefinition miningDefinition)
        {
            return player != null &&
                   player.MiningSpeed > 0f &&
                   miningDefinition.IsMineable &&
                   player.MiningPower >= miningDefinition.RequiredMiningPower;
        }

        public static float GetMiningDuration(float hardness, float miningSpeed, float minimumDurationSeconds)
        {
            float safeMiningSpeed = System.MathF.Max(0.001f, miningSpeed);
            return System.MathF.Max(minimumDurationSeconds, hardness / safeMiningSpeed);
        }
    }
}
