using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using TerrariaModder.Core;
using TerrariaModder.Core.Logging;

namespace PetChests
{
    public class Mod : IMod
    {
        public string Id => "pet-chests";
        public string Name => "Pet Chests";
        public string Version => "1.0.0";

        private static ILogger _log;
        private static ModContext _context;
        private static Harmony _harmony;
        private static Timer _patchTimer;

        // Config
        internal static bool Enabled = true;
        internal static int InteractionRange = 200;

        // Cached types - will be resolved at runtime
        internal static Type MainType;
        internal static Type PlayerType;
        internal static Type ProjectileType;

        public void Initialize(ModContext context)
        {
            _context = context;
            _log = context.Logger;

            LoadConfig();

            _log.Info("Pet Chests initializing...");

            try
            {
                // Find Terraria types
                var terrariaAsm = Assembly.Load("Terraria");
                MainType = terrariaAsm.GetType("Terraria.Main");
                PlayerType = terrariaAsm.GetType("Terraria.Player");
                ProjectileType = terrariaAsm.GetType("Terraria.Projectile");

                if (MainType == null || PlayerType == null || ProjectileType == null)
                {
                    _log.Error("Could not find Terraria types");
                    return;
                }

                _harmony = new Harmony("com.terrariamodder.petchests");

                // Delay patching to avoid early initialization issues
                _patchTimer = new Timer(PatchAfterDelay, null, 5000, Timeout.Infinite);
                _log.Info("Patches will be applied after 5 seconds...");
            }
            catch (Exception ex)
            {
                _log.Error($"Init failed: {ex.Message}");
            }
        }

