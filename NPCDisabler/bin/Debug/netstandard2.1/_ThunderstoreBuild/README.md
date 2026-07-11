# PModeration

**Personal client-side player hiding & blocking for Atlyss**  
Hide annoying players (oversized models, distracting RP titles, floating names, audio spam) without muting chat or affecting anyone else.

[**Join the Discord Community**](https://discord.gg/ePhX4Fb2we)

### Features

- **Silent local hiding** — Completely removes a player's visuals, nameplate (@global + RPNames title), model, attached effects, and audio locally.  
- **SteamID-based** — Blocks persist across character swaps, logouts, and re-joins.  
- **Separate from mute** — Hide visuals while still chatting (perfect for oversized/obstructing characters you still want to talk to).  
- **Optional chat censor** — Replace blocked players' messages with "------" (or custom text) if you want full silence.  
- **Easy controls** — In-game commands: `/block <name>`, `/unblock <name-or-ID>`, `/blocklist`, `/blockexport`, `/blockimport`.  
- **Configurable** — Toggle hard block (full GameObject disable) vs soft block (safer for parties), debug mode, and more via EasySettings UI.  
- **Public API** — Other mods can check/add/remove blocks or listen to events (`PModeration.PModerationAPI`).  

Purely **client-side** — no host involvement, no mutual visibility, no drama. Just personal boundary control.

### Installation

1. Install via Thunderstore Mod Manager or manually.  
2. Requires:  
   - BepInEx  
   - Nessie.ATLYSS.EasySettings  
   - Soggy_Pancake.AtlyssCommandLib  
   - CodeTalker  
3. Launch game → configure via Mod Settings menu or edit `BepInEx/config/PModeration.cfg`

### Commands (all client-side)

- `/block <name>` — Hide a player  
- `/unblock <name-or-SteamID>` — Unhide  
- `/blocklist` — Show blocked IDs  
- `/blockexport` — Save blocklist to file  
- `/blockimport <path>` — Load blocklist from file  
- `/blockhelp` — Shows command lists
- `/hideglobal <name>` — hides only the @GlobalName text for that player (model, RP title, location, etc. stay visible)
- `/unhideglobal <name-or-ID>` — shows the @GlobalName again

### Configuration (via Mod Settings or .cfg)

- **Enabled** — Master switch (default: true)  
- **Hard Block (Advanced)** — Disable entire GameObject (default: false – soft block recommended for party stability)  
- **Censor Chat** — Replace blocked messages (default: false – Atlyss mute already exists)  
- **Replacement Text** — What blocked chat shows (default: "[BLOCKED]")  
- **Debug Mode** — Log actions (default: false)

### Developer API

Other mods can reference `PModeration.PModerationAPI` for integration:

```csharp
using PModeration;

bool isBlocked = PModerationAPI.IsPlayerBlocked(steamID);
PModerationAPI.BlockPlayer(steamID);
PModerationAPI.UnblockPlayer(steamID);
var blockedList = PModerationAPI.GetBlockedSteamIDs();

PModerationAPI.OnPlayerBlocked += id => { /* react */ };
PModerationAPI.OnPlayerUnblocked += id => { /* react */ };
```

See `PModerationAPI.cs` for full docs.

### Credits

- s0apy — Author
- Community feedback from Atlyss Discord (Sparky, Destiny, etc.)

### Notes
- Purely client-side – no host enforcement, no mutual visibility
- Designed to separate visual hiding from Atlyss built-in mute (as requested)
- Tested in lobbies with oversized characters, RP titles, and chat spam

### Known Limitations
- Hiding only works when blocked player is in the same scene/lobby
- Hard block may cause rare party desync – keep disabled by default
- Chat censor optional (redundant with Atlyss mute in most cases)


Enjoy your cleaner lobbies. No babysitting, just personal peace. 🐼
```