using System;
using System.Collections.Generic;

namespace TerrariaModder.Core.Config
{
    /// <summary>
    /// Interface for mod configuration.
    /// </summary>
    public interface IModConfig
    {
        /// <summary>Get a config value.</summary>
        T Get<T>(string key);

        /// <summary>Get a config value with fallback default.</summary>
        T Get<T>(string key, T defaultValue);

        /// <summary>Try to get a config value.</summary>
        bool TryGet<T>(string key, out T value);

        /// <summary>Set a config value.</summary>
        void Set<T>(string key, T value);

        /// <summary>Check if a key exists.</summary>
        bool HasKey(string key);

        /// <summary>Save config to file.</summary>
        void Save();

        /// <summary>Reload config from file.</summary>
        void Reload();

        /// <summary>Reset all values to defaults.</summary>
        void ResetToDefaults();

        /// <summary>Fired when a specific value changes.</summary>
        event Action<string> OnValueChanged;

        /// <summary>Fired when config is reloaded from disk.</summary>
        event Action OnConfigReloaded;

        /// <summary>Schema fields for this config.</summary>
        IReadOnlyDictionary<string, ConfigField> Schema { get; }

        /// <summary>True if there are unsaved changes.</summary>
        bool HasUnsavedChanges { get; }

        /// <summary>Path to config file.</summary>
        string FilePath { get; }
    }
}
