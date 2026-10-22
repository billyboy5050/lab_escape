# Isometric board view

- Branch: `feat/isometric-board`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/14
- Roadmap item: none (owner request after the 2026-10-20 restyle and the concept art)
- Started: 2026-10-22
- Last updated: 2026-10-22, by Claude (Opus 5.5)

## Goal

Add an isometric (tilted three-quarter) view of the fight board to the web client, chosen by a toggle, with today's top-down view kept as the default. The owner liked the concept art of the room seen at an angle and wants to try it in the real game. Round 2 has not run yet and a presentation change confounds comparisons between rounds, so isometric stays opt-in until the owner decides.

Done: both views play a whole fight with the same input, previews, intents, effects and debug overlay; the view is a saved preference with a `?view=iso` URL override; client only (no engine, rules or content change); docs updated; the pull request has screenshots of both views and a clean Codex review.

## Status

Implementation, tests, docs and browser checks done; waiting on a clean Codex review of the latest push, then the screenshots and this note are deleted and the pull request marked ready.

## Done so far

- Projection in `src/client/art/geometry.ts`; isometric room and blocks in `src/client/art/iso.tsx`; `Board.tsx` draws a top-down scene (unchanged layer order) or an isometric one (floor layer, depth-sorted blocks/objects/units, HUD layer above).
- Slides tweened in JS on the isometric board (`src/client/useSlides.ts`), with a timer fallback for hidden pages.
- Occluding blocks fade; click boxes on figures; flat objects (mines, corpses) lie on the floor, previews included.
- Toggle: Top/Iso control, `V`, saved preference, `?view=`; replay `meta.view` (top, iso, both) validated in `recordProblems`.
- Tests: `tests/isometric.test.tsx`, plus `meta.view` cases in `tests/record.test.ts`.
- Top-down markup checked byte-identical to `main` for several states (script below).
- Browser checks: both views, both themes, narrow and phone widths, shortcuts, hover previews, intents, moves and undo, shots, a kill, the golden Tech replay end to end in Iso with the debug overlay.
- Docs: README, spec changes table, implementation notes, roadmap.
- Screenshots for the pull request description in `docs/work/feat-isometric-board/`.

## Next steps

1. Check the Codex review on the latest push (`gh api repos/billyboy5050/lab_escape/pulls/14/comments`); fix or answer each finding, reply and resolve its thread, push, and comment `@codex review` again.
2. When the review is clean: delete this note and `docs/work/feat-isometric-board/` in one last commit, push, request a final Codex review, and mark the pull request ready (`gh pr ready 14`). The pull request description links the screenshots by commit SHA, so they stay visible after the deletion.

## Decisions

- 2026-10-22: projection taken from the concept canvas (Weapons wing board): floor in `matrix(1 0.5 -1 0.5 ox oy)` with 64-px tiles, so tile (x, y) centres at (ox + 64(x−y), oy + 32(x+y) + 32). Upright art stays front-on at projected centres, drawn back to front by x+y. Units 1.3 times their top-down size relative to a tile.
- 2026-10-22: the briefing screen's map stays top-down.
- 2026-10-22: the top-down view keeps its old layer order and markup, so playtests on the default view see no change.
- 2026-10-22: the URL's `?view=` wins for that page but is not saved; switching views saves the choice and rewrites `?view=` if present.
- 2026-10-22: the replay records the view (`meta.view`), because the saved preference could otherwise carry an isometric view silently into a playtester's fight. The playtest report does not use it yet (roadmap item).
- 2026-10-22: blocks fade when they would hide a unit, an object or the hovered tile; units never fade, and bars and chips go on a top layer instead.

## Open questions

- For the owner: whether isometric becomes the default, and whether any playtest round uses it (roadmap, Decisions waiting on the owner).

## Notes for whoever picks this up

- Client only, apart from the optional `meta.view` field in `src/telemetry/record.ts`. No engine, rules or content change, so no `ENGINE_VERSION` bump and no golden or report regeneration.
- To compare the top-down markup with `main`: add a worktree of `origin/main`, symlink `node_modules` into it, and render `Board` with `renderToStaticMarkup` from both trees for the same states (run `tsx` with each tree's `tsconfig.json`); the outputs should be identical.
- To watch a whole fight in the isometric view: copy `golden/<preset>.json`'s `record` into `telemetry/_iso-check/<preset>.json` (ignored by git) and open `/?replay=telemetry/_iso-check/<preset>.json&view=iso`.
