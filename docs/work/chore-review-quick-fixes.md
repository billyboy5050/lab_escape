# Quick fixes from the repository review

- Branch: `chore/review-quick-fixes`
- Pull request: to be opened with this note
- Roadmap item: Vite's native config loader (under Smaller items), plus new items this review adds
- Started: 2026-11-09
- Last updated: 2026-11-09, by Claude

## Goal

The owner asked for a review of the repository as it stands on 2026-11-09, with the quick and easy fixes made. This branch holds those fixes only: tooling and housekeeping, no rule, content or client behaviour. Done means `npm test` runs only this checkout's tests, `npm audit` is clean, the Vite config warning is gone, and the gaps the review found are in the roadmap.

## Status

In progress.

## Done so far

- Baseline checked on `main` at d74d113: `npm test`, `npm run build`, `dotnet test core/Tests`, `conformance` (18 of 18 identical) and the Unity EditMode (20) and PlayMode (8) tests all pass.

## Next steps

1. Limit Vitest to `tests/`, so it stops running the copies under `.claude/worktrees/`.
2. Run `npm audit fix` for the Vite advisory and rerun the tests and the build.
3. Add the `.ts` extension to the import in `vite.config.ts`, with `allowImportingTsExtensions`.
4. Commit `unity/ProjectSettings/PackageManagerSettings.asset`, which the editor wrote and nothing ignores.
5. Record the review's findings in the roadmap, then delete this note.

## Decisions

- 2026-11-09: suggestions about the plan itself (milestone order, tester rounds) go to the owner in conversation and not into this branch, because they are the owner's decisions.

## Open questions

- None yet.

## Notes for whoever picks this up

Nothing here touches `src/` rules, `content/`, `core/` or the Unity scripts, so no generated file needs regenerating.
