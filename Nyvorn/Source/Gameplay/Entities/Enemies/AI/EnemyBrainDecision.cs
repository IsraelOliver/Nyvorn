namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    public readonly struct EnemyBrainDecision
    {
        public EnemyBrainDecision(EnemyIntent intent, float moveVelocityX, bool triggerAttackVisual)
        {
            Intent = intent;
            MoveVelocityX = moveVelocityX;
            TriggerAttackVisual = triggerAttackVisual;
        }

        public EnemyIntent Intent { get; }
        public float MoveVelocityX { get; }
        public bool TriggerAttackVisual { get; }
    }
}
