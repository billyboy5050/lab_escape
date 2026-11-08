# Rooms as data, any size (M11)

- Branch: `feat/rooms-as-data`
- Pull request: to be added once opened
- Roadmap item: M11: Rooms as data, any size
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude

## Goal

Give the engine one room model (floor plan, hazards, waves and round cap together), so a fight can take place in any room of any size. The Armory must play identically from it, in TypeScript and C#, and rooms of other sizes must load and play headless. Done means the acceptance line for M11 in `docs/phase-2.md` holds and CI proves it.

## Status

In progress.

## Done so far

- Read the code: the board is already data-driven, but the engine reads one `c.map`, `c.waves` and `rules.roundCap`, and C# holds one `Board`.

## Next steps

1. Add `RoomDef`, `Content.rooms` and `FightSettings.room` in TypeScript, and make the engine read the fight's room.

## Decisions

- 2026-11-08: `content/map.json`, `content/waves.json` and `rules.roundCap` do not change, and no `content/rooms.json` is added until M14. The Armory's room is built from those files on load, so the content hash, the golden replays and the committed reports stay byte for byte the same during the freeze. `ContentFiles.rooms` is optional and only hashed when present.

## Open questions

- None.

## Notes for whoever picks this up

Every change has to land in both `src/` and `core/`, and `dotnet run --project core/Sim -c Release -- conformance` must still pass.
