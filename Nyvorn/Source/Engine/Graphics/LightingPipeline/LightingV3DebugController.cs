using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Controls debug visualization for Phase 2 foundation.
    /// Handles hotkeys and state management.
    ///
    /// Hotkeys (DEBUG builds only, V3 mode only):
    /// - F6: Cycle Phase 2 visualization modes (None → Classification → SunOpacity → LocalLightOpacity → SampleGrid → None)
    /// - F7: Dump Phase 2 foundation metrics to console
    ///
    /// Edge detection: Mode changes only on key press (not held).
    /// Visual confirmation: Mode name displayed in overlay for 2 seconds.
    /// </summary>
    public class LightingV3DebugController
    {
        private static int _instanceCounter = 0;
        private readonly int _instanceId;

        private LightingDebugMode _currentMode = LightingDebugMode.None;
        private KeyboardState _lastKeyboardState;
        private bool _isDebugEnabled;
        private double _modeConfirmationTimer = 0f;  // Seconds remaining to show confirmation

        public const double MODE_CONFIRMATION_DURATION = 2.0;  // Show mode name for 2 seconds

        /// <summary>
        /// Create debug controller (debug mode detection is automatic).
        /// </summary>
        public LightingV3DebugController()
        {
            _instanceId = ++_instanceCounter;
#if DEBUG
            _isDebugEnabled = true;
#else
            _isDebugEnabled = false;
#endif
            _lastKeyboardState = Keyboard.GetState();
        }

        /// <summary>
        /// Get unique instance ID for debugging controller lifecycle.
        /// </summary>
        public int InstanceId => _instanceId;

        /// <summary>
        /// Get current debug visualization mode.
        /// </summary>
        public LightingDebugMode GetCurrentMode() => _currentMode;

        /// <summary>
        /// Check if mode confirmation should be displayed (just changed).
        /// </summary>
        public bool ShowModeConfirmation => _modeConfirmationTimer > 0;

        /// <summary>
        /// Get current mode name for confirmation display.
        /// </summary>
        public string GetModeConfirmationText() => $"Lighting Debug: {_currentMode}";

        /// <summary>
        /// Update input and handle hotkeys.
        /// Call once per frame in PlayingState.Update() (NOT Draw).
        /// </summary>
        public void Update(double deltaTime)
        {
            if (!_isDebugEnabled)
                return;

            var currentKeyboardState = Keyboard.GetState();

            // F6: Cycle debug modes (edge detection)
            if (IsKeyPressedEdge(Keys.F6, currentKeyboardState, _lastKeyboardState))
            {
                CycleMode();
                _modeConfirmationTimer = MODE_CONFIRMATION_DURATION;
            }

            // F7: Dump metrics (requires foundation reference in caller)
            // Handled by PlayingState due to foundation coupling

            _lastKeyboardState = currentKeyboardState;

            // Decay confirmation timer
            _modeConfirmationTimer -= deltaTime;
            if (_modeConfirmationTimer < 0)
                _modeConfirmationTimer = 0;
        }

        /// <summary>
        /// Cycle to next visualization mode.
        /// </summary>
        public void CycleMode()
        {
            _currentMode = (LightingDebugMode)(((int)_currentMode + 1) % 5);
            System.Console.WriteLine($"[LightingV3Debug] ControllerId: {_instanceId} | Mode -> {_currentMode}");
        }

        /// <summary>
        /// Set visualization mode explicitly.
        /// </summary>
        public void SetMode(LightingDebugMode mode)
        {
            _currentMode = mode;
            _modeConfirmationTimer = MODE_CONFIRMATION_DURATION;
        }

        /// <summary>
        /// Check if a key is newly pressed this frame (edge detection).
        /// Only true on the frame the key transitions from up to down.
        /// </summary>
        private static bool IsKeyPressedEdge(Keys key, KeyboardState current, KeyboardState previous)
        {
            bool wasKeyDown = previous.IsKeyDown(key);
            bool isKeyDown = current.IsKeyDown(key);

            // Edge: key was up last frame, is down this frame
            return !wasKeyDown && isKeyDown;
        }

        /// <summary>
        /// Check if debug is enabled (DEBUG build only).
        /// </summary>
        public bool IsDebugEnabled => _isDebugEnabled;
    }
}
