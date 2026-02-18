---
title: AdminPanel Mod - God Mode and Cheats for Terraria 1.4.5
description: Walkthrough of the AdminPanel mod for Terraria 1.4.5. God mode, speed boost, teleport, time control, and respawn settings with custom UI sliders.
parent: Walkthroughs
nav_order: 6
---

# AdminPanel Walkthrough

**Difficulty:** Advanced
**Concepts:** Custom UI with sliders, Harmony patching, reflection, boss detection

AdminPanel provides a draggable UI with admin/cheat features: god mode, movement speed, health/mana restore, time controls, teleportation, and custom respawn times.

## What It Does

Press Backslash (`\`) to open a panel with:
- **God Mode** toggle (also available via F9 hotkey)
- **Full Health/Mana** instant restore
- **Movement Speed** - 1x to 10x speed slider
- **Time Controls** - Dawn, Noon, Dusk, Night presets + speed slider (1x-60x)
- **Teleport** - Spawn, Dungeon, Hell, Beach, Bed, Random
- **Respawn Time** - Separate sliders for normal vs boss deaths

## Key Concepts

### 1. God Mode with Harmony Patching

God mode requires a Harmony patch because the game resets immunity flags every frame:

```csharp
private static bool _godModeActive;
private static FieldInfo _immuneField;
private static FieldInfo _immuneTimeField;
private static FieldInfo _immuneNoBlink;

public static void OnGameReady()
{
    // Patch Player.ResetEffects to maintain immunity
    var resetEffectsMethod = _playerType.GetMethod("ResetEffects",
        BindingFlags.Public | BindingFlags.Instance);

    _harmony.Patch(resetEffectsMethod,
        postfix: new HarmonyMethod(typeof(Mod), nameof(ResetEffects_Postfix)));
}

private static void ResetEffects_Postfix(object __instance)
{
    if (!_godModeActive) return;

    var localPlayer = GetLocalPlayer();
    if (__instance == localPlayer)
    {
        _immuneField?.SetValue(__instance, true);
        _immuneTimeField?.SetValue(__instance, 2);
        _immuneNoBlink?.SetValue(__instance, true);
    }
}
```

**Key insight:** Terraria's `Player.ResetEffects()` clears immunity every frame. A postfix patch re-applies it for god mode to work.

### 2. Boss Detection Matching Vanilla

Custom respawn times need to know if the player died during a boss fight:

```csharp
private static bool DetectBossFight(object player)
{
    var playerCenter = _playerCenterProp.GetValue(player);
    float playerX = (float)_vector2XField.GetValue(playerCenter);
    float playerY = (float)_vector2YField.GetValue(playerCenter);

    var npcs = (Array)_npcArrayField.GetValue(null);
    for (int i = 0; i < Math.Min(npcs.Length, 200); i++)
    {
        var npc = npcs.GetValue(i);
        if (npc == null) continue;

        bool active = (bool)_npcActiveField.GetValue(npc);
        if (!active) continue;

        bool isBoss = (bool)_npcBossField.GetValue(npc);
        int npcTypeId = (int)_npcTypeField.GetValue(npc);

        // Boss OR Eater of Worlds segments (13,14,15), exclude Martian Saucer (395)
        if ((isBoss || npcTypeId == 13 || npcTypeId == 14 || npcTypeId == 15)
            && npcTypeId != 395)
        {
            var npcCenter = _npcCenterProp.GetValue(npc);
            float npcX = (float)_vector2XField.GetValue(npcCenter);
            float npcY = (float)_vector2YField.GetValue(npcCenter);

            // Vanilla uses Manhattan distance < 4000 pixels
            if (Math.Abs(playerX - npcX) + Math.Abs(playerY - npcY) < 4000f)
                return true;
        }
    }
    return false;
}
```

**Vanilla logic:** The game considers you "in a boss fight" if a boss NPC is within 4000 pixels (Manhattan distance). Eater of Worlds segments (types 13, 14, 15) count as bosses. Martian Saucer (395) is excluded.

### 3. Custom Respawn Times

Speed up respawn by reducing the timer in `Player.UpdateDead`:

```csharp
private static readonly int[] NormalRespawnSeconds = { 1, 2, 3, 5, 10, 15, 20, 30, 45 };
private static readonly int[] BossRespawnSeconds = { 2, 5, 7, 10, 20, 30, 45, 60, 90 };
private static float _normalRespawnMult = 1.0f;
private static float _bossRespawnMult = 1.0f;

private static void UpdateDead_Postfix(object __instance)
{
    var localPlayer = GetLocalPlayer();
    if (__instance != localPlayer) return;

    _inBossFight = DetectBossFight(__instance);
    float mult = _inBossFight ? _bossRespawnMult : _normalRespawnMult;

    if (mult >= 1.0f) return; // No speed-up needed

    int currentTimer = (int)_respawnTimerField.GetValue(__instance);
    if (currentTimer > 0)
    {
        // mult=0.5 means 2x speed, mult=0.25 means 4x speed
        int extraReduction = (int)((1.0f / mult) - 1);
        if (extraReduction > 0)
        {
            _respawnTimerField.SetValue(__instance,
                Math.Max(0, currentTimer - extraReduction));
        }
    }
}
```

### 4. Time Speed Control

Time speed uses a Harmony **postfix** on `Main.UpdateTimeRate` to multiply `dayRate` after vanilla sets it. Direct field setting doesn't work because vanilla overwrites `dayRate` every frame:

```csharp
private static int _timeSpeedMultiplier = 1;
private static FieldInfo _dayRateField;

// Patch Main.UpdateTimeRate - multiply AFTER vanilla sets dayRate
var updateTimeRateMethod = _mainType.GetMethod("UpdateTimeRate",
    BindingFlags.Public | BindingFlags.Static);
_harmony.Patch(updateTimeRateMethod,
    postfix: new HarmonyMethod(typeof(Mod), nameof(UpdateTimeRate_Postfix)));

private static void UpdateTimeRate_Postfix()
{
    if (_timeSpeedMultiplier <= 1) return;

    int current = (int)_dayRateField.GetValue(null);
    if (current > 0)  // Don't multiply if frozen (dayRate=0)
    {
        _dayRateField.SetValue(null, current * _timeSpeedMultiplier);
    }
}
```

**Key insight:** Setting `Main.dayRate` directly gets overwritten by `Main.UpdateTimeRate()` every frame. The postfix approach multiplies whatever vanilla calculated, preserving events/bosses that freeze time (dayRate=0).

Time presets set `Main.dayTime` and `Main.time` directly:

| Button | dayTime | time |
|--------|---------|------|
| Dawn | true | 0 |
| Noon | true | 27000 |
| Dusk | false | 0 |
| Night | false | 16200 |

### 5. Movement Speed via Harmony Prefix

Movement speed uses a Harmony **prefix** on `Player.HorizontalMovement` to multiply `maxRunSpeed` and `runAcceleration`:

```csharp
private static int _moveSpeedMultiplier = 1;
private static FieldInfo _maxRunSpeedField;
private static FieldInfo _runAccelerationField;

// Patch Player.HorizontalMovement
var horizontalMovementMethod = _playerType.GetMethod("HorizontalMovement",
    BindingFlags.Public | BindingFlags.Instance);
_harmony.Patch(horizontalMovementMethod,
    prefix: new HarmonyMethod(typeof(Mod), nameof(HorizontalMovement_Prefix)));

private static void HorizontalMovement_Prefix(object __instance)
{
    if (_moveSpeedMultiplier <= 1) return;

    var localPlayer = GetLocalPlayer();
    if (__instance != localPlayer) return;

    float maxRun = (float)_maxRunSpeedField.GetValue(__instance);
    float runAccel = (float)_runAccelerationField.GetValue(__instance);
    _maxRunSpeedField.SetValue(__instance, maxRun * _moveSpeedMultiplier);
    _runAccelerationField.SetValue(__instance, runAccel * _moveSpeedMultiplier);
}
```

**Key insight:** A **prefix** on HorizontalMovement runs after all equipment/buff effects have been applied (in UpdateEquips) but before the movement calculation. This multiplies the final effective speed rather than a base value.

### 6. UI Sliders with Widget Library

The `Slider` widget from the Widget Library replaces manual drag state tracking:

```csharp
using TerrariaModder.Core.UI.Widgets;

private Slider _timeSlider = new Slider();
private Slider _normalRespawnSlider = new Slider();
private Slider _bossRespawnSlider = new Slider();
private Slider _moveSpeedSlider = new Slider();

// In draw callback:
int newSpeed = _timeSlider.Draw(x, y, width, 22, _timeSpeedMultiplier, 1, 60);
```

The `Slider` handles drag tracking, click-to-seek, thumb hover states, and bounds clamping internally. Each `Slider` instance maintains its own drag state, so use separate instances for independent sliders.

The panel itself uses `DraggablePanel` and `StackLayout` with `ButtonAt` for inline button rows:

```csharp
private DraggablePanel _panel = new DraggablePanel("admin-panel", "Admin Panel", 380, 560);

private void DrawPanel()
{
    if (!_panel.BeginDraw()) return;

    var s = new StackLayout(_panel.ContentX, _panel.ContentY, _panel.ContentWidth, spacing: 4);
    int hw = (s.Width - 8) / 2;

    s.SectionHeader("PLAYER");
    if (s.Toggle("God Mode", _godModeActive)) OnToggleGodMode();
    if (s.ButtonAt(s.X, hw, "Full Health")) RestoreHealth();
    if (s.ButtonAt(s.X + hw + 8, hw, "Full Mana")) RestoreMana();
    s.Advance(26);

    s.SectionHeader("MOVEMENT");
    int sy = s.Advance(22);
    _moveSpeedMultiplier = _moveSpeedSlider.Draw(s.X + 50, sy, s.Width - 150, 22,
        _moveSpeedMultiplier, 1, 10);

    s.SectionHeader("TIME");
    int qw = (s.Width - 24) / 4;
    if (s.ButtonAt(s.X, qw, "Dawn")) SetTime(true, 0);
    if (s.ButtonAt(s.X + qw + 8, qw, "Noon")) SetTime(true, 27000);
    if (s.ButtonAt(s.X + (qw + 8) * 2, qw, "Dusk")) SetTime(false, 0);
    if (s.ButtonAt(s.X + (qw + 8) * 3, qw, "Night")) SetTime(false, 16200);
    s.Advance(26);

    // ... teleport, respawn sections follow same pattern

    _panel.EndDraw();
}
```

Note the use of `ButtonAt(x, width, label)` for placing multiple buttons on one row; standard `Button()` takes the full layout width.

### 7. Teleportation via Reflection

Find the `Player.Teleport` method and invoke it:

```csharp
private static MethodInfo _teleportMethod;
private static Type _vector2Type;

// During init, find the method
foreach (var method in _playerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
{
    if (method.Name == "Teleport")
    {
        var parms = method.GetParameters();
        if (parms.Length >= 1 && parms[0].ParameterType.Name == "Vector2")
        {
            _teleportMethod = method;
            _vector2Type = parms[0].ParameterType;
            break;
        }
    }
}

// Invoke with world coordinates (tiles * 16)
private void TeleportPlayer(float worldX, float worldY)
{
    var player = GetLocalPlayer();
    var pos = Activator.CreateInstance(_vector2Type, new object[] { worldX, worldY });

    var parms = _teleportMethod.GetParameters();
    if (parms.Length == 1)
        _teleportMethod.Invoke(player, new[] { pos });
    else if (parms.Length == 2)
        _teleportMethod.Invoke(player, new object[] { pos, 1 });
    else if (parms.Length >= 3)
        _teleportMethod.Invoke(player, new object[] { pos, 1, 0 });
}
```

Six teleport destinations: Spawn (world spawn point), Dungeon, Hell (bottom of world), Beach (ocean edge), Bed (player's bed/spawn point), and **Random** (invokes `TeleportationPotion()` for a random position).

### 8. Settings Persistence

AdminPanel auto-saves settings to config using dirty detection, only writing when a value actually changes:

```csharp
private int _prevTimeSpeed = 1;

private void SaveSettingIfChanged<T>(string key, T current, ref T previous) where T : IEquatable<T>
{
    if (!current.Equals(previous))
    {
        _context.Config.Set(key, current);
        previous = current;
    }
}
```

Settings persisted: `godMode`, `timeSpeed`, `normalRespawnIndex`, `bossRespawnIndex`, `moveSpeed`. All restored on `Initialize` via `LoadSettings()`.

### 9. Static vs Instance State

Harmony patch methods must be static, but UI code uses instance state:

```csharp
#region Instance State
private ILogger _log;
private bool _uiOpen;
private int _panelX = -1;
#endregion

#region Static State (for Harmony patches)
private static bool _godModeActive;
private static int _timeSpeedMultiplier = 1;
private static Harmony _harmony;
#endregion
```

Static state must be reset on world unload:

```csharp
private void ResetState()
{
    _godModeActive = false;
    _timeSpeedMultiplier = 1;
    _normalRespawnMult = 1.0f;
    _bossRespawnMult = 1.0f;
}
```

## Code Structure Overview

```csharp
public class Mod : IMod
{
    #region Constants
    private static readonly int[] NormalRespawnSeconds = { 1, 2, 3, 5, 10, 15, 20, 30, 45 };
    #endregion

    #region UI (Widget Library)
    private DraggablePanel _panel;
    private Slider _timeSlider = new Slider();
    private Slider _normalRespawnSlider = new Slider();
    private Slider _bossRespawnSlider = new Slider();
    private Slider _moveSpeedSlider = new Slider();
    #endregion

    #region Static State (for Harmony patches)
    private static bool _godModeActive;
    #endregion

    #region Reflection Cache
    private static Type _mainType;
    private static FieldInfo _immuneField;
    #endregion

    #region IMod Implementation
    public void Initialize(ModContext context)
    {
        _panel = new DraggablePanel("admin-panel", "Admin Panel", 380, 560);
        _panel.RegisterDrawCallback(DrawPanel);
        // ...
    }
    public void Unload()
    {
        _panel?.UnregisterDrawCallback();
        _harmony?.UnpatchAll("...");
    }
    #endregion

    #region Harmony Patches
    private static void ResetEffects_Postfix(object __instance) { /* god mode */ }
    private static void UpdateDead_Postfix(object __instance) { /* respawn time */ }
    private static void UpdateTimeRate_Postfix() { /* time speed multiplier */ }
    private static void HorizontalMovement_Prefix(object __instance) { /* move speed */ }
    #endregion

    #region UI Drawing (uses StackLayout + Slider widgets)
    private void DrawPanel() { /* ... */ }
    #endregion

    #region Game Actions
    private void TeleportPlayer(float worldX, float worldY) { /* ... */ }
    #endregion
}
```

## Lessons Learned

1. **Use Widget Library for UI** - `DraggablePanel` + `StackLayout` + `Slider` replaces manual panel, drag, and slider code
2. **Postfix for state the game resets** - ResetEffects clears immunity, UpdateTimeRate overwrites dayRate; postfix re-applies
3. **Prefix for pre-calculation injection** - HorizontalMovement prefix multiplies speed before movement math runs
4. **Match vanilla logic exactly** - Boss detection uses specific NPC types and distance checks
5. **Separate static and instance state** - Harmony patches need static; UI needs instance
6. **Persist settings with dirty detection** - Only write config when value actually changes
7. **Teleport uses world coords** - Multiply tile coordinates by 16
8. **Handle method overloads** - `Player.Teleport` has multiple signatures
9. **Clean up on unload** - Call `UnregisterDrawCallback()` and reset game state

For more on the Widget Library, see [Core API Reference - Widget Library](../core-api-reference#widget-library).
For Harmony patching patterns, see [Harmony Basics](../harmony-basics).
