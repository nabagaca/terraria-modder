using System;
using System.Collections.Generic;
using System.Reflection;

namespace TerrariaModder.Core.Reflection
{
    /// <summary>
    /// Finds types across loaded assemblies with caching.
    /// </summary>
    public static class TypeFinder
    {
        private static readonly Dictionary<string, Type> _cache = new Dictionary<string, Type>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Find a type by its full name across all loaded assemblies.
        /// </summary>
        public static Type Find(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            lock (_lock)
            {
                if (_cache.TryGetValue(fullName, out var cached))
                    return cached;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var type = asm.GetType(fullName);
                        if (type != null)
                        {
                            _cache[fullName] = type;
                            return type;
                        }
                    }
                    catch
                    {
                        // Some assemblies may throw on GetType
                    }
                }

                // Cache null to avoid repeated lookups
                _cache[fullName] = null;
                return null;
            }
        }

        /// <summary>
        /// Find a type, throwing if not found.
        /// </summary>
        public static Type FindRequired(string fullName)
        {
            var type = Find(fullName);
            if (type == null)
                throw ReflectionException.TypeNotFound(fullName);
            return type;
        }

        /// <summary>
        /// Try to find a type.
        /// </summary>
        public static bool TryFind(string fullName, out Type type)
        {
            type = Find(fullName);
            return type != null;
        }

        // Common Terraria types
        public static Type Main => Find("Terraria.Main");
        public static Type Player => Find("Terraria.Player");
        public static Type NPC => Find("Terraria.NPC");
        public static Type Item => Find("Terraria.Item");
        public static Type Projectile => Find("Terraria.Projectile");
        public static Type Tile => Find("Terraria.Tile");
        public static Type WorldGen => Find("Terraria.WorldGen");
        public static Type NetMessage => Find("Terraria.NetMessage");
        public static Type Lang => Find("Terraria.Lang");
        public static Type PlayerInput => Find("Terraria.GameInput.PlayerInput");

        // XNA/FNA types
        public static Type Vector2 => Find("Microsoft.Xna.Framework.Vector2");
        public static Type Color => Find("Microsoft.Xna.Framework.Color");
        public static Type Rectangle => Find("Microsoft.Xna.Framework.Rectangle");
        public static Type Keyboard => Find("Microsoft.Xna.Framework.Input.Keyboard");
        public static Type Keys => Find("Microsoft.Xna.Framework.Input.Keys");
        public static Type Mouse => Find("Microsoft.Xna.Framework.Input.Mouse");
        public static Type SpriteBatch => Find("Microsoft.Xna.Framework.Graphics.SpriteBatch");
        public static Type Texture2D => Find("Microsoft.Xna.Framework.Graphics.Texture2D");
        public static Type SpriteFont => Find("Microsoft.Xna.Framework.Graphics.SpriteFont");

        /// <summary>
        /// Clear the type cache.
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }
    }
}
