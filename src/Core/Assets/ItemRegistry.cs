using System;
using System.Collections.Generic;
using System.Linq;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Central registry mapping string item IDs ("modid:itemname") to runtime type IDs (6145+).
    /// Type assignment is deterministic: all registered items sorted alphabetically, assigned sequentially.
    /// This means same mods installed = same type assignments every time, regardless of load order.
    /// </summary>
    public static class ItemRegistry
    {
        private static ILogger _log;

        // Registered definitions: fullId ("modid:itemname") → definition
        private static readonly Dictionary<string, ItemDefinition> _definitions
            = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);

        // Mod folder paths for texture loading
        private static readonly Dictionary<string, string> _modFolders
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Per-mod item lists for enumeration/unload
        private static readonly Dictionary<string, List<string>> _modItems
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Runtime type mappings (populated by AssignRuntimeTypes)
        private static readonly Dictionary<string, int> _idToType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _typeToId = new Dictionary<int, string>();
        private static readonly Dictionary<int, ItemDefinition> _typeToDefinition = new Dictionary<int, ItemDefinition>();

        /// <summary>First vanilla item count (before extension). Custom types start here.</summary>
        public static int VanillaItemCount { get; private set; }

        /// <summary>Whether runtime types have been assigned.</summary>
        public static bool TypesAssigned { get; private set; }

        /// <summary>Number of registered custom items.</summary>
        public static int Count => _definitions.Count;

        /// <summary>All registered full IDs.</summary>
        public static IEnumerable<string> AllIds => _definitions.Keys;

        public static void Initialize(ILogger logger, int vanillaItemCount)
        {
            _log = logger;
            VanillaItemCount = vanillaItemCount;
        }

        /// <summary>
        /// Register a custom item definition.
        /// Called by mods during Initialize() via ModContext.RegisterItem().
        /// </summary>
        public static bool Register(string modId, string itemName, ItemDefinition definition)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(itemName) || definition == null)
            {
                _log?.Warn("[ItemRegistry] Invalid registration: null modId, itemName, or definition");
                return false;
            }

            string error = definition.Validate();
            if (error != null)
            {
                _log?.Warn($"[ItemRegistry] Validation failed for {modId}:{itemName}: {error}");
                return false;
            }

            string fullId = $"{modId}:{itemName}";

            if (_definitions.ContainsKey(fullId))
            {
                _log?.Warn($"[ItemRegistry] Duplicate item: {fullId}");
                return false;
            }

            if (TypesAssigned)
            {
                _log?.Warn($"[ItemRegistry] Cannot register {fullId}: types already assigned");
                return false;
            }

            _definitions[fullId] = definition;

            if (!_modItems.TryGetValue(modId, out var list))
            {
                list = new List<string>();
                _modItems[modId] = list;
            }
            list.Add(itemName);

            _log?.Debug($"[ItemRegistry] Registered: {fullId} ({definition.DisplayName})");
            return true;
        }

        /// <summary>
        /// Register a mod's folder path (for texture loading).
        /// </summary>
        public static void RegisterModFolder(string modId, string folderPath)
        {
            _modFolders[modId] = folderPath;
        }

        /// <summary>
        /// Assign deterministic runtime type IDs to all registered items.
        /// Called once after all mods have loaded.
        /// </summary>
        public static void AssignRuntimeTypes()
        {
            if (TypesAssigned) return;
            if (_definitions.Count == 0)
            {
                _log?.Info("[ItemRegistry] No custom items registered");
                TypesAssigned = true;
                return;
            }

            _idToType.Clear();
            _typeToId.Clear();
            _typeToDefinition.Clear();

            // Sort all IDs alphabetically for deterministic assignment
            var sortedIds = _definitions.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            for (int i = 0; i < sortedIds.Count; i++)
            {
                int runtimeType = VanillaItemCount + i;
                string fullId = sortedIds[i];

                _idToType[fullId] = runtimeType;
                _typeToId[runtimeType] = fullId;
                _typeToDefinition[runtimeType] = _definitions[fullId];
            }

            TypesAssigned = true;
            _log?.Info($"[ItemRegistry] Assigned {sortedIds.Count} runtime types ({VanillaItemCount} - {VanillaItemCount + sortedIds.Count - 1})");

            // Log all assignments at Debug level
            foreach (var id in sortedIds)
            {
                _log?.Debug($"[ItemRegistry]   {id} → type {_idToType[id]}");
            }
        }

        // ── Lookups ──

        /// <summary>Get runtime type for a full item ID. Returns -1 if not found.</summary>
        public static int GetRuntimeType(string fullId)
        {
            return _idToType.TryGetValue(fullId, out int type) ? type : -1;
        }

        /// <summary>Get full item ID for a runtime type. Returns null if not found.</summary>
        public static string GetFullId(int runtimeType)
        {
            return _typeToId.TryGetValue(runtimeType, out string id) ? id : null;
        }

        /// <summary>Get definition for a runtime type. Returns null if not found.</summary>
        public static ItemDefinition GetDefinition(int runtimeType)
        {
            return _typeToDefinition.TryGetValue(runtimeType, out var def) ? def : null;
        }

        /// <summary>Get definition by full ID. Returns null if not found.</summary>
        public static ItemDefinition GetDefinitionById(string fullId)
        {
            return _definitions.TryGetValue(fullId, out var def) ? def : null;
        }

        /// <summary>Check if a runtime type is a custom item.</summary>
        public static bool IsCustomItem(int type) => type >= VanillaItemCount && _typeToId.ContainsKey(type);

        /// <summary>Get mod folder path for texture loading.</summary>
        public static string GetModFolder(string modId)
        {
            return _modFolders.TryGetValue(modId, out var path) ? path : null;
        }

        /// <summary>Get all item names registered by a specific mod.</summary>
        public static IEnumerable<string> GetItemsForMod(string modId)
        {
            return _modItems.TryGetValue(modId, out var list)
                ? (IEnumerable<string>)list
                : Array.Empty<string>();
        }

        /// <summary>
        /// Resolve an item reference to a runtime type.
        /// Accepts "modid:itemname" (custom) or vanilla item type ID (int).
        /// Returns -1 if unresolvable.
        /// </summary>
        public static int ResolveItemType(string itemRef)
        {
            if (string.IsNullOrEmpty(itemRef)) return -1;

            // Try as custom item first
            int customType = GetRuntimeType(itemRef);
            if (customType >= 0) return customType;

            // Try as vanilla int type
            if (int.TryParse(itemRef, out int vanillaType) && vanillaType > 0 && vanillaType < VanillaItemCount)
                return vanillaType;

            // Try resolving vanilla item name via reflection
            return ResolveVanillaItemName(itemRef);
        }

        private static Dictionary<string, int> _vanillaNameCache;

        private static int ResolveVanillaItemName(string name)
        {
            if (_vanillaNameCache == null)
            {
                _vanillaNameCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var itemIdType = typeof(Terraria.ID.ItemID);
                    foreach (var field in itemIdType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    {
                        if (field.FieldType == typeof(short))
                        {
                            short val = (short)field.GetValue(null);
                            if (val > 0 && val < VanillaItemCount)
                                _vanillaNameCache[field.Name] = val;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Warn($"[ItemRegistry] Failed to build vanilla name cache: {ex.Message}");
                }
            }

            // Try exact match and common variations
            if (_vanillaNameCache.TryGetValue(name, out int id)) return id;

            // Try with spaces removed
            string noSpaces = name.Replace(" ", "");
            if (_vanillaNameCache.TryGetValue(noSpaces, out id)) return id;

            return -1;
        }

        /// <summary>Clear all registrations (for testing/reload).</summary>
        public static void Clear()
        {
            _definitions.Clear();
            _modItems.Clear();
            _idToType.Clear();
            _typeToId.Clear();
            _typeToDefinition.Clear();
            TypesAssigned = false;
            _vanillaNameCache = null;
        }
    }
}
