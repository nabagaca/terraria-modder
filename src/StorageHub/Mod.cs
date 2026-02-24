using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TerrariaModder.Core;
using TerrariaModder.Core.Assets;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.UI;
using StorageHub.Config;
using StorageHub.Storage;
using StorageHub.Patches;
using StorageHub.UI;
using StorageHub.Crafting;
using StorageHub.Relay;
using StorageHub.Debug;
using StorageHub.DedicatedBlocks;
using StorageHub.Utils;

namespace StorageHub
{
    /// <summary>
    /// Storage Hub - Unified storage access, crafting, and item management.
    ///
    /// CORE DESIGN PRINCIPLES:
    ///
    /// 1. IStorageProvider Abstraction:
    ///    - Multiplayer requires different access patterns than singleplayer
    ///    - UI calls interface methods, doesn't know if it's direct access or packets
    ///    - Built from day one to avoid retrofitting later
    ///
    /// 2. Data Safety:
    ///    - Passive operations (viewing/sorting) = ZERO writes
    ///    - UI holds ItemSnapshot copies, never references to real items
    ///    - Active operations (take/craft) are explicit user actions
    ///
    /// 3. Progression:
    ///    - 4 tiers using consumable items (Shadow Scale → Luminite)
    ///    - Station memory at Tier 3+ (endgame convenience)
    ///    - Relays extend range without tier upgrade
    ///
    /// 4. Chest Registration:
    ///    - Must manually open chest first (prevents remote looting world-gen chests)
    ///    - Locked/trapped chests excluded
    /// </summary>
    public class Mod : IMod
    {
        public string Id => "storage-hub";
        public string Name => "Storage Hub";
        public string Version => "1.0.0";

        private ILogger _log;
        private ModContext _context;
        private bool _enabled;
        private bool _dedicatedBlocksOnly;

        // Configuration
        private StorageHubConfig _hubConfig;
        private string _modFolder;

        // Core components
        private ChestRegistry _registry;
        private IStorageProvider _storageProvider;
        private ChestOpenDetector _chestDetector;
        private StorageNetworkResolver _networkResolver;

        // Crafting system
        private RecipeIndex _recipeIndex;
        private CraftabilityChecker _craftChecker;
        private CraftingExecutor _craftExecutor;
        private RecursiveCrafter _recursiveCrafter;

        // Range/Relay system
        private RangeCalculator _rangeCalc;

        // UI
        private StorageHubUI _ui;

        // Debug
        private DebugDumper _debugDumper;
        public static DebugDumper Dumper { get; private set; }

        private const int NearbyQuickStackScanRadiusTiles = 39;

        // Dedicated tile runtime IDs
        private int _storageHeartTileType = -1;
        private int _storageUnitTileType = -1;
        private int _storageComponentTileType = -1;
        private int _storageConnectorTileType = -1;
        private int _storageAccessTileType = -1;
        private int _storageCraftingAccessTileType = -1;

        // Reflection for getting world/character names
        private static Type _mainType;
        private static FieldInfo _worldNameField;
        private static FieldInfo _maxTilesXField;
        private static FieldInfo _maxTilesYField;
        private static FieldInfo _playerArrayField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerChestField;
        private static FieldInfo _playerPositionField;
        private static FieldInfo _playerWidthField;
        private static FieldInfo _playerHeightField;
        private static PropertyInfo _playerNameProp;
        private static FieldInfo _vectorXField;
        private static FieldInfo _vectorYField;
        private static MethodInfo _visualizeChestTransferMethod;
        private static MethodInfo _soundPlayMethod;
        private static FieldInfo _soundGrabField;
        private static FieldInfo _contentSamplesItemsByTypeField;
        private static PropertyInfo _contentSamplesItemsByTypeProperty;
        private static Type _itemType;
        private static MethodInfo _itemSetDefaultsMethod;
        private static FieldInfo _itemStackField;
        private bool _visualizeInitLogged;
        private bool _visualizeFailureLogged;
        private bool _visualizeUnavailableLogged;

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _context = context;

            LoadConfig();
            InitReflection();

            if (!_enabled)
            {
                _log.Info("StorageHub is disabled in config");
                return;
            }

            // Set up mod folder path (context.ModFolder points to this mod's folder)
            _modFolder = context.ModFolder;

            // Register keybind
            context.RegisterKeybind("toggle", "Toggle Storage Hub", "Open/close the Storage Hub UI", "F5", OnToggleUI);

            // Register dedicated custom tiles/items (Storage Heart + Storage Unit)
            DedicatedBlocksManager.Register(
                context,
                _log,
                OnStorageHeartRightClick,
                OnStorageAccessRightClick,
                OnStorageCraftingAccessRightClick);

            // Shift-click quick-deposit from inventory while Storage Hub is open
            InventoryQuickDepositPatch.Initialize(_log);
            VanillaQuickStackPatch.Initialize(_log);

