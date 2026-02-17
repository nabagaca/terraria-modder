# Seed Lab

Mix and match individual features from Terraria's secret seeds at runtime. Toggle enemy scaling, boss AI changes, spawn rates, world generation features, and more, without needing to create a new world.

## Features

- **In-Game Panel (F10)**:Toggle seed features while playing. Changes take effect immediately for runtime features (enemy stats, spawn rates, boss AI).
- **World-Gen Panel**:Configure seed features before creating a world. Works from the title screen and character/world select menus.
- **9 Special Seeds**:For the Worthy, Drunk World, Don't Starve, Not the Bees, Remix, Zenith, 10th Anniversary, No Traps, Skyblock
- **6 Secret Seeds**:Vampire, Infected, Team Spawns, Dual Dungeons, Halloween Forever, Christmas Forever
- **120+ Individual Features**:Each seed is broken down into granular features (enemy stats, boss AI, spawn rates, etc.) that can be toggled independently.
- **Easy Groups**:Toggle entire categories at once (e.g., "FTW Enemy Scaling" enables all FTW enemy-related features)
- **Presets**:Save and load custom seed configurations
- **Per-World State**:Feature states persist per world

## Keybinds

| Key | Action |
|-----|--------|
| `F10` | Toggle Seed Lab panel |

F10 works both in-world (runtime overrides) and in menus (world-gen overrides). Rebindable via F6 Mod Menu.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| enabled | true | Enable the Seed Lab mod |

## Debug Commands

| Command | Description |
|---------|-------------|
| `seed-lab.status` | Show current seed feature states |
| `seed-lab.toggle <seed>` | Toggle a seed or feature group (e.g., `seed-lab.toggle ftw`) |
| `seed-lab.preset list\|apply\|save\|delete <name>` | Manage presets |
| `seed-lab.reset` | Reset all features to match the current world's seed flags |

## How It Works

**Runtime features** (enemy stats, spawn rates, boss AI) use Harmony patches on `NPC.SetDefaults`, `NPC.AI`, `Spawner.GetSpawnRate`, etc. Each patch checks whether the corresponding feature is enabled before applying the seed's behavior.

**World-gen features** use Harmony patches on `WorldGen.Reset()`, `GenPass.Apply()`, and `FinalizeSecretSeeds()` to inject seed flags during world creation.

**State is separate**:runtime state (`state.json`) and world-gen state (`state-worldgen.json`) are independent, so you can have different configurations for playing vs. creating worlds.

## Multiplayer

Singleplayer only.

## Installation

Requires TerrariaModder Core.

Extract this zip into your Terraria folder. The mod goes into
`TerrariaModder/mods/seed-lab/`.
