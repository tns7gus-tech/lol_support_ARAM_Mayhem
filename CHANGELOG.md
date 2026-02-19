# Changelog

All notable changes to this project will be documented in this file.

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

