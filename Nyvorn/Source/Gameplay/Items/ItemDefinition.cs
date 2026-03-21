using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Items
{
    public sealed class ItemDefinition
    {
        public required ItemId Id { get; init; }
        public required string Name { get; init; }
        public required string TexturePath { get; init; }
        public required bool Stackable { get; init; }
        public required int MaxStack { get; init; }
        public required float GravityScale { get; init; }
        public required Point WorldSize { get; init; }
        public required Point WorldPivot { get; init; }
        public required Point SpriteSheetCell { get; init; }
        public required Rectangle WorldCollisionRect { get; init; }

        public Rectangle SourceRectangle =>
            new Rectangle(
                SpriteSheetCell.X * WorldSize.X,
                SpriteSheetCell.Y * WorldSize.Y,
                WorldSize.X,
                WorldSize.Y);
    }
}
