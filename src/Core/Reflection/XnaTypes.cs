using System;
using System.Reflection;

namespace TerrariaModder.Core.Reflection
{
    /// <summary>
    /// Wrapper for Microsoft.Xna.Framework.Vector2.
    /// Avoids compile-time XNA dependency.
    /// </summary>
    public struct Vec2
    {
        public float X;
        public float Y;

        private static Type _xnaType;
        private static FieldInfo _xField, _yField;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Convert from XNA Vector2.</summary>
        public static Vec2 FromXna(object xnaVector)
        {
            if (xnaVector == null) return default;
            EnsureXnaType();
            if (_xField == null || _yField == null) return default;

            try
            {
                return new Vec2(
                    (float)_xField.GetValue(xnaVector),
                    (float)_yField.GetValue(xnaVector)
                );
            }
            catch
            {
                return default;
            }
        }

        private static void EnsureXnaType()
        {
            if (_xnaType != null) return;
            _xnaType = TypeFinder.Vector2;
            if (_xnaType == null) return;

            _xField = _xnaType.GetField("X");
            _yField = _xnaType.GetField("Y");
        }

        public static Vec2 Zero => new Vec2(0, 0);

        public override string ToString() => $"({X:F2}, {Y:F2})";
        public override bool Equals(object obj) => obj is Vec2 v && X == v.X && Y == v.Y;
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);
    }
}
