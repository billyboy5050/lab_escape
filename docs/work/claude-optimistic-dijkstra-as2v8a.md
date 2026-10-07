# Close the developer round

- Branch: `claude/optimistic-dijkstra-as2v8a` (created by the session tool; restarted from `main` after PR #9 merged)
- Pull request: <link, once opened>
- Roadmap item: "Finish the developer round" (Up next, item 1)
- Started: 2026-10-07
- Last updated: 2026-10-07, by Claude (Claude Code)

## Goal

Finish the round 1 record with what the owner has now supplied: the playtest report over all fights, a fifth Hybrid fight (another win), "no other major bugs", and the drone report ("it moved towards enemies and got shot every time"). Done means the implementation notes hold the report's figures and the drone finding with evidence, and the roadmap item is deleted with any follow-ups added.

## Status

In progress: investigating the drone report; record not yet updated.

## Done so far

- The drone report is the rules as written, not a code bug: `droneTurn` flies to within zap range of the nearest enemy, Guards shoot the nearest player-side unit for 3, and the drone has 3 HP. Greedy-bot fights confirm it (most drones die in the round they are summoned or the next, mostly to Guards).

## Next steps

1. Add the report's figures and the drone finding to the developer round in `docs/implementation-notes.md`, delete the roadmap item, and update the tuning candidates.

## Decisions

- 2026-10-07: Record the drone as a design finding with evidence and options for the owner; do not change content or rules in this branch.

## Open questions

- How the owner wants the drone fixed (content: more HP or zap range; or a rule change to its behaviour).

## Notes for whoever picks this up

- The owner restamped the round 1 replays from engine 0.1.0 to 0.1.1 so `npm run sim -- telemetry` would read them; every replay ran to its end.
- Simulator worker threads fail in this container (`Cannot find module .../src/sim/batch` from `worker.ts`, Node 22.22.0, tsx 4.23.15); `--workers 1` works.
