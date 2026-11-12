# Presentation polish for the Unity fight

- Branch: `feat/unity-presentation-polish`
- Pull request: <link, once opened>
- Roadmap item: "Sound on the Unity fight's cues" (Smaller items); the rest is new, ahead of the M12 owner checkpoint
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

Quick fixes that close the gap between the Unity fight's presentation and the web client's before the owner's M12 checkpoint, after the combat-weight pass (PR #23). The owner asked for the fixes that make the biggest difference, and chose on 2026-11-12 to pull sound forward from M13.

Done means, in the Unity client only (no rule or `content/` change):

1. Tile coordinates (A–H, 1–8) along the board's near edges, and the hovered tile named in the Scanner.
2. The web client's type: its condensed display face and its monospace, loaded from the OS at run time (no font files).
3. Panel frames (corner ticks, LED, lighter top edge) and keycaps for key hints.
4. A backdrop (glow and grid) instead of the flat void, a drop shadow and hazard stripes on the slab, and contact shading so blocks and figures stand on the floor.
5. Soft hurt and alarm flashes.
6. An end card with the fight's figures from `FightSession.Summary()`.
7. One "not your turn" instead of one per button; an ability's rule in the Scanner on hover (tooltips do not show in a build).
8. The web client's synthesised sound, each sound on its cue, with M to mute.

## Status

In progress: work note and draft pull request.

## Done so far

- 

## Next steps

1. Take "before" screenshots: build the Mac player and run `-labSmoke reports/winning-line-tech.json` at 1600×900 into a scratch folder.

## Decisions

- 2026-11-12: fonts come from the OS, as the web client's do (`--display` and `--mono` in `src/client/styles.css`), so the project still keeps no font files. The web build has no system fonts and keeps Unity's default.
- 2026-11-12: the owner chose to include sound in this branch rather than leave it for M13.

## Open questions

- None yet.

## Notes for whoever picks this up

- Close the Unity editor before batch-mode tests or builds (README, Unity client).
- Plan: one commit per item above, in that order.
