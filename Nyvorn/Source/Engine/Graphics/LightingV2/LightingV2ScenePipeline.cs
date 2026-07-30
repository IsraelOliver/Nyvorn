namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Phases of the Lighting V2 scene rendering pipeline (PHASE 2: Composition Neutral).
    /// Each phase is a distinct render step composing the final scene RenderTarget.
    /// </summary>
    public enum LightingV2ScenePipeline
    {
        /// <summary>Prepare scene RenderTarget, clear to black.</summary>
        Phase0_Prepare,

        /// <summary>Draw atmosphere: sky, sun, moons, clouds.</summary>
        Phase1_Atmosphere,

        /// <summary>Draw world: terrain, decorations, tiles.</summary>
        Phase2_World,

        /// <summary>Draw entities: player, enemies, NPCs.</summary>
        Phase3_Entities,

        /// <summary>Apply lighting: multiply light map over scene (not active in PHASE 2).</summary>
        Phase4_Lighting,

        /// <summary>Composite scene RenderTarget to backbuffer.</summary>
        Phase5_Composite,

        /// <summary>Draw screen-space effects: rain, overlays (after composition).</summary>
        Phase6_ScreenEffects,

        /// <summary>Draw HUD, UI, minimap, console (after all world rendering).</summary>
        Phase7_HUD
    }
}
