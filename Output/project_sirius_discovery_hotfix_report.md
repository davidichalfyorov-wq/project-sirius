# Project Sirius / LibreLancer + Discovery 4.86 hotfix report

## Applied changes

### P0: UI crash with cimgui disabled
- Replaced unsafe `game!.Debug` access in `UiContext.MouseOnDoubleClick` with `game?.Debug?.CaptureMouse == true`.
- Guarded `RoomGameplay.Draw` debug rendering and cursor code from `Game.Debug == null`.
- Guarded `/debug` chat command in `CGameSession` when DebugView is unavailable.

### P0: Missing string resources on Linux
- Added runtime loading of loose JSON resources from `DATA/strings.json`, `DATA/infocards.json`, or root `strings.json`.
- `InfocardManager` now supports both flat JSON `{ "1271": "..." }` and nested JSON `{ "strings": { ... }, "infocards": { ... } }`.
- Added `src/LibreLancer.Tools.Strings`, a small command-line tool that uses LibreLancer's existing PE resource parser to extract Windows DLL string/infocard resources into `DATA/strings.json`.

Example:

```bash
dotnet run --project src/LibreLancer.Tools.Strings -- \
  "/path/to/Discovery Freelancer 4.86.0" \
  --output "/path/to/Discovery Freelancer 4.86.0/DATA/strings.json"
```

### P0/P1: Voice lookup and NPC rooms
- Added a recursive `DATA/AUDIO` voice index for `.utf` voice packs and loose `.wav` line files.
- `SoundManager` now tries UTF voice packs first, then loose WAV line files by voice ID/hash.
- Fixed `GF_NPC` parser behavior for `knowdb` and `rumorknowdb` groups and preserved `rumor_type2`.
- Added room NPC placement for `BaseRoom.Npcs` using existing `ThnRoomHandler.AddNpc`.
- Added a click hotspot for base NPCs; it shows the first available rumor text in the console/chat path as a minimal interaction fallback.
- Added synthetic fixed-NPC fallback when an `MRoom` fixed NPC references a missing `GF_NPC` record, so the room can still be populated instead of logging a hard failure.

### P1/P2: Discovery INI compatibility
- Extended explosion schema for one-value/multi-value `lifetime` and Discovery-specific `strength`, `radius`, `hull_damage`, `impulse`.
- Added Discovery ship types `WEAPONS_PLATFORM` and `MISSION_SATELLITE`.
- Confirmed `DcDtBt` material handling is already present in `Utf/Mat/Material.cs`.

### P2: Discovery/FLHook key commands
- Added missing key commands to `InputAction`: HUD, cloak, lights, player info, self-destruct, shields, group mark/unmark, jump drive.
- Added RPC methods and client bindings for cloak, jump-drive request, and self-destruct.
- Cloak toggles existing `CloakComponent` if installed.
- Self-destruct damages the player ship through server-side health.
- Jump drive currently acknowledges the command but does not resolve Discovery jump routes/player-base logic yet.

## Verification

- `git diff --check` passes.
- The local container does not have `dotnet` installed, so `dotnet build -c Release src/lancer/lancer.csproj` could not be executed here.
- The uploaded archive contains only `Discovery Freelancer 4.86.0/librelancer.ini` under the Discovery data folder, not the full `DATA/` and DLL/audio assets, so runtime validation against all Discovery 4.86 data could not be performed in this environment.

## Known remaining work

- Generate and place real `DATA/strings.json` from the actual Discovery DLLs with `LibreLancer.Tools.Strings`.
- Validate every base/bar against the full `DATA/MISSIONS/mbases.ini`, especially NPC costumes and fidget markers.
- Replace the simple rumor console fallback with the full Freelancer bar dialogue UI flow.
- Implement real Discovery jump-drive routing, player-owned-base persistence, cruise modifiers, and complete shield management.
- Investigate the intro THN particle/sun renderer warnings using the full asset tree.
