## How to use

1. Download `LSA_v0.4.4.zip`
2. Extract the archive
3. Start the LoL client
4. Run `LSA.exe`
5. Done

## Hotkeys
- `Ctrl+Shift+O`: Toggle overlay visibility
- `Ctrl+Shift+C`: Toggle click-through mode
- `Ctrl+Shift+P`: [DEV] Cycle Mock phase

## Included files
- `LSA.exe`: Main app (self-contained)
- `data/`: Augment/item/champion data

## v0.4.4 Changed
- Reworked overlay to a compact no-scroll layout
- Reduced overall UI size/font/spacing by 50%+
- Removed explicit `S/A/B/C` tier letters, kept tier color dots only
- Removed on-screen `reason` text and augment selection hint flow

## v0.4.4 Removed
- Removed visible connection log and hotkey guide blocks from overlay
- Cleared unnecessary OP.GG source strings in `knowledge_base.json`:
  - `notes: source: op.gg aram-mayhem + communitydragon ko_kr`
  - `reason: OP.GG S-tier (ARAM Mayhem)`
  - `reason: OP.GG alternative core build`
