using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Config
{
    /// <summary>
    /// Parses config schema from mod.json config_schema field.
    /// </summary>
    public static class ConfigSchema
    {
        /// <summary>
        /// Parse config schema from the raw JSON object.
        /// </summary>
        public static Dictionary<string, ConfigField> Parse(string configSchemaJson, ILogger log)
        {
            var result = new Dictionary<string, ConfigField>();

            if (string.IsNullOrWhiteSpace(configSchemaJson))
                return result;

            try
            {
                // Parse each field from the JSON
                // Expected format: { "fieldName": { "type": "...", "default": ..., ... }, ... }
                var fieldPattern = @"""(\w+)""\s*:\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}";
                var matches = Regex.Matches(configSchemaJson, fieldPattern);

                foreach (Match match in matches)
                {
                    string fieldName = match.Groups[1].Value;
                    string fieldJson = match.Groups[2].Value;

                    var field = ParseField(fieldName, fieldJson, log);
                    if (field != null)
                    {
                        result[fieldName] = field;
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Warn($"Failed to parse config schema: {ex.Message}");
            }

            return result;
        }

        private static ConfigField ParseField(string name, string json, ILogger log)
        {
            var field = new ConfigField { Key = name };

            // Extract type
            string typeStr = ExtractString(json, "type")?.ToLowerInvariant() ?? "string";
            field.Type = ParseFieldType(typeStr);

            // Extract label and description
            field.Label = ExtractString(json, "label") ?? name;
            field.Description = ExtractString(json, "description") ?? "";

            // Extract default
            field.Default = ExtractDefault(json, field.Type);

            // Extract constraints based on type
            switch (field.Type)
            {
                case ConfigFieldType.Int:
                case ConfigFieldType.Float:
                    field.Min = ExtractNumber(json, "min");
                    field.Max = ExtractNumber(json, "max");
                    field.Step = ExtractNumber(json, "step");
                    break;

                case ConfigFieldType.String:
                    field.MaxLength = (int?)ExtractNumber(json, "maxLength");
                    field.Pattern = ExtractString(json, "pattern");
                    break;

                case ConfigFieldType.Enum:
                    field.Options = ExtractStringArray(json, "options");
                    break;
            }

            return field;
        }

        private static ConfigFieldType ParseFieldType(string typeStr)
        {
            switch (typeStr)
            {
                case "bool":
                case "boolean":
                    return ConfigFieldType.Bool;
                case "int":
                case "integer":
                    return ConfigFieldType.Int;
                case "float":
                case "number":
                case "double":
                    return ConfigFieldType.Float;
                case "key":
                    return ConfigFieldType.Key;
                case "enum":
                    return ConfigFieldType.Enum;
                default:
                    return ConfigFieldType.String;
            }
        }

        private static object ExtractDefault(string json, ConfigFieldType type)
        {
            // Look for "default": value
            var match = Regex.Match(json, @"""default""\s*:\s*(.+?)(?:,|\s*$)");
            if (!match.Success) return GetTypeDefault(type);

            string valueStr = match.Groups[1].Value.Trim();

            // Remove trailing comma if present
            if (valueStr.EndsWith(","))
                valueStr = valueStr.Substring(0, valueStr.Length - 1).Trim();

            switch (type)
            {
                case ConfigFieldType.Bool:
                    return valueStr.ToLowerInvariant() == "true";

                case ConfigFieldType.Int:
                    if (int.TryParse(valueStr, out int intVal))
                        return intVal;
                    return 0;

                case ConfigFieldType.Float:
                    if (double.TryParse(valueStr, out double floatVal))
                        return floatVal;
                    return 0.0;

                case ConfigFieldType.String:
                case ConfigFieldType.Key:
                case ConfigFieldType.Enum:
                    // Remove quotes
                    if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                        return valueStr.Substring(1, valueStr.Length - 2);
                    return valueStr;

                default:
                    return null;
            }
        }

        private static object GetTypeDefault(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Bool: return false;
                case ConfigFieldType.Int: return 0;
                case ConfigFieldType.Float: return 0.0;
                case ConfigFieldType.String: return "";
                case ConfigFieldType.Key: return "";
                case ConfigFieldType.Enum: return "";
                default: return null;
            }
        }

        private static string ExtractString(string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"([^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"";
            var match = Regex.Match(json, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }
            return null;
        }

        private static double? ExtractNumber(string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*([\\d.\\-]+)";
            var match = Regex.Match(json, pattern);
            if (match.Success && double.TryParse(match.Groups[1].Value, out double val))
            {
                return val;
            }
            return null;
        }

        private static List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            var pattern = $"\"{key}\"\\s*:\\s*\\[([^\\]]*)\\]";
            var match = Regex.Match(json, pattern);
            if (match.Success)
            {
                string arrayContent = match.Groups[1].Value;
                var stringPattern = "\"([^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"";
                var stringMatches = Regex.Matches(arrayContent, stringPattern);
                foreach (Match stringMatch in stringMatches)
                {
                    result.Add(stringMatch.Groups[1].Value);
                }
            }
            return result;
        }
    }
}
