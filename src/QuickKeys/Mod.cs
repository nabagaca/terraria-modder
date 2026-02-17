using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Logging;

namespace QuickKeys
{
    public class Mod : IMod
    {
        public string Id => "quick-keys";
        public string Name => "Quick Keys";
        public string Version => "1.0.0";

        private ILogger _log;
        private ModContext _context;
        private bool _enabled;
        private bool _showMessages;
        private bool _enableExtendedHotbar;

        // Reflection caches
        private static Type _mainType;
        private static Type _playerType;
        private static Type _itemType;
        private static Type _tileIdSetsType;
        private static Type _chestUIType;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerArrayField;
        private static FieldInfo _tileArrayField;
        private static MethodInfo _quickStackAllChestsMethod;
        private static MethodInfo _chestUIQuickStackMethod;
        private static MethodInfo _itemCheckMethod;

        // Player fields
        private static FieldInfo _playerInventoryField;
        private static PropertyInfo _playerSelectedItemProp;
        private static FieldInfo _playerSelectedItemStateField;
        private static MethodInfo _selectedItemStateSelectMethod;
        private static FieldInfo _playerChestField;
        private static FieldInfo _playerPositionField;
        private static FieldInfo _playerWidthField;
        private static FieldInfo _playerHeightField;
        private static FieldInfo _playerTileRangeXField;
        private static FieldInfo _playerTileRangeYField;
        private static FieldInfo _playerBlockRangeField;
        private static FieldInfo _playerTileTargetXField;
        private static FieldInfo _playerTileTargetYField;
        private static FieldInfo _playerControlUseItemField;
        private static FieldInfo _playerItemAnimationField;
        private static FieldInfo _playerReuseDelayField;
        private static PropertyInfo _playerItemTimeIsZeroProp;

        // Item fields
        private static FieldInfo _itemTypeField;
        private static FieldInfo _itemStackField;
        private static FieldInfo _itemCreateTileField;
        private static FieldInfo _itemPlaceStyleField;
        private static MethodInfo _itemTurnToAirMethod;
        private static PropertyInfo _itemNameProp;

        // Tile fields
        private static MethodInfo _tileActiveMethod;
        private static FieldInfo _tileTypeField;

        // TileID.Sets.Torches
        private static FieldInfo _torchesSetField;

        // Ruler
        private static FieldInfo _playerRulerLineField;
        private static FieldInfo _playerBuilderAccStatusField;
        private static bool _rulerActive = false;

        // Smart cursor fields (for temporarily disabling during auto-torch)
        private static FieldInfo _smartCursorWantedMouseField;
        private static FieldInfo _smartCursorWantedGamePadField;

        // Item restoration state
        private static bool _autoRevertSelectedItem = false;
        private static int _originalSelectedItem = -1;
        private static int _swappedSlot = -1;
        private static bool _placingTorch = false;
        private static bool _usedSelectMethod = false; // true = used SelectItem, false = used swap

        // Terraria item IDs for recall items (priority order)
        private static readonly int[] RecallItemIds = new int[] {
            3124,  // Cell Phone
            5437,  // Shellphone (base)
            5358,  // Shellphone (spawn)
            5359,  // Shellphone (ocean)
            5360,  // Shellphone (underworld)
            5361,  // Shellphone (home)
            50,    // Magic Mirror
            3199,  // Ice Mirror
            2350   // Recall Potion
        };

        // Torch tile ID
        // Torch tile type is now read from item.createTile dynamically

        // Extended hotbar: slots 11-20 (indices 10-19), registered as keybinds

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _context = context;

            LoadConfig();
            InitReflection();

            // Always register keybinds so they appear in F6 menu, even if mod is disabled
            // The callback functions check _enabled before doing anything
            context.RegisterKeybind("auto-torch", "Auto Torch", "Place a torch near cursor", "OemTilde", OnAutoTorch);
            context.RegisterKeybind("auto-recall", "Auto Recall", "Use recall item (mirror/phone)", "Home", OnAutoRecall);
            context.RegisterKeybind("quick-stack", "Quick Stack", "Quick stack to nearby chests", "End", OnQuickStack);
            context.RegisterKeybind("ruler", "Ruler", "Toggle ruler distance overlay", "K", OnRulerToggle);

