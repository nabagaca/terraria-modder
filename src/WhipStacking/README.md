# Whip Stacking

Restores pre-1.4.5 whip tag stacking behavior. Multiple whip tags can be active on NPCs simultaneously instead of only the most recent one.

## What It Does

In Terraria 1.4.5, Re-Logic changed whip behavior so that only one whip tag can be active on an NPC at a time. Hitting with a new whip removes the previous tag. This mod restores the pre-1.4.5 behavior where all whip tags stack, letting summoner builds benefit from multiple whip buffs simultaneously.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| enabled | true | Allow multiple whip tags to stack on NPCs |

Toggle via F6 Mod Menu in-game.

## Technical Details

- Uses 10 Harmony patches on whip-related projectile and NPC methods
- Resets tag state cleanly on world load/unload and config changes

## Multiplayer

Singleplayer only. Modifies shared NPC state.

## Installation

Requires TerrariaModder Core.

Extract this zip into your Terraria folder. The mod goes into
`TerrariaModder/mods/whip-stacking/`.
