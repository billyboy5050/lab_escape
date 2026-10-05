# Lab Escape: Armory Lockdown (MVP prototype)

The playable MVP described in `Lab_Escape_Roguelite_MVP_Prototype_Spec_rev2.md`: one 8x8 room, three hidden waves, the Tech and Alien kits, 12 upgrades, the lab's hazard AI, telemetry, replays and a headless simulator.

Interpretations the spec left open, and calls to confirm, are listed in [IMPLEMENTATION_NOTES.md](IMPLEMENTATION_NOTES.md).

## Quick start

```bash
npm install
```

```bash
npm run dev
```

Open http://localhost:5173, pick a preset or build a loadout, and fight.

```bash
npm test
```

```bash
npm run sim -- presets --seeds 1000
```

## Playing

- **Move** for free: hover a green tile to see the path and what the enemies would do if you stood there, click to go. Undo free moves with **Z** until you spend AP or step on something.
- **Act**: pick an ability (click, or keys **1–9**, **0**), hover a highlighted target to preview it, click to commit. Previews show damage after armor, kills, statuses, areas, chain and shot lines, and a red ring with **!** on any of your own units that would be hit. **Esc** cancels.
  - Barrier Shield and Lunge take two clicks (second tile, or strike target; click the landing tile again to lunge without striking).
  - Grapple Hook: **Tab** or the button switches between pull unit and pull self.
- **End turn** with **E**. The status strip warns how much damage you would take if you ended the turn now (minions and enemies only; the lab's hazards stay hidden).
- **Intents**: every enemy and minion shows its planned path and attack, recomputed after each action (toggle with **I**). Hover a unit for its plan in words.
- **Speed**: 1x, 2x or skip (**F** cycles, **Space** skips the current animation).
- **L** opens the intercom log. **`** opens the debug overlay.

## Debug overlay (`)

Unit IDs and tile names, spawn tiles of later waves, the hidden wave timer, every hazard's state and which step of the value rule produced its last decision, the Flamer's scored options, step-through (pause after each action, next action **N**, next phase **Shift+N**), set HP, set AP and movement, spawn a unit, force the next wave, hold back wave 3, load and play a replay, and restart. Debug edits are commands, so they are recorded in the replay.

A replay can also be opened from the URL: `http://localhost:5173/?replay=reports/winning-line-hybrid.json`.

## Tuning

Every number lives in `content/*.json`:

| File | Holds |
| --- | --- |
| `rules.json` | AP, movement, sprint, caps, statuses, corpse decay, egg timers, chain depth, round cap, arrival-round mode, early waves, loadout size, UI options |
| `units.json` | Player, enemies and minions: HP, armor, movement, attacks, AI weights |
| `abilities.json` | The 11 abilities and the utility actions, with their rule text |
| `upgrades.json` | The 12 upgrades |
| `hazards.json` | Wall gun, panel grid, gas vents |
| `map.json` | Armory Lockdown terrain and hazard placement |
| `waves.json` | The three waves |
| `intercom.json` | Lines, triggers and lexicon |
| `presets.json` | Tech, Alien and Hybrid presets |

With `npm run dev` running, saving a content file hot-reloads it and restarts the fight (a fight log never mixes two sets of values). Invalid content is rejected with the reason and the old values stay.

The spec's open decisions are switches in `rules.json`: `earlyWaveWhenEmpty`, `ui.showHazardCooldowns`, `arrivalRound` (`all`, `first`, `none`), `loadout.abilities` (8, or 6 for the informed round), and `statuses.corrode.duration` (0 = rest of the fight).

## Headless simulator

```bash
npm run sim -- --help
```

| Command | What it does |
| --- | --- |
| `batch --preset hybrid --seeds 1000` | One preset, many seeds: win rate with a confidence interval, rounds for wins and losses, causes of death, damage by source, hazard fires, spec metrics |
| `presets --seeds 1000` | All three presets; saves the first winning line of each (`reports/winning-line-*.json`), the paper check |
| `sweep --seeds 1000 [--size 6]` | Random valid loadouts with the win-rate lift of every ability and upgrade |
| `tune --param units.warden.hp --values 12,16,20` | One tuning value across a list |
| `golden [--record]` | Verify (or re-record) the golden replays |
| `replay FILE` | Replay a saved command log and print its summary |

