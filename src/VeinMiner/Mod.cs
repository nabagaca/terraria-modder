using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.Reflection;

namespace VeinMiner
{
    public class Mod : IMod
    {
        public string Id => "vein-miner";
        public string Name => "Vein Miner";
        public string Version => "1.0.0";

        private static Mod _instance;

        private ILogger _log;
        private ModContext _context;
        private Harmony _harmony;
        private MethodInfo _killTileMethod;

        private bool _enabled = true;
        private bool _showMessages;
        private int _activationWindowMs = 900;
        private int _maxVeinBlocks = 64;
        private bool _useOreSet = true;
        private string _tileWhitelistCsv = "";

        private readonly HashSet<int> _allowedTiles = new HashSet<int>();
        private readonly Dictionary<string, int> _tileNameMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private long _activationUntilTicksUtc;
        private bool _isVeinMining;

        private static readonly (int dx, int dy)[] NeighborOffsets = new (int dx, int dy)[]
        {
            (-1, 0), (1, 0), (0, -1), (0, 1),
            (-1, -1), (1, -1), (-1, 1), (1, 1)
        };

        public struct KillTileState
        {
            public bool Candidate;
            public int X;
            public int Y;
            public int TileType;
        }

        public void Initialize(ModContext context)
        {
            _context = context;
            _log = context.Logger;
            _instance = this;

            BuildNameMaps();
            LoadConfig();
            RebuildWhitelists();

            context.RegisterKeybind("activate", "Activate Vein Miner", "Arm vein mining for your next ore break", "OemTilde", OnActivatePressed);

            ApplyPatches();
            _log.Info("Vein Miner initialized");
        }

        public void OnConfigChanged()
        {
            LoadConfig();
            RebuildWhitelists();
            _log.Info("Vein Miner config reloaded");
        }

        private void ApplyPatches()
        {
            try
            {
                _harmony = new Harmony("com.terrariamodder.veinminer");
                _killTileMethod = typeof(WorldGen).GetMethod(
                    "KillTile",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool) },
                    null);

                if (_killTileMethod == null)
                {
                    _log.Error("Could not find WorldGen.KillTile overload");
                    return;
                }

