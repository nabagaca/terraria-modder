using System;
using System.Collections.Generic;
using TerrariaModder.Core.Logging;

namespace SeedLab.Patches
{
    /// <summary>
    /// Debug commands for Seed Lab preset management.
    /// Provides command-line access to feature toggles and presets.
    ///
    /// World creation UI injection (adding preset buttons to the vanilla world creation screen)
    /// is deferred — the vanilla multi-select seed buttons already work, and the in-game panel
    /// handles feature-level mixing after world load.
    /// </summary>
    public static class SeedLabCommands
    {
        private static ILogger _log;
        private static FeatureManager _manager;
        private static PresetManager _presetManager;

        public static void Register(TerrariaModder.Core.ModContext context, FeatureManager manager, PresetManager presetManager, ILogger log)
        {
            _log = log;
            _manager = manager;
            _presetManager = presetManager;

            context.RegisterCommand("status", "Show current seed feature states", CmdStatus);
            context.RegisterCommand("toggle", "Toggle a seed/group: seed-lab.toggle ftw | seed-lab.toggle ftw_enemy_scaling", CmdToggle);
            context.RegisterCommand("preset", "Apply/save/delete preset: seed-lab.preset apply Name | seed-lab.preset save Name | seed-lab.preset delete Name | seed-lab.preset list", CmdPreset);
            context.RegisterCommand("reset", "Reset all features to match world seed flags", CmdReset);
        }

        private static void CmdStatus(string[] args)
        {
            if (!_manager.Initialized)
            {
                _log.Info("[SeedLab] Not in a world");
                return;
            }

            _log.Info("[SeedLab] === Feature Status ===");
            foreach (var seed in SeedFeatures.Seeds)
            {
                bool anyEnabled = _manager.IsSeedAnyEnabled(seed.Id);
                string flag = FeatureManager.GetFlag(seed.FlagField) ? "(world has seed)" : "";
                _log.Info($"  {seed.DisplayName} {flag}");

                foreach (var group in seed.Groups)
                {
                    bool groupOn = _manager.IsGroupEnabled(group.Id);
                    _log.Info($"    [{(groupOn ? "ON" : "  ")}] {group.DisplayName}");

                    foreach (var feature in group.Features)
                    {
                        bool featureOn = _manager.IsFeatureEnabled(feature.Id);
                        _log.Info($"      [{(featureOn ? "X" : " ")}] {feature.DisplayName} ({feature.Id})");
                    }
                }
            }
        }

        private static void CmdToggle(string[] args)
        {
            if (!_manager.Initialized)
            {
                _log.Info("[SeedLab] Not in a world");
                return;
            }

            if (args.Length < 1)
            {
                _log.Info("[SeedLab] Usage: seedlab.toggle <seed_id|group_id>");
                return;
            }

            string target = args[0].ToLower();

            // Try as seed
            foreach (var seed in SeedFeatures.Seeds)
            {
                if (seed.Id == target)
                {
                    _manager.ToggleSeed(seed.Id);
                    _manager.SaveState();
                    _log.Info($"[SeedLab] Toggled seed '{seed.DisplayName}'");
                    return;
                }
            }

            // Try as group
            if (_manager.GroupsById.ContainsKey(target))
            {
                _manager.ToggleGroup(target);
                _manager.SaveState();
                _log.Info($"[SeedLab] Toggled group '{target}'");
                return;
            }

            // Try as feature
            if (_manager.FeaturesById.ContainsKey(target))
            {
                _manager.ToggleFeature(target);
                _manager.SaveState();
                _log.Info($"[SeedLab] Toggled feature '{target}'");
                return;
            }

            _log.Info($"[SeedLab] Unknown target '{target}'. Use 'seedlab.status' to see IDs.");
        }

        private static void CmdPreset(string[] args)
        {
            if (args.Length < 1)
            {
                _log.Info("[SeedLab] Usage: seedlab.preset list | seedlab.preset apply <name> | seedlab.preset save <name> | seedlab.preset delete <name>");
                return;
            }

            string action = args[0].ToLower();

            if (action == "list")
            {
                var presets = _presetManager.Presets;
                if (presets.Count == 0)
                {
                    _log.Info("[SeedLab] No presets saved");
                    return;
                }
                _log.Info("[SeedLab] Saved presets:");
                foreach (var kvp in presets)
                {
                    _log.Info($"  - {kvp.Key}");
                }
                return;
            }

            if (args.Length < 2)
            {
                _log.Info("[SeedLab] Preset name required");
                return;
            }

            string name = string.Join(" ", args, 1, args.Length - 1);

            switch (action)
            {
                case "apply":
                    if (!_manager.Initialized) { _log.Info("[SeedLab] Not in a world"); return; }
                    if (!_presetManager.HasPreset(name)) { _log.Info($"[SeedLab] Preset '{name}' not found"); return; }
                    _presetManager.ApplyPreset(name, _manager);
                    _manager.SaveState();
                    break;

                case "save":
                    if (!_manager.Initialized) { _log.Info("[SeedLab] Not in a world"); return; }
                    _presetManager.SavePreset(name, _manager);
                    break;

                case "delete":
                    _presetManager.DeletePreset(name);
                    break;

                default:
                    _log.Info($"[SeedLab] Unknown action '{action}'");
                    break;
            }
        }

        private static void CmdReset(string[] args)
        {
            if (!_manager.Initialized)
            {
                _log.Info("[SeedLab] Not in a world");
                return;
            }

            _manager.InitFromWorldFlags();
            _manager.SaveState();
            _log.Info("[SeedLab] Reset all features to match world seed flags");
        }
    }
}
