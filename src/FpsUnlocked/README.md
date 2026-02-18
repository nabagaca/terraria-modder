# FPS Unlocked

Unlock Terraria's frame rate beyond the default 60 FPS cap with smooth frame interpolation. Game logic stays at 60hz while rendering runs at your display's refresh rate.

## How It Works

Terraria runs at a fixed 60 updates per second. This mod decouples rendering from game logic by interpolating entity positions between game ticks, drawing additional frames at your monitor's refresh rate.

Interpolated entities:
- **Players**: Position, body/head/leg rotation, held item, afterimage trails
- **NPCs & Enemies**: Position, rotation, graphical offsets
- **Projectiles**: Position and rotation
- **World Items, Gore, Combat Text, Popup Text**
- **Camera**: Sub-pixel positioning (removes vanilla integer snap)
- **Mouse**: Polled every render frame for lower input lag

Teleport detection prevents interpolation artifacts when entities jump large distances. Spawn/death transitions are handled gracefully.

## Frame Rate Modes

| Mode | Description |
|------|-------------|
| VSync (Vanilla) | Default 60 FPS, no changes |
| Capped | Custom FPS limit (30-1000), uses stopwatch-based frame limiter |
| Uncapped | No limit, renders as fast as hardware allows |

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| enabled | true | Enable the mod |
| mode | VSync (Vanilla) | Frame rate mode: VSync (Vanilla), Capped, or Uncapped |
| maxFps | 144 | Max FPS for Capped mode (30-1000) |
| interpolation | true | Smooth entity motion between ticks. Disable for raw FPS unlock where game speed scales with frame rate |
| mouseEveryFrame | true | Poll mouse every render frame for lower input lag (only when interpolation is on) |

All settings configurable via F6 Mod Menu in-game. Changes take effect immediately.

## Technical Details

- 8 Harmony patches on Main.Update, Main.DoUpdate, Main.DoDraw, Game.SuppressDraw, DoDraw_UpdateCameraPosition, and Lighting.LightTiles
- IL-emitted delegates for all entity field access (no reflection overhead per frame)
- Flat float arrays for keyframe storage (cache-friendly, zero allocation per frame)
- Stopwatch-based frame limiter for Capped mode (more accurate than XNA's IsFixedTimeStep)
- Camera transpiler removes integer snap (`conv.i4`/`conv.r4` NOPs) for sub-pixel scrolling
- Dust particles linked to entities via `customData` are offset by the parent entity's interpolation delta
- Lighting engine capped at 240 calls/sec (4 per game tick) to prevent held-torch flicker at high frame rates
- Teleport detection skips interpolation when entities jump large distances (>16 tiles)
- DoDraw finalizer ensures entity positions are always restored, even if rendering throws an exception

## Multiplayer

Works in multiplayer. Only affects local rendering.

## Installation

Requires TerrariaModder Core.

Extract this zip into your Terraria folder. The mod goes into
`TerrariaModder/mods/fps-unlocked/`.
