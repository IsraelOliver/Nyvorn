using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Items;
using System;

namespace Nyvorn.Source.Gameplay.UI
{
    internal static class ItemIconLayout
    {
        public static Rectangle FitInside(ItemDefinition definition, Rectangle bounds)
        {
            Rectangle source = definition.SourceRectangle;
            if (source.Width <= 0 || source.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return bounds;

            float scale = Math.Min(
                (float)bounds.Width / source.Width,
                (float)bounds.Height / source.Height);

            int width = Math.Max(1, (int)MathF.Round(source.Width * scale));
            int height = Math.Max(1, (int)MathF.Round(source.Height * scale));
            int x = bounds.X + ((bounds.Width - width) / 2);
            int y = bounds.Y + ((bounds.Height - height) / 2);

            return new Rectangle(x, y, width, height);
        }
    }
}
