namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Pure geometric classification of a cell in the lighting grid.
    /// Does NOT consider: lighting, sky exposure, depth, time of day, or current light values.
    ///
    /// Classification describes structural geometry only:
    /// - Is foreground solid?
    /// - Is background wall present?
    /// - Are both empty?
    ///
    /// This classification is static and can be cached.
    /// </summary>
    public enum LightingCellClassification : byte
    {
        /// <summary>
        /// Both foreground and background are empty.
        /// Open to atmosphere/sky.
        /// </summary>
        OpenAtmosphere = 0,

        /// <summary>
        /// Foreground is empty, but background has a wall.
        /// Ground level or cavern ceiling.
        /// </summary>
        VisibleBackground = 1,

        /// <summary>
        /// Foreground is solid (block, structure, etc).
        /// Completely blocks view and light.
        /// </summary>
        SolidForeground = 2,
    }

    /// <summary>
    /// Static helper methods for classification.
    /// </summary>
    public static class LightingCellClassificationHelper
    {
        public static bool IsSolid(this LightingCellClassification classification)
        {
            return classification == LightingCellClassification.SolidForeground;
        }

        public static bool HasBackground(this LightingCellClassification classification)
        {
            return classification == LightingCellClassification.VisibleBackground;
        }

        public static bool IsOpen(this LightingCellClassification classification)
        {
            return classification == LightingCellClassification.OpenAtmosphere;
        }

        public static string GetDebugName(this LightingCellClassification classification)
        {
            return classification switch
            {
                LightingCellClassification.OpenAtmosphere => "OpenAtmosphere",
                LightingCellClassification.VisibleBackground => "VisibleBackground",
                LightingCellClassification.SolidForeground => "SolidForeground",
                _ => "Unknown"
            };
        }
    }
}
