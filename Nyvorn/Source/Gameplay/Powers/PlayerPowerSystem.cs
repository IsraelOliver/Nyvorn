using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Powers
{
    public sealed class PlayerPowerSystem
    {
        private readonly List<Power> unlockedPowers = new();
        private int selectedPowerIndex;

        public IReadOnlyList<Power> UnlockedPowers => unlockedPowers;
        public Power CurrentPower => unlockedPowers.Count == 0 ? null : unlockedPowers[selectedPowerIndex];

        public void AddPower(Power power)
        {
            if (power != null)
                unlockedPowers.Add(power);
        }

        public void Update(float dt)
        {
            for (int i = 0; i < unlockedPowers.Count; i++)
                unlockedPowers[i].Update(dt);
        }

        public void CycleNextPower()
        {
            if (unlockedPowers.Count <= 1)
                return;

            selectedPowerIndex = (selectedPowerIndex + 1) % unlockedPowers.Count;
        }

        public bool TryActivateCurrentPower()
        {
            return CurrentPower?.TryActivate() ?? false;
        }
    }
}
