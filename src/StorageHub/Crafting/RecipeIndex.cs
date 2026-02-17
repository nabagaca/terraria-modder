using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaModder.Core.Logging;

namespace StorageHub.Crafting
{
    /// <summary>
    /// Indexes all Terraria recipes for efficient lookup.
    /// Built once at world load, provides fast queries by output item or ingredient.
    ///
    /// Why this exists:
    /// - Iterating all 5000+ recipes every frame is expensive
    /// - Pre-indexed dictionaries enable O(1) lookup
    /// - Supports both "what can I make" and "where is this used" queries
    /// </summary>
    public class RecipeIndex
    {
        private readonly ILogger _log;

        // Recipe data cached from reflection
        private List<RecipeInfo> _allRecipes = new List<RecipeInfo>();

        // Index: Output item type -> List of recipe indices
        private Dictionary<int, List<int>> _byOutput = new Dictionary<int, List<int>>();

        // Index: Ingredient item type -> List of recipe indices that use it
        private Dictionary<int, List<int>> _byIngredient = new Dictionary<int, List<int>>();

        // Index: Required tile type -> List of recipe indices that require it
        private Dictionary<int, List<int>> _byStation = new Dictionary<int, List<int>>();

        // Reverse lookup: OriginalIndex (Terraria recipe array index) -> internal _allRecipes index
        private Dictionary<int, int> _originalToInternal = new Dictionary<int, int>();

        // Reflection cache
        private static Type _recipeType;
        private static Type _itemType;
        private static Type _mainType;
        private static FieldInfo _recipeArrayField;
        private static FieldInfo _numRecipesField;
        private static FieldInfo _createItemField;
        private static FieldInfo _requiredItemField;
        private static FieldInfo _requiredTileField;
        private static FieldInfo _needWaterField;
        private static FieldInfo _needHoneyField;
        private static FieldInfo _needLavaField;
        private static FieldInfo _needSnowBiomeField;
        private static FieldInfo _needGraveyardField;
        private static FieldInfo _needShimmerField;
        private static FieldInfo _itemTypeField;
        private static FieldInfo _itemStackField;
        private static PropertyInfo _itemNameProp;

        // Recipe group reflection cache
        private static FieldInfo _quickLookupField;       // Recipe.requiredItemQuickLookup (RequiredItemEntry[])
        private static FieldInfo _entryIdField;            // RequiredItemEntry.itemIdOrRecipeGroup
        private static FieldInfo _entryStackField;         // RequiredItemEntry.stack
        private static int _fakeItemIdOffset;              // RecipeGroup.FakeItemIdOffset (1000000)
        private static FieldInfo _recipeGroupsField;       // RecipeGroup.recipeGroups (Dictionary<int, RecipeGroup>)
        private static FieldInfo _validItemsField;         // RecipeGroup.ValidItems (HashSet<int>)
        private static bool _recipeGroupsAvailable;        // True if recipe group reflection succeeded

        public int TotalRecipes => _allRecipes.Count;

        /// <summary>
        /// The fake item ID offset used by recipe groups (1000000).
        /// IDs >= this value are recipe group fake IDs, not real item IDs.
        /// </summary>
        public int FakeItemIdOffset => _fakeItemIdOffset > 0 ? _fakeItemIdOffset : 1000000;

        /// <summary>Whether recipe groups are available (reflection succeeded).</summary>
        public bool HasRecipeGroupSupport => _recipeGroupsAvailable;

        public RecipeIndex(ILogger log)
        {
            _log = log;
        }