            // Subscribe to frame events (world load/unload handled via IMod interface)
            FrameEvents.OnPreUpdate += OnUpdate;
            UIRenderer.RegisterPanelDraw("storage-hub", OnDraw);

            _dedicatedBlocksOnly = _context.Config.Get("dedicatedBlocksOnly", true);

            _log.Info(_dedicatedBlocksOnly
                ? "StorageHub initialized - use Storage Heart/Access to open"
                : "StorageHub initialized - Press F5 to open");
        }

        private void LoadConfig()
        {
            _enabled = _context.Config.Get<bool>("enabled");
        }

        private void InitReflection()
        {
            try
            {
                _mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");

                if (_mainType != null)
                {
                    _worldNameField = _mainType.GetField("worldName", BindingFlags.Public | BindingFlags.Static);
                    _maxTilesXField = _mainType.GetField("maxTilesX", BindingFlags.Public | BindingFlags.Static);
                    _maxTilesYField = _mainType.GetField("maxTilesY", BindingFlags.Public | BindingFlags.Static);
                    _playerArrayField = _mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                    _myPlayerField = _mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                }

                var playerType = Type.GetType("Terraria.Player, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Player");

                if (playerType != null)
                {
                    _playerChestField = playerType.GetField("chest", BindingFlags.Public | BindingFlags.Instance);
                    _playerPositionField = playerType.GetField("position", BindingFlags.Public | BindingFlags.Instance);
                    _playerWidthField = playerType.GetField("width", BindingFlags.Public | BindingFlags.Instance);
                    _playerHeightField = playerType.GetField("height", BindingFlags.Public | BindingFlags.Instance);
                    _playerNameProp = playerType.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
                }

                if (_playerPositionField != null)
                {
                    var vectorType = _playerPositionField.FieldType;
                    _vectorXField = vectorType?.GetField("X", BindingFlags.Public | BindingFlags.Instance);
                    _vectorYField = vectorType?.GetField("Y", BindingFlags.Public | BindingFlags.Instance);
                }

                var terrariaAsm = _mainType?.Assembly ?? Assembly.Load("Terraria");
                if (terrariaAsm != null)
                {
                    var chestType = terrariaAsm.GetType("Terraria.Chest");
                    _itemType = terrariaAsm.GetType("Terraria.Item");
                    var expectedVectorType = _playerPositionField?.FieldType;
                    _visualizeChestTransferMethod = ResolveVisualizeTransferMethod(chestType, expectedVectorType, _itemType);

                    var contentSamplesType = terrariaAsm.GetType("Terraria.ID.ContentSamples");
                    _contentSamplesItemsByTypeField = contentSamplesType?.GetField("ItemsByType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    _contentSamplesItemsByTypeProperty = contentSamplesType?.GetProperty("ItemsByType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (_itemType != null)
                    {
                        _itemStackField = _itemType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);

                        foreach (var method in _itemType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (method.Name != "SetDefaults")
                                continue;

                            var p = method.GetParameters();
                            if (p.Length >= 1 && p[0].ParameterType == typeof(int))
                            {
                                _itemSetDefaultsMethod = method;
                                break;
                            }
                        }
                    }

                    if (_visualizeChestTransferMethod != null && !_visualizeInitLogged)
                    {
                        _visualizeInitLogged = true;
                        var p = _visualizeChestTransferMethod.GetParameters();
                        _log.Info($"[QuickStack] Visual transfer method: {FormatMethodSignature(_visualizeChestTransferMethod)}");
                    }
                    else if (_visualizeChestTransferMethod == null && !_visualizeUnavailableLogged)
                    {
                        _visualizeUnavailableLogged = true;
                        _log.Warn("[QuickStack] Visual transfer method not found");
                    }

                    var soundEngineType = terrariaAsm.GetType("Terraria.Audio.SoundEngine");
                    if (soundEngineType != null)
                    {
                        foreach (var method in soundEngineType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (method.Name != "PlaySound")
                                continue;

                            var p = method.GetParameters();
                            if (p.Length == 6 && p[0].ParameterType == typeof(int))
                            {
                                _soundPlayMethod = method;
                                break;
                            }
                        }
                    }

                    var soundIdType = terrariaAsm.GetType("Terraria.ID.SoundID");
                    _soundGrabField = soundIdType?.GetField("Grab", BindingFlags.Public | BindingFlags.Static);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Reflection init error: {ex.Message}");
            }
        }

        private void OnToggleUI()
        {
            if (_ui == null) return;

            if (_ui.IsOpen)
            {
                _ui.Toggle();
                return;
            }

            if (_dedicatedBlocksOnly)
            {
                GameText.Show("Use a Storage Heart or Storage Access to open Storage Hub.");
                return;
            }

            _ui.Toggle();
        }

        private void OnUpdate()
        {
            if (!_enabled) return;

            // Check for chest opens
            _chestDetector?.Update();

            // Update UI
            _ui?.Update();
        }

        private void OnDraw()
        {
            if (!_enabled) return;

            // Draw UI (handles its own visibility check)
            _ui?.Draw();
        }

        public void OnWorldLoad()
        {
            if (!_enabled) return;

            try
            {
                string worldName = GetWorldName();
                string charName = GetCharacterName();

                // Validate we got actual names, not "Unknown"
                if (worldName == "Unknown" || charName == "Unknown")
                {
                    _log.Warn($"World load with incomplete names: world={worldName}, char={charName}");
                    _log.Warn("Config may not load correctly - please re-enter world");
                }

                _log.Info($"World loaded: {worldName}, Character: {charName}");

                // Initialize config
                _hubConfig = new StorageHubConfig(_log, _modFolder);
                _hubConfig.Load(worldName, charName);

                // Initialize registry
                _registry = new ChestRegistry(_log, _hubConfig);
                _registry.LoadFromConfig();
                // Wire up save callback so registrations persist immediately (survives force quit)
                _registry.OnRegistrationChanged = () => _hubConfig.Save();

                EnsureDedicatedTileTypesResolved();

                // Initialize storage provider
                _storageProvider = new SingleplayerProvider(_log, _registry, _hubConfig);

                // Initialize chest detector
                _chestDetector = new ChestOpenDetector(_log, _registry);
                _chestDetector.Initialize();
                _chestDetector.SetDedicatedMode(_dedicatedBlocksOnly, _storageUnitTileType);

                EnsureNetworkResolverReady(forceRecreate: true);

                // Initialize crafting system
                _recipeIndex = new RecipeIndex(_log);
                if (_recipeIndex.InitReflection())
                {
                    _recipeIndex.Build();
                }
                _craftChecker = new CraftabilityChecker(_log, _recipeIndex, _storageProvider, _hubConfig);
                _craftExecutor = new CraftingExecutor(_log, _storageProvider);
                _recursiveCrafter = new RecursiveCrafter(_log, _recipeIndex, _craftChecker);
                _recursiveCrafter.SetExecutor(_craftExecutor);

                // Initialize range calculator
                _rangeCalc = new RangeCalculator(_log, _hubConfig);

                // Initialize debug dumper (clears old dumps)
                _debugDumper = new DebugDumper(_log, _modFolder);
                Dumper = _debugDumper;

                // Initialize UI
                _ui = new StorageHubUI(_log, _storageProvider, _hubConfig, _recipeIndex, _craftChecker, _recursiveCrafter, _rangeCalc, _context.Config);
                InventoryQuickDepositPatch.SetCallbacks(
                    () => _ui != null && _ui.IsOpen,
                    TryQuickDepositInventorySlot);
                VanillaQuickStackPatch.SetCallback(
                    OnVanillaQuickStackAllChests,
                    GetVanillaQuickStackSuppressedTileType,
                    GetVanillaQuickStackSuppressedChestPositions);

                _log.Info($"StorageHub ready - {_registry.Count} registered chests, Tier {_hubConfig.Tier}");

                // Dump initial state
                _debugDumper.DumpState("WORLD_LOAD");
            }
            catch (Exception ex)
            {
                _log.Error($"OnWorldLoad error: {ex.Message}");
            }
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
                if (players == null || myPlayer < 0 || myPlayer >= players.Length)
                    return null;

                return players.GetValue(myPlayer);
            }
            catch
            {
                return null;
            }
        }

