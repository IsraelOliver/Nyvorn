using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionInputRouter
    {
        public required Hotbar Hotbar { get; init; }

        public int SelectedHotbarIndex { get; private set; }

        public InputState RouteFrameInput(InputState input)
        {
            UpdateSelectedHotbarSlot(input);
            return ShouldReserveAttackForBlockInteraction()
                ? CreateBlockInteractionInput(input)
                : input;
        }

        public void SetSelectedHotbarIndex(int index)
        {
            SelectedHotbarIndex = Math.Clamp(index, 0, Hotbar.Capacity - 1);
        }

        private void UpdateSelectedHotbarSlot(InputState input)
        {
            if (input.HotbarSelectionIndex >= 0 && input.HotbarSelectionIndex < Hotbar.Capacity)
            {
                SelectedHotbarIndex = input.HotbarSelectionIndex;
                return;
            }

            if (input.MouseWheelDelta == 0)
                return;

            int direction = input.MouseWheelDelta > 0 ? -1 : 1;
            SelectedHotbarIndex = (SelectedHotbarIndex + direction + Hotbar.Capacity) % Hotbar.Capacity;
        }

        private bool ShouldReserveAttackForBlockInteraction()
        {
            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (selectedSlot.IsEmpty)
                return false;

            return selectedSlot.ItemId == ItemId.SandBlock ||
                   selectedSlot.ItemId == ItemId.Workbench ||
                   TileItemMapper.TryGetTileType(selectedSlot.ItemId, out _);
        }

        private static InputState CreateBlockInteractionInput(InputState input)
        {
            return new InputState(
                input.MoveDir,
                input.JumpPressed,
                false,
                false,
                input.PlacePressed,
                input.ActivePowerPressed,
                input.ActivePowerJustPressed,
                input.TogglePlayerHubPressed,
                input.ToggleMapPressed,
                input.InteractPressed,
                input.CyclePowerPressed,
                input.ToggleDebugPressed,
                input.CancelPressed,
                input.HotbarSelectionIndex,
                input.DodgePressed,
                input.DodgeDir,
                input.MouseScreenPosition,
                input.MouseWheelDelta);
        }
    }
}
