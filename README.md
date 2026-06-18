# NightVisionGoggles Mod for Raft

Night Vision Goggles as equippable head equipment with a real GPU-powered night vision shader via the PostProcessing stack. Press V to toggle, M to cycle between phosphor-green, bright, and thermal vision modes.

Current version: `1.0.0`

## Features

- **Real GPU Shader** -- Uses Raft's built-in UnityEngine.PostProcessing stack for hardware-accelerated color grading (exposure, tint, saturation) with zero CPU overhead
- **Three Vision Modes** -- Standard (phosphor-green), Bright (increased exposure), Thermal (orange/red)
- **Battery System** -- 300 uses, drains while active (one use per second), auto-deactivates when empty
- **Distance Fog** -- Subtle exponential green fog creates realistic range falloff like real night vision devices
- **Goggle Magnification** -- FOV reduced by 25% while active for authentic goggle optics
- **Ambient Light Boost** -- Scene lighting is amplified so dark areas become visible
- **Lens Vignette** -- Darkened screen edges simulate the optical characteristics of night vision lenses
- **Raft-Style UI** -- Battery indicator with custom fill bar, auto-detected Raft font, colour palette matching the game's design
- **Crafting Recipe** -- 1 Battery, 4 Plastic, 2 CircuitBoard, 2 CopperIngot at the Equipment crafting station
- **No Raft key conflicts** -- Uses V (toggle) and M (cycle mode), both unused by the base game

## Installation

1. Install [RaftModLoader](https://www.raftmodding.com/loader)
2. Download the latest `NightVisionGoggles.rmod` file
3. Place the `.rmod` file in your RaftModLoader mods folder (or use the Mod Manager)
4. Launch Raft through RaftModLoader

## Usage

| Key | Action |
|-----|--------|
| **V** | Toggle Night Vision on/off |
| **M** | Cycle mode (Standard > Bright > Thermal > Standard) |

## Crafting Recipe

| Ingredient | Amount |
|------------|--------|
| Battery | 1 |
| Plastic | 4 |
| CircuitBoard | 2 |
| CopperIngot | 2 |

Craft at the Equipment station. The goggles appear in the head equipment slot.

## Technical Implementation

The night vision effect is achieved through three layers:

1. **GPU PostProcessing** -- `PostProcessingBehaviour` on the player camera applies ColorGrading (exposure boost, green tint push, near-zero saturation) via Unity's built-in `Hidden/Post FX/Color Grading` shader
2. **CPU Ambient Light** -- `RenderSettings.ambientLight` is boosted and tinted to illuminate unlit geometry
3. **Global Fog** -- `RenderSettings.fog` (exponential, low density) adds distance-based falloff

All three layers are saved on activation and fully restored on deactivation. The effect gracefully handles save/load and game restart.

## License

This project is licensed under the GNU Affero General Public License v3.0.

## Support

- Report bugs via the Raft Modding Discord
- Created by Flaze
