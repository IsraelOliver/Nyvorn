using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueDebugRenderer
    {
        private readonly Texture2D pixel;

        public TissueDebugRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, TissueNetwork tissueNetwork, float revealStrength, Vector2 focusPosition, float revealRadius)
        {
            if (tissueNetwork == null || revealStrength <= 0.001f)
                return;

            foreach (TissueBranch branch in tissueNetwork.Branches)
            {
                Vector2 midpoint = branch.Points[branch.Points.Count / 2];
                float focusFalloff = GetFocusFalloff(midpoint, focusPosition, revealRadius);
                float alphaScale = MathHelper.Max(0.7f, revealStrength * focusFalloff);
                if (alphaScale <= 0.01f)
                    continue;

                Color branchColor = branch.IsPrimary
                    ? new Color(255, 80, 220) * (0.92f * alphaScale)
                    : new Color(80, 255, 180) * (0.78f * alphaScale);

                float thickness = branch.IsPrimary
                    ? 4.5f + branch.Thickness
                    : 2.5f + branch.Thickness;

                for (int i = 0; i < branch.Points.Count - 1; i++)
                    DrawLine(spriteBatch, branch.Points[i], branch.Points[i + 1], branchColor, thickness);
            }

            foreach (TissueNode node in tissueNetwork.Nodes)
            {
                float focusFalloff = GetFocusFalloff(node.Position, focusPosition, revealRadius);
                float alphaScale = MathHelper.Max(0.75f, revealStrength * focusFalloff);
                if (alphaScale <= 0.01f)
                    continue;

                float size = node.IsPrimary ? 10f : 6f;
                Color nodeColor = node.IsPrimary
                    ? new Color(255, 255, 120) * (0.95f * alphaScale)
                    : new Color(120, 255, 255) * (0.85f * alphaScale);

                DrawPoint(spriteBatch, node.Position, size, nodeColor);
            }
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;

            float rotation = System.MathF.Atan2(delta.Y, delta.X);
            Rectangle destination = new Rectangle(
                (int)start.X,
                (int)(start.Y - (thickness * 0.5f)),
                (int)length,
                (int)thickness);

            spriteBatch.Draw(pixel, destination, null, color, rotation, Vector2.Zero, SpriteEffects.None, 0f);
        }

        private void DrawPoint(SpriteBatch spriteBatch, Vector2 position, float size, Color color)
        {
            int radius = (int)System.MathF.Ceiling(size * 0.5f);
            Rectangle rectangle = new Rectangle(
                (int)position.X - radius,
                (int)position.Y - radius,
                radius * 2,
                radius * 2);

            spriteBatch.Draw(pixel, rectangle, color);
        }

        private float GetFocusFalloff(Vector2 worldPosition, Vector2 focusPosition, float revealRadius)
        {
            if (revealRadius <= 0f)
                return 1f;

            float distance = Vector2.Distance(worldPosition, focusPosition);
            float normalized = MathHelper.Clamp(distance / revealRadius, 0f, 1f);
            float strongNear = 1f - (normalized * normalized);
            return MathHelper.Lerp(0.18f, 1f, strongNear);
        }
    }
}
