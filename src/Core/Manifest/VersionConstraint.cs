using System;
using System.Text.RegularExpressions;

namespace TerrariaModder.Core.Manifest
{
    /// <summary>
    /// Parses and evaluates version constraints like ">=1.0.0", "^1.0.0", "~1.0.0".
    /// </summary>
    public class VersionConstraint
    {
        private readonly string _operator;
        private readonly Version _version;
        private readonly Version _upperBound;

        /// <summary>
        /// The original constraint string.
        /// </summary>
        public string Original { get; }

        /// <summary>
        /// Whether the constraint was parsed successfully.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Parse error message if not valid.
        /// </summary>
        public string Error { get; }

        private VersionConstraint(string original, string op, Version version, Version upperBound = null)
        {
            Original = original;
            _operator = op;
            _version = version;
            _upperBound = upperBound;
            IsValid = true;
        }

        private VersionConstraint(string original, string error)
        {
            Original = original;
            IsValid = false;
            Error = error;
        }

        /// <summary>
        /// Parse a version constraint string.
        /// Supports: >=, >, =, ~, ^ operators and ranges with comma.
        /// </summary>
        public static VersionConstraint Parse(string constraint)
        {
            if (string.IsNullOrWhiteSpace(constraint))
            {
                return new VersionConstraint(constraint, "empty", null);
            }

            constraint = constraint.Trim();

            // Handle range: ">=1.0.0,<2.0.0"
            if (constraint.Contains(","))
            {
                var parts = constraint.Split(',');
                if (parts.Length != 2)
                {
                    return new VersionConstraint(constraint, "Invalid range format");
                }

                var lower = Parse(parts[0].Trim());
                var upper = Parse(parts[1].Trim());

                if (!lower.IsValid) return lower;
                if (!upper.IsValid) return upper;

                return new VersionConstraint(constraint, "range", lower._version, upper._version);
            }

            // Pattern: operator + version
            var match = Regex.Match(constraint, @"^(>=|>|<=|<|=|~|\^)?(\d+(?:\.\d+(?:\.\d+)?)?)$");
            if (!match.Success)
            {
                return new VersionConstraint(constraint, $"Invalid format: {constraint}");
            }

            string op = match.Groups[1].Value;
            string versionStr = match.Groups[2].Value;

            if (string.IsNullOrEmpty(op))
            {
                op = ">="; // Default to >= if no operator
            }

            if (!TryParseVersion(versionStr, out var version))
            {
                return new VersionConstraint(constraint, $"Invalid version: {versionStr}");
            }

            // Calculate upper bound for ~ and ^
            Version upperBound = null;
            if (op == "~")
            {
                // ~1.2.3 means >=1.2.3 <1.3.0
                upperBound = new Version(version.Major, version.Minor + 1, 0);
                op = ">=";
            }
            else if (op == "^")
            {
                // ^1.2.3 means >=1.2.3 <2.0.0
                upperBound = new Version(version.Major + 1, 0, 0);
                op = ">=";
            }

            return new VersionConstraint(constraint, op, version, upperBound);
        }

        /// <summary>
        /// Check if a version satisfies this constraint.
        /// </summary>
        public bool IsSatisfiedBy(string versionString)
        {
            if (!IsValid) return false;
            if (string.IsNullOrEmpty(versionString)) return false;

            if (!TryParseVersion(versionString, out var version))
            {
                return false;
            }

            return IsSatisfiedBy(version);
        }

        /// <summary>
        /// Check if a version satisfies this constraint.
        /// </summary>
        public bool IsSatisfiedBy(Version version)
        {
            if (!IsValid) return false;
            if (version == null) return false;

            bool satisfiesLower;
            switch (_operator)
            {
                case ">=":
                    satisfiesLower = version >= _version;
                    break;
                case ">":
                    satisfiesLower = version > _version;
                    break;
                case "=":
                    satisfiesLower = version == _version;
                    break;
                case "<":
                    satisfiesLower = version < _version;
                    break;
                case "<=":
                    satisfiesLower = version <= _version;
                    break;
                case "range":
                    satisfiesLower = version >= _version;
                    break;
                case "empty":
                    return true; // Empty constraint matches everything
                default:
                    return false;
            }

            if (!satisfiesLower) return false;

            // Check upper bound if present
            if (_upperBound != null)
            {
                return version < _upperBound;
            }

            return true;
        }

        private static bool TryParseVersion(string versionStr, out Version version)
        {
            version = null;

            // Ensure we have at least major.minor
            var parts = versionStr.Split('.');
            if (parts.Length == 1)
            {
                versionStr = versionStr + ".0.0";
            }
            else if (parts.Length == 2)
            {
                versionStr = versionStr + ".0";
            }

            return Version.TryParse(versionStr, out version);
        }

        public override string ToString() => Original;
    }
}
