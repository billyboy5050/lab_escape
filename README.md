# Lab Escape: Armory Lockdown (MVP prototype)

The playable MVP described in the [spec](docs/spec.md): one 8x8 room, three hidden waves, the Tech and Alien kits, 12 upgrades, the lab's hazard AI, telemetry, replays and a headless simulator.

Milestones M0 to M6 are done and M7's tooling is built. M7 is not done until the first full playtest round has run and its metrics are recorded; that and whatever it turns up is what remains. See the [roadmap](docs/roadmap.md).

## Documents

| Document | What it is for |
| --- | --- |
| [docs/spec.md](docs/spec.md) | The design spec (revision 2) and the source of truth for the rules |
| [docs/implementation-notes.md](docs/implementation-notes.md) | Where the implementation interpreted the spec, the defaults taken for its open decisions, and what the simulator found |
| [docs/roadmap.md](docs/roadmap.md) | Where things stand, what is next, and the decisions waiting on the owner |
| [docs/work/](docs/work/) | One note per branch in progress, so anyone can pick the work up |
| [CLAUDE.md](CLAUDE.md) (also `AGENTS.md`) | How to work in this repo: branches, pull requests, keeping the docs current, and the invariants not to break |

## Quick start

Needs Node 22.12 or later on the 22 or 24 line, or 26 and up: the range Vitest and Vite support (`engines` in `package.json`). `.nvmrc` picks 22.

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

| Script | What it does |
| --- | --- |
| `npm run dev` | Dev server on port 5173, with content hot reload and the telemetry endpoint |
| `npm test` | All tests once (Vitest); `npm run test:watch` to keep them running |
| `npm run typecheck` | TypeScript check with no output |
| `npm run build` | Typecheck, then a static build in `dist/` (`npm run preview` serves it) |
| `npm run sim -- <command>` | The headless simulator (below) |
| `npm run golden:record` | Re-record the golden replays |

`npm install` also points git at the repo's hooks in `.githooks/`, which refuse commits on `main`, merges, resets and rebases that would move it anywhere but `origin/main`, and pushes to it. Changes reach `main` only through pull requests that pass CI, which GitHub's branch protection enforces; see [CLAUDE.md](CLAUDE.md).

## Playing

- **Move** for free: hover a green tile to see the path and what the enemies would do if you stood there, click to go. Undo free moves with **Z** until you spend AP or step on something.
- **Act**: pick an ability (click, or keys **1–9**, **0**, **-**, **=** in action-bar order), hover a highlighted target to preview it, click to commit. Previews show damage after armor, kills, statuses, areas, chain and shot lines, and a red ring with **!** on any of your own units that would be hit. **Esc** cancels.
  - Barrier Shield and Lunge take two clicks (second tile, or strike target; click the landing tile again to lunge without striking).
  - Grapple Hook: **Tab** or the button switches between pull unit and pull self.
- **End turn** with **E**, or **Enter** when no button or link has focus (Enter operates a focused control instead). The status strip warns how much damage you would take if you ended the turn now (minions and enemies only; the lab's hazards stay hidden).
- **Intents**: every enemy and minion shows its planned path and attack, recomputed after each action (toggle with **I**). Hover a unit for its plan in words.
- **Speed**: 1x, 2x or skip (**F** cycles, **Space** skips the current animation unless a control has focus).
- **L** opens the intercom log. **`** opens the debug overlay.

With Ctrl, Cmd or Alt held, the action keys are left to the browser (Ctrl or Cmd with `-` and `=` zoom the page). The other shortcuts do not check for modifiers yet, so Ctrl or Cmd with Z, E, F or L also triggers the game's undo, end turn, speed or log (see the [roadmap](docs/roadmap.md)). The action keys are defined once in `src/client/actionKeys.ts`.

## Debug overlay (`)

The engine version and content hash, unit IDs and tile names, spawn tiles of later waves, the hidden wave timer, every hazard's state and which step of the value rule produced its last decision, the Flamer's scored options, step-through (pause after each action, next action **N**, next phase **Shift+N**), set HP, set AP and movement, spawn a unit, force the next wave, hold back wave 3, load a replay and play or step through it, restart, and a count of triggers the chain rule suppressed. Debug edits are commands, so they are recorded in the replay.

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

