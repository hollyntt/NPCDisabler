# NPCDisabler — Early Beta

Client-side NetNPC hider for ATLYSS. Toggle individual NPCs off or everything at once without touching the network, saves, or other players.

## Features

- Auto-discovers NetNPCs (Angela, Skrit, etc) in your current map instance
- Per-NPC toggles added live to EasySettings as new NPCs are found
- Global "Disable All NetNPC's" override, layered on top of individual toggles
- 100% client-side -`SetActive(false)` only, no network state touched
- Scoped to your current map instance, not every loaded copy of that map

## Requirements

- BepInEx
- [EasySettings](https://thunderstore.io/c/atlyss/p/Nessie/EasySettings/) (soft dependency — mod works without it, but you lose the settings UI)
- CodeTalker

## Installation

Install via Thunderstore Mod Manager / r2modman, or drop `NPCDisabler.dll` into `BepInEx/plugins`.

## Configuration

All settings are available in-game via EasySettings, and persist in `BepInEx/config`.

| Setting | Location | Description |
|---|---|---|
| `Disable All NetNPCs` | Global | Hides every NetNPC found, overriding individual toggles below |
| Per-NPC toggle | NPCs | Hides that specific NPC by name |

## Known Issues

- NPCs are identified by GameObject name - if two differently-behaving NPCs share a name, they'll share a toggle
- Early beta - validated against a decompiled Assembly-CSharp dump, not yet exhaustively play-tested across every zone