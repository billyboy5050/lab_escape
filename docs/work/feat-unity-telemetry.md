# Telemetry and replays in the Unity client

- Branch: `feat/unity-telemetry`
- Pull request: <link, once opened>
- Roadmap item: M13, part 1 "Telemetry and replays" (Up next)
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

The first part of M13, which the owner asked to start on 2026-11-12. The Unity client saves each fight's telemetry as the web client does, and plays a saved fight back, so playtests can move to Unity. Done means:

- At the end of a fight (not a replay), the same three files as the web client, `replay.json`, `summary.json` and `events.jsonl`, in a folder named by `Labels.TelemetryFolderName`: under the repository's `telemetry/` in the editor, where `npm run sim -- telemetry` reads them; under the app's data folder in a Mac or Windows build; offered as downloads in the web build, as the phase plan asks.
- A tester ID (`-labTester`, or `?tester=` in the web build), remembered, and attempts numbered per tester as in the web client. Saved preferences are untrusted input.
- A saved fight plays back from the start screen's list of recent fights, from `-labReplay <file>`, or from `?replay=` in the web build. Every replay passes `Records.CheckCompatible` first; one that does not is refused with its reason.
- Tests, README, implementation notes, roadmap.

## Status

In progress: work note and draft pull request.

## Done so far

- 

## Next steps

1. Add `Logic/Telemetry.cs` (no Unity code): the three files for a session, and attempt numbering over an injectable store.

## Decisions

- 2026-11-12: replay loading starts on the start screen and the command line; the debug overlay's replay controls (pause, step) come with M13's part 2, as in the web client, where they live in the debug panel.

## Open questions

- None yet.

## Notes for whoever picks this up

- Unity 6.3 LTS (6000.3.28f1). Close the editor before batch-mode runs.
