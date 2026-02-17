using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaModder.Core.Logging;

namespace WhipStacking
{
    internal static class TagPatches
    {
        private static ILogger _log;
        internal static bool Enabled = true;

        // TagEffectState fields
        private static FieldInfo _ownerField;
        private static FieldInfo _effectField;
        private static MethodInfo _typeSetter;

        // UniqueTagEffect fields/methods
        private static FieldInfo _tagDurationField;
        private static MethodInfo _canApplyTagToNPC;
        private static MethodInfo _onSetToPlayer;
        private static MethodInfo _onTagAppliedToNPC;
        private static MethodInfo _canRunHitEffects;
        private static MethodInfo _modifyTaggedHit;
        private static MethodInfo _modifyProcHit;
        private static MethodInfo _onTaggedHit;
        private static MethodInfo _onProcHit;

        // ItemID.Sets.UniqueTagEffects array
        private static Array _uniqueTagEffects;

        // NPC fields
        private static FieldInfo _npcWhoAmI;
        private static FieldInfo _npcType;

        // Player fields
        private static FieldInfo _playerWhoAmI;

        // Main.maxNPCs
        private static int _maxNPCs;

        // Reusable list for Update cleanup (avoids allocation per frame)
        private static readonly List<int> _toRemove = new List<int>();

        internal static bool Initialize(ILogger log)
        {
            _log = log;
            try
            {
                var asm = Assembly.Load("Terraria");
                var tagType = asm.GetType("Terraria.GameContent.Items.TagEffectState");
                var effectType = asm.GetType("Terraria.GameContent.Items.UniqueTagEffect");
                var mainType = asm.GetType("Terraria.Main");
                var playerType = asm.GetType("Terraria.Player");
                var npcType = asm.GetType("Terraria.NPC");
                var setsType = asm.GetType("Terraria.ID.ItemID+Sets");

                // TagEffectState
                _ownerField = tagType.GetField("_owner", BindingFlags.NonPublic | BindingFlags.Instance);
                _effectField = tagType.GetField("_effect", BindingFlags.NonPublic | BindingFlags.Instance);
                _typeSetter = tagType.GetProperty("Type").GetSetMethod(true);

                // UniqueTagEffect
                _tagDurationField = effectType.GetField("TagDuration");
                _canApplyTagToNPC = effectType.GetMethod("CanApplyTagToNPC");
                _onSetToPlayer = effectType.GetMethod("OnSetToPlayer");
                _onTagAppliedToNPC = effectType.GetMethod("OnTagAppliedToNPC");
                _canRunHitEffects = effectType.GetMethod("CanRunHitEffects");
                _modifyTaggedHit = effectType.GetMethod("ModifyTaggedHit");
                _modifyProcHit = effectType.GetMethod("ModifyProcHit");
                _onTaggedHit = effectType.GetMethod("OnTaggedHit");
                _onProcHit = effectType.GetMethod("OnProcHit");

                // ItemID.Sets.UniqueTagEffects
                _uniqueTagEffects = (Array)setsType.GetField("UniqueTagEffects",
                    BindingFlags.Public | BindingFlags.Static).GetValue(null);

                // NPC
                _npcWhoAmI = npcType.GetField("whoAmI", BindingFlags.Public | BindingFlags.Instance);
                _npcType = npcType.GetField("type", BindingFlags.Public | BindingFlags.Instance);

                // Player
                _playerWhoAmI = playerType.GetField("whoAmI", BindingFlags.Public | BindingFlags.Instance);

                // Main.maxNPCs
                _maxNPCs = (int)mainType.GetField("maxNPCs", BindingFlags.Public | BindingFlags.Static).GetValue(null);

                // Verify critical fields
                if (_ownerField == null) { log.Error("_owner not found"); return false; }
                if (_effectField == null) { log.Error("_effect not found"); return false; }
                if (_typeSetter == null) { log.Error("Type setter not found"); return false; }
                if (_tagDurationField == null) { log.Error("TagDuration not found"); return false; }
                if (_uniqueTagEffects == null) { log.Error("UniqueTagEffects not found"); return false; }

                log.Info($"Reflection initialized, maxNPCs={_maxNPCs}");
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Reflection init failed: {ex.Message}");
                return false;
            }
        }

        // --- Helpers ---

        private static int GetPlayerIndex(object tagState)
        {
            var owner = _ownerField.GetValue(tagState);
            return (int)_playerWhoAmI.GetValue(owner);
        }

