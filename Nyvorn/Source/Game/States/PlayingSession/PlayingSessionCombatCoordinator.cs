using Nyvorn.Source.Gameplay.Combat;
using Nyvorn.Source.Gameplay.Combat.Weapons;
using Nyvorn.Source.Gameplay.Items;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionCombatCoordinator
    {
        public required SessionRuntimeContext RuntimeContext { get; init; }
        public required Dictionary<ItemId, Weapon> Weapons { get; init; }
        public required CombatSystem CombatSystem { get; init; }

        public void SyncEquippedWeapon(int selectedHotbarIndex)
        {
            InventorySlot selectedSlot = RuntimeContext.Hotbar.GetSlot(selectedHotbarIndex);
            if (selectedSlot.IsEmpty || !Weapons.TryGetValue(selectedSlot.ItemId, out Weapon weapon))
                weapon = Weapons[ItemId.None];

            RuntimeContext.Player.SetEquippedWeapon(weapon);
        }

        public void ResolveCombat()
        {
            CombatSystem.Resolve(RuntimeContext.Player, RuntimeContext.Enemies);
        }
    }
}
