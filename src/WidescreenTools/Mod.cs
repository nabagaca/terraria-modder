using System;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Logging;
using WidescreenTools.Patches;

namespace WidescreenTools
{
    public class Mod : IMod
    {
        internal static Mod Instance { get; private set; }

        public string Id => "widescreen-tools";
        public string Name => "Widescreen Tools";
        public string Version => "0.1.0";

        private ILogger _log;
        private ModContext _context;
        private bool _enabled;
        private bool _overrideForcedMinimumZoom;
        private bool _unlockHighResModes;
        private bool _persistResolution;
        private int _desiredResolutionWidth;
        private int _desiredResolutionHeight;
        private int _worldViewWidth;
        private int _worldViewHeight;
        private bool _pendingApply = true;
        private bool _startupResolutionHandled;
        private int _lastObservedWidth;
        private int _lastObservedHeight;

        public void Initialize(ModContext context)
        {
            Instance = this;
            _log = context.Logger;
            _context = context;

            WidescreenZoomOverride.Initialize(_log);
            WidescreenResolutionOverride.Initialize(_log);
            LoadConfigValues();
            ApplyResolutionOverrides();
            FrameEvents.OnPostUpdate += OnPostUpdate;

            _log.Info($"{Name} v{Version} initialized");
        }

        public void OnConfigChanged()
        {
            LoadConfigValues();
            _pendingApply = true;
        }

        public void OnWorldLoad()
        {
        }

        public void OnWorldUnload()
        {
        }

        public void Unload()
        {
            Instance = null;
            FrameEvents.OnPostUpdate -= OnPostUpdate;
            WidescreenZoomOverride.RestoreOriginal();
        }

        private void OnPostUpdate()
        {
            if (_pendingApply)
            {
                _pendingApply = false;
                ApplyConfiguredOverrides();
            }

            TrackResolutionChanges();
        }

        private void LoadConfigValues()
        {
            if (_context?.Config == null)
            {
                return;
            }

            _enabled = _context.Config.Get("enabled", true);
            _overrideForcedMinimumZoom = _context.Config.Get("overrideForcedMinimumZoom", true);
            _unlockHighResModes = _context.Config.Get("unlockHighResModes", true);
            _persistResolution = _context.Config.Get("persistResolution", true);
            _desiredResolutionWidth = _context.Config.Get("desiredResolutionWidth", 0);
            _desiredResolutionHeight = _context.Config.Get("desiredResolutionHeight", 0);
            _worldViewWidth = _context.Config.Get("worldViewWidth", 5120);
            _worldViewHeight = _context.Config.Get("worldViewHeight", 1440);

            if (_worldViewWidth < WidescreenZoomOverride.VanillaWidth)
            {
                _worldViewWidth = WidescreenZoomOverride.VanillaWidth;
            }

            if (_worldViewHeight < WidescreenZoomOverride.VanillaHeight)
            {
                _worldViewHeight = WidescreenZoomOverride.VanillaHeight;
            }
        }

        private void ApplyConfiguredOverrides()
        {
            ApplyResolutionOverrides();
            ApplySavedResolution();

            if (!_enabled || !_overrideForcedMinimumZoom)
            {
                WidescreenZoomOverride.RestoreOriginal();
                _log.Info("[WidescreenTools] Forced minimum zoom override disabled");
                return;
            }

            if (WidescreenZoomOverride.Apply(_worldViewWidth, _worldViewHeight))
            {
                _log.Info($"[WidescreenTools] Forced minimum zoom comparer set to {_worldViewWidth}x{_worldViewHeight}");
            }
        }

        private void ApplyResolutionOverrides()
        {
            if (!_enabled || !_unlockHighResModes)
            {
                return;
            }

            WidescreenResolutionOverride.Apply();
        }

        private void ApplySavedResolution()
        {
            if (!_enabled || !_unlockHighResModes || !_persistResolution)
            {
                _startupResolutionHandled = true;
                return;
            }

            if (_desiredResolutionWidth <= 0 || _desiredResolutionHeight <= 0)
            {
                _startupResolutionHandled = true;
                return;
            }

            if (Main.screenWidth == _desiredResolutionWidth && Main.screenHeight == _desiredResolutionHeight)
            {
                SetPendingResolution(_desiredResolutionWidth, _desiredResolutionHeight);
                _startupResolutionHandled = true;
                _lastObservedWidth = Main.screenWidth;
                _lastObservedHeight = Main.screenHeight;
                return;
            }

            if (TrySetResolution(_desiredResolutionWidth, _desiredResolutionHeight))
            {
                SetPendingResolution(_desiredResolutionWidth, _desiredResolutionHeight);
                _startupResolutionHandled = true;
                _lastObservedWidth = Main.screenWidth;
                _lastObservedHeight = Main.screenHeight;
                _log.Info($"[WidescreenTools] Restored saved resolution {_desiredResolutionWidth}x{_desiredResolutionHeight}");
                return;
            }

            _startupResolutionHandled = true;
        }

        private void TrackResolutionChanges()
        {
            if (!_enabled || !_unlockHighResModes || !_persistResolution || _context?.Config == null)
            {
                return;
            }

            if (!_startupResolutionHandled)
            {
                return;
            }

            if (Main.screenWidth <= 0 || Main.screenHeight <= 0)
            {
                return;
            }

            if (Main.screenWidth == _lastObservedWidth && Main.screenHeight == _lastObservedHeight)
            {
                return;
            }

            _lastObservedWidth = Main.screenWidth;
            _lastObservedHeight = Main.screenHeight;

            if (_desiredResolutionWidth == Main.screenWidth && _desiredResolutionHeight == Main.screenHeight)
            {
                return;
            }

            _desiredResolutionWidth = Main.screenWidth;
            _desiredResolutionHeight = Main.screenHeight;
            _context.Config.Set("desiredResolutionWidth", _desiredResolutionWidth);
            _context.Config.Set("desiredResolutionHeight", _desiredResolutionHeight);
            _context.Config.Save();
            _log.Info($"[WidescreenTools] Saved resolution {_desiredResolutionWidth}x{_desiredResolutionHeight}");
        }

        private bool TrySetResolution(int width, int height)
        {
            try
            {
                var setResolution = typeof(Main).GetMethod("SetResolution", new[] { typeof(int), typeof(int) });
                if (setResolution == null)
                {
                    _log.Warn("[WidescreenTools] Failed to find Main.SetResolution(int, int)");
                    return false;
                }

                setResolution.Invoke(null, new object[] { width, height });
                return true;
            }
            catch (Exception ex)
            {
                _log.Warn($"[WidescreenTools] Failed to restore saved resolution {width}x{height}: {ex.Message}");
                return false;
            }
        }

        internal void ApplySavedResolutionFromCache()
        {
            if (_startupResolutionHandled)
            {
                return;
            }

            ApplySavedResolution();
        }

        private static void SetPendingResolution(int width, int height)
        {
            Main.PendingResolutionWidth = width;
            Main.PendingResolutionHeight = height;
        }
    }
}