        public void OnWorldUnload()
        {
            if (!_enabled) return;

            try
            {
                // Close UI
                _ui?.Close();

                // Save config
                if (_registry != null && _hubConfig != null)
                {
                    _registry.SaveToConfig();
                    _hubConfig.Save();
                    _log.Info("StorageHub config saved");
                }

                // Reset detector
                _chestDetector?.Reset();

                // Write debug session summary
                _debugDumper?.WriteSessionSummary();
                InventoryQuickDepositPatch.ClearCallbacks();
                VanillaQuickStackPatch.ClearCallback();

                // Clear singleton and null out references to prevent stale polling between worlds
                ChestRegistry.ClearInstance();
                _ui = null;
                _hubConfig = null;
                _registry = null;
                _storageProvider = null;
                _chestDetector = null;
                _recipeIndex = null;
                _craftChecker = null;
                _craftExecutor = null;
                _recursiveCrafter = null;
                _rangeCalc = null;
                _debugDumper = null;
                Dumper = null;
                _networkResolver = null;
                _storageHeartTileType = -1;
                _storageUnitTileType = -1;
                _storageComponentTileType = -1;
                _storageConnectorTileType = -1;
                _storageAccessTileType = -1;
                _storageCraftingAccessTileType = -1;
            }
            catch (Exception ex)
            {
                _log.Error($"OnWorldUnload error: {ex.Message}");
            }
        }

