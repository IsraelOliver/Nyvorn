using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Decorations
{
    public sealed record TreePartDefinition(
        TreePartType Type,
        Rectangle SourceRectangle,
        Point LogicalTileSize,
        Point DrawOffsetPixels,
        bool IsBase,
        bool IsRoot,
        bool IsBranch,
        bool CanReceiveBranch,
        bool CanContinueVertically,
        TreePartType? ComplementaryPartType = null);
}