            // Extended hotbar keybinds (slots 11-20) - always register so they appear in F6,
            // but callbacks check _enableExtendedHotbar before acting
            context.RegisterKeybind("hotbar-11", "Hotbar Slot 11", "Quick-use item in slot 11", "NumPad1", () => OnHotbarSlot(10));
            context.RegisterKeybind("hotbar-12", "Hotbar Slot 12", "Quick-use item in slot 12", "NumPad2", () => OnHotbarSlot(11));
            context.RegisterKeybind("hotbar-13", "Hotbar Slot 13", "Quick-use item in slot 13", "NumPad3", () => OnHotbarSlot(12));
            context.RegisterKeybind("hotbar-14", "Hotbar Slot 14", "Quick-use item in slot 14", "NumPad4", () => OnHotbarSlot(13));
            context.RegisterKeybind("hotbar-15", "Hotbar Slot 15", "Quick-use item in slot 15", "NumPad5", () => OnHotbarSlot(14));
            context.RegisterKeybind("hotbar-16", "Hotbar Slot 16", "Quick-use item in slot 16", "NumPad6", () => OnHotbarSlot(15));
            context.RegisterKeybind("hotbar-17", "Hotbar Slot 17", "Quick-use item in slot 17", "NumPad7", () => OnHotbarSlot(16));
            context.RegisterKeybind("hotbar-18", "Hotbar Slot 18", "Quick-use item in slot 18", "NumPad8", () => OnHotbarSlot(17));
            context.RegisterKeybind("hotbar-19", "Hotbar Slot 19", "Quick-use item in slot 19", "NumPad9", () => OnHotbarSlot(18));
            context.RegisterKeybind("hotbar-20", "Hotbar Slot 20", "Quick-use item in slot 20", "NumPad0", () => OnHotbarSlot(19));

            if (!_enabled)
            {
                _log.Info("QuickKeys is disabled in config (keybinds registered but inactive)");
                return;
            }

            // Subscribe to post-update for item restoration
            FrameEvents.OnPostUpdate += OnPostUpdate;

            _log.Info("QuickKeys initialized - keybinds registered with F6 menu");
        }


        private void LoadConfig()
        {
            _enabled = _context.Config.Get<bool>("enabled");
            _showMessages = _context.Config.Get<bool>("showMessages");
            _enableExtendedHotbar = _context.Config.Get<bool>("enableExtendedHotbar");

            // Enable debug logging if configured
            bool debugLogging = _context.Config.Get("debugLogging", false);
            if (debugLogging)
            {
                _log.MinLevel = TerrariaModder.Core.Logging.LogLevel.Debug;
            }
        }

        public void OnConfigChanged()
        {
            LoadConfig();
        }

