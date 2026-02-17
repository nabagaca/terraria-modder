using System;
using System.Collections.Generic;
using System.Reflection;

namespace TerrariaModder.Core.Reflection
{
    /// <summary>
    /// Caches reflection results to avoid repeated lookups.
    /// Thread-safe for read operations.
    /// </summary>
    internal static class ReflectionCache
    {
        private static readonly Dictionary<string, FieldInfo> _fields = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, PropertyInfo> _properties = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<string, ConstructorInfo> _constructors = new Dictionary<string, ConstructorInfo>();
        private static readonly object _lock = new object();

        private const BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        /// <summary>
        /// Get a field from the cache, or look it up and cache it.
        /// </summary>
        public static FieldInfo GetField(Type type, string name, BindingFlags? flags = null)
        {
            if (type == null) return null;

            var key = $"F:{type.FullName}.{name}";
            lock (_lock)
            {
                if (_fields.TryGetValue(key, out var cached))
                    return cached;

                var field = type.GetField(name, flags ?? AllFlags);
                _fields[key] = field;
                return field;
            }
        }

        /// <summary>
        /// Get a method from the cache, or look it up and cache it.
        /// </summary>
        public static MethodInfo GetMethod(Type type, string name, Type[] paramTypes = null, BindingFlags? flags = null)
        {
            if (type == null) return null;

            var paramKey = paramTypes != null ? string.Join(",", Array.ConvertAll(paramTypes, t => t?.Name ?? "null")) : "";
            var key = $"M:{type.FullName}.{name}({paramKey})";

            lock (_lock)
            {
                if (_methods.TryGetValue(key, out var cached))
                    return cached;

                MethodInfo method;
                try
                {
                    if (paramTypes != null)
                        method = type.GetMethod(name, flags ?? AllFlags, null, paramTypes, null);
                    else
                        method = type.GetMethod(name, flags ?? AllFlags);
                }
                catch (AmbiguousMatchException)
                {
                    // Multiple overloads exist - caller should provide paramTypes
                    method = null;
                }

                _methods[key] = method;
                return method;
            }
        }

        /// <summary>
        /// Get a property from the cache, or look it up and cache it.
        /// </summary>
        public static PropertyInfo GetProperty(Type type, string name, BindingFlags? flags = null)
        {
            if (type == null) return null;

            var key = $"P:{type.FullName}.{name}";
            lock (_lock)
            {
                if (_properties.TryGetValue(key, out var cached))
                    return cached;

                var property = type.GetProperty(name, flags ?? AllFlags);
                _properties[key] = property;
                return property;
            }
        }

        /// <summary>
        /// Get a constructor from the cache, or look it up and cache it.
        /// </summary>
        public static ConstructorInfo GetConstructor(Type type, Type[] paramTypes, BindingFlags? flags = null)
        {
            if (type == null) return null;

            var paramKey = paramTypes != null ? string.Join(",", Array.ConvertAll(paramTypes, t => t?.Name ?? "null")) : "";
            var key = $"C:{type.FullName}({paramKey})";

            lock (_lock)
            {
                if (_constructors.TryGetValue(key, out var cached))
                    return cached;

                var ctor = type.GetConstructor(flags ?? AllFlags, null, paramTypes ?? Type.EmptyTypes, null);
                _constructors[key] = ctor;
                return ctor;
            }
        }

        /// <summary>
        /// Clear all caches.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _fields.Clear();
                _methods.Clear();
                _properties.Clear();
                _constructors.Clear();
            }
        }

        /// <summary>
        /// Get cache statistics for debugging.
        /// </summary>
        public static (int fields, int methods, int properties, int constructors) GetStats()
        {
            lock (_lock)
            {
                return (_fields.Count, _methods.Count, _properties.Count, _constructors.Count);
            }
        }
    }
}
