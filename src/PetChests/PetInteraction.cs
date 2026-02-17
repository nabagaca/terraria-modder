using System;
using System.Reflection;

namespace PetChests
{
    /// <summary>
    /// Handles pet interaction - right-click to open/close piggy bank.
    /// </summary>
    public static class PetInteraction
    {
        // Cached reflection for Main static fields
        private static bool _initialized = false;
        private static FieldInfo _hadInteractableField;
        private static FieldInfo _mouseRightField;
        private static FieldInfo _playerInventoryField;
        private static FieldInfo _stackSplitField;
        private static PropertyInfo _mouseScreenProp;
        private static FieldInfo _screenPositionField;
        private static FieldInfo _maxProjectilesField;
        private static FieldInfo _projectileArrayField;

        // Player fields
        private static FieldInfo _playerPositionField;
        private static FieldInfo _playerWidthField;
        private static FieldInfo _playerHeightField;
        private static FieldInfo _playerChestField;
        private static FieldInfo _playerChestXField;
        private static FieldInfo _playerChestYField;
        private static FieldInfo _playerWhoAmIField;
        private static FieldInfo _playerNoThrowField;
        private static FieldInfo _playerMouseInterfaceField;
        private static FieldInfo _playerControlUseItemField;
        private static FieldInfo _playerControlUseTileField;
        private static FieldInfo _playerReleaseUseItemField;
        private static FieldInfo _playerReleaseUseTileField;
        private static FieldInfo _playerItemAnimationField;
        private static FieldInfo _playerItemTimeField;
        private static FieldInfo _playerNoItemsField;
        private static MethodInfo _setTalkNPCMethod;
        private static FieldInfo _piggyBankTrackerField;
        private static MethodInfo _trackerClearMethod;

        // Additional Main fields for input blocking
        private static FieldInfo _mouseRightReleaseField;
        private static FieldInfo _blockInteractionField;  // Player.BlockInteractionWithProjectiles

        // Vector2 fields (XNA type)
        private static Type _vector2Type;
        private static FieldInfo _xField;
        private static FieldInfo _yField;

        // State
        private static bool _lastMouseRight = false;
        private static int _framesSinceLastInteraction = 9999;
        private const int INTERACTION_COOLDOWN = 15;
        private static bool _keepPiggyOpen = false;
        private static int _boundPetIndex = -1;
        private static int _closeCooldown = 0;  // Prevents immediate reopen after close
        private const int CLOSE_COOLDOWN_FRAMES = 10;
        private const float SOUND_MUTE_RANGE = 120f;  // Distance in pixels to mute projectile sounds
        private const int STACK_SPLIT_DELAY = 600;     // Delay before stack splitting is allowed
        private const int NO_THROW_VALUE = 4;          // Frames to prevent item throw after opening

        public static bool IsKeepingPiggyOpen() => _keepPiggyOpen;

        /// <summary>
        /// Returns true if pet should NOT be interactible (either open or just closed)
        /// </summary>
        public static bool ShouldBlockInteraction() => _keepPiggyOpen || _closeCooldown > 0;

        /// <summary>
        /// Block input in prefix (before vanilla processes) to prevent clicking sounds.
        /// Also handles right-click to close before blocking.
        /// </summary>
        public static void BlockInputInPrefix(object player)
        {
            if (!EnsureInitialized()) return;

            // If in close cooldown, still block input to prevent reopen
            if (_closeCooldown > 0)
            {
                BlockAllInput(player);
                return;
            }

            if (!_keepPiggyOpen) return;

            try
            {
                // First check if user right-clicked to close (BEFORE we block mouseRight)
                bool mouseRight = (bool)_mouseRightField.GetValue(null);
                if (mouseRight && !_lastMouseRight)
                {
                    // Rising edge - user just clicked
                    // Check if clicking on bound pet to close
                    if (_boundPetIndex >= 0 && CheckClickOnBoundPet(player))
                    {
                        ClosePiggyBank(player);
                        // Set to false so HandleInteraction doesn't see a false "mouse release"
                        // (we blocked mouseRight to false, so !false && true would trigger open)
                        _lastMouseRight = false;
                        // STILL block input after close to prevent vanilla reopen
                        BlockAllInput(player);
                        return;
                    }
                }
                _lastMouseRight = mouseRight;

                BlockAllInput(player);
            }
            catch { }
        }

