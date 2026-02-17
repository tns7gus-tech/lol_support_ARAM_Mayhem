# Changelog

All notable changes to this project will be documented in this file.

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
- **Tests**: Fixed unit test `RecommendationServiceTests` failure due to champion name mismatch ("Jinx" -> "징크스").
- **Data**: Removed invalid champion entries ("자헨", "유나라") from `knowledge_base.json`.
- **Cleanup**: Removed unused `Class1.cs` file and stale remote branches.

### Changed
- **HotKey**: Implemented configuration-driven hotkey registration in `HotKeyService`.
- **Performance**: Optimized augment lookup to O(1) in `RecommendationService`.

## [v0.2.0] - Previous Release
- Initial release with overlay functionality.
