using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TerrariaModder.Core.Logging;
using StorageHub.Storage;

namespace StorageHub.Crafting
{
    /// <summary>
    /// Executes crafting operations by consuming materials and creating items.
    ///
    /// Design principles:
    /// - Consumes materials from ALL registered storage via IStorageProvider
    /// - Consumes materials first, then creates items
    /// - Returns created item to player's inventory or mouse cursor
    /// </summary>
    public class CraftingExecutor
    {
        private readonly ILogger _log;
        private readonly IStorageProvider _storage;

        // Reflection cache
        private static Type _mainType;
        private static Type _itemType;
        private static Type _playerType;
        private static FieldInfo _playerArrayField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerInventoryField;
        private static FieldInfo _itemTypeField;
        private static FieldInfo _itemStackField;
        private static FieldInfo _itemPrefixField;
        private static FieldInfo _mouseItemField;
        private static MethodInfo _setDefaultsMethod;
        private static MethodInfo _quickSpawnMethod;
        private static Type _entitySourceType;
        private static bool _initialized;
        private static bool _initFailed;

        public CraftingExecutor(ILogger log, IStorageProvider storage)
        {
            _log = log;
            _storage = storage;
            if (!_initialized && !_initFailed)
            {
                InitReflection();
            }
        }

        private void InitReflection()
        {
            try
            {
                _mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");
                _itemType = Type.GetType("Terraria.Item, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Item");
                _playerType = Type.GetType("Terraria.Player, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Player");

                if (_mainType != null)
                {
                    _playerArrayField = _mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                    _myPlayerField = _mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                    _mouseItemField = _mainType.GetField("mouseItem", BindingFlags.Public | BindingFlags.Static);
                }

                if (_playerType != null)
                {
                    _playerInventoryField = _playerType.GetField("inventory", BindingFlags.Public | BindingFlags.Instance);

                    // Find QuickSpawnItem method (the reliable way to give items to players)
                    // List all QuickSpawnItem overloads for debugging
                    var allQSI = _playerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "QuickSpawnItem")
                        .ToList();

                    // Find the one with (IEntitySource, int itemType, int stack) signature
                    _quickSpawnMethod = allQSI.FirstOrDefault(m =>
                    {
                        var ps = m.GetParameters();
                        return ps.Length == 3 &&
                               ps[1].ParameterType == typeof(int) &&
                               ps[2].ParameterType == typeof(int);
                    });

                    if (_quickSpawnMethod != null)
                    {
                        var parms = _quickSpawnMethod.GetParameters();
                        _entitySourceType = parms[0].ParameterType;
                    }
                }

                if (_itemType != null)
                {
                    _itemTypeField = _itemType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                    _itemStackField = _itemType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);
                    _itemPrefixField = _itemType.GetField("prefix", BindingFlags.Public | BindingFlags.Instance);
                    // SetDefaults signature is SetDefaults(int Type, ItemVariant variant = null)
                    // Must search by name since GetMethod with exact types won't match optional params
                    foreach (var m in _itemType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name == "SetDefaults")
                        {
                            var p = m.GetParameters();
                            if (p.Length >= 1 && p[0].ParameterType == typeof(int))
                            {
                                _setDefaultsMethod = m;
                                break;
                            }
                        }
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                _log.Error($"CraftingExecutor init failed: {ex.Message}");
                _initFailed = true;
            }
        }

