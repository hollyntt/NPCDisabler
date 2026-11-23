# Changelog
## x.x.3
- Fixed NPC toggles not showing up in the settings menu: NPCs already in your config now load at startup, so their toggles exist when the menu is built
- NPCs are still added to the menu as you discover them in-game
- Fixed NPC names with special characters breaking config entries (and stopping later NPCs from registering)
- One bad toggle can no longer stop the rest from being added to the menu
- Removed a listener for an EasySettings event that doesn't exist in current versions
- Added an "NPCs" header to the settings tab
- Minor cleanup: removed a duplicate NPC scan per cycle and fixed a possible crash while the NPC list was changing
## x.x.2
- fuk what in the code revert
## 1.0.0
- Initial release