        private static void BlockAllInput(object player)
        {
            try
            {
                // Block all use-related input before vanilla gets to process it
                _playerControlUseItemField?.SetValue(player, false);
                _playerControlUseTileField?.SetValue(player, false);
                _playerReleaseUseItemField?.SetValue(player, true);
                _playerReleaseUseTileField?.SetValue(player, true);
                _playerNoItemsField?.SetValue(player, true);
                _mouseRightField?.SetValue(null, false);
                _mouseRightReleaseField?.SetValue(null, false);

                // Block vanilla projectile interactions
                _blockInteractionField?.SetValue(null, 3);

                // Also mute projectiles in prefix to prevent sounds during Player.Update
                MuteNearbyProjectiles(player);
            }
            catch { }
        }

        private static bool CheckClickOnBoundPet(object player)
        {
            try
            {
                var projectiles = (Array)_projectileArrayField.GetValue(null);
                var boundPet = projectiles.GetValue(_boundPetIndex);
                if (!PetHelper.IsActive(boundPet)) return false;

                float mouseWorldX, mouseWorldY;
                if (!GetMouseWorldPosition(out mouseWorldX, out mouseWorldY)) return false;

                return PetHelper.IsPointInHitbox(boundPet, mouseWorldX, mouseWorldY);
            }
            catch
            {
                return false;
            }
        }

        public static void Reset()
        {
            _keepPiggyOpen = false;
            _boundPetIndex = -1;
            _lastMouseRight = false;
            _framesSinceLastInteraction = 9999;
            _closeCooldown = 0;
        }

        private static bool EnsureInitialized()
        {
            if (_initialized) return true;

            try
            {
                var mainType = Mod.MainType;
                var playerType = Mod.PlayerType;

                // Get Main.CurrentFrameFlags.HadAnActiveInteractableProjectile
                var flagsType = mainType.GetNestedType("CurrentFrameFlags", BindingFlags.Public);
                if (flagsType != null)
                {
                    _hadInteractableField = flagsType.GetField("HadAnActiveInteractableProjectile",
                        BindingFlags.Public | BindingFlags.Static);
                }

                // Main fields
                _mouseRightField = mainType.GetField("mouseRight", BindingFlags.Public | BindingFlags.Static);
                _playerInventoryField = mainType.GetField("playerInventory", BindingFlags.Public | BindingFlags.Static);
                _stackSplitField = mainType.GetField("stackSplit", BindingFlags.Public | BindingFlags.Static);
                _mouseScreenProp = mainType.GetProperty("MouseScreen", BindingFlags.Public | BindingFlags.Static);
                _screenPositionField = mainType.GetField("screenPosition", BindingFlags.Public | BindingFlags.Static);
                _maxProjectilesField = mainType.GetField("maxProjectiles", BindingFlags.Public | BindingFlags.Static);
                _projectileArrayField = mainType.GetField("projectile", BindingFlags.Public | BindingFlags.Static);

                // Player fields
                var entityType = playerType.BaseType;
                _playerPositionField = entityType?.GetField("position", BindingFlags.Public | BindingFlags.Instance)
                                    ?? playerType.GetField("position", BindingFlags.Public | BindingFlags.Instance);
                _playerWidthField = entityType?.GetField("width", BindingFlags.Public | BindingFlags.Instance)
                                 ?? playerType.GetField("width", BindingFlags.Public | BindingFlags.Instance);
                _playerHeightField = entityType?.GetField("height", BindingFlags.Public | BindingFlags.Instance)
                                  ?? playerType.GetField("height", BindingFlags.Public | BindingFlags.Instance);

                _playerChestField = playerType.GetField("chest", BindingFlags.Public | BindingFlags.Instance);
                _playerChestXField = playerType.GetField("chestX", BindingFlags.Public | BindingFlags.Instance);
                _playerChestYField = playerType.GetField("chestY", BindingFlags.Public | BindingFlags.Instance);
                _playerWhoAmIField = playerType.GetField("whoAmI", BindingFlags.Public | BindingFlags.Instance);
                _playerNoThrowField = playerType.GetField("noThrow", BindingFlags.Public | BindingFlags.Instance);
                _playerMouseInterfaceField = playerType.GetField("mouseInterface", BindingFlags.Public | BindingFlags.Instance);
                _playerControlUseItemField = playerType.GetField("controlUseItem", BindingFlags.Public | BindingFlags.Instance);
                _playerControlUseTileField = playerType.GetField("controlUseTile", BindingFlags.Public | BindingFlags.Instance);
                _playerReleaseUseItemField = playerType.GetField("releaseUseItem", BindingFlags.Public | BindingFlags.Instance);
                _playerReleaseUseTileField = playerType.GetField("releaseUseTile", BindingFlags.Public | BindingFlags.Instance);
                _playerItemAnimationField = playerType.GetField("itemAnimation", BindingFlags.Public | BindingFlags.Instance);
                _playerItemTimeField = playerType.GetField("itemTime", BindingFlags.Public | BindingFlags.Instance);
                _playerNoItemsField = playerType.GetField("noItems", BindingFlags.Public | BindingFlags.Instance);
                _setTalkNPCMethod = playerType.GetMethod("SetTalkNPC", BindingFlags.Public | BindingFlags.Instance);
                _piggyBankTrackerField = playerType.GetField("piggyBankProjTracker", BindingFlags.Public | BindingFlags.Instance);
                _mouseRightReleaseField = mainType.GetField("mouseRightRelease", BindingFlags.Public | BindingFlags.Static);
                _blockInteractionField = playerType.GetField("BlockInteractionWithProjectiles", BindingFlags.Public | BindingFlags.Static);

                _initialized = _mouseRightField != null && _playerChestField != null;

                if (_initialized)
                {
                    Mod.Log("PetInteraction initialized");
                    PetHelper.ForceInitialize();
                }
            }
            catch (Exception ex)
            {
                Mod.Log($"PetInteraction init error: {ex.Message}");
                _initialized = false;
            }

            return _initialized;
        }

