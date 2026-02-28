using HarmonyLib;
using Terraria;

namespace WidescreenTools.Patches
{
    [HarmonyPatch(typeof(Main), "CacheSupportedDisplaySizes")]
    internal static class WidescreenResolutionPatch
    {
        [HarmonyPostfix]
        private static void CacheSupportedDisplaySizes_Postfix()
        {
            WidescreenResolutionOverride.Apply();
            Mod.Instance?.ApplySavedResolutionFromCache();
        }
    }
}