                var patchType = typeof(Mod);
                _harmony.Patch(
                    _killTileMethod,
                    prefix: new HarmonyMethod(patchType.GetMethod(nameof(KillTile_Prefix), BindingFlags.Public | BindingFlags.Static)),
                    postfix: new HarmonyMethod(patchType.GetMethod(nameof(KillTile_Postfix), BindingFlags.Public | BindingFlags.Static)));
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to apply Vein Miner patches: {ex.Message}");
            }
        }

        public static void KillTile_Prefix(int i, int j, bool fail, bool effectOnly, bool noItem, ref KillTileState __state)
        {
            var inst = _instance;
            __state = default;
            if (inst == null) return;
            inst.OnKillTilePrefix(i, j, fail, effectOnly, noItem, ref __state);
        }

        public static void KillTile_Postfix(ref KillTileState __state)
        {
            var inst = _instance;
            if (inst == null || !__state.Candidate) return;
            inst.OnKillTilePostfix(__state);
        }

        private void OnKillTilePrefix(int i, int j, bool fail, bool effectOnly, bool noItem, ref KillTileState state)
        {
            if (!_enabled || _isVeinMining) return;
            if (fail || effectOnly || noItem) return;
            if (!IsActivationOpen()) return;

            if (!InBounds(i, j)) return;
            Tile tile = Main.tile[i, j];
            if (!TileHasBlock(tile)) return;

            int tileType = tile.type;
            if (!_allowedTiles.Contains(tileType)) return;
            if (!IsHeldToolAllowed()) return;

            state.Candidate = true;
            state.X = i;
            state.Y = j;
            state.TileType = tileType;
        }

        private void OnKillTilePostfix(KillTileState state)
        {
            if (!InBounds(state.X, state.Y)) return;

            Tile tileAfter = Main.tile[state.X, state.Y];
            if (TileHasBlock(tileAfter) && tileAfter.type == state.TileType) return;

            _activationUntilTicksUtc = 0;

            int mined = MineConnectedTiles(state.X, state.Y, state.TileType, _maxVeinBlocks);
            if (_showMessages && mined > 0)
            {
                GameMessage($"Mined {mined + 1} connected blocks");
            }
        }

        private int MineConnectedTiles(int rootX, int rootY, int tileType, int maxExtraBlocks)
        {
            if (maxExtraBlocks <= 0) return 0;

            var visited = new HashSet<long>();
            var queue = new Queue<(int x, int y)>();
            int mined = 0;

            foreach (var offset in NeighborOffsets)
            {
                queue.Enqueue((rootX + offset.dx, rootY + offset.dy));
            }

            _isVeinMining = true;
            try
            {
                while (queue.Count > 0 && mined < maxExtraBlocks)
                {
                    var point = queue.Dequeue();
                    int x = point.x;
                    int y = point.y;
                    if (!InBounds(x, y)) continue;

                    long key = ((long)x << 32) | (uint)y;
                    if (!visited.Add(key)) continue;

                    Tile tile = Main.tile[x, y];
                    if (!TileHasBlock(tile) || tile.type != tileType) continue;

                    WorldGen.KillTile(x, y, false, false, false);

                    Tile after = Main.tile[x, y];
                    if (TileHasBlock(after) && after.type == tileType) continue;

                    mined++;
                    foreach (var offset in NeighborOffsets)
                    {
                        queue.Enqueue((x + offset.dx, y + offset.dy));
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Vein mining error: {ex.Message}");
            }
            finally
            {
                _isVeinMining = false;
            }

            return mined;
        }

        private bool IsHeldToolAllowed()
        {
            Player player = Main.LocalPlayer;
            if (player == null) return false;

            Item item = player.HeldItem;
            if (item == null || item.IsAir) return false;
            return item.pick > 0;
        }

        private bool IsActivationOpen()
        {
            return DateTime.UtcNow.Ticks <= _activationUntilTicksUtc;
        }

        private void OnActivatePressed()
        {
            if (!_enabled) return;
            _activationUntilTicksUtc = DateTime.UtcNow.AddMilliseconds(_activationWindowMs).Ticks;

            if (_showMessages)
            {
                GameMessage("Vein mining armed");
            }
        }

        private void LoadConfig()
        {
            if (_context?.Config == null) return;

            _enabled = _context.Config.Get("enabled", true);
            _showMessages = _context.Config.Get("showMessages", false);
            _activationWindowMs = _context.Config.Get("activationWindowMs", 900);
            _maxVeinBlocks = _context.Config.Get("maxVeinBlocks", 64);
            _useOreSet = _context.Config.Get("useOreSet", true);
            _tileWhitelistCsv = _context.Config.Get("tileWhitelist", "");

            if (_activationWindowMs < 100) _activationWindowMs = 100;
            if (_maxVeinBlocks < 1) _maxVeinBlocks = 1;
        }

        private void RebuildWhitelists()
        {
            _allowedTiles.Clear();

            if (_useOreSet && TileID.Sets.Ore != null)
            {
                for (int i = 0; i < TileID.Sets.Ore.Length; i++)
                {
                    if (TileID.Sets.Ore[i])
                    {
                        _allowedTiles.Add(i);
                    }
                }
            }

            AddCsvTokensToSet(_tileWhitelistCsv, _tileNameMap, _allowedTiles);
        }

        private static void AddCsvTokensToSet(string csv, Dictionary<string, int> lookup, HashSet<int> target)
        {
            if (string.IsNullOrWhiteSpace(csv)) return;

            string[] tokens = csv.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in tokens)
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;

                if (int.TryParse(token, out int numericId))
                {
                    target.Add(numericId);
                    continue;
                }

                if (lookup.TryGetValue(token, out int id) || lookup.TryGetValue(NormalizeToken(token), out id))
                {
                    target.Add(id);
                }
            }
        }

        private void BuildNameMaps()
        {
            _tileNameMap.Clear();
            BuildIdNameMap(typeof(TileID), _tileNameMap);
        }

        private static void BuildIdNameMap(Type idType, Dictionary<string, int> map)
        {
            foreach (FieldInfo field in idType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(ushort) && field.FieldType != typeof(short) && field.FieldType != typeof(int)) continue;

                object value = field.GetValue(null);
                if (value == null) continue;

                int id = Convert.ToInt32(value);
                map[field.Name] = id;
                map[NormalizeToken(field.Name)] = id;
            }
        }

        private static string NormalizeToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;

            char[] chars = token.ToCharArray();
            var result = new char[chars.Length];
            int idx = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsLetterOrDigit(c))
                {
                    result[idx++] = char.ToLowerInvariant(c);
                }
            }
            return new string(result, 0, idx);
        }

        private static bool TileHasBlock(Tile tile)
        {
            return tile != null && tile.active();
        }

        private static bool InBounds(int x, int y)
        {
            return x > 0 && y > 0 && x < Main.maxTilesX - 1 && y < Main.maxTilesY - 1;
        }

        private void GameMessage(string message, byte r = 120, byte g = 255, byte b = 120)
        {
            Game.ShowMessage($"[VeinMiner] {message}", r, g, b);
        }

        public void OnWorldLoad()
        {
            _activationUntilTicksUtc = 0;
        }

        public void OnWorldUnload()
        {
            _activationUntilTicksUtc = 0;
        }

        public void Unload()
        {
            try
            {
                _harmony?.UnpatchAll("com.terrariamodder.veinminer");
            }
            catch
            {
            }

            _isVeinMining = false;
            _activationUntilTicksUtc = 0;
            _instance = null;
            _log?.Info("Vein Miner unloaded");
        }
    }
}
