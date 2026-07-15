# NPCDisabler

Client-side NetNPC hider for ATLYSS. Toggle individual NPCs off or everything at once without touching the network, saves, or other players. Perfect for RP servers, custom maps, or just clearing up visual clutter.
### [Join the Discord Community](https://discord.gg/ePhX4Fb2we)

## Features
- **Auto-Discovery**: Automatically finds NetNPCs (Angela, Skrit, etc.) in your current map instance.
- **Live UI Updates**: Per-NPC toggles are added live to the EasySettings menu as new NPCs are discovered in the world.
- **Global Override**: "Disable All NetNPCs" toggle layered on top of individual toggles for quick clearing.
- **Multiplayer Safe**: 100% client-side. Uses `SetActive(false)` for clients, and safely disables local visual/physical components for the host to prevent Mirror network desyncs.
- **Instance Scoped**: Strictly targets your current `MapInstance`, ensuring it never accidentally hides NPCs in other loaded maps or the root scene.

## Requirements
- BepInEx
- [EasySettings](https://thunderstore.io/c/atlyss/p/Nessie/EasySettings/) *(Soft dependency — the mod works without it, but you lose the in-game settings UI)*
- CodeTalker

## Installation
Install via Thunderstore Mod Manager / r2modman, or manually drop `NPCDisabler.dll` into your `BepInEx/plugins` folder.

## Configuration
All settings are available in-game via the EasySettings menu and persist in `BepInEx/config`.

| Setting | Location | Description |
| --- | --- | --- |
| **Disable All NetNPCs** | Global | Hides every NetNPC found, overriding individual toggles below. |
| **Per-NPC Toggle** | NPCs | Hides that specific NPC by its GameObject name. |

## Known Issues
- NPCs are identified by their base GameObject name. If two differently-behaving NPCs share the exact same name in the game files, they will share the same toggle.