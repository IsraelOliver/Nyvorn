using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.World;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class SessionRuntimeContext
    {
        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required List<Enemy> Enemies { get; init; }
        public required List<WorldItem> WorldItems { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Inventory Inventory { get; init; }
        public required Camera2D Camera { get; init; }
    }
}
