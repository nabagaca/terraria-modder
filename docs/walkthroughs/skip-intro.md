---
title: SkipIntro Mod - Terraria 1.4.5 Harmony Patch Example
description: Walkthrough of a simple Terraria 1.4.5 mod that skips the ReLogic splash screen. Learn Harmony patching and lifecycle hooks for beginners.
parent: Walkthroughs
nav_order: 1
---

# SkipIntro Walkthrough

**Difficulty:** Beginner
**Concepts:** Harmony patching, lifecycle hooks, static fields

SkipIntro is the simplest mod in the framework. It skips the ReLogic splash screen on game startup.

## What It Does

When Terraria starts, it shows the ReLogic logo for several seconds. This mod detects when assets have loaded and immediately skips to the main menu.

## Key Concepts

### 1. Lifecycle Hooks for Patching

Manual Harmony patches are applied in the `OnGameReady` lifecycle hook. The injector calls this static method when `Main.Initialize()` completes and all game types are ready:

```csharp
public static void OnGameReady()
{
    _harmony = new Harmony("com.terrariamodder.skipintro");
    PatchGame();
}
```

This is the recommended pattern for all mods that apply manual patches. The injector discovers `OnGameReady()` on your types automatically, no registration needed.

### 2. Finding Game Types via Reflection

We need to access Terraria's internal fields:

```csharp
var mainType = Type.GetType("Terraria.Main, Terraria")
    ?? Assembly.Load("Terraria").GetType("Terraria.Main");

_showSplashField = mainType.GetField("showSplash",
    BindingFlags.Public | BindingFlags.Static);

_isAsyncLoadCompleteField = mainType.GetField("_isAsyncLoadComplete",
    BindingFlags.NonPublic | BindingFlags.Static);
```

We try multiple approaches because field accessibility can vary.

### 3. Postfix Patch

A postfix runs after the original method:

```csharp
var doUpdateMethod = mainType.GetMethod("DoUpdate",
    BindingFlags.NonPublic | BindingFlags.Instance);

var postfix = typeof(Mod).GetMethod("DoUpdate_Postfix",
    BindingFlags.Public | BindingFlags.Static);

_harmony.Patch(doUpdateMethod, postfix: new HarmonyMethod(postfix));
```

The postfix signature:
```csharp
public static void DoUpdate_Postfix(object __instance)
```

- `__instance` is the Main instance being updated
- The method must be `public static`

### 4. One-Time Logic with Guard

We only want to skip once:

```csharp
private static bool _hasSkipped = false;

public static void DoUpdate_Postfix(object __instance)
{
    if (_hasSkipped) return;  // Don't run again

    // ... skip logic ...

    _hasSkipped = true;  // Mark as done
}
```

### 5. Cleanup on Unload

Always unpatch and reset state:

```csharp
public void Unload()
{
    _harmony?.UnpatchAll("com.terrariamodder.skipintro");
    _hasSkipped = false;  // Reset for next session
}
```

## The Skip Logic

The actual skip checks two conditions:

1. **Assets loaded** - `_isAsyncLoadComplete` is true
2. **Still on splash** - `showSplash` is true

Then it either:
- Sets `splashCounter` to a high value (fast-forwards animation)
- Or sets `showSplash = false` as fallback

```csharp
if (isAsyncLoadComplete)
{
    if (_splashCounterField != null)
    {
        _splashCounterField.SetValue(__instance, 99999);
    }
    else
    {
        _showSplashField.SetValue(null, false);
    }
    _hasSkipped = true;
}
```

## Idealized Source

> **Note:** This is a simplified version using the `OnGameReady()` lifecycle hook pattern.
> The actual SkipIntro mod uses a `Timer`-based delayed patch approach instead, which
> predates the lifecycle hook system. Both approaches work; `OnGameReady()` is the
> recommended pattern for new mods.

```csharp
using System;
using System.Reflection;
using HarmonyLib;
using TerrariaModder.Core;
using TerrariaModder.Core.Logging;

namespace SkipIntro
{
    public class Mod : IMod
    {
        public string Id => "skip-intro";
        public string Name => "Skip Intro";
        public string Version => "1.0.0";

        private static ILogger _log;
        private static Harmony _harmony;
        private static bool _hasSkipped = false;
        private static bool _enabled = true;
        private static FieldInfo _showSplashField;
        private static FieldInfo _isAsyncLoadCompleteField;
        private static FieldInfo _splashCounterField;

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _enabled = context.Config.Get<bool>("enabled");

            if (!_enabled)
            {
                _log.Info("Skip Intro is disabled");
            }
        }

        /// <summary>
        /// Called by injector when Main.Initialize() completes.
        /// Game types are ready -- safe to apply Harmony patches.
        /// </summary>
        public static void OnGameReady()
        {
            if (!_enabled) return;

            try
            {
                _harmony = new Harmony("com.terrariamodder.skipintro");

                var mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");

                _showSplashField = mainType.GetField("showSplash",
                    BindingFlags.Public | BindingFlags.Static);
                _isAsyncLoadCompleteField = mainType.GetField("_isAsyncLoadComplete",
                    BindingFlags.NonPublic | BindingFlags.Static);
                _splashCounterField = mainType.GetField("splashCounter",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var doUpdateMethod = mainType.GetMethod("DoUpdate",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (doUpdateMethod != null)
                {
                    _harmony.Patch(doUpdateMethod,
                        postfix: new HarmonyMethod(typeof(Mod), "DoUpdate_Postfix"));
                    _log?.Info("Patched Main.DoUpdate");
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"Patch error: {ex.Message}");
            }
        }

        public static void DoUpdate_Postfix(object __instance)
        {
            if (_hasSkipped) return;

            try
            {
                bool showSplash = (bool)_showSplashField.GetValue(null);
                if (!showSplash)
                {
                    _hasSkipped = true;
                    return;
                }

                if (_isAsyncLoadCompleteField != null)
                {
                    bool loaded = (bool)_isAsyncLoadCompleteField.GetValue(null);
                    if (loaded)
                    {
                        if (_splashCounterField != null)
                            _splashCounterField.SetValue(__instance, 99999);
                        else
                            _showSplashField.SetValue(null, false);

                        _hasSkipped = true;
                        _log?.Info("Intro skipped!");
                    }
                }
            }
            catch { _hasSkipped = true; }
        }

        public void OnWorldLoad() { }
        public void OnWorldUnload() { }

        public void Unload()
        {
            _harmony?.UnpatchAll("com.terrariamodder.skipintro");
            _hasSkipped = false;
        }
    }
}
```

## Lessons Learned

1. **Use lifecycle hooks for patches** - Apply manual Harmony patches in `OnGameReady()`
2. **Use reflection carefully** - Check for null before using fields
3. **Guard one-time logic** - Use a static bool to prevent repeated execution
4. **Clean up on unload** - Unpatch and reset static state
5. **Have fallbacks** - Multiple ways to achieve the same result

For more on Harmony patching patterns, see [Harmony Basics](../harmony-basics).
