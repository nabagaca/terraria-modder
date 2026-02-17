# Auto Furniture Buffs

Automatically applies buffs from nearby activated furniture without needing to click on each one.

## Features

- Scans for buff-giving furniture near your character
- Automatically applies buffs when in range
- Supports all vanilla buff stations:
  - Crystal Ball (Clairvoyance)
  - Ammo Box (Ammo Box buff)
  - Bewitching Table (Bewitched - extra minion)
  - Sharpening Station (Sharpened)
  - War Table (War Table buff)
  - Slice of Cake (Sugar Rush)

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| enabled | true | Enable auto-buff functionality |
| scanRadius | 40 | Tile radius to scan for furniture (5-100, higher = more CPU) |

All settings configurable via F6 Mod Menu in-game.

## Technical Details

- Uses a Harmony postfix on `Player.Update` to scan nearby tiles each frame
- Looks up tile IDs to identify buff furniture and applies the corresponding buff
- Scan radius is configurable to balance performance vs. convenience

## Multiplayer

Works in multiplayer.

## Installation

Requires TerrariaModder Core.

Extract this zip into your Terraria folder. The mod goes into
`TerrariaModder/mods/auto-furniture-buffs/`.