        public void Unload()
        {
            FrameEvents.OnPreUpdate -= OnUpdate;
            UIRenderer.UnregisterPanelDraw("storage-hub");

            // Clean up UI
            _ui?.Close();
            _ui = null;
            InventoryQuickDepositPatch.Unload();
            VanillaQuickStackPatch.Unload();

            _hubConfig = null;
            _registry = null;
            _storageProvider = null;
            _chestDetector = null;
            _recipeIndex = null;
            _craftChecker = null;
            _craftExecutor = null;
            _recursiveCrafter = null;
            _rangeCalc = null;
            _networkResolver = null;

            _log.Info("StorageHub unloaded");
        }

        private bool OnStorageHeartRightClick(int tileX, int tileY)
        {
            return OpenDedicatedNetwork(tileX, tileY, preferCraftingTab: false);
        }

        private bool OnStorageAccessRightClick(int tileX, int tileY)
        {
            return OpenDedicatedNetwork(tileX, tileY, preferCraftingTab: false);
        }

        private bool OnStorageCraftingAccessRightClick(int tileX, int tileY)
        {
            return OpenDedicatedNetwork(tileX, tileY, preferCraftingTab: true);
        }

        private bool OpenDedicatedNetwork(int tileX, int tileY, bool preferCraftingTab)
        {
            if (!_enabled) return false;
            if (_ui == null || _registry == null) return false;

            if (!_dedicatedBlocksOnly)
            {
                if (preferCraftingTab) _ui.OpenCrafting();
                else _ui.OpenItems();
                return true;
            }

            if (_networkResolver == null)
            {
                GameText.Show("Storage network resolver is not available.");
                return false;
            }

            if (!_networkResolver.TryResolveNetwork(tileX, tileY, out var network) || !network.HasHeart)
            {
                GameText.Show("This access point is not connected to a Storage Heart.");
                return false;
            }

            if (network.UnitCount <= 0)
            {
                GameText.Show("A Storage Heart needs at least one connected Storage Unit.");
                return false;
            }

            _registry.ReplaceAll(network.UnitPositions);

            if (preferCraftingTab) _ui.OpenCrafting();
            else _ui.OpenItems();

            _log.Debug($"Opened Storage Hub from network heart ({network.HeartX}, {network.HeartY}) with {network.UnitCount} unit(s)");
            return true;
        }

        private bool TryQuickDepositInventorySlot(int inventorySlot)
        {
            if (!_enabled || _storageProvider == null || _ui == null || !_ui.IsOpen)
                return false;

            int deposited = _storageProvider.DepositFromInventorySlot(inventorySlot, singleItem: false);
            if (deposited > 0)
            {
                _ui.MarkDirty();
                return true;
            }

            return false;
        }

        private void OnVanillaQuickStackAllChests()
        {
            if (!_enabled || !_dedicatedBlocksOnly)
                return;

            if (_storageProvider == null || _registry == null)
                return;

            if (!EnsureNetworkResolverReady(forceRecreate: false))
                return;

            var player = GetLocalPlayer();
            if (player == null)
                return;

            // Keep vanilla container quick-stack behavior untouched while any container is open.
            if (GetSafeInt(_playerChestField, player, -1) != -1)
                return;

            if (!TryGetPlayerCenterTile(player, out int playerTileX, out int playerTileY))
                return;

            var networks = FindNearbyQuickStackNetworks(playerTileX, playerTileY);
            _log.Debug($"[QuickStack] Callback fired at ({playerTileX}, {playerTileY}), nearby networks={networks.Count}");
            if (networks.Count == 0)
                return;

            int totalDeposited = 0;
            bool playFeedbackSound = false;

            foreach (var network in networks)
            {
                if (network.UnitCount <= 0)
                    continue;

                var transfers = new List<QuickStackTransfer>();
                using (_registry.UseTemporaryPositions(network.UnitPositions))
                {
                    totalDeposited += _storageProvider.QuickStackInventory(
                        includeHotbar: false,
                        includeFavorited: false,
                        transfers: transfers);
                }

                if (transfers.Count > 0)
                {
                    TryVisualizeQuickStackTransfers(player, network, transfers);
                    playFeedbackSound = true;
                }
            }

            if (totalDeposited > 0)
            {
                if (playFeedbackSound)
                    TryPlayQuickStackSound();

                _ui?.MarkDirty();
                _log.Info($"[QuickStack] Deposited {totalDeposited} item(s) into {networks.Count} nearby storage network(s)");
            }
        }