        /// <summary>
        /// Execute a recipe craft, consuming materials from ALL storage and creating items.
        ///
        /// IMPORTANT: Uses two-phase commit to prevent item loss:
        /// 1. Build a consumption plan (which slots to take from)
        /// 2. Execute all takes atomically - if ANY fails, abort before modifying
        /// 3. Create output items (directly to inventory if intermediate step, or via QuickSpawnItem)
        /// </summary>
        /// <param name="recipe">Recipe to craft.</param>
        /// <param name="count">Number of times to craft the recipe.</param>
        /// <param name="directToInventory">If true, place output directly in player inventory
        /// instead of using QuickSpawnItem. Used for intermediate recursive craft steps
        /// so the output is immediately available to the next step.</param>
        /// <returns>True if crafting succeeded.</returns>
        public bool ExecuteCraft(RecipeInfo recipe, int count = 1, bool directToInventory = false)
        {
            if (recipe == null || count <= 0) return false;

            try
            {
                var player = GetLocalPlayer();
                if (player == null)
                {
                    _log.Error("Cannot craft: player not found");
                    return false;
                }

                // Get all items from storage
                var allItems = _storage.GetAllItems();

                // PHASE 1: Build consumption plan - identify exactly which slots we'll consume from
                var consumptionPlan = new List<ConsumptionEntry>();
                foreach (var ing in recipe.Ingredients)
                {
                    // Use long to prevent overflow, then validate
                    long neededLong = (long)ing.RequiredStack * count;
                    if (neededLong > int.MaxValue || neededLong < 0)
                    {
                        _log.Error($"Overflow calculating materials for {ing.Name}: {ing.RequiredStack} * {count}");
                        return false;
                    }
                    int needed = (int)neededLong;
                    int remaining = needed;

                    // Find items matching this ingredient (supports recipe groups)
                    foreach (var item in allItems)
                    {
                        if (remaining <= 0) break;
                        if (item.IsEmpty) continue;

                        // Match: exact ID for normal ingredients, any valid ID for recipe groups
                        bool matches = ing.IsRecipeGroup && ing.ValidItemIds != null
                            ? ing.ValidItemIds.Contains(item.ItemId)
                            : item.ItemId == ing.ItemId;
                        if (!matches) continue;

                        int toTake = Math.Min(item.Stack, remaining);
                        consumptionPlan.Add(new ConsumptionEntry
                        {
                            SourceChestIndex = item.SourceChestIndex,
                            SourceSlot = item.SourceSlot,
                            ItemId = item.ItemId,
                            Amount = toTake,
                            ItemName = item.Name
                        });
                        remaining -= toTake;
                    }

                    if (remaining > 0)
                    {
                        _log.Warn($"Not enough {ing.Name}: need {needed}, can only get {needed - remaining}");
                        return false;
                    }
                }

                // PHASE 2: Execute all consumptions - all or nothing
                var consumed = new List<ConsumptionEntry>();
                bool allSucceeded = true;

                foreach (var entry in consumptionPlan)
                {
                    if (_storage.TakeItem(entry.SourceChestIndex, entry.SourceSlot, entry.Amount, out var taken))
                    {
                        var consumedEntry = entry;
                        consumedEntry.ActualTaken = taken.Stack;
                        consumed.Add(consumedEntry);
                    }
                    else
                    {
                        _log.Error($"Failed to take {entry.Amount}x {entry.ItemName} from {SourceIndex.GetSourceName(entry.SourceChestIndex)} slot {entry.SourceSlot}");
                        allSucceeded = false;
                        break;
                    }
                }

                // If any consumption failed, attempt to restore consumed items
                if (!allSucceeded && consumed.Count > 0)
                {
                    _log.Error($"Partial consumption failure - attempting to restore {consumed.Count} consumed items...");
                    int restored = 0;
                    int partialRestores = 0;
                    foreach (var entry in consumed)
                    {
                        var recovery = new ItemSnapshot(
                            entry.ItemId, entry.ActualTaken, 0,
                            entry.ItemName, 999, 0,
                            SourceIndex.PlayerInventory, 0);
                        int deposited = _storage.DepositItem(recovery, out _);
                        if (deposited >= entry.ActualTaken)
                            restored++;
                        else if (deposited > 0)
                            partialRestores++;
                    }
                    if (restored == consumed.Count)
                    {
                        _log.Info("All consumed items restored successfully");
                    }
                    else
                    {
                        _log.Error($"CRITICAL: Only fully restored {restored}/{consumed.Count} items ({partialRestores} partial) - some may be lost!");
                    }
                    return false;
                }
                else if (!allSucceeded)
                {
                    return false;
                }

                // PHASE 3: Create the output item and give to player
                // Use long to prevent overflow, then validate
                long totalOutputLong = (long)recipe.OutputStack * count;
                if (totalOutputLong > int.MaxValue || totalOutputLong < 0)
                {
                    _log.Error($"Overflow calculating output for {recipe.OutputName}: {recipe.OutputStack} * {count}");
                    return false;
                }
                int totalOutput = (int)totalOutputLong;

                // For intermediate recursive craft steps, place directly in inventory
                // so the next step can immediately find the items. QuickSpawnItem drops
                // items on the ground with auto-pickup, which has a timing delay.
                bool created = directToInventory
                    ? PlaceInInventoryDirect(player, recipe.OutputItemId, totalOutput)
                    : GiveItemToPlayer(player, recipe.OutputItemId, totalOutput);
                if (!created)
                {
                    // CRITICAL: Output failed - restore ALL consumed items
                    _log.Error($"Failed to give crafted item {recipe.OutputName} - restoring consumed materials...");
                    int restored = 0;
                    int partialRestores2 = 0;
                    foreach (var entry in consumed)
                    {
                        var recovery = new ItemSnapshot(
                            entry.ItemId, entry.ActualTaken, 0,
                            entry.ItemName, 999, 0,
                            SourceIndex.PlayerInventory, 0);
                        int deposited = _storage.DepositItem(recovery, out _);
                        if (deposited >= entry.ActualTaken)
                            restored++;
                        else if (deposited > 0)
                            partialRestores2++;
                    }
                    if (restored == consumed.Count)
                    {
                        _log.Info("All materials restored - craft aborted safely");
                    }
                    else
                    {
                        _log.Error($"CRITICAL: Only fully restored {restored}/{consumed.Count} material entries ({partialRestores2} partial) - some may be lost!");
                    }
                    return false;
                }

                _log.Info($"Crafted {totalOutput}x {recipe.OutputName}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"ExecuteCraft failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Entry in the consumption plan for two-phase commit.
        /// </summary>
        private struct ConsumptionEntry
        {
            public int SourceChestIndex;
            public int SourceSlot;
            public int ItemId;
            public int Amount;
            public int ActualTaken;
            public string ItemName;
        }
        private static void InvokeSetDefaults(object item, int type)
        {
            if (_setDefaultsMethod == null) return;
            var paramCount = _setDefaultsMethod.GetParameters().Length;
            if (paramCount == 1)
                _setDefaultsMethod.Invoke(item, new object[] { type });
            else
                _setDefaultsMethod.Invoke(item, new object[] { type, null });
        }

        /// <summary>
        /// Place items directly into player inventory slots (no QuickSpawnItem, no world drop).
        /// Items are immediately available for subsequent crafting steps.
        /// </summary>
        private bool PlaceInInventoryDirect(object player, int itemId, int stack)
        {
            try
            {
                var inventory = _playerInventoryField?.GetValue(player) as Array;
                if (inventory == null || _itemType == null || _setDefaultsMethod == null || _itemStackField == null)
                {
                    _log.Error("PlaceInInventoryDirect: reflection fields not available");
                    return false;
                }

                var maxStackField = _itemType.GetField("maxStack", BindingFlags.Public | BindingFlags.Instance);
                int remaining = stack;

                // First pass: stack with existing items of same type
                for (int i = 0; i < Math.Min(inventory.Length, 50) && remaining > 0; i++)
                {
                    var slot = inventory.GetValue(i);
                    if (slot == null) continue;

                    var slotTypeVal = _itemTypeField?.GetValue(slot);
                    int slotType = slotTypeVal != null ? (int)slotTypeVal : 0;
                    if (slotType != itemId) continue;

                    var slotStackVal = _itemStackField?.GetValue(slot);
                    int slotStack = slotStackVal != null ? (int)slotStackVal : 0;
                    var maxStackVal = maxStackField?.GetValue(slot);
                    int maxStack = maxStackVal != null ? (int)maxStackVal : 9999;
                    if (maxStack <= 0) maxStack = 9999;

                    if (slotStack < maxStack)
                    {
                        int toAdd = Math.Min(remaining, maxStack - slotStack);
                        _itemStackField.SetValue(slot, slotStack + toAdd);
                        remaining -= toAdd;
                    }
                }

                // Second pass: use empty slots
                for (int i = 0; i < Math.Min(inventory.Length, 50) && remaining > 0; i++)
                {
                    var slot = inventory.GetValue(i);
                    if (slot == null) continue;

                    var slotTypeVal = _itemTypeField?.GetValue(slot);
                    int slotType = slotTypeVal != null ? (int)slotTypeVal : -1;
                    if (slotType != 0) continue;

                    InvokeSetDefaults(slot, itemId);
                    var maxStackVal = maxStackField?.GetValue(slot);
                    int maxStack = maxStackVal != null ? (int)maxStackVal : 9999;
                    if (maxStack <= 0) maxStack = 9999;
                    int toPlace = Math.Min(remaining, maxStack);
                    _itemStackField.SetValue(slot, toPlace);
                    remaining -= toPlace;
                }

                if (remaining > 0)
                {
                    _log.Warn($"PlaceInInventoryDirect: inventory full, {remaining}x {itemId} could not be placed");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"PlaceInInventoryDirect failed: {ex.Message}");
                return false;
            }
        }

        private bool GiveItemToPlayer(object player, int itemId, int stack)
        {
            try
            {
                // PRIMARY METHOD: Use QuickSpawnItem(IEntitySource, Item) overload
                // The (IEntitySource, int, int) overload calls Prefix(-1) which randomizes prefix.
                // Crafted items should have no prefix (vanilla behavior).
                var quickSpawnItemMethod = _playerType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "QuickSpawnItem") return false;
                        var ps = m.GetParameters();
                        return ps.Length == 2 && ps[1].ParameterType == _itemType;
                    });

                // Fallback to (IEntitySource, int, int) if Item overload not found
                if (quickSpawnItemMethod == null)
                {
                    quickSpawnItemMethod = _playerType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "QuickSpawnItem") return false;
                            var ps = m.GetParameters();
                            return ps.Length == 3 && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(int);
                        });
                }

                if (quickSpawnItemMethod != null && player != null)
                {
                    // Create entity source
                    object source = CreateEntitySource(player);

                    if (source != null)
                    {
                        var qsiParams = quickSpawnItemMethod.GetParameters();
                        if (qsiParams.Length == 2 && qsiParams[1].ParameterType == _itemType)
                        {
                            // Item overload — construct unprefixed item
                            var item = Activator.CreateInstance(_itemType);
                            InvokeSetDefaults(item, itemId);
                            _itemStackField?.SetValue(item, stack);
                            // Ensure prefix is 0 (no random modifier)
                            _itemPrefixField?.SetValue(item, (byte)0);
                            quickSpawnItemMethod.Invoke(player, new object[] { source, item });
                        }
                        else
                        {
                            // (IEntitySource, int, int) fallback — prefix will be randomized
                            quickSpawnItemMethod.Invoke(player, new object[] { source, itemId, stack });
                        }
                        return true;
                    }
                }

                // FALLBACK: Put directly in inventory slots (loop to handle stack > maxStack)
                int fallbackRemaining = stack;
                var inventory = _playerInventoryField?.GetValue(player) as Array;
                if (inventory != null && _itemType != null && _setDefaultsMethod != null && _itemStackField != null)
                {
                    var maxStackField = _itemType.GetField("maxStack", BindingFlags.Public | BindingFlags.Instance);

                    for (int i = 0; i < Math.Min(inventory.Length, 50) && fallbackRemaining > 0; i++) // Main inventory is slots 0-49
                    {
                        var slot = inventory.GetValue(i);
                        if (slot == null) continue;

                        var slotTypeVal = _itemTypeField?.GetValue(slot);
                        int slotType = slotTypeVal != null ? (int)slotTypeVal : -1;
                        if (slotType == 0)
                        {
                            // Empty slot - use SetDefaults to properly initialize all item fields
                            InvokeSetDefaults(slot, itemId);
                            var maxStackVal = maxStackField?.GetValue(slot);
                            int maxStack = maxStackVal != null ? (int)maxStackVal : 9999;
                            if (maxStack <= 0) maxStack = 9999;
                            int toPlace = Math.Min(fallbackRemaining, maxStack);
                            _itemStackField.SetValue(slot, toPlace);
                            fallbackRemaining -= toPlace;
                        }
                    }

                    if (fallbackRemaining <= 0) return true;
                    _log.Warn($"Inventory full, {fallbackRemaining}x {itemId} could not be placed");
                }

                // LAST RESORT: Try mouse slot (use fallbackRemaining, not original stack)
                if (fallbackRemaining > 0 && _mouseItemField != null && _itemType != null)
                {
                    var mouseItem = _mouseItemField.GetValue(null);
                    if (mouseItem != null)
                    {
                        var mouseTypeVal = _itemTypeField?.GetValue(mouseItem);
                        int mouseType = mouseTypeVal != null ? (int)mouseTypeVal : -1;
                        if (mouseType == 0)
                        {
                            var newItem = Activator.CreateInstance(_itemType);
                            if (newItem != null && _setDefaultsMethod != null)
                            {
                                InvokeSetDefaults(newItem, itemId);
                                var maxStackVal = _itemType.GetField("maxStack", BindingFlags.Public | BindingFlags.Instance)?.GetValue(newItem);
                                int maxStack = maxStackVal != null ? (int)maxStackVal : 9999;
                                if (maxStack <= 0) maxStack = 9999;
                                _itemStackField?.SetValue(newItem, Math.Min(fallbackRemaining, maxStack));
                                _mouseItemField.SetValue(null, newItem);
                                return true;
                            }
                        }
                    }
                }

                _log.Error("All item placement methods failed");
                return false;
            }
            catch (Exception ex)
            {
                _log.Error($"GiveItemToPlayer failed: {ex.Message}");
                return false;
            }
        }

        private object CreateEntitySource(object player)
        {
            try
            {
                var entitySourceType = _mainType?.Assembly.GetType("Terraria.DataStructures.EntitySource_Parent");
                if (entitySourceType == null)
                    entitySourceType = _mainType?.Assembly.GetType("Terraria.DataStructures.EntitySource_Gift");

                if (entitySourceType != null)
                {
                    var ctor = entitySourceType.GetConstructors().FirstOrDefault();
                    if (ctor != null)
                    {
                        var ctorParms = ctor.GetParameters();
                        if (ctorParms.Length == 1)
                            return ctor.Invoke(new[] { player });
                        if (ctorParms.Length == 0)
                            return ctor.Invoke(null);
                    }
                }
            }
            catch { }
            return null;
        }

        private object GetLocalPlayer()
        {
            try
            {
                if (_myPlayerField == null || _playerArrayField == null)
                    return null;

                var myPlayerVal = _myPlayerField.GetValue(null);
                if (myPlayerVal == null) return null;

                int myPlayer = (int)myPlayerVal;
                var players = _playerArrayField.GetValue(null) as Array;
                if (players == null) return null;

                // Bounds check before accessing array
                if (myPlayer < 0 || myPlayer >= players.Length)
                    return null;

                return players.GetValue(myPlayer);
            }
            catch
            {
                return null;
            }
        }
    }
}