Options: `--bot random|greedy|greedy-naive`, `--from N`, `--workers N`, `--set path=value` (repeatable, e.g. `--set units.warden.hp=16`), `--hold-wave3`, `--csv FILE`, `--out DIR`.

Bots:

- **random** picks a random legal command (finds crashes, infinite loops and exploits).
- **greedy** scores each candidate action by playing out the round's minion and enemy phases and valuing the result, and takes a random legal action one time in ten. It treats a hazard showing its cue as a threat to that hazard's area, as a player who has learned the room would; **greedy-naive** ignores cues.
- **Scripted** fights are the golden replays.

The game has no randomness; all variation comes from the bot's seed, stored with each fight. The same batch with the same seeds gives identical results, whatever the number of worker threads.

## Telemetry and replays

Each fight is a replay file: loadout, settings and the ordered player commands, plus the engine version and content hash it is valid against. Replaying it reproduces the fight exactly.

With the dev server running, every finished fight is saved to `telemetry/<time>-<preset>-<outcome>/` as `replay.json`, `summary.json` (outcome, rounds, damage by source including friendly fire, abilities used, peak minions, hazard fires with victims, cause of death, turn times) and `events.jsonl` (one event per line). The result screen also offers the three files as downloads.

Golden replays live in `golden/` (one command log and event log per preset). `npm test` fails if a build changes them; re-record on purpose with `npm run golden:record` after an intended rule or content change.

## Code layout

The rules engine is a deterministic library with no rendering, input or clock dependencies. `step(content, state, command)` returns a new state and the events it produced; previews and intents call the same code on a copy.

| Module | Folder | Responsibility |
| --- | --- | --- |
| state | `src/state` | Types, grid geometry and symmetric line of sight, initial state |
| rules | `src/rules` | The `World` rules context: damage and armor, statuses, deaths and corpses, movement and mines, the chain queue and once-per-chain rule, projectiles, environment steps, waves, intercom |
| effects | `src/effects` | Ability behaviours and upgrade hooks |
| ai | `src/ai` | Enemy, minion and hazard AI (the value rule) |
| engine | `src/engine` | The round and phase loop, `step`, `newGame`, legal commands |
| content | `src/content` | JSON loading, validation, content hash, overrides |
| preview | `src/preview` | Dry-run previews and intents |
| telemetry | `src/telemetry` | Replay records, summaries, the client fight session (command log and undo) |
| sim | `src/sim` | Bots, batch runner, worker pool, reports, golden replays, CLI |
| client | `src/client` | React UI: loadout, fight and result screens, board, HUD, debug overlay |

Tests are in `tests/` (Vitest): line of sight and its symmetry, combat and timers, the value rule case by case, enemies and waves, both kits, every upgrade, the chain rule, intercom, previews matching results over 100 random states, intents matching the real end of turn, replay determinism, undo, golden replays and batch reproducibility.

## Status against the build order

| Milestone | Status |
| --- | --- |
| M0 Foundations | Done: grid, symmetric LOS (with the spec's 88-pair check), content loader with validation and hash, phase loop, movement, command log and replay |
| M1 Combat core | Done: damage types, armor, deaths and corpses, Sidearm, Guard AI, event queue and once-per-chain rule, win and loss, result screen, previews (verified against 100 random states) |
| M2 Tech kit | Done: mines, turret and Reload, drone cycle, shields, grapple, minion phase and cap, intents |
| M3 Enemies and waves | Done: Medic, Flamer, Warden, waves with the arrival round, hidden timer, round cap, intercom feed and log |
| M4 Lab hazards | Done: gun, panel grid, vents, prime-then-fire, the value rule with the combined kill check, cues, debug explanations |
| M5 Alien kit | Done: Acid Spit, Lunge, Spore Pod, Brood Egg on floor and corpses, basic, spitter and burster hatchlings, Parasite, poison and spread |
| M6 Loadout and upgrades | Done: loadout screen, presets, all 12 upgrades |
| M7 Instrumentation and balance | Tooling done (telemetry, replay, simulator, bots, golden replays); the playtest rounds are yours to run |
| Stretch | Not built: scrap and Repair, mender hatchling, Creep, search bot, scalable text and remappable keys |
