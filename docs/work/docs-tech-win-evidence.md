# Record the owner's first Tech win

- Branch: `docs/tech-win-evidence`
- Pull request: <link, once opened>
- Roadmap item: Tune against the alarms once play confirms them, "Tech's difficulty"
- Started: 2026-10-20
- Last updated: 2026-10-20, by Claude

## Goal

The owner won with the Tech kit for the first time on 2026-10-20, in round 28 of 30, and shared the replay. Record what the fight shows as evidence for the roadmap's Tech item in the implementation notes, and point the roadmap at it. Done means the notes have the figures and the fight's shape, and the roadmap item links to them.

## Status

In progress.

## Done so far

- 

## Next steps

1. Add a section after "What the developer playtest round says" in `docs/implementation-notes.md`, and update the roadmap's Tech item.

## Decisions

- 2026-10-20: the replay itself stays out of git. It is tied to content hash 08a289f83c157f and would stop loading after any content change, and it cannot be regenerated like the bot's winning lines. The owner keeps it in `telemetry/`.

## Open questions

- 

## Notes for whoever picks this up

- Figures come from `npm run sim -- replay telemetry/2026-10-20T20-21-03-588Z-custom-win/replay.json` and the fight's `summary.json` and `events.jsonl`, on the owner's machine.
