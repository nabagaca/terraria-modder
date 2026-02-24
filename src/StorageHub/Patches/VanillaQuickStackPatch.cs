using System;
using System.Reflection;
using HarmonyLib;
using TerrariaModder.Core.Logging;

namespace StorageHub.Patches
{
    /// <summary>
    /// Hooks the vanilla "Quick Stack to Nearby Chests" action so Storage Hub can
    /// apply additional quick-stack behavior after Terraria handles nearby chests.
    /// </summary>
    internal static class VanillaQuickStackPatch
    {
        private static Harmony _harmony;
        private static ILogger _log;
        private static bool _applied;

        private static Action _onQuickStackAllChests;

        public static void Initialize(ILogger log)
        {
            _log = log;
            if (_applied) return;

            try
            {
                var playerType = Type.GetType("Terraria.Player, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Player");
                if (playerType == null)
                {
                    _log?.Warn("[VanillaQuickStackPatch] Terraria.Player type not found");
                    return;
                }

                MethodInfo quickStack = playerType.GetMethod("QuickStackAllChests",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);

                if (quickStack == null)
                {
                    foreach (var method in playerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (method.Name == "QuickStackAllChests" && method.GetParameters().Length == 0)
                        {
                            quickStack = method;
                            break;
                        }
                    }
                }

                if (quickStack == null)
                {
                    _log?.Warn("[VanillaQuickStackPatch] Player.QuickStackAllChests not found");
                    return;
                }

                _harmony = new Harmony("com.storagehub.vanilla.quickstack");
                _harmony.Patch(quickStack,
                    postfix: new HarmonyMethod(typeof(VanillaQuickStackPatch), nameof(QuickStackAllChests_Postfix)));

                _applied = true;
                _log?.Info("[VanillaQuickStackPatch] Applied Player.QuickStackAllChests postfix");
            }
            catch (Exception ex)
            {
                _log?.Error($"[VanillaQuickStackPatch] Failed to apply: {ex.Message}");
            }
        }

        public static void Unload()
        {
            try
            {
                _onQuickStackAllChests = null;

                if (_harmony != null)
                {
                    _harmony.UnpatchAll(_harmony.Id);
                    _harmony = null;
                }
            }
            catch
            {
                // Best effort unpatch.
            }
            finally
            {
                _applied = false;
            }
        }

        public static void SetCallback(Action onQuickStackAllChests)
        {
            _onQuickStackAllChests = onQuickStackAllChests;
        }

        public static void ClearCallback()
        {
            _onQuickStackAllChests = null;
        }

        private static void QuickStackAllChests_Postfix()
        {
            try
            {
                _log?.Debug("[VanillaQuickStackPatch] Player.QuickStackAllChests postfix invoked");
                _onQuickStackAllChests?.Invoke();
            }
            catch (Exception ex)
            {
                _log?.Error($"[VanillaQuickStackPatch] Postfix error: {ex.Message}");
            }
        }
    }
}
