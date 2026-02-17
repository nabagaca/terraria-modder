using System;
using Terraria;

namespace TerrariaModder.Core.Reflection
{
    /// <summary>
    /// Convenience accessors for common game state.
    /// Uses direct Terraria references where possible, reflection for XNA types.
    /// </summary>
    public static class Game
    {
        #region Game State

        /// <summary>True if in main menu.</summary>
        public static bool InMenu => Main.gameMenu;

        /// <summary>True if game is paused.</summary>
        public static bool IsPaused => Main.gamePaused;

        /// <summary>True if currently loading.</summary>
        public static bool IsLoading => Main.gameMenu && Main.menuMode == 10;

        /// <summary>True if in a world (not menu).</summary>
        public static bool InWorld => !Main.gameMenu && Main.LocalPlayer != null;

        /// <summary>True if in multiplayer.</summary>
        public static bool IsMultiplayer => Main.netMode != 0;

        /// <summary>True if this is the server.</summary>
        public static bool IsServer => Main.netMode == 2;

        /// <summary>True if this is a client.</summary>
        public static bool IsClient => Main.netMode == 1;

        /// <summary>True if singleplayer.</summary>
        public static bool IsSingleplayer => Main.netMode == 0;

        #endregion

        #region Screen

        /// <summary>Screen width in pixels.</summary>
        public static int ScreenWidth => Main.screenWidth;

        /// <summary>Screen height in pixels.</summary>
        public static int ScreenHeight => Main.screenHeight;

        /// <summary>UI scale factor.</summary>
        public static float UIScale => Main.UIScale;

        /// <summary>Current screen position (top-left corner in world coordinates).</summary>
        public static Vec2 ScreenPosition => Vec2.FromXna(GameAccessor.TryGetMainField<object>("screenPosition"));

        /// <summary>Mouse X in screen coordinates.</summary>
        public static int MouseX => Main.mouseX;

        /// <summary>Mouse Y in screen coordinates.</summary>
        public static int MouseY => Main.mouseY;

        /// <summary>Mouse position in screen coordinates.</summary>
        public static Vec2 MouseScreen => new Vec2(Main.mouseX, Main.mouseY);

        /// <summary>Mouse position in world coordinates.</summary>
        public static Vec2 MouseWorld => Vec2.FromXna(GameAccessor.TryGetStaticProperty<object>(TypeFinder.Main, "MouseWorld"));

        /// <summary>True if left mouse button is pressed.</summary>
        public static bool MouseLeft => Main.mouseLeft;

        /// <summary>True if right mouse button is pressed.</summary>
        public static bool MouseRight => Main.mouseRight;

        #endregion

        #region World

        /// <summary>World width in tiles.</summary>
        public static int MaxTilesX => Main.maxTilesX;

        /// <summary>World height in tiles.</summary>
        public static int MaxTilesY => Main.maxTilesY;

        /// <summary>Surface level (Y coordinate where surface starts).</summary>
        public static int WorldSurface => (int)Main.worldSurface;

        /// <summary>Rock layer level.</summary>
        public static int RockLayer => (int)Main.rockLayer;

        /// <summary>Current time of day (0-54000 for day, 0-32400 for night).</summary>
        public static double Time => Main.time;

        /// <summary>True if daytime.</summary>
        public static bool IsDayTime => Main.dayTime;

        /// <summary>True if blood moon active.</summary>
        public static bool BloodMoon => Main.bloodMoon;

        /// <summary>True if eclipse active.</summary>
        public static bool Eclipse => Main.eclipse;

        /// <summary>True if raining.</summary>
        public static bool Raining => Main.raining;

        #endregion

        #region Local Player

        /// <summary>Index of local player.</summary>
        public static int MyPlayerIndex => Main.myPlayer;

        /// <summary>Local player instance.</summary>
        public static Player LocalPlayer => Main.LocalPlayer;

        /// <summary>Local player position.</summary>
        public static Vec2 PlayerPosition
        {
            get
            {
                var player = LocalPlayer;
                if (player == null) return Vec2.Zero;
                return Vec2.FromXna(GameAccessor.TryGetField<object>(player, "position"));
            }
        }

        /// <summary>Local player center position.</summary>
        public static Vec2 PlayerCenter
        {
            get
            {
                var player = LocalPlayer;
                if (player == null) return Vec2.Zero;
                return Vec2.FromXna(GameAccessor.TryGetProperty<object>(player, "Center"));
            }
        }

        /// <summary>Local player current health.</summary>
        public static int PlayerHealth => LocalPlayer?.statLife ?? 0;

        /// <summary>Local player max health.</summary>
        public static int PlayerMaxHealth => LocalPlayer?.statLifeMax2 ?? 0;

        /// <summary>Local player current mana.</summary>
        public static int PlayerMana => LocalPlayer?.statMana ?? 0;

