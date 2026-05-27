using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueGenerator
    {
        private readonly int seed;

        public TissueGenerator(int seed)
        {
            this.seed = seed;
        }

        public TissueNetwork Generate(WorldMap worldMap)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            // Stub temporario: a rede procedural antiga foi removida. Mantemos
            // o contrato para que minimapa, save/load, poderes e sessao possam
            // continuar integrados enquanto o novo modelo biologico nasce.
            return new TissueNetwork(
                seed,
                new Rectangle(0, 0, worldMap.Width * worldMap.TileSize, worldMap.Height * worldMap.TileSize),
                Array.Empty<TissueNode>(),
                Array.Empty<TissueBranch>());
        }
    }
}
