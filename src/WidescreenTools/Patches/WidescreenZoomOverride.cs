using System;
using System.Reflection;
using Terraria;
using TerrariaModder.Core.Logging;

namespace WidescreenTools.Patches
{
    internal static class WidescreenZoomOverride
    {
        public const int VanillaWidth = 3839;
        public const int VanillaHeight = 1200;

        private static ILogger _log;
        private static FieldInfo _maxWorldViewSizeField;
        private static ConstructorInfo _pointConstructor;
        private static MethodInfo _initTargetsMethod;
        private static FieldInfo _mainInstanceField;
        private static FieldInfo _dedServField;
        private static bool _initialized;
        private static bool _capturedOriginal;
        private static object _originalValue;

        public static void Initialize(ILogger log)
        {
            _log = log;

            if (_initialized)
            {
                return;
            }

            _maxWorldViewSizeField = typeof(Main).GetField("MaxWorldViewSize", BindingFlags.Public | BindingFlags.Static);
            _pointConstructor = _maxWorldViewSizeField?.FieldType.GetConstructor(new[] { typeof(int), typeof(int) });
            _initTargetsMethod = typeof(Main).GetMethod("InitTargets", BindingFlags.Instance | BindingFlags.NonPublic);
            _mainInstanceField = typeof(Main).GetField("instance", BindingFlags.Public | BindingFlags.Static);
            _dedServField = typeof(Main).GetField("dedServ", BindingFlags.Public | BindingFlags.Static);
            _initialized = true;

            if (_maxWorldViewSizeField == null || _pointConstructor == null)
            {
                _log?.Warn("[WidescreenTools] Could not resolve Terraria.Main.MaxWorldViewSize");
            }
        }

        public static bool Apply(int width, int height)
        {
            if (!EnsureField())
            {
                return false;
            }

            object target = CreatePoint(Math.Max(width, VanillaWidth), Math.Max(height, VanillaHeight));
            if (target == null)
            {
                return false;
            }

            if (!TrySetValue(target, "apply"))
            {
                return false;
            }

            RefreshRenderTargets();
            return true;
        }

        public static void RestoreOriginal()
        {
            if (!EnsureField())
            {
                return;
            }

            if (_capturedOriginal)
            {
                TrySetValue(_originalValue, "restore");
            }
            else
            {
                object vanillaPoint = CreatePoint(VanillaWidth, VanillaHeight);
                if (vanillaPoint != null)
                {
                    if (TrySetValue(vanillaPoint, "restore"))
                    {
                        RefreshRenderTargets();
                    }
                }
            }
        }

        private static bool EnsureField()
        {
            if (!_initialized)
            {
                Initialize(_log);
            }

            return _maxWorldViewSizeField != null;
        }

        private static void RefreshRenderTargets()
        {
            try
            {
                if (_initTargetsMethod == null)
                {
                    return;
                }

                if (_dedServField?.GetValue(null) is bool dedicated && dedicated)
                {
                    return;
                }

                object mainInstance = _mainInstanceField?.GetValue(null);
                if (mainInstance == null)
                {
                    return;
                }

                _initTargetsMethod.Invoke(mainInstance, null);
                _log?.Info("[WidescreenTools] Reinitialized render targets for updated world-view size");
            }
            catch (Exception ex)
            {
                _log?.Warn($"[WidescreenTools] Failed to reinitialize render targets: {ex.Message}");
            }
        }

        private static object CreatePoint(int width, int height)
        {
            try
            {
                return _pointConstructor?.Invoke(new object[] { width, height });
            }
            catch (Exception ex)
            {
                _log?.Error($"[WidescreenTools] Failed to create Point({width}, {height}): {ex.Message}");
                return null;
            }
        }

        private static bool TrySetValue(object value, string operation)
        {
            try
            {
                if (!_capturedOriginal)
                {
                    _originalValue = _maxWorldViewSizeField.GetValue(null);
                    _capturedOriginal = true;
                }

                _maxWorldViewSizeField.SetValue(null, value);
                return true;
            }
            catch (FieldAccessException)
            {
                try
                {
                    var attributesField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (attributesField != null)
                    {
                        var attributes = (FieldAttributes)attributesField.GetValue(_maxWorldViewSizeField);
                        attributesField.SetValue(_maxWorldViewSizeField, attributes & ~FieldAttributes.InitOnly);
                    }

                    _maxWorldViewSizeField.SetValue(null, value);
                    return true;
                }
                catch (Exception ex)
                {
                    _log?.Error($"[WidescreenTools] Failed to {operation} MaxWorldViewSize: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"[WidescreenTools] Failed to {operation} MaxWorldViewSize: {ex.Message}");
                return false;
            }
        }
    }
}
