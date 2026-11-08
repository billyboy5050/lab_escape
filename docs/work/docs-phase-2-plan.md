# Close the playtests and plan the full-game phase

- Branch: `docs/phase-2-plan`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/16
- Roadmap item: First-time player round, Informed player round (both closed by this branch)
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude

## Goal

Milestone M8 of the full-game phase, docs only. Record that the owner closed playtesting as successful on 2026-11-08, and say honestly what was not measured. Commit the plan for the next phase (`docs/phase-2.md`): the move to Unity, runs, room shapes, events that decide upgrades, the kit rework, Occult and three wings. Done means the docs are consistent, nothing still says playtesting is the next step, and the pull request has a clean Codex review.

## Status

In progress.

## Done so far

- Read the roadmap, spec, implementation notes and Design Doc v0.1, and agreed the plan with the owner.
- Baseline on `main`: `npm test` passes 1,056 tests, and `npm run build` passes.

## Next steps

1. Write `docs/phase-2.md` from the plan the owner approved in the session.
2. Record the playtest result in `docs/implementation-notes.md` and `docs/spec.md`.
3. Update `docs/roadmap.md`, `CLAUDE.md` and `README.md`.
4. Run `npm test`, `npm run build` and `grep -rn "M7\|first-time player round\|playtest and tuning" README.md CLAUDE.md docs`.
5. Delete this note in the last commit, run `gh pr ready`, and comment `@codex review`.

## Decisions

- 2026-11-08: the owner marked the playtests successful and closed M7 on testers' verbal feedback. No recordings or telemetry from those testers reached the project, so no figures are written for that round.
- 2026-11-08: owner's decisions for the phase: three short wings; a run starts by picking a branch; weapons plus upgrade paths; a light meta layer, late; events between battles, where a reaction shapes the upgrade offer; rooms beyond 8×8 (halls, hallways, big hazards); move to Unity with the rules ported to plain C#, porting first; 3D with a fixed isometric camera; Mac and Windows plus a web build for testers.

## Open questions

- None for this branch. Decisions due later are listed in `docs/phase-2.md`.

## Notes for whoever picks this up

Docs only: no rule, content or code changes, so the golden replays and reports do not need regenerating.
