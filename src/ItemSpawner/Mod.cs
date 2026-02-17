using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.UI;
using TerrariaModder.Core.UI.Widgets;

namespace ItemSpawner
{
    public class Mod : IMod
    {
        public string Id => "item-spawner";
        public string Name => "Item Spawner";
        public string Version => "1.0.0";

        private ILogger _log;
        private ModContext _context;
        private bool _enabled;
        private bool _singleplayerOnly;

        private List<ItemEntry> _allItems = new List<ItemEntry>();
        private List<ItemEntry> _filteredItems = new List<ItemEntry>();

        // Reflection - shared
        private static Type _mainType;
        private static Type _itemType;
        private static Type _playerType;
        private static FieldInfo _netModeField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerArrayField;
        private static PropertyInfo _itemNameProp;
        private static FieldInfo _itemMaxStackField;

        // Reflection - cursor/inventory placement
        private static FieldInfo _mouseItemField;
        private static FieldInfo _itemTypeField;
        private static FieldInfo _itemStackField;
        private static MethodInfo _itemSetDefaultsIntMethod;
        private static FieldInfo _playerInventoryField;

        // UI - grid layout
        private const int GridSlotSize = 44;
        private const int SlotOuter = GridSlotSize - 2;
        private const int IconPad = 4;
        private const int IconSize = SlotOuter - IconPad * 2;

        private DraggablePanel _panel = new DraggablePanel("item-spawner", "Item Spawner", 520, 520);
        private TextInput _searchInput = new TextInput("Type to search...", 200);
        private ScrollView _scroll = new ScrollView();

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _context = context;

            LoadConfig();
            InitReflection();

            if (!_enabled)
            {
                _log.Info("ItemSpawner is disabled in config");
                return;
            }

            _searchInput.KeyBlockId = "item-spawner";
            _panel.OnClose = OnPanelClose;
            _panel.RegisterDrawCallback(OnDraw);

            context.RegisterKeybind("toggle", "Toggle Spawner", "Open/close spawner UI", "Insert", OnToggleUI);
            FrameEvents.OnPreUpdate += OnUpdate;

            _log.Info("ItemSpawner initialized - Press Insert to open");
        }

        private void LoadConfig()
        {
            _enabled = _context.Config.Get<bool>("enabled");
            _singleplayerOnly = _context.Config.Get<bool>("singleplayerOnly");
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
                    _netModeField = _mainType.GetField("netMode", BindingFlags.Public | BindingFlags.Static);
                    _myPlayerField = _mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                    _playerArrayField = _mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                    _mouseItemField = _mainType.GetField("mouseItem", BindingFlags.Public | BindingFlags.Static);
                }

                if (_itemType != null)
                {
                    _itemNameProp = _itemType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                    _itemMaxStackField = _itemType.GetField("maxStack", BindingFlags.Public | BindingFlags.Instance);
                    _itemTypeField = _itemType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                    _itemStackField = _itemType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);

