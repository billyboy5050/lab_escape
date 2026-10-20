# Industrial lab presentation

- Branch: `feat/lab-presentation`
- Pull request: <link, once opened>
- Roadmap item: none (owner request, 2026-10-20)
- Started: 2026-10-20
- Last updated: 2026-10-20, by Claude

## Goal

Playtesters said the game looked like it would be fun but did not engage with it because of its presentation. This branch moves the client from placeholder shapes and a dashboard layout toward the final look the owner chose: an industrial sci-fi lab. Done means the board, units, hazards, HUD, loadout, result and a new title and briefing screen are restyled, combat has visual effects and synthesized sound with a mute toggle, and no rule, number or replay changes.

## Status

In progress: setting up.

## Done so far

- 

## Next steps

1. Read the client (`src/client/`) and plan the restyle.

## Decisions

- 2026-10-20: the owner chose the industrial sci-fi lab direction (riveted metal floor, hazard-striped walls, illustrated vector units, a HUD styled as the lab's security terminal in amber and teal) over retro pixel art and a clean board-game look. All art is hand-drawn SVG and CSS, with no new dependencies.
- 2026-10-20: the owner asked for combat effects, restyled loadout and result screens, a title and briefing screen, and synthesized sound effects (Web Audio, with a mute toggle) in this pass. The spec listed final art and audio polish as out of scope for the MVP; this request changes that, and the spec records it under "Changes since revision 2".

## Open questions

- 

## Notes for whoever picks this up

- Client only: nothing under `src/` outside `src/client/` should change, so `ENGINE_VERSION`, the golden replays and the reports stay as they are.
- The rules sheet describes unit glyphs ("Reading the screen"); update it if the glyphs change.