        private static int GetNpcIndex(object npc) => (int)_npcWhoAmI.GetValue(npc);
        private static int GetNpcTypeId(object npc) => (int)_npcType.GetValue(npc);
        private static object GetEffect(int itemType) => _uniqueTagEffects.GetValue(itemType);
        private static int GetTagDuration(object effect) => (int)_tagDurationField.GetValue(effect);

        private static WhipTagEntry GetOrCreateEntry(int playerIdx, int whipType, object effect)
        {
            var dict = MultiTagState.GetOrCreate(playerIdx);
            if (!dict.TryGetValue(whipType, out var entry))
            {
                entry = new WhipTagEntry(whipType, effect, _maxNPCs);
                dict[whipType] = entry;
            }
            return entry;
        }

        /// <summary>
        /// Shared logic for setting active effect. Updates vanilla fields for compatibility,
        /// adds new whip to our dict without clearing existing whips.
        /// </summary>
        private static void SetActiveEffect(object tagState, int type)
        {
            var effect = GetEffect(type);

            // Update vanilla _effect/Type for compatibility with unpatched code
            _effectField.SetValue(tagState, effect);
            _typeSetter.Invoke(tagState, new object[] { type });

            // Add to our dict if not already tracked
            int playerIdx = GetPlayerIndex(tagState);
            var dict = MultiTagState.GetOrCreate(playerIdx);
            if (!dict.ContainsKey(type))
            {
                dict[type] = new WhipTagEntry(type, effect, _maxNPCs);
                if (effect != null)
                    _onSetToPlayer.Invoke(effect, new object[] { _ownerField.GetValue(tagState) });
            }
        }

        // === HARMONY PREFIXES ===

