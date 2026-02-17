using System;
using System.Reflection;
using Terraria;
using TerrariaModder.Core.Reflection;

namespace TerrariaModder.Core.Input
{
    /// <summary>
    /// Tracks keyboard and mouse state using reflection to avoid XNA dependency.
    /// Call Update() once per frame.
    /// </summary>
    public static class InputState
    {
        private static object _previousKeyState;
        private static object _currentKeyState;
        private static object _previousMouseState;
        private static object _currentMouseState;
        private static bool _initialized;
        private static bool _firstFrame = true;

        // Keyboard reflection
        private static Type _keyboardType;
        private static Type _keysType;
        private static MethodInfo _getKeyStateMethod;
        private static MethodInfo _isKeyDownMethod;
        private static MethodInfo _isKeyUpMethod;

        // Mouse reflection
        private static Type _mouseType;
        private static MethodInfo _getMouseStateMethod;
        private static PropertyInfo _leftButtonProp;
        private static PropertyInfo _rightButtonProp;
        private static PropertyInfo _middleButtonProp;
        private static PropertyInfo _scrollWheelProp;
        private static object _pressedValue;
        private static int _previousScrollWheel;
        private static int _currentScrollWheel;

        /// <summary>
        /// Initialize reflection for input access.
        /// </summary>
        private static bool EnsureInitialized()
        {
            if (_initialized) return _keyboardType != null;

            _initialized = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_keyboardType == null)
                    {
                        _keyboardType = asm.GetType("Microsoft.Xna.Framework.Input.Keyboard");
                        if (_keyboardType != null)
                            _keysType = asm.GetType("Microsoft.Xna.Framework.Input.Keys");
                    }

                    if (_mouseType == null)
                    {
                        _mouseType = asm.GetType("Microsoft.Xna.Framework.Input.Mouse");
                    }

                    if (_keyboardType != null && _mouseType != null)
                        break;
                }

                if (_keyboardType == null || _keysType == null)
                    return false;

                _getKeyStateMethod = _keyboardType.GetMethod("GetState", Type.EmptyTypes);
                if (_getKeyStateMethod == null) return false;

                var keyStateType = _getKeyStateMethod.ReturnType;
                _isKeyDownMethod = keyStateType.GetMethod("IsKeyDown", new[] { _keysType });
                _isKeyUpMethod = keyStateType.GetMethod("IsKeyUp", new[] { _keysType });

