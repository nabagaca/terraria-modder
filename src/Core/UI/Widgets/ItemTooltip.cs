using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.UI.Widgets
{
    /// <summary>
    /// Vanilla-style item tooltip widget. Shows full item stats like Terraria's native tooltips:
    /// rarity-colored name, damage+type, speed tier, knockback tier, defense, tool power,
    /// tooltip description lines, prefix modifications (green/red), and more.
    ///
    /// Usage: Call Set() during hover detection, DrawDeferred() renders at end of frame.
    /// Automatically integrated into Tooltip.Clear()/DrawDeferred() for DraggablePanel users.
    /// For manual draw loops (like StorageHub), call DrawDeferred() explicitly after Set().
    /// </summary>
    public static class ItemTooltip
    {
        // Pending state
        private static bool _hasTooltip;
        private static int _itemType;
        private static int _prefix;
        private static int _stack;
        private static string[] _extraLines;
        private static string _nameOverride;

        // Tooltip line model
        private struct TooltipLine
        {
            public string Text;
            public Color4 Color;
        }

        private static readonly List<TooltipLine> _lines = new List<TooltipLine>(24);

        // Prefix colors (matching vanilla)
        private static readonly Color4 PrefixBetter = new Color4(120, 190, 120);
        private static readonly Color4 PrefixWorse = new Color4(190, 120, 120);

        // Logging
        private static readonly ILogger _log = LogManager.GetLogger("ItemTooltip");

        // Reflection cache
        private static bool _reflectionInitialized;
        private static bool _reflectionValid;
        private static System.Collections.IDictionary _itemsByType;
        private static Type _itemClrType;

        // Item fields
        private static FieldInfo _fDamage, _fCrit, _fUseAnimation, _fUseStyle, _fKnockBack;
        private static FieldInfo _fDefense, _fPick, _fAxe, _fHammer, _fTileBoost;
        private static FieldInfo _fHealLife, _fHealMana, _fMana;
        private static FieldInfo _fConsumable, _fMaterial, _fVanity;
        private static FieldInfo _fAccessory, _fHeadSlot, _fBodySlot, _fLegSlot;
        private static FieldInfo _fCreateTile, _fCreateWall, _fAmmo, _fNotAmmo;
        private static FieldInfo _fBuffTime, _fMelee, _fRanged, _fMagic, _fSummon;
        private static FieldInfo _fRarity, _fBait, _fFishingPole, _fQuestItem;
        private static FieldInfo _fScale, _fShootSpeed;
        private static FieldInfo _fStack;

        // Item methods/properties
        private static MethodInfo _mSetDefaults;
        private static MethodInfo _mPrefixMethod;
        private static MethodInfo _mAffixName;
        private static FieldInfo _pToolTip;
        private static PropertyInfo _pToolTipLines;
        private static MethodInfo _mGetLine;

        // Lang.tip access
        private static Array _langTip;
        private static PropertyInfo _ltValueProp;

        /// <summary>
        /// Set an item tooltip to display. Call during hover detection.
        /// Extra lines are appended at the end (e.g. "In: Chest #3").
        /// </summary>
        public static void Set(int itemType, int prefix = 0, int stack = 1, params string[] extraLines)
        {
            if (itemType <= 0) return;
            _itemType = itemType;
            _prefix = prefix;
            _stack = stack;
            _extraLines = extraLines;
            _nameOverride = null;
            _hasTooltip = true;
        }

        /// <summary>
        /// Set an item tooltip with a custom display name (e.g. "Any Iron Bar" for recipe groups).
        /// Stats and tooltip lines come from the item type; only the title is overridden.
        /// </summary>
        public static void SetWithName(int itemType, string displayName, params string[] extraLines)
        {
            if (itemType <= 0) return;
            _itemType = itemType;
            _prefix = 0;
            _stack = 1;
            _extraLines = extraLines;
            _nameOverride = displayName;
            _hasTooltip = true;
        }

        /// <summary>
        /// Clear pending tooltip. Called automatically via Tooltip.Clear().
        /// </summary>
        public static void Clear()
        {
            _hasTooltip = false;
            _nameOverride = null;
        }

        /// <summary>
        /// Draw the item tooltip near the mouse cursor.
        /// Called automatically via Tooltip.DrawDeferred(), or manually for non-DraggablePanel UIs.
        /// </summary>
        public static void DrawDeferred()
        {
            if (!_hasTooltip) return;
            _hasTooltip = false;

            if (!InitReflection()) return;

            BuildLines();
            if (_lines.Count == 0) return;

            RenderLines();
        }

        #region Reflection Init

        private static bool InitReflection()
        {
            if (_reflectionInitialized) return _reflectionValid;
            _reflectionInitialized = true;

            try
            {
                // ContentSamples.ItemsByType
                var csType = Type.GetType("Terraria.ID.ContentSamples, Terraria");
                if (csType == null)
                {
                    var asm = Assembly.Load("Terraria");
                    csType = asm?.GetType("Terraria.ID.ContentSamples");
                }
                var ibtField = csType?.GetField("ItemsByType", BindingFlags.Public | BindingFlags.Static);
                _itemsByType = ibtField?.GetValue(null) as System.Collections.IDictionary;
                if (_itemsByType == null || _itemsByType.Count == 0) return false;

                // Get Item type from first entry
                foreach (System.Collections.DictionaryEntry e in _itemsByType)
                {
                    _itemClrType = e.Value.GetType();
                    break;
                }
                if (_itemClrType == null) return false;

                // Cache fields
                _fDamage = _itemClrType.GetField("damage");
                _fCrit = _itemClrType.GetField("crit");
                _fUseAnimation = _itemClrType.GetField("useAnimation");
                _fUseStyle = _itemClrType.GetField("useStyle");
                _fKnockBack = _itemClrType.GetField("knockBack");
                _fDefense = _itemClrType.GetField("defense");
                _fPick = _itemClrType.GetField("pick");
                _fAxe = _itemClrType.GetField("axe");
                _fHammer = _itemClrType.GetField("hammer");
                _fTileBoost = _itemClrType.GetField("tileBoost");
                _fHealLife = _itemClrType.GetField("healLife");
                _fHealMana = _itemClrType.GetField("healMana");
                _fMana = _itemClrType.GetField("mana");
                _fConsumable = _itemClrType.GetField("consumable");
                _fMaterial = _itemClrType.GetField("material");
                _fVanity = _itemClrType.GetField("vanity");
                _fAccessory = _itemClrType.GetField("accessory");
                _fHeadSlot = _itemClrType.GetField("headSlot");
                _fBodySlot = _itemClrType.GetField("bodySlot");
                _fLegSlot = _itemClrType.GetField("legSlot");
                _fCreateTile = _itemClrType.GetField("createTile");
                _fCreateWall = _itemClrType.GetField("createWall");
                _fAmmo = _itemClrType.GetField("ammo");
                _fNotAmmo = _itemClrType.GetField("notAmmo");
                _fBuffTime = _itemClrType.GetField("buffTime");
                _fMelee = _itemClrType.GetField("melee");
                _fRanged = _itemClrType.GetField("ranged");
                _fMagic = _itemClrType.GetField("magic");
                _fSummon = _itemClrType.GetField("summon");
                _fRarity = _itemClrType.GetField("rare");
                _fBait = _itemClrType.GetField("bait");
                _fFishingPole = _itemClrType.GetField("fishingPole");
                _fQuestItem = _itemClrType.GetField("questItem");
                _fScale = _itemClrType.GetField("scale");
                _fShootSpeed = _itemClrType.GetField("shootSpeed");
                _fStack = _itemClrType.GetField("stack");

                // Methods — SetDefaults has signature (int, ItemVariant = null), so use binder overload
                _mSetDefaults = _itemClrType.GetMethod("SetDefaults",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);
                if (_mSetDefaults == null)
                {
                    // Fallback: find first SetDefaults with int as first param
                    _mSetDefaults = _itemClrType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "SetDefaults"
                            && m.GetParameters().Length >= 1
                            && m.GetParameters()[0].ParameterType == typeof(int));
                }
                _mPrefixMethod = _itemClrType.GetMethod("Prefix", new[] { typeof(int) });
                _mAffixName = _itemClrType.GetMethod("AffixName", Type.EmptyTypes);

                // ToolTip is a public field (not a property) on Item
                _pToolTip = _itemClrType.GetField("ToolTip", BindingFlags.Public | BindingFlags.Instance);

                // ItemTooltip type (for Lines/GetLine)
                if (_pToolTip != null)
                {
                    var tooltipType = _pToolTip.FieldType;
                    _pToolTipLines = tooltipType.GetProperty("Lines");
                    _mGetLine = tooltipType.GetMethod("GetLine", new[] { typeof(int) });
                }

                // Lang.tip
                var langType = Type.GetType("Terraria.Lang, Terraria");
                if (langType == null)
                {
                    var asm = Assembly.Load("Terraria");
                    langType = asm?.GetType("Terraria.Lang");
                }
                var tipField = langType?.GetField("tip", BindingFlags.Public | BindingFlags.Static);
                _langTip = tipField?.GetValue(null) as Array;

                var ltType = Type.GetType("Terraria.Localization.LocalizedText, Terraria");
                if (ltType == null)
                {
                    var asm = Assembly.Load("Terraria");
                    ltType = asm?.GetType("Terraria.Localization.LocalizedText");
                }
                _ltValueProp = ltType?.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

                _reflectionValid = _fDamage != null && _mSetDefaults != null;
                _log.Info($"[ItemTooltip] Reflection init: valid={_reflectionValid}, " +
                    $"itemsByType={_itemsByType?.Count ?? -1}, itemType={_itemClrType?.Name}, " +
                    $"damage={_fDamage != null}, setDefaults={_mSetDefaults != null}, " +
                    $"prefix={_mPrefixMethod != null}, affixName={_mAffixName != null}, " +
                    $"langTip={_langTip?.Length ?? -1}, toolTip={_pToolTip != null}");
                return _reflectionValid;
            }
            catch (Exception ex)
            {
                _log.Error($"[ItemTooltip] Reflection init failed: {ex.Message}");
                _reflectionValid = false;
                return false;
            }
        }

        #endregion

        #region Line Building

        private static void BuildLines()
        {
            _lines.Clear();

            if (!_itemsByType.Contains(_itemType)) return;

            // Get base item from ContentSamples
            object baseItem = _itemsByType[_itemType];

            // If prefix specified, create a temp prefixed item for display
            object displayItem = baseItem;
            if (_prefix > 0)
            {
                try
                {
                    displayItem = Activator.CreateInstance(_itemClrType);
                    // SetDefaults may have 1 or 2 params (int Type, ItemVariant variant = null)
                    var sdParams = _mSetDefaults.GetParameters();
                    if (sdParams.Length == 1)
                        _mSetDefaults.Invoke(displayItem, new object[] { _itemType });
                    else
                        _mSetDefaults.Invoke(displayItem, new object[] { _itemType, null });
                    _mPrefixMethod.Invoke(displayItem, new object[] { (int)_prefix });
                }
                catch (Exception ex)
                {
                    _log.Debug($"[ItemTooltip] Prefix item creation failed: {ex.Message}");
                    displayItem = baseItem;
                }
            }

            // Read fields from display item (prefixed if applicable)
            int damage = GetInt(_fDamage, displayItem);
            int crit = GetInt(_fCrit, displayItem);
            int useAnimation = GetInt(_fUseAnimation, displayItem);
            int useStyle = GetInt(_fUseStyle, displayItem);
            float knockBack = GetFloat(_fKnockBack, displayItem);
            int defense = GetInt(_fDefense, displayItem);
            int pick = GetInt(_fPick, displayItem);
            int axe = GetInt(_fAxe, displayItem);
            int hammer = GetInt(_fHammer, displayItem);
            int tileBoost = GetInt(_fTileBoost, displayItem);
            int healLife = GetInt(_fHealLife, displayItem);
            int healMana = GetInt(_fHealMana, displayItem);
            int mana = GetInt(_fMana, displayItem);
            bool consumable = GetBool(_fConsumable, displayItem);
            bool material = GetBool(_fMaterial, displayItem);
            bool vanity = GetBool(_fVanity, displayItem);
            bool accessory = GetBool(_fAccessory, displayItem);
            int headSlot = GetInt(_fHeadSlot, displayItem);
            int bodySlot = GetInt(_fBodySlot, displayItem);
            int legSlot = GetInt(_fLegSlot, displayItem);
            int createTile = GetInt(_fCreateTile, displayItem);
            int createWall = GetInt(_fCreateWall, displayItem);
            int ammo = GetInt(_fAmmo, displayItem);
            bool notAmmo = GetBool(_fNotAmmo, displayItem);
            int buffTime = GetInt(_fBuffTime, displayItem);
            bool melee = GetBool(_fMelee, displayItem);
            bool ranged = GetBool(_fRanged, displayItem);
            bool magic = GetBool(_fMagic, displayItem);
            bool summon = GetBool(_fSummon, displayItem);
            int rarity = GetInt(_fRarity, displayItem);
            int bait = GetInt(_fBait, displayItem);
            int fishingPole = GetInt(_fFishingPole, displayItem);
            bool questItem = GetBool(_fQuestItem, displayItem);

            // === Line 0: Name (rarity colored) ===
            string name;
            if (_nameOverride != null)
            {
                name = _nameOverride;
            }
            else
            {
                try
                {
                    name = _mAffixName.Invoke(displayItem, null) as string ?? "???";
                }
                catch
                {
                    name = "???";
                }
            }
            if (_stack > 1) name += " (" + _stack + ")";
            _lines.Add(new TooltipLine { Text = name, Color = UIColors.GetRarityColor(rarity) });

            // === Damage + type ===
            if (damage > 0 && (!notAmmo || useStyle != 0))
            {
                string damageType;
                if (melee) damageType = GetTip(2);
                else if (ranged) damageType = GetTip(3);
                else if (magic) damageType = GetTip(4);
                else if (summon) damageType = GetTip(53);
                else damageType = GetTip(55);

                AddLine(damage + damageType, UIColors.Text);

                // Crit chance
                if (melee || ranged || magic)
                    AddLine(crit + GetTip(5), UIColors.Text);

                // Speed tier
                if (useStyle != 0 && !summon)
                {
                    if (useAnimation <= 8) AddLine(GetTip(6), UIColors.Text);
                    else if (useAnimation <= 20) AddLine(GetTip(7), UIColors.Text);
                    else if (useAnimation <= 25) AddLine(GetTip(8), UIColors.Text);
                    else if (useAnimation <= 30) AddLine(GetTip(9), UIColors.Text);
                    else if (useAnimation <= 35) AddLine(GetTip(10), UIColors.Text);
                    else if (useAnimation <= 45) AddLine(GetTip(11), UIColors.Text);
                    else if (useAnimation <= 55) AddLine(GetTip(12), UIColors.Text);
                    else AddLine(GetTip(13), UIColors.Text);
                }

                // Knockback tier
                if (knockBack == 0f) AddLine(GetTip(14), UIColors.Text);
                else if (knockBack <= 1.5f) AddLine(GetTip(15), UIColors.Text);
                else if (knockBack <= 3f) AddLine(GetTip(16), UIColors.Text);
                else if (knockBack <= 4f) AddLine(GetTip(17), UIColors.Text);
                else if (knockBack <= 6f) AddLine(GetTip(18), UIColors.Text);
                else if (knockBack <= 7f) AddLine(GetTip(19), UIColors.Text);
                else if (knockBack <= 9f) AddLine(GetTip(20), UIColors.Text);
                else if (knockBack <= 11f) AddLine(GetTip(21), UIColors.Text);
                else AddLine(GetTip(22), UIColors.Text);
            }

            // === Fishing / bait ===
            if (fishingPole > 0)
                AddLine(fishingPole + "% fishing power", UIColors.Text);
            if (bait > 0)
                AddLine(bait + "% bait power", UIColors.Text);

            // === Equippable ===
            if (accessory || headSlot > 0 || bodySlot > 0 || legSlot > 0)
                AddLine(GetTip(23), UIColors.Text);

            // === Quest item ===
            if (questItem)
                AddLine("Quest Item", UIColors.Warning);

            // === Vanity ===
            if (vanity)
                AddLine(GetTip(24), UIColors.Text);

            // === Defense ===
            if (defense > 0)
                AddLine(defense + GetTip(25), UIColors.Text);

            // === Tool power ===
            if (pick > 0) AddLine(pick + GetTip(26), UIColors.Text);
            if (axe > 0) AddLine((axe * 5) + GetTip(27), UIColors.Text);
            if (hammer > 0) AddLine(hammer + GetTip(28), UIColors.Text);

            // === Tile range boost ===
            if (tileBoost != 0)
            {
                string sign = tileBoost > 0 ? "+" : "";
                AddLine(sign + tileBoost + GetTip(54), UIColors.Text);
            }

            // === Heal life/mana ===
            if (healLife > 0)
                AddLine("Restores " + healLife + " life", UIColors.Text);
            if (healMana > 0)
                AddLine("Restores " + healMana + " mana", UIColors.Text);

            // === Mana cost ===
            if (mana > 0)
                AddLine("Uses " + mana + " mana", UIColors.Text);

            // === Placeable / Ammo / Consumable ===
            if (createWall > 0 || createTile > -1)
            {
                if (consumable)
                    AddLine(GetTip(33), UIColors.Text);
            }
            else if (ammo > 0 && !notAmmo)
            {
                AddLine(GetTip(34), UIColors.Text);
            }
            else if (consumable)
            {
                AddLine(GetTip(35), UIColors.Text);
            }

            // === Material ===
            if (material)
                AddLine(GetTip(36), UIColors.Text);

            // === Tooltip description lines ===
            if (_pToolTip != null)
            {
                try
                {
                    var tooltip = _pToolTip.GetValue(displayItem);
                    if (tooltip != null && _pToolTipLines != null && _mGetLine != null)
                    {
                        int lineCount = (int)_pToolTipLines.GetValue(tooltip);
                        for (int i = 0; i < lineCount; i++)
                        {
                            string line = _mGetLine.Invoke(tooltip, new object[] { i }) as string;
                            if (!string.IsNullOrEmpty(line))
                                AddLine(line, UIColors.Text);
                        }
                    }
                }
                catch { }
            }

            // === Buff duration ===
            if (buffTime > 0)
            {
                int seconds = buffTime / 60;
                if (seconds < 60)
                    AddLine(Math.Round((double)buffTime / 60.0) + " second duration", UIColors.Text);
                else
                    AddLine(Math.Round((double)seconds / 60.0) + " minute duration", UIColors.Text);
            }

            // === Prefix modifications ===
            if (_prefix > 0 && displayItem != baseItem)
                BuildPrefixLines(baseItem, displayItem);

            // === Extra context lines from caller ===
            if (_extraLines != null)
            {
                for (int i = 0; i < _extraLines.Length; i++)
                {
                    if (!string.IsNullOrEmpty(_extraLines[i]))
                        AddLine(_extraLines[i], UIColors.Success);
                }
            }
        }

        private static void BuildPrefixLines(object baseItem, object prefixedItem)
        {
            // Damage
            int baseDmg = GetInt(_fDamage, baseItem);
            int prefDmg = GetInt(_fDamage, prefixedItem);
            if (baseDmg > 0 && prefDmg != baseDmg)
            {
                float pct = ((float)(prefDmg - baseDmg) / baseDmg) * 100f;
                bool better = pct > 0;
                AddLine((better ? "+" : "") + (int)Math.Round(pct) + GetTip(39), better ? PrefixBetter : PrefixWorse);
            }

            // Speed (useAnimation — lower is faster, so invert)
            int baseSpd = GetInt(_fUseAnimation, baseItem);
            int prefSpd = GetInt(_fUseAnimation, prefixedItem);
            if (baseSpd > 0 && prefSpd != baseSpd)
            {
                float pct = ((float)(baseSpd - prefSpd) / baseSpd) * 100f; // inverted
                bool better = pct > 0;
                AddLine((better ? "+" : "") + (int)Math.Round(pct) + GetTip(40), better ? PrefixBetter : PrefixWorse);
            }

            // Crit
            int baseCrit = GetInt(_fCrit, baseItem);
            int prefCrit = GetInt(_fCrit, prefixedItem);
            if (prefCrit != baseCrit)
            {
                int diff = prefCrit - baseCrit;
                bool better = diff > 0;
                AddLine((better ? "+" : "") + diff + GetTip(41), better ? PrefixBetter : PrefixWorse);
            }

            // Mana (lower is better, so invert display)
            int baseMana = GetInt(_fMana, baseItem);
            int prefMana = GetInt(_fMana, prefixedItem);
            if (baseMana > 0 && prefMana != baseMana)
            {
                float pct = ((float)(prefMana - baseMana) / baseMana) * 100f;
                bool better = pct < 0; // less mana = better
                AddLine((pct > 0 ? "+" : "") + (int)Math.Round(pct) + GetTip(42), better ? PrefixBetter : PrefixWorse);
            }

            // Size (scale)
            float baseScale = GetFloat(_fScale, baseItem);
            float prefScale = GetFloat(_fScale, prefixedItem);
            if (baseScale > 0f && Math.Abs(prefScale - baseScale) > 0.001f)
            {
                float pct = ((prefScale - baseScale) / baseScale) * 100f;
                bool better = pct > 0;
                AddLine((better ? "+" : "") + (int)Math.Round(pct) + GetTip(43), better ? PrefixBetter : PrefixWorse);
            }

            // Shoot speed (velocity)
            float baseVel = GetFloat(_fShootSpeed, baseItem);
            float prefVel = GetFloat(_fShootSpeed, prefixedItem);
            if (baseVel > 0f && Math.Abs(prefVel - baseVel) > 0.001f)
            {
                float pct = ((prefVel - baseVel) / baseVel) * 100f;
                bool better = pct > 0;
                AddLine((better ? "+" : "") + (int)Math.Round(pct) + GetTip(44), better ? PrefixBetter : PrefixWorse);
            }

            // Knockback
            float baseKb = GetFloat(_fKnockBack, baseItem);
            float prefKb = GetFloat(_fKnockBack, prefixedItem);
            if (baseKb > 0f && Math.Abs(prefKb - baseKb) > 0.001f)
            {
                float pct = ((prefKb - baseKb) / baseKb) * 100f;
                bool better = pct > 0;
                AddLine((better ? "+" : "") + (int)Math.Round(pct) + GetTip(45), better ? PrefixBetter : PrefixWorse);
            }
        }

        #endregion

        #region Rendering

        private static void RenderLines()
        {
            int maxWidth = 350;
            int lineHeight = 16;
            int padding = 8;
            int maxContentWidth = maxWidth - padding * 2;

            // Build wrapped lines with colors
            var wrappedLines = new List<TooltipLine>();
            foreach (var line in _lines)
            {
                if (string.IsNullOrEmpty(line.Text))
                {
                    wrappedLines.Add(line);
                    continue;
                }

                if (TextUtil.MeasureWidth(line.Text) <= maxContentWidth)
                {
                    wrappedLines.Add(line);
                    continue;
                }

                // Word-wrap
                string remaining = line.Text;
                while (TextUtil.MeasureWidth(remaining) > maxContentWidth)
                {
                    int breakAt = -1;
                    for (int i = remaining.Length - 1; i > 0; i--)
                    {
                        if (remaining[i] == ' ' && TextUtil.MeasureWidth(remaining.Substring(0, i)) <= maxContentWidth)
                        {
                            breakAt = i;
                            break;
                        }
                    }
                    if (breakAt <= 0)
                    {
                        breakAt = remaining.Length;
                        for (int i = 1; i < remaining.Length; i++)
                        {
                            if (TextUtil.MeasureWidth(remaining.Substring(0, i)) > maxContentWidth)
                            {
                                breakAt = Math.Max(1, i - 1);
                                break;
                            }
                        }
                    }
                    wrappedLines.Add(new TooltipLine { Text = remaining.Substring(0, breakAt), Color = line.Color });
                    remaining = remaining.Substring(breakAt).TrimStart();
                }
                if (remaining.Length > 0)
                    wrappedLines.Add(new TooltipLine { Text = remaining, Color = line.Color });
            }

            // Calculate dimensions
            int tooltipWidth = 0;
            foreach (var line in wrappedLines)
                tooltipWidth = Math.Max(tooltipWidth, TextUtil.MeasureWidth(line.Text));
            tooltipWidth = Math.Min(tooltipWidth + padding * 2, maxWidth);

            int tooltipHeight = wrappedLines.Count * lineHeight + padding * 2;

            // Position near mouse, clamped to screen
            int tx = WidgetInput.MouseX + 16;
            int ty = WidgetInput.MouseY + 16;

            if (tx + tooltipWidth > WidgetInput.ScreenWidth - 4)
                tx = WidgetInput.MouseX - tooltipWidth - 4;
            if (ty + tooltipHeight > WidgetInput.ScreenHeight - 4)
                ty = WidgetInput.MouseY - tooltipHeight - 4;

            tx = Math.Max(4, tx);
            ty = Math.Max(4, ty);

            // Draw background
            UIRenderer.DrawRect(tx, ty, tooltipWidth, tooltipHeight, UIColors.TooltipBg);
            UIRenderer.DrawRectOutline(tx, ty, tooltipWidth, tooltipHeight, UIColors.Border);

            // Draw lines
            int ly = ty + padding;
            for (int i = 0; i < wrappedLines.Count; i++)
            {
                UIRenderer.DrawText(wrappedLines[i].Text, tx + padding, ly, wrappedLines[i].Color);
                ly += lineHeight;
            }
        }

        #endregion

        #region Helpers

        private static void AddLine(string text, Color4 color)
        {
            if (!string.IsNullOrEmpty(text))
                _lines.Add(new TooltipLine { Text = text, Color = color });
        }

        private static int GetInt(FieldInfo field, object obj)
        {
            if (field == null) return 0;
            try
            {
                var val = field.GetValue(obj);
                return val != null ? (int)val : 0;
            }
            catch { return 0; }
        }

        private static float GetFloat(FieldInfo field, object obj)
        {
            if (field == null) return 0f;
            try
            {
                var val = field.GetValue(obj);
                return val != null ? (float)val : 0f;
            }
            catch { return 0f; }
        }

        private static bool GetBool(FieldInfo field, object obj)
        {
            if (field == null) return false;
            try
            {
                var val = field.GetValue(obj);
                return val != null && (bool)val;
            }
            catch { return false; }
        }

        private static string GetTip(int index)
        {
            if (_langTip == null || index < 0 || index >= _langTip.Length)
                return "";
            try
            {
                var obj = _langTip.GetValue(index);
                if (obj == null) return "";
                return _ltValueProp?.GetValue(obj) as string ?? "";
            }
            catch { return ""; }
        }

        #endregion
    }
}
