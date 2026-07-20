namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    public readonly struct EnemyBrainDecision
    {
        public EnemyBrainDecision(EnemyIntent intent, float moveVelocityX, bool triggerAttackVisual, bool wantsJump = false)
        {
            Intent = intent;
            MoveVelocityX = moveVelocityX;
            TriggerAttackVisual = triggerAttackVisual;
            WantsJump = wantsJump;
        }

        public EnemyIntent Intent { get; }
        public float MoveVelocityX { get; }
        public bool TriggerAttackVisual { get; }

        // Deliberate jump requested by the brain (e.g. a pathfinding jump-link), distinct from the
        // reactive Fighter-AI "walked into a wall while grounded" jump LocomotionController applies
        // on its own from Perception.
        public bool WantsJump { get; }
    }
}
