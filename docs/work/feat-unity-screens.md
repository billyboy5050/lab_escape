# Title, loadout, briefing and result screens in the Unity client

- Branch: `feat/unity-screens`
- Pull request: <link, once opened>
- Roadmap item: M13, part 1, "Screens, as far as they work"
- Started: 2026-11-13
- Last updated: 2026-11-13, by Claude

## Goal

Give the Unity client the web client's flow around a fight, as far as it works: Title, then the Loadout screen as the Sandbox-mode picker, then the Briefing, then the fight, then the result with its full report. M15 rebuilds this flow around a run (phase plan), so each screen goes only as far as it is useful until then: no title art, and the briefing's map is a plain top-down plan. Done when the whole flow plays in the editor and a Mac build, each screen has tests, and the README and implementation notes describe it.

## Status

In progress: the branch has just been cut.

## Done so far

- Read the web screens (`src/client/screens/`) and Unity's start and end cards (`Fight.uxml`, `Hud.cs`, `FightDirector.cs`).

## Next steps

1. A screen state in `FightDirector` and the title screen with its typewriter text (`Logic/Typewriter.cs`).
2. The old start screen becomes the loadout picker: presets, abilities, upgrades, "hold back wave 3", the remembered loadout (`Logic/LoadoutPicker.cs`, checked on load).
3. The briefing screen with a plan of the room.
4. The result's full report behind a button on the end card.
5. Tests, README, implementation notes and roadmap.

## Decisions

- 2026-11-13: no text fields, as in the debug overlay. The tester ID is read-only: it comes from `-labTester` or `?tester=` as it does now.
- 2026-11-13: saved fights stay on the loadout screen, where the start screen lists them now, and play back without a briefing.

## Open questions

- None.

## Notes for whoever picks this up

- Fight again (R) and Retry start the fight at once, as the web client's Retry does, without the briefing.
