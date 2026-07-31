using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Controls debug visualization for Phase 2 foundation.
    /// Handles hotkeys and state management.
    ///
    /// Hotkeys (only active in DEBUG):
    /// - Ctrl+Shift+V: Cycle visualization modes
    /// - Ctrl+Shift+F: Dump metrics to console
    /// </summary>
    public class LightingV3DebugController
    {
        private LightingDebugMode _currentMode = LightingDebugMode.None;
        private KeyboardState _lastKeyboardState;
        private bool _isDebugEnabled;

        /// <summary>
        /// Create debug controller (debug mode detection is automatic).
        /// </summary>
        public LightingV3DebugController()
        {
#if DEBUG
            _isDebugEnabled = true;
#else
            _isDebugEnabled = false;
#endif
            _lastKeyboardState = Keyboard.GetState();
        }

        /// <summary>
        /// Get current debug visualization mode.
        /// </summary>
        public LightingDebugMode GetCurrentMode() => _currentMode;

        /// <summary>
        /// Update input and handle hotkeys.
        /// Call once per frame in PlayingState.Draw().
        /// </summary>
        public void Update()
        {
            if (!_isDebugEnabled)
                return;

            var currentKeyboardState = Keyboard.GetState();

            // Ctrl+Shift+V: Cycle debug modes
            if (IsKeyPressed(Keys.V, currentKeyboardState, _lastKeyboardState))
            {
                CycleMode();
            }

            // Ctrl+Shift+F: Dump metrics (requires foundation reference)
            // This is handled by caller (PlayingState) due to foundation coupling

            _lastKeyboardState = currentKeyboardState;
        }

        /// <summary>
        /// Cycle to next visualization mode.
        /// </summary>
        public void CycleMode()
        {
            _currentMode = (LightingDebugMode)(((int)_currentMode + 1) % 5);
            System.Console.WriteLine($"[LightingV3Debug] Mode → {_currentMode}");
        }

        /// <summary>
        /// Set visualization mode explicitly.
        /// </summary>
        public void SetMode(LightingDebugMode mode)
        {
            _currentMode = mode;
        }

        /// <summary>
        /// Check if a key is newly pressed (not held).
        /// </summary>
        private static bool IsKeyPressed(Keys key, KeyboardState current, KeyboardState previous)
        {
            bool isCtrlHeld = current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
            bool isShiftHeld = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
            bool wasKeyDown = previous.IsKeyDown(key);
            bool isKeyDown = current.IsKeyDown(key);

            return isCtrlHeld && isShiftHeld && isKeyDown && !wasKeyDown;
        }

        /// <summary>
        /// Check if debug is enabled (DEBUG build only).
        /// </summary>
        public bool IsDebugEnabled => _isDebugEnabled;
    }
}