        /// <summary>
        /// Initialize reflection for recipe access.
        /// </summary>
        public bool InitReflection()
        {
            try
            {
                _mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");

                _recipeType = Type.GetType("Terraria.Recipe, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Recipe");

                _itemType = Type.GetType("Terraria.Item, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Item");

                if (_mainType == null || _recipeType == null || _itemType == null)
                {
                    _log.Error("RecipeIndex: Failed to find required types");
                    return false;
                }

                _recipeArrayField = _mainType.GetField("recipe", BindingFlags.Public | BindingFlags.Static);
                _numRecipesField = _recipeType.GetField("numRecipes", BindingFlags.Public | BindingFlags.Static);

                _createItemField = _recipeType.GetField("createItem", BindingFlags.Public | BindingFlags.Instance);
                _requiredItemField = _recipeType.GetField("requiredItem", BindingFlags.Public | BindingFlags.Instance);
                _requiredTileField = _recipeType.GetField("requiredTile", BindingFlags.Public | BindingFlags.Instance);

                _needWaterField = _recipeType.GetField("needWater", BindingFlags.Public | BindingFlags.Instance);
                _needHoneyField = _recipeType.GetField("needHoney", BindingFlags.Public | BindingFlags.Instance);
                _needLavaField = _recipeType.GetField("needLava", BindingFlags.Public | BindingFlags.Instance);
                _needSnowBiomeField = _recipeType.GetField("needSnowBiome", BindingFlags.Public | BindingFlags.Instance);
                _needGraveyardField = _recipeType.GetField("needGraveyardBiome", BindingFlags.Public | BindingFlags.Instance);
                // Note: needShimmer does NOT exist on vanilla 1.4.5 Recipe class.
                // Shimmer transmutation is handled separately (not through Recipe objects).
                // This field will be null, and GetBoolField returns false — harmless.
                _needShimmerField = _recipeType.GetField("needShimmer", BindingFlags.Public | BindingFlags.Instance);

                _itemTypeField = _itemType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                _itemStackField = _itemType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);
                _itemNameProp = _itemType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);

