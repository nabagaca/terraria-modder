using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.Reflection;

namespace TerrariaModder.Core.UI
{
    /// <summary>
    /// Low-level UI rendering using reflection to avoid XNA dependencies.
    /// Uses Terraria's built-in SpriteBatch, fonts, and textures.
    /// </summary>
    public static class UIRenderer
    {
        private static ILogger _log;
        private static bool _initialized;
        private static bool _initFailed;

        // Cached reflection objects
        private static object _spriteBatch;
        private static object _font;
        private static object _magicPixel;

        // Types
        private static Type _spriteBatchType;
        private static Type _vector2Type;
        private static Type _colorType;
        private static Type _rectangleType;
        private static Type _spriteSortModeType;
        private static Type _blendStateType;
        private static Type _samplerStateType;
        private static Type _depthStencilStateType;
        private static Type _rasterizerStateType;
        private static Type _matrixType;

        // Constructors
        private static ConstructorInfo _vector2Ctor;
        private static FieldInfo _vector2XField;
        private static ConstructorInfo _colorCtorRgba;
        private static ConstructorInfo _rectangleCtor;

        // Methods
        private static MethodInfo _drawTextureRect;
        private static MethodInfo _drawString;
        private static MethodInfo _measureString;
        private static MethodInfo _spriteBatchBegin;
        private static MethodInfo _spriteBatchEnd;

        // SpriteBatch state
        private static FieldInfo _spriteBatchBeginCalled;
        private static object _spriteSortModeDeferred;
        private static object _blendStateAlphaBlend;
        private static object _depthStencilStateNone;
        private static object _mainDefaultSamplerState;
        private static object _mainRasterizer;
        private static object _mainTransformMatrix;
        private static object _identityMatrix;

        // Track if we called Begin
        private static bool _weCalledBegin;

        // Input state
        private static FieldInfo _mouseXField;
        private static FieldInfo _mouseYField;
        private static FieldInfo _mouseLeftField;
        private static FieldInfo _mouseLeftReleaseField;
        private static FieldInfo _mouseRightField;
        private static FieldInfo _mouseRightReleaseField;
        private static FieldInfo _mouseMiddleField;
        private static FieldInfo _mouseMiddleReleaseField;
        private static FieldInfo _blockMouseField;

        // Screen dimensions
        private static FieldInfo _screenWidthField;
        private static FieldInfo _screenHeightField;
        private static FieldInfo _uiScaleField;

        private static bool _scrollPatchApplied;
        private static bool _ignoreMousePatchApplied;
        private static bool _inventoryScrollPatchApplied;
        private static bool _copyIntoPatchApplied;

        // Cached Player control fields (perf: avoid reflection lookup every frame)
        private static FieldInfo _controlUseTileField;
        private static FieldInfo _controlUseItemField;
        private static FieldInfo _controlUpField;
        private static FieldInfo _controlDownField;
        private static FieldInfo _controlLeftField;
        private static FieldInfo _controlRightField;
        private static FieldInfo _controlJumpField;
        private static FieldInfo _controlThrowField;
        private static FieldInfo _controlHookField;
        private static FieldInfo _controlMountField;
        private static FieldInfo _mouseInterfaceField;

        // Cached fields for scroll consumption and keyboard blocking
        private static Type _playerInputType;
        private static FieldInfo _scrollWheelDeltaForUIField;
        private static FieldInfo _scrollWheelDeltaField;
        private static FieldInfo _blockInputField;
        private static FieldInfo _mainPlayerInputWritingTextField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerArrayField;

        // Raw mouse position from PlayerInput (never transformed by zoom — always hardware pixel coords)
        private static FieldInfo _playerInputMouseXField;
        private static FieldInfo _playerInputMouseYField;

        public static void Initialize(ILogger log)
        {
            _log = log;
            // Scroll block patch is applied lazily in BlockInput when first needed
        }

