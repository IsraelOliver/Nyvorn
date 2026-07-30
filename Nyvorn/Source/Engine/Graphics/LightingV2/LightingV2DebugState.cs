namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Debug visualization and metrics for Lighting V2.
    /// Toggled via debug hotkeys without affecting production behavior.
    /// </summary>
    public sealed class LightingV2DebugState
    {
        public LightingDebugLayer VisualizeLayer { get; set; } = LightingDebugLayer.None;

        public bool ShowCellClassification { get; set; } = false;

        public bool ShowDirtyCells { get; set; } = false;

        public bool ShowPropagationBounds { get; set; } = false;

        public bool LogPerformanceMetrics { get; set; } = false;

        /// <summary>Metrics computed during the last frame.</summary>
        public int CellsProcessedLastFrame { get; set; } = 0;

        public int RaysCastLastFrame { get; set; } = 0;

        public int LightSourcesLastFrame { get; set; } = 0;

        public float CpuTimeMillisecondsLastFrame { get; set; } = 0f;

        public float GpuUploadMillisecondsLastFrame { get; set; } = 0f;

        public void ResetMetrics()
        {
            CellsProcessedLastFrame = 0;
            RaysCastLastFrame = 0;
            LightSourcesLastFrame = 0;
            CpuTimeMillisecondsLastFrame = 0f;
            GpuUploadMillisecondsLastFrame = 0f;
        }

        public string GetStatusText()
        {
            return $"L2 Layer:{VisualizeLayer} Cells:{CellsProcessedLastFrame} " +
                   $"Rays:{RaysCastLastFrame} Sources:{LightSourcesLastFrame} " +
                   $"CPU:{CpuTimeMillisecondsLastFrame:F2}ms GPU:{GpuUploadMillisecondsLastFrame:F2}ms";
        }
    }
}