                    _itemSetDefaultsIntMethod = _itemType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "SetDefaults" &&
                            m.GetParameters().Length >= 1 &&
                            m.GetParameters()[0].ParameterType == typeof(int));
                }

                if (_playerType != null)
                {
                    _playerInventoryField = _playerType.GetField("inventory", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Reflection init error: {ex.Message}");
            }
        }

        private void BuildItemCatalog()
        {
            try
            {
                var itemIdType = Type.GetType("Terraria.ID.ItemID, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.ID.ItemID");

                int itemCount = 5500;
                if (itemIdType != null)
                {
                    var countField = itemIdType.GetField("Count", BindingFlags.Public | BindingFlags.Static);
                    if (countField != null)
                    {
                        var countValue = countField.GetValue(null);
                        itemCount = Convert.ToInt32(countValue);
                    }
                }

                if (_itemType == null)
                {
                    _log.Error("_itemType is null - cannot build catalog");
                    return;
                }

                int errorCount = 0;
                if (_itemSetDefaultsIntMethod == null)
                {
                    _log.Error("Could not find suitable SetDefaults method");
                    return;
                }

                int paramCount = _itemSetDefaultsIntMethod.GetParameters().Length;

                for (int i = 1; i < itemCount; i++)
                {
                    try
                    {
                        var item = Activator.CreateInstance(_itemType);

                        object[] args = paramCount >= 2
                            ? new object[] { i, null }
                            : new object[] { i };

                        _itemSetDefaultsIntMethod.Invoke(item, args);

                        string name = _itemNameProp?.GetValue(item)?.ToString() ?? "";
                        int maxStack = 1;
                        if (_itemMaxStackField != null)
                            maxStack = (int)_itemMaxStackField.GetValue(item);

                        if (!string.IsNullOrEmpty(name) && name.Trim() != "")
                        {
                            _allItems.Add(new ItemEntry { Id = i, Name = name, MaxStack = maxStack });
                        }
                    }
                    catch
                    {
                        errorCount++;
                    }
                }

                _allItems = _allItems.OrderBy(i => i.Name).ToList();
                _filteredItems = new List<ItemEntry>(_allItems);
                _log.Info($"Item catalog built with {_allItems.Count} items");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to build item catalog: {ex.Message}");
            }
        }

        #region UI

        private void OnToggleUI()
        {
            if (!_panel.IsOpen)
            {
                if (_singleplayerOnly)
                {
                    int netMode = 0;
                    try { netMode = (int)_netModeField.GetValue(null); }
                    catch { }
                    if (netMode != 0)
                    {
                        _log.Warn("ItemSpawner is disabled in multiplayer");
                        return;
                    }
                }

                _searchInput.Clear();
                _scroll.ResetScroll();
                if (_allItems.Count == 0) BuildItemCatalog();
                FilterItems();
                _panel.Open();
                UIRenderer.OpenInventory();
            }
            else
            {
                _panel.Close();
            }
        }

        private void OnPanelClose()
        {
            _searchInput.Unfocus();
            UIRenderer.CloseInventory();
        }

        private void FilterItems()
        {
            string search = _searchInput.Text;
            if (string.IsNullOrEmpty(search))
            {
                _filteredItems = new List<ItemEntry>(_allItems);
            }
            else
            {
                _filteredItems.Clear();
                string lower = search.ToLower();
                foreach (var item in _allItems)
                {
                    if (item.Name.ToLower().Contains(lower))
                        _filteredItems.Add(item);
                }
            }
            _scroll.ResetScroll();
            _panel.Title = $"Item Spawner ({_filteredItems.Count}/{_allItems.Count})";
        }

        private void OnUpdate()
        {
            if (!_panel.IsOpen) return;
            _searchInput.Update();
        }

        private void OnDraw()
        {
            if (!_panel.BeginDraw()) return;
            try
            {
                var s = new StackLayout(_panel.ContentX, _panel.ContentY, _panel.ContentWidth);

                // Search bar
                int searchY = s.Advance(30);
                _searchInput.Draw(s.X, searchY, s.Width, 28);
                if (_searchInput.HasChanged) FilterItems();

                // Help text
                int helpY = s.Advance(34);
                UIRenderer.DrawRect(s.X, helpY, s.Width, 30, UIColors.SectionBg);
                UIRenderer.DrawTextSmall("L-Click: +1 to cursor    R-Click: Full stack to cursor", s.X + 4, helpY + 2, UIColors.Success);
                UIRenderer.DrawTextSmall("Shift+L: +1 to inventory  Shift+R: Full stack to inventory", s.X + 4, helpY + 16, UIColors.Info);

                // Item grid
                int gridX = s.X;
                int gridY = s.CurrentY;
                int gridWidth = s.Width;
                int gridHeight = _panel.ContentHeight - s.TotalHeight;

                int gridColumns = Math.Max(1, _scroll.ContentWidth / GridSlotSize);
                int totalRows = (_filteredItems.Count + gridColumns - 1) / gridColumns;
                int totalContentHeight = totalRows * GridSlotSize;

                _scroll.Begin(gridX, gridY, gridWidth, gridHeight, totalContentHeight);

                bool shiftHeld = WidgetInput.IsShiftHeld;
                int contentWidth = _scroll.ContentWidth;
                // Recalculate columns after Begin (ContentWidth may shrink for scrollbar)
                gridColumns = Math.Max(1, contentWidth / GridSlotSize);

                int visibleRows = gridHeight / GridSlotSize;
                int startRow = _scroll.ScrollOffset / GridSlotSize;
                int startIndex = startRow * gridColumns;

                for (int i = 0; i < (visibleRows + 1) * gridColumns && startIndex + i < _filteredItems.Count; i++)
                {
                    int itemIdx = startIndex + i;
                    int row = i / gridColumns;
                    int col = i % gridColumns;

                    int itemContentY = (startRow + row) * GridSlotSize;
                    if (!_scroll.IsVisible(itemContentY, GridSlotSize)) continue;

                    int slotX = gridX + col * GridSlotSize;
                    int slotY = _scroll.ContentY + itemContentY;

                    var item = _filteredItems[itemIdx];
                    bool isInScrollBounds = WidgetInput.IsMouseOver(gridX, gridY, gridWidth, gridHeight);
                    bool isHover = isInScrollBounds && WidgetInput.IsMouseOver(slotX, slotY, SlotOuter, SlotOuter);

                    // Slot background
                    UIRenderer.DrawRect(slotX, slotY, SlotOuter, SlotOuter,
                        isHover ? UIColors.InputFocusBg : UIColors.ItemBg);

                    // Hover border
                    if (isHover)
                        UIRenderer.DrawRectOutline(slotX, slotY, SlotOuter, SlotOuter, UIColors.Accent, 1);

                    // Item icon
                    UIRenderer.DrawItem(item.Id, slotX + IconPad, slotY + IconPad, IconSize, IconSize);

                    // Hover tooltip + click handling
                    if (isHover)
                    {
                        ItemTooltip.Set(item.Id);

                        if (WidgetInput.MouseLeftClick)
                        {
                            _searchInput.Unfocus();
                            if (shiftHeld)
                                SpawnToInventory(item.Id, 1);
                            else
                                SpawnToCursor(item.Id, 1);
                            WidgetInput.ConsumeClick();
                        }
                        else if (WidgetInput.MouseRightClick)
                        {
                            _searchInput.Unfocus();
                            if (shiftHeld)
                                SpawnToInventory(item.Id, item.MaxStack);
                            else
                                SpawnToCursor(item.Id, item.MaxStack);
                            WidgetInput.ConsumeRightClick();
                        }
                    }
                }

                _scroll.End();

                if (_filteredItems.Count == 0)
                {
                    UIRenderer.DrawText("No items found", gridX + 5, gridY + 10, UIColors.Warning);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Draw error: {ex.Message}");
            }
            finally
            {
                _panel.EndDraw();
            }
        }

        #endregion

        #region Spawning

        private void SpawnToCursor(int itemId, int stack)
        {
            try
            {
                if (_mouseItemField == null || _itemTypeField == null || _itemStackField == null || _itemType == null)
                {
                    _log.Warn("Cannot spawn to cursor - missing reflection fields");
                    SpawnToInventory(itemId, stack);
                    return;
                }

                var mouseItem = _mouseItemField.GetValue(null);
                if (mouseItem == null)
                {
                    SpawnToInventory(itemId, stack);
                    return;
                }

                int currentType = (int)_itemTypeField.GetValue(mouseItem);

                if (currentType == 0)
                {
                    if (_itemSetDefaultsIntMethod == null)
                    {
                        SpawnToInventory(itemId, stack);
                        return;
                    }
                    var newItem = Activator.CreateInstance(_itemType);
                    if (newItem != null)
                    {
                        InvokeSetDefaults(newItem, itemId);
                        _itemStackField.SetValue(newItem, stack);
                        _mouseItemField.SetValue(null, newItem);
                    }
                }
                else if (currentType == itemId)
                {
                    int currentStack = (int)_itemStackField.GetValue(mouseItem);
                    int maxStack = (int)_itemMaxStackField.GetValue(mouseItem);
                    int canAdd = maxStack - currentStack;
                    if (canAdd > 0)
                    {
                        int toAdd = Math.Min(stack, canAdd);
                        _itemStackField.SetValue(mouseItem, currentStack + toAdd);
                    }
                }
                else
                {
                    SpawnToInventory(itemId, stack);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to spawn to cursor: {ex.Message}");
            }
        }

        private void InvokeSetDefaults(object item, int itemId)
        {
            var parms = _itemSetDefaultsIntMethod.GetParameters();
            if (parms.Length == 1)
                _itemSetDefaultsIntMethod.Invoke(item, new object[] { itemId });
            else if (parms.Length == 2)
                _itemSetDefaultsIntMethod.Invoke(item, new object[] { itemId, null });
            else
                _itemSetDefaultsIntMethod.Invoke(item, new object[] { itemId });
        }

        private void SpawnToInventory(int itemId, int stack)
        {
            try
            {
                int myPlayer = (int)_myPlayerField.GetValue(null);
                var players = (Array)_playerArrayField.GetValue(null);
                var player = players.GetValue(myPlayer);

                var quickSpawnMethod = _playerType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "QuickSpawnItem" && m.GetParameters().Length >= 3);

                if (quickSpawnMethod != null)
                {
                    var parms = quickSpawnMethod.GetParameters();
                    var entitySourceType = parms[0].ParameterType;

                    object source = null;
                    var entitySourceIdType = _mainType.Assembly.GetType("Terraria.DataStructures.EntitySource_Parent");
                    if (entitySourceIdType == null)
                    {
                        entitySourceIdType = _mainType.Assembly.GetType("Terraria.DataStructures.EntitySource_Gift");
                    }

                    if (entitySourceIdType != null)
                    {
                        var ctor = entitySourceIdType.GetConstructors().FirstOrDefault();
                        if (ctor != null)
                        {
                            var ctorParms = ctor.GetParameters();
                            if (ctorParms.Length == 1)
                            {
                                source = ctor.Invoke(new[] { player });
                            }
                            else if (ctorParms.Length == 0)
                            {
                                source = ctor.Invoke(null);
                            }
                        }
                    }

                    if (source != null)
                    {
                        quickSpawnMethod.Invoke(player, new object[] { source, itemId, stack });
                        return;
                    }
                }

                _log.Warn("Could not spawn item - no suitable method found");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to spawn item: {ex.Message}");
            }
        }

        #endregion

        public void OnWorldLoad()
        {
            if (_allItems.Count == 0)
                BuildItemCatalog();
        }

        public void OnWorldUnload()
        {
            _panel.Close();
        }

        public void Unload()
        {
            FrameEvents.OnPreUpdate -= OnUpdate;
            _panel.UnregisterDrawCallback();
            _searchInput.Unfocus();

            _allItems.Clear();
            _filteredItems.Clear();

            _mainType = null;
            _itemType = null;
            _playerType = null;
            _netModeField = null;
            _myPlayerField = null;
            _playerArrayField = null;
            _itemNameProp = null;
            _itemMaxStackField = null;
            _mouseItemField = null;
            _itemTypeField = null;
            _itemStackField = null;
            _itemSetDefaultsIntMethod = null;
            _playerInventoryField = null;

            _log.Info("ItemSpawner unloaded");
        }

        private class ItemEntry
        {
            public int Id;
            public string Name;
            public int MaxStack;
        }
    }
}
