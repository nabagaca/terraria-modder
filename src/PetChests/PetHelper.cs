using System;
using System.Reflection;

namespace PetChests
{
    /// <summary>
    /// Helper class for identifying cosmetic pet projectiles.
    /// Uses reflection only for types not directly accessible.
    /// </summary>
    public static class PetHelper
    {
        // Chester = 960, Flying Piggy Bank = 525 - skip these, vanilla handles them
        public const int CHESTER_PROJ_TYPE = 960;
        public const int FLYING_PIGGY_BANK_PROJ_TYPE = 525;

        // Cached reflection
        private static bool[] _projPet;
        private static bool[] _lightPet;
        private static bool _initialized = false;

        private static FieldInfo _projTypeField;
        private static FieldInfo _projActiveField;
        private static FieldInfo _projOwnerField;
        private static FieldInfo _projPositionField;
        private static FieldInfo _projWidthField;
        private static FieldInfo _projHeightField;

        // Vector2 fields (XNA type - needs reflection)
        private static Type _vector2Type;
        private static FieldInfo _xField;
        private static FieldInfo _yField;

        public static void ForceInitialize()
        {
            EnsureInitialized();
        }

        private static bool EnsureInitialized()
        {
            if (_initialized) return true;

            try
            {
                var mainType = Mod.MainType;
                var projType = Mod.ProjectileType;

                // Get Main.projPet array
                var projPetField = mainType.GetField("projPet", BindingFlags.Public | BindingFlags.Static);
                if (projPetField != null)
                {
                    _projPet = (bool[])projPetField.GetValue(null);
                }

                // Get ProjectileID.Sets.LightPet array
                var projectileIdType = projType.Assembly.GetType("Terraria.ID.ProjectileID");
                if (projectileIdType != null)
                {
                    var setsType = projectileIdType.GetNestedType("Sets", BindingFlags.Public);
                    if (setsType != null)
                    {
                        var lightPetField = setsType.GetField("LightPet", BindingFlags.Public | BindingFlags.Static);
                        if (lightPetField != null)
                        {
                            _lightPet = (bool[])lightPetField.GetValue(null);
                        }
                    }
                }

                // Cache projectile fields
                _projTypeField = projType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                _projActiveField = projType.GetField("active", BindingFlags.Public | BindingFlags.Instance);
                _projOwnerField = projType.GetField("owner", BindingFlags.Public | BindingFlags.Instance);
                _projPositionField = projType.GetField("position", BindingFlags.Public | BindingFlags.Instance);
                _projWidthField = projType.GetField("width", BindingFlags.Public | BindingFlags.Instance);
                _projHeightField = projType.GetField("height", BindingFlags.Public | BindingFlags.Instance);

                _initialized = _projPet != null && _projTypeField != null;

                if (_initialized)
                {
                    Mod.Log("PetHelper initialized successfully");
                }
                else
                {
                    Mod.Log($"PetHelper init failed - projPet: {_projPet != null}, typeField: {_projTypeField != null}");
                }
            }
            catch (Exception ex)
            {
                Mod.Log($"PetHelper init error: {ex.Message}");
                _initialized = false;
            }

            return _initialized;
        }

        /// <summary>
        /// Check if a projectile is a cosmetic pet (not a light pet, not Chester)
        /// </summary>
        public static bool IsCosmeticPet(object proj)
        {
            if (proj == null) return false;
            if (!EnsureInitialized()) return false;

            try
            {
                bool active = (bool)_projActiveField.GetValue(proj);
                if (!active) return false;

                int type = (int)_projTypeField.GetValue(proj);

                // Bounds check
                if (type < 0 || type >= _projPet.Length)
                    return false;

                // Must be a pet
                if (!_projPet[type])
                    return false;

                // Must NOT be a light pet
                if (_lightPet != null && type < _lightPet.Length && _lightPet[type])
                    return false;

                // Skip Chester and Flying Piggy Bank
                if (type == CHESTER_PROJ_TYPE || type == FLYING_PIGGY_BANK_PROJ_TYPE)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int GetType(object proj)
        {
            if (proj == null || _projTypeField == null) return -1;
            try { return (int)_projTypeField.GetValue(proj); }
            catch { return -1; }
        }

        public static bool IsActive(object proj)
        {
            if (proj == null || _projActiveField == null) return false;
            try { return (bool)_projActiveField.GetValue(proj); }
            catch { return false; }
        }

        public static int GetOwner(object proj)
        {
            if (proj == null || _projOwnerField == null) return -1;
            try { return (int)_projOwnerField.GetValue(proj); }
            catch { return -1; }
        }

        public static int GetWidth(object proj)
        {
            if (proj == null || _projWidthField == null) return 0;
            try { return (int)_projWidthField.GetValue(proj); }
            catch { return 0; }
        }

        public static int GetHeight(object proj)
        {
            if (proj == null || _projHeightField == null) return 0;
            try { return (int)_projHeightField.GetValue(proj); }
            catch { return 0; }
        }

        public static bool GetPosition(object proj, out float x, out float y)
        {
            x = 0;
            y = 0;

            if (proj == null || _projPositionField == null) return false;

            try
            {
                var pos = _projPositionField.GetValue(proj);
                if (pos == null) return false;

                // Vector2 is XNA type - needs reflection for X/Y fields
                if (_vector2Type == null)
                {
                    _vector2Type = pos.GetType();
                    _xField = _vector2Type.GetField("X");
                    _yField = _vector2Type.GetField("Y");
                }

                x = (float)_xField.GetValue(pos);
                y = (float)_yField.GetValue(pos);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool GetCenter(object proj, out float centerX, out float centerY)
        {
            centerX = 0;
            centerY = 0;

            float x, y;
            if (!GetPosition(proj, out x, out y)) return false;

            int width = GetWidth(proj);
            int height = GetHeight(proj);

            centerX = x + width / 2f;
            centerY = y + height / 2f;
            return true;
        }

        public static bool IsPointInHitbox(object proj, float worldX, float worldY)
        {
            float x, y;
            if (!GetPosition(proj, out x, out y)) return false;

            int width = GetWidth(proj);
            int height = GetHeight(proj);

            return worldX >= x && worldX <= x + width &&
                   worldY >= y && worldY <= y + height;
        }
    }
}
