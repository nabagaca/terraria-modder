using System;
using System.Reflection;

namespace TerrariaModder.Core.Reflection
{
    /// <summary>
    /// Safe, cached API for accessing Terraria game internals via reflection.
    /// </summary>
    public static class GameAccessor
    {
        private const BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        #region Field Access

        /// <summary>Get a static field from Terraria.Main.</summary>
        public static T GetMainField<T>(string fieldName)
        {
            var mainType = TypeFinder.Main;
            if (mainType == null) throw ReflectionException.TypeNotFound("Terraria.Main");
            return GetStaticField<T>(mainType, fieldName);
        }

        /// <summary>Set a static field on Terraria.Main.</summary>
        public static void SetMainField<T>(string fieldName, T value)
        {
            var mainType = TypeFinder.Main;
            if (mainType == null) throw ReflectionException.TypeNotFound("Terraria.Main");
            SetStaticField(mainType, fieldName, value);
        }

        /// <summary>Get a static field from a type.</summary>
        public static T GetStaticField<T>(Type type, string fieldName)
        {
            if (type == null) throw ReflectionException.TypeNotFound("null");

            var field = ReflectionCache.GetField(type, fieldName);
            if (field == null) throw ReflectionException.FieldNotFound(type, fieldName);

            try
            {
                return (T)field.GetValue(null);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("GetStaticField", type.FullName, fieldName, ex);
            }
        }

        /// <summary>Set a static field on a type.</summary>
        public static void SetStaticField<T>(Type type, string fieldName, T value)
        {
            if (type == null) throw ReflectionException.TypeNotFound("null");

            var field = ReflectionCache.GetField(type, fieldName);
            if (field == null) throw ReflectionException.FieldNotFound(type, fieldName);

            try
            {
                field.SetValue(null, value);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("SetStaticField", type.FullName, fieldName, ex);
            }
        }

        /// <summary>Get an instance field.</summary>
        public static T GetField<T>(object instance, string fieldName)
        {
            if (instance == null) throw ReflectionException.NullInstance("GetField", fieldName);

            var type = instance.GetType();
            var field = ReflectionCache.GetField(type, fieldName);
            if (field == null) throw ReflectionException.FieldNotFound(type, fieldName);

            try
            {
                return (T)field.GetValue(instance);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("GetField", type.FullName, fieldName, ex);
            }
        }

        /// <summary>Set an instance field.</summary>
        public static void SetField<T>(object instance, string fieldName, T value)
        {
            if (instance == null) throw ReflectionException.NullInstance("SetField", fieldName);

            var type = instance.GetType();
            var field = ReflectionCache.GetField(type, fieldName);
            if (field == null) throw ReflectionException.FieldNotFound(type, fieldName);

            try
            {
                field.SetValue(instance, value);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("SetField", type.FullName, fieldName, ex);
            }
        }

        #endregion

        #region Property Access

        /// <summary>Get a static property from Terraria.Main.</summary>
        public static T GetMainProperty<T>(string propertyName)
        {
            var mainType = TypeFinder.Main;
            if (mainType == null) throw ReflectionException.TypeNotFound("Terraria.Main");
            return GetStaticProperty<T>(mainType, propertyName);
        }

        /// <summary>Get a static property from a type.</summary>
        public static T GetStaticProperty<T>(Type type, string propertyName)
        {
            if (type == null) throw ReflectionException.TypeNotFound("null");

            var prop = ReflectionCache.GetProperty(type, propertyName);
            if (prop == null) throw ReflectionException.PropertyNotFound(type, propertyName);

            try
            {
                return (T)prop.GetValue(null);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("GetStaticProperty", type.FullName, propertyName, ex);
            }
        }

        /// <summary>Get an instance property.</summary>
        public static T GetProperty<T>(object instance, string propertyName)
        {
            if (instance == null) throw ReflectionException.NullInstance("GetProperty", propertyName);

            var type = instance.GetType();
            var prop = ReflectionCache.GetProperty(type, propertyName);
            if (prop == null) throw ReflectionException.PropertyNotFound(type, propertyName);

            try
            {
                return (T)prop.GetValue(instance);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("GetProperty", type.FullName, propertyName, ex);
            }
        }