        private List<StorageNetworkResult> FindNearbyQuickStackNetworks(int playerTileX, int playerTileY)
        {
            var networks = new List<StorageNetworkResult>();

            if (_networkResolver == null)
                return networks;

            if (!TryGetWorldTileBounds(out int maxTilesX, out int maxTilesY))
                return networks;

            int startX = Math.Max(0, playerTileX - NearbyQuickStackScanRadiusTiles);
            int startY = Math.Max(0, playerTileY - NearbyQuickStackScanRadiusTiles);
            int endX = Math.Min(maxTilesX - 1, playerTileX + NearbyQuickStackScanRadiusTiles);
            int endY = Math.Min(maxTilesY - 1, playerTileY + NearbyQuickStackScanRadiusTiles);

            var scannedAccesses = new HashSet<(int x, int y)>();
            var seenHearts = new HashSet<(int x, int y)>();

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    if (!CustomTileContainers.TryGetTileDefinition(x, y, out var def, out int tileType))
                        continue;

                    if (!IsQuickStackAccessTileType(tileType))
                        continue;

                    int topX = x;
                    int topY = y;
                    if (def != null && CustomTileContainers.TryGetTopLeft(x, y, def, out int resolvedTopX, out int resolvedTopY))
                    {
                        topX = resolvedTopX;
                        topY = resolvedTopY;
                    }

                    if (!scannedAccesses.Add((topX, topY)))
                        continue;

                    if (!_networkResolver.TryResolveNetwork(topX, topY, out var network))
                        continue;

                    if (!network.HasHeart || network.UnitCount <= 0)
                        continue;

                    if (!seenHearts.Add((network.HeartX, network.HeartY)))
                        continue;

                    networks.Add(network);
                }
            }

            return networks;
        }

        private bool IsQuickStackAccessTileType(int tileType)
        {
            return tileType == _storageHeartTileType ||
                   tileType == _storageAccessTileType ||
                   tileType == _storageCraftingAccessTileType;
        }

        private int GetVanillaQuickStackSuppressedTileType()
        {
            if (!_enabled || !_dedicatedBlocksOnly)
                return -1;

            EnsureDedicatedTileTypesResolved();
            return _storageUnitTileType;
        }

        private IEnumerable<(int x, int y)> GetVanillaQuickStackSuppressedChestPositions()
        {
            if (!_enabled || !_dedicatedBlocksOnly)
                return Array.Empty<(int x, int y)>();

            var positions = new HashSet<(int x, int y)>();

            if (_registry != null)
            {
                foreach (var pos in _registry.GetRegisteredPositions())
                    positions.Add(pos);
            }

            if (EnsureNetworkResolverReady(forceRecreate: false))
            {
                var player = GetLocalPlayer();
                if (player != null && TryGetPlayerCenterTile(player, out int playerTileX, out int playerTileY))
                {
                    foreach (var network in FindNearbyQuickStackNetworks(playerTileX, playerTileY))
                    {
                        foreach (var unitPos in network.UnitPositions)
                            positions.Add(unitPos);
                    }
                }
            }

            return positions;
        }

        private void EnsureDedicatedTileTypesResolved()
        {
            _storageHeartTileType = DedicatedBlocksManager.ResolveStorageHeartTileType();
            _storageUnitTileType = DedicatedBlocksManager.ResolveStorageUnitTileType();
            _storageComponentTileType = DedicatedBlocksManager.ResolveStorageComponentTileType();
            _storageConnectorTileType = DedicatedBlocksManager.ResolveStorageConnectorTileType();
            _storageAccessTileType = DedicatedBlocksManager.ResolveStorageAccessTileType();
            _storageCraftingAccessTileType = DedicatedBlocksManager.ResolveStorageCraftingAccessTileType();
        }

        private bool EnsureNetworkResolverReady(bool forceRecreate)
        {
            EnsureDedicatedTileTypesResolved();

            bool validTypes = _storageHeartTileType >= 0 &&
                              _storageUnitTileType >= 0 &&
                              _storageComponentTileType >= 0 &&
                              _storageConnectorTileType >= 0 &&
                              _storageAccessTileType >= 0 &&
                              _storageCraftingAccessTileType >= 0;

            if (!validTypes)
            {
                _log.Warn($"[QuickStack] Dedicated tile type resolution failed: heart={_storageHeartTileType}, unit={_storageUnitTileType}, component={_storageComponentTileType}, connector={_storageConnectorTileType}, access={_storageAccessTileType}, crafting={_storageCraftingAccessTileType}");
                return false;
            }

            if (_chestDetector != null)
                _chestDetector.SetDedicatedMode(_dedicatedBlocksOnly, _storageUnitTileType);

            if (forceRecreate || _networkResolver == null)
            {
                _networkResolver = new StorageNetworkResolver(
                    _log,
                    _storageHeartTileType,
                    _storageUnitTileType,
                    _storageComponentTileType,
                    _storageConnectorTileType,
                    _storageAccessTileType,
                    _storageCraftingAccessTileType);
            }

            return true;
        }

        private bool TryVisualizeQuickStackTransfers(object player, StorageNetworkResult network, IReadOnlyList<QuickStackTransfer> transfers)
        {
            if (transfers == null || transfers.Count == 0)
                return false;

            if (_visualizeChestTransferMethod == null)
            {
                if (!_visualizeUnavailableLogged)
                {
                    _visualizeUnavailableLogged = true;
                    _log.Warn("[QuickStack] Visual transfer unavailable at runtime");
                }
                return false;
            }

            try
            {
                if (!TryGetPlayerCenterWorld(player, out float fromX, out float fromY))
                    return false;

                object from = CreateVector2(fromX, fromY);
                // Use heart center so stacked units above/below are never treated as transfer target.
                object to = CreateVector2(network.HeartX * 16f + 16f, network.HeartY * 16f + 16f);
                if (from == null || to == null)
                    return false;

                object itemsByType = _contentSamplesItemsByTypeField?.GetValue(null)
                    ?? _contentSamplesItemsByTypeProperty?.GetValue(null, null);

                bool visualized = false;
                int visualizedCount = 0;

                foreach (var transfer in transfers)
                {
                    if (transfer.ItemId <= 0 || transfer.Stack <= 0)
                        continue;

                    // Prefer a concrete item instance, matching Magic Storage behavior.
                    if (!TryCreateTransferItemSample(transfer.ItemId, out object itemSample) &&
                        !TryGetContentSampleItem(itemsByType, transfer.ItemId, out itemSample))
                    {
                        continue;
                    }

                    if (itemSample != null && _itemStackField != null)
                    {
                        try { _itemStackField.SetValue(itemSample, Math.Max(1, transfer.Stack)); }
                        catch { }
                    }

                    try
                    {
                        if (!TryInvokeVisualizeTransfer(from, to, itemSample, transfer.ItemId, transfer.Stack))
                            continue;

                        visualized = true;
                        visualizedCount++;
                    }
                    catch (Exception ex)
                    {
                        if (!_visualizeFailureLogged)
                        {
                            _visualizeFailureLogged = true;
                            _log.Warn($"[QuickStack] Visual transfer invocation failed: {ex.Message}");
                        }
                    }
                }

                if (!visualized)
                    _log.Info($"[QuickStack] Visualization skipped (transfers={transfers.Count}, visuals={visualizedCount})");

                return visualized;
            }
            catch (Exception ex)
            {
                if (!_visualizeFailureLogged)
                {
                    _visualizeFailureLogged = true;
                    _log.Warn($"[QuickStack] Visual transfer failed: {ex.Message}");
                }

                return false;
            }
        }

        private bool TryGetPlayerCenterWorld(object player, out float worldX, out float worldY)
        {
            worldX = 0f;
            worldY = 0f;

            try
            {
                if (player == null || _playerPositionField == null || _vectorXField == null || _vectorYField == null)
                    return false;

                var position = _playerPositionField.GetValue(player);
                if (position == null)
                    return false;

                object xObj = _vectorXField.GetValue(position);
                object yObj = _vectorYField.GetValue(position);
                if (xObj == null || yObj == null)
                    return false;

                float x = Convert.ToSingle(xObj);
                float y = Convert.ToSingle(yObj);
                int width = GetSafeInt(_playerWidthField, player, 20);
                int height = GetSafeInt(_playerHeightField, player, 40);

                worldX = x + width * 0.5f;
                worldY = y + height * 0.5f;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private object CreateVector2(float x, float y)
        {
            try
            {
                var vectorType = _playerPositionField?.FieldType;
                if (vectorType == null)
                    return null;

                return Activator.CreateInstance(vectorType, new object[] { x, y });
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetContentSampleItem(object itemsByType, int itemType, out object item)
        {
            item = null;
            if (itemsByType == null || itemType <= 0)
                return false;

            try
            {
                if (itemsByType is Array array)
                {
                    if (itemType >= 0 && itemType < array.Length)
                    {
                        item = array.GetValue(itemType);
                        return item != null;
                    }

                    return false;
                }

                if (itemsByType is IDictionary dictionary)
                {
                    if (dictionary.Contains(itemType))
                    {
                        item = dictionary[itemType];
                        return item != null;
                    }

                    return false;
                }

                var type = itemsByType.GetType();
                var indexer = type.GetProperty("Item", new[] { typeof(int) });
                if (indexer != null)
                {
                    item = indexer.GetValue(itemsByType, new object[] { itemType });
                    if (item != null)
                        return true;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!string.Equals(method.Name, "TryGetValue", StringComparison.Ordinal))
                        continue;

                    var p = method.GetParameters();
                    if (p.Length != 2 || p[0].ParameterType != typeof(int) || !p[1].ParameterType.IsByRef)
                        continue;

                    var args = new object[] { itemType, null };
                    bool found = (bool)method.Invoke(itemsByType, args);
                    if (found)
                    {
                        item = args[1];
                        return item != null;
                    }

                    return false;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private bool TryCreateTransferItemSample(int itemType, out object item)
        {
            item = null;
            if (itemType <= 0 || _itemType == null || _itemSetDefaultsMethod == null)
                return false;

            try
            {
                item = Activator.CreateInstance(_itemType);
                if (item == null)
                    return false;

                int paramCount = _itemSetDefaultsMethod.GetParameters().Length;
                if (paramCount <= 1)
                    _itemSetDefaultsMethod.Invoke(item, new object[] { itemType });
                else
                    _itemSetDefaultsMethod.Invoke(item, new object[] { itemType, null });

                return true;
            }
            catch
            {
                item = null;
                return false;
            }
        }

        private bool TryInvokeVisualizeTransfer(object from, object to, object itemSample, int itemType, int stack)
        {
            if (_visualizeChestTransferMethod == null || from == null || to == null || itemSample == null)
                return false;

            try
            {
                var parameters = _visualizeChestTransferMethod.GetParameters();
                if (parameters.Length == 0)
                    return false;

                var args = new object[parameters.Length];
                bool usedFrom = false;
                bool usedTo = false;
                bool usedItem = false;
                bool usedItemType = false;
                bool usedStack = false;

                Type vectorType = _playerPositionField?.FieldType ?? from.GetType();
                Type itemRuntimeType = itemSample.GetType();

                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    Type paramType = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;

                    if (!usedFrom && vectorType != null && paramType == vectorType)
                    {
                        args[i] = from;
                        usedFrom = true;
                        continue;
                    }

                    if (!usedTo && vectorType != null && paramType == vectorType)
                    {
                        args[i] = to;
                        usedTo = true;
                        continue;
                    }

                    if (!usedItem && (paramType == itemRuntimeType || paramType.IsAssignableFrom(itemRuntimeType)))
                    {
                        args[i] = itemSample;
                        usedItem = true;
                        continue;
                    }

                    if (!usedItemType && (paramType == typeof(int) || paramType == typeof(short) || paramType == typeof(byte) || paramType == typeof(long)))
                    {
                        args[i] = Convert.ChangeType(Math.Max(1, itemType), paramType);
                        usedItemType = true;
                        usedItem = true; // Newer signatures use item type instead of Item instance.
                        continue;
                    }

                    if (paramType != null && string.Equals(paramType.Name, "ItemTransferVisualizationSettings", StringComparison.Ordinal))
                    {
                        args[i] = CreateItemTransferVisualizationSettings(paramType, stack);
                        continue;
                    }

                    if (!usedStack && !usedItemType &&
                        (paramType == typeof(int) || paramType == typeof(short) || paramType == typeof(byte) || paramType == typeof(long)))
                    {
                        args[i] = Convert.ChangeType(Math.Max(1, stack), paramType);
                        usedStack = true;
                        continue;
                    }

                    args[i] = GetDefaultValue(paramType);
                }

                if (!usedFrom || !usedTo || !usedItem)
                    return false;

                _visualizeChestTransferMethod.Invoke(null, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private object CreateItemTransferVisualizationSettings(Type settingsType, int stack)
        {
            if (settingsType == null)
                return null;

            try
            {
                object settings = Activator.CreateInstance(settingsType);
                if (settings == null)
                    return null;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (var field in settingsType.GetFields(flags))
                {
                    if (!IsLikelyStackMemberName(field.Name))
                        continue;

                    TryAssignIntegral(settings, field.FieldType, v => field.SetValue(settings, v), stack);
                }

                foreach (var prop in settingsType.GetProperties(flags))
                {
                    if (!prop.CanWrite || !IsLikelyStackMemberName(prop.Name))
                        continue;

                    TryAssignIntegral(settings, prop.PropertyType, v => prop.SetValue(settings, v, null), stack);
                }

                return settings;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLikelyStackMemberName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string n = name.ToLowerInvariant();
            return n.Contains("stack") || n.Contains("count") || n.Contains("amount");
        }

        private static void TryAssignIntegral(object target, Type memberType, Action<object> setter, int value)
        {
            if (target == null || memberType == null || setter == null)
                return;

            if (memberType != typeof(int) &&
                memberType != typeof(short) &&
                memberType != typeof(byte) &&
                memberType != typeof(long) &&
                memberType != typeof(uint) &&
                memberType != typeof(ushort) &&
                memberType != typeof(ulong))
            {
                return;
            }

            try
            {
                object converted = Convert.ChangeType(Math.Max(1, value), memberType);
                setter(converted);
            }
            catch
            {
                // Best effort only.
            }
        }

        private MethodInfo ResolveVisualizeTransferMethod(Type chestType, Type vectorType, Type itemType)
        {
            if (chestType == null)
                return null;

            MethodInfo best = null;
            int bestScore = int.MinValue;

            foreach (var method in chestType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "VisualizeChestTransfer", StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                int score = 0;

                if (parameters.Length >= 4)
                    score += 3;

                int vectorMatches = 0;
                bool hasItem = false;
                bool hasIntegral = false;
                foreach (var p in parameters)
                {
                    var paramType = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    if (vectorType != null && paramType == vectorType)
                        vectorMatches++;
                    if (itemType != null && (paramType == itemType || paramType.IsAssignableFrom(itemType)))
                        hasItem = true;
                    if (paramType == typeof(int) || paramType == typeof(short) || paramType == typeof(byte) || paramType == typeof(long))
                        hasIntegral = true;
                }

                score += vectorMatches >= 2 ? 4 : vectorMatches;
                if (hasItem) score += 3;
                if (hasIntegral) score += 2;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = method;
                }
            }

            return best;
        }

        private static object GetDefaultValue(Type type)
        {
            if (type == null)
                return null;
            if (!type.IsValueType)
                return null;
            if (type == typeof(bool))
                return false;
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatMethodSignature(MethodInfo method)
        {
            if (method == null)
                return "<null>";

            var parameters = method.GetParameters();
            var parts = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                parts[i] = parameters[i].ParameterType.Name;

            return $"{method.Name}({string.Join(", ", parts)})";
        }

        private void TryPlayQuickStackSound()
        {
            try
            {
                if (_soundPlayMethod == null)
                    return;

                int soundId = GetSafeStaticInt(_soundGrabField, 7);
                _soundPlayMethod.Invoke(null, new object[] { soundId, -1, -1, 1, 1f, 0f });
            }
            catch
            {
                // Best effort feedback only.
            }
        }

        private bool TryGetPlayerCenterTile(object player, out int tileX, out int tileY)
        {
            tileX = 0;
            tileY = 0;

            try
            {
                if (player == null || _playerPositionField == null || _vectorXField == null || _vectorYField == null)
                    return false;

                var position = _playerPositionField.GetValue(player);
                if (position == null)
                    return false;

                object xObj = _vectorXField.GetValue(position);
                object yObj = _vectorYField.GetValue(position);
                if (xObj == null || yObj == null)
                    return false;

                float x = Convert.ToSingle(xObj);
                float y = Convert.ToSingle(yObj);
                int width = GetSafeInt(_playerWidthField, player, 20);
                int height = GetSafeInt(_playerHeightField, player, 40);

                float centerX = x + width * 0.5f;
                float centerY = y + height * 0.5f;

                tileX = (int)(centerX / 16f);
                tileY = (int)(centerY / 16f);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetWorldTileBounds(out int maxTilesX, out int maxTilesY)
        {
            maxTilesX = 0;
            maxTilesY = 0;

            try
            {
                object xObj = _maxTilesXField?.GetValue(null);
                object yObj = _maxTilesYField?.GetValue(null);
                if (xObj == null || yObj == null)
                    return false;

                maxTilesX = Convert.ToInt32(xObj);
                maxTilesY = Convert.ToInt32(yObj);
                return maxTilesX > 0 && maxTilesY > 0;
            }
            catch
            {
                return false;
            }
        }

        private int GetSafeInt(FieldInfo field, object obj, int defaultValue)
        {
            if (field == null || obj == null)
                return defaultValue;

            try
            {
                var val = field.GetValue(obj);
                if (val == null)
                    return defaultValue;
                return Convert.ToInt32(val);
            }
            catch
            {
                return defaultValue;
            }
        }

        private int GetSafeStaticInt(FieldInfo field, int defaultValue)
        {
            if (field == null)
                return defaultValue;

            try
            {
                var value = field.GetValue(null);
                if (value == null)
                    return defaultValue;

                return Convert.ToInt32(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        private string GetWorldName()
        {
            try
            {
                var worldName = _worldNameField?.GetValue(null) as string;
                if (string.IsNullOrEmpty(worldName))
                {
                    _log.Warn("World name is empty or null");
                    return "Unknown";
                }
                return worldName;
            }
            catch (Exception ex)
            {
                _log.Error($"GetWorldName error: {ex.Message}");
                return "Unknown";
            }
        }

        private string GetCharacterName()
        {
            try
            {
                if (_myPlayerField == null || _playerArrayField == null)
                {
                    _log.Warn("Player reflection fields not initialized");
                    return "Unknown";
                }

                var myPlayerVal = _myPlayerField.GetValue(null);
                if (myPlayerVal == null)
                {
                    _log.Warn("myPlayer field is null");
                    return "Unknown";
                }

                int myPlayer = (int)myPlayerVal;
                var players = _playerArrayField.GetValue(null) as Array;
                if (players == null)
                {
                    _log.Warn("Player array is null");
                    return "Unknown";
                }

                // Bounds check before array access
                if (myPlayer < 0 || myPlayer >= players.Length)
                {
                    _log.Warn($"myPlayer index {myPlayer} out of bounds (array length: {players.Length})");
                    return "Unknown";
                }

                var player = players.GetValue(myPlayer);
                if (player == null)
                {
                    _log.Warn($"Player at index {myPlayer} is null");
                    return "Unknown";
                }

                // Try to get name field/property
                var charName = _playerNameProp?.GetValue(player) as string;
                if (string.IsNullOrEmpty(charName))
                {
                    // Try direct field access as fallback
                    var nameField = player.GetType().GetField("name", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    charName = nameField?.GetValue(player) as string;
                }

                if (string.IsNullOrEmpty(charName))
                {
                    _log.Warn("Character name is empty or null");
                    return "Unknown";
                }
                return charName;
            }
            catch (Exception ex)
            {
                _log.Error($"GetCharacterName error: {ex.Message}");
                return "Unknown";
            }
        }
    }
}
