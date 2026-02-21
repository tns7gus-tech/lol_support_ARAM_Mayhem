# Changelog

All notable changes to this project will be documented in this file.

## [v0.4.4] - 2026-02-21

### Changed
- **Minimal Overlay UI**: Reworked overlay layout to a compact no-scroll format with auto height (`SizeToContent=Height`) so augment/item info is visible at once.
- **Compact Typography/Spacing**: Reduced panel width, font sizes, paddings, and row heights by over 50% for a denser ARAM in-game footprint.
- **Tier Badge Simplification**: Removed explicit `S/A/B/C` text badges and retained only tier color dots for faster visual scanning.

### Removed
- **Reason/Hint Noise in Overlay**: Removed on-screen reason texts, augment selection hint box, and click-to-filter flow from the recommendations panel.
- **Auxiliary Panels**: Removed visible connection log/hotkey guidance blocks from overlay content to keep only core recommendation data.
- **Data Source Noise Text**: Cleared OP.GG source note/reason strings (`notes: source: op.gg...`, `reason: OP.GG S-tier...`, `reason: OP.GG alternative core build`) from `knowledge_base.json`.

## [v0.4.3] - 2026-02-21

### Fixed
- **Korean Encoding Recovery**: Repaired mojibake in item/champion names inside `knowledge_base.json` by re-syncing names from Data Dragon ko_KR with UTF-8-safe loading.
- **Overlay Label Localization**: Changed item section label from `Situational` to `상황별` in overlay UI.

### Changed
- **Data Sync Script Encoding Path**: Updated `scripts/update_knowledge_base_from_opgg.ps1` to load Data Dragon JSON via explicit UTF-8 text download before parsing.

## [v0.4.2] - 2026-02-21

### Added
- **OP.GG ARAM Mayhem Full Sync Script**: Added `scripts/update_knowledge_base_from_opgg.ps1` to collect champion-specific augment/item data for all champions and update `knowledge_base.json`.
- **OP.GG Raw Fetch Script**: Added `scripts/fetch_opgg_aram_mayhem_builds.ps1` for reusable champion build data extraction.

### Changed
- **Augment Display Scope**: Overlay now displays the full champion recommendation list instead of truncating to Top 8 (`OverlayWindow` no longer applies `.Take(8)`).
- **Champion Data Coverage**: Updated `knowledge_base.json` with full champion-specific S-tier augment lists and item builds from OP.GG ARAM Mayhem.

### Fixed
- **Smolder/Champion Specificity**: Prevented global S-tier mixing by enforcing champion-specific augment pools in recommendation scoring.
- **Unknown Augment Names**: Removed unresolved `???` entries by syncing augment IDs/names through CommunityDragon mapping during data generation.

## [v0.3.8] - 2026-02-19

### Added
- **Multi-Monitor Visibility Guard**: Overlay position is now clamped to the current virtual desktop bounds at startup, on display changes, and when re-showing the window.
- **Display Change Recovery**: Automatically repositions the overlay when monitor layout/resolution changes so it does not stay off-screen.
- **In-Game Visibility Guidance**: Added an in-app note recommending `Borderless/Windowed` mode for reliable overlay display.

### Fixed
- **Off-Screen Restore**: Invalid or out-of-range saved coordinates are corrected to visible positions.
- **Topmost Reassertion**: Overlay now reasserts topmost state after display changes and when toggling visibility/click-through.

## [v0.3.7] - 2026-02-19

### Added
- **Safe In-Game Static Mode**: Recommendations are now frozen when phase enters `InProgress`, keeping the pick-screen reference visible without in-game auto-updates.
- **Freeze State Logging**: Overlay `Connection Log` now records when recommendation freeze turns on/off.

### Fixed
- **In-Game Recommendation Reset**: Prevented fallback polling and champion-change events from clearing recommendations during in-game phase.
- **LCU Runtime Diagnostics**: Added runtime connection logs for websocket connect/disconnect and reconnect attempts/results to make disconnect causes visible in overlay.

