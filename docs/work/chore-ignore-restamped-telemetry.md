# Ignore the restamped round-1 telemetry

- Branch: `chore/ignore-restamped-telemetry`
- Pull request: to be linked once opened
- Roadmap item: none
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude Code

## Goal

Add the owner's `telemetry-0.1.0-restamped/` folder to `.gitignore`, so `git add -A` on any branch cannot pull it in. The folder stays where it is. Done when `git status` no longer lists it and the pull request has passed review.

## Status

In progress.

## Done so far

- Branch cut from an up-to-date `main`.

## Next steps

1. Add `/telemetry-0.1.0-restamped/` to `.gitignore`, and check with `git status` that the folder is no longer listed.

## Decisions

- 2026-11-08: ignore the exact folder name, not a pattern, because this is the only restamped copy.

## Open questions

- None.

## Notes for whoever picks this up

The folder is the copy of the 22 round-1 fights that the owner restamped from engine 0.1.0 to 0.1.1 (see "How the figures were gathered" in the implementation notes). Do not commit or delete it.
