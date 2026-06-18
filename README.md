# NightVisionGoggles Mod for Raft

Craftable Night Vision Goggles with a real GPU-powered green phosphor night vision effect. Press V to toggle on and off.

Current version: `1.0.0`

## Features

- **Real GPU Shader** -- Uses Raft's built-in PostProcessing stack for hardware-accelerated color grading (exposure, tint, saturation) with zero CPU overhead
- **Pure Green Phosphor** -- No red, no blue, just classic night vision green
- **Battery System** -- 300 uses, drains while active, auto-deactivates when empty
- **Goggle Magnification** -- FOV reduced by 25% while active for authentic goggle optics
- **Ambient Light Boost** -- Scene lighting is amplified so dark areas become visible
- **HDR Enabled** -- Camera HDR is enabled while active so the exposure effect works properly
- **Lens Vignette** -- Darkened screen edges simulate the optical characteristics of night vision lenses
- **Raft-Style UI** -- Battery indicator with custom fill bar, auto-detected Raft font, colour palette matching the game's design
- **Crafting Recipe** -- 1 Battery, 4 Plastic, 2 CircuitBoard, 2 CopperIngot at the Equipment crafting station
- **No Raft key conflicts** -- Uses V (toggle), unused by the base game

## Installation

1. Install [RaftModLoader](https://www.raftmodding.com/loader)
2. Download the latest `NightVisionGoggles.rmod` file from the [releases page](https://github.com/FlazeIGuess/nightvisiongoggles-raft/releases)
3. Place the `.rmod` file in your RaftModLoader mods folder (or use the Mod Manager)
4. Launch Raft through RaftModLoader

## Usage

| Key | Action |
|-----|--------|
| **V** | Toggle Night Vision on/off |

## Crafting Recipe

| Ingredient | Amount |
|------------|--------|
| Battery | 1 |
| Plastic | 4 |
| CircuitBoard | 2 |
| CopperIngot | 2 |

Craft at the Equipment station. The goggles appear in the head equipment slot. Everyone needs their own pair.

## Configuration (Optional)

For in-game keybind configuration, install the [Extra Settings API](https://www.raftmodding.com/mods/extra-settings-api) mod. This allows you to rebind the toggle key.

Access via Settings > Mods > NightVisionGoggles

The mod works perfectly fine without Extra Settings API using the default V key.

## License

This project is licensed under the GNU Affero General Public License v3.0.

## Support

- Report bugs on the [Issues page](https://github.com/FlazeIGuess/nightvisiongoggles-raft/issues)
- Created by Flaze
