using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Config
{
    /// <summary>
    /// Config implementation for a mod.
    /// </summary>
    public class ModConfig : IModConfig, IDisposable
    {
        private readonly Dictionary<string, ConfigField> _schema;
        private readonly Dictionary<string, object> _values;
        private readonly object _valuesLock = new object(); // Guards _values against FileSystemWatcher thread
        private readonly Dictionary<string, object> _baselineValues; // Snapshot at startup for restart-required tracking
        private readonly ILogger _log;
        private readonly string _modId;
        private FileSystemWatcher _watcher;
        private DateTime _lastWriteTime;
        private bool _dirty;

        public string FilePath { get; }
        public bool HasUnsavedChanges => _dirty;
        public IReadOnlyDictionary<string, ConfigField> Schema => _schema;

        public event Action<string> OnValueChanged;
        public event Action OnConfigReloaded;

        public ModConfig(string modId, string configPath, Dictionary<string, ConfigField> schema, ILogger logger)
        {
            _modId = modId;
            FilePath = configPath;
            _schema = schema ?? new Dictionary<string, ConfigField>();
            _values = new Dictionary<string, object>();
            _baselineValues = new Dictionary<string, object>();
            _log = logger;

            // Load or create config
            LoadOrCreate();

            // Snapshot baseline values for restart-required tracking
            foreach (var kvp in _values)
            {
                _baselineValues[kvp.Key] = kvp.Value;
            }

            // Set up file watcher for hot reload
            SetupFileWatcher();
        }

        public T Get<T>(string key)
        {
            object value;
            lock (_valuesLock)
            {
                if (!_values.TryGetValue(key, out value))
                {
                    if (_schema.TryGetValue(key, out var field))
                        value = field.Default;
                    else
                        throw new KeyNotFoundException($"Config key not found: {key}");
                }
            }

            return ConvertValue<T>(value);
        }

        public T Get<T>(string key, T defaultValue)
        {
            object value;
            lock (_valuesLock)
            {
                if (!_values.TryGetValue(key, out value))
                {
                    if (_schema.TryGetValue(key, out var field))
                        value = field.Default;
                    else
                        return defaultValue;
                }
            }

            try
            {
                return ConvertValue<T>(value);
            }
            catch (Exception ex)
            {
                _log.Warn($"[{_modId}] Config key '{key}': failed to convert value '{value}' to {typeof(T).Name}, using default. {ex.Message}");
                return defaultValue;
            }
        }

        public bool TryGet<T>(string key, out T value)
        {
            value = default;
            try
            {
                value = Get<T>(key);
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _log.Warn($"[{_modId}] Config key '{key}': failed to get as {typeof(T).Name}. {ex.Message}");
                return false;
            }
        }

        public bool HasKey(string key)
        {
            lock (_valuesLock)
            {
                return _values.ContainsKey(key) || _schema.ContainsKey(key);
            }
        }

        /// <summary>
        /// Check if any values have changed from the baseline (startup values).
        /// Used to determine if restart is required for mods without hot reload.
        /// </summary>
        public bool HasChangesFromBaseline()
        {
            lock (_valuesLock)
            {
                foreach (var kvp in _values)
                {
                    if (!_baselineValues.TryGetValue(kvp.Key, out var baseline))
                    {
                        return true; // New key added
                    }

                    if (!ValuesEqual(kvp.Value, baseline))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // Handle boolean comparisons (could be stored as different types)
            if (a is bool ba && b is bool bb)
                return ba == bb;

            // Handle numeric comparisons (int/double/float can be mixed)
            if (IsNumeric(a) && IsNumeric(b))
            {
                try
                {
                    double da = Convert.ToDouble(a);
                    double db = Convert.ToDouble(b);
                    return Math.Abs(da - db) < 0.0001;
                }
                catch (InvalidCastException) { }
                catch (OverflowException) { }
            }

            // String comparison
            if (a is string sa && b is string sb)
                return sa == sb;

            return Equals(a, b);
        }

        private static bool IsNumeric(object o)
        {
            return o is int || o is long || o is float || o is double || o is decimal;
        }

        public void Set<T>(string key, T value)
        {
            object boxed = value;

            // Validate and clamp if schema exists
            if (_schema.TryGetValue(key, out var field))
            {
                boxed = field.Clamp(boxed);
            }

            bool changed;
            lock (_valuesLock)
            {
                var oldValue = _values.ContainsKey(key) ? _values[key] : null;
                _values[key] = boxed;
                changed = !Equals(oldValue, boxed);
                if (changed) _dirty = true;
            }

            // Fire event outside lock to prevent deadlocks
            if (changed)
            {
                OnValueChanged?.Invoke(key);
            }
        }

        public void Save()
        {
            try
            {
                string content;
                lock (_valuesLock)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("{");

                    int count = 0;
                    foreach (var kvp in _values)
                    {
                        count++;
                        string valueStr = SerializeValue(kvp.Value);
                        string comma = count < _values.Count ? "," : "";
                        sb.AppendLine($"  \"{kvp.Key}\": {valueStr}{comma}");
                    }

                    sb.AppendLine("}");
                    content = sb.ToString();
                    _dirty = false;
                }

                // File I/O outside lock
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(FilePath, content);
                _lastWriteTime = File.GetLastWriteTime(FilePath);

                _log.Debug($"[{_modId}] Config saved: {FilePath}");
            }
            catch (Exception ex)
            {
                _log.Error($"[{_modId}] Failed to save config '{FilePath}': {ex.Message}");
            }
        }

        public void Reload()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    lock (_valuesLock)
                    {
                        ParseJson(json);
                        _lastWriteTime = File.GetLastWriteTime(FilePath);
                        _dirty = false;
                    }
                    _log.Debug($"[{_modId}] Config reloaded from {FilePath}");
                    OnConfigReloaded?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[{_modId}] Failed to reload config '{FilePath}': {ex.Message}");
            }
        }

        public void ResetToDefaults()
        {
            lock (_valuesLock)
            {
                _values.Clear();
                foreach (var kvp in _schema)
                {
                    _values[kvp.Key] = kvp.Value.Default;
                }
                _dirty = true;
            }
        }

        private void LoadOrCreate()
        {
            // Initialize with defaults from schema
            foreach (var kvp in _schema)
            {
                _values[kvp.Key] = kvp.Value.Default;
            }

            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    ParseJson(json);
                    _lastWriteTime = File.GetLastWriteTime(FilePath);
                    _log.Debug($"[{_modId}] Config loaded from {FilePath} with {_values.Count} values");
                }
                catch (Exception ex)
                {
                    _log.Warn($"[{_modId}] Failed to load config '{FilePath}', using defaults: {ex.Message}");
                }
            }
            else
            {
                // Create default config file
                _log.Info($"[{_modId}] Creating default config file: {FilePath}");
                Save();
            }
        }

        private void ParseJson(string json)
        {
            // Simple JSON parser for flat config objects
            // Use lookahead (?=...) instead of non-capturing group (?:...) to avoid consuming the next key's quote
            var pattern = @"""(\w+)""\s*:\s*(.+?)(?=,\s*""|,?\s*\})";
            var matches = Regex.Matches(json, pattern, RegexOptions.Singleline);

            _log.Debug($"ParseJson found {matches.Count} matches");

            foreach (Match match in matches)
            {
                string key = match.Groups[1].Value;
                string valueStr = match.Groups[2].Value.Trim();

                object value = DeserializeValue(valueStr, key);
                if (value != null)
                {
                    // Validate against schema
                    if (_schema.TryGetValue(key, out var field))
                    {
                        if (!field.Validate(value, out string error))
                        {
                            _log.Warn($"Config [{key}]: {error}, using default");
                            value = field.Default;
                        }
                        else
                        {
                            value = field.Clamp(value);
                        }
                    }

                    _log.Debug($"Config parsed: {key} = {value}");
                    _values[key] = value;
                }
            }
        }

        private object DeserializeValue(string valueStr, string key)
        {
            // Remove trailing commas
            valueStr = valueStr.TrimEnd(',').Trim();

            // Determine type from schema if available
            ConfigFieldType type = ConfigFieldType.String;
            if (_schema.TryGetValue(key, out var field))
                type = field.Type;

            // Boolean
            if (valueStr == "true") return true;
            if (valueStr == "false") return false;

            // String (quoted)
            if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
            {
                return valueStr.Substring(1, valueStr.Length - 2)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }

            // Number
            if (type == ConfigFieldType.Int && int.TryParse(valueStr, out int intVal))
                return intVal;

            if (double.TryParse(valueStr, out double numVal))
            {
                if (type == ConfigFieldType.Int)
                    return (int)numVal;
                return numVal;
            }

            return valueStr;
        }

        private string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is bool b) return b ? "true" : "false";
            if (value is int i) return i.ToString();
            if (value is double d) return d.ToString("0.######");
            if (value is float f) return f.ToString("0.######");
            if (value is string s) return $"\"{EscapeString(s)}\"";
            return $"\"{EscapeString(value.ToString())}\"";
        }

        private string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private T ConvertValue<T>(object value)
        {
            if (value == null) return default;
            if (value is T typed) return typed;

            var targetType = typeof(T);

            // Handle numeric conversions
            if (targetType == typeof(int))
            {
                if (value is double d) return (T)(object)(int)d;
                if (value is long l) return (T)(object)(int)l;
                if (int.TryParse(value.ToString(), out int i)) return (T)(object)i;
            }
            if (targetType == typeof(float))
            {
                if (value is double d) return (T)(object)(float)d;
                if (float.TryParse(value.ToString(), out float f)) return (T)(object)f;
            }
            if (targetType == typeof(double))
            {
                if (double.TryParse(value.ToString(), out double d)) return (T)(object)d;
            }
            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value.ToString(), out bool b)) return (T)(object)b;
            }
            if (targetType == typeof(string))
            {
                return (T)(object)value.ToString();
            }

            return (T)Convert.ChangeType(value, targetType);
        }

        private void SetupFileWatcher()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                var file = Path.GetFileName(FilePath);

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;

                var watcher = new FileSystemWatcher(dir, file);
                try
                {
                    watcher.NotifyFilter = NotifyFilters.LastWrite;
                    watcher.Changed += OnFileChanged;
                    watcher.EnableRaisingEvents = true;
                    _watcher = watcher;
                }
                catch
                {
                    watcher.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"[{_modId}] Could not set up config file watcher for '{FilePath}': {ex.Message}");
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                // Debounce - check if file was actually modified
                var writeTime = File.GetLastWriteTime(FilePath);
                if (writeTime <= _lastWriteTime) return;

                // Don't reload if we just saved
                if (_dirty) return;

                _log.Info($"[{_modId}] Config file changed externally, reloading: {FilePath}");
                Reload();
            }
            catch (Exception ex)
            {
                _log.Error($"[{_modId}] Error handling config file change for '{FilePath}': {ex.Message}");
            }
        }

        public void Dispose()
        {
            var watcher = _watcher;
            if (watcher != null)
            {
                _watcher = null;
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileChanged;
                watcher.Dispose();
            }
        }
    }
}