                // Recipe group support — optional, gracefully degrades if not available
                InitRecipeGroupReflection();

                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"RecipeIndex reflection error: {ex.Message}");
                return false;
            }
        }

        private void InitRecipeGroupReflection()
        {
            try
            {
                // RequiredItemEntry is a nested struct in Recipe
                var entryType = _recipeType.GetNestedType("RequiredItemEntry", BindingFlags.Public);
                if (entryType == null)
                {
                    _log.Warn("RecipeIndex: RequiredItemEntry not found — recipe groups unavailable");
                    return;
                }

                _quickLookupField = _recipeType.GetField("requiredItemQuickLookup", BindingFlags.Public | BindingFlags.Instance);
                _entryIdField = entryType.GetField("itemIdOrRecipeGroup", BindingFlags.Public | BindingFlags.Instance);
                _entryStackField = entryType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);

                var recipeGroupType = Type.GetType("Terraria.RecipeGroup, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.RecipeGroup");

                if (recipeGroupType != null)
                {
                    var offsetField = recipeGroupType.GetField("FakeItemIdOffset", BindingFlags.Public | BindingFlags.Static);
                    if (offsetField != null)
                    {
                        var val = offsetField.GetValue(null);
                        _fakeItemIdOffset = val != null ? (int)val : 1000000;
                    }
                    else
                    {
                        _fakeItemIdOffset = 1000000; // Known constant
                    }

                    _recipeGroupsField = recipeGroupType.GetField("recipeGroups", BindingFlags.Public | BindingFlags.Static);
                    _validItemsField = recipeGroupType.GetField("ValidItems", BindingFlags.Public | BindingFlags.Instance);
                }

                if (_quickLookupField != null && _entryIdField != null && _entryStackField != null)
                {
                    _recipeGroupsAvailable = true;
                    _log.Info($"RecipeIndex: Recipe group support enabled (FakeItemIdOffset={_fakeItemIdOffset})");
                }
                else
                {
                    _log.Warn("RecipeIndex: Incomplete recipe group reflection — falling back to exact matching");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"RecipeIndex: Recipe group init failed: {ex.Message} — falling back to exact matching");
            }
        }

        /// <summary>
        /// Build the recipe index from Terraria's recipe array.
        /// Call once at world load.
        /// </summary>
        public void Build()
        {
            _allRecipes.Clear();
            _byOutput.Clear();
            _byIngredient.Clear();
            _byStation.Clear();
            _originalToInternal.Clear();

            try
            {
                var recipeArray = _recipeArrayField?.GetValue(null) as Array;
                if (recipeArray == null)
                {
                    _log.Error("Cannot build recipe index: recipe array is null");
                    return;
                }

                var numRecipesVal = _numRecipesField?.GetValue(null);
                if (numRecipesVal == null)
                {
                    _log.Error("Cannot build recipe index: numRecipes field is null");
                    return;
                }

                // Safe type conversion with validation
                int numRecipes;
                if (numRecipesVal is int intVal)
                {
                    numRecipes = intVal;
                }
                else
                {
                    _log.Error($"Cannot build recipe index: numRecipes is not int, got {numRecipesVal.GetType().Name}");
                    return;
                }

                // Validate numRecipes is reasonable
                if (numRecipes < 0 || numRecipes > recipeArray.Length)
                {
                    _log.Error($"Invalid numRecipes: {numRecipes} (array length: {recipeArray.Length})");
                    return;
                }

                _log.Info($"Building recipe index from {numRecipes} recipes...");

                for (int i = 0; i < numRecipes; i++)
                {
                    var recipe = recipeArray.GetValue(i);
                    if (recipe == null) continue;

                    var info = ExtractRecipeInfo(recipe, i);
                    if (info == null || info.OutputItemId <= 0) continue;

                    _allRecipes.Add(info);
                    int recipeIdx = _allRecipes.Count - 1;
                    _originalToInternal[info.OriginalIndex] = recipeIdx;

                    // Index by output
                    if (!_byOutput.TryGetValue(info.OutputItemId, out var outputList))
                    {
                        outputList = new List<int>();
                        _byOutput[info.OutputItemId] = outputList;
                    }
                    outputList.Add(recipeIdx);

                    // Index by ingredient
                    foreach (var ing in info.Ingredients)
                    {
                        if (!_byIngredient.TryGetValue(ing.ItemId, out var ingList))
                        {
                            ingList = new List<int>();
                            _byIngredient[ing.ItemId] = ingList;
                        }
                        ingList.Add(recipeIdx);
                    }

                    // Index by station
                    foreach (var tile in info.RequiredTiles)
                    {
                        if (tile <= 0) continue;
                        if (!_byStation.TryGetValue(tile, out var stationList))
                        {
                            stationList = new List<int>();
                            _byStation[tile] = stationList;
                        }
                        stationList.Add(recipeIdx);
                    }
                }

                _log.Info($"Recipe index built: {_allRecipes.Count} recipes, {_byOutput.Count} unique outputs, {_byIngredient.Count} unique ingredients");
            }
            catch (Exception ex)
            {
                _log.Error($"Recipe index build error: {ex.Message}");
            }
        }

        private RecipeInfo ExtractRecipeInfo(object recipe, int originalIndex)
        {
            try
            {
                // Validate required fields
                if (_itemTypeField == null || _itemStackField == null)
                    return null;

                var info = new RecipeInfo { OriginalIndex = originalIndex };

                // Get output item
                var createItem = _createItemField?.GetValue(recipe);
                if (createItem != null)
                {
                    var outputTypeVal = _itemTypeField.GetValue(createItem);
                    var outputStackVal = _itemStackField.GetValue(createItem);
                    info.OutputItemId = outputTypeVal != null ? (int)outputTypeVal : 0;
                    info.OutputStack = outputStackVal != null ? (int)outputStackVal : 0;
                    info.OutputName = _itemNameProp?.GetValue(createItem) as string ?? "";
                }

                // Get ingredients — prefer requiredItemQuickLookup (has recipe group info)
                bool usedQuickLookup = false;
                if (_recipeGroupsAvailable)
                {
                    usedQuickLookup = ExtractIngredientsFromQuickLookup(recipe, info);
                }

                if (!usedQuickLookup)
                {
                    // Fallback: use requiredItem[] (no recipe group support)
                    var requiredItems = _requiredItemField?.GetValue(recipe) as Array;
                    if (requiredItems != null)
                    {
                        foreach (var item in requiredItems)
                        {
                            if (item == null) continue;

                            var typeVal = _itemTypeField.GetValue(item);
                            int type = typeVal != null ? (int)typeVal : 0;
                            if (type <= 0) break; // Empty slot marks end of ingredients

                            var stackVal = _itemStackField.GetValue(item);
                            int stack = stackVal != null ? (int)stackVal : 0;
                            string name = _itemNameProp?.GetValue(item) as string ?? "";

                            info.Ingredients.Add(new IngredientInfo
                            {
                                ItemId = type,
                                RequiredStack = stack,
                                Name = name
                            });
                        }
                    }
                }

                // Get required tile (single int in 1.4.5, -1 = none)
                var requiredTileVal = _requiredTileField?.GetValue(recipe);
                if (requiredTileVal is int tileId && tileId >= 0)
                {
                    info.RequiredTiles.Add(tileId);
                }

                // Get environmental requirements
                info.NeedWater = GetBoolField(_needWaterField, recipe);
                info.NeedHoney = GetBoolField(_needHoneyField, recipe);
                info.NeedLava = GetBoolField(_needLavaField, recipe);
                info.NeedSnowBiome = GetBoolField(_needSnowBiomeField, recipe);
                info.NeedGraveyard = GetBoolField(_needGraveyardField, recipe);
                info.NeedShimmer = GetBoolField(_needShimmerField, recipe);

                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extract ingredients from requiredItemQuickLookup, which contains recipe group info.
        /// </summary>
        private bool ExtractIngredientsFromQuickLookup(object recipe, RecipeInfo info)
        {
            try
            {
                var quickLookup = _quickLookupField?.GetValue(recipe) as Array;
                if (quickLookup == null) return false;

                for (int i = 0; i < quickLookup.Length; i++)
                {
                    var entry = quickLookup.GetValue(i);
                    if (entry == null) break;

                    var idVal = _entryIdField.GetValue(entry);
                    var stackVal = _entryStackField.GetValue(entry);
                    if (idVal == null || stackVal == null) break;

                    int idOrGroup = (int)idVal;
                    int stack = (int)stackVal;
                    if (idOrGroup <= 0) break; // Empty slot marks end of ingredients

                    bool isGroup = idOrGroup >= _fakeItemIdOffset;
                    var ingredient = new IngredientInfo
                    {
                        ItemId = idOrGroup, // Fake group ID or real item ID
                        RequiredStack = stack,
                        IsRecipeGroup = isGroup
                    };

                    if (isGroup)
                    {
                        // Resolve group info
                        int groupId = idOrGroup - _fakeItemIdOffset;
                        ingredient.ValidItemIds = GetGroupValidItems(groupId);
                        string groupName = GetGroupDisplayName(groupId);
                        // Ensure "Any " prefix for recipe group display
                        if (!groupName.StartsWith("Any ", StringComparison.OrdinalIgnoreCase)
                            && !groupName.StartsWith("Group #"))
                            groupName = "Any " + groupName;
                        ingredient.Name = groupName;
                    }
                    else
                    {
                        // Normal item — get name from requiredItem array (same index)
                        ingredient.Name = GetItemNameFromRequiredItem(recipe, i);
                    }

                    info.Ingredients.Add(ingredient);
                }

                return info.Ingredients.Count > 0;
            }
            catch (Exception ex)
            {
                _log.Warn($"RecipeIndex: QuickLookup extraction failed: {ex.Message}");
                info.Ingredients.Clear();
                return false;
            }
        }

        /// <summary>
        /// Get the ValidItems HashSet for a recipe group by its registered ID.
        /// </summary>
        private HashSet<int> GetGroupValidItems(int groupId)
        {
            try
            {
                var groupsDict = _recipeGroupsField?.GetValue(null);
                if (groupsDict == null) return null;

                // Dictionary<int, RecipeGroup>.TryGetValue via reflection
                var tryGetMethod = groupsDict.GetType().GetMethod("TryGetValue");
                if (tryGetMethod == null) return null;

                var args = new object[] { groupId, null };
                bool found = (bool)tryGetMethod.Invoke(groupsDict, args);
                if (!found || args[1] == null) return null;

                var validItems = _validItemsField?.GetValue(args[1]);
                if (validItems is HashSet<int> hashSet)
                    return hashSet;

                // If the type doesn't match directly, copy manually
                if (validItems is System.Collections.IEnumerable enumerable)
                {
                    var result = new HashSet<int>();
                    foreach (var item in enumerable)
                    {
                        if (item is int id)
                            result.Add(id);
                    }
                    return result;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get display name for a recipe group (e.g., "Any Iron Bar").
        /// </summary>
        private string GetGroupDisplayName(int groupId)
        {
            try
            {
                var groupsDict = _recipeGroupsField?.GetValue(null);
                if (groupsDict == null) return $"Group #{groupId}";

                var tryGetMethod = groupsDict.GetType().GetMethod("TryGetValue");
                if (tryGetMethod == null) return $"Group #{groupId}";

                var args = new object[] { groupId, null };
                bool found = (bool)tryGetMethod.Invoke(groupsDict, args);
                if (!found || args[1] == null) return $"Group #{groupId}";

                // GetText is a Func<string> field
                var getTextField = args[1].GetType().GetField("GetText", BindingFlags.Public | BindingFlags.Instance);
                if (getTextField != null)
                {
                    var getText = getTextField.GetValue(args[1]) as Func<string>;
                    if (getText != null)
                    {
                        string name = getText();
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }

                return $"Group #{groupId}";
            }
            catch
            {
                return $"Group #{groupId}";
            }
        }

        /// <summary>
        /// Get item name from the requiredItem array at a given index (for non-group ingredients).
        /// </summary>
        private string GetItemNameFromRequiredItem(object recipe, int index)
        {
            try
            {
                var requiredItems = _requiredItemField?.GetValue(recipe) as Array;
                if (requiredItems == null || index >= requiredItems.Length) return "";

                var item = requiredItems.GetValue(index);
                if (item == null) return "";

                return _itemNameProp?.GetValue(item) as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        private bool GetBoolField(FieldInfo field, object obj)
        {
            if (field == null) return false;
            try
            {
                var val = field.GetValue(obj);
                if (val == null) return false;
                return (bool)val;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get all recipes.
        /// </summary>
        public IReadOnlyList<RecipeInfo> GetAllRecipes() => _allRecipes;

        /// <summary>
        /// Get recipe by internal index (used for sub-recipe lookups from GetRecipesByOutput).
        /// </summary>
        public RecipeInfo GetRecipe(int index)
        {
            if (index < 0 || index >= _allRecipes.Count) return null;
            return _allRecipes[index];
        }

        /// <summary>
        /// Get recipe by its OriginalIndex (Terraria recipe array index).
        /// Use this when looking up a recipe from CraftabilityResult.Recipe.OriginalIndex.
        /// </summary>
        public RecipeInfo GetRecipeByOriginalIndex(int originalIndex)
        {
            if (_originalToInternal.TryGetValue(originalIndex, out int internalIndex))
                return GetRecipe(internalIndex);
            return null;
        }

        /// <summary>
        /// Get recipes that output a specific item.
        /// </summary>
        public IReadOnlyList<int> GetRecipesByOutput(int itemId)
        {
            if (_byOutput.TryGetValue(itemId, out var list))
                return list;
            return Array.Empty<int>();
        }

        /// <summary>
        /// Get recipes that use a specific ingredient.
        /// </summary>
        public IReadOnlyList<int> GetRecipesUsingIngredient(int itemId)
        {
            if (_byIngredient.TryGetValue(itemId, out var list))
                return list;
            return Array.Empty<int>();
        }

        /// <summary>
        /// Get recipes that require a specific station.
        /// </summary>
        public IReadOnlyList<int> GetRecipesByStation(int tileId)
        {
            if (_byStation.TryGetValue(tileId, out var list))
                return list;
            return Array.Empty<int>();
        }

        /// <summary>
        /// Check if any recipe can create the given item.
        /// </summary>
        public bool HasRecipeFor(int itemId) => _byOutput.ContainsKey(itemId);
    }

    /// <summary>
    /// Cached information about a recipe.
    /// </summary>
    public class RecipeInfo
    {
        public int OriginalIndex { get; set; }
        public int OutputItemId { get; set; }
        public int OutputStack { get; set; }
        public string OutputName { get; set; }

        public List<IngredientInfo> Ingredients { get; } = new List<IngredientInfo>();
        public List<int> RequiredTiles { get; } = new List<int>();

        // Environmental requirements
        public bool NeedWater { get; set; }
        public bool NeedHoney { get; set; }
        public bool NeedLava { get; set; }
        public bool NeedSnowBiome { get; set; }
        public bool NeedGraveyard { get; set; }
        public bool NeedShimmer { get; set; }

        /// <summary>
        /// Check if this recipe has any environmental requirements.
        /// </summary>
        public bool HasEnvironmentalRequirements =>
            NeedWater || NeedHoney || NeedLava || NeedSnowBiome || NeedGraveyard || NeedShimmer;
    }

    /// <summary>
    /// Cached information about a recipe ingredient.
    /// ItemId is the real item ID for normal ingredients, or the fake group ID for recipe groups.
    /// </summary>
    public class IngredientInfo
    {
        public int ItemId { get; set; }
        public int RequiredStack { get; set; }
        public string Name { get; set; }

        /// <summary>True if this ingredient accepts any item from a recipe group (e.g., "any iron bar").</summary>
        public bool IsRecipeGroup { get; set; }

        /// <summary>For recipe groups: the set of real item IDs that satisfy this ingredient.</summary>
        public HashSet<int> ValidItemIds { get; set; }
    }
}
