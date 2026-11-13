# Shortcuts and settings in the Unity client

- Branch: `feat/unity-shortcuts-settings`
- Pull request: <link, once opened>
- Roadmap item: M13, part 1, "Shortcuts and settings"
- Started: 2026-11-13
- Last updated: 2026-11-13, by Claude

## Goal

Close the gaps between the web client's keyboard handling and the Unity client's, and give the Unity client the reduced-motion setting its roadmap note asks for. Modified keys (Ctrl, Cmd, Alt) are left to the system on every screen, not only in a fight; Shift+Z does not undo, as in the web client; and a "reduce motion" setting, remembered between sessions, switches off what shakes, slows, flashes and pops on screen. Done when each has tests and the README and implementation notes describe it.

## Status

In progress: the branch has just been cut.

## Done so far

- Compared the web client's keys (`src/client/screens/FightScreen.tsx`, `App.tsx`, `actionKeys.ts`) with `FightDirector.ReadKeys` and `ReadStartKeys`. Every web key is in Unity except V, the web board's view switch, which has no Unity counterpart (one fixed camera). The gaps are the modifier guard on the screens before a fight, and Shift+Z.

## Next steps

1. `KeyRules` (no Unity code) and the modifier guard on `ReadStartKeys`; Shift+Z.
2. `Motion` (the setting, remembered through `IPrefs`) and its effects: the camera's shake, kick, punch and entrance, the board clock's hit stop and slow-down, the edge flashes, the lights' strike and alarm, the HP jolt, the pop-in of banners and cards, the typewriters, the units' idle bob.
3. A toggle: the C key, a button in the fight's controls and one on the title.
4. Tests, README, implementation notes and roadmap.

## Decisions

- 2026-11-13: the setting is a remembered toggle, default off. Reading the system's reduce-motion preference is left as a follow-up, because it needs a different call on each platform and the web one can only be tried in a browser.
- 2026-11-13: unit bodies still move (shoves, squash, walking, lunges): they say what happened. The setting acts on the screen, the camera, the clock and the flashes.

## Open questions

- None.

## Notes for whoever picks this up

- The web client's reduced motion is the `prefers-reduced-motion` media query only; it has no setting.
