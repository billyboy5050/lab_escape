# C# rules core (M9)

- Branch: `feat/csharp-rules-core`
- Pull request: to be opened
- Roadmap item: M9: C# rules core
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude

## Goal

Port the rules core (`src/state`, `util`, `content`, `rules`, `effects`, `ai`, `engine`, `preview`) to a plain C# library at `core/Core/` with no Unity dependency, with NUnit tests at `core/Tests/`. It reads the same `content/*.json`. Done means: the C# content hash equals the TypeScript hash; replaying `golden/*.json` reproduces `golden/*.events.jsonl` exactly; the rule tests are ported to NUnit; and previews match results over 100 random states. No rule or content value changes.

## Status

In progress: the .NET SDK is approved (2026-11-08) and being installed with Homebrew; the port has not started.

## Done so far

- TypeScript baseline green: 528 tests in 27 files.

## Next steps

1. Read every file in the eight folders in scope, then write the project skeleton in `core/`.

## Decisions

- 2026-11-08: the owner approved `brew install dotnet` (SDK 10.0.403). `core/Core` targets `netstandard2.1` with C# 9, the level Unity's LTS supports, so the same source compiles there.

## Open questions

- None yet.

## Notes for whoever picks this up

The TypeScript engine stays authoritative and must not change. The simulator is M10 and the Unity client is M12: both are out of scope.
