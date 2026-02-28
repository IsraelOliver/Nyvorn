using Microsoft.Xna.Framework;
using System.Collections.Generic;

// esses arquivo será o "banco de dados" das animações do player, e o Animator.cs é a "máquina de estados" que controla qual frame desenhar baseado no estado atual do player (Idle, Walk, Jump, Fall).
// O Animator.cs é genérico o suficiente pra ser usado por outros personagens, e o PlayerAnimations.cs é específico do player, onde definimos quais são os frames de cada animação.
// O Animator.cs não tem conhecimento de quais são os estados ou quais frames cada estado tem, ele só recebe um dicionário de animações e o estado atual, e se vira pra desenhar o frame certo baseado nisso.

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public static class PlayerAnimations
    {
        public static Dictionary<AnimationState, Rectangle[]> Create()
        {
            const int frameW = 32;
            const int frameH = 32;

            return new Dictionary<AnimationState, Rectangle[]>
            {
                // Idle (linha 1, coluna 0)
                {
                    AnimationState.Idle,
                    new[]
                    {
                        new Rectangle(0 * frameW, 1 * frameH, frameW, frameH)
                    }
                },

                // Walk (linha 0, colunas 0–7)
                {
                    AnimationState.Walk,
                    new[]
                    {
                        new Rectangle(0 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(1 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(2 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(3 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(4 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(5 * frameW, 0 * frameH, frameW, frameH)
                    }
                },

                // Jump (linha 1, coluna 1)
                {
                    AnimationState.Jump,
                    new[]
                    {
                        new Rectangle(1 * frameW, 1 * frameH, frameW, frameH)
                    }
                },

                // Fall (linha 1, coluna 2)
                {
                    AnimationState.Fall,
                    new[]
                    {
                        new Rectangle(2 * frameW, 1 * frameH, frameW, frameH)
                    }
                }
            };
        }
    }
}
