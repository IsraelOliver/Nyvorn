using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Tissue
{
    public static class TissueDebugPalette
    {
        public static Color GetColor(TissueLocalType localType)
        {
            return localType switch
            {
                TissueLocalType.Thin => new Color(110, 235, 255),
                TissueLocalType.Normal => Color.White,
                TissueLocalType.Junction => new Color(255, 224, 92),
                TissueLocalType.Dense => new Color(255, 92, 210),
                _ => Color.Transparent
            };
        }
    }
}
