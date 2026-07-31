namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Sub-tile sampling configuration for V3 lighting foundation.
    /// Defines how many samples per tile and their spacing.
    ///
    /// Configurable for future evaluation:
    /// - SamplesPerAxis = 2: 2x2 = 4 samples per tile (0.5 tile spacing)
    /// - SamplesPerAxis = 4: 4x4 = 16 samples per tile (0.25 tile spacing)
    ///
    /// Do NOT call this "0.25 tile precision" when SamplesPerAxis=2.
    /// The actual precision is SampleSpacingTiles = 1.0 / SamplesPerAxis.
    /// </summary>
    public class LightingSamplingConfig
    {
        /// <summary>
        /// Number of samples along each axis (2 = 2x2 grid, 4 = 4x4 grid, etc).
        /// </summary>
        public int SamplesPerAxis { get; private set; }

        /// <summary>
        /// Total samples per tile: SamplesPerAxis * SamplesPerAxis.
        /// </summary>
        public int SamplesPerTile => SamplesPerAxis * SamplesPerAxis;

        /// <summary>
        /// Spacing between samples in tile units: 1.0 / SamplesPerAxis.
        /// For SamplesPerAxis=2: SampleSpacingTiles = 0.5
        /// For SamplesPerAxis=4: SampleSpacingTiles = 0.25
        /// </summary>
        public float SampleSpacingTiles => 1.0f / SamplesPerAxis;

        /// <summary>
        /// Private constructor: Use static factory methods.
        /// </summary>
        private LightingSamplingConfig(int samplesPerAxis)
        {
            if (samplesPerAxis < 1)
                throw new System.ArgumentException("SamplesPerAxis must be >= 1", nameof(samplesPerAxis));

            SamplesPerAxis = samplesPerAxis;
        }

        /// <summary>
        /// 2x2 sampling: 4 samples per tile, 0.5 tile spacing.
        /// Current default for Phase 2.
        /// </summary>
        public static readonly LightingSamplingConfig Default2x2 = new LightingSamplingConfig(2);

        /// <summary>
        /// 4x4 sampling: 16 samples per tile, 0.25 tile spacing.
        /// Available for future evaluation.
        /// </summary>
        public static readonly LightingSamplingConfig Option4x4 = new LightingSamplingConfig(4);

        /// <summary>
        /// Create custom configuration (for testing only).
        /// </summary>
        public static LightingSamplingConfig CreateCustom(int samplesPerAxis)
        {
            return new LightingSamplingConfig(samplesPerAxis);
        }

        public override string ToString()
        {
            return $"LightingSamplingConfig({SamplesPerAxis}x{SamplesPerAxis}, " +
                   $"{SamplesPerTile} samples/tile, {SampleSpacingTiles} tile spacing)";
        }
    }
}
