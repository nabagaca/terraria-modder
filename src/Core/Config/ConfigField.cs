using System.Collections.Generic;

namespace TerrariaModder.Core.Config
{
    /// <summary>
    /// Metadata for a config field from the schema.
    /// </summary>
    public class ConfigField
    {
        /// <summary>Field key/name.</summary>
        public string Key { get; set; }

        /// <summary>Field type.</summary>
        public ConfigFieldType Type { get; set; }

        /// <summary>Default value.</summary>
        public object Default { get; set; }

        /// <summary>Display label for UI.</summary>
        public string Label { get; set; }

        /// <summary>Description for UI tooltip.</summary>
        public string Description { get; set; }

        // Numeric constraints
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }

        // String constraints
        public int? MaxLength { get; set; }
        public string Pattern { get; set; }

        // Enum options
        public List<string> Options { get; set; }

        /// <summary>
        /// Validate a value against this field's constraints.
        /// </summary>
        public bool Validate(object value, out string error)
        {
            error = null;

            if (value == null)
            {
                error = "Value cannot be null";
                return false;
            }

            switch (Type)
            {
                case ConfigFieldType.Bool:
                    if (!(value is bool))
                    {
                        error = "Expected boolean";
                        return false;
                    }
                    break;

                case ConfigFieldType.Int:
                    if (!TryConvertToDouble(value, out double intVal))
                    {
                        error = "Expected integer";
                        return false;
                    }
                    if (Min.HasValue && intVal < Min.Value)
                    {
                        error = $"Value must be >= {Min.Value}";
                        return false;
                    }
                    if (Max.HasValue && intVal > Max.Value)
                    {
                        error = $"Value must be <= {Max.Value}";
                        return false;
                    }
                    break;

                case ConfigFieldType.Float:
                    if (!TryConvertToDouble(value, out double floatVal))
                    {
                        error = "Expected number";
                        return false;
                    }
                    if (Min.HasValue && floatVal < Min.Value)
                    {
                        error = $"Value must be >= {Min.Value}";
                        return false;
                    }
                    if (Max.HasValue && floatVal > Max.Value)
                    {
                        error = $"Value must be <= {Max.Value}";
                        return false;
                    }
                    break;

                case ConfigFieldType.String:
                case ConfigFieldType.Key:
                    string strVal = value?.ToString() ?? "";
                    if (MaxLength.HasValue && strVal.Length > MaxLength.Value)
                    {
                        error = $"String too long (max {MaxLength.Value})";
                        return false;
                    }
                    break;

                case ConfigFieldType.Enum:
                    string enumVal = value?.ToString() ?? "";
                    if (Options != null && !Options.Contains(enumVal))
                    {
                        error = $"Invalid option: {enumVal}. Valid: {string.Join(", ", Options)}";
                        return false;
                    }
                    break;
            }

            return true;
        }

        /// <summary>
        /// Clamp a value to valid range.
        /// </summary>
        public object Clamp(object value)
        {
            if (value == null) return Default;

            switch (Type)
            {
                case ConfigFieldType.Int:
                    if (TryConvertToDouble(value, out double intVal))
                    {
                        if (Min.HasValue) intVal = System.Math.Max(intVal, Min.Value);
                        if (Max.HasValue) intVal = System.Math.Min(intVal, Max.Value);
                        return (int)intVal;
                    }
                    break;

                case ConfigFieldType.Float:
                    if (TryConvertToDouble(value, out double floatVal))
                    {
                        if (Min.HasValue) floatVal = System.Math.Max(floatVal, Min.Value);
                        if (Max.HasValue) floatVal = System.Math.Min(floatVal, Max.Value);
                        return floatVal;
                    }
                    break;

                case ConfigFieldType.Enum:
                    string enumVal = value?.ToString() ?? "";
                    if (Options != null && !Options.Contains(enumVal))
                    {
                        return Default;
                    }
                    break;
            }

            return value;
        }

        private static bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value is double d) { result = d; return true; }
            if (value is float f) { result = f; return true; }
            if (value is int i) { result = i; return true; }
            if (value is long l) { result = l; return true; }
            if (value is decimal dec) { result = (double)dec; return true; }
            if (double.TryParse(value?.ToString(), out result)) return true;
            return false;
        }
    }
}