        private static void PatchAfterDelay(object state)
        {
            try
            {
                _log?.Info("Applying patches now...");

                // Patch Projectile.IsInteractable
                var isInteractableMethod = ProjectileType.GetMethod("IsInteractable",
                    BindingFlags.Public | BindingFlags.Instance);
                if (isInteractableMethod != null)
                {
                    var postfix = typeof(Mod).GetMethod("IsInteractible_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    _harmony.Patch(isInteractableMethod, postfix: new HarmonyMethod(postfix));
                    _log?.Info("Patched Projectile.IsInteractable");
                }

                // Patch Projectile.TryGetContainerIndex
                var tryGetContainerMethod = ProjectileType.GetMethod("TryGetContainerIndex",
                    BindingFlags.Public | BindingFlags.Instance);
                if (tryGetContainerMethod != null)
                {
                    var prefix = typeof(Mod).GetMethod("TryGetContainerIndex_Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    _harmony.Patch(tryGetContainerMethod, prefix: new HarmonyMethod(prefix));
                    _log?.Info("Patched Projectile.TryGetContainerIndex");
                }

                // Patch Player.Update - use dynamic method lookup
                var updateMethod = PlayerType.GetMethod("Update", new Type[] { typeof(int) });
                if (updateMethod != null)
                {
                    var prefix = typeof(Mod).GetMethod("PlayerUpdate_Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    var postfix = typeof(Mod).GetMethod("PlayerUpdate_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    _harmony.Patch(updateMethod,
                        prefix: new HarmonyMethod(prefix),
                        postfix: new HarmonyMethod(postfix));
                    _log?.Info("Patched Player.Update");
                }

                // Patch Main.PlayInteractiveProjectileOpenCloseSound to mute during pet piggy bank
                // Method signature: public static void PlayInteractiveProjectileOpenCloseSound(int projType, bool open)
                var playSoundMethod = MainType.GetMethod("PlayInteractiveProjectileOpenCloseSound",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(int), typeof(bool) }, null);
                if (playSoundMethod != null)
                {
                    var prefix = typeof(Mod).GetMethod("PlaySound_Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    _harmony.Patch(playSoundMethod, prefix: new HarmonyMethod(prefix));
                    _log?.Info("Patched Main.PlayInteractiveProjectileOpenCloseSound");
                }
                else
                {
                    _log?.Info("Could not find PlayInteractiveProjectileOpenCloseSound");
                }

                // Patch Player.HandleBeingInChestRange to skip tile-based chest checks when our pet piggy is open
                var handleChestMethod = PlayerType.GetMethod("HandleBeingInChestRange",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (handleChestMethod != null)
                {
                    var prefix = typeof(Mod).GetMethod("HandleChestRange_Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    _harmony.Patch(handleChestMethod, prefix: new HarmonyMethod(prefix));
                    _log?.Info("Patched Player.HandleBeingInChestRange");
                }
                else
                {
                    _log?.Info("Could not find HandleBeingInChestRange");
                }

                _log?.Info("All patches applied successfully");
            }
            catch (Exception ex)
            {
                _log?.Error($"Delayed patch error: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            Enabled = _context.Config.Get("enabled", true);
            InteractionRange = _context.Config.Get("interactionRange", 200);
        }

        public void OnConfigChanged()
        {
            LoadConfig();
            _log.Info($"Config reloaded - Enabled: {Enabled}, Range: {InteractionRange}");
        }

        public void OnWorldLoad()
        {
            PetInteraction.Reset();
            _log.Info("World loaded - interaction state reset");
        }

        public void OnWorldUnload()
        {
            PetInteraction.Reset();
        }

        public void Unload()
        {
            _patchTimer?.Dispose();
            _harmony?.UnpatchAll("com.terrariamodder.petchests");
            _patchTimer = null;
            _log.Info("Pet Chests unloaded");
        }

        internal static void Log(string message) => _log?.Info(message);

        #region Harmony Patches

        /// <summary>
        /// Make cosmetic pets interactible like Chester
        /// But NOT while piggy bank is already open or just closed
        /// </summary>
        public static void IsInteractible_Postfix(object __instance, ref bool __result)
        {
            if (!Enabled) return;

            try
            {
                // If piggy bank is open via pet OR just closed, make pet NOT interactible
                // This prevents vanilla from repeatedly trying to interact with it
                // and prevents immediate reopen after closing
                if (PetInteraction.ShouldBlockInteraction())
                {
                    __result = false;
                    return;
                }

                if (__result) return;

                if (PetHelper.IsCosmeticPet(__instance))
                {
                    __result = true;
                }
            }
            catch { }
        }

        /// <summary>
        /// Return piggy bank container index (-2) for cosmetic pets
        /// </summary>
        public static bool TryGetContainerIndex_Prefix(object __instance, ref int containerIndex, ref bool __result)
        {
            if (!Enabled) return true;

            try
            {
                if (PetHelper.IsCosmeticPet(__instance))
                {
                    containerIndex = -2; // Piggy bank
                    __result = true;
                    return false; // Skip original
                }
            }
            catch { }

            return true;
        }

        private static int _updateCount = 0;
        private const int MIN_UPDATES = 300;
        private static FieldInfo _gameMenuField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _chestField;

        /// <summary>
        /// Prefix: Block input and force chest state before vanilla processes
        /// </summary>
        public static void PlayerUpdate_Prefix(object __instance, int i)
        {
            if (_updateCount < MIN_UPDATES) return;
            if (!Enabled) return;

            try
            {
                if (_gameMenuField == null)
                    _gameMenuField = MainType.GetField("gameMenu", BindingFlags.Public | BindingFlags.Static);
                if (_myPlayerField == null)
                    _myPlayerField = MainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                if (_chestField == null)
                    _chestField = PlayerType.GetField("chest", BindingFlags.Public | BindingFlags.Instance);

                bool gameMenu = (bool)_gameMenuField.GetValue(null);
                if (gameMenu) return;

                int myPlayer = (int)_myPlayerField.GetValue(null);
                if (i != myPlayer) return;

                // Block input BEFORE vanilla processes it - critical for preventing clicking sounds
                PetInteraction.BlockInputInPrefix(__instance);

                // If we're keeping piggy open, force chest state
                if (PetInteraction.IsKeepingPiggyOpen())
                {
                    int chest = (int)_chestField.GetValue(__instance);
                    if (chest == -1)
                    {
                        _chestField.SetValue(__instance, -2);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix: Handle pet interactions
        /// </summary>
        public static void PlayerUpdate_Postfix(object __instance, int i)
        {
            _updateCount++;
            if (_updateCount < MIN_UPDATES) return;
            if (!Enabled) return;

            try
            {
                if (_gameMenuField == null)
                    _gameMenuField = MainType.GetField("gameMenu", BindingFlags.Public | BindingFlags.Static);
                if (_myPlayerField == null)
                    _myPlayerField = MainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);

                bool gameMenu = (bool)_gameMenuField.GetValue(null);
                if (gameMenu) return;

                int myPlayer = (int)_myPlayerField.GetValue(null);
                if (i != myPlayer) return;

                // Set interactible flags for cosmetic pets
                PetInteraction.SetInteractableFlags(__instance);

                // Handle pet interaction
                PetInteraction.HandleInteraction(__instance);
            }
            catch (Exception ex)
            {
                Log($"Update error: {ex.Message}");
            }
        }

        /// <summary>
        /// Skip the open/close sound while our pet piggy bank is active
        /// This prevents the clicking spam from vanilla's tracker validation
        /// </summary>
        public static bool PlaySound_Prefix()
        {
            if (!Enabled) return true;

            // If we're keeping piggy open via pet, skip the sound
            if (PetInteraction.IsKeepingPiggyOpen())
            {
                return false; // Skip original
            }

            return true;
        }

        /// <summary>
        /// Skip HandleBeingInChestRange when our pet piggy bank is open.
        /// This prevents vanilla from checking tile-based chests and resetting chest=-1.
        /// Chester works because it sets the tracker to a valid type 960 projectile.
        /// We can't do that with cosmetic pets (wrong type), so we skip the method entirely.
        /// </summary>
        public static bool HandleChestRange_Prefix(object __instance)
        {
            if (!Enabled) return true;

            // If we're keeping piggy open via pet, skip the entire method
            if (PetInteraction.IsKeepingPiggyOpen())
            {
                return false; // Skip original
            }

            return true;
        }

        #endregion
    }
}
