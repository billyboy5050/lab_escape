# Lab Escape

A tactics roguelite, played in Unity. Today it has one room, Armory Lockdown: the MVP described in the [spec](docs/spec.md), with one 8x8 room, three hidden waves, the Tech and Alien kits, 12 upgrades, the lab's hazard AI, telemetry, replays and a headless simulator.

Milestones M0 to M7 are done: the owner closed playtesting as successful on 2026-11-08. The full-game phase moved the game to Unity (M8 to M13) and now builds it out; see the [phase plan](docs/phase-2.md) and the [roadmap](docs/roadmap.md). The rules run in a plain C# library (`core/`) that the Unity client (`unity/`) and the headless simulator share. The TypeScript prototype and its React web client, in which the MVP was built and playtested, were retired at M13 on 2026-11-14; git history keeps them.

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

Needs the .NET SDK (`brew install dotnet` on a Mac), and Git LFS before you clone (`brew install git-lfs`, then `git lfs install --skip-repo`), because the game's two fonts are kept there. Playing needs Unity: see [Unity client](#unity-client).

```bash
dotnet test core/Tests
```

```bash
dotnet run --project core/Sim -c Release -- presets --seeds 1000
```

To play, open the `unity` folder in Unity Hub and press Play.

Any build of `core/`, such as the test run above, also points git at the repo's hooks in `.githooks/` (`core/Directory.Build.targets`). They refuse commits on `main`, merges, resets and rebases that would move it anywhere but `origin/main`, and pushes to it. Changes reach `main` only through pull requests that pass CI, which GitHub's branch protection enforces; see [CLAUDE.md](CLAUDE.md).

## Playing

The game opens on the title. **Enter** or Begin goes to the loadout screen, where a preset (or its number key) fills the picks and abilities and upgrades are picked by row, up to the rules' limits, with what an upgrade needs shown when it cannot be taken; "Hold back wave 3" is there too. The last picks are remembered. **Enter** or Start fight goes on to the briefing: a plan of the room from above (walls, barriers, the lab's devices outlined, your start marked), the intercom's first line, the objective and your kit. **Enter** goes in and the fight starts; **Esc** goes back a screen at each step. The fight and its turn clock start only then, so time on the briefing is not counted as turn time. The end card's Full report opens the incident report: the damage you took, dealt and your side took by source, and the fight's facts. Fight again goes straight back into the fight.

- **Move** for free: hover a highlighted tile to see the path and what the enemies would do if you stood there, click to go. Undo free moves with **Z** until you spend AP or step on something.
- **Act**: pick an ability (click, or keys **1–9**, **0**, **-**, **=** in action-bar order), hover a highlighted target to preview it, click to commit. Previews show damage after armor, kills, statuses, areas, chain and shot lines, and a warning on any of your own units that would be hit. **Esc** cancels. Barrier Shield and Lunge take two clicks, and **Tab** switches the Grapple Hook between pulling a unit and pulling yourself.
- **End turn** with **E** or **Enter**.
- **Intents**: every enemy and minion shows its planned path and attack, recomputed after each action (toggle with **I**).
- **Speed**: **Space** skips the animation and **F** changes the speed.
- **L** opens the intercom log, **M** mutes and unmutes (the game remembers), **C** turns reduce motion on and off (remembered too, and on every screen), and **`** opens the debug overlay.
- **Camera**: the mouse wheel zooms, and when zoomed in the right or middle button, **W A S D** or the arrow keys pan.

A key held with Ctrl, Cmd or Alt is the system's, on every screen, so Cmd+M still minimises the window; Shift+Z does not undo.

Reduce motion (the button in the fight's controls and on the title, or **C**) stops the screen moving for its own sake: no camera shake, kick or pull-back at the start of a fight, no hit stop or slow motion on a blow, no flashing red edges or flickering lights, no jolt of the HP, no banners popping in, no text printing a character at a time and no idle bob. Units still move, because how they move says what happened. It is off until you turn it on, and the game does not yet read the system's own setting.

A fight opens with the lights striking, the room's name and the first wave falling in. **Space** skips that like any other animation.

The board's edges are lettered A to H and numbered 1 to 8, as the log names tiles. Point at a unit, a tile or an action button and the Scanner describes it; for an action that is its rule and, if it cannot be used now, why.

Every finished fight is saved for the playtest report, and the loadout screen offers recent ones to watch again, with no briefing: see [Telemetry and replays](#telemetry-and-replays). While one plays back, **P** pauses it between commands and plays it on, and **N** plays its next command while it is paused.

## Debug overlay (`)

