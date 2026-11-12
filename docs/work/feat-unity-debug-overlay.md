# Debug overlay and step mode in Unity

- Branch: `feat/unity-debug-overlay`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/27
- Roadmap item: M13, part 1 of the remaining list: "Debug overlay and step mode"
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

Port the web client's backtick debug panel to the Unity client: engine version and content hash, unit IDs on the board, spawn tiles and timing of later waves, the hidden wave timer, each hazard's state with its value-rule explanation, and step mode. A replay can be paused and stepped one command at a time. Done means the overlay toggles with the backtick, EditMode and PlayMode tests cover it and pass on 6000.3.28f1, a Mac build smoke run passes, the docs are updated, Codex has reviewed, and the owner has played it before the PR is marked ready.

## Status

In progress: overlay built, EditMode green, PlayMode being made green; docs drafted. Then the Mac build smoke run, Codex review, and the owner's play.

## Done so far

- Branch cut from `main` at 8bdc409; `npm test` green.
- `Logic/DebugReport.cs`, step mode in `FightFlow`, debug log lines in `EventFormatter`, step hint in `ActionModel`, with `Tests/EditMode/DebugOverlayTests.cs` (11 tests, all pass).
- The screen: debug column in `Fight.uxml`/`Fight.uss`, `Hud` API, `BoardView.Hazards`/`WavesShown`/`Spawns` moved on by cues and `Level`, spawn squares on the floor, `BoardTags` ID chips and labels, `FightDirector` keys (backtick, P, N, Shift+N) and `-labDebug`.
- `Tests/PlayMode/DebugOverlayScreenTests.cs` (3 tests). First run: 18 of 19 PlayMode tests passed; the hazard-cue test sampled too coarsely and was rewritten to record the mark at FrameShown and CuePlayed.
- README (Unity client), implementation notes (the debug overlay paragraph) and roadmap (part removed, debug edits added as M13 part 2, the primed glow as a smaller item) drafted.

## Plan

- `Logic/DebugReport.cs` (no Unity code): the panel's text from a state (header, wave timer, hazards with their last value-rule decision, Flamer options, chain-rule count) and the board's marks (spawn tiles of later waves, hazard labels).
- `FightFlow`: step mode (`StepMode`, `Holding`, `Pending`, `Advance(untilPhase)`). In step mode a frame plays out and holds at its end while more are queued; N pumps the next frame, Shift+N runs to the frame before the next phase. Nothing queued: it finishes as usual.
- `EventFormatter`: the web client's debug log lines (hazard holds, cancel reasons, chain suppressed, AI decisions, debug events) behind a flag.
- `BoardView`: hazards and waves "as the board shows them", moved on by hazard and wave cues and by `Level`, so the overlay never reads the state mid-frame. Spawn marks on the floor; `BoardTags` draws unit IDs on the figures' tags, hazard labels, wave labels and Flamer scores.
- HUD: a debug column on the left (`column-debug` in `Fight.uxml`), shown with the backtick, so it covers no board; the camera reframes into what is left.
- `FightDirector`: backtick toggles; P pauses a replay, N steps (the next frame when step mode holds one, else the next command of a paused replay); `-labDebug` opens the overlay for a smoke run.

## Next steps

1. Rerun EditMode and PlayMode (README's Unity client commands, editor closed) until both are green.
2. Mac build, then a smoke run with `-labDebug` on `reports/winning-line-tech.json`; look at the screenshots.
3. Commit, push, comment `@codex review`, answer findings.
4. Ask the owner to play it (backtick, step mode, a saved fight paused and stepped) before `gh pr ready`.

## Decisions

- 2026-11-12: the panel reads `Flow.Shown`, like the rest of the HUD, so in step mode it describes the frame on screen. The board's marks follow cues and `Level`.
- 2026-11-12: ported what explains a fight (the task's list, plus the Flamer's options, the debug log lines and the chain-rule count). The web client's debug edits (set HP, set AP and movement, spawn a unit, force a wave, hold back wave 3) and restart are not ported here; they go to the roadmap so they are not lost when the web client retires.
- 2026-11-12: tile names are not written on every tile: the Unity board's edges always carry its coordinates.

## Open questions

## Notes for whoever picks this up