        public static void SetInteractableFlags(object player)
        {
            if (!EnsureInitialized()) return;
            if (_hadInteractableField == null) return;

            try
            {
                int whoAmI = (int)_playerWhoAmIField.GetValue(player);
                var projectiles = (Array)_projectileArrayField.GetValue(null);
                int maxProj = (int)_maxProjectilesField.GetValue(null);

                for (int i = 0; i < maxProj; i++)
                {
                    var proj = projectiles.GetValue(i);
                    if (!PetHelper.IsActive(proj)) continue;
                    if (PetHelper.GetOwner(proj) != whoAmI) continue;

                    if (PetHelper.IsCosmeticPet(proj))
                    {
                        _hadInteractableField.SetValue(null, true);
                        return;
                    }
                }
            }
            catch { }
        }

        public static void HandleInteraction(object player)
        {
            if (!EnsureInitialized()) return;

            _framesSinceLastInteraction++;
            if (_closeCooldown > 0) _closeCooldown--;

            // Don't process any new interactions during close cooldown
            if (_closeCooldown > 0) return;

            try
            {
                int whoAmI = (int)_playerWhoAmIField.GetValue(player);

                // Handle keep-open state
                if (_keepPiggyOpen)
                {
                    HandleKeepOpenState(player, whoAmI);
                    return;
                }

                // Detect mouse release (falling edge)
                bool mouseRight = (bool)_mouseRightField.GetValue(null);
                bool mouseReleased = !mouseRight && _lastMouseRight;
                _lastMouseRight = mouseRight;

                if (!mouseReleased) return;
                if (_framesSinceLastInteraction < INTERACTION_COOLDOWN) return;

                // Get mouse world position
                float mouseWorldX, mouseWorldY;
                if (!GetMouseWorldPosition(out mouseWorldX, out mouseWorldY)) return;

                // Get player center
                float playerCenterX, playerCenterY;
                if (!GetEntityCenter(player, out playerCenterX, out playerCenterY)) return;

                // Find clicked pet
                var projectiles = (Array)_projectileArrayField.GetValue(null);
                int maxProj = (int)_maxProjectilesField.GetValue(null);

                object clickedPet = null;
                int clickedIndex = -1;

                for (int i = 0; i < maxProj; i++)
                {
                    var proj = projectiles.GetValue(i);
                    if (!PetHelper.IsActive(proj)) continue;
                    if (PetHelper.GetOwner(proj) != whoAmI) continue;

                    if (!PetHelper.IsCosmeticPet(proj)) continue;

                    if (PetHelper.IsPointInHitbox(proj, mouseWorldX, mouseWorldY))
                    {
                        float petCenterX, petCenterY;
                        if (PetHelper.GetCenter(proj, out petCenterX, out petCenterY))
                        {
                            float dist = Distance(playerCenterX, playerCenterY, petCenterX, petCenterY);
                            if (dist <= Mod.InteractionRange)
                            {
                                clickedPet = proj;
                                clickedIndex = i;
                                break;
                            }
                        }
                    }
                }

                if (clickedPet == null) return;

                _framesSinceLastInteraction = 0;
                _keepPiggyOpen = true;
                _boundPetIndex = clickedIndex;

                OpenPiggyBank(player, clickedPet);

                // Block vanilla from processing this click
                _playerMouseInterfaceField.SetValue(player, true);
            }
            catch (Exception ex)
            {
                Mod.Log($"Interaction error: {ex.Message}");
            }
        }

