# Admin Panel

Quick access to admin/cheat features with a toggleable panel UI.

## Features

- **God Mode** - Toggle invincibility (panel button or F9 hotkey)
- **Full Health/Mana** - Instant restore buttons
- **Movement Speed** - Speed multiplier slider (1-10x)
- **Time Controls** - Dawn, Noon, Dusk, Night presets + speed slider (1x-60x)
- **Teleport** - Spawn, Dungeon, Hell, Beach, Player Bed, Random
- **Respawn Time** - Separate sliders for normal and boss deaths

## Keybinds

| Key | Action |
|-----|--------|
| `\` (Backslash) | Toggle admin panel |
| `F9` | Toggle god mode |

Both keybinds can be remapped via the F6 Mod Menu.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| enabled | true | Enable the mod |
| singleplayerOnly | true | Disable in multiplayer |

## Technical Details

This mod demonstrates several key patterns:

- **Custom UI panel** with dragging, buttons, sliders
- **Harmony patching** for god mode (Player.ResetEffects) and respawn (Player.UpdateDead)
- **Reflection** for accessing game state (time, player stats, NPC data)
- **Boss detection** matching vanilla logic (within 4000 pixels)

## Respawn Time Presets

**Normal deaths** (base 10s): 1s, 2s, 3s, 5s, 10s (default), 15s, 20s, 30s, 45s

**Boss deaths** (base 20s): 2s, 5s, 7s, 10s, 20s (default), 30s, 45s, 60s, 90s

Note: Expert mode adds 50% to base times in vanilla.

## Multiplayer

Singleplayer only.

## Installation

Requires TerrariaModder Core.

Extract this zip into your Terraria folder. The mod goes into
`TerrariaModder/mods/admin-panel/`.
