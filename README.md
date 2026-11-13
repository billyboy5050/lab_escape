# Lab Escape: Armory Lockdown (MVP prototype)

The playable MVP described in the [spec](docs/spec.md): one 8x8 room, three hidden waves, the Tech and Alien kits, 12 upgrades, the lab's hazard AI, telemetry, replays and a headless simulator.

Milestones M0 to M7 are done: the owner closed playtesting as successful on 2026-11-08. The full-game phase comes next, moving the game to Unity and building it out; see the [phase plan](docs/phase-2.md) and the [roadmap](docs/roadmap.md). Until that move is finished, everything below describes this TypeScript prototype, except the last two sections: the [C# core and simulator](#c-core-and-simulator) and the [Unity client](#unity-client).

## Documents

| Document | What it is for |
| --- | --- |
| [docs/spec.md](docs/spec.md) | The design spec (revision 2) and the source of truth for the rules |
| [docs/implementation-notes.md](docs/implementation-notes.md) | How the tooling reads the spec where it is silent, known limitations, what the simulator found, and what the playtests found |
| [docs/roadmap.md](docs/roadmap.md) | Where things stand, what is next, and the decisions waiting on the owner |
| [docs/phase-2.md](docs/phase-2.md) | The plan for the full-game phase: the move to Unity, the game it builds, and the milestones |
| [docs/rules-sheet.html](docs/rules-sheet.html) | The one-page rules sheet for first-time players in playtests; open it in a browser and print it |
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

Open http://localhost:5173, press Begin, pick a preset or build a loadout, read the briefing and fight.

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

The game opens on a title screen; Begin (or **Enter**) goes to the loadout. Start fight shows a briefing (the room, the lab's opening intercom line, the goal and your kit), and Enter the armory (**Enter**) starts the fight; **Esc** goes back to the loadout. The fight and its turn clock start only then, so time on the briefing is not counted as turn time. Retry from the result screen goes straight back into the fight; a `?replay=` link skips the title.

- **Move** for free: hover a highlighted tile to see the path and what the enemies would do if you stood there, click to go. Undo free moves with **Z** until you spend AP or step on something.
- **Act**: pick an ability (click, or keys **1–9**, **0**, **-**, **=** in action-bar order), hover a highlighted target to preview it, click to commit. Previews show damage after armor, kills, statuses, areas, chain and shot lines, and a red ring with **!** on any of your own units that would be hit. **Esc** cancels.
  - Barrier Shield and Lunge take two clicks (second tile, or strike target; click the landing tile again to lunge without striking).
  - Grapple Hook: **Tab** or the button switches between pull unit and pull self.
- **End turn** with **E**, or **Enter** when no button or link has focus (Enter operates a focused control instead). The status strip warns how much damage you would take if you ended the turn now (minions and enemies only; the lab's hazards stay hidden).
- **Intents**: every enemy and minion shows its planned path and attack, recomputed after each action (toggle with **I**). Hover a unit for its plan in words.
- **Speed**: 1x, 2x or skip (**F** cycles, **Space** skips the current animation unless a control has focus).
- **L** opens the intercom log. **`** opens the debug overlay.
- **Sound**: **M** (or the speaker button in the header) mutes and unmutes on every screen, and the browser remembers the choice. Sounds are synthesised in the browser; a primed hazard makes none, so its cue stays visual only.
- **View**: the board is drawn from straight above (Top, the default) or tilted to an isometric three-quarter view (Iso). Switch with the Top/Iso control under the board or **V**, at any time, even mid-fight. The browser remembers the choice for later fights and visits; `?view=iso` or `?view=top` in the URL overrides it for that page (switching then updates the URL too). In Iso, a pillar or wall that would hide a unit, an object or the tile under the pointer fades, and clicking a figure picks the tile it stands on. Top-down stays the default in this web client; the Unity client will use a fixed isometric camera (see the [phase plan](docs/phase-2.md)).

With Ctrl, Cmd or Alt held, the action keys, **V** and **M** are left to the browser (Ctrl or Cmd with `-` and `=` zoom the page). The other shortcuts do not check for modifiers yet, so Ctrl or Cmd with Z, E, F or L also triggers the game's undo, end turn, speed or log (see the [roadmap](docs/roadmap.md)). The action keys are defined once in `src/client/actionKeys.ts`.

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
| `map.json` | Armory Lockdown terrain and hazard placement, and the tile types every room shares |
| `waves.json` | The three waves |
| `intercom.json` | Lines, triggers and lexicon |
| `presets.json` | Tech, Alien and Hybrid presets |

With `npm run dev` running, saving a content file hot-reloads it and restarts the fight (a fight log never mixes two sets of values). Invalid content is rejected with the reason and the old values stay.

Several of the spec's decisions are switches in `rules.json`, so a playtest can try the other answer: `earlyWaveWhenEmpty`, `ui.showHazardCooldowns`, `arrivalRound` (`all`, `first`, `none`), `loadout.abilities`, and `statuses.corrode.duration` (0 = rest of the fight). The spec's "Decisions taken" table gives the value chosen for each.

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
| `--rooms FILE` | Add the rooms in a JSON file to the content, e.g. `golden/rooms.json` |
| `--room ID` | Fight in this room (default: the Armory) |
| `--csv FILE` | Write one row per fight |
| `--out DIR` | Write the report, CSV and winning replays to DIR (default `reports/` for `presets`) |
| `--record` | With `golden`: re-record the golden replays |

Counts must be whole numbers, and `--seeds`, `--workers` and `--size` at least 1. A `presets` run at 1,000 seeds takes about two minutes on an 8-core machine; use `--seeds 100` while iterating.

A fight takes place in the Armory unless it names another room. To try the rooms of other sizes:

```bash
npm run sim -- batch --rooms golden/rooms.json --room hall --seeds 100
```

Bots:

- **random** picks a random legal command (finds crashes, infinite loops and exploits).
- **greedy** scores each candidate action by playing out the round's minion and enemy phases and valuing the result, and takes a random legal action one time in ten. It treats a hazard showing its cue as a threat to that hazard's area, as a player who has learned the room would; **greedy-naive** ignores cues.
- **Scripted** fights are the golden replays.

The game has no randomness; all variation comes from the bot's seed, stored with each fight. The same batch with the same seeds gives identical results, whatever the number of worker threads.

## Telemetry and replays

Each fight is a replay file: loadout, settings and the ordered player commands, plus the engine version and content hash it is valid against. Replaying it reproduces the fight exactly.

With the dev server running, every finished fight is saved to `telemetry/<time>-<preset>-<outcome>/` (or `telemetry/<time>-<tester>-a<attempt>-<preset>-<outcome>/` when a tester ID is set) as `replay.json`, `summary.json` (outcome, rounds, damage by source including friendly fire, abilities used, peak minions, hazard fires with victims, cause of death, turn times) and `events.jsonl` (one event per line). The result screen also offers the three files as downloads.

After a playtest round, `npm run sim -- telemetry` reads every fight under `telemetry/` and prints the spec's playtest metrics: first attempts lost, rounds in first-time losses, informed win rate by attempt, length of winning fights, median turn time with a full swarm, friendly-fire share, deaths by source and hazard fires per fight. It replays each `replay.json`, so a fight from another engine or content version, an unfinished fight, one played with debug commands, one with wave 3 held back and one with no tester ID are skipped and listed with the reason. The informed round continues each tester's attempt numbers, so give `--informed-from` the number of their first informed attempt; only the next three attempts from there count.

**In the Unity client** each fight of the player's own is saved in the same form, so the same report reads it. The editor saves to the repository's `telemetry/`; a Mac or Windows build to `telemetry/` in the app's data folder (`~/Library/Application Support/Lab Escape/Lab Escape/telemetry/` on a Mac); the web build, which has no disk, offers the three files as downloads on the end card. Give a tester ID with `-labTester P3` on the command line or `?tester=P3` in the web build's URL; the game remembers it until given `-labTester -` (or `?tester=-`). The start screen lists the latest saved fights to watch again, and a replay also plays from `-labReplay <file>`, or from `?replay=<path>` beside the web build's page. A replay plays by itself (**Space** skips, **F** changes the speed), and nothing is saved at its end. One recorded on another engine version or content is refused on the start screen with the reason.

For playtests, give each person a tester ID: open the game at `?tester=P3` or type it into the Tester ID field on the loadout screen. The browser remembers it. The replay's `meta` then carries `tester` and `attempt` (1 for their first finished fight, counted per tester in that browser, so abandoned fights do not use a number), and both go into the folder name. Clear the field outside a playtest. Every replay's `meta` also records the board `view` the fight was played in: `top`, `iso`, or `both` if it was switched during the fight. The browser remembers the view, so check the control shows Top before each tester starts, or open the game at `?view=top`. `telemetry/` is ignored by git; `src/telemetry/` is source.

First-time players get the one-page rules sheet and no coaching (spec, playtest round 2). Open `docs/rules-sheet.html` in a browser, or http://localhost:5173/docs/rules-sheet.html while the dev server runs, and print it; it fits one A4 or Letter page. It states the rules and explains the screen, and leaves out what the game hides on purpose: when waves arrive, which tiles the lab's defences cover, what makes the lab fire them, and the intercom's code words. Its numbers are tagged with where they come from in `content/`, and `npm test` fails if one no longer matches.

Golden replays live in `golden/`: one command log and event log per preset in the Armory, and one per room in `golden/rooms.json`, a hand-written set of rooms of other sizes. `npm test` fails if a build changes them; re-record on purpose with `npm run golden:record` after an intended rule or content change.

## Code layout

The rules engine is a deterministic library with no rendering, input or clock dependencies. `step(content, state, command)` returns a new state and the events it produced; previews and intents call the same code on a copy.

| Module | Folder | Responsibility |
| --- | --- | --- |
| state | `src/state` | Types, grid geometry and symmetric line of sight for a board of any size, initial state, `ENGINE_VERSION` |
| rules | `src/rules` | The `World` rules context: damage and armor, statuses, deaths and corpses, movement and mines, the chain queue and once-per-chain rule, projectiles, environment steps, waves, intercom |
| effects | `src/effects` | Ability behaviours and upgrade hooks |
| ai | `src/ai` | Enemy, minion and hazard AI (the value rule) |
| engine | `src/engine` | The round and phase loop, `step`, `newGame`, legal commands |
| content | `src/content` | JSON loading, validation, rooms, content hash, overrides, the list of required fields |
| preview | `src/preview` | Dry-run previews and intents |
| telemetry | `src/telemetry` | Replay records, summaries, the client fight session (command log and undo) |
| sim | `src/sim` | Bots, batch runner, worker pool, reports, golden replays, CLI |
| client | `src/client` | React UI: title, loadout, briefing, fight and result screens, board, HUD, debug overlay, sound (`sfx.ts`); the art and combat effects, all SVG drawn in code, are in `src/client/art`, with the board's two projections (top-down and isometric) in `geometry.ts` and the isometric room in `iso.tsx` |
| util | `src/util` | Tile names, canonical JSON hashing, statistics |
| dev | `src/dev` | Path checks for the dev server's telemetry endpoint (used by `vite.config.ts`) |

Imports point one way: `util` and `content` at the bottom, then `state`, `rules`, `effects` and `ai`, `engine`, then `preview` and `telemetry`, with `sim` and `client` on top.

Tests are in `tests/` (Vitest): line of sight and its symmetry, combat and timers, the value rule case by case, enemies and waves, both kits, every upgrade, the chain rule, intercom, previews matching results over 100 random states, intents matching the real end of turn, replay determinism and file checks, undo, golden replays and batch reproducibility, plus content validation, CLI options, reports and statistics, telemetry paths, saved preferences, the client's shortcut and formatting helpers, the isometric board's projection, draw order, fading and click boxes, and the rules sheet against the content.

CI (`.github/workflows/ci.yml`) runs the build and the tests on every pull request, then the C# tests and the C# simulator's conformance check.

## C# core and simulator

`core/` is the port of the rules core, telemetry and simulator to plain C#, the first steps of moving the game to Unity ([phase plan](docs/phase-2.md#the-move-to-unity)). Nothing in the web game uses it. It reads the same `content/*.json`, and until M13 the TypeScript stays authoritative.

Needs the .NET SDK (`brew install dotnet` on a Mac).

```bash
dotnet test core/Tests
```

The simulator has the same commands, options and report files as `npm run sim`, and runs about twice as fast:

```bash
dotnet run --project core/Sim -c Release -- batch --preset hybrid --seeds 100
```

Its `conformance` command regenerates every committed report and compares each with its file, byte for byte. It is the proof that the port matches, and CI runs it:

```bash
dotnet run --project core/Sim -c Release -- conformance
```

| Folder | What it is |
| --- | --- |
| `core/Core` | The library: one namespace, `LabEscape.Core`, with a folder for each module in `src/`. It targets .NET Standard 2.1 and C# 9, the level Unity's LTS compiles, and has no Unity dependency. It is also a local Unity package, which is why .NET's build output goes to `core/.build/` |
| `core/Sim` | The simulator, on the library: bots, batches, reports, golden replays and playtest metrics (`src/sim`), as a console tool |
| `core/Tests` | NUnit tests: the rule, telemetry and simulator tests from `tests/`, the golden replays and the hash vectors |

The same names are used on both sides, with C# capitalisation: `step` is `Engine.Step`, `newGame` is `Engine.NewGame`, `previewCommand` is `Previews.PreviewCommand`. `GameObject` is `BoardObject` and the `Debug` event is `DebugEvent`, so they cannot clash with Unity's types.

## Unity client

`unity/` is the game moving to Unity ([phase plan](docs/phase-2.md#the-move-to-unity)). It plays one full fight in greybox on a fixed isometric camera, with previews, intents and a HUD, on the C# rules core. The web game above stays the playable version until M13.

Needs Unity 6000.3.28f1 (6.3 LTS), with Windows Build Support (Mono) for the Windows build and Web Build Support for the web build. In Unity Hub, Installs, Install Editor, 6.3 LTS, and tick both modules; a Mac editor builds for the Mac with no module. In Unity Hub choose Add, pick the `unity` folder and open it, then press Play: every scene starts the game. Pick a loadout, and the fight begins.

The keys are the web game's. The number keys, `-` and `=` pick an action, **E** or Enter ends the turn, **Z** undoes a move, **Esc** cancels, **Space** skips the animation, **F** changes the speed, **I** shows or hides intents, **L** opens the intercom log, **M** mutes and unmutes (the game remembers), **Tab** switches the grapple's mode, and **`** opens the debug overlay. The mouse wheel zooms, and when zoomed in the right or middle button, **W A S D** or the arrow keys pan.

A fight opens with the lights striking, the room's name and the first wave falling in. **Space** skips that like any other animation.

Every finished fight is saved for the playtest report, and the start screen offers recent ones to watch again: see [Telemetry and replays](#telemetry-and-replays). While one plays back, **P** pauses it between commands and plays it on, and **N** plays its next command while it is paused.

The debug overlay (**`**) is a column on the left of the board. It gives the engine version and content hash, and under them the folder watched for hot reload or why the last save was refused, the hidden wave timer (the round, the waves spawned, when the next arrives and every wave's units and tiles), each hazard's state and its last decision with the step of the value rule that took it, the Flamer's scored options while you can act, and how many triggers the chain rule has stopped. On the board it puts each unit's ID in its tag, a dashed square and the wave's number on each spawn tile of a wave still to come, each hazard's state on its tiles, and the Flamer's score for each place and facing it weighed; its buttons turn each of these off, and Log adds the web client's debug lines to the combat log. Pause after each action turns on step mode: every action's animation stops at its end until **N** shows the next, or **Shift+N** runs on to the last before the next phase. With a saved fight playing back, its pause and step are there too. Closing the overlay turns all of it off, step mode included. Under Edit state it sets a unit's HP, sets AP and movement, and spawns a unit; under Waves and fight it forces the next wave, holds wave 3 back and restarts the fight. Pick a unit with the arrows and a number with its - and + buttons; to spawn, pick a kind, press Spawn on a tile and click the board (Esc cancels, and the nearest free tile is used if the one clicked is taken). Setting HP, AP and movement, spawning and forcing a wave are commands, so they are in the fight's log and in its saved replay, and the log says what each did. They work on your turn, with the board still, and not while a saved fight plays. Holding wave 3 back restarts the fight with two waves in play, as the web client's checkbox does, and letting it go restarts it again. Restart fight starts the same fight again, or plays a saved fight from its first command.

Content hot reload works as in the web client. In the editor the game reads and watches the repository's `content/`; a Mac or Windows build reads the copy it was built with, unless it is started with `-labContent <folder>`, which it then reads and watches instead. Saving a content file restarts your fight with the new values, and a selected preset follows its edited picks. A saved fight playing back is stopped, since its recording is only valid against the content it was made with. Content that is not valid, including broken JSON and a missing file, is refused with the reason, and the game goes on with its old values: the reason shows for a few seconds and stays in the debug overlay until the next save puts it right. A game started on content that does not load waits on the start screen and begins once a save fixes it. The web build has no folder to read and does not reload.

The board's edges are lettered A to H and numbered 1 to 8, as the log names tiles. Point at a unit, a tile or an action button and the Scanner describes it; for an action that is its rule and, if it cannot be used now, why.

Close the editor before running these, because batch mode cannot share a project with an open editor. `U` is the editor's binary:

```bash
U=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity
```

```bash
$U -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults /tmp/edit.xml
```

```bash
$U -batchmode -projectPath unity -runTests -testPlatform PlayMode -testResults /tmp/play.xml
```

```bash
$U -batchmode -quit -projectPath unity -executeMethod LabEscape.Client.Editor.Builds.Mac
```

The build lands in `unity/Builds/Mac/`. To play a saved fight through it unattended and take screenshots, run it with `-labSmoke <replay.json> <output folder>`; add `-labDebug` to take them with the debug overlay open. The fight's telemetry goes to `telemetry/` in that folder, never among a playtest's fights, and it uses up no tester's attempt number. A build started while the Mac's display is asleep waits for it before it draws anything, so a smoke run started then never finishes; add `-batchmode` to check its telemetry without screenshots.

A still cannot show whether a motion has weight. To look at an animation, add `-labFilm 0,2,8` to that command: it films what follows those commands of the replay (0 is the fight's opening) and skips the rest. The game then advances a sixtieth of a second for each frame it draws, and every frame is saved at half size under `film/` in the output folder, with a `cues.txt` that says which cue and which sound played on which frame, and how loud the output was. Add `-labNoSystemFonts` to see the game as the web build draws it, without the system faces it takes the web client's type from.

The Windows build needs the editor's Windows Build Support (Mono) module, and a Mac can make it:

```bash
$U -batchmode -quit -projectPath unity -executeMethod LabEscape.Client.Editor.Builds.Windows
```

It lands in `unity/Builds/Windows/`. Copy the whole folder to a Windows PC and run `LabEscape.exe`.

A Windows or web build can leave the project set to that platform. Run the editor once with `-batchmode -quit -projectPath unity -buildTarget StandaloneOSX` to set it back to the Mac before the next test run or Mac build.

The web build needs the editor's Web Build Support module:

```bash
$U -batchmode -quit -projectPath unity -executeMethod LabEscape.Client.Editor.Builds.Web
```

It lands in `unity/Builds/Web/`. Its files are gzip-compressed, so it has to come from a web server that sends `Content-Encoding: gzip` for the `.gz` files; opening `index.html` from the disk does not work.

| Folder | What it holds |
| --- | --- |
| `unity/Assets/Scripts/Logic` | No Unity code: the animation queue and its step mode (`FightFlow`), the staging that lays a frame's events out in time (`Staging`), the board's clock (`BoardClock`), targeting (`ActionModel`), log text (`EventFormatter`), the debug overlay's text (`DebugReport`), and which cue makes which sound (`SoundCues`) and the synth that makes it (`Synth`) |
| `unity/Assets/Scripts/Board` | The camera, room, unit models, overlays, effects and sound player, all built from code. `Feel.cs` holds every number that gives a blow its weight |
| `unity/Assets/Scripts/Hud` | Fills in the interface, the tags and coordinates anchored to the board, and the web client's faces from the system (`Fonts`) |
| `unity/Assets/Scripts/FightDirector.cs` | Reads input and ties the rest together |
| `unity/Assets/Resources/UI` | The interface, in UI Toolkit's text formats: `Fight.uxml` and `Fight.uss` |
| `unity/Assets/Tests` | EditMode: the golden replays on Unity's runtime, and the client logic. PlayMode: the real fight screen |