        private void InitReflection()
        {
            try { var asm = Assembly.Load("Terraria");
                _mainType = asm.GetType("Terraria.Main");
                _playerType = asm.GetType("Terraria.Player");
                _itemType = asm.GetType("Terraria.Item");
                _tileIdSetsType = asm.GetType("Terraria.ID.TileID+Sets");
                _chestUIType = asm.GetType("Terraria.UI.ChestUI");
            } catch (Exception ex) { _log.Error($"Load types: {ex.Message}"); }

            try { if (_mainType != null) {
                _myPlayerField = _mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                _playerArrayField = _mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                _tileArrayField = _mainType.GetField("tile", BindingFlags.Public | BindingFlags.Static);
            }} catch (Exception ex) { _log.Error($"Main fields: {ex.Message}"); }

            try { if (_playerType != null) {
                // Use GetMethods to find the right overload
                foreach (var m in _playerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == "QuickStackAllChests" && m.GetParameters().Length == 0)
                        _quickStackAllChestsMethod = m;
                    // ItemCheck() with no parameters
                    if (m.Name == "ItemCheck" && m.GetParameters().Length == 0)
                        _itemCheckMethod = m;
                }

                _playerInventoryField = _playerType.GetField("inventory", BindingFlags.Public | BindingFlags.Instance);
                _playerSelectedItemProp = _playerType.GetProperty("selectedItem", BindingFlags.Public | BindingFlags.Instance);

                // Get selectedItemState field and its Select method
                _playerSelectedItemStateField = _playerType.GetField("selectedItemState", BindingFlags.Public | BindingFlags.Instance);
                if (_playerSelectedItemStateField != null)
                {
                    var stateType = _playerSelectedItemStateField.FieldType;
                    _selectedItemStateSelectMethod = stateType.GetMethod("Select", BindingFlags.Public | BindingFlags.Instance);
                }
                _playerChestField = _playerType.GetField("chest", BindingFlags.Public | BindingFlags.Instance);
                _playerPositionField = _playerType.GetField("position", BindingFlags.Public | BindingFlags.Instance);
                _playerWidthField = _playerType.GetField("width", BindingFlags.Public | BindingFlags.Instance);
                _playerHeightField = _playerType.GetField("height", BindingFlags.Public | BindingFlags.Instance);
                _playerTileRangeXField = _playerType.GetField("tileRangeX", BindingFlags.Public | BindingFlags.Instance);
                _playerTileRangeYField = _playerType.GetField("tileRangeY", BindingFlags.Public | BindingFlags.Instance);
                _playerBlockRangeField = _playerType.GetField("blockRange", BindingFlags.Public | BindingFlags.Instance);
                _playerTileTargetXField = _playerType.GetField("tileTargetX", BindingFlags.Public | BindingFlags.Instance);
                _playerTileTargetYField = _playerType.GetField("tileTargetY", BindingFlags.Public | BindingFlags.Instance);
                _playerControlUseItemField = _playerType.GetField("controlUseItem", BindingFlags.Public | BindingFlags.Instance);
                _playerItemAnimationField = _playerType.GetField("itemAnimation", BindingFlags.Public | BindingFlags.Instance);
                _playerReuseDelayField = _playerType.GetField("reuseDelay", BindingFlags.Public | BindingFlags.Instance);
                _playerItemTimeIsZeroProp = _playerType.GetProperty("ItemTimeIsZero", BindingFlags.Public | BindingFlags.Instance);
                _playerRulerLineField = _playerType.GetField("rulerLine", BindingFlags.Public | BindingFlags.Instance);
                _playerBuilderAccStatusField = _playerType.GetField("builderAccStatus", BindingFlags.Public | BindingFlags.Instance);

            }} catch (Exception ex) { _log.Error($"Player fields: {ex.Message}"); }

