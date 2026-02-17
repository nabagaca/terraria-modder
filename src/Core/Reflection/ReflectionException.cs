using System;

namespace TerrariaModder.Core.Reflection
{
    /// <summary>
    /// Exception thrown when reflection operations fail.
    /// Provides clear error messages for debugging.
    /// </summary>
    public class ReflectionException : Exception
    {
        public string TypeName { get; }
        public string MemberName { get; }
        public string Operation { get; }

        public ReflectionException(string message) : base(message)
        {
        }

        public ReflectionException(string message, Exception inner) : base(message, inner)
        {
        }

        public ReflectionException(string operation, string typeName, string memberName, string details)
            : base($"{operation} failed: {typeName}.{memberName} - {details}")
        {
            Operation = operation;
            TypeName = typeName;
            MemberName = memberName;
        }

        public ReflectionException(string operation, string typeName, string memberName, Exception inner)
            : base($"{operation} failed: {typeName}.{memberName} - {inner.Message}", inner)
        {
            Operation = operation;
            TypeName = typeName;
            MemberName = memberName;
        }

        public static ReflectionException FieldNotFound(Type type, string fieldName)
            => new ReflectionException("GetField", type?.FullName ?? "null", fieldName, "Field not found");

        public static ReflectionException MethodNotFound(Type type, string methodName)
            => new ReflectionException("GetMethod", type?.FullName ?? "null", methodName, "Method not found");

        public static ReflectionException PropertyNotFound(Type type, string propertyName)
            => new ReflectionException("GetProperty", type?.FullName ?? "null", propertyName, "Property not found");

        public static ReflectionException TypeNotFound(string typeName)
            => new ReflectionException("TypeLookup", typeName ?? "null", "", "Type not found. Ensure the assembly containing this type is loaded.");

        public static ReflectionException NullInstance(string operation, string memberName)
            => new ReflectionException($"{operation} failed: Instance is null when accessing {memberName}");
    }
}