        /// <summary>Local player max mana.</summary>
        public static int PlayerMaxMana => LocalPlayer?.statManaMax2 ?? 0;

        /// <summary>True if player is dead.</summary>
        public static bool PlayerDead => LocalPlayer?.dead ?? true;

        /// <summary>Currently selected inventory slot.</summary>
        public static int SelectedItem => LocalPlayer?.selectedItem ?? 0;

        /// <summary>Currently held item.</summary>
        public static Item HeldItem => LocalPlayer?.HeldItem;

        #endregion

        #region Arrays

        /// <summary>Player array.</summary>
        public static Player[] Players => Main.player;

        /// <summary>NPC array.</summary>
        public static NPC[] NPCs => Main.npc;

        /// <summary>Projectile array.</summary>
        public static Projectile[] Projectiles => Main.projectile;

        #endregion

        #region Input Helpers

        /// <summary>Block mouse input from reaching the game.</summary>
        public static void BlockMouse()
        {
            Main.blockMouse = true;
            if (LocalPlayer != null)
                LocalPlayer.mouseInterface = true;
        }

        /// <summary>Check if chat is open.</summary>
        public static bool ChatOpen => Main.drawingPlayerChat;

        /// <summary>Check if inventory is open.</summary>
        public static bool InventoryOpen => Main.playerInventory;

        /// <summary>Check if any UI is blocking input.</summary>
        public static bool UIBlocking => Main.blockMouse || (LocalPlayer?.mouseInterface ?? false);

        #endregion

        #region Utility

        /// <summary>Convert tile coordinates to world coordinates.</summary>
        public static Vec2 TileToWorld(int tileX, int tileY) => new Vec2(tileX * 16, tileY * 16);

        /// <summary>Convert world coordinates to tile coordinates.</summary>
        public static (int x, int y) WorldToTile(Vec2 worldPos) => ((int)(worldPos.X / 16), (int)(worldPos.Y / 16));

        /// <summary>Get tile at world position.</summary>
        public static Tile GetTile(int tileX, int tileY)
        {
            if (tileX < 0 || tileX >= MaxTilesX || tileY < 0 || tileY >= MaxTilesY)
                return default;
            return Main.tile[tileX, tileY];
        }

        #endregion

        #region Actions

        private static System.Reflection.MethodInfo _newTextMethod;
        private static System.Reflection.MethodInfo _placeTileMethod;
        private static Type _worldGenType;

        /// <summary>
        /// Show a chat message to the local player.
        /// </summary>
        /// <param name="text">Message text</param>
        /// <param name="r">Red (0-255)</param>
        /// <param name="g">Green (0-255)</param>
        /// <param name="b">Blue (0-255)</param>
        public static void ShowMessage(string text, byte r = 255, byte g = 255, byte b = 255)
        {
            try
            {
                if (_newTextMethod == null)
                {
                    var mainType = TypeFinder.Main;
                    if (mainType == null) return;

                    _newTextMethod = mainType.GetMethod("NewText",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null, new[] { typeof(string), typeof(byte), typeof(byte), typeof(byte) }, null);
                }

                _newTextMethod?.Invoke(null, new object[] { text, r, g, b });
            }
            catch { }
        }

        /// <summary>
        /// Place a tile at the specified position.
        /// </summary>
        /// <param name="tileX">Tile X coordinate</param>
        /// <param name="tileY">Tile Y coordinate</param>
        /// <param name="tileType">Tile type ID (e.g., 4 for torch)</param>
        /// <param name="style">Tile style variant (default 0)</param>
        /// <returns>True if tile was placed successfully</returns>
        public static bool PlaceTile(int tileX, int tileY, int tileType, int style = 0)
        {
            try
            {
                if (_worldGenType == null)
                {
                    var mainType = TypeFinder.Main;
                    if (mainType == null) return false;
                    _worldGenType = mainType.Assembly.GetType("Terraria.WorldGen");
                }

                if (_placeTileMethod == null && _worldGenType != null)
                {
                    _placeTileMethod = _worldGenType.GetMethod("PlaceTile",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null, new[] { typeof(int), typeof(int), typeof(int),
                                      typeof(bool), typeof(bool), typeof(int), typeof(int) }, null);
                }

                if (_placeTileMethod == null) return false;

                // Parameters: x, y, type, mute=false, forced=false, plr=myPlayer, style
                return (bool)_placeTileMethod.Invoke(null,
                    new object[] { tileX, tileY, tileType, false, false, Main.myPlayer, style });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a tile type is solid.
        /// </summary>
        public static bool IsTileSolid(int tileType)
        {
            if (tileType < 0 || tileType >= Main.tileSolid.Length)
                return false;
            return Main.tileSolid[tileType];
        }

        #endregion
    }
}
