namespace Nyvorn.Source.Gameplay.Combat
{
    public sealed class CombatConfig
    {
        public int PlayerAttackDamage { get; init; } = 20;
        public int EnemyContactDamage { get; init; } = 10;

        public float PlayerAttackKnockbackX { get; init; } = 150f;
        public float PlayerAttackKnockbackY { get; init; } = -65f;

        public float EnemyContactKnockbackX { get; init; } = 180f;
        public float EnemyContactKnockbackY { get; init; } = -75f;
    }
}
