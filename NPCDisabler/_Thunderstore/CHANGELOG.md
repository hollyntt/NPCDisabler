# Changelog

## v1.0.1

### Fixed
- Fixed a crash on startup ("The script 'NPCDisabler.Plugin' could not be instantiated!") that occurred whenever EasySettings was not installed, even though EasySettings is declared as a soft dependency.
- Fixed a second, related crash path where NPC scanning could still fail without EasySettings installed, because a method that always runs (regardless of whether EasySettings is present) referenced EasySettings types directly.
- Fixed a multiplayer bug where the host hiding an NPC could disable that NPC's server-authoritative behavior (navigation, shop state, animation sync) for every connected client, not just hide it locally for the host.
- Fixed NPC detection potentially matching the wrong map instance when multiple instances of the same map are loaded at once, by matching NPCs to the player's assigned map instance directly instead of relying on scene name/identity alone.
- Fixed the EasySettings dependency check using an incorrect plugin GUID, which prevented the mod from correctly detecting whether EasySettings was actually installed.

### Changed
- All EasySettings integration calls are now wrapped in error handling, so an unexpected failure inside the EasySettings API logs a warning instead of crashing the mod.

## v1.0.0

### Added
- Initial release. Client-side NetNPC hider for ATLYSS.
- Per-NPC toggles and a global "Disable All NetNPCs" override, configurable via EasySettings when installed.
- Hiding is local-only and does not affect other players or touch save data.