        /// <summary>
        /// TrySetActiveEffect(int type) — Don't clear old tags or remove old player buff.
        /// Add new whip to multi-tag dict, update vanilla fields for compat.
        /// </summary>
        public static bool TrySetActiveEffect_Prefix(object __instance, int type)
        {
            if (!Enabled) return true;
            try
            {
                SetActiveEffect(__instance, type);
            }
            catch (Exception ex)
            {
                _log?.Error($"TrySetActiveEffect error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// TryApplyTagToNPC(int itemType, NPC npc) — Tag NPC in the specific whip's entry.
        /// </summary>
        public static bool TryApplyTagToNPC_Prefix(object __instance, int itemType, object npc)
        {
            if (!Enabled) return true;
            try
            {
                var effect = GetEffect(itemType);
                if (effect == null) return true;

                bool canApply = (bool)_canApplyTagToNPC.Invoke(effect, new object[] { GetNpcTypeId(npc) });
                if (!canApply) return false;

                SetActiveEffect(__instance, itemType);

                int playerIdx = GetPlayerIndex(__instance);
                var entry = GetOrCreateEntry(playerIdx, itemType, effect);
                entry.TimeLeftOnNPC[GetNpcIndex(npc)] = GetTagDuration(effect);

                _onTagAppliedToNPC.Invoke(effect, new object[] { _ownerField.GetValue(__instance), npc });
            }
            catch (Exception ex)
            {
                _log?.Error($"TryApplyTagToNPC error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// ModifyHit — Apply tag damage from ALL active whips on this NPC.
        /// </summary>
        public static bool ModifyHit_Prefix(object __instance, object optionalProjectile, object npcHit,
            ref int damageDealt, ref bool crit)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);
                if (dict.Count == 0) return false;

                int npcIdx = GetNpcIndex(npcHit);
                var owner = _ownerField.GetValue(__instance);

                foreach (var entry in dict.Values)
                {
                    if (entry.TimeLeftOnNPC[npcIdx] <= 0 || entry.Effect == null) continue;

                    bool canRun = (bool)_canRunHitEffects.Invoke(entry.Effect,
                        new object[] { owner, optionalProjectile, npcHit });
                    if (!canRun) continue;

                    // ModifyTaggedHit (ref params via reflection args array)
                    var tagArgs = new object[] { owner, optionalProjectile, npcHit, damageDealt, crit };
                    _modifyTaggedHit.Invoke(entry.Effect, tagArgs);
                    damageDealt = (int)tagArgs[3];
                    crit = (bool)tagArgs[4];

                    if (entry.ProcTimeLeftOnNPC[npcIdx] > 0)
                    {
                        var procArgs = new object[] { owner, optionalProjectile, npcHit, damageDealt, crit };
                        _modifyProcHit.Invoke(entry.Effect, procArgs);
                        damageDealt = (int)procArgs[3];
                        crit = (bool)procArgs[4];
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"ModifyHit error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// OnHit — Fire hit effects from ALL active whips on this NPC.
        /// </summary>
        public static bool OnHit_Prefix(object __instance, object optionalProjectile, object npcHit, int calcDamage)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);
                if (dict.Count == 0) return false;

                int npcIdx = GetNpcIndex(npcHit);
                var owner = _ownerField.GetValue(__instance);

                foreach (var entry in dict.Values)
                {
                    if (entry.TimeLeftOnNPC[npcIdx] <= 0 || entry.Effect == null) continue;

                    bool canRun = (bool)_canRunHitEffects.Invoke(entry.Effect,
                        new object[] { owner, optionalProjectile, npcHit });
                    if (!canRun) continue;

                    _onTaggedHit.Invoke(entry.Effect,
                        new object[] { owner, optionalProjectile, npcHit, calcDamage });

                    if (entry.ProcTimeLeftOnNPC[npcIdx] > 0)
                    {
                        entry.ProcTimeLeftOnNPC[npcIdx] = 0; // clear proc before firing (matches vanilla)
                        _onProcHit.Invoke(entry.Effect,
                            new object[] { owner, optionalProjectile, npcHit, calcDamage });
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"OnHit error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update — Tick down ALL whip timers, remove expired entries.
        /// </summary>
        public static bool Update_Prefix(object __instance)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);
                if (dict.Count == 0) return false;

                _toRemove.Clear();
                foreach (var kvp in dict)
                {
                    var entry = kvp.Value;
                    for (int i = 0; i < entry.TimeLeftOnNPC.Length; i++)
                    {
                        if (entry.TimeLeftOnNPC[i] > 0) entry.TimeLeftOnNPC[i]--;
                    }
                    for (int i = 0; i < entry.ProcTimeLeftOnNPC.Length; i++)
                    {
                        if (entry.ProcTimeLeftOnNPC[i] > 0) entry.ProcTimeLeftOnNPC[i]--;
                    }

                    if (!entry.HasAnyActiveTags())
                        _toRemove.Add(kvp.Key);
                }

                for (int i = 0; i < _toRemove.Count; i++)
                    dict.Remove(_toRemove[i]);
            }
            catch (Exception ex)
            {
                _log?.Error($"Update error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// IsNPCTagged — True if ANY whip has active tag on this NPC.
        /// </summary>
        public static bool IsNPCTagged_Prefix(object __instance, int npcIndex, ref bool __result)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);

                __result = false;
                foreach (var entry in dict.Values)
                {
                    if (entry.TimeLeftOnNPC[npcIndex] > 0)
                    {
                        __result = true;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"IsNPCTagged error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// CanProcOnNPC — True if ANY whip has active proc on this NPC.
        /// </summary>
        public static bool CanProcOnNPC_Prefix(object __instance, int npcIndex, ref bool __result)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);

                __result = false;
                foreach (var entry in dict.Values)
                {
                    if (entry.ProcTimeLeftOnNPC[npcIndex] > 0)
                    {
                        __result = true;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"CanProcOnNPC error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// TryEnableProcOnNPC — Enable proc on the matching whip's entry.
        /// </summary>
        public static bool TryEnableProcOnNPC_Prefix(object __instance, int expectedActiveEffectType, object npc)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);

                if (dict.TryGetValue(expectedActiveEffectType, out var entry) && entry.Effect != null)
                {
                    int npcIdx = GetNpcIndex(npc);
                    entry.ProcTimeLeftOnNPC[npcIdx] = GetTagDuration(entry.Effect);
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"TryEnableProcOnNPC error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// ClearProcOnNPC — Clear proc for all whips on this NPC.
        /// </summary>
        public static bool ClearProcOnNPC_Prefix(object __instance, int npcIndex)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);

                foreach (var entry in dict.Values)
                    entry.ProcTimeLeftOnNPC[npcIndex] = 0;
            }
            catch (Exception ex)
            {
                _log?.Error($"ClearProcOnNPC error: {ex.Message}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// ResetNPCSlotData — Clear all whip data for this NPC index.
        /// </summary>
        public static bool ResetNPCSlotData_Prefix(object __instance, int npcIndex)
        {
            if (!Enabled) return true;
            try
            {
                int playerIdx = GetPlayerIndex(__instance);
                var dict = MultiTagState.GetOrCreate(playerIdx);

                foreach (var entry in dict.Values)
                {
                    entry.TimeLeftOnNPC[npcIndex] = 0;
                    entry.ProcTimeLeftOnNPC[npcIndex] = 0;
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"ResetNPCSlotData error: {ex.Message}");
                return true;
            }
            return false;
        }
    }
}
