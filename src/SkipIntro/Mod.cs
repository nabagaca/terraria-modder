using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using TerrariaModder.Core;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.Reflection;

namespace SkipIntro
{
    public class Mod : IMod
    {
        public string Id => "skip-intro";
        public string Name => "Skip Intro";
        public string Version => "1.0.0";

        private ILogger _log;
        private Harmony _harmony;
        private Timer _patchTimer;
        private static bool _hasSkipped = false;
        private static FieldInfo _showSplashField;
        private static FieldInfo _isAsyncLoadCompleteField;
        private static FieldInfo _splashCounterField;
        private static ILogger _staticLog;

        // NOTE: This mod intentionally does NOT implement OnConfigChanged
        // to test the "restart required" functionality

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _staticLog = _log;

            if (!context.Config.Get<bool>("enabled"))
            {
                _log.Info("Skip Intro is disabled in config");
                return;
            }

            _harmony = new Harmony("com.terrariamodder.skipintro");

            // Delay patching to allow game to initialize
            _patchTimer = new Timer(PatchAfterDelay, null, 5000, Timeout.Infinite);
            _log.Info("Skip Intro initialized - patches will apply in 5 seconds");
        }

        private void PatchAfterDelay(object state)
        {
            try
            {
                var mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");

                if (mainType == null)
                {
                    _log.Error("Could not find Terraria.Main type");
                    return;
                }

                _showSplashField = mainType.GetField("showSplash", BindingFlags.Public | BindingFlags.Static);
                _isAsyncLoadCompleteField = mainType.GetField("_isAsyncLoadComplete", BindingFlags.NonPublic | BindingFlags.Static);
                _splashCounterField = mainType.GetField("splashCounter", BindingFlags.NonPublic | BindingFlags.Instance);

                if (_showSplashField == null)
                {
                    _log.Warn("Could not find showSplash field - intro skip may not work");
                    return;
                }

                var doUpdateMethod = mainType.GetMethod("DoUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (doUpdateMethod != null)
                {
                    var postfix = typeof(Mod).GetMethod("DoUpdate_Postfix", BindingFlags.Public | BindingFlags.Static);
                    _harmony.Patch(doUpdateMethod, postfix: new HarmonyMethod(postfix));
                    _log.Info("Successfully patched Main.DoUpdate");
                }
                else
                {
                    _log.Warn("Could not find Main.DoUpdate method");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Patch error: {ex.Message}");
            }
        }

        public static void DoUpdate_Postfix(object __instance)
        {
            try
            {
                if (_hasSkipped) return;

                bool showSplash = (bool)_showSplashField.GetValue(null);
                if (!showSplash)
                {
                    _hasSkipped = true;
                    return;
                }

                if (_isAsyncLoadCompleteField != null)
                {
                    bool isAsyncLoadComplete = (bool)_isAsyncLoadCompleteField.GetValue(null);
                    if (isAsyncLoadComplete)
                    {
                        if (_splashCounterField != null)
                        {
                            _splashCounterField.SetValue(__instance, 99999);
                            _hasSkipped = true;
                            _staticLog?.Info("Intro splash skipped!");
                        }
                        else
                        {
                            _showSplashField.SetValue(null, false);
                            _hasSkipped = true;
                            _staticLog?.Info("Intro splash skipped (fallback)");
                        }
                    }
                }
            }
            catch
            {
                // Silently mark as skipped to prevent repeated attempts on error
                _hasSkipped = true;
            }
        }

        public void OnWorldLoad() { }
        public void OnWorldUnload() { }

        public void Unload()
        {
            _harmony?.UnpatchAll("com.terrariamodder.skipintro");
            _patchTimer?.Dispose();

            // Reset static state for hot-reload support
            _hasSkipped = false;
            _showSplashField = null;
            _isAsyncLoadCompleteField = null;
            _splashCounterField = null;
            _staticLog = null;

            _log.Info("Skip Intro unloaded");
        }
    }
}
