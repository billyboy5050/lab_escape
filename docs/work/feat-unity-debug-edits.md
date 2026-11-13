# Debug edits in the Unity debug overlay

- Branch: `feat/unity-debug-edits`
- Pull request: <link, once opened>
- Roadmap item: M13, part 1, "Debug edits"
- Started: 2026-11-13
- Last updated: 2026-11-13, by Claude

## Goal

Give the Unity debug overlay the rest of what the spec's debug overlay and the web client's panel have: set a unit's HP, set AP and movement, spawn a unit, force the next wave, hold back wave 3, and restart the fight. The first four are the `debug` commands the C# core already runs, so they go through the fight like any command and are recorded in its replay. Done when each works in the overlay, a replay of an edited fight plays back the same, and the README and implementation notes describe it.

## Status

In progress: the branch has just been cut.

## Done so far

- Read the web panel (`src/client/components/DebugPanel.tsx`) and the C# core's `DebugSetHp`, `DebugSetAp`, `DebugSpawn` and `DebugForceWave` (`core/Core/State/Commands.cs`, `Engine.cs`).

## Next steps

1. A `DebugEdits` class in `unity/Assets/Scripts/Logic` (no Unity code) that holds the selections and builds the commands.
2. The overlay's controls in `Fight.uxml`, `Fight.uss` and `Hud.cs`, and their handlers in `FightDirector.cs`.
3. EditMode and PlayMode tests, then README, implementation notes and roadmap.

## Decisions

- 2026-11-13: no text fields in the overlay. A focused text field would take the game's keys (E, Space, 1 to =) while the player types, so values are steppers and a spawn tile is picked by clicking the board.

## Open questions

- None.

## Notes for whoever picks this up

- The web client's controls apply a command at any time the fight is not busy. Here they are enabled only when the player can act, and not while a saved fight plays back, since an edit would diverge from its recording.