        /// <summary>Set an instance property.</summary>
        public static void SetProperty<T>(object instance, string propertyName, T value)
        {
            if (instance == null) throw ReflectionException.NullInstance("SetProperty", propertyName);

            var type = instance.GetType();
            var prop = ReflectionCache.GetProperty(type, propertyName);
            if (prop == null) throw ReflectionException.PropertyNotFound(type, propertyName);

            try
            {
                prop.SetValue(instance, value);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("SetProperty", type.FullName, propertyName, ex);
            }
        }

        #endregion

        #region Method Invocation

        /// <summary>Invoke a static method on Terraria.Main.</summary>
        public static T InvokeMainMethod<T>(string methodName, params object[] args)
        {
            var mainType = TypeFinder.Main;
            if (mainType == null) throw ReflectionException.TypeNotFound("Terraria.Main");
            return InvokeStaticMethod<T>(mainType, methodName, args);
        }

        /// <summary>Invoke a static method.</summary>
        public static T InvokeStaticMethod<T>(Type type, string methodName, params object[] args)
        {
            if (type == null) throw ReflectionException.TypeNotFound("null");

            var method = ReflectionCache.GetMethod(type, methodName);
            if (method == null) throw ReflectionException.MethodNotFound(type, methodName);

            try
            {
                return (T)method.Invoke(null, args);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("InvokeStaticMethod", type.FullName, methodName, ex);
            }
        }

        /// <summary>Invoke an instance method.</summary>
        public static T InvokeMethod<T>(object instance, string methodName, params object[] args)
        {
            if (instance == null) throw ReflectionException.NullInstance("InvokeMethod", methodName);

            var type = instance.GetType();
            var method = ReflectionCache.GetMethod(type, methodName);
            if (method == null) throw ReflectionException.MethodNotFound(type, methodName);

            try
            {
                return (T)method.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                throw new ReflectionException("InvokeMethod", type.FullName, methodName, ex);
            }
        }

        /// <summary>Invoke an instance method with no return value.</summary>
        public static void InvokeMethod(object instance, string methodName, params object[] args)
        {
            InvokeMethod<object>(instance, methodName, args);
        }

        #endregion

        #region Array Access

        /// <summary>Get an element from an array.</summary>
        public static T GetArrayElement<T>(object array, params int[] indices)
        {
            if (array == null) throw new ReflectionException("Array is null");

            try
            {
                var arr = (Array)array;
                return (T)arr.GetValue(indices);
            }
            catch (Exception ex)
            {
                throw new ReflectionException($"GetArrayElement failed: {ex.Message}", ex);
            }
        }

        /// <summary>Set an element in an array.</summary>
        public static void SetArrayElement<T>(object array, T value, params int[] indices)
        {
            if (array == null) throw new ReflectionException("Array is null");

            try
            {
                var arr = (Array)array;
                arr.SetValue(value, indices);
            }
            catch (Exception ex)
            {
                throw new ReflectionException($"SetArrayElement failed: {ex.Message}", ex);
            }
        }

        #endregion

        #region Safe Variants (Return default instead of throwing)

        /// <summary>Try to get a field, returning default on failure.</summary>
        public static T TryGetField<T>(object instance, string fieldName, T defaultValue = default)
        {
            try
            {
                return GetField<T>(instance, fieldName);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>Try to get a static field, returning default on failure.</summary>
        public static T TryGetStaticField<T>(Type type, string fieldName, T defaultValue = default)
        {
            try
            {
                return GetStaticField<T>(type, fieldName);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>Try to get a Main field, returning default on failure.</summary>
        public static T TryGetMainField<T>(string fieldName, T defaultValue = default)
        {
            try
            {
                return GetMainField<T>(fieldName);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>Try to get a property, returning default on failure.</summary>
        public static T TryGetProperty<T>(object instance, string propertyName, T defaultValue = default)
        {
            try
            {
                return GetProperty<T>(instance, propertyName);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>Try to get a static property, returning default on failure.</summary>
        public static T TryGetStaticProperty<T>(Type type, string propertyName, T defaultValue = default)
        {
            try
            {
                return GetStaticProperty<T>(type, propertyName);
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion
    }
}
