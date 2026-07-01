namespace Nyvorn.Source.Gameplay.World.Objects
{
    public readonly record struct WorldObjectMiningDefinition(bool IsMineable, float Hardness, int RequiredMiningPower);
}
