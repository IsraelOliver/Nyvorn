namespace Nyvorn.Source.World.Tissue
{
    public interface ITissuePropagationService
    {
        bool TryPropagate(
            TissuePropagationRequest request,
            out TissuePropagationResult result);
    }
}
