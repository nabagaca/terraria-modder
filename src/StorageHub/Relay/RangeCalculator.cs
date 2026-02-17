using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaModder.Core.Logging;
using StorageHub.Config;

namespace StorageHub.Relay
{
    /// <summary>
    /// Calculates effective storage range including relay extensions.
    ///
    /// Design:
    /// - Base range from tier
    /// - Relays extend range to additional areas
    /// - BFS expansion from player through relays
    /// - Max 10 relays to prevent unlimited range cheese
    /// </summary>
    public class RangeCalculator
    {
        private readonly ILogger _log;
        private readonly StorageHubConfig _config;

        // Cached player position
        private float _playerX;
        private float _playerY;

        // Reflection for player position
        private static FieldInfo _playerArrayField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _positionField;
        private static FieldInfo _positionXField;
        private static FieldInfo _positionYField;

        public RangeCalculator(ILogger log, StorageHubConfig config)
        {
            _log = log;
            _config = config;
            InitReflection();
        }

        private void InitReflection()
        {
            try
            {
                var mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");

                if (mainType != null)
                {
                    _playerArrayField = mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                    _myPlayerField = mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                }

                var playerType = Type.GetType("Terraria.Player, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Player");

                if (playerType != null)
                {
                    _positionField = playerType.GetField("position", BindingFlags.Public | BindingFlags.Instance);
                }

                // Vector2 has X and Y fields — scan loaded assemblies since the assembly name
                // varies: "Microsoft.Xna.Framework" (Windows XNA), "FNA", "MonoGame.Framework"
                Type vectorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    vectorType = asm.GetType("Microsoft.Xna.Framework.Vector2");
                    if (vectorType != null) break;
                }

                if (vectorType != null)
                {
                    _positionXField = vectorType.GetField("X", BindingFlags.Public | BindingFlags.Instance);
                    _positionYField = vectorType.GetField("Y", BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"RangeCalculator reflection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update cached player position.
        /// </summary>
        public void UpdatePlayerPosition()
        {
            try
            {
                if (_myPlayerField == null || _playerArrayField == null)
                    return;

                var myPlayerVal = _myPlayerField.GetValue(null);
                if (myPlayerVal == null) return;

                int myPlayer = (int)myPlayerVal;
                var players = _playerArrayField.GetValue(null) as Array;
                if (players == null) return;

                // Bounds check before array access
                if (myPlayer < 0 || myPlayer >= players.Length)
                    return;

                var player = players.GetValue(myPlayer);

                if (player != null && _positionField != null)
                {
                    var position = _positionField.GetValue(player);
                    if (position != null && _positionXField != null && _positionYField != null)
                    {
                        var xVal = _positionXField.GetValue(position);
                        var yVal = _positionYField.GetValue(position);
                        if (xVal != null && yVal != null)
                        {
                            _playerX = Convert.ToSingle(xVal);
                            _playerY = Convert.ToSingle(yVal);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"Failed to get player position: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current player position.
        /// </summary>
        public (float x, float y) GetPlayerPosition() => (_playerX, _playerY);

        /// <summary>
        /// Check if a position is within range (considering relays).
        /// </summary>
        public bool IsInRange(int tileX, int tileY)
        {
            // Convert tile to pixel coordinates
            float pixelX = tileX * 16;
            float pixelY = tileY * 16;

            int baseRange = ProgressionTier.GetRange(_config.Tier);

            // If at max tier, everything is in range
            if (baseRange == int.MaxValue)
                return true;

            // Check direct range from player
            // Use long for range squared to prevent overflow with large ranges
            float distSq = GetDistanceSquared(_playerX, _playerY, pixelX, pixelY);
            long baseRangeSq = (long)baseRange * baseRange;
            if (distSq <= baseRangeSq)
                return true;

            // Check relay chain
            return IsInRelayRange(pixelX, pixelY, baseRange);
        }

        private bool IsInRelayRange(float targetX, float targetY, int baseRange)
        {
            // BFS through relays
            // Use (X, Y) tuple instead of GetHashCode() to avoid hash collisions
            var visited = new HashSet<(int, int)>();
            var queue = new Queue<RelayPosition>();

            // Start with player position as origin
            int relayRange = RelayConstants.RelayRadius;

            // Use long for range squared to prevent overflow with large ranges
            long baseRangeSq = (long)baseRange * baseRange;
            long relayRangeSq = (long)relayRange * relayRange;

            // Find relays reachable from player
            foreach (var relay in _config.Relays)
            {
                float relayPixelX = relay.X * 16;
                float relayPixelY = relay.Y * 16;

                float distSq = GetDistanceSquared(_playerX, _playerY, relayPixelX, relayPixelY);
                if (distSq <= baseRangeSq)
                {
                    queue.Enqueue(relay);
                    visited.Add((relay.X, relay.Y));
                }
            }

            // BFS through relay chain
            while (queue.Count > 0 && visited.Count < RelayConstants.MaxRelays)
            {
                var current = queue.Dequeue();
                float currentX = current.X * 16;
                float currentY = current.Y * 16;

                // Check if target is reachable from this relay
                float targetDistSq = GetDistanceSquared(currentX, currentY, targetX, targetY);
                if (targetDistSq <= relayRangeSq)
                    return true;

                // Find relays reachable from this relay
                foreach (var relay in _config.Relays)
                {
                    var key = (relay.X, relay.Y);
                    if (visited.Contains(key)) continue;

                    float relayPixelX = relay.X * 16;
                    float relayPixelY = relay.Y * 16;

                    float distSq = GetDistanceSquared(currentX, currentY, relayPixelX, relayPixelY);
                    if (distSq <= relayRangeSq)
                    {
                        queue.Enqueue(relay);
                        visited.Add(key);
                    }
                }
            }

            return false;
        }

        private float GetDistanceSquared(float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Get the base range for current tier in tiles.
        /// </summary>
        public int GetBaseRangeTiles()
        {
            int range = ProgressionTier.GetRange(_config.Tier);
            if (range == int.MaxValue) return int.MaxValue;
            return range / 16;
        }

        /// <summary>
        /// Get relay range in tiles.
        /// </summary>
        public int GetRelayRangeTiles() => RelayConstants.RelayRadius / 16;
    }
}