        private static void HandleKeepOpenState(object player, int whoAmI)
        {
            // Check if inventory was closed (user pressed ESC) - do this FIRST before blocking input
            bool invOpen = true;
            if (_playerInventoryField != null)
            {
                invOpen = (bool)_playerInventoryField.GetValue(null);
            }
            if (!invOpen)
            {
                // User closed inventory via ESC
                ClosePiggyBank(player);
                return;
            }

            // CRITICAL: Block all input to prevent clicking sounds
            // Match the tModLoader version exactly
            _playerControlUseItemField?.SetValue(player, false);
            _playerControlUseTileField?.SetValue(player, false);
            _playerReleaseUseItemField?.SetValue(player, true);  // Tell game button was released
            _playerReleaseUseTileField?.SetValue(player, true);
            _playerItemAnimationField?.SetValue(player, 0);
            _playerItemTimeField?.SetValue(player, 0);
            _playerNoItemsField?.SetValue(player, true);  // Prevent item use
            _mouseRightField?.SetValue(null, false);
            _mouseRightReleaseField?.SetValue(null, false);

            // Keep chest open (in case game logic tried to close it)
            int chest = (int)_playerChestField.GetValue(player);
            if (chest != -2)
            {
                _playerChestField.SetValue(player, -2);
                // Re-clear tracker - vanilla may have reset it
                ClearPiggyBankTracker(player);
            }

            // Mute all nearby projectiles to prevent sounds
            MuteNearbyProjectiles(player);

            // Check if bound pet is still valid
            if (_boundPetIndex >= 0)
            {
                var projectiles = (Array)_projectileArrayField.GetValue(null);
                var boundPet = projectiles.GetValue(_boundPetIndex);

                if (!PetHelper.IsActive(boundPet) || PetHelper.GetOwner(boundPet) != whoAmI)
                {
                    ClosePiggyBank(player);
                    return;
                }

                // Check distance
                float playerCX, playerCY, petCX, petCY;
                if (GetEntityCenter(player, out playerCX, out playerCY) &&
                    PetHelper.GetCenter(boundPet, out petCX, out petCY))
                {
                    float dist = Distance(playerCX, playerCY, petCX, petCY);
                    if (dist > Mod.InteractionRange)
                    {
                        ClosePiggyBank(player);
                        return;
                    }
                }
            }
        }

        private static FieldInfo _projSoundDelayField;

        private static void MuteNearbyProjectiles(object player)
        {
            try
            {
                if (_projSoundDelayField == null)
                {
                    _projSoundDelayField = Mod.ProjectileType.GetField("soundDelay",
                        BindingFlags.Public | BindingFlags.Instance);
                }
                if (_projSoundDelayField == null) return;

                int whoAmI = (int)_playerWhoAmIField.GetValue(player);
                var projectiles = (Array)_projectileArrayField.GetValue(null);
                int maxProj = (int)_maxProjectilesField.GetValue(null);

                float playerCX, playerCY;
                if (!GetEntityCenter(player, out playerCX, out playerCY)) return;

                for (int i = 0; i < maxProj; i++)
                {
                    var proj = projectiles.GetValue(i);
                    if (!PetHelper.IsActive(proj)) continue;
                    if (PetHelper.GetOwner(proj) != whoAmI) continue;

                    float petCX, petCY;
                    if (PetHelper.GetCenter(proj, out petCX, out petCY))
                    {
                        float dist = Distance(playerCX, playerCY, petCX, petCY);
                        if (dist <= SOUND_MUTE_RANGE)
                        {
                            _projSoundDelayField.SetValue(proj, int.MaxValue);
                        }
                    }
                }
            }
            catch { }
        }

