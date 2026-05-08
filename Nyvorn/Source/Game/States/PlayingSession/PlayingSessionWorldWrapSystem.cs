using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionWorldWrapSystem
    {
        public required SessionRuntimeContext RuntimeContext { get; init; }

        public Vector2 NormalizePlayerAndMouse(Vector2 mouseWorld)
        {
            float worldWidth = RuntimeContext.WorldMap.PixelWidth;
            float wrapDelta = 0f;

            if (RuntimeContext.Player.Position.X < 0f)
                wrapDelta = worldWidth;
            else if (RuntimeContext.Player.Position.X >= worldWidth)
                wrapDelta = -worldWidth;

            if (wrapDelta == 0f)
                return mouseWorld;

            RuntimeContext.Player.ShiftX(wrapDelta);
            RuntimeContext.Camera.ShiftX(wrapDelta);

            return new Vector2(mouseWorld.X + wrapDelta, mouseWorld.Y);
        }
    }
}
