using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Logging;
using WidescreenTools.Patches;

namespace WidescreenTools
{
    public class Mod : IMod
    {
        public string Id => "widescreen-tools";
        public string Name => "Widescreen Tools";
        public string Version => "0.1.0";

        private ILogger _log;
        private ModContext _context;
        private bool _enabled;
        private bool _overrideForcedMinimumZoom;
        private int _worldViewWidth;
        private int _worldViewHeight;
        private bool _pendingApply = true;

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _context = context;

            WidescreenZoomOverride.Initialize(_log);
            LoadConfigValues();
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
            FrameEvents.OnPostUpdate -= OnPostUpdate;
            WidescreenZoomOverride.RestoreOriginal();
        }

        private void OnPostUpdate()
        {
            if (!_pendingApply)
            {
                return;
            }

            _pendingApply = false;
            ApplyConfiguredOverrides();
        }

        private void LoadConfigValues()
        {
            if (_context?.Config == null)
            {
                return;
            }

            _enabled = _context.Config.Get("enabled", true);
            _overrideForcedMinimumZoom = _context.Config.Get("overrideForcedMinimumZoom", true);
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
    }
}
