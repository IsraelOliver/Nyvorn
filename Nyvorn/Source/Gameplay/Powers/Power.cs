using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Powers
{
    public abstract class Power
    {
        private float cooldownRemaining;

        protected Power(string name, Texture2D icon, float cooldown, float energyCost)
        {
            Name = name;
            Icon = icon;
            Cooldown = cooldown;
            EnergyCost = energyCost;
        }

        public string Name { get; }
        public Texture2D Icon { get; }
        public float Cooldown { get; }
        public float EnergyCost { get; }
        public float CooldownRemaining => cooldownRemaining;
        public float CooldownProgress => Cooldown <= 0f ? 0f : cooldownRemaining / Cooldown;
        public bool IsReady => cooldownRemaining <= 0f;

        public virtual bool CanActivate() => IsReady;

        public void Update(float dt)
        {
            if (cooldownRemaining > 0f)
                cooldownRemaining = System.MathF.Max(0f, cooldownRemaining - dt);

            OnUpdate(dt);
        }

        public bool TryActivate()
        {
            if (!CanActivate())
                return false;

            if (!ActivateCore())
                return false;

            cooldownRemaining = Cooldown;
            return true;
        }

        protected virtual void OnUpdate(float dt) { }

        protected abstract bool ActivateCore();
    }
}
