# Vein Miner

Mine connected ore with one activation key press.

## Features

- Keybind-gated vein mining (default key: backtick / `OemTilde`)
- Ores enabled by default via `TileID.Sets.Ore`
- Optional tile whitelist (TileID names or numeric IDs)
- Safety cap (`maxVeinBlocks`) to limit per-trigger mining work

## In-Game Configuration (F6)

This mod exposes settings through `manifest.json` `config_schema`, so it appears in the same in-game config UI as other mods like AutoBuffs.

Key settings:

- `activationWindowMs`: how long the mod stays armed after pressing the key
- `maxVeinBlocks`: maximum extra connected blocks to mine
- `useOreSet`: include all ore tiles automatically
- `tileWhitelist`: extra allowed tile types (CSV names/IDs)

## Keybind

- `Activate Vein Miner` default: `OemTilde`

Rebind via the F6 keybind menu.

## Installation

Place these in:

- `TerrariaModder/mods/vein-miner/VeinMiner.dll`
- `TerrariaModder/mods/vein-miner/manifest.json`