        private static void OpenPiggyBank(object player, object pet)
        {
            try
            {
                _playerChestField.SetValue(player, -2);

                // Set chest position to pet location
                float petCenterX, petCenterY;
                if (PetHelper.GetCenter(pet, out petCenterX, out petCenterY))
                {
                    _playerChestXField?.SetValue(player, (int)(petCenterX / 16f));
                    _playerChestYField?.SetValue(player, (int)(petCenterY / 16f));
                }

                // Clear talk NPC
                _setTalkNPCMethod?.Invoke(player, new object[] { -1 });

                // Open inventory
                _playerInventoryField?.SetValue(null, true);

                // Set stack split delay
                _stackSplitField?.SetValue(null, STACK_SPLIT_DELAY);

                // IMPORTANT: Do NOT set piggy bank tracker to our pet!
                // Vanilla validates that tracked projectile is type 525 or 960.
                // If not, it sets chest=-1 and plays close sound every frame.
                // Instead, clear the tracker so validation is skipped.
                ClearPiggyBankTracker(player);

                // Play open sound
                PlaySound(10);

                // Prevent item throw
                _playerNoThrowField?.SetValue(player, NO_THROW_VALUE);
            }
            catch (Exception ex)
            {
                Mod.Log($"OpenPiggyBank error: {ex.Message}");
            }
        }

        private static void ClosePiggyBank(object player)
        {
            _keepPiggyOpen = false;
            _boundPetIndex = -1;
            _closeCooldown = CLOSE_COOLDOWN_FRAMES;  // Prevent immediate reopen
            _playerChestField?.SetValue(player, -1);
            PlaySound(11);
        }

        /// <summary>
        /// Clear the piggy bank tracker so vanilla doesn't try to validate our pet.
        /// Vanilla checks if tracked projectile is type 525 or 960 - if not, it closes chest every frame.
        /// IMPORTANT: TrackedProjectileReference is a STRUCT - we must set it back after modifying!
        /// </summary>
        private static void ClearPiggyBankTracker(object player)
        {
            try
            {
                if (_piggyBankTrackerField == null) return;

                // Get the struct (this is a copy!)
                var tracker = _piggyBankTrackerField.GetValue(player);
                if (tracker == null) return;

                // Try to find Clear method
                if (_trackerClearMethod == null)
                {
                    var trackerType = tracker.GetType();
                    _trackerClearMethod = trackerType.GetMethod("Clear",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (_trackerClearMethod != null)
                {
                    // Call Clear on the copy
                    _trackerClearMethod.Invoke(tracker, null);
                    // CRITICAL: Set the modified struct back to the field!
                    _piggyBankTrackerField.SetValue(player, tracker);
                }
            }
            catch { }
        }

        private static bool GetMouseWorldPosition(out float x, out float y)
        {
            x = 0;
            y = 0;

            try
            {
                if (_mouseScreenProp == null) return false;

                var mouseScreen = _mouseScreenProp.GetValue(null);
                var screenPos = _screenPositionField.GetValue(null);

                if (_vector2Type == null)
                {
                    _vector2Type = mouseScreen.GetType();
                    _xField = _vector2Type.GetField("X");
                    _yField = _vector2Type.GetField("Y");
                }

                float mouseX = (float)_xField.GetValue(mouseScreen);
                float mouseY = (float)_yField.GetValue(mouseScreen);
                float screenX = (float)_xField.GetValue(screenPos);
                float screenY = (float)_yField.GetValue(screenPos);

                x = mouseX + screenX;
                y = mouseY + screenY;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool GetEntityCenter(object player, out float centerX, out float centerY)
        {
            centerX = 0;
            centerY = 0;

            try
            {
                var pos = _playerPositionField.GetValue(player);
                if (pos == null) return false;

                if (_vector2Type == null)
                {
                    _vector2Type = pos.GetType();
                    _xField = _vector2Type.GetField("X");
                    _yField = _vector2Type.GetField("Y");
                }

                float x = (float)_xField.GetValue(pos);
                float y = (float)_yField.GetValue(pos);
                int width = (int)_playerWidthField.GetValue(player);
                int height = (int)_playerHeightField.GetValue(player);

                centerX = x + width / 2f;
                centerY = y + height / 2f;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float Distance(float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void PlaySound(int soundId)
        {
            try
            {
                var soundEngineType = Mod.MainType.Assembly.GetType("Terraria.Audio.SoundEngine");
                if (soundEngineType == null) return;

                var playMethod = soundEngineType.GetMethod("PlaySound",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(int), typeof(int), typeof(int) }, null);

                playMethod?.Invoke(null, new object[] { soundId, -1, -1 });
            }
            catch { }
        }
    }
}