        /// <summary>
        /// Apply a Harmony prefix patch on Player.Update to consume scroll BEFORE hotbar scrolling.
        /// This runs after PlayerInput.UpdateInput but before the hotbar scroll code.
        /// </summary>
        private static void ApplyScrollBlockPatch()
        {
            if (_scrollPatchApplied) return;

            try
            {
                var playerType = typeof(Terraria.Main).Assembly.GetType("Terraria.Player");
                if (playerType == null)
                {
                    _log?.Warn("[UI] Could not find Player type for scroll patch");
                    return;
                }

                var updateMethod = playerType.GetMethod("Update", BindingFlags.Public | BindingFlags.Instance);
                if (updateMethod == null)
                {
                    _log?.Warn("[UI] Could not find Player.Update for scroll patch");
                    return;
                }

                var harmony = new Harmony("TerrariaModder.Core.UI.ScrollBlock");
                var prefix = typeof(UIRenderer).GetMethod(nameof(PlayerUpdateScrollBlockPrefix), BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(updateMethod, prefix: new HarmonyMethod(prefix));

                _scrollPatchApplied = true;
                _log?.Info("[UI] Applied Player.Update scroll block patch");
            }
            catch (Exception ex)
            {
                _log?.Error($"[UI] Failed to apply scroll block patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply a Harmony postfix patch on PlayerInput.IgnoreMouseInterface to return true when blocking.
        /// This prevents HUD buttons (quick stack, bestiary, sort) from processing clicks through our modals.
        /// </summary>
        private static void ApplyIgnoreMouseInterfacePatch()
        {
            if (_ignoreMousePatchApplied) return;

            try
            {
                var playerInputType = typeof(Terraria.Main).Assembly.GetType("Terraria.GameInput.PlayerInput");
                if (playerInputType == null)
                {
                    _log?.Warn("[UI] Could not find PlayerInput type for IgnoreMouseInterface patch");
                    return;
                }

                var ignoreMouseProperty = playerInputType.GetProperty("IgnoreMouseInterface", BindingFlags.Public | BindingFlags.Static);
                if (ignoreMouseProperty == null)
                {
                    _log?.Warn("[UI] Could not find PlayerInput.IgnoreMouseInterface property");
                    return;
                }

                var getter = ignoreMouseProperty.GetGetMethod();
                if (getter == null)
                {
                    _log?.Warn("[UI] Could not find PlayerInput.IgnoreMouseInterface getter");
                    return;
                }

                var harmony = new Harmony("TerrariaModder.Core.UI.IgnoreMouseInterface");
                var postfix = typeof(UIRenderer).GetMethod(nameof(IgnoreMouseInterfacePostfix), BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));

                _ignoreMousePatchApplied = true;
                _log?.Info("[UI] Applied PlayerInput.IgnoreMouseInterface patch for HUD button blocking");
            }
            catch (Exception ex)
            {
                _log?.Error($"[UI] Failed to apply IgnoreMouseInterface patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply a Harmony prefix on Main.DoScrollingInInventory to block crafting/recipe scroll
        /// when mouse is over a mod panel. This method runs BEFORE Player.Update, so we must
        /// consume scroll here rather than in PlayerUpdateScrollBlockPrefix.
        /// Mouse position IS current here (DoUpdate_HandleInput already ran).
        /// </summary>
        private static void ApplyInventoryScrollPatch()
        {
            if (_inventoryScrollPatchApplied) return;

            try
            {
                var mainType = typeof(Terraria.Main);
                var method = mainType.GetMethod("DoScrollingInInventory", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    _log?.Warn("[UI] Could not find Main.DoScrollingInInventory for scroll patch");
                    return;
                }

                var harmony = new Harmony("TerrariaModder.Core.UI.InventoryScroll");
                var prefix = typeof(UIRenderer).GetMethod(nameof(DoScrollingInInventoryPrefix), BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));

                _inventoryScrollPatchApplied = true;
                _log?.Info("[UI] Applied Main.DoScrollingInInventory scroll block patch");
            }
            catch (Exception ex)
            {
                _log?.Error($"[UI] Failed to apply inventory scroll patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Harmony prefix for Main.DoScrollingInInventory.
        /// When mouse is over a mod panel, consume scroll and skip the method to prevent
        /// crafting recipe scrolling behind the panel.
        /// </summary>
        private static bool DoScrollingInInventoryPrefix()
        {
            if (IsBlocking)
            {
                // Capture scroll for our UI, but don't overwrite if already captured
                var raw = ReadRawScrollDelta();
                if (raw != 0 && _capturedScrollValue == 0)
                    _capturedScrollValue = raw;
                ConsumeScroll();
                return false; // skip recipe scrolling when any panel is open
            }
            return true;
        }

        /// <summary>
        /// Apply a Harmony postfix on TriggersSet.CopyInto(Player) to clear controlUseTile
        /// and controlUseItem when mouse is over a mod panel. This is the most surgical approach:
        /// it clears the specific control flags AFTER CopyInto sets them from triggers, but BEFORE
        /// Player.Update body uses them. No global flags (blockMouse, mouseInterface) are set,
        /// so Draw-phase inventory processing is not affected.
        /// </summary>
        private static void ApplyCopyIntoPatch()
        {
            if (_copyIntoPatchApplied) return;

            try
            {
                var triggersSetType = typeof(Terraria.Main).Assembly.GetType("Terraria.GameInput.TriggersSet");
                if (triggersSetType == null)
                {
                    _log?.Warn("[UI] Could not find TriggersSet type for CopyInto patch");
                    return;
                }

                var playerType = typeof(Terraria.Main).Assembly.GetType("Terraria.Player");
                var method = triggersSetType.GetMethod("CopyInto", BindingFlags.Public | BindingFlags.Instance, null, new[] { playerType }, null);
                if (method == null)
                {
                    _log?.Warn("[UI] Could not find TriggersSet.CopyInto for world interaction block patch");
                    return;
                }

                // Cache Player control fields for performance (called every frame)
                _controlUseTileField = playerType.GetField("controlUseTile", BindingFlags.Public | BindingFlags.Instance);
                _controlUseItemField = playerType.GetField("controlUseItem", BindingFlags.Public | BindingFlags.Instance);

                var harmony = new Harmony("TerrariaModder.Core.UI.CopyIntoBlock");
                var postfix = typeof(UIRenderer).GetMethod(nameof(CopyIntoPostfix), BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));

                _copyIntoPatchApplied = true;
                _log?.Info("[UI] Applied TriggersSet.CopyInto postfix for world interaction blocking");
            }
            catch (Exception ex)
            {
                _log?.Error($"[UI] Failed to apply CopyInto patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Harmony postfix for TriggersSet.CopyInto(Player).
        /// After triggers are copied to player controls, clears controlUseTile and controlUseItem
        /// when mouse is over a mod panel. Uses UIScale-corrected coordinates for Update phase.
        /// </summary>
        private static void CopyIntoPostfix(object p)
        {
            if (!IsBlocking) return;
            if (!IsMouseOverAnyPanelUpdatePhase()) return;

            try
            {
                _controlUseTileField?.SetValue(p, false);
                _controlUseItemField?.SetValue(p, false);
            }
            catch { }
        }

        /// <summary>
        /// <summary>
        /// Harmony postfix for PlayerInput.IgnoreMouseInterface getter.
        /// Returns true when mouse is over a mod panel to prevent HUD buttons from processing clicks.
        /// Only blocks when mouse is actually over a panel, so HUD buttons work outside panels.
        /// </summary>
        private static void IgnoreMouseInterfacePostfix(ref bool __result)
        {
            // If we're already ignoring mouse, keep it that way
            if (__result) return;

            // Only force IgnoreMouseInterface when mouse is actually over a panel
            // This allows HUD buttons (quick stack, bestiary, sort) to work when mouse is outside
            if (IsBlocking && IsMouseOverAnyPanel())
            {
                __result = true;
            }
        }

        // Store scroll value for our UI before clearing it for the game
        private static int _capturedScrollValue;

        /// <summary>
        /// Harmony prefix for Player.Update - runs AFTER DoUpdate_HandleInput so mouse position is current.
        /// Sets mouseInterface when mouse is over a panel (prevents world interaction like mining).
        /// Captures scroll for UI panels. Clears player controls only when text field is focused.
        /// NOTE: We do NOT set blockMouse here — click prevention during Draw phase is handled by
        /// ShouldBlockItemSlot and IgnoreMouseInterface patches.
        /// </summary>
        private static void PlayerUpdateScrollBlockPrefix(object __instance)
        {
            if (IsBlocking)
            {
                // Capture scroll for our UI — only if not already captured earlier
                if (_capturedScrollValue == 0)
                    _capturedScrollValue = ReadRawScrollDelta();
                ConsumeScroll();
            }

            // Only clear player controls when a text field is focused (keyboard needed for typing)
            // When panels are open but no text field focused, player can still walk/jump/etc.
            if (IsWaitingForKeyInput)
            {
                try
                {
                    _mainPlayerInputWritingTextField?.SetValue(null, true);
                }
                catch { }

                try
                {
                    if (__instance != null)
                    {
                        _controlUpField?.SetValue(__instance, false);
                        _controlDownField?.SetValue(__instance, false);
                        _controlLeftField?.SetValue(__instance, false);
                        _controlRightField?.SetValue(__instance, false);
                        _controlJumpField?.SetValue(__instance, false);
                        _controlUseItemField?.SetValue(__instance, false);
                        _controlUseTileField?.SetValue(__instance, false);
                        _controlThrowField?.SetValue(__instance, false);
                        _controlHookField?.SetValue(__instance, false);
                        _controlMountField?.SetValue(__instance, false);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Read the raw scroll wheel delta directly from PlayerInput.
        /// </summary>
        private static int ReadRawScrollDelta()
        {
            try
            {
                if (_scrollWheelDeltaForUIField != null)
                    return (int)_scrollWheelDeltaForUIField.GetValue(null);
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Initialize rendering objects. Called lazily on first draw.
        /// </summary>
        private static bool EnsureInitialized()
        {
            // Skip on dedicated server - no graphics subsystem
            if (Game.IsServer) return false;

            if (_initialized) return true;
            if (_initFailed) return false;

            try
            {
                var mainType = typeof(Terraria.Main);

                // Find XNA types from loaded assemblies
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _spriteBatchType ??= asm.GetType("Microsoft.Xna.Framework.Graphics.SpriteBatch");
                    _vector2Type ??= asm.GetType("Microsoft.Xna.Framework.Vector2");
                    _colorType ??= asm.GetType("Microsoft.Xna.Framework.Color");
                    _rectangleType ??= asm.GetType("Microsoft.Xna.Framework.Rectangle");
                    _spriteSortModeType ??= asm.GetType("Microsoft.Xna.Framework.Graphics.SpriteSortMode");
                    _blendStateType ??= asm.GetType("Microsoft.Xna.Framework.Graphics.BlendState");
                    _samplerStateType ??= asm.GetType("Microsoft.Xna.Framework.Graphics.SamplerState");
                    _depthStencilStateType ??= asm.GetType("Microsoft.Xna.Framework.Graphics.DepthStencilState");
                    _rasterizerStateType ??= asm.GetType("Microsoft.Xna.Framework.Graphics.RasterizerState");
                    _matrixType ??= asm.GetType("Microsoft.Xna.Framework.Matrix");
                    if (_matrixType != null && _identityMatrix == null)
                    {
                        var identityProp = _matrixType.GetProperty("Identity", BindingFlags.Public | BindingFlags.Static);
                        _identityMatrix = identityProp?.GetValue(null);
                    }

                    if (_spriteBatchType != null && _vector2Type != null && _colorType != null && _rectangleType != null)
                        break;
                }

                if (_vector2Type == null || _colorType == null || _rectangleType == null)
                {
                    _log?.Error("[UI] Failed to find XNA types");
                    _initFailed = true;
                    return false;
                }

                // Get SpriteBatch
                var sbField = mainType.GetField("spriteBatch", BindingFlags.Public | BindingFlags.Static);
                _spriteBatch = sbField?.GetValue(null);
                if (_spriteBatch == null)
                {
                    _log?.Warn("[UI] SpriteBatch not available yet");
                    return false;
                }

                // Get constructors
                _vector2Ctor = _vector2Type.GetConstructor(new[] { typeof(float), typeof(float) });
                _vector2XField = _vector2Type.GetField("X");
                _colorCtorRgba = _colorType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
                _rectangleCtor = _rectangleType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });

                // Get MagicPixel texture
                var textureAssetsType = mainType.Assembly.GetType("Terraria.GameContent.TextureAssets");
                if (textureAssetsType != null)
                {
                    var magicPixelField = textureAssetsType.GetField("MagicPixel", BindingFlags.Public | BindingFlags.Static);
                    var magicPixelAsset = magicPixelField?.GetValue(null);
                    if (magicPixelAsset != null)
                    {
                        var valueProp = magicPixelAsset.GetType().GetProperty("Value");
                        _magicPixel = valueProp?.GetValue(magicPixelAsset);
                    }
                }

                // Get MouseText font
                var fontAssetsType = mainType.Assembly.GetType("Terraria.GameContent.FontAssets");
                if (fontAssetsType != null)
                {
                    var mouseTextField = fontAssetsType.GetField("MouseText", BindingFlags.Public | BindingFlags.Static);
                    var fontAsset = mouseTextField?.GetValue(null);
                    if (fontAsset != null)
                    {
                        var valueProp = fontAsset.GetType().GetProperty("Value");
                        _font = valueProp?.GetValue(fontAsset);
                    }
                }

                // Get SpriteBatch.Draw method for textures
                // Signature: Draw(Texture2D texture, Rectangle destinationRectangle, Color color)
                if (_spriteBatchType != null && _magicPixel != null)
                {
                    var texture2dType = _magicPixel.GetType();
                    _drawTextureRect = _spriteBatchType.GetMethods()
                        .FirstOrDefault(m =>
                            m.Name == "Draw" &&
                            m.GetParameters().Length == 3 &&
                            m.GetParameters()[0].ParameterType == texture2dType &&
                            m.GetParameters()[1].ParameterType == _rectangleType &&
                            m.GetParameters()[2].ParameterType == _colorType);

                    if (_drawTextureRect == null)
                        _log?.Warn("[UI] Could not find exact Draw(Texture2D, Rectangle, Color) method");
                }

                // Get DrawString extension method
                Type dynamicSpriteFontType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    dynamicSpriteFontType = asm.GetType("ReLogic.Graphics.DynamicSpriteFont");
                    if (dynamicSpriteFontType != null) break;
                }

                if (dynamicSpriteFontType != null && _font != null)
                {
                    Type extensionType = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        extensionType = asm.GetType("ReLogic.Graphics.DynamicSpriteFontExtensionMethods");
                        if (extensionType != null) break;
                    }

                    if (extensionType != null)
                    {
                        _drawString = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .FirstOrDefault(m => m.Name == "DrawString" && m.GetParameters().Length >= 5);
                    }

                    // Cache MeasureString on the font type
                    _measureString = dynamicSpriteFontType.GetMethod("MeasureString",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(string) }, null);
                }

                // Get SpriteBatch Begin/End
                if (_spriteBatchType != null)
                {
                    _spriteBatchBegin = _spriteBatchType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Begin" && m.GetParameters().Length == 7);
                    _spriteBatchEnd = _spriteBatchType.GetMethod("End", BindingFlags.Public | BindingFlags.Instance);
                    // XNA 4.0 uses "inBeginEndPair", FNA uses "beginCalled"
                    _spriteBatchBeginCalled = _spriteBatchType.GetField("inBeginEndPair", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? _spriteBatchType.GetField("beginCalled", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? _spriteBatchType.GetField("_beginCalled", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                // Get render state values
                if (_spriteSortModeType != null)
                    _spriteSortModeDeferred = Enum.Parse(_spriteSortModeType, "Deferred");
                if (_blendStateType != null)
                    _blendStateAlphaBlend = _blendStateType.GetProperty("AlphaBlend", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (_depthStencilStateType != null)
                    _depthStencilStateNone = _depthStencilStateType.GetProperty("None", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                // Get Main's render state
                var defaultSamplerField = mainType.GetField("DefaultSamplerState", BindingFlags.Public | BindingFlags.Static);
                _mainDefaultSamplerState = defaultSamplerField?.GetValue(null);
                if (_mainDefaultSamplerState == null)
                {
                    var defaultSamplerProp = mainType.GetProperty("DefaultSamplerState", BindingFlags.Public | BindingFlags.Static);
                    _mainDefaultSamplerState = defaultSamplerProp?.GetValue(null);
                }

                var rasterizerField = mainType.GetField("Rasterizer", BindingFlags.Public | BindingFlags.Static);
                _mainRasterizer = rasterizerField?.GetValue(null);
                if (_mainRasterizer == null)
                {
                    var rasterizerProp = mainType.GetProperty("Rasterizer", BindingFlags.Public | BindingFlags.Static);
                    _mainRasterizer = rasterizerProp?.GetValue(null);
                }

                // Get transform matrix
                var gameViewMatrixField = mainType.GetField("GameViewMatrix", BindingFlags.Public | BindingFlags.Static);
                if (gameViewMatrixField != null)
                {
                    var gameViewMatrix = gameViewMatrixField.GetValue(null);
                    if (gameViewMatrix != null)
                    {
                        var transformProp = gameViewMatrix.GetType().GetProperty("TransformationMatrix", BindingFlags.Public | BindingFlags.Instance);
                        _mainTransformMatrix = transformProp?.GetValue(gameViewMatrix);
                    }
                }

                // Get input fields
                _mouseXField = mainType.GetField("mouseX", BindingFlags.Public | BindingFlags.Static);
                _mouseYField = mainType.GetField("mouseY", BindingFlags.Public | BindingFlags.Static);
                _mouseLeftField = mainType.GetField("mouseLeft", BindingFlags.Public | BindingFlags.Static);
                _mouseLeftReleaseField = mainType.GetField("mouseLeftRelease", BindingFlags.Public | BindingFlags.Static);
                _mouseRightField = mainType.GetField("mouseRight", BindingFlags.Public | BindingFlags.Static);
                _mouseRightReleaseField = mainType.GetField("mouseRightRelease", BindingFlags.Public | BindingFlags.Static);
                _mouseMiddleField = mainType.GetField("mouseMiddle", BindingFlags.Public | BindingFlags.Static);
                _mouseMiddleReleaseField = mainType.GetField("mouseMiddleRelease", BindingFlags.Public | BindingFlags.Static);
                _blockMouseField = mainType.GetField("blockMouse", BindingFlags.Public | BindingFlags.Static);
                _screenWidthField = mainType.GetField("screenWidth", BindingFlags.Public | BindingFlags.Static);
                _screenHeightField = mainType.GetField("screenHeight", BindingFlags.Public | BindingFlags.Static);
                _uiScaleField = mainType.GetField("_uiScaleUsed", BindingFlags.NonPublic | BindingFlags.Static);

                // Cache PlayerInput type and fields used per-frame
                _playerInputType = mainType.Assembly.GetType("Terraria.GameInput.PlayerInput");
                if (_playerInputType != null)
                {
                    // Raw mouse position (never transformed by zoom)
                    _playerInputMouseXField = _playerInputType.GetField("MouseX", BindingFlags.Public | BindingFlags.Static);
                    _playerInputMouseYField = _playerInputType.GetField("MouseY", BindingFlags.Public | BindingFlags.Static);
                    if (_playerInputMouseXField == null)
                        _log?.Warn("[UI] Could not find PlayerInput.MouseX field");

                    // Scroll fields (used by ConsumeScroll/ReadRawScrollDelta)
                    _scrollWheelDeltaForUIField = _playerInputType.GetField("ScrollWheelDeltaForUI", BindingFlags.Public | BindingFlags.Static);
                    _scrollWheelDeltaField = _playerInputType.GetField("ScrollWheelDelta", BindingFlags.Public | BindingFlags.Static);

                    // WritingText for keyboard blocking
                    _mainPlayerInputWritingTextField = _playerInputType.GetField("WritingText", BindingFlags.Public | BindingFlags.Static);
                }

                // Cache fields for BlockKeyboardInput
                _blockInputField = mainType.GetField("blockInput", BindingFlags.Public | BindingFlags.Static);
                _myPlayerField = mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                _playerArrayField = mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);

                // Cache Player control fields (used by PlayerUpdateScrollBlockPrefix and BlockKeyboardInput)
                var playerType = mainType.Assembly.GetType("Terraria.Player");
                if (playerType != null)
                {
                    _controlUpField = playerType.GetField("controlUp", BindingFlags.Public | BindingFlags.Instance);
                    _controlDownField = playerType.GetField("controlDown", BindingFlags.Public | BindingFlags.Instance);
                    _controlLeftField = playerType.GetField("controlLeft", BindingFlags.Public | BindingFlags.Instance);
                    _controlRightField = playerType.GetField("controlRight", BindingFlags.Public | BindingFlags.Instance);
                    _controlJumpField = playerType.GetField("controlJump", BindingFlags.Public | BindingFlags.Instance);
                    _controlThrowField = playerType.GetField("controlThrow", BindingFlags.Public | BindingFlags.Instance);
                    _controlHookField = playerType.GetField("controlHook", BindingFlags.Public | BindingFlags.Instance);
                    _controlMountField = playerType.GetField("controlMount", BindingFlags.Public | BindingFlags.Instance);
                    _mouseInterfaceField = playerType.GetField("mouseInterface", BindingFlags.Public | BindingFlags.Instance);
                }

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"[UI] Failed to initialize renderer: {ex.Message}");
                _initFailed = true;
                return false;
            }
        }

        #region Scissor Clipping

        private static object _graphicsDevice;
        private static PropertyInfo _scissorRectProperty;
        private static object _rasterizerStateScissor;
        private static object _rasterizerStateNormal;
        private static bool _scissorInitialized;
        private static object _savedScissorRect;
        private static object _savedRasterizerState;
        private static bool _clipActive;

        /// <summary>
        /// Initialize scissor clipping support.
        /// </summary>
        private static bool InitScissor()
        {
            if (_scissorInitialized) return _graphicsDevice != null;

            try
            {
                var mainType = typeof(Terraria.Main);

                // Get graphics device
                var graphicsField = mainType.GetField("graphics", BindingFlags.Public | BindingFlags.Static);
                if (graphicsField != null)
                {
                    var graphicsManager = graphicsField.GetValue(null);
                    if (graphicsManager != null)
                    {
                        var gdProp = graphicsManager.GetType().GetProperty("GraphicsDevice");
                        _graphicsDevice = gdProp?.GetValue(graphicsManager);
                    }
                }

                if (_graphicsDevice == null)
                {
                    // Try instance.GraphicsDevice
                    if (_mainInstanceField == null)
                        _mainInstanceField = mainType.GetField("instance", BindingFlags.Public | BindingFlags.Static);

                    if (_mainInstanceField != null)
                    {
                        var instance = _mainInstanceField.GetValue(null);
                        var gdProp = instance?.GetType().GetProperty("GraphicsDevice");
                        _graphicsDevice = gdProp?.GetValue(instance);
                    }
                }

                if (_graphicsDevice != null)
                {
                    _scissorRectProperty = _graphicsDevice.GetType().GetProperty("ScissorRectangle");

                    // Create RasterizerState with ScissorTestEnable = true
                    var rasterizerStateType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Microsoft.Xna.Framework.Graphics.RasterizerState"))
                        .FirstOrDefault(t => t != null);

                    if (rasterizerStateType != null)
                    {
                        _rasterizerStateScissor = Activator.CreateInstance(rasterizerStateType);
                        var scissorEnableProp = rasterizerStateType.GetProperty("ScissorTestEnable");
                        var cullModeProp = rasterizerStateType.GetProperty("CullMode");

                        if (scissorEnableProp != null)
                            scissorEnableProp.SetValue(_rasterizerStateScissor, true);

                        // Set CullMode to None to match Terraria's default
                        if (cullModeProp != null)
                        {
                            var cullModeType = cullModeProp.PropertyType;
                            var noneValue = Enum.Parse(cullModeType, "None");
                            cullModeProp.SetValue(_rasterizerStateScissor, noneValue);
                        }

                        // Get CullNone for restoring
                        _rasterizerStateNormal = rasterizerStateType.GetProperty("CullNone", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    }

                }

                _scissorInitialized = true;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[UI] InitScissor failed: {ex.Message}");
                _scissorInitialized = true;
            }

            return _graphicsDevice != null;
        }

        /// <summary>
        /// Begin a clip rectangle. All drawing after this will be clipped to the specified bounds.
        /// Must call EndClip() when done.
        /// </summary>
        public static void BeginClip(int x, int y, int width, int height)
        {
            if (!EnsureInitialized()) return;
            if (!InitScissor()) return;
            if (_clipActive) return; // Don't nest clips

            try
            {
                // Safety check: verify SpriteBatch is in a valid state before manipulating it
                if (_spriteBatch == null) return;

                // Refresh transform matrix BEFORE touching SpriteBatch state
                var mainType2 = typeof(Terraria.Main);
                var gameViewMatrixField = mainType2.GetField("GameViewMatrix", BindingFlags.Public | BindingFlags.Static);
                if (gameViewMatrixField != null)
                {
                    var gameViewMatrix = gameViewMatrixField.GetValue(null);
                    if (gameViewMatrix != null)
                    {
                        var transformProp = gameViewMatrix.GetType().GetProperty("TransformationMatrix", BindingFlags.Public | BindingFlags.Instance);
                        _mainTransformMatrix = transformProp?.GetValue(gameViewMatrix);
                    }
                }

                // Use identity matrix as fallback when transform isn't available (e.g., title screen)
                var clipTransform = _mainTransformMatrix ?? _identityMatrix;
                if (clipTransform == null || _spriteBatchBegin == null ||
                    _spriteSortModeDeferred == null || _blendStateAlphaBlend == null ||
                    _mainDefaultSamplerState == null || _depthStencilStateNone == null ||
                    _rasterizerStateScissor == null)
                {
                    return;
                }

                // End current batch so we can restart with scissor rasterizer
                if (_spriteBatchBeginCalled != null)
                {
                    bool beginCalled = (bool)_spriteBatchBeginCalled.GetValue(_spriteBatch);
                    if (beginCalled)
                    {
                        _spriteBatchEnd?.Invoke(_spriteBatch, null);
                    }
                }

                // Save current scissor rect
                _savedScissorRect = _scissorRectProperty?.GetValue(_graphicsDevice);

                // Save current rasterizer state
                var rasterizerStateProp = _graphicsDevice.GetType().GetProperty("RasterizerState");
                _savedRasterizerState = rasterizerStateProp?.GetValue(_graphicsDevice);

                // Set new scissor rect
                var newScissor = _rectangleCtor.Invoke(new object[] { x, y, width, height });
                _scissorRectProperty?.SetValue(_graphicsDevice, newScissor);

                // Set rasterizer state with scissor test enabled
                rasterizerStateProp?.SetValue(_graphicsDevice, _rasterizerStateScissor);

                // Begin batch with scissor-enabled rasterizer
                _spriteBatchBegin.Invoke(_spriteBatch, new object[]
                {
                    _spriteSortModeDeferred,
                    _blendStateAlphaBlend,
                    _mainDefaultSamplerState,
                    _depthStencilStateNone,
                    _rasterizerStateScissor,
                    null,
                    clipTransform
                });

                _clipActive = true;
            }
            catch (Exception ex)
            {
                // On failure (e.g., during window resize), clean up state
                _clipActive = false;
                _log?.Warn($"[UI] BeginClip failed: {ex.Message}");
            }
        }

        /// <summary>
        /// End the current clip rectangle and restore normal drawing.
        /// </summary>
        public static void EndClip()
        {
            if (!_clipActive) return;

            try
            {
                // Safety check for window resize scenarios
                if (_spriteBatch == null)
                {
                    _clipActive = false;
                    return;
                }

                // End clipped batch
                if (_spriteBatchBeginCalled != null)
                {
                    bool beginCalled = (bool)_spriteBatchBeginCalled.GetValue(_spriteBatch);
                    if (beginCalled)
                    {
                        _spriteBatchEnd?.Invoke(_spriteBatch, null);
                    }
                }

                // Restore scissor rect
                if (_savedScissorRect != null)
                    _scissorRectProperty?.SetValue(_graphicsDevice, _savedScissorRect);

                // Restore rasterizer state
                var rasterizerStateProp = _graphicsDevice?.GetType().GetProperty("RasterizerState");
                if (_savedRasterizerState != null)
                    rasterizerStateProp?.SetValue(_graphicsDevice, _savedRasterizerState);

                // Begin normal batch again
                var matrix = _mainTransformMatrix ?? _identityMatrix;
                var rasterizer = _mainRasterizer;
                bool restored = false;
                if (_spriteBatchBegin != null && _spriteSortModeDeferred != null && _blendStateAlphaBlend != null &&
                    _mainDefaultSamplerState != null && _depthStencilStateNone != null &&
                    rasterizer != null && matrix != null)
                {
                    _spriteBatchBegin.Invoke(_spriteBatch, new object[]
                    {
                        _spriteSortModeDeferred,
                        _blendStateAlphaBlend,
                        _mainDefaultSamplerState,
                        _depthStencilStateNone,
                        rasterizer,
                        null,
                        matrix
                    });
                    restored = true;
                }

                if (!restored)
                {
                    // Couldn't restore with full params — use simple Begin as fallback
                    RestoreSpriteBatch();
                }

                _clipActive = false;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[UI] EndClip failed: {ex.Message}");
                _clipActive = false;
            }
        }

        /// <summary>
        /// Check if scissor clipping is currently active.
        /// </summary>
        public static bool IsClipping => _clipActive;

        #endregion

        #region SpriteBatch Management

        /// <summary>
        /// Begin drawing. Call before any Draw calls.
        /// </summary>
        private static bool _beginDrawErrorLogged;

        public static void BeginDraw()
        {
            if (!EnsureInitialized()) return;
            _weCalledBegin = false;

            // Safety: if clipping is active, don't try to begin again
            // This can happen if a previous draw was interrupted (e.g., window resize)
            if (_clipActive)
            {
                _clipActive = false; // Reset stale clip state
            }

            try
            {
                // Check if SpriteBatch.Begin was already called
                bool beginAlreadyCalled = false;
                if (_spriteBatchBeginCalled != null && _spriteBatch != null)
                    beginAlreadyCalled = (bool)_spriteBatchBeginCalled.GetValue(_spriteBatch);

                if (beginAlreadyCalled)
                {
                    // Already in drawing state, good to go
                    return;
                }

                // Try simple Begin first (no arguments)
                var simpleBegin = _spriteBatchType?.GetMethod("Begin", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (simpleBegin != null)
                {
                    simpleBegin.Invoke(_spriteBatch, null);
                    _weCalledBegin = true;
                    return;
                }

                // Fall back to complex Begin if simple doesn't exist
                if (_spriteBatchBegin != null)
                {
                    // Refresh transform matrix (it can change)
                    var mainType = typeof(Terraria.Main);
                    var gameViewMatrixField = mainType.GetField("GameViewMatrix", BindingFlags.Public | BindingFlags.Static);
                    if (gameViewMatrixField != null)
                    {
                        var gameViewMatrix = gameViewMatrixField.GetValue(null);
                        if (gameViewMatrix != null)
                        {
                            var transformProp = gameViewMatrix.GetType().GetProperty("TransformationMatrix", BindingFlags.Public | BindingFlags.Instance);
                            _mainTransformMatrix = transformProp?.GetValue(gameViewMatrix);
                        }
                    }

                    if (_spriteSortModeDeferred != null && _blendStateAlphaBlend != null &&
                        _mainDefaultSamplerState != null && _depthStencilStateNone != null &&
                        _mainRasterizer != null && _mainTransformMatrix != null)
                    {
                        _spriteBatchBegin.Invoke(_spriteBatch, new object[]
                        {
                            _spriteSortModeDeferred,
                            _blendStateAlphaBlend,
                            _mainDefaultSamplerState,
                            _depthStencilStateNone,
                            _mainRasterizer,
                            null, // Effect
                            _mainTransformMatrix
                        });
                        _weCalledBegin = true;
                    }
                    else if (!_beginDrawErrorLogged)
                    {
                        _log?.Warn($"[UI] BeginDraw missing state: sort={_spriteSortModeDeferred != null} blend={_blendStateAlphaBlend != null} sampler={_mainDefaultSamplerState != null} depth={_depthStencilStateNone != null} rast={_mainRasterizer != null} matrix={_mainTransformMatrix != null}");
                        _beginDrawErrorLogged = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_beginDrawErrorLogged)
                {
                    var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _log?.Error($"[UI] BeginDraw error: {inner}");
                    _beginDrawErrorLogged = true;
                }
            }
        }

        /// <summary>
        /// End drawing. Call after all Draw calls.
        /// </summary>
        public static void EndDraw()
        {
            if (_weCalledBegin && _spriteBatchEnd != null && _spriteBatch != null)
            {
                try
                {
                    _spriteBatchEnd.Invoke(_spriteBatch, null);
                }
                catch { }
            }
            _weCalledBegin = false;
        }

        /// <summary>
        /// Restore SpriteBatch to a working state using simple Begin().
        /// Used as fallback when full Begin with transform matrix isn't possible (e.g., title screen).
        /// </summary>
        private static void RestoreSpriteBatch()
        {
            try
            {
                var simpleBegin = _spriteBatchType?.GetMethod("Begin", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (simpleBegin != null)
                {
                    simpleBegin.Invoke(_spriteBatch, null);
                }
            }
            catch { }
        }

        /// <summary>
        /// Safety method: if SpriteBatch is in a begun state (from a previous frame's exception),
        /// call End() to reset it. Called at the start of DoDraw to prevent cascading errors.
        /// </summary>
        public static void EnsureSpriteBatchCleanState()
        {
            if (!_initialized || _spriteBatch == null || _spriteBatchBeginCalled == null) return;
            try
            {
                bool beginCalled = (bool)_spriteBatchBeginCalled.GetValue(_spriteBatch);
                if (beginCalled && _spriteBatchEnd != null)
                {
                    _spriteBatchEnd.Invoke(_spriteBatch, null);
                }
            }
            catch { }
        }

        #endregion

        #region Drawing

        private static bool _drawRectErrorLogged;

        /// <summary>Draw a filled rectangle.</summary>
        public static void DrawRect(int x, int y, int width, int height, byte r, byte g, byte b, byte a = 255)
        {
            if (!EnsureInitialized()) return;
            if (_magicPixel == null || _drawTextureRect == null || _spriteBatch == null) return;

            try
            {
                var rect = _rectangleCtor.Invoke(new object[] { x, y, width, height });
                var color = _colorCtorRgba.Invoke(new object[] { (int)r, (int)g, (int)b, (int)a });
                _drawTextureRect.Invoke(_spriteBatch, new[] { _magicPixel, rect, color });
            }
            catch (Exception ex)
            {
                if (!_drawRectErrorLogged)
                {
                    var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _log?.Error($"[UI] DrawRect failed: {inner}");
                    _drawRectErrorLogged = true;
                }
            }
        }

        /// <summary>Draw a rectangle outline.</summary>
        public static void DrawRectOutline(int x, int y, int width, int height, byte r, byte g, byte b, byte a = 255, int thickness = 1)
        {
            DrawRect(x, y, width, thickness, r, g, b, a); // Top
            DrawRect(x, y + height - thickness, width, thickness, r, g, b, a); // Bottom
            DrawRect(x, y, thickness, height, r, g, b, a); // Left
            DrawRect(x + width - thickness, y, thickness, height, r, g, b, a); // Right
        }

        private static MethodInfo _utilsDrawBorderString;
        private static bool _drawTextErrorLogged;

        /// <summary>Draw text at position using Terraria's Utils.DrawBorderString.</summary>
        public static void DrawText(string text, int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            if (!EnsureInitialized()) return;
            if (_spriteBatch == null) return;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                // Try Utils.DrawBorderString first (more reliable)
                if (_utilsDrawBorderString == null)
                {
                    var utilsType = typeof(Terraria.Main).Assembly.GetType("Terraria.Utils");
                    if (utilsType != null)
                    {
                        _utilsDrawBorderString = utilsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .FirstOrDefault(m => m.Name == "DrawBorderString" && m.GetParameters().Length >= 4);
                    }
                }

                if (_utilsDrawBorderString != null)
                {
                    var pos = _vector2Ctor.Invoke(new object[] { (float)x, (float)y });
                    var color = _colorCtorRgba.Invoke(new object[] { (int)r, (int)g, (int)b, (int)a });

                    // Utils.DrawBorderString(SpriteBatch, string, Vector2, Color, ...)
                    var parameters = _utilsDrawBorderString.GetParameters();
                    object[] args;

                    if (parameters.Length == 4)
                    {
                        args = new object[] { _spriteBatch, text, pos, color };
                    }
                    else if (parameters.Length >= 5)
                    {
                        // Has scale parameter
                        args = new object[] { _spriteBatch, text, pos, color, 1f };
                        if (parameters.Length >= 6)
                        {
                            // Pad with defaults
                            var fullArgs = new object[parameters.Length];
                            fullArgs[0] = _spriteBatch;
                            fullArgs[1] = text;
                            fullArgs[2] = pos;
                            fullArgs[3] = color;
                            fullArgs[4] = 1f; // scale
                            for (int i = 5; i < parameters.Length; i++)
                            {
                                fullArgs[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue :
                                    (parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null);
                            }
                            args = fullArgs;
                        }
                    }
                    else
                    {
                        return;
                    }

                    _utilsDrawBorderString.Invoke(null, args);
                    return;
                }

                // Fallback to direct font drawing
                if (_font != null && _drawString != null)
                {
                    var pos = _vector2Ctor.Invoke(new object[] { (float)x, (float)y });
                    var color = _colorCtorRgba.Invoke(new object[] { (int)r, (int)g, (int)b, (int)a });
                    var zero = _vector2Ctor.Invoke(new object[] { 0f, 0f });
                    var one = _vector2Ctor.Invoke(new object[] { 1f, 1f });

                    _drawString.Invoke(null, new object[] { _spriteBatch, _font, text, pos, color, 0f, zero, one, 0, 0f });
                }
            }
            catch (Exception ex)
            {
                if (!_drawTextErrorLogged)
                {
                    var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _log?.Error($"[UI] DrawText failed: {inner}");
                    _drawTextErrorLogged = true;
                }
            }
        }

        /// <summary>Draw text with shadow for readability.</summary>
        public static void DrawTextShadow(string text, int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            // Utils.DrawBorderString already has border, so just call DrawText
            DrawText(text, x, y, r, g, b, a);
        }

        /// <summary>Draw smaller text at position (0.75 scale).</summary>
        public static void DrawTextSmall(string text, int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            DrawTextScaled(text, x, y, r, g, b, a, 0.75f);
        }

        /// <summary>Draw text at position with custom scale.</summary>
        public static void DrawTextScaled(string text, int x, int y, byte r, byte g, byte b, byte a, float scale)
        {
            if (!EnsureInitialized()) return;
            if (_spriteBatch == null) return;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                if (_utilsDrawBorderString == null)
                {
                    var utilsType = typeof(Terraria.Main).Assembly.GetType("Terraria.Utils");
                    if (utilsType != null)
                    {
                        _utilsDrawBorderString = utilsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .FirstOrDefault(m => m.Name == "DrawBorderString" && m.GetParameters().Length >= 4);
                    }
                }

                if (_utilsDrawBorderString != null)
                {
                    var pos = _vector2Ctor.Invoke(new object[] { (float)x, (float)y });
                    var color = _colorCtorRgba.Invoke(new object[] { (int)r, (int)g, (int)b, (int)a });

                    var parameters = _utilsDrawBorderString.GetParameters();
                    object[] args;

                    if (parameters.Length >= 5)
                    {
                        // Has scale parameter
                        var fullArgs = new object[parameters.Length];
                        fullArgs[0] = _spriteBatch;
                        fullArgs[1] = text;
                        fullArgs[2] = pos;
                        fullArgs[3] = color;
                        fullArgs[4] = scale;
                        for (int i = 5; i < parameters.Length; i++)
                        {
                            fullArgs[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue :
                                (parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null);
                        }
                        args = fullArgs;
                    }
                    else
                    {
                        args = new object[] { _spriteBatch, text, pos, color };
                    }

                    _utilsDrawBorderString.Invoke(null, args);
                }
            }
            catch (Exception ex)
            {
                if (!_drawTextErrorLogged)
                {
                    var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _log?.Error($"[UI] DrawTextScaled failed: {inner}");
                    _drawTextErrorLogged = true;
                }
            }
        }

        /// <summary>Draw a panel with background and border.</summary>
        public static void DrawPanel(int x, int y, int width, int height, byte bgR = 40, byte bgG = 40, byte bgB = 60, byte bgA = 240)
        {
            DrawRect(x, y, width, height, bgR, bgG, bgB, bgA);
            DrawRectOutline(x, y, width, height, 100, 100, 120, 255, 2);
        }

        #region Color4 Overloads

        /// <summary>Draw a filled rectangle using a Color4.</summary>
        public static void DrawRect(int x, int y, int width, int height, Color4 c)
            => DrawRect(x, y, width, height, c.R, c.G, c.B, c.A);

        /// <summary>Draw a rectangle outline using a Color4.</summary>
        public static void DrawRectOutline(int x, int y, int width, int height, Color4 c, int thickness = 1)
            => DrawRectOutline(x, y, width, height, c.R, c.G, c.B, c.A, thickness);

        /// <summary>Draw text using a Color4.</summary>
        public static void DrawText(string text, int x, int y, Color4 c)
            => DrawText(text, x, y, c.R, c.G, c.B, c.A);

        /// <summary>Draw text with shadow using a Color4.</summary>
        public static void DrawTextShadow(string text, int x, int y, Color4 c)
            => DrawTextShadow(text, x, y, c.R, c.G, c.B, c.A);

        /// <summary>Draw small text using a Color4.</summary>
        public static void DrawTextSmall(string text, int x, int y, Color4 c)
            => DrawTextSmall(text, x, y, c.R, c.G, c.B, c.A);

        /// <summary>Draw a panel with background using a Color4.</summary>
        public static void DrawPanel(int x, int y, int width, int height, Color4 c)
        {
            DrawRect(x, y, width, height, c.R, c.G, c.B, c.A);
            DrawRectOutline(x, y, width, height, 100, 100, 120, 255, 2);
        }

        /// <summary>
        /// Measure the pixel width of a text string using the actual game font.
        /// Falls back to 7px per character estimate if font is not available.
        /// </summary>
        public static int MeasureText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            if (_font != null && _measureString != null && _vector2XField != null)
            {
                try
                {
                    var result = _measureString.Invoke(_font, new object[] { text });
                    if (result != null)
                        return (int)System.Math.Ceiling((float)_vector2XField.GetValue(result));
                }
                catch { }
            }

            // Fallback: 7px per character
            return text.Length * 7;
        }

        #endregion

        #region Item Drawing

        private static object _itemTextureAssets;
        private static Type _assetType;
        private static PropertyInfo _assetValueProp;
        private static PropertyInfo _assetIsLoadedProp;
        private static MethodInfo _assetRequestMethod;
        private static MethodInfo _loadItemMethod;
        private static MethodInfo _drawTextureScaled;
        private static bool _itemDrawingInitialized;
        private static bool _itemDrawingFailed;

        /// <summary>
        /// Draw a Terraria item texture at the specified position.
        /// </summary>
        /// <param name="itemType">The item type ID.</param>
        /// <param name="x">X position to draw at.</param>
        /// <param name="y">Y position to draw at.</param>
        /// <param name="width">Target width (item will be scaled to fit).</param>
        /// <param name="height">Target height (item will be scaled to fit).</param>
        /// <param name="alpha">Transparency (0-255).</param>
        public static void DrawItem(int itemType, int x, int y, int width, int height, byte alpha = 255)
        {
            if (!EnsureInitialized()) return;
            if (!InitItemDrawing()) return;
            if (itemType <= 0) return;

            try
            {
                // Get the item texture from TextureAssets.Item[itemType]
                var itemTexArray = _itemTextureAssets as Array;
                if (itemTexArray == null || itemType >= itemTexArray.Length) return;

                var asset = itemTexArray.GetValue(itemType);
                if (asset == null) return;

                // Check if asset is loaded - if not, try to load it
                bool isLoaded = true;
                if (_assetIsLoadedProp != null)
                {
                    isLoaded = (bool)_assetIsLoadedProp.GetValue(asset);
                }

                // If not loaded, try Main.instance.LoadItem() first (the proper Terraria way)
                if (!isLoaded)
                {
                    try
                    {
                        // Call Main.instance.LoadItem(itemType) to trigger loading
                        if (_mainInstanceField == null)
                            _mainInstanceField = typeof(Terraria.Main).GetField("instance", BindingFlags.Public | BindingFlags.Static);

                        if (_loadItemMethod == null)
                            _loadItemMethod = typeof(Terraria.Main).GetMethod("LoadItem", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);

                        if (_mainInstanceField != null && _loadItemMethod != null)
                        {
                            var mainInstance = _mainInstanceField.GetValue(null);
                            if (mainInstance != null)
                            {
                                _loadItemMethod.Invoke(mainInstance, new object[] { itemType });
                            }
                        }
                    }
                    catch { }

                    // Fallback: try asset's own Request method
                    if (_assetRequestMethod != null)
                    {
                        try { _assetRequestMethod.Invoke(asset, null); } catch { }
                    }
                }

                var texture = _assetValueProp.GetValue(asset);
                if (texture == null) return;

                // Get texture dimensions
                var texType = texture.GetType();
                var widthProp = texType.GetProperty("Width");
                var heightProp = texType.GetProperty("Height");
                if (widthProp == null || heightProp == null) return;

                int texWidth = (int)widthProp.GetValue(texture);
                int texHeight = (int)heightProp.GetValue(texture);

                // Some items have animation frames (multiple rows stacked vertically)
                // Need to calculate the correct frame height to show just one frame
                int frameHeight = texHeight;

                // Try to get frame count from Main.itemAnimations
                int frameCount = 1;
                try
                {
                    var mainType = typeof(Terraria.Main);
                    var itemAnimationsField = mainType.GetField("itemAnimations", BindingFlags.Public | BindingFlags.Static);
                    if (itemAnimationsField != null)
                    {
                        var itemAnimationsArray = itemAnimationsField.GetValue(null) as Array;
                        if (itemAnimationsArray != null && itemType < itemAnimationsArray.Length)
                        {
                            var animValue = itemAnimationsArray.GetValue(itemType);
                            if (animValue != null)
                            {
                                frameCount = (int)animValue;
                                if (frameCount > 1)
                                {
                                    frameHeight = texHeight / frameCount;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Fallback heuristic if animation data wasn't available
                // Items with very tall textures relative to width are likely animated
                if (frameCount == 1 && texHeight > texWidth * 3)
                {
                    // Estimate frame count by assuming roughly square frames
                    int estimatedFrames = texHeight / Math.Max(texWidth, 1);
                    if (estimatedFrames > 1)
                    {
                        frameHeight = texHeight / estimatedFrames;
                    }
                }

                // Guard against zero dimensions
                if (texWidth <= 0 || frameHeight <= 0)
                    return;

                // Calculate scale to fit in target area while maintaining aspect ratio
                float scaleX = (float)width / texWidth;
                float scaleY = (float)height / frameHeight;
                float scale = Math.Min(scaleX, scaleY);

                // Clamp scale to reasonable range
                scale = Math.Max(0.1f, Math.Min(scale, 4.0f));

                // Center the item in the target area
                int drawWidth = (int)(texWidth * scale);
                int drawHeight = (int)(frameHeight * scale);
                int drawX = x + (width - drawWidth) / 2;
                int drawY = y + (height - drawHeight) / 2;

                // Find Draw method with scale parameter
                // Signature: Draw(Texture2D, Vector2 position, Rectangle? sourceRect, Color, float rotation, Vector2 origin, float scale, SpriteEffects, float layerDepth)
                if (_drawTextureScaled == null)
                {
                    var texture2dType = texture.GetType();
                    var spriteEffectsType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Microsoft.Xna.Framework.Graphics.SpriteEffects"))
                        .FirstOrDefault(t => t != null);

                    if (spriteEffectsType != null)
                    {
                        _drawTextureScaled = _spriteBatchType.GetMethods()
                            .FirstOrDefault(m =>
                                m.Name == "Draw" &&
                                m.GetParameters().Length == 9 &&
                                m.GetParameters()[0].ParameterType == texture2dType &&
                                m.GetParameters()[6].ParameterType == typeof(float));
                    }
                }

                if (_drawTextureScaled != null)
                {
                    var pos = _vector2Ctor.Invoke(new object[] { (float)drawX, (float)drawY });
                    var color = _colorCtorRgba.Invoke(new object[] { 255, 255, 255, (int)alpha });
                    var origin = _vector2Ctor.Invoke(new object[] { 0f, 0f });

                    // Get SpriteEffects.None
                    var spriteEffectsType = _drawTextureScaled.GetParameters()[7].ParameterType;
                    var spriteEffectsNone = Enum.Parse(spriteEffectsType, "None");

                    // Source rectangle for first frame
                    var sourceRect = _rectangleCtor.Invoke(new object[] { 0, 0, texWidth, frameHeight });

                    // Make it nullable
                    var nullableRectType = typeof(Nullable<>).MakeGenericType(_rectangleType);
                    var nullableRect = Activator.CreateInstance(nullableRectType, sourceRect);

                    _drawTextureScaled.Invoke(_spriteBatch, new object[]
                    {
                        texture,
                        pos,
                        nullableRect,
                        color,
                        0f, // rotation
                        origin,
                        scale,
                        spriteEffectsNone,
                        0f // layer depth
                    });
                }
                else
                {
                    // Fallback: use simpler Draw method with destination rectangle
                    var destRect = _rectangleCtor.Invoke(new object[] { drawX, drawY, drawWidth, drawHeight });
                    var sourceRect = _rectangleCtor.Invoke(new object[] { 0, 0, texWidth, frameHeight });
                    var color = _colorCtorRgba.Invoke(new object[] { 255, 255, 255, (int)alpha });

                    // Find Draw(Texture2D, Rectangle dest, Rectangle? source, Color)
                    var texture2dType = texture.GetType();
                    var drawWithSource = _spriteBatchType.GetMethods()
                        .FirstOrDefault(m =>
                            m.Name == "Draw" &&
                            m.GetParameters().Length == 4 &&
                            m.GetParameters()[0].ParameterType == texture2dType &&
                            m.GetParameters()[1].ParameterType == _rectangleType);

                    if (drawWithSource != null)
                    {
                        // Make source nullable
                        var nullableRectType = typeof(Nullable<>).MakeGenericType(_rectangleType);
                        var nullableSource = Activator.CreateInstance(nullableRectType, sourceRect);

                        drawWithSource.Invoke(_spriteBatch, new object[] { texture, destRect, nullableSource, color });
                    }
                    else
                    {
                        // Ultimate fallback: Draw with just dest rect and color
                        _drawTextureRect?.Invoke(_spriteBatch, new object[] { texture, destRect, color });
                    }
                }
            }
            catch (Exception ex)
            {
                // Log once per session to avoid spam
                if (!_itemDrawingFailed)
                {
                    _log?.Warn($"[UI] DrawItem failed for type {itemType}: {ex.Message}");
                }
            }
        }

        private static bool InitItemDrawing()
        {
            if (_itemDrawingInitialized) return !_itemDrawingFailed;
            if (_itemDrawingFailed) return false;

            try
            {
                // Get TextureAssets.Item array
                var textureAssetsType = typeof(Terraria.Main).Assembly.GetType("Terraria.GameContent.TextureAssets");
                if (textureAssetsType == null)
                {
                    _itemDrawingFailed = true;
                    _log?.Warn("[UI] TextureAssets type not found");
                    return false;
                }

                var itemField = textureAssetsType.GetField("Item", BindingFlags.Public | BindingFlags.Static);
                if (itemField == null)
                {
                    _itemDrawingFailed = true;
                    _log?.Warn("[UI] TextureAssets.Item field not found");
                    return false;
                }

                _itemTextureAssets = itemField.GetValue(null);
                if (_itemTextureAssets == null)
                {
                    _log?.Warn("[UI] TextureAssets.Item is null (textures not loaded yet)");
                    return false;
                }

                // Get Asset<Texture2D>.Value property and loading methods
                var itemArray = _itemTextureAssets as Array;
                if (itemArray != null && itemArray.Length > 0)
                {
                    var firstAsset = itemArray.GetValue(1); // Index 0 might be null/empty
                    if (firstAsset != null)
                    {
                        _assetType = firstAsset.GetType();
                        _assetValueProp = _assetType.GetProperty("Value");

                        // Get IsLoaded property to check if texture is ready
                        _assetIsLoadedProp = _assetType.GetProperty("IsLoaded");

                        // Get Request method to trigger lazy loading
                        // Try different possible method names
                        _assetRequestMethod = _assetType.GetMethod("Request", BindingFlags.Public | BindingFlags.Instance);
                        if (_assetRequestMethod == null)
                            _assetRequestMethod = _assetType.GetMethod("Wait", BindingFlags.Public | BindingFlags.Instance);
                        if (_assetRequestMethod == null)
                        {
                            // Try getting the value - this should trigger loading as a side effect
                            // Some Asset implementations load on first access
                        }

                    }
                }

                if (_assetValueProp == null)
                {
                    _itemDrawingFailed = true;
                    _log?.Warn("[UI] Asset.Value property not found");
                    return false;
                }

                _itemDrawingInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                _itemDrawingFailed = true;
                _log?.Error($"[UI] InitItemDrawing failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Texture Loading & Drawing

        private static MethodInfo _fromStreamMethod;
        private static bool _textureLoadingInitialized;
        private static bool _textureLoadingFailed;
        private static readonly Dictionary<string, object> _textureCache = new Dictionary<string, object>();

        /// <summary>
        /// Load a PNG file as a Texture2D. Returns cached texture on subsequent calls.
        /// Returns null if file not found or loading fails.
        /// </summary>
        public static object LoadTexture(string pngPath)
        {
            if (string.IsNullOrEmpty(pngPath)) return null;
            if (!EnsureInitialized()) return null;

            // Check cache first
            if (_textureCache.TryGetValue(pngPath, out var cached))
                return cached;

            if (!InitTextureLoading()) return null;

            if (!System.IO.File.Exists(pngPath))
            {
                _log?.Warn($"[UI] Texture not found: {pngPath}");
                return null;
            }

            try
            {
                object texture;
                using (var stream = System.IO.File.OpenRead(pngPath))
                {
                    texture = _fromStreamMethod.Invoke(null, new object[] { _graphicsDevice, stream });
                }

                if (texture == null)
                {
                    _log?.Warn($"[UI] FromStream returned null for {pngPath}");
                    return null;
                }

                _textureCache[pngPath] = texture;
                _log?.Debug($"[UI] Loaded texture: {System.IO.Path.GetFileName(pngPath)}");
                return texture;
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                _log?.Warn($"[UI] Failed to load texture {pngPath}: {msg}");
                return null;
            }
        }

        /// <summary>
        /// Draw a Texture2D (loaded via LoadTexture) at the specified position.
        /// Maintains aspect ratio within the given width/height bounds.
        /// </summary>
        public static void DrawTexture(object texture, int x, int y, int width, int height, byte alpha = 255)
        {
            if (texture == null) return;
            if (!EnsureInitialized()) return;
            if (_drawTextureRect == null || _spriteBatch == null) return;

            try
            {
                // Get texture dimensions
                var texType = texture.GetType();
                var widthProp = texType.GetProperty("Width");
                var heightProp = texType.GetProperty("Height");
                if (widthProp == null || heightProp == null) return;

                int texWidth = (int)widthProp.GetValue(texture);
                int texHeight = (int)heightProp.GetValue(texture);
                if (texWidth <= 0 || texHeight <= 0) return;

                // Calculate scale to fit within bounds while maintaining aspect ratio
                float scaleX = (float)width / texWidth;
                float scaleY = (float)height / texHeight;
                float scale = Math.Min(scaleX, scaleY);

                int drawWidth = (int)(texWidth * scale);
                int drawHeight = (int)(texHeight * scale);

                // Center within target area
                int drawX = x + (width - drawWidth) / 2;
                int drawY = y + (height - drawHeight) / 2;

                var destRect = _rectangleCtor.Invoke(new object[] { drawX, drawY, drawWidth, drawHeight });
                var color = _colorCtorRgba.Invoke(new object[] { 255, 255, 255, (int)alpha });

                _drawTextureRect.Invoke(_spriteBatch, new object[] { texture, destRect, color });
            }
            catch (Exception ex)
            {
                _log?.Warn($"[UI] DrawTexture failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static bool InitTextureLoading()
        {
            if (_textureLoadingInitialized) return !_textureLoadingFailed;
            if (_textureLoadingFailed) return false;

            try
            {
                // Find Texture2D type
                Type texture2dType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    texture2dType = asm.GetType("Microsoft.Xna.Framework.Graphics.Texture2D");
                    if (texture2dType != null) break;
                }
                if (texture2dType == null) { _textureLoadingFailed = true; return false; }

                // Find GraphicsDevice type
                var gdType = texture2dType.Assembly.GetType("Microsoft.Xna.Framework.Graphics.GraphicsDevice");
                if (gdType == null) { _textureLoadingFailed = true; return false; }

                // Find FromStream(GraphicsDevice, Stream)
                _fromStreamMethod = texture2dType.GetMethod("FromStream",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { gdType, typeof(System.IO.Stream) }, null);
                if (_fromStreamMethod == null) { _textureLoadingFailed = true; return false; }

                // Get GraphicsDevice from Main
                var mainType = typeof(Terraria.Main);
                var graphicsProp = mainType.GetProperty("graphics", BindingFlags.Public | BindingFlags.Static);
                if (graphicsProp != null)
                {
                    var gm = graphicsProp.GetValue(null);
                    if (gm != null)
                    {
                        var gdProp = gm.GetType().GetProperty("GraphicsDevice");
                        _graphicsDevice = gdProp?.GetValue(gm);
                    }
                }

                if (_graphicsDevice == null)
                {
                    var instField = mainType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                    var inst = instField?.GetValue(null);
                    if (inst != null)
                    {
                        var gdProp = inst.GetType().GetProperty("GraphicsDevice");
                        _graphicsDevice = gdProp?.GetValue(inst);
                    }
                }

                if (_graphicsDevice == null) return false; // Not ready yet, retry later

                _textureLoadingInitialized = true;
                _log?.Info("[UI] Texture loading initialized");
                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"[UI] InitTextureLoading failed: {ex.Message}");
                _textureLoadingFailed = true;
                return false;
            }
        }

        #endregion

        #endregion

        #region Input

        /// <summary>
        /// Mouse X position. Reads directly from Main.mouseX.
        /// </summary>
        public static int MouseX
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _mouseXField != null ? (int)_mouseXField.GetValue(null) : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Mouse Y position. Reads directly from Main.mouseY.
        /// </summary>
        public static int MouseY
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _mouseYField != null ? (int)_mouseYField.GetValue(null) : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Whether left mouse button is currently held.
        /// </summary>
        public static bool MouseLeft
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _mouseLeftField != null && (bool)_mouseLeftField.GetValue(null);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether left mouse was just clicked (pressed && released).
        /// </summary>
        public static bool MouseLeftClick
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    if (_mouseLeftField == null || _mouseLeftReleaseField == null) return false;
                    return (bool)_mouseLeftField.GetValue(null) && (bool)_mouseLeftReleaseField.GetValue(null);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether right mouse button is currently held.
        /// </summary>
        public static bool MouseRight
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _mouseRightField != null && (bool)_mouseRightField.GetValue(null);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether right mouse was just clicked (pressed && released).
        /// </summary>
        public static bool MouseRightClick
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    if (_mouseRightField == null || _mouseRightReleaseField == null) return false;
                    return (bool)_mouseRightField.GetValue(null) && (bool)_mouseRightReleaseField.GetValue(null);
                }
                catch { return false; }
            }
        }

        public static bool MouseMiddle
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _mouseMiddleField != null && (bool)_mouseMiddleField.GetValue(null);
                }
                catch { return false; }
            }
        }

        public static bool MouseMiddleClick
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    if (_mouseMiddleField == null || _mouseMiddleReleaseField == null) return false;
                    return (bool)_mouseMiddleField.GetValue(null) && (bool)_mouseMiddleReleaseField.GetValue(null);
                }
                catch { return false; }
            }
        }

        public static int ScreenWidth
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _screenWidthField != null ? (int)_screenWidthField.GetValue(null) : 1920;
                }
                catch { return 1920; }
            }
        }

        public static int ScreenHeight
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    return _screenHeightField != null ? (int)_screenHeightField.GetValue(null) : 1080;
                }
                catch { return 1080; }
            }
        }
        public static int ScrollWheel
        {
            get
            {
                // When blocking, use the captured value (before we cleared it)
                if (IsBlocking && _capturedScrollValue != 0)
                {
                    return _capturedScrollValue;
                }

                try
                {
                    if (_scrollWheelDeltaForUIField != null)
                        return (int)_scrollWheelDeltaForUIField.GetValue(null);
                }
                catch { }
                return 0;
            }
        }

        // Track which panels are currently requesting blocking (for robustness with multiple modals)
        private static readonly HashSet<string> _blockingPanels = new HashSet<string>();
        private static bool _preUpdateSubscribed;
        private static bool _patchesApplied;

        // Track which panels need keyboard input blocked (text fields, keybind capture, etc.)
        // Set-based to prevent "last writer wins" conflicts between multiple panels
        private static readonly HashSet<string> _keyInputBlockers = new HashSet<string>();

        /// <summary>
        /// True if any panel is requesting mouse blocking.
        /// Derived from _blockingPanels count for robustness with multiple modals.
        /// </summary>
        public static bool IsBlocking => _blockingPanels.Count > 0;

        /// <summary>
        /// True if any panel is waiting for keyboard input (text fields, keybind capture).
        /// When true, player controls are cleared and hotbar keys blocked.
        /// </summary>
        public static bool IsWaitingForKeyInput
        {
            get => _keyInputBlockers.Count > 0;
            set
            {
                // Legacy setter for backwards compatibility - uses a generic key
                if (value)
                    _keyInputBlockers.Add("_legacy");
                else
                    _keyInputBlockers.Remove("_legacy");
            }
        }

        /// <summary>
        /// Register a panel that needs keyboard input blocked (e.g., text field focused).
        /// Use a unique panelId per UI (e.g., "debug-console", "item-spawner-search").
        /// </summary>
        public static void RegisterKeyInputBlock(string panelId)
        {
            _keyInputBlockers.Add(panelId);
        }

        /// <summary>
        /// Unregister a panel's keyboard input block request.
        /// Keyboard is freed when no panels need it blocked.
        /// </summary>
        public static void UnregisterKeyInputBlock(string panelId)
        {
            _keyInputBlockers.Remove(panelId);
        }

        /// <summary>
        /// Set Terraria's mouse blocking flags directly.
        /// When true, prevents clicking on game world items.
        /// </summary>
        public static void SetMouseBlocking(bool block)
        {
            try
            {
                _blockMouseField?.SetValue(null, block);

                var playerArray = _playerArrayField?.GetValue(null) as Array;
                if (_myPlayerField != null && playerArray != null)
                {
                    int myPlayer = (int)_myPlayerField.GetValue(null);
                    var player = playerArray.GetValue(myPlayer);
                    _mouseInterfaceField?.SetValue(player, block);
                }
            }
            catch { }
        }

        /// <summary>
        /// Legacy method for backwards compatibility. Use RegisterPanelBounds/UnregisterPanelBounds instead.
        /// </summary>
        public static void BlockMouseOnly(bool block)
        {
            SetMouseBlocking(block);
            EnsurePatchesApplied();
        }

        /// <summary>
        /// Apply Harmony patches and subscribe to frame events lazily.
        /// Called once when the first panel registers.
        /// </summary>
        private static void EnsurePatchesApplied()
        {
            if (_patchesApplied) return;

            try
            {
                if (!_scrollPatchApplied)
                    ApplyScrollBlockPatch();
                if (!_ignoreMousePatchApplied)
                    ApplyIgnoreMouseInterfacePatch();
                if (!_inventoryScrollPatchApplied)
                    ApplyInventoryScrollPatch();
                if (!_copyIntoPatchApplied)
                    ApplyCopyIntoPatch();
                if (!_preUpdateSubscribed)
                {
                    TerrariaModder.Core.Events.FrameEvents.OnPreUpdate += OnPreUpdateConsumeScroll;
                    _preUpdateSubscribed = true;
                }

                _patchesApplied = true;
            }
            catch { }
        }

        /// <summary>
        /// Update Terraria's blocking flags based on current panel registration state.
        /// Called automatically when panels are registered/unregistered.
        /// </summary>
        private static void UpdateBlockingState()
        {
            if (_blockingPanels.Count > 0)
            {
                EnsurePatchesApplied();
            }
            else
            {
                // No panels open — explicitly clear blocking flags
                SetMouseBlocking(false);
            }
        }

        /// <summary>
        /// Block keyboard input (for text fields, keybind capture, etc.).
        /// Call this in addition to BlockMouseOnly when user is typing.
        /// </summary>
        public static void BlockKeyboardInput(bool block)
        {
            try
            {
                _blockInputField?.SetValue(null, block);
                _mainPlayerInputWritingTextField?.SetValue(null, block);

                var playerArray = _playerArrayField?.GetValue(null) as Array;
                if (_myPlayerField != null && playerArray != null)
                {
                    int myPlayer = (int)_myPlayerField.GetValue(null);
                    var player = playerArray.GetValue(myPlayer);

                    if (block && player != null)
                    {
                        _controlUpField?.SetValue(player, false);
                        _controlDownField?.SetValue(player, false);
                        _controlLeftField?.SetValue(player, false);
                        _controlRightField?.SetValue(player, false);
                        _controlJumpField?.SetValue(player, false);
                        _controlUseItemField?.SetValue(player, false);
                        _controlUseTileField?.SetValue(player, false);
                        _controlThrowField?.SetValue(player, false);
                        _controlHookField?.SetValue(player, false);
                        _controlMountField?.SetValue(player, false);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Block all input (mouse + keyboard). Use when user is actively typing in a text field.
        /// </summary>
        public static void BlockInput(bool block)
        {
            BlockMouseOnly(block);
            BlockKeyboardInput(block);
        }

        /// <summary>
        /// Called during Update phase (prefix on Main.DoUpdate) — fires BEFORE mouse position is updated.
        /// IMPORTANT: Mouse position (Main.mouseX/mouseY) is stale here (from previous frame).
        /// Mouse-position-based blocking is done in PlayerUpdateScrollBlockPrefix instead,
        /// which fires after DoUpdate_HandleInput updates the mouse coordinates.
        /// This method only handles: clearing blockMouse and keyboard blocking.
        /// </summary>
        private static void OnPreUpdateConsumeScroll()
        {
            // Reset captured scroll and consume stale scroll from previous frame.
            _capturedScrollValue = 0;
            if (IsBlocking)
                ConsumeScroll();

            // Block keyboard when any panel needs text/key input
            if (IsWaitingForKeyInput)
            {
                // Set WritingText early to block hotbar keys BEFORE PlayerInput.UpdateInput
                try
                {
                    _mainPlayerInputWritingTextField?.SetValue(null, true);
                }
                catch { }

                BlockKeyboardInput(true);
            }
            else
            {
                // No text input needed — explicitly free keyboard
                // (clears WritingText, blockInput that may be stale from previous frame)
                BlockKeyboardInput(false);
            }
        }

        /// <summary>
        /// Consume the current click so subsequent checks return false.
        /// Call after handling a click to prevent double-processing.
        /// </summary>
        public static void ConsumeClick()
        {
            try { _mouseLeftReleaseField?.SetValue(null, false); } catch { }
        }

        /// <summary>
        /// Set player.mouseInterface = true during Draw phase when mouse is over any registered panel.
        /// This feeds into Terraria's lastMouseInterface next frame, suppressing sign tooltips,
        /// smart cursor hover, and other world hover effects.
        /// Safe to call during Draw (Terraria's own UI layers set mouseInterface the same way).
        /// </summary>
        public static void SetMouseInterfaceIfOverPanel()
        {
            if (!IsBlocking) return;
            if (!IsMouseOverAnyPanel()) return;

            try
            {
                var playerArray = _playerArrayField?.GetValue(null) as Array;
                if (_myPlayerField != null && playerArray != null)
                {
                    int myPlayer = (int)_myPlayerField.GetValue(null);
                    var player = playerArray.GetValue(myPlayer);
                    _mouseInterfaceField?.SetValue(player, true);
                }
            }
            catch { }
        }

        #region Inventory Control

        private static FieldInfo _playerInventoryField;

        /// <summary>
        /// Open the player's inventory.
        /// </summary>
        public static void OpenInventory()
        {
            try
            {
                if (_playerInventoryField == null)
                    _playerInventoryField = typeof(Terraria.Main).GetField("playerInventory", BindingFlags.Public | BindingFlags.Static);
                _playerInventoryField?.SetValue(null, true);
            }
            catch { }
        }

        /// <summary>
        /// Close the player's inventory.
        /// </summary>
        public static void CloseInventory()
        {
            try
            {
                if (_playerInventoryField == null)
                    _playerInventoryField = typeof(Terraria.Main).GetField("playerInventory", BindingFlags.Public | BindingFlags.Static);
                _playerInventoryField?.SetValue(null, false);
            }
            catch { }
        }

        /// <summary>
        /// Check if the player's inventory is open.
        /// </summary>
        public static bool IsInventoryOpen
        {
            get
            {
                try
                {
                    if (_playerInventoryField == null)
                        _playerInventoryField = typeof(Terraria.Main).GetField("playerInventory", BindingFlags.Public | BindingFlags.Static);
                    return _playerInventoryField != null && (bool)_playerInventoryField.GetValue(null);
                }
                catch { return false; }
            }
        }

        #endregion

        // Track panel bounds for click-through prevention
        // Bounds persist across frames until UI closes (calls UnregisterPanelBounds)
        private static readonly Dictionary<string, (int x, int y, int w, int h)> _registeredPanels = new Dictionary<string, (int, int, int, int)>();

        // Z-order draw system — panels draw back-to-front, clicking brings to front
        private static readonly List<string> _panelZOrder = new List<string>();
        private static readonly Dictionary<string, Action> _panelDrawCallbacks = new Dictionary<string, Action>();

        /// <summary>
        /// Register a panel's draw callback for z-ordered drawing.
        /// New panels start on top (end of z-order list).
        /// Call once during mod initialization, not every frame.
        /// </summary>
        public static void RegisterPanelDraw(string panelId, Action drawCallback)
        {
            _panelDrawCallbacks[panelId] = drawCallback;
            if (!_panelZOrder.Contains(panelId))
                _panelZOrder.Add(panelId);
        }

        /// <summary>
        /// Unregister a panel's draw callback.
        /// </summary>
        public static void UnregisterPanelDraw(string panelId)
        {
            _panelDrawCallbacks.Remove(panelId);
            _panelZOrder.Remove(panelId);
        }

        /// <summary>
        /// Bring a panel to the front of the z-order (draws last, gets clicks first).
        /// </summary>
        public static void BringToFront(string panelId)
        {
            if (_panelZOrder.Remove(panelId))
                _panelZOrder.Add(panelId);
        }

        /// <summary>
        /// Draw all registered panels in z-order (back to front).
        /// Auto-detects focus clicks: if user clicks on a panel, it's brought to front.
        /// Called from DrawCursor_Prefix during Draw phase (UI-space coordinates).
        /// </summary>
        public static void DrawAllPanels()
        {
            if (_panelDrawCallbacks.Count == 0) return;

            // Safety: skip drawing if SpriteBatch hasn't had Begin() called.
            // On the title screen, DrawCursor_Prefix can fire before SpriteBatch.Begin,
            // and every DrawRect/DrawText would throw an exception (causing massive lag).
            if (_spriteBatch != null && _spriteBatchBeginCalled != null)
            {
                try
                {
                    bool beginCalled = (bool)_spriteBatchBeginCalled.GetValue(_spriteBatch);
                    if (!beginCalled) return;
                }
                catch { return; }
            }

            // Auto-focus: if user clicked on a panel, bring it to front
            if (MouseLeftClick)
            {
                // Walk z-order back-to-front to find topmost panel under mouse
                for (int i = _panelZOrder.Count - 1; i >= 0; i--)
                {
                    string id = _panelZOrder[i];
                    if (_registeredPanels.TryGetValue(id, out var bounds))
                    {
                        var (x, y, w, h) = bounds;
                        if (MouseX >= x && MouseX < x + w && MouseY >= y && MouseY < y + h)
                        {
                            // Already on top — no change needed
                            if (i < _panelZOrder.Count - 1)
                                BringToFront(id);
                            break;
                        }
                    }
                }
            }

            // Draw panels in z-order (back to front)
            // Copy list to avoid modification during iteration
            var order = new List<string>(_panelZOrder);
            foreach (var panelId in order)
            {
                if (_panelDrawCallbacks.TryGetValue(panelId, out var callback))
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception ex)
                    {
                        _log?.Error($"[UI] Error drawing panel '{panelId}': {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Register a UI panel's bounds for click-through prevention.
        /// Call this when your UI is visible. Bounds persist until UnregisterPanelBounds is called.
        /// Use a unique panelId per UI (e.g., "mod-menu", "item-spawner", "storage-hub").
        /// Automatically enables mouse blocking when first panel is registered.
        /// </summary>
        public static void RegisterPanelBounds(string panelId, int x, int y, int width, int height)
        {
            _registeredPanels[panelId] = (x, y, width, height);
            _blockingPanels.Add(panelId);
            UpdateBlockingState();
        }

        /// <summary>
        /// Simplified overload - auto-generates panel ID from caller location.
        /// Just pass position and size.
        /// </summary>
        public static void RegisterPanelBounds(int x, int y, int width, int height)
        {
            // Use a simple hash as ID - this works because each UI only has one panel
            string id = $"panel_{x}_{y}_{width}_{height}";
            _registeredPanels[id] = (x, y, width, height);
            _blockingPanels.Add(id);
            UpdateBlockingState();
        }

        /// <summary>
        /// Unregister a panel. Call when UI closes.
        /// Automatically disables mouse blocking when last panel is unregistered.
        /// </summary>
        public static void UnregisterPanelBounds(string panelId)
        {
            _registeredPanels.Remove(panelId);
            _blockingPanels.Remove(panelId);
            UpdateBlockingState();
        }

        /// <summary>
        /// Clear all panel registrations.
        /// </summary>
        public static void ClearAllPanelBounds()
        {
            _registeredPanels.Clear();
            _blockingPanels.Clear();
            UpdateBlockingState();
        }

        /// <summary>
        /// Get the current UI scale factor. During Update phase, Main.mouseX/Y are in screen-space
        /// but panel bounds are in UI-space (pre-scale). We need to divide mouse coords by UIScale
        /// to compare correctly.
        /// </summary>
        private static float GetUIScale()
        {
            try
            {
                if (_uiScaleField != null)
                    return (float)_uiScaleField.GetValue(null);
            }
            catch { }
            return 1f;
        }

        /// <summary>
        /// Get raw hardware mouse position from PlayerInput.MouseX/MouseY.
        /// Unlike Main.mouseX/mouseY, these are NEVER transformed by SetZoom_World/SetZoom_UI.
        /// Use this in Update-phase patches where Main.mouseX is in world-space.
        /// Falls back to Main.mouseX/mouseY if PlayerInput fields aren't available.
        /// </summary>
        private static (int x, int y) GetRawMousePosition()
        {
            try
            {
                if (_playerInputMouseXField != null && _playerInputMouseYField != null)
                {
                    return ((int)_playerInputMouseXField.GetValue(null),
                            (int)_playerInputMouseYField.GetValue(null));
                }
            }
            catch { }
            // Fallback — may be wrong during Update if zoom transforms are active
            return (MouseX, MouseY);
        }

        /// <summary>
        /// Get mouse position in UI-space coordinates (accounting for UIScale).
        /// Uses PlayerInput.MouseX/MouseY (raw hardware coords) to avoid zoom transform issues.
        /// Panel bounds are registered in UI-space (drawing coordinates = rawPixel / UIScale).
        /// </summary>
        private static (int x, int y) GetUISpaceMouse()
        {
            float scale = GetUIScale();
            if (scale <= 0f) scale = 1f;
            var (rawX, rawY) = GetRawMousePosition();
            return ((int)(rawX / scale), (int)(rawY / scale));
        }

        /// <summary>
        /// Check if mouse is over any registered panel.
        /// </summary>
        public static bool IsMouseOverAnyPanel()
        {
            if (_registeredPanels.Count == 0) return false;

            int mx = MouseX;
            int my = MouseY;

            foreach (var kvp in _registeredPanels)
            {
                var (x, y, w, h) = kvp.Value;
                if (mx >= x && mx < x + w && my >= y && my < y + h)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if mouse is over any registered panel, using raw hardware mouse coords + UIScale.
        /// During Update phase, Main.mouseX/mouseY are in world-space (after SetZoom_World),
        /// NOT raw screen-space. We must read PlayerInput.MouseX/MouseY (raw hardware coords)
        /// and divide by UIScale to get UI-space coordinates matching panel bounds.
        /// Draw-phase callers should use IsMouseOverAnyPanel() instead (coords already UI-space).
        /// </summary>
        private static bool IsMouseOverAnyPanelUpdatePhase()
        {
            if (_registeredPanels.Count == 0) return false;

            var (mx, my) = GetUISpaceMouse();

            foreach (var kvp in _registeredPanels)
            {
                var (x, y, w, h) = kvp.Value;
                if (mx >= x && mx < x + w && my >= y && my < y + h)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if mouse is over a specific registered panel.
        /// </summary>
        public static bool IsMouseOverPanel(string panelId)
        {
            if (!_registeredPanels.TryGetValue(panelId, out var bounds))
                return false;

            int mx = MouseX;
            int my = MouseY;
            var (x, y, w, h) = bounds;
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        /// <summary>
        /// Check if a higher-z-order panel should block input for this panel.
        /// Returns true if any panel above myPanelId in the z-order has its bounds under the mouse.
        /// </summary>
        public static bool ShouldBlockForHigherPriorityPanel(string myPanelId)
        {
            int myIndex = _panelZOrder.IndexOf(myPanelId);
            if (myIndex < 0) return false; // Not in z-order, don't block

            // Check all panels above us in z-order
            for (int i = myIndex + 1; i < _panelZOrder.Count; i++)
            {
                string otherId = _panelZOrder[i];
                if (_registeredPanels.ContainsKey(otherId) && IsMouseOverPanel(otherId))
                    return true;
            }

            return false;
        }

        #region ItemSlot Click-Through Prevention

        // Throttle logging to avoid spam (log once per second max)
        private static DateTime _lastItemSlotBlockLog = DateTime.MinValue;
        private static int _itemSlotBlockCount = 0;

        /// <summary>
        /// Check if ItemSlot interactions should be blocked because a modal UI is open
        /// and the mouse is over it. Used by the ItemSlot.Handle Harmony patch.
        /// </summary>
        /// <returns>True if ItemSlot should be blocked, false to allow normal behavior</returns>
        public static bool ShouldBlockItemSlot()
        {
            // Quick check - if no panels are blocking, allow everything
            if (!IsBlocking)
                return false;

            // Check if mouse is over any of our modal panels
            if (!IsMouseOverAnyPanel())
                return false;

            // We should block - log it (throttled)
            _itemSlotBlockCount++;
            var now = DateTime.Now;
            if ((now - _lastItemSlotBlockLog).TotalSeconds >= 1.0)
            {
                _log?.Info($"[UI] ItemSlot blocked {_itemSlotBlockCount}x in last second (mouse over modal panel)");
                _lastItemSlotBlockLog = now;
                _itemSlotBlockCount = 0;
            }

            return true;
        }

        /// <summary>
        /// Called when the ItemSlot patch is first applied. Logs success.
        /// </summary>
        public static void LogItemSlotPatchApplied()
        {
            _log?.Info("[UI] ItemSlot.Handle patch applied - inventory click-through prevention active");
        }

        #endregion

        /// <summary>
        /// Consume scroll wheel input to prevent it from reaching the game.
        /// </summary>
        public static void ConsumeScroll()
        {
            try
            {
                _scrollWheelDeltaForUIField?.SetValue(null, 0);
                _scrollWheelDeltaField?.SetValue(null, 0);
            }
            catch { }
        }

        /// <summary>
        /// Consume right-click input to prevent it from reaching the game.
        /// </summary>
        public static void ConsumeRightClick()
        {
            try { _mouseRightReleaseField?.SetValue(null, false); } catch { }
        }

        /// <summary>
        /// Consume middle-click input to prevent it from reaching the game.
        /// </summary>
        public static void ConsumeMiddleClick()
        {
            try { _mouseMiddleReleaseField?.SetValue(null, false); } catch { }
        }

        public static bool IsMouseOver(int x, int y, int width, int height)
        {
            int mx = MouseX, my = MouseY;
            return mx >= x && mx < x + width && my >= y && my < y + height;
        }

        #endregion

        #region Text Input

        private static FieldInfo _writingTextField;
        private static PropertyInfo _writingTextProp;
        private static FieldInfo _currentInputTextTakerOverrideField;
        private static MethodInfo _handleIMEMethod;
        private static FieldInfo _mainInstanceField;
        private static MethodInfo _getInputTextMethod;
        private static MethodInfo _clrInputMethod;
        private static FieldInfo _inputTextField;
        private static object _inputTextTaker; // Persistent object for CurrentInputTextTakerOverride

        /// <summary>
        /// Enable text input mode. Call this when a text field is focused.
        /// Must be called in BOTH Update and Draw phases.
        /// </summary>
        public static void EnableTextInput()
        {
            try
            {
                var mainType = typeof(Terraria.Main);
                var playerInputType = mainType.Assembly.GetType("Terraria.GameInput.PlayerInput");

                // Set PlayerInput.WritingText = true
                if (_writingTextField == null && _writingTextProp == null && playerInputType != null)
                {
                    _writingTextField = playerInputType.GetField("WritingText", BindingFlags.Public | BindingFlags.Static);
                    if (_writingTextField == null)
                        _writingTextProp = playerInputType.GetProperty("WritingText", BindingFlags.Public | BindingFlags.Static);
                }

                if (_writingTextField != null)
                    _writingTextField.SetValue(null, true);
                else if (_writingTextProp != null)
                    _writingTextProp.SetValue(null, true);

                // Set Main.CurrentInputTextTakerOverride to prevent chat from stealing input
                if (_currentInputTextTakerOverrideField == null)
                    _currentInputTextTakerOverrideField = mainType.GetField("CurrentInputTextTakerOverride", BindingFlags.Public | BindingFlags.Static);

                if (_currentInputTextTakerOverrideField != null)
                {
                    // Use a persistent object (not string) for the override
                    if (_inputTextTaker == null)
                        _inputTextTaker = new object();
                    _currentInputTextTakerOverrideField.SetValue(null, _inputTextTaker);
                }
            }
            catch { }
        }

        /// <summary>
        /// Disable text input mode. Call this when text field loses focus or UI closes.
        /// </summary>
        public static void DisableTextInput()
        {
            try
            {
                if (_writingTextField != null)
                    _writingTextField.SetValue(null, false);
                else if (_writingTextProp != null)
                    _writingTextProp.SetValue(null, false);

                _currentInputTextTakerOverrideField?.SetValue(null, null);
            }
            catch { }
        }

        /// <summary>
        /// Call before GetInputText to handle IME input properly.
        /// </summary>
        public static void HandleIME()
        {
            try
            {
                var mainType = typeof(Terraria.Main);

                if (_mainInstanceField == null)
                    _mainInstanceField = mainType.GetField("instance", BindingFlags.Public | BindingFlags.Static);

                if (_handleIMEMethod == null)
                    _handleIMEMethod = mainType.GetMethod("HandleIME", BindingFlags.Public | BindingFlags.Instance);

                if (_mainInstanceField != null && _handleIMEMethod != null)
                {
                    var instance = _mainInstanceField.GetValue(null);
                    if (instance != null)
                        _handleIMEMethod.Invoke(instance, null);
                }
            }
            catch { }
        }

        /// <summary>
        /// Clear input state. Call when starting to take text input.
        /// </summary>
        public static void ClearInput()
        {
            try
            {
                if (_clrInputMethod == null)
                    _clrInputMethod = typeof(Terraria.Main).GetMethod("clrInput", BindingFlags.Public | BindingFlags.Static);
                _clrInputMethod?.Invoke(null, null);
            }
            catch { }
        }

        private static FieldInfo _inputTextEscapeField;

        /// <summary>
        /// Check if Escape was pressed during text input. Clears the flag after checking.
        /// </summary>
        public static bool CheckInputEscape()
        {
            try
            {
                if (_inputTextEscapeField == null)
                    _inputTextEscapeField = typeof(Terraria.Main).GetField("inputTextEscape", BindingFlags.Public | BindingFlags.Static);

                if (_inputTextEscapeField != null)
                {
                    bool result = (bool)_inputTextEscapeField.GetValue(null);
                    if (result)
                        _inputTextEscapeField.SetValue(null, false); // Clear the flag
                    return result;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Get keyboard input text. Call during Draw phase after EnableTextInput and HandleIME.
        /// </summary>
        public static string GetInputText(string currentText)
        {
            try
            {
                var mainType = typeof(Terraria.Main);

                // Make sure we have override set
                if (_currentInputTextTakerOverrideField == null)
                    _currentInputTextTakerOverrideField = mainType.GetField("CurrentInputTextTakerOverride", BindingFlags.Public | BindingFlags.Static);

                if (_currentInputTextTakerOverrideField != null && _currentInputTextTakerOverrideField.GetValue(null) != null)
                {
                    // Try Main.GetInputText(string, bool) first
                    if (_getInputTextMethod == null)
                    {
                        _getInputTextMethod = mainType.GetMethod("GetInputText", BindingFlags.Public | BindingFlags.Static,
                            null, new[] { typeof(string), typeof(bool) }, null);
                        if (_getInputTextMethod == null)
                        {
                            _getInputTextMethod = mainType.GetMethod("GetInputText", BindingFlags.Public | BindingFlags.Static,
                                null, new[] { typeof(string) }, null);
                        }
                    }

                    if (_getInputTextMethod != null)
                    {
                        var parms = _getInputTextMethod.GetParameters();
                        string result;
                        if (parms.Length == 2)
                            result = (string)_getInputTextMethod.Invoke(null, new object[] { currentText, false });
                        else
                            result = (string)_getInputTextMethod.Invoke(null, new object[] { currentText });

                        if (result != null)
                            return result;
                    }

                    // Fallback to Main.inputText field
                    if (_inputTextField == null)
                        _inputTextField = mainType.GetField("inputText", BindingFlags.Public | BindingFlags.Static);

                    if (_inputTextField != null)
                    {
                        string inputText = (string)_inputTextField.GetValue(null);
                        if (inputText != null && inputText != currentText)
                            return inputText;
                    }
                }
            }
            catch { }
            return currentText ?? "";
        }

        #endregion
    }
}
