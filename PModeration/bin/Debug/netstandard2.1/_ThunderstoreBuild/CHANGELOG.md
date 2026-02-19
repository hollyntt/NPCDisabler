# Changelog

All notable changes to PModeration will be documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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