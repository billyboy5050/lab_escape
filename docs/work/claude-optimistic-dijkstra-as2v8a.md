# Record the developer playtest round

- Branch: `claude/optimistic-dijkstra-as2v8a` (created by the session tool; restarted from `main` after PR #8 merged)
- Pull request: <link, once opened>
- Roadmap item: "Developer playtest round" (Up next, item 1)
- Started: 2026-10-07
- Last updated: 2026-10-07, by Claude (Claude Code)

## Goal

Record the owner's developer playtest round (spec round 1) as the roadmap asks: findings in `docs/implementation-notes.md`, bugs and follow-ups as roadmap items, and the round's item deleted. Done means the record holds what was played, the owner's impressions, the figures from the result screens, how they compare with the simulator, and what round 1 changes about the tuning candidates.

## Status

In progress: note written, record not yet drafted.

## Done so far

- Oriented: `main` at `1157ce2`, `npm test` green (492 tests). The telemetry folder is on the owner's machine, not in this container.

## Next steps

1. Add a "Developer playtest round" section to `docs/implementation-notes.md` from the owner's counts (21 fights) and the three result screens they shared, then update the roadmap.

## Decisions

- 2026-10-07: Work from the owner's counts and three result screens, since `telemetry/` is not committed. The full metrics need `npm run sim -- telemetry --include-unlabelled` run on the owner's machine (round 1 fights have no tester ID).

## Open questions

- Which preset won the second of the two wins?
- Were there any bugs?

## Notes for whoever picks this up

- Round 1 fights have no tester ID, so the playtest report skips them unless given `--include-unlabelled`.
