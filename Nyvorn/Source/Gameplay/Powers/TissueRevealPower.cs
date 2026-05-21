using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Game.States;

namespace Nyvorn.Source.Gameplay.Powers
{
    public sealed class TissueRevealPower : Power
    {
        private readonly PlayingSessionTissueSystem tissueSystem;

        public TissueRevealPower(PlayingSessionTissueSystem tissueSystem, Texture2D icon = null)
            : base("Tissue Reveal", icon, cooldown: 1.1f, energyCost: 0f)
        {
            this.tissueSystem = tissueSystem;
        }

        protected override bool ActivateCore()
        {
            tissueSystem.TriggerReveal();
            return true;
        }
    }
}
