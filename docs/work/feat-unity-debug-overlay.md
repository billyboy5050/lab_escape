# Debug overlay and step mode in Unity

- Branch: `feat/unity-debug-overlay`
- Pull request: <link, once opened>
- Roadmap item: M13, part 1 of the remaining list: "Debug overlay and step mode"
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

Port the web client's backtick debug panel to the Unity client: engine version and content hash, unit IDs on the board, spawn tiles and timing of later waves, the hidden wave timer, each hazard's state with its value-rule explanation, and step mode. A replay can be paused and stepped one command at a time. Done means the overlay toggles with the backtick, EditMode and PlayMode tests cover it and pass on 6000.3.28f1, a Mac build smoke run passes, the docs are updated, Codex has reviewed, and the owner has played it before the PR is marked ready.

## Status

In progress: reading the web panel and the Unity client.

## Done so far

- Branch cut from `main` at 8bdc409; `npm test` green.

## Next steps

1. Read `src/client/components/DebugPanel.tsx` and the Unity `FightDirector`, `Hud`, `BoardTags`, then plan the port.

## Decisions

## Open questions

## Notes for whoever picks this up
