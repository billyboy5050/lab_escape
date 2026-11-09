# Move the first tester round ahead of the later wings

- Branch: `docs/phase-2-early-tester-round`
- Pull request: to be opened with this note
- Roadmap item: none; it changes the order of the milestones in `docs/phase-2.md`
- Started: 2026-11-09
- Last updated: 2026-11-09, by Claude

## Goal

A review of the repository on 2026-11-09 suggested four changes to the phase plan, and the owner asked for them to be written into `docs/phase-2.md`. Done means the plan and the roadmap agree on the new order, and nothing in the docs still names a milestone by its old number.

1. Testers play a run after the Weapons wing, before the Xeno and Ritual wings are built, and the owner decides the later wings from the result.
2. The Weapons wing's art has a milestone that owns it.
3. M13 ports the screens that M15 and M17 replace only as far as they work.
4. The art sourcing decision is needed before Stage B starts, not before M20.

## Status

In progress.

## Done so far

- Found every reference to M20 to M25 and to Stages C and D: all are in `docs/phase-2.md` and `docs/roadmap.md`.

## Next steps

1. Rewrite the milestone table in `docs/phase-2.md` (M13's row, Stages C and D), then the sections that name the old numbers: success measures, risks, what the owner needs to do.
2. Change the three matching lines in `docs/roadmap.md`.
3. Check that `git merge-tree` shows no conflict with `origin/chore/review-quick-fixes` (PR #21), which also edits the roadmap.

## Decisions

- 2026-11-09: M22 to M25 are renumbered so the numbers stay in build order. None of them had started, and only the plan and the roadmap named them.
- 2026-11-09: the plan's web-build risk is left as it reads. PR #21 records the first web build in the implementation notes, and this branch does not depend on it.

## Open questions

- None.

## Notes for whoever picks this up

Docs only. PR #21 is open and touches `docs/roadmap.md` near the art-sourcing line; this branch was cut from `main`, not from that branch.
