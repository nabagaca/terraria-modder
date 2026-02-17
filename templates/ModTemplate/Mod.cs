using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Logging;

namespace ModTemplate
{
    /// <summary>
    /// Main mod class. Rename this namespace and class to match your mod.
    /// </summary>
    public class Mod : IMod
    {
        // These must match the values in manifest.json
        public string Id => "my-mod";
        public string Name => "My Mod";
        public string Version => "1.0.0";

        private ILogger _log;
        private ModContext _context;

        // Config values - cached for performance, updated in OnConfigChanged
        private bool _enabled = true;
        private int _exampleNumber = 10;
        private float _exampleFloat = 1.0f;

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _context = context;

            _log.Info($"{Name} v{Version} initialized!");

            // Load config values
            LoadConfigValues();

            // Register keybinds
            context.RegisterKeybind("toggle", "Toggle Feature", "Toggle the main feature", "F7", OnTogglePressed);

            // Subscribe to game events (uncomment what you need)
            // GameEvents.OnWorldLoad += OnWorldLoad;
            // GameEvents.OnWorldUnload += OnWorldUnload;
            // GameEvents.OnDayStart += () => _log.Info("Day started!");
            // GameEvents.OnNightStart += () => _log.Info("Night started!");
            // FrameEvents.OnPostUpdate += OnUpdate;
            // PlayerEvents.OnPlayerSpawn += OnPlayerSpawn;
            // PlayerEvents.OnPlayerDeath += OnPlayerDeath;
            // NPCEvents.OnBossSpawn += OnBossSpawn;
        }

        private void OnTogglePressed()
        {
            _enabled = !_enabled;
            _log.Info($"Feature is now: {(_enabled ? "ENABLED" : "DISABLED")}");
        }

        #region Config

        /// <summary>
        /// Optional: called when config is changed via the mod menu (F6).
        /// Detected via reflection — not part of the IMod interface.
        /// Implement this to support live config updates without restart.
        /// If omitted, the mod menu shows "restart required" for config changes.
        /// </summary>
        public void OnConfigChanged()
        {
            _log.Info("Config changed, reloading values...");
            LoadConfigValues();
        }

        private void LoadConfigValues()
        {
            if (_context?.Config == null) return;

            _enabled = _context.Config.Get("enabled", true);
            _exampleNumber = _context.Config.Get("exampleNumber", 10);
            _exampleFloat = _context.Config.Get("exampleFloat", 1.0f);

            _log.Debug($"Config loaded: enabled={_enabled}, exampleNumber={_exampleNumber}, exampleFloat={_exampleFloat}");
        }

        #endregion

        #region Lifecycle

        public void OnWorldLoad()
        {
            _log.Info("World loaded!");
        }

        public void OnWorldUnload()
        {
            _log.Info("World unloaded!");
        }

        public void Unload()
        {
            // Unsubscribe from events to prevent memory leaks
            // GameEvents.OnWorldLoad -= OnWorldLoad;
            // FrameEvents.OnPostUpdate -= OnUpdate;

            _log.Info($"{Name} unloaded!");
        }

        #endregion

        #region Event Handlers (uncomment and use as needed)

        // private void OnUpdate()
        // {
        //     if (!_enabled) return;
        //     // Called every frame - be careful with performance!
        // }

        // private void OnPlayerSpawn(PlayerSpawnEventArgs args)
        // {
        //     _log.Info($"Player spawned at ({args.SpawnX}, {args.SpawnY})");
        // }

        // private void OnPlayerDeath(PlayerDeathEventArgs args)
        // {
        //     _log.Info($"Player died! Reason: {args.DeathReason}");
        // }

        // private void OnBossSpawn(BossSpawnEventArgs args)
        // {
        //     _log.Info($"Boss spawned: {args.BossName}");
        // }

        #endregion
    }
}