            try { if (_itemType != null) {
                _itemTypeField = _itemType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                _itemStackField = _itemType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);
                _itemCreateTileField = _itemType.GetField("createTile", BindingFlags.Public | BindingFlags.Instance);
                _itemPlaceStyleField = _itemType.GetField("placeStyle", BindingFlags.Public | BindingFlags.Instance);
                _itemTurnToAirMethod = _itemType.GetMethod("TurnToAir", BindingFlags.Public | BindingFlags.Instance);
                _itemNameProp = _itemType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            }} catch (Exception ex) { _log.Error($"Item fields: {ex.Message}"); }

            try {
                var asm = Assembly.Load("Terraria");
                var tileType = asm.GetType("Terraria.Tile");
                if (tileType != null) {
                    var methods = tileType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

                    // Try active() first, then HasTile
                    foreach (var m in methods)
                    {
                        if (m.Name == "active" && m.GetParameters().Length == 0)
                        {
                            _tileActiveMethod = m;
                            break;
                        }
                    }
                    // Fallback to HasTile property getter
                    if (_tileActiveMethod == null)
                    {
                        var hasTileProp = tileType.GetProperty("HasTile", BindingFlags.Public | BindingFlags.Instance);
                        if (hasTileProp != null)
                            _tileActiveMethod = hasTileProp.GetGetMethod();
                    }
                    _tileTypeField = tileType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                }
            } catch (Exception ex) { _log.Error($"Tile fields: {ex.Message}"); }

            try { if (_tileIdSetsType != null) {
                // Try both Torch (1.4+) and Torches (older)
                _torchesSetField = _tileIdSetsType.GetField("Torch", BindingFlags.Public | BindingFlags.Static);
                if (_torchesSetField == null)
                    _torchesSetField = _tileIdSetsType.GetField("Torches", BindingFlags.Public | BindingFlags.Static);
            }} catch (Exception ex) { _log.Error($"TileID.Sets: {ex.Message}"); }

            // Smart cursor fields (for temporarily disabling during auto-torch)
            try {
                _smartCursorWantedMouseField = _mainType.GetField("SmartCursorWanted_Mouse", BindingFlags.Public | BindingFlags.Static);
                _smartCursorWantedGamePadField = _mainType.GetField("SmartCursorWanted_GamePad", BindingFlags.Public | BindingFlags.Static);
            } catch (Exception ex) { _log.Error($"SmartCursor fields: {ex.Message}"); }

            try { if (_chestUIType != null) {
                // Find QuickStack with no parameters
                foreach (var m in _chestUIType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "QuickStack" && m.GetParameters().Length == 0)
                    {
                        _chestUIQuickStackMethod = m;
                        break;
                    }
                }
            }} catch (Exception ex) { _log.Error($"ChestUI: {ex.Message}"); }
        }

        private void OnHotbarSlot(int slotIndex)
        {
            if (!_enabled || !_enableExtendedHotbar) return;

            var player = GetLocalPlayer();
            if (player == null) return;

            var inventory = GetInventory(player);
            if (inventory == null) return;

            if (slotIndex >= 0 && slotIndex < inventory.Length)
            {
                var item = inventory.GetValue(slotIndex);
                int itemType = GetItemType(item);
                int itemStack = GetItemStack(item);
                if (itemType != 0 && itemStack > 0)
                    QuickUseItemAt(player, inventory, slotIndex);
            }
        }

        private object GetLocalPlayer()
        {
            try
            {
                if (_myPlayerField == null || _playerArrayField == null) return null;
                int myPlayer = (int)_myPlayerField.GetValue(null);
                var players = (Array)_playerArrayField.GetValue(null);
                return players?.GetValue(myPlayer);
            }
            catch { return null; } // Safe default when reflection fails
        }

        private Array GetInventory(object player)
        {
            if (player == null || _playerInventoryField == null) return null;
            return _playerInventoryField.GetValue(player) as Array;
        }

        private int GetItemType(object item)
        {
            if (item == null || _itemTypeField == null) return 0;
            try { return (int)_itemTypeField.GetValue(item); }
            catch { return 0; } // Treat as empty slot on reflection failure
        }

        private int GetItemStack(object item)
        {
            if (item == null || _itemStackField == null) return 0;
            try { return (int)_itemStackField.GetValue(item); }
            catch { return 0; } // Treat as empty slot on reflection failure
        }

        private int GetItemCreateTile(object item)
        {
            if (item == null || _itemCreateTileField == null) return -1;
            try { return (int)_itemCreateTileField.GetValue(item); }
            catch { return -1; } // No tile creation on reflection failure
        }

        private string GetItemName(object item)
        {
            if (item == null || _itemNameProp == null) return "";
            try { return _itemNameProp.GetValue(item)?.ToString() ?? ""; }
            catch { return ""; } // Empty name on reflection failure
        }

        private int GetItemPlaceStyle(object item)
        {
            if (item == null || _itemPlaceStyleField == null) return 0;
            try { return (int)_itemPlaceStyleField.GetValue(item); }
            catch { return 0; } // Default style on reflection failure
        }

        /// <summary>
        /// Consume one item from stack. Returns true if consumed, false if stack was already 0.
        /// Properly handles TurnToAir when stack reaches 0.
        /// </summary>
        private bool ConsumeOneFromStack(object item)
        {
            if (item == null || _itemStackField == null) return false;
            try
            {
                int stack = (int)_itemStackField.GetValue(item);
                if (stack <= 0) return false;

                stack--;
                _itemStackField.SetValue(item, stack);

                // When stack reaches 0, turn item to air (removes it from inventory)
                if (stack <= 0 && _itemTurnToAirMethod != null)
                {
                    _itemTurnToAirMethod.Invoke(item, null);
                }
                return true;
            }
            catch { return false; }
        }

        private void ShowMessage(string message, byte r = 255, byte g = 255, byte b = 0)
        {
            if (!_showMessages) return;
            TerrariaModder.Core.Reflection.Game.ShowMessage($"[QuickKeys] {message}", r, g, b);
        }

        #region Ruler

        private void OnRulerToggle()
        {
            if (!_enabled) return;
            _rulerActive = !_rulerActive;

            // When toggling off, actively disable the ruler
            if (!_rulerActive && _playerBuilderAccStatusField != null)
            {
                try
                {
                    var player = GetLocalPlayer();
                    if (player != null)
                    {
                        var accStatus = _playerBuilderAccStatusField.GetValue(player) as int[];
                        if (accStatus != null && accStatus.Length > 0)
                            accStatus[0] = 1; // 1 = disabled
                    }
                }
                catch { }
            }

            _log.Info($"Ruler toggled: {_rulerActive}");
            ShowMessage(_rulerActive ? "Ruler ON" : "Ruler OFF", 173, 216, 230);
        }

        #endregion

        #region Auto Torch

        // Cache for HasTile property
        private static PropertyInfo _hasTileProp;

        private void OnAutoTorch()
        {
            if (!_enabled) return;
            if (_placingTorch) return;
            _placingTorch = true;

            // Note: We use direct WorldGen.PlaceTile via Game.PlaceTile, which bypasses
            // smart cursor entirely. No need to disable smart cursor here.

            try
            {
                var player = GetLocalPlayer();
                if (player == null) return;

                var inventory = GetInventory(player);
                if (inventory == null) return;

                // Get torch set
                bool[] torchSet = _torchesSetField?.GetValue(null) as bool[];
                if (torchSet == null) return;

                // Find a torch in inventory (must have stack > 0)
                int torchSlot = -1;
                object torchItem = null;
                int torchTileType = -1;
                int torchPlaceStyle = 0;

                for (int i = 0; i < inventory.Length; i++)
                {
                    var item = inventory.GetValue(i);
                    int stack = GetItemStack(item);
                    if (stack <= 0) continue; // Skip empty or depleted slots

                    int createTile = GetItemCreateTile(item);
                    if (createTile >= 0 && createTile < torchSet.Length && torchSet[createTile])
                    {
                        torchSlot = i;
                        torchItem = item;
                        torchTileType = createTile;
                        torchPlaceStyle = GetItemPlaceStyle(item);
                        break;
                    }
                }

                if (torchSlot == -1 || torchItem == null)
                {
                    ShowMessage("No torches in inventory!", 255, 255, 0);
                    return;
                }

                // Select the torch item directly (like HelpfulHotkeys does)
                int originalSelected = (int)_playerSelectedItemProp.GetValue(player);
                bool needRestore = (originalSelected != torchSlot);

                if (needRestore)
                {
                    // Try to set selectedItem directly (like HelpfulHotkeys)
                    bool selected = SelectItem(player, torchSlot);
                    if (selected)
                        _usedSelectMethod = true;
                    else
                    {
                        SwapInventorySlots(inventory, originalSelected, torchSlot);
                        _usedSelectMethod = false;
                    }
                    _autoRevertSelectedItem = true;
                    _originalSelectedItem = originalSelected;
                    _swappedSlot = torchSlot;
                }

                // Get player position
                float posX = 0, posY = 0;
                GetPlayerPosition(player, out posX, out posY);
                int width = 20, height = 42; // defaults
                try { if (_playerWidthField != null) width = (int)_playerWidthField.GetValue(player); } catch { } // Use default on failure
                try { if (_playerHeightField != null) height = (int)_playerHeightField.GetValue(player); } catch { } // Use default on failure

                // Set initial tile target to player center
                int centerX = (int)((posX + width / 2f) / 16f);
                int centerY = (int)((posY + height / 2f) / 16f);
                _playerTileTargetXField?.SetValue(player, centerX);
                _playerTileTargetYField?.SetValue(player, centerY);

                // Get tile ranges
                int tileRangeX = 5, tileRangeY = 4, blockRange = 0;
                try { if (_playerTileRangeXField != null) tileRangeX = (int)_playerTileRangeXField.GetValue(player); } catch { } // Use default
                try { if (_playerTileRangeYField != null) tileRangeY = (int)_playerTileRangeYField.GetValue(player); } catch { } // Use default
                try { if (_playerBlockRangeField != null) blockRange = (int)_playerBlockRangeField.GetValue(player); } catch { } // Use default
                tileRangeX = Math.Min(tileRangeX, 50);
                tileRangeY = Math.Min(tileRangeY, 50);

                // Build list of positions sorted by distance from mouse
                float mouseX = 0, mouseY = 0;
                GetMouseWorld(out mouseX, out mouseY);
                // If mouse is 0,0, use player center
                if (mouseX == 0 && mouseY == 0)
                {
                    mouseX = posX + width / 2f;
                    mouseY = posY + height / 2f;
                }

                var targets = new List<Tuple<float, int, int>>();
                int minX = -tileRangeX - blockRange + (int)(posX / 16f) + 1;
                int maxX = tileRangeX + blockRange - 1 + (int)((posX + width) / 16f);
                int minY = -tileRangeY - blockRange + (int)(posY / 16f) + 1;
                int maxY = tileRangeY + blockRange - 2 + (int)((posY + height) / 16f);

                for (int j = minX; j <= maxX; j++)
                {
                    for (int k = minY; k <= maxY; k++)
                    {
                        float dist = (float)Math.Sqrt((mouseX - j * 16f) * (mouseX - j * 16f) + (mouseY - k * 16f) * (mouseY - k * 16f));
                        targets.Add(new Tuple<float, int, int>(dist, j, k));
                    }
                }
                targets.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                // Try each position using direct tile placement
                var tileArray = _tileArrayField?.GetValue(null);
                bool placeSuccess = false;

                foreach (var target in targets)
                {
                    int tileX = target.Item2;
                    int tileY = target.Item3;

                    // Get tile state before
                    var tile = GetTile(tileArray, tileX, tileY);
                    bool hadTileBefore = GetTileHasTile(tile);

                    _playerTileTargetXField?.SetValue(player, tileX);
                    _playerTileTargetYField?.SetValue(player, tileY);

                    // Use Core utility for tile placement with correct tile type and style
                    if (!hadTileBefore)
                    {
                        try
                        {
                            // Use the actual torch type and style from the inventory item
                            bool placed = TerrariaModder.Core.Reflection.Game.PlaceTile(tileX, tileY, torchTileType, torchPlaceStyle);
                            if (placed)
                            {
                                // Consume one torch from inventory (torchItem reference is still valid)
                                ConsumeOneFromStack(torchItem);
                                placeSuccess = true;
                                break;
                            }
                        }
                        catch
                        {
                            // Continue trying other positions
                        }
                    }
                }

                if (!placeSuccess)
                    ShowMessage("No valid spot for torch", 255, 255, 0);
            }
            catch (Exception ex)
            {
                _log.Error($"Auto-torch error: {ex.Message}");
                _log.Error($"Stack: {ex.StackTrace}");
            }
            finally
            {
                _placingTorch = false;
            }
        }

        private object GetTile(object tileArray, int x, int y)
        {
            if (tileArray == null) return null;
            try
            {
                // tileArray is Tile[,] - a 2D array
                if (tileArray is Array arr)
                    return arr.GetValue(x, y);
            }
            catch
            {
                // Return null on failure
            }
            return null;
        }

        private bool IsTileActive(object tile)
        {
            if (tile == null || _tileActiveMethod == null) return true;
            try
            {
                return (bool)_tileActiveMethod.Invoke(tile, null);
            }
            catch
            {
                return true;
            }
        }

        #endregion

        #region Auto Recall

        private void OnAutoRecall()
        {
            if (!_enabled) return;
            try
            {
                var player = GetLocalPlayer();
                if (player == null) return;

                var inventory = GetInventory(player);
                if (inventory == null) return;

                int foundSlot = -1;
                string itemName = "";
                foreach (int itemId in RecallItemIds)
                {
                    for (int i = 0; i < Math.Min(58, inventory.Length); i++)
                    {
                        var item = inventory.GetValue(i);
                        if (GetItemType(item) == itemId && GetItemStack(item) > 0)
                        {
                            foundSlot = i;
                            itemName = GetItemName(item);
                            break;
                        }
                    }
                    if (foundSlot >= 0) break;
                }

                if (foundSlot == -1)
                {
                    ShowMessage("No recall item found!", 255, 255, 0);
                    return;
                }

                QuickUseItemAt(player, inventory, foundSlot);
                ShowMessage($"Using {itemName}", 173, 216, 230);
            }
            catch (Exception ex)
            {
                _log.Error($"Auto-recall error: {ex.Message}");
            }
        }

        #endregion

        #region Quick Stack

        private void OnQuickStack()
        {
            if (!_enabled) return;
            var player = GetLocalPlayer();
            if (player == null) return;

            try
            {
                int chest = (int)_playerChestField.GetValue(player);
                if (chest >= 0 && _chestUIQuickStackMethod != null)
                {
                    _chestUIQuickStackMethod.Invoke(null, null);
                    ShowMessage("Quick stacked to chest", 144, 238, 144);
                }
                else if (_quickStackAllChestsMethod != null)
                {
                    _quickStackAllChestsMethod.Invoke(player, null);
                    ShowMessage("Quick stacked to nearby chests", 144, 238, 144);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Quick stack error: {ex.Message}");
            }
        }

        #endregion

        #region Item Quick Use & Restoration

        private void QuickUseItemAt(object player, Array inventory, int slot, bool use = true)
        {
            if (_autoRevertSelectedItem || player == null || inventory == null) return;

            int selectedItem = (int)_playerSelectedItemProp.GetValue(player);
            if (selectedItem == slot) return;
            if (GetItemType(inventory.GetValue(slot)) == 0) return;

            _originalSelectedItem = selectedItem;
            _swappedSlot = slot;
            _autoRevertSelectedItem = true;

            // Check if player can switch
            int itemAnimation = (int)_playerItemAnimationField.GetValue(player);
            bool itemTimeIsZero = _playerItemTimeIsZeroProp != null ? (bool)_playerItemTimeIsZeroProp.GetValue(player) : true;
            int reuseDelay = (int)_playerReuseDelayField.GetValue(player);

            if (itemAnimation == 0 && itemTimeIsZero && reuseDelay == 0)
            {
                // Try SelectItem first, fall back to swap
                bool selected = SelectItem(player, slot);
                if (selected)
                    _usedSelectMethod = true;
                else
                {
                    SwapInventorySlots(inventory, selectedItem, slot);
                    _usedSelectMethod = false;
                }
                _playerControlUseItemField.SetValue(player, true);
                if (use)
                    InvokeItemCheck(player);
            }
        }

        private bool GetTileHasTile(object tile)
        {
            if (tile == null) return false;
            try
            {
                // Try HasTile property first (1.4+)
                if (_hasTileProp == null)
                {
                    _hasTileProp = tile.GetType().GetProperty("HasTile", BindingFlags.Public | BindingFlags.Instance);
                }
                if (_hasTileProp != null)
                    return (bool)_hasTileProp.GetValue(tile);

                // Fallback to active() method
                return IsTileActive(tile);
            }
            catch { return false; } // Treat as no tile on reflection failure
        }

        private int GetTileType(object tile)
        {
            if (tile == null || _tileTypeField == null) return -1;
            try
            {
                return (int)(ushort)_tileTypeField.GetValue(tile);
            }
            catch { return -1; } // Invalid tile type on reflection failure
        }

        private void SwapInventorySlots(Array inventory, int slotA, int slotB)
        {
            if (inventory == null) return;
            var temp = inventory.GetValue(slotA);
            inventory.SetValue(inventory.GetValue(slotB), slotA);
            inventory.SetValue(temp, slotB);
        }

        private bool SelectItem(object player, int index)
        {
            // Try to set selectedItem property directly (like HelpfulHotkeys does with Player.selectedItem = index)
            if (_playerSelectedItemProp != null && _playerSelectedItemProp.CanWrite)
            {
                try
                {
                    _playerSelectedItemProp.SetValue(player, index);
                    int verify = (int)_playerSelectedItemProp.GetValue(player);
                    if (verify == index)
                        return true;
                }
                catch
                {
                    // Fall through to next method
                }
            }

            // Fall back to selectedItemState.Select()
            if (_playerSelectedItemStateField != null && _selectedItemStateSelectMethod != null)
            {
                try
                {
                    var state = _playerSelectedItemStateField.GetValue(player);
                    if (state != null)
                    {
                        _selectedItemStateSelectMethod.Invoke(state, new object[] { index });
                        int verify = (int)_playerSelectedItemProp.GetValue(player);
                        return verify == index;
                    }
                }
                catch
                {
                    // Selection failed
                }
            }
            return false;
        }

        private void InvokeItemCheck(object player)
        {
            if (_itemCheckMethod == null || player == null) return;
            try
            {
                _itemCheckMethod.Invoke(player, null);
            }
            catch (Exception ex)
            {
                _log.Error($"InvokeItemCheck error: {ex.Message}");
            }
        }

        private void OnPostUpdate()
        {
            // Apply ruler flags every frame (vanilla ResetEffects clears rulerLine)
            if (_rulerActive)
            {
                try
                {
                    var player = GetLocalPlayer();
                    if (player != null)
                    {
                        if (_playerRulerLineField != null)
                            _playerRulerLineField.SetValue(player, true);
                        if (_playerBuilderAccStatusField != null)
                        {
                            var accStatus = _playerBuilderAccStatusField.GetValue(player) as int[];
                            if (accStatus != null && accStatus.Length > 0)
                                accStatus[0] = 0;
                        }
                    }
                }
                catch { }
            }

            if (!_autoRevertSelectedItem) return;

            try
            {
                var player = GetLocalPlayer();
                if (player == null) return;

                int itemAnimation = (int)_playerItemAnimationField.GetValue(player);
                bool itemTimeIsZero = _playerItemTimeIsZeroProp != null ? (bool)_playerItemTimeIsZeroProp.GetValue(player) : true;
                int reuseDelay = (int)_playerReuseDelayField.GetValue(player);

                if (itemAnimation == 0 && itemTimeIsZero && reuseDelay == 0)
                {
                    if (_originalSelectedItem >= 0)
                    {
                        if (_usedSelectMethod)
                        {
                            // Restore using SelectItem since that's what we used to change
                            SelectItem(player, _originalSelectedItem);
                        }
                        else if (_swappedSlot >= 0)
                        {
                            // Restore by swapping back
                            var inventory = GetInventory(player);
                            if (inventory != null)
                                SwapInventorySlots(inventory, _originalSelectedItem, _swappedSlot);
                        }
                    }
                    _autoRevertSelectedItem = false;
                    _originalSelectedItem = -1;
                    _swappedSlot = -1;
                    _usedSelectMethod = false;
                }
            }
            catch { } // Silently fail item restoration - not critical
        }

        #endregion

        #region Helpers

        private void GetPlayerPosition(object player, out float x, out float y)
        {
            x = 0; y = 0;
            if (player == null || _playerPositionField == null) return;
            try
            {
                var pos = _playerPositionField.GetValue(player);
                if (pos == null) return;
                var posType = pos.GetType();

                // Vector2 has X and Y as FIELDS, not properties
                var xField = posType.GetField("X");
                var yField = posType.GetField("Y");
                if (xField != null) x = (float)xField.GetValue(pos);
                if (yField != null) y = (float)yField.GetValue(pos);
            }
            catch { } // Use default 0,0 position on reflection failure
        }

        private static PropertyInfo _mouseWorldProp;
        private void GetMouseWorld(out float x, out float y)
        {
            x = 0; y = 0;
            try
            {
                // MouseWorld is a PROPERTY, not a field
                if (_mouseWorldProp == null)
                    _mouseWorldProp = _mainType?.GetProperty("MouseWorld", BindingFlags.Public | BindingFlags.Static);

                if (_mouseWorldProp != null)
                {
                    var mouseWorld = _mouseWorldProp.GetValue(null);
                    if (mouseWorld != null)
                    {
                        var type = mouseWorld.GetType();
                        // Vector2 has X and Y as public fields
                        var xField = type.GetField("X", BindingFlags.Public | BindingFlags.Instance);
                        var yField = type.GetField("Y", BindingFlags.Public | BindingFlags.Instance);

                        if (xField != null && yField != null)
                        {
                            x = (float)xField.GetValue(mouseWorld);
                            y = (float)yField.GetValue(mouseWorld);
                        }
                    }
                }
            }
            catch
            {
                // Use default 0,0 on failure
            }
        }

        #endregion

        public void OnWorldLoad()
        {
        }

        public void OnWorldUnload()
        {
            _rulerActive = false;
            _autoRevertSelectedItem = false;
            _originalSelectedItem = -1;
            _swappedSlot = -1;
            _usedSelectMethod = false;
        }

        public void Unload()
        {
            FrameEvents.OnPostUpdate -= OnPostUpdate;

            // Reset static state for hot-reload support
            _placingTorch = false;
            _rulerActive = false;

            _log.Info("QuickKeys unloaded");
        }
    }
}
