# Telemetry and replays in the Unity client

- Branch: `feat/unity-telemetry`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/26
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

Built and verified. Waiting on the owner's hand-played check and on Codex's review.

## Done so far

- `Logic/Telemetry.cs`: the three files, folder names, tester (with `-` to forget) and attempt numbering over an untrusted store, replay loading, the saved-fight list. `TelemetrySink.cs`: where each build saves, and the web build's downloads (`Assets/Plugins/WebGL/LabDownload.jslib`).
- The director saves at the end of a fight of the player's own, plays replays from the start screen, `-labReplay` and `?replay=`, and hides the player's highlights and previews while one plays.
- Tests: EditMode 77/77 (12 new), PlayMode 16/16 (3 new, and the winning-line test checks the saved files and the start screen's list). Tests and the smoke run save to their own folder.
- Checked in builds: the Mac build saves the three files with the tester and attempt (batch mode, since the display was asleep), and `npm run sim -- telemetry` reads them; the web build plays `?replay=` to victory with no new save offered, and refuses a missing file with its name.
- Docs: README, implementation notes, roadmap, CLAUDE.md.

## Next steps

1. The owner plays a fight in the editor or the Mac build, checks the end card's "Saved to" line, then watches it from the start screen's list.
2. Answer Codex's findings, delete this note in the last commit, and mark the pull request ready.

## Decisions

- 2026-11-12: replay loading starts on the start screen and the command line; the debug overlay's replay controls (pause, step) come with M13's part 2, as in the web client, where they live in the debug panel.

## Open questions

- None yet.

## Notes for whoever picks this up

- Unity 6.3 LTS (6000.3.28f1). Close the editor before batch-mode runs.
