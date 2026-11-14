# Retire the TypeScript

- Branch: `chore/retire-typescript`
- Pull request: <link, once opened>
- Roadmap item: M13, "Retire the TypeScript web client"
- Started: 2026-11-14
- Last updated: 2026-11-14, by Claude

## Goal

Finish M13 by deleting the TypeScript prototype: the web client, the rules engine, the simulator, their tests and the Node tooling. The owner approved deleting all of it on 2026-11-14, after running the Windows build. The C# in `core/` becomes the only rules engine and simulator, and the Unity client the only client. Done means the repository has no TypeScript or Node tooling left, every check the TypeScript tests made that the C# does not yet make has moved to C#, the git hooks still turn themselves on, CI runs only .NET, and CLAUDE.md, the README and the docs describe the repository as it is.

## Status

In progress: mapping what depends on the TypeScript.

## Done so far

- Branch cut from `main` at 2dfd707.

## Next steps

1. Find everything that depends on the TypeScript: files that read `src/` or `tests/`, TypeScript tests with no C# counterpart, and every mention in the docs, CI, hooks and `.claude/launch.json`.

## Decisions

- 2026-11-14: the owner chose to delete all the TypeScript at M13, not only the web client, so that rules from M14 on are written once. The other option was keeping the TypeScript engine as a cross-check for a while.

## Open questions

- None yet.

## Notes for whoever picks this up

- No content or rule changes on this branch, so no generated file needs regenerating. The conformance check proves the C# still reproduces every committed report.
