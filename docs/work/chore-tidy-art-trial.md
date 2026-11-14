# Tidy up after the art trial's merge

- Branch: `chore/tidy-art-trial`
- Pull request: <link, once opened>
- Roadmap item: "How to source 3D art", under Decisions waiting on the owner
- Started: 2026-11-14
- Last updated: 2026-11-14, by Claude

## Goal

Pull request #34, the art trial, was merged into `main` by accident on 2026-11-14. It was an experiment, but the owner played its generated unit models and wants to keep them. This branch keeps the models and removes the rest: the Quaternius trial's editor scripts (which need packs git does not hold), their `.gitignore` lines and the trial's work note. It moves the trial's findings into the roadmap, documents the models and `-labGreyboxUnits`, and answers Codex's three findings on #34. Done is `main` as if the models had arrived on a proper `feat/` branch.

## Status

In progress.

## Done so far

- 

## Next steps

1. Delete `unity/Assets/ArtTrial/` and the packs' lines in `.gitignore`.

## Decisions

- 2026-11-14: fix forward, not revert. The owner chose to keep the generated models; reverting and re-adding them would end in the same `main` with more churn. Git history keeps the trial's scripts for anyone who wants to run it again.

## Open questions

- None yet.

## Notes for whoever picks this up

- The trial's findings and how to rerun it are in `docs/work/chore-art-trial-quaternius.md` until this branch deletes it; after that, in commit 9ac7ad2.
