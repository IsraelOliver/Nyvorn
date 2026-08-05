using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Debug interface for V5 Sky Ambient Prototype 0.1.
    /// Handles portal marking, visualization mode cycling, and Legacy/V5 toggling.
    ///
    /// CONTROLS:
    /// - Shift+V: Toggle between Legacy and V5 mode
    /// - LMB on background: Add sky portal at tile
    /// - RMB on portal: Remove portal
    /// - Ctrl+Shift+P: Clear all portals
    /// - 1-5: Cycle debug visualization modes
    /// - Shift+D: Toggle debug overlay visibility
    /// </summary>
    public sealed class V5SkyAmbientDebugger
    {
        public List<V5SkyPortal> Portals { get; } = new();

        public enum DebugVisualizationMode
        {
            None = 0,
            PortalPositions = 1,
            BackgroundFieldIntensity = 2,
            AccumulatedContributions = 3,
            ForegroundLightMap = 4,
            FinalColoredResult = 5
        }

        private DebugVisualizationMode currentVisMode = DebugVisualizationMode.None;
        private bool showDebugOverlay = true;
        private KeyboardState previousKeyboardState = new();

        public DebugVisualizationMode CurrentVisualizationMode => currentVisMode;
        public bool ShowDebugOverlay => showDebugOverlay;

        /// <summary>
        /// Add a portal at the given tile position.
        /// </summary>
        public void AddPortal(int tileX, int tileY)
        {
            var portal = new V5SkyPortal(tileX, tileY);
            if (!Portals.Contains(portal))
                Portals.Add(portal);
        }

        /// <summary>
        /// Remove a portal at the given tile position.
        /// </summary>
        public bool RemovePortal(int tileX, int tileY)
        {
            var portal = new V5SkyPortal(tileX, tileY);
            return Portals.Remove(portal);
        }

        /// <summary>
        /// Clear all portals.
        /// </summary>
        public void ClearPortals()
        {
            Portals.Clear();
        }

        /// <summary>
        /// Process debug input. Call this from PlayingState update.
        /// Returns true if a change was made (recompute needed).
        /// </summary>
        public bool ProcessDebugInput(KeyboardState currentKeyboard, MouseState currentMouse, int tileSize, Vector2 cameraPos)
        {
            bool changed = false;

            // Shift+V: toggle mode
            if (IsKeyPressed(currentKeyboard, Keys.V, previousKeyboardState) && IsModifierPressed(currentKeyboard, Keys.LeftShift, Keys.RightShift))
            {
                // This is handled by caller (PlayingState)
                // Here we just track that it happened
            }

            // 1-5: cycle visualization mode
            for (int i = 1; i <= 5; i++)
            {
                if (IsKeyPressed(currentKeyboard, (Keys)((int)Keys.D1 + i - 1), previousKeyboardState))
                {
                    currentVisMode = (DebugVisualizationMode)i;
                }
            }

            // Shift+D: toggle overlay
            if (IsKeyPressed(currentKeyboard, Keys.D, previousKeyboardState) && IsModifierPressed(currentKeyboard, Keys.LeftShift, Keys.RightShift))
            {
                showDebugOverlay = !showDebugOverlay;
            }

            // Ctrl+Shift+P: clear all portals
            if (IsKeyPressed(currentKeyboard, Keys.P, previousKeyboardState)
                && IsModifierPressed(currentKeyboard, Keys.LeftControl, Keys.RightControl)
                && IsModifierPressed(currentKeyboard, Keys.LeftShift, Keys.RightShift))
            {
                ClearPortals();
                changed = true;
            }

            // LMB: add portal (on background tiles)
            if (currentMouse.LeftButton == ButtonState.Pressed)
            {
                int mouseWorldX = (int)(cameraPos.X + (currentMouse.X / 2f)); // Assuming 2x zoom
                int mouseWorldY = (int)(cameraPos.Y + (currentMouse.Y / 2f));
                int tileX = mouseWorldX / tileSize;
                int tileY = mouseWorldY / tileSize;

                AddPortal(tileX, tileY);
                changed = true;
            }

            // RMB: remove portal
            if (currentMouse.RightButton == ButtonState.Pressed)
            {
                int mouseWorldX = (int)(cameraPos.X + (currentMouse.X / 2f));
                int mouseWorldY = (int)(cameraPos.Y + (currentMouse.Y / 2f));
                int tileX = mouseWorldX / tileSize;
                int tileY = mouseWorldY / tileSize;

                if (RemovePortal(tileX, tileY))
                    changed = true;
            }

            previousKeyboardState = currentKeyboard;
            return changed;
        }

        private static bool IsKeyPressed(KeyboardState current, Keys key, KeyboardState previous)
        {
            return current.IsKeyDown(key) && !previous.IsKeyDown(key);
        }

        private static bool IsModifierPressed(KeyboardState keyboard, Keys key1, Keys key2)
        {
            return keyboard.IsKeyDown(key1) || keyboard.IsKeyDown(key2);
        }
    }
}
