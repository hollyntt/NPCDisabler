# Changelog

All notable changes to PModeration will be documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
## [1.1.5] - 2026-03-16
- Code cleanup

## [1.1.4] - 2026-03-09

### Fixed
- Weird bug that was found.. i dont know it has nothing to do with PModeration fully lol.

# ChatColors 
im looking at you but its not of you issue at all.

# DiceAndColor.. 
your CROLL patch needs a 1-line fix pls apply this when u see this

- "text3 == "/croll" after splitting on space and lowercasing array[0]. When ChatColors wraps it first, array[0] becomes <color=#HEX>/croll</color> so the comparison fails completely."

heres ur fix, strip yo thang b4 applying thx.

```string text3 = Regex.Replace(text2, "<.*?>", "");```

## [1.1.3] - 2026-03-08

### Fixed
- Audio Bug from Release.

## [1.1.2] - 2026-02-22

### Fixed
- UI Bug from Release to 1.1.1.

## [1.1.1] - 2026-02-22

### Fixed
- Made public API extensions avaliable: `HideGlobalName`, `UnhideGlobalName`, `IsGlobalNameHidden`

## [1.1.0] - 2026-02-22

### Added
- New feature: **Hide only @global name** (`_text_playerGlobalName`) without hiding the full player model, RP title, location, or audio
    - Separate UI button in who-list entries (next to main Block button)
    - Dedicated commands: `/hideglobal <name>` and `/unhideglobal <name-or-ID>`
    - Persistent per SteamID (saved to `PModeration_GlobalNameHides.json`)
    - Works independently of full blocking (can hide @name on visible players)

- Enhanced UI button injection:
    - More robust reference button detection (multiple paths + fallback to any Button/Image)
    - Reduced console spam (warnings only once per entry type)
    - Delayed injection timing (WaitForEndOfFrame) for better UI stability

- New help command: `/blockhelp` — displays all commands with descriptions and tips in a clean, colored list

- Public API extensions: `HideGlobalName`, `UnhideGlobalName`, `IsGlobalNameHidden`

### Changed
- Reordered and clarified `/blockhelp` output for better in-game usability

### Fixed
- Global name hide logic now survives soft-block restore loops (applied **after** children `SetActive(true)`)
- Hard-block restore path correctly re-applies @name hide state
- Button icon logic corrected (shows "show" icon when name is hidden, "hide" when visible)

### Notes
- UI buttons now appear more reliably in who-list entries
- All changes remain purely client-side — no server/host impact

## [1.0.0] - 2026-02-19

### Added
- Initial release: client-side player visual & audio hiding
- SteamID-based persistent blocking (survives character swaps)
- Soft block (default – disable renderers/canvases/audio) & optional hard block (full GameObject disable)
- Optional chat message censor ("------" or custom)
- Console commands: `/block`, `/unblock`, `/blocklist`, `/blockexport`, `/blockimport`
- EasySettings UI integration for config
- Public API (`PModerationAPI`) for other mods to check/add/remove blocks and listen to events
- Periodic scene refresh + force refresh on changes
- Debug logging toggle
