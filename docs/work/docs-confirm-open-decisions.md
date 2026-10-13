# Record the owner's answers to the open questions

- Branch: `docs/confirm-open-decisions`
- Pull request: <link, once opened>
- Roadmap item: "Decisions waiting on the owner"
- Started: 2026-10-13
- Last updated: 2026-10-13, by Claude (Claude Code)

## Goal

On 2026-10-13 the owner went through every question under the roadmap's "Decisions waiting on the owner" and the spec's Proposed design calls, and confirmed them all as they stand. This branch records that: the confirmed interpretations become clarifications in the spec, the open decisions become decisions taken, the Proposed tags and "confirm it is intended" notes come off, and the roadmap drops the answered items. Nothing is overturned, so no rule, content or engine version changes. Done is a docs-only pull request that leaves no "to confirm" item behind.

## Status

In progress: work note written, docs not yet edited.

## Done so far

- Asked the owner every question; all answered.

## Next steps

1. Move the game-rule interpretations from `docs/implementation-notes.md` into the matching sections of `docs/spec.md` as "Clarifications" blocks, and the open-decision defaults into the spec's "Open decisions" list.

## Decisions

- 2026-10-13, owner answers:
  - Warden charge: confirmed as written; range 4 is distance moved, so a target 2 to 5 tiles away can be charged.
  - Arrival round: every wave.
  - Hazard cooldowns: hidden.
  - An empty room does not bring the next wave early: fixed schedule.
  - Loadout size: 8 of 11, and the informed round (spec round 3) plays the Hybrid preset as it is. 6 of 11 is tried in the simulator's loadout sweep, not in the playtest.
  - Corrode: lasts the rest of the fight.
  - The six differences from the design document marked "Reason not recorded": all kept.
  - Every Proposed design call in the spec: confirmed. The paper check and the wave-2 first playtest are done.
  - Every interpretation in the implementation notes, and the remaining open-decision defaults: confirmed.
  - Golden replays: stay greedy-bot fights; hand-played lines will not be written.
  - Stretch items: none called for yet; the rule stands.

## Open questions

- None.

## Notes for whoever picks this up

Docs only. `npm run build` and `npm test` should pass unchanged; `tests/rulesSheet.test.ts` does not read the spec. The untracked `telemetry-0.1.0-restamped/` folder at the top level is the owner's; leave it out of commits.
