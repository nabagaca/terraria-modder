using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TerrariaModder.Core.Logging;

namespace SeedLab
{
    /// <summary>
    /// Manages named presets of feature configurations.
    /// Presets can be saved in either normal mode (group-level) or advanced mode (feature-level).
    /// </summary>
    public class PresetManager
    {
        private readonly ILogger _log;
        private readonly string _presetsPath;

        // presetName → Preset
        private readonly Dictionary<string, Preset> _presets = new Dictionary<string, Preset>();

        public PresetManager(ILogger log, string presetsPath)
        {
            _log = log;
            _presetsPath = presetsPath;
            Load();
        }

        public IReadOnlyDictionary<string, Preset> Presets => _presets;

        /// <summary>
        /// Save current feature states as a named preset.
        /// </summary>
        public void SavePreset(string name, FeatureManager manager)
        {
            var preset = new Preset
            {
                Name = name,
                AdvancedMode = manager.AdvancedMode,
                FeatureStates = manager.GetAllStates()
            };
            _presets[name] = preset;
            Save();
            _log.Info($"[SeedLab] Saved preset '{name}'");
        }

        /// <summary>
        /// Load a preset and apply it to the feature manager.
        /// </summary>
        public void ApplyPreset(string name, FeatureManager manager)
        {
            if (!_presets.TryGetValue(name, out var preset)) return;

            manager.DisableAll();
            manager.ApplyStates(preset.FeatureStates);
            _log.Info($"[SeedLab] Applied preset '{name}'");
        }

        /// <summary>
        /// Delete a named preset.
        /// </summary>
        public void DeletePreset(string name)
        {
            if (_presets.Remove(name))
            {
                Save();
                _log.Info($"[SeedLab] Deleted preset '{name}'");
            }
        }

        public bool HasPreset(string name)
        {
            return _presets.ContainsKey(name);
        }

        private void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                int i = 0;
                foreach (var kvp in _presets)
                {
                    var preset = kvp.Value;
                    sb.AppendLine($"  \"{EscapeJson(kvp.Key)}\": {{");
                    sb.AppendLine($"    \"advancedMode\": {(preset.AdvancedMode ? "true" : "false")},");
                    sb.AppendLine("    \"features\": {");

                    int j = 0;
                    foreach (var fkvp in preset.FeatureStates)
                    {
                        string comma = j < preset.FeatureStates.Count - 1 ? "," : "";
                        sb.AppendLine($"      \"{fkvp.Key}\": {(fkvp.Value ? "true" : "false")}{comma}");
                        j++;
                    }

                    sb.AppendLine("    }");
                    string outerComma = i < _presets.Count - 1 ? "," : "";
                    sb.AppendLine($"  }}{outerComma}");
                    i++;
                }
                sb.AppendLine("}");

                string dir = Path.GetDirectoryName(_presetsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_presetsPath, sb.ToString());
            }
            catch (Exception ex)
            {
                _log.Error($"[SeedLab] Failed to save presets: {ex.Message}");
            }
        }

        private void Load()
        {
            if (!File.Exists(_presetsPath)) return;

            try
            {
                string json = File.ReadAllText(_presetsPath);
                ParsePresets(json);
                _log.Info($"[SeedLab] Loaded {_presets.Count} presets");
            }
            catch (Exception ex)
            {
                _log.Error($"[SeedLab] Failed to load presets: {ex.Message}");
            }
        }

        private void ParsePresets(string json)
        {
            // Simple regex-based JSON parsing for our known structure
            // Match top-level keys (preset names)
            var presetPattern = new Regex(@"""([^""]+)""\s*:\s*\{[^{}]*""advancedMode""\s*:\s*(true|false)[^{}]*""features""\s*:\s*\{([^}]*)\}\s*\}", RegexOptions.Singleline);

            foreach (Match m in presetPattern.Matches(json))
            {
                string name = m.Groups[1].Value;
                bool advanced = m.Groups[2].Value.ToLower() == "true";
                string featuresBlock = m.Groups[3].Value;

                var states = new Dictionary<string, bool>();
                var featurePattern = new Regex(@"""(\w+)""\s*:\s*(true|false)");
                foreach (Match fm in featurePattern.Matches(featuresBlock))
                {
                    states[fm.Groups[1].Value] = fm.Groups[2].Value.ToLower() == "true";
                }

                _presets[name] = new Preset
                {
                    Name = name,
                    AdvancedMode = advanced,
                    FeatureStates = states
                };
            }
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    public class Preset
    {
        public string Name;
        public bool AdvancedMode;
        public Dictionary<string, bool> FeatureStates = new Dictionary<string, bool>();
    }
}