                // Mouse
                if (_mouseType != null)
                {
                    _getMouseStateMethod = _mouseType.GetMethod("GetState", Type.EmptyTypes);
                    if (_getMouseStateMethod != null)
                    {
                        var mouseStateType = _getMouseStateMethod.ReturnType;
                        _leftButtonProp = mouseStateType.GetProperty("LeftButton");
                        _rightButtonProp = mouseStateType.GetProperty("RightButton");
                        _middleButtonProp = mouseStateType.GetProperty("MiddleButton");
                        _scrollWheelProp = mouseStateType.GetProperty("ScrollWheelValue");

                        // Get the "Pressed" enum value
                        var buttonStateType = _leftButtonProp?.PropertyType;
                        if (buttonStateType != null)
                            _pressedValue = Enum.Parse(buttonStateType, "Pressed");
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Update input state. Called each frame by the framework.
        /// </summary>
        public static void Update()
        {
            // Skip on dedicated server - XNA input APIs don't exist
            if (Game.IsServer) return;

            if (!EnsureInitialized()) return;

            try
            {
                // Keyboard
                _previousKeyState = _currentKeyState;
                _currentKeyState = _getKeyStateMethod.Invoke(null, null);

                // Mouse
                if (_getMouseStateMethod != null)
                {
                    _previousMouseState = _currentMouseState;
                    _currentMouseState = _getMouseStateMethod.Invoke(null, null);

                    _previousScrollWheel = _currentScrollWheel;
                    if (_scrollWheelProp != null && _currentMouseState != null)
                        _currentScrollWheel = (int)_scrollWheelProp.GetValue(_currentMouseState);
                }

                // First frame - set previous = current to avoid false positives
                if (_firstFrame)
                {
                    _previousKeyState = _currentKeyState;
                    _previousMouseState = _currentMouseState;
                    _previousScrollWheel = _currentScrollWheel;
                    _firstFrame = false;
                }
            }
            catch
            {
                // Silently fail
            }
        }

        /// <summary>Check if key is currently down.</summary>
        public static bool IsKeyDown(int keyCode)
        {
            if (Game.IsServer) return false;
            if (keyCode == KeyCode.None) return false;

            // Mouse buttons
            if (keyCode >= KeyCode.MouseLeft)
                return IsMouseButtonDown(keyCode);

            if (_currentKeyState == null || _isKeyDownMethod == null) return false;

            try
            {
                var keyEnum = Enum.ToObject(_keysType, keyCode);
                return (bool)_isKeyDownMethod.Invoke(_currentKeyState, new[] { keyEnum });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Check if key was just pressed this frame.</summary>
        public static bool IsKeyJustPressed(int keyCode)
        {
            if (Game.IsServer) return false;
            if (keyCode == KeyCode.None) return false;

            // Mouse buttons
            if (keyCode >= KeyCode.MouseLeft)
                return IsMouseButtonJustPressed(keyCode);

            if (_currentKeyState == null || _previousKeyState == null) return false;

            try
            {
                var keyEnum = Enum.ToObject(_keysType, keyCode);
                bool currentDown = (bool)_isKeyDownMethod.Invoke(_currentKeyState, new[] { keyEnum });
                bool previousUp = (bool)_isKeyUpMethod.Invoke(_previousKeyState, new[] { keyEnum });
                return currentDown && previousUp;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Check if key was just released this frame.</summary>
        public static bool IsKeyJustReleased(int keyCode)
        {
            if (Game.IsServer) return false;
            if (keyCode == KeyCode.None) return false;

            // Mouse buttons
            if (keyCode >= KeyCode.MouseLeft)
                return IsMouseButtonJustReleased(keyCode);

            if (_currentKeyState == null || _previousKeyState == null) return false;

            try
            {
                var keyEnum = Enum.ToObject(_keysType, keyCode);
                bool currentUp = (bool)_isKeyUpMethod.Invoke(_currentKeyState, new[] { keyEnum });
                bool previousDown = (bool)_isKeyDownMethod.Invoke(_previousKeyState, new[] { keyEnum });
                return currentUp && previousDown;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Check if Ctrl is held.</summary>
        public static bool IsCtrlDown() =>
            IsKeyDown(KeyCode.LeftControl) || IsKeyDown(KeyCode.RightControl);

        /// <summary>Check if Shift is held.</summary>
        public static bool IsShiftDown() =>
            IsKeyDown(KeyCode.LeftShift) || IsKeyDown(KeyCode.RightShift);

        /// <summary>Check if Alt is held.</summary>
        public static bool IsAltDown() =>
            IsKeyDown(KeyCode.LeftAlt) || IsKeyDown(KeyCode.RightAlt);

        /// <summary>Get scroll wheel delta since last frame.</summary>
        public static int ScrollWheelDelta => _currentScrollWheel - _previousScrollWheel;

        private static bool IsMouseButtonDown(int button)
        {
            if (_currentMouseState == null || _pressedValue == null) return false;

            PropertyInfo prop = GetMouseButtonProperty(button);
            if (prop == null) return false;

            try
            {
                var state = prop.GetValue(_currentMouseState);
                return state.Equals(_pressedValue);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMouseButtonJustPressed(int button)
        {
            if (_currentMouseState == null || _previousMouseState == null || _pressedValue == null) return false;

            PropertyInfo prop = GetMouseButtonProperty(button);
            if (prop == null) return false;

            try
            {
                var currentState = prop.GetValue(_currentMouseState);
                var previousState = prop.GetValue(_previousMouseState);
                return currentState.Equals(_pressedValue) && !previousState.Equals(_pressedValue);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMouseButtonJustReleased(int button)
        {
            if (_currentMouseState == null || _previousMouseState == null || _pressedValue == null) return false;

            PropertyInfo prop = GetMouseButtonProperty(button);
            if (prop == null) return false;

            try
            {
                var currentState = prop.GetValue(_currentMouseState);
                var previousState = prop.GetValue(_previousMouseState);
                return !currentState.Equals(_pressedValue) && previousState.Equals(_pressedValue);
            }
            catch
            {
                return false;
            }
        }

        private static PropertyInfo GetMouseButtonProperty(int button)
        {
            switch (button)
            {
                case KeyCode.MouseLeft: return _leftButtonProp;
                case KeyCode.MouseRight: return _rightButtonProp;
                case KeyCode.MouseMiddle: return _middleButtonProp;
                default: return null;
            }
        }

        /// <summary>Check if input should be blocked (chat open, menu, etc).</summary>
        public static bool ShouldBlockInput()
        {
            return Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.gameMenu;
        }
    }
}
