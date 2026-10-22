# Isometric board view

- Branch: `feat/isometric-board`
- Pull request: to be opened
- Roadmap item: none (owner request after the 2026-10-20 restyle and the concept art)
- Started: 2026-10-22
- Last updated: 2026-10-22, by Claude (Opus 5.5)

## Goal

Add an isometric (tilted three-quarter) view of the fight board to the web client, chosen by a toggle, with today's top-down view kept as the default. The owner liked the concept art of the room seen at an angle and wants to try it in the real game. Round 2 has not run yet and a presentation change confounds comparisons between rounds, so isometric stays opt-in until the owner decides.

Done: both views play a whole fight with the same input, previews, intents, effects and debug overlay; the view is a saved preference with a `?view=iso` URL override; client only (no engine, rules or content change); docs updated; the pull request has screenshots of both views and a clean Codex review.

## Status

In progress: work note written, implementation not started.

## Done so far

- Branch cut from `main` at c399c0c; baseline `npm test` green (504 tests).

## Next steps

1. Add a projection abstraction to `src/client/art/geometry.ts` (tile centre, tile outline, floor transform, depth key, board size) with top-down and isometric versions, and route `Board.tsx`, `fx.tsx`, `terrain.tsx` and `objects.tsx` through it.

## Decisions

- 2026-10-22: projection taken from the concept canvas (Weapons wing board): floor in `matrix(1 0.5 -1 0.5 ox oy)` with 64-px tiles, so tile (x, y) centres at (ox + 64(x−y), oy + 32(x+y) + 32). Upright art stays front-on at projected centres, drawn back to front by x+y. Units about 1.3 times their top-down size relative to a tile.
- 2026-10-22: the briefing screen's map stays top-down.

## Open questions

- None yet.

## Notes for whoever picks this up

- Client only. No engine, rules or content change, so no `ENGINE_VERSION` bump and no golden or report regeneration.
- `UnitToken` relies on a stable DOM order for its CSS slide (comment in `Board.tsx`); depth sorting must not break it.
