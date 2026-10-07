# One-page rules sheet

- Branch: `claude/optimistic-dijkstra-as2v8a` (created by the session tool, so it keeps that name rather than `docs/rules-sheet`)
- Pull request: <link, once opened>
- Roadmap item: "One-page rules sheet" (Up next, item 1)
- Started: 2026-10-07
- Last updated: 2026-10-07, by Claude (Claude Code)

## Goal

Write the one-page rules sheet that the spec's playtest round 2 hands to first-time players ("a one-page rules sheet and no coaching"). It is written from the spec and the rule text in `content/abilities.json`, as a printable page in `docs/`, and says only what a player needs to start a fight: the goal, the turn, the actions, the enemies, the hazards and how to read the screen. Done means the page exists, prints on one A4 or Letter page, matches the current content values, and is linked from the README, CLAUDE.md's document table and the roadmap's playtest items.

## Status

In progress: note written, sheet not yet drafted.

## Done so far

- Oriented: no open pull requests or unmerged branches; `npm test` green (487 tests) on `aa38c91`.

## Next steps

1. Read `docs/spec.md` (rules, controls, playtest round 2) and `content/*.json` (abilities, units, hazards, rules), then draft `docs/rules-sheet.md`.

## Decisions

- 2026-10-07: Markdown in `docs/`, like the other documents, so it diffs and reviews like them and prints from GitHub or any Markdown viewer.

## Open questions

- None yet.

## Notes for whoever picks this up

- Numbers on the sheet come from `content/`. If content changes, the sheet must change with it; say so wherever the sheet is linked.