The debug overlay is a column on the left of the board. It gives the engine version and content hash, and under them the folder watched for hot reload or why the last save was refused, the hidden wave timer (the round, the waves spawned, when the next arrives and every wave's units and tiles), each hazard's state and its last decision with the step of the value rule that took it, the Flamer's scored options while you can act, and how many triggers the chain rule has stopped. On the board it puts each unit's ID in its tag, a dashed square and the wave's number on each spawn tile of a wave still to come, each hazard's state on its tiles, and the Flamer's score for each place and facing it weighed; its buttons turn each of these off, and Log adds the engine's debug lines to the combat log.

Pause after each action turns on step mode: every action's animation stops at its end until **N** shows the next, or **Shift+N** runs on to the last before the next phase. With a saved fight playing back, its pause and step are there too. Closing the overlay turns all of it off, step mode included.

Under Edit state it sets a unit's HP, sets AP and movement, and spawns a unit; under Waves and fight it forces the next wave, holds wave 3 back and restarts the fight. Pick a unit with the arrows and a number with its - and + buttons; to spawn, pick a kind, press Spawn on a tile and click the board (Esc cancels, and the nearest free tile is used if the one clicked is taken). Setting HP, AP and movement, spawning and forcing a wave are commands, so they are in the fight's log and in its saved replay, and the log says what each did. They work on your turn, with the board still, and not while a saved fight plays. Holding wave 3 back restarts the fight with two waves in play, and letting it go restarts it again. Restart fight starts the same fight again, or plays a saved fight from its first command.

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

Saving a content file reloads it and restarts your fight with the new values, so a fight log never mixes two sets of values, and a selected preset follows its edited picks. In the editor the game reads and watches the repository's `content/`; a Mac or Windows build reads the copy it was built with, unless it is started with `-labContent <folder>`, which it then reads and watches instead. A saved fight playing back is stopped, since its recording is only valid against the content it was made with. Content that is not valid, including broken JSON and a missing file, is refused with the reason, and the game goes on with its old values: the reason shows for a few seconds and stays in the debug overlay until the next save puts it right. A game started on content that does not load waits on a screen that gives the reasons and begins once a save fixes it. The web build has no folder to read and does not reload.

Several of the spec's decisions are switches in `rules.json`, so a playtest can try the other answer: `earlyWaveWhenEmpty`, `ui.showHazardCooldowns`, `arrivalRound` (`all`, `first`, `none`), `loadout.abilities`, and `statuses.corrode.duration` (0 = rest of the fight). The spec's "Decisions taken" table gives the value chosen for each.

Any content change alters the content hash, and replays are only valid against the hash they were recorded with. A content change therefore needs every generated file regenerated in the same commit: the golden replays, the preset reports and winning lines, the random-bot reports and the sweep. Otherwise the tests fail, saved fights stop loading and the committed balance figures go stale. The commands for each are under [Regenerating generated files](CLAUDE.md#regenerating-generated-files) in CLAUDE.md.

## Headless simulator

```bash
dotnet run --project core/Sim -c Release -- --help
```

Each command below follows `dotnet run --project core/Sim -c Release --`.

| Command | What it does |
| --- | --- |
| `batch --preset hybrid --seeds 1000` | One preset, many seeds: win rate with a confidence interval, rounds for wins and losses, causes of death, damage by source, hazard fires, spec metrics |
| `presets --seeds 1000` | All three presets; saves the first winning line of each (`reports/winning-line-*.json`), the paper check |
| `sweep --seeds 1000 [--size 6]` | Random valid loadouts with the win-rate lift of every ability and upgrade |
| `tune --param units.warden.hp --values 12,16,20` | One tuning value across a list |
| `golden [--record]` | Verify (or re-record) the golden replays |
| `replay FILE` | Replay a saved command log and print its summary |
| `telemetry [DIR]` | Playtest metrics from the fights saved under `telemetry/` (or DIR), each against the spec's target and alarm, then every tester's attempts |
| `conformance` | Regenerate every committed report and compare each with its file, byte for byte; CI runs it, so a report left stale by a rule or content change fails there |

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

Counts must be whole numbers, and `--seeds`, `--workers` and `--size` at least 1. A `presets` run at 1,000 seeds takes about a minute on an 8-core machine; use `--seeds 100` while iterating.

A fight takes place in the Armory unless it names another room. To try the rooms of other sizes:

```bash
dotnet run --project core/Sim -c Release -- batch --rooms golden/rooms.json --room hall --seeds 100
```

Bots:

- **random** picks a random legal command (finds crashes, infinite loops and exploits).
- **greedy** scores each candidate action by playing out the round's minion and enemy phases and valuing the result, and takes a random legal action one time in ten. It treats a hazard showing its cue as a threat to that hazard's area, as a player who has learned the room would; **greedy-naive** ignores cues.
- **Scripted** fights are the golden replays.

The game has no randomness; all variation comes from the bot's seed, stored with each fight. The same batch with the same seeds gives identical results, whatever the number of worker threads.

## Telemetry and replays

Each fight is a replay file: loadout, settings and the ordered player commands, plus the engine version and content hash it is valid against. Replaying it reproduces the fight exactly.

Every finished fight of the player's own is saved in a folder named `<time>-<preset>-<outcome>` (or `<time>-<tester>-a<attempt>-<preset>-<outcome>` when a tester ID is set) as `replay.json`, `summary.json` (outcome, rounds, damage by source including friendly fire, abilities used, peak minions, hazard fires with victims, cause of death, turn times) and `events.jsonl` (one event per line). The editor saves to the repository's `telemetry/`, which git ignores; a Mac or Windows build to `telemetry/` in the app's data folder (`~/Library/Application Support/Lab Escape/Lab Escape/telemetry/` on a Mac); the web build, which has no disk, offers the three files as downloads on the end card.

The loadout screen lists the latest saved fights to watch again, and a replay also plays from `-labReplay <file>`, or from `?replay=<path>` beside the web build's page. A replay plays by itself (**Space** skips, **F** changes the speed), and nothing is saved at its end. One recorded on another engine version or content is refused on the loadout screen with the reason.

For playtests, give each person a tester ID: `-labTester P3` on the command line, or `?tester=P3` in the web build's URL. The game remembers it until given `-labTester -` (or `?tester=-`), and the loadout screen shows it, so clear it outside a playtest. The replay's `meta` then carries `tester` and `attempt` (1 for their first finished fight, counted per tester on that machine, so abandoned fights do not use a number), and both go into the folder name.

After a playtest round, the simulator's `telemetry` command reads every fight under `telemetry/` (or the folder given) and prints the spec's playtest metrics: first attempts lost, rounds in first-time losses, informed win rate by attempt, length of winning fights, median turn time with a full swarm, friendly-fire share, deaths by source and hazard fires per fight. It replays each `replay.json`, so a fight from another engine or content version, an unfinished fight, one played with debug commands, one with wave 3 held back and one with no tester ID are skipped and listed with the reason. The informed round continues each tester's attempt numbers, so give `--informed-from` the number of their first informed attempt; only the next three attempts from there count. Fights saved by the retired web client are read the same way.

First-time players get the one-page rules sheet and no coaching (spec, playtest round 2). Open `docs/rules-sheet.html` in a browser and print it; it fits one A4 or Letter page. It states the rules and explains the screen, and leaves out what the game hides on purpose: when waves arrive, which tiles the lab's defences cover, what makes the lab fire them, and the intercom's code words. Its numbers are tagged with where they come from in `content/`, and the tests fail if one no longer matches.

Golden replays live in `golden/`: one command log and event log per preset in the Armory, and one per room in `golden/rooms.json`, a hand-written set of rooms of other sizes. The tests fail if a build changes them; re-record on purpose with the simulator's `golden --record` after an intended rule or content change.

## Code layout

The rules engine is a deterministic library with no rendering, input or clock dependencies. `Engine.Step(content, state, command)` returns a new state and the events it produced; previews and intents call the same code on a copy.

| Folder | What it is |
| --- | --- |
| `core/Core` | The library: one namespace, `LabEscape.Core`, with a folder for each module (below). It targets .NET Standard 2.1 and C# 9, the level Unity's LTS compiles, and has no Unity dependency. It is also a local Unity package, which is why .NET's build output goes to `core/.build/` |
| `core/Sim` | The simulator, on the library, as a console tool: bots, the batch runner and its threads, reports, golden replays, playtest metrics and the command line |
| `core/Tests` | NUnit tests (below) |
| `unity/` | The Unity client (see [Unity client](#unity-client)) |

| Module | Folder in `core/Core` | Responsibility |
| --- | --- | --- |
| State | `State` | Types, commands, events, grid geometry and symmetric line of sight for a board of any size, initial state, `EngineVersion` |
| Rules | `Rules` | The `World` rules context: damage and armor, statuses, deaths and corpses, movement and mines, the chain queue and once-per-chain rule, projectiles, environment steps, waves, intercom |
| Effects | `Effects` | Ability behaviours and upgrade hooks |
| Ai | `Ai` | Enemy, minion and hazard AI (the value rule) |
| Engine | `Engine` | The round and phase loop, `Step`, `NewGame`, legal actions |
| Content | `Content` | JSON loading, validation, rooms, content hash, overrides, the list of required fields |
| Preview | `Preview` | Dry-run previews and intents |
| Telemetry | `Telemetry` | Replay records, summaries, the fight session (command log and undo) |
| Util | `Util` | Tile names, canonical JSON hashing, JavaScript-compatible text, ordered maps, statistics |

References point one way: `Util` and `Content` at the bottom, then `State`, `Rules`, `Effects` and `Ai`, `Engine`, then `Preview` and `Telemetry`, with `core/Sim` and `unity/` on top. `GameObject` is `BoardObject` and the `Debug` event is `DebugEvent`, so they cannot clash with Unity's types.

The tests in `core/Tests` cover line of sight and its symmetry, combat and timers, the value rule case by case, enemies and waves, both kits, every upgrade, the chain rule, intercom, previews matching results over 100 random states, intents matching the real end of turn, replay determinism and file checks, undo, golden replays and batch reproducibility at any worker count, plus content validation, the content hash, rooms, the simulator's options, reports and statistics, telemetry and tester labels, and the rules sheet against the content. The Unity client's own tests are in `unity/Assets/Tests`.

CI (`.github/workflows/ci.yml`) runs `dotnet test core/Tests` and the simulator's `conformance` check on every pull request. It has no Unity licence, so the Unity tests run on a developer's machine.

## Unity client

`unity/` is the game. It plays the Armory fight in greybox on a fixed isometric camera, with previews, intents and a HUD, on the C# rules core.

Needs Unity 6000.3.28f1 (6.3 LTS), with Windows Build Support (Mono) for the Windows build and Web Build Support for the web build. In Unity Hub, Installs, Install Editor, 6.3 LTS, and tick both modules; a Mac editor builds for the Mac with no module. In Unity Hub choose Add, pick the `unity` folder and open it, then press Play: every scene starts the game.

The two fonts in `unity/Assets/Resources/Fonts` are kept in Git LFS, so install it before you clone (`brew install git-lfs`, then `git lfs install --skip-repo`). A clone made without it has small pointer files in their place: the web build then draws blanks, the font tests fail, and the pre-push hook refuses to push until git-lfs is installed.

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

A still cannot show whether a motion has weight. To look at an animation, add `-labFilm 0,2,8` to that command: it films what follows those commands of the replay (0 is the fight's opening) and skips the rest. The game then advances a sixtieth of a second for each frame it draws, and every frame is saved at half size under `film/` in the output folder, with a `cues.txt` that says which cue and which sound played on which frame, and how loud the output was. Add `-labNoSystemFonts` to see the game as the web build draws it, with the two faces the game ships in place of the system's.

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
| `unity/Assets/Scripts/Logic` | No Unity code: the animation queue and its step mode (`FightFlow`), the staging that lays a frame's events out in time (`Staging`), the board's clock (`BoardClock`), targeting (`ActionModel`), log text (`EventFormatter`), the debug overlay's text (`DebugReport`), the screens' words and the loadout picker, the keys (`KeyRules`), telemetry, and which cue makes which sound (`SoundCues`) and the synth that makes it (`Synth`) |
| `unity/Assets/Scripts/Board` | The camera, room, unit models, overlays, effects and sound player, all built from code. `Feel.cs` holds every number that gives a blow its weight |
| `unity/Assets/Scripts/Hud` | Fills in the interface, the tags and coordinates anchored to the board, and the faces from the system, or the ones the game ships where it has none (`Fonts`) |
| `unity/Assets/Scripts/FightDirector.cs` | Reads input and ties the rest together |
| `unity/Assets/Resources/UI` | The interface, in UI Toolkit's text formats: `Fight.uxml` and `Fight.uss` |
| `unity/Assets/Resources/Fonts` | The faces for a machine without the system's, such as the web build: Barlow Condensed SemiBold and DejaVu Sans Mono, with their licences, in Git LFS |
| `unity/Assets/Tests` | EditMode: the golden replays on Unity's runtime, and the client logic. PlayMode: the real fight screen |
