namespace Nyvorn.Source.World.Generation
{
    public interface IWorldGenPass
    {
        string Name { get; }
        void Apply(WorldGenContext context);
    }
}
