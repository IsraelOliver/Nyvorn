using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Items
{
    public sealed class ItemDefinition
    {
        private Point worldPivot;

        public required ItemId Id { get; init; }
        public required string Name { get; init; }
        public required string TexturePath { get; init; }
        public required bool Stackable { get; init; }
        public required int MaxStack { get; init; }
        public required float GravityScale { get; init; }
        public required Point WorldSize { get; init; }
        public Point WorldPivot
        {
            get => WorldBaseAnchor.HasValue
                ? CreatePivotFromBaseAnchor(SourceFrameSize.Y, WorldBaseAnchor.Value)
                : worldPivot;
            init => worldPivot = value;
        }

        public Point? WorldBaseAnchor { get; init; }
        public required Point SpriteSheetCell { get; init; }
        public Point? FrameSize { get; init; }
        public required Rectangle WorldCollisionRect { get; init; }
        public EquipmentKind EquipmentKind { get; init; } = EquipmentKind.None;
        public int? MiningPower { get; init; }
        public float? MiningSpeed { get; init; }
        public int? PowerTier { get; init; }
        public int? HitDamage { get; init; }
        public float? HitKnockbackX { get; init; }
        public float? HitKnockbackY { get; init; }
        public float? UseSpeed { get; init; }

        public Point SourceFrameSize => FrameSize ?? WorldSize;

        public Rectangle SourceRectangle =>
            new Rectangle(
                SpriteSheetCell.X * SourceFrameSize.X,
                SpriteSheetCell.Y * SourceFrameSize.Y,
                SourceFrameSize.X,
                SourceFrameSize.Y);

        public static Point CreatePivotFromBaseAnchor(int frameHeight, Point baseAnchor)
        {
            return new Point(baseAnchor.X, frameHeight - baseAnchor.Y);
        }
    }
}
