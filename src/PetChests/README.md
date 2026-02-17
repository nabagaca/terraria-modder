# Pet Chests

Use your cosmetic pets as portable piggy banks! Right-click any summoned pet to access your piggy bank storage.

> **Note for Developers**: This is an advanced mod example showcasing complex Harmony patching and workarounds for vanilla game validation. For learning the basics, start with simpler mods like SkipIntro or AutoBuffs.

## Features

- Right-click any summoned cosmetic pet to open piggy bank storage
- Works with ALL cosmetic pets (not light pets like fairies)
- Pet detection uses `Main.projPet[]` array and filters out light pets via `ProjectileID.Sets.LightPet`
- Configurable interaction range
- No consumables or special items needed - just summon your pet and click it

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | true | Enable pet chest functionality |
| Interaction Range | 200 | Maximum distance (pixels) to use pet as chest |

All settings configurable via F6 menu in-game.

## Installation

Requires TerrariaModder Core.

Extract this zip into your Terraria folder. The mod goes into
`TerrariaModder/mods/pet-chests/`.

## Multiplayer

Works in multiplayer.

## Technical Notes (For Developers)

This mod demonstrates advanced techniques for working around vanilla game validation:

### Challenge
Terraria validates piggy bank projectiles via `piggyBankProjTracker`. When a piggy bank is opened, vanilla checks that the tracked projectile is type 525 (Flying Piggy) or 960 (Chester). If not, it closes the chest and plays the close sound every frame.

### Solution
Instead of fighting the tracker, PetChests:
1. Clears the tracker (`TrackedProjectileReference.Clear()`) so vanilla skips validation
2. Patches `HandleBeingInChestRange` to skip tile-based chest checks
3. Patches `PlayInteractiveProjectileOpenCloseSound` to mute spam sounds
4. Manually maintains the piggy bank UI state

### Key Patterns
- **Delayed patching**: Patches applied after 5-second timer to avoid initialization issues
- **Struct reflection**: `TrackedProjectileReference` is a struct - must set back after modification
- **Input blocking**: Comprehensive input suppression to prevent click sounds and vanilla re-opening

## Credits

**Author**: Inidar

**Dependencies**:
- [TerrariaInjector](https://github.com/ConfuzzedCat/TerrariaInjector) by ConfuzzedCat
- [Harmony](https://github.com/pardeike/Harmony) by pardeike
