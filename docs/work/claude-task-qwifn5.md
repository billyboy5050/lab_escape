# Report every unit that dies with the player

- Branch: `claude/task-qwifn5`
- Pull request: not yet opened
- Roadmap item: none (found in code review)
- Started: 2026-10-06
- Last updated: 2026-10-06, by Claude

## Goal

`flushDeaths` handles dead units in ID order and the player is ID 0, so when the player dies the fight ends before enemies killed in the same flush are reported (no `UnitDied`, corpse or intercom line). Telemetry kill counts and friendly-fire attribution for lost fights are therefore undercounted. Done: every unit that dies with the player is reported, `ENGINE_VERSION` is bumped, generated files are regenerated, and the notes are current.

## Status

In progress.

## Done so far

- Nothing yet.

## Next steps

1. Add `tests/simultaneous-deaths.test.ts`, fix `flushDeaths` in `src/rules/world.ts`, bump `ENGINE_VERSION`, regenerate generated files.

## Decisions

- 2026-10-06: what was decided, why, and any rejected option worth knowing about.

## Open questions

- Questions for the owner, and anything you are unsure of.

## Notes for whoever picks this up

Gotchas, commands to rerun, half-finished edits, files touched.