Any content change alters the content hash, and replays are only valid against the hash they were recorded with. A content change therefore needs every generated file regenerated in the same commit: the golden replays, the preset reports and winning lines, the random-bot reports and the sweep. Otherwise the tests and the `?replay=` links stop working and the committed balance figures go stale. The commands for each are under [Regenerating generated files](CLAUDE.md#regenerating-generated-files) in CLAUDE.md.

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
| `telemetry [DIR]` | Playtest metrics from the fights saved under `telemetry/` (or DIR), each against the spec's target and alarm, then every tester's attempts |

| Option | Meaning |
| --- | --- |
| `--bot random\|greedy\|greedy-naive` | Bot to play (default greedy) |
| `--preset tech\|alien\|hybrid` | Preset for `batch` and `tune` (default hybrid) |
| `--seeds N`, `--from N` | Number of seeds (default 1000) and the first seed (default 1) |
| `--workers N` | Worker threads (default: CPU count minus 1) |
| `--exclude-tester ID` | With `telemetry`: leave a tester out (repeatable), such as the developer who played round 1, so the first-time figures cover only first-time players |
| `--include-unlabelled` | With `telemetry`: also count fights saved with no tester ID (skipped by default, since ordinary play is saved in the same folder) |
| `--informed-from N`, `--informed-attempts N`, `--informed-preset ID` | With `telemetry`: the first attempt number counted as informed (default 2), how many attempts per tester the informed round has (default 3) and the preset it plays (default hybrid) |
| `--size N` | Abilities per random loadout in a sweep (default: the loadout size in `rules.json`) |
| `--set PATH=VAL` | Content override, repeatable, e.g. `--set units.warden.hp=16` |
| `--hold-wave3` | Hold back wave 3; clearing wave 2 wins (the first playtest's setup) |
| `--csv FILE` | Write one row per fight |
| `--out DIR` | Write the report, CSV and winning replays to DIR (default `reports/` for `presets`) |
| `--record` | With `golden`: re-record the golden replays |

Counts must be whole numbers, and `--seeds`, `--workers` and `--size` at least 1. A `presets` run at 1,000 seeds takes about two minutes on an 8-core machine; use `--seeds 100` while iterating.

Bots:

- **random** picks a random legal command (finds crashes, infinite loops and exploits).
- **greedy** scores each candidate action by playing out the round's minion and enemy phases and valuing the result, and takes a random legal action one time in ten. It treats a hazard showing its cue as a threat to that hazard's area, as a player who has learned the room would; **greedy-naive** ignores cues.
- **Scripted** fights are the golden replays.

The game has no randomness; all variation comes from the bot's seed, stored with each fight. The same batch with the same seeds gives identical results, whatever the number of worker threads.

## Telemetry and replays

Each fight is a replay file: loadout, settings and the ordered player commands, plus the engine version and content hash it is valid against. Replaying it reproduces the fight exactly.

With the dev server running, every finished fight is saved to `telemetry/<time>-<preset>-<outcome>/` (or `telemetry/<time>-<tester>-a<attempt>-<preset>-<outcome>/` when a tester ID is set) as `replay.json`, `summary.json` (outcome, rounds, damage by source including friendly fire, abilities used, peak minions, hazard fires with victims, cause of death, turn times) and `events.jsonl` (one event per line). The result screen also offers the three files as downloads.

After a playtest round, `npm run sim -- telemetry` reads every fight under `telemetry/` and prints the spec's playtest metrics: first attempts lost, rounds in first-time losses, informed win rate by attempt, length of winning fights, median turn time with a full swarm, friendly-fire share, deaths by source and hazard fires per fight. It replays each `replay.json`, so a fight from another engine or content version, an unfinished fight, one played with debug commands, one with wave 3 held back and one with no tester ID are skipped and listed with the reason. The informed round continues each tester's attempt numbers, so give `--informed-from` the number of their first informed attempt; only the next three attempts from there count.

For playtests, give each person a tester ID: open the game at `?tester=P3` or type it into the Tester ID field on the loadout screen. The browser remembers it. The replay's `meta` then carries `tester` and `attempt` (1 for their first finished fight, counted per tester in that browser, so abandoned fights do not use a number), and both go into the folder name. Clear the field outside a playtest. `telemetry/` is ignored by git; `src/telemetry/` is source.

Golden replays live in `golden/` (one command log and event log per preset). `npm test` fails if a build changes them; re-record on purpose with `npm run golden:record` after an intended rule or content change.

## Code layout

The rules engine is a deterministic library with no rendering, input or clock dependencies. `step(content, state, command)` returns a new state and the events it produced; previews and intents call the same code on a copy.

| Module | Folder | Responsibility |
| --- | --- | --- |
| state | `src/state` | Types, grid geometry and symmetric line of sight, initial state, `ENGINE_VERSION` |
| rules | `src/rules` | The `World` rules context: damage and armor, statuses, deaths and corpses, movement and mines, the chain queue and once-per-chain rule, projectiles, environment steps, waves, intercom |
| effects | `src/effects` | Ability behaviours and upgrade hooks |
| ai | `src/ai` | Enemy, minion and hazard AI (the value rule) |
| engine | `src/engine` | The round and phase loop, `step`, `newGame`, legal commands |
| content | `src/content` | JSON loading, validation, content hash, overrides, the list of required fields |
| preview | `src/preview` | Dry-run previews and intents |
| telemetry | `src/telemetry` | Replay records, summaries, the client fight session (command log and undo) |
| sim | `src/sim` | Bots, batch runner, worker pool, reports, golden replays, CLI |
| client | `src/client` | React UI: loadout, fight and result screens, board, HUD, debug overlay |
| util | `src/util` | Tile names, canonical JSON hashing, statistics |
| dev | `src/dev` | Path checks for the dev server's telemetry endpoint (used by `vite.config.ts`) |

Imports point one way: `util` and `content` at the bottom, then `state`, `rules`, `effects` and `ai`, `engine`, then `preview` and `telemetry`, with `sim` and `client` on top.

Tests are in `tests/` (Vitest): line of sight and its symmetry, combat and timers, the value rule case by case, enemies and waves, both kits, every upgrade, the chain rule, intercom, previews matching results over 100 random states, intents matching the real end of turn, replay determinism and file checks, undo, golden replays and batch reproducibility, plus content validation, CLI options, reports and statistics, telemetry paths, saved preferences and the client's shortcut and formatting helpers.

CI (`.github/workflows/ci.yml`) runs the build and the tests on every pull request.
