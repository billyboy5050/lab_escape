# C# simulator and CI (M10)

- Branch: `feat/csharp-simulator`
- Pull request: to be opened
- Roadmap item: M10: C# simulator and CI
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude

## Goal

Port the headless simulator and telemetry from TypeScript to C#, as `core/Sim/` on the existing rules library, and run the C# tests in CI. Done when the same seeds reproduce the committed reports byte for byte, the C# tests run inside the existing `check` job, and the tests the rules core left for this milestone are ported (see "Tests not yet ported" in the implementation notes).

## Status

In progress: reading the TypeScript simulator and the C# core.

## Done so far

- Baseline green: 1,584 TypeScript tests, 298 C# tests.

## Next steps

1. Port `src/telemetry` and `src/sim` to C#, then compare its reports with `reports/`.

## Decisions

## Open questions

## Notes for whoever picks this up