## [v0.3.6] - 2026-02-19

### Added
- **Connection Log Copy UX**: Replaced the connection log list with a selectable read-only text box and expanded retained history to 50 lines.
- **Click-through Status Indicator**: Added `CT ON/OFF` status text in the top status bar so click-through mode is visible at a glance.

### Fixed
- **LCU Lockfile Read Reliability**: Added shared-read retries for temporarily locked lockfile access and transient malformed content during lockfile updates.
- **LCU Probe Fallback Flow**: If lockfile parsing fails on one candidate path, probing now continues to remaining candidates instead of stopping early.
- **Connection Validation**: Startup/reconnect now require successful REST phase probe before treating LCU as connected.
- **Reconnect Resource Handling**: Disposes previous `HttpClient` before recreating it during reconnect attempts.

## [v0.3.5] - 2026-02-19

### Fixed
- **Korean Text Recovery**: Restored corrupted Korean text in `README.md`, `release_notes.md`, and overlay UI strings.
- **Release Documentation**: Rewrote release notes for readable Korean and aligned package reference to `LSA_v0.3.5.zip`.

## [v0.3.4] - 2026-02-18
### Added
- **Connection Diagnostics UI**: Added in-overlay `Connection Log` panel to show recent LCU connection attempts and fallback reasons.
- **LCU Diagnostics Capture**: `LcuProvider` now records lockfile probing and HTTP connection failures for easier troubleshooting.

### Changed
- **App UX**: Added single-instance guard, tray icon/menu, and explicit `X` exit button on overlay header.

## [v0.3.3] - 2026-02-18

### Fixed
- **LCU Detection (PC Bang)**: Added `config.lol.installPath`-based lockfile lookup and expanded lockfile discovery to include `E:` and all local fixed drives under common Riot install patterns.
- **Startup Connection**: `OverlayWindow` now passes configured LoL install path into `LcuProvider`, reducing false fallback to Mock mode on non-standard installations.

## [v0.3.2] - 2026-02-17

### Changed
- **Branding**: Removed all `Your.Gengi` naming from app metadata/UI and project planning docs.

## [v0.3.1] - 2026-02-17

### Fixed
- **LCU WebSocket**: Fixed fragmented frame handling in `LcuProvider` receive loop to prevent dropped or malformed event messages.
- **LCU Monitoring**: Added guard against duplicate monitoring starts and awaited monitor/socket tasks on stop for cleaner lifecycle handling.
- **Overlay UI**: Cleared recommendation panel when champion becomes unselected (`null`) to avoid stale champion data in UI.
- **Performance**: Replaced per-bind brush allocations with cached frozen brushes in `OverlayWindow` tier rendering.
- **Tests**: Updated `MockProviderTests` to use `async/await` instead of blocking `.Result` in phase cycle assertions.

## [v0.3.0] - 2026-02-17

### Added
- **Data**: Populated `knowledge_base.json` with baseline recommendation data for all 169 champions.
- **Data**: Added `dps` tag support in `knowledge_base.json` rules for better marksman recommendations.
- **Security**: Hardened WebSocket TLS validation in `LcuProvider` to strictly check for loopback connections.
- **Docs**: Added `walkthrough.md` detailing Codex changes verification.

### Fixed
- **Tests**: Fixed unit test `RecommendationServiceTests` failure due to champion name mismatch ("Jinx" -> "吏뺥겕??).
- **Data**: Removed invalid champion entries ("?먰뿨", "?좊굹??) from `knowledge_base.json`.
- **Cleanup**: Removed unused `Class1.cs` file and stale remote branches.

### Changed
- **HotKey**: Implemented configuration-driven hotkey registration in `HotKeyService`.
- **Performance**: Optimized augment lookup to O(1) in `RecommendationService`.

## [v0.2.0] - Previous Release
- Initial release with overlay functionality.

