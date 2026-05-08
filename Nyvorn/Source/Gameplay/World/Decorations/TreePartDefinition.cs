using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Decorations
{
    public sealed record TreePartDefinition(
        TreePartType Type,
        Rectangle SourceRectangle,
        Point TileSize,
        bool IsBase,
        bool IsRoot,
        bool IsBranch,
        bool CanReceiveBranch,
        bool CanContinueVertically,
        TreePartType? ComplementaryPartType = null);
}
