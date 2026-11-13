# Implementation notes

How the tooling and the client read the [spec](spec.md) where it is silent, the known limitations, what the simulator found, and what the playtests found.

The rules themselves are in the spec. On 2026-10-13 the owner confirmed every reading the implementation had taken and a decision for every item the spec left open. The readings about the rules and what the player sees moved into the spec, as the Clarifications at the end of each section, and the decisions are under Decisions taken in its "Risks and decisions" section. The readings below are about the tooling and were confirmed at the same time.

## How the tooling reads the spec

### Client, telemetry and replays

- **The dev server's telemetry endpoint** writes only under `telemetry/`. The folder and file names come from the request, so each is cleaned and checked to stay inside that folder, and a request that cannot is refused with a 400.
- **Content is checked before it is used.** A hot reload is accepted only if `validateContent` passes, and anything the game reads without a fallback must be present: a missing collection (`map.hazards`, `presets`, `intercom.lines`, `hazards`, ...) or block (`rules.ui`, `rules.eggs.corpseHosts`, an upgrade's `requires`) is a reported problem and not an empty list, because accepting it crashes the next render or damage roll. Fields read with `!` are listed in `src/content/required.ts`.
- **Saved preferences and replay files are untrusted.** A stored loadout, preset, attempt list, mute setting or board view that does not have the expected shape is ignored and the defaults are used, so a stale or corrupt value cannot stop the app starting. A replay is checked for its header, the shape of the whole record and the field types of every command before it plays (a debug AP of "oops" would otherwise become NaN), and a `?replay=` file is judged against the content as it is when the response arrives, not when the request went out.
- **Effects and sounds are decoration.** The board's combat effects, its shake and alarm flashes, and the sound effects are drawn from each animation frame's events (`src/client/art/fx.tsx`, `src/client/sfx.ts`) and never read a rule. A primed hazard makes no sound and gets no effect beyond its cue, so the spec's "only its cue" stays visual. At Skip speed only the last frame of a sequence shows its effects and plays its sounds.
- **The isometric view is a projection, not a second board.** Both views draw the same state through `src/client/art/geometry.ts`: floor things (highlights, hatching, area tiles, vents, panels) in floor space through the projection's transform, upright things (terrain blocks, units, objects, chips, text) front-on at projected points. Previews and intents still come from the engine. The top-down projection is the identity and keeps the old layer order, so the default board renders exactly as before; this was checked by rendering both from the same states and comparing the markup. In the isometric view, units are drawn in depth order, so their slide is driven frame by frame (`src/client/useSlides.ts`) instead of by a CSS transition, which reordering the elements would cancel. The replay's `meta.view` records the view (`top`, `iso`, or `both` if switched mid-fight); replays ignore it. The briefing's map of the room stays top-down in both views.
- **The title and briefing come before the fight exists.** The fight session, and with it the turn clock, starts when the player leaves the briefing, so turn times are measured as before. Retry skips the briefing. The briefing states what the rules sheet states (the goal, the opening intercom line, the kit) and nothing the game hides.
- **Replays stop at a rejected command.** A replay command the engine refuses (a corrupted or edited file) pauses playback there and says which command it was, instead of playing the rest against a state it no longer matches.
- **Action shortcuts** run 1 to 9, 0, then `-` and `=`, because a full loadout (8 abilities) plus the four utilities is 12 actions. With Ctrl, Cmd or Alt held, the action keys and `V` (board view) are left to the browser; the other shortcuts do not check modifiers yet (see the roadmap). `E` always ends the turn; `Enter` does too unless a button or link has focus, when Enter operates that control instead (a focused button is focused after a mouse click as well, so Enter after clicking a button presses it again).

### Playtest report

- **The playtest report reads replays, not `summary.json`.** `npm run sim -- telemetry` replays every `replay.json`, because the summary does not record the minion count per turn and a replay is checked against the engine version and content hash. A fight with debug commands, or with wave 3 held back (a different win condition), is not counted as an attempt. The preset of a fight comes from its loadout (a match with a preset's abilities and upgrades), not from the replay's `meta.preset`. Telemetry does not record the playtest round either, so `--exclude-tester` leaves out testers such as the developer. A fight with no tester ID is not counted, unless `--include-unlabelled` is given: ordinary play is saved in the same folder.
- **Informed attempts are chosen by attempt number.** Telemetry does not record which playtest round a fight belonged to, and a tester's attempt numbers carry on from round 2 into round 3, so the report counts a tester's fights on the informed preset from `--informed-from` (default attempt 2) for `--informed-attempts` attempts (default 3, the spec's number) as informed. If a tester plays more than one first-time attempt, set `--informed-from` to the number of their first informed attempt. One cutoff cannot fit testers who needed different numbers of first-time fights; recording the round is a roadmap item. A replay whose turn times do not match its turns (one missing or extra) gives no turn time, and the row says how many fights were left out.
- **Playtest metric details.** The first attempt of each tester, and each informed attempt, is their earliest fight with that number, since attempt numbers are counted per browser. "First-time losses" are all lost attempts before `--informed-from`, one per tester and attempt number. Figures print with one decimal, so a value never reads as the other side of its alarm. The friendly-fire mean leaves out fights where the player took no damage, as the simulator's report does. "Share of deaths from one source" groups by category (player, minion, enemy, hazard), like the simulator's report, and a loss with no killer is the round cap. The quit-after-one-loss alarm cannot be told from a tester who has not played again yet, so the report states how many testers have no fight after their first loss and does not set the status from it.

### Simulator

- **Greedy bot** looks one action ahead. It plays out the round's minion and enemy phases after each candidate and scores what is left.
  - **Pruning:** shield pairs only within 2 tiles, grapple self-pulls deduplicated by landing tile, Spore Pods only on enemies.
  - **Sprint** is scored by its best follow-up move.
  - **Cues:** by default it treats a hazard showing its cue as a threat to the tiles that hazard covers, as a player who has learned the room would. `--bot greedy-naive` turns that off.
- **Command-line numbers** (`--seeds`, `--from`, `--workers`, `--size`) must be whole numbers, and `--seeds`, `--workers` and `--size` at least 1. Anything else is an error that names the option. A `NaN` used to reach the worker pool, which then started no workers and let the run end silently with exit code 0.

## Known limitations

- Multi-tile moves animate as one straight slide between start and end tiles, not tile by tile.
- Telemetry is saved by the dev server (`telemetry/`). A static build (`npm run build`) has no endpoint, so players download the files from the result screen.
- The client was checked by hand in Chrome at desktop sizes, in light and dark themes. The narrow-layout (phone) breakpoint exists but was not playtested.
- In the isometric view a figure stands up over the tile behind it, and a unit's head can overlap the feet of the unit behind. Pillars and walls fade when they would hide a unit, an object or the hovered tile; units never fade, but every HP bar, chip and badge is drawn above all figures. At phone width the isometric tiles are about half as tall as top-down ones, so they are harder to tap.

## Rooms as data

Since M11 a fight takes place in a room: a floor plan, its hazards, its waves and its round cap, held together as a `RoomDef`. Each fight's settings can name one (`settings.room`), and a fight that names none is in the Armory. The name travels with the replay, and a replay naming a room the content lacks is refused.

- **The Armory stays in its old files until the freeze ends.** Its room is built on load from `map.json`, `waves.json` and `rules.roundCap`. Those files did not change, so neither did the content hash, the golden replays or the committed reports.
- **Other rooms go in an optional `rooms` list in the content.** Nothing ships in it yet, so `content/rooms.json` does not exist. The list is part of the hash only when present, so adding a room makes old replays stop loading, as any content change does.
- **A room's `legend` and `terrain` add to the ones in `map.json`.** A room names only the tile types it brings of its own. A void tile for a non-rectangular room is ordinary terrain that blocks everything: no code knows about it.
- **Rooms are limited to 26 columns and 99 rows,** because tile names are one letter and up to two digits. Validation rejects anything larger. The plan's largest room is 14 wide.
- **Validation is the same for every room, the Armory included.** Beyond what it checked before, it now rejects a player start or a wave spawn on terrain that blocks movement, which the engine would otherwise have quietly moved, a terrain type with a missing blocking flag, and two hazards with one ID.
- **Test rooms.** `golden/rooms.json` holds three hand-written rooms: 6×5, 12×12 and a non-rectangular 14×8. They are not game content and carry no balance meaning. Each has a golden replay recorded by the TypeScript, which the C# tests reproduce event for event.

## The C# rules core

`core/` is the rules core ported to C# (M9). It reads the same content files and must behave exactly as the TypeScript engine does until M13.

**How it was proven (2026-11-08).** The C# content hash equals the hash the TypeScript engine recorded. Each golden replay reproduces its `golden/*.events.jsonl` byte for byte, and its event and final-state hashes match. A one-off comparison then ran 3,150 fights from the TypeScript engine through the C# one: 3,000 random-bot fights over presets and random builds, 150 greedy-bot fights, six content variants and random debug commands. At each of the 47,022 steps it compared the events, frames, state, legal commands, action statuses, move options, intents and a preview, and it also tried 47,484 random or illegal commands and compared the verdict and error text. There were no differences. The simulator's `conformance` command now repeats this at scale: see [the C# simulator](#the-c-simulator).

**JavaScript behaviour the port reproduces**, in case a change touches one of these:

- Insertion-ordered maps are `OrderedMap`. Where the order shows, in hit and move lists and validation messages, a `Dictionary` would be wrong.
- Sorting uses `Sequences.StableSorted`, because `List.Sort` is not stable and `Array.prototype.sort` is.
- `structuredClone` is `GameState.Clone`. The classes it does not copy (`SourceRef`, `HazardDecision`, `IntercomEntry`, `Outcome`) must stay unchanged once made.
- Number formatting, string escaping and key order in the hash and event logs are in `JsText` and `Hash`. Event classes write their fields in the order the TypeScript objects hold them, which is why `maxAmmo` comes before `ammo`.
- `Tiles.Parse` is written by hand, because a .NET regular expression's `\d` also matches non-ASCII digits.

**Where the two differ on purpose.** No valid fight reaches any of these.

- Content shapes that make `validateContent` throw a `TypeError` (a wave with no `units`, a preset with no `abilities` or `upgrades`, an intercom line with no `trigger`) are reported as problems in C#.
- `ContentLoader.Build` also rejects values the TypeScript validator does not check, such as an upgrade's unknown damage type or a fractional number in a field the rules treat as whole.
- `Command.Parse` throws on an unknown command type or debug operation, where the TypeScript `step` does nothing and returns ok.
- JSON.NET replaces a lone surrogate in a JSON string, so content holding one would hash differently. The hash code itself handles them as JavaScript does.

Tests of the web client and dev server have no C# counterpart.

## The C# simulator

`core/Sim` is `src/sim` ported to C# (M10), on the telemetry code in `core/Core/Telemetry`. Telemetry is in the library rather than the tool because the Unity client will record fights with it too.

**How it was proven (2026-11-08).** `conformance` reruns the greedy and random presets and the sweep, 1,000 seeds each (7,000 fights), and compares the 18 committed reports byte for byte. All 18 matched, and CI now checks this on every pull request. A one-off comparison then ran the same commands through both simulators and compared exit codes, printed output and every file written, per-fight CSVs included: 21 commands (other bots, overrides, wave 3 held back, sweep sizes, negative seeds, `tune`, `golden`, `replay` and bad options), `telemetry` under five sets of options on 52 mixed folders, and 670,000 random cases of number formatting, `toFixed`, `localeCompare`, `Math.round` and indented JSON against Node. The only difference was the wording of an operating system's file error.

**JavaScript behaviour the port reproduces**, in case a change touches one of these:

- `JsText.ToFixed` works on the double's exact binary value and rounds a tie up, as `toFixed` does. A .NET format string is not guaranteed to.
- `Hash.Stringify(value, 2)` is `JSON.stringify(value, null, 2)`, the form of the stats and replay files.
- `Stats.RoundHalfUp` is `Math.round`; `Math.Round` sends a tie to the even number.
- `LocaleOrder` is `localeCompare` for ASCII, in ICU's order: punctuation, digits, then letters with lower case before upper. The tool runs without culture data, and an ordinal comparison would sort tester IDs differently.
- `StrictJson` refuses what `JSON.parse` refuses. JSON.NET alone accepts comments, single quotes and bare `NaN`.
- `Options.ParseInteger` reads a number as `Number()` does (`1e3`, `0x10`), and `ParsedArgs` follows Node's `parseArgs`, so a value starting with a dash needs the `--from=-3` form.

**Where the two differ on purpose.**

- Threads share one `Content`, which is only read, where each TypeScript worker builds its own.
- A seed or count too large for a 64-bit integer is refused, where JavaScript carries on with a rounded number.
- The wording of errors from the file system and from a JSON parser is .NET's.

**Speed.** 300 greedy Hybrid fights took 7.9 s against 17.0 s for the TypeScript, on 34 s of processor time against 135 s (a 10-core Mac, 2026-11-08). `conformance` takes about 75 s there.

## The Unity client

M12 built one fight in `unity/`, in greybox. It runs the C# rules core and keeps the web client's behaviour, so the owner can compare the two.

Where it differs from the web client on purpose:

- **One view.** A fixed isometric camera, as the [phase plan](phase-2.md#the-move-to-unity) decides, with pan and zoom. The top-down view and its V key are gone.
- **A unit's side is still its base shape.** Round for the player's side, a square that this camera shows as a diamond for the lab's soldiers, a hexagon for the Warden.
- **Words where the web client has icons.** Statuses read `PSN 2`, `COR 1` and `PIN`, and armor reads `ARM 2`, because greybox has no icon art. Each is a word or number as well as a colour.
- **Moves walk tile by tile.** The web client slides straight from start to end.
- **A frame's events are staged in time.** The web client starts every effect of a frame at once. See [how a frame is animated](#how-a-frame-is-animated).
- **A fight has an entrance.** The lights strike, the room's name is set down, the camera pulls back from the player and the first wave falls in. Space skips it.
- **The log follows the screen.** A line is written when its event is seen, so the log never reports a hit before the shot is fired. Watched or skipped, a fight leaves the same log.
- **One dark theme,** in the web client's dark colours.
- **Each sound plays on its cue.** The web client plays a frame's sounds as the frame starts; here a shot is heard as it leaves and its hit as it lands. See [the fight's presentation](#the-fights-presentation).
- **The buttons take no keyboard focus,** so Space and Enter always mean skip and end turn. The web client's focus rules in `src/client/actionKeys.ts` have nothing to act on here.

Not ported yet, and left for M13 by the milestone table: the title, briefing and result screens. The start screen is a plain preset picker and the loadout screen does not exist yet.

**Telemetry and replays (2026-11-12, M13's first part).** A fight of the player's own is saved as the web client saves it, the same three files in a folder named by `Labels.TelemetryFolderName`, so `npm run sim -- telemetry` reads both clients' fights (README, [Telemetry and replays](../README.md#telemetry-and-replays)). Where the files go is `TelemetrySink`'s choice: the repository's `telemetry/` in the editor, the app's data folder in a Mac or Windows build, downloads in the web build, through a small browser plugin (`Assets/Plugins/WebGL/LabDownload.jslib`, text, not a binary). The attempt is numbered just before the fight is saved, as in the web client, so a fight abandoned on the way does not use a number. A replay plays its commands by itself, one when the board is still and the player's turn has come, and its session is not timed, so its meta keeps the recording's turn times. The player's controls, highlights and previews wait until it has finished; a replay that ends unfinished can be played on from there, and is still not saved. Where the web client loads a replay from its debug panel, Unity lists saved fights on the start screen. P pauses a replay between commands and N plays one command while it is paused; the command already playing finishes either way, and a command asked for while the board is busy plays as soon as it is still. The tests and the smoke run keep their fights apart from a playtest's (`TelemetrySink.Isolate`): their files go to a folder of their own and their tester and attempt counts stay in memory, so they never use up a tester's attempt numbers. A replay's loadout is checked against the game (`Engine.LoadoutProblems`) when it is loaded, since `Records.CheckCompatible` checks only its shape; a fight that still fails to start puts the start screen back with the reason. In the web build, a `?replay=` that arrives after the player has started a fight is dropped.

**The debug overlay (2026-11-12, M13's second part).** The backtick opens and closes it. It is a column of its own to the left of the board, so it covers none of it and the camera frames the room in what is left. It shows what the web client's shows that explains a fight (README, [Unity client](../README.md#unity-client)), worked out in `DebugReport`, which has no Unity code. Where it reads differently from the web client:

- **The board's marks follow cues.** The board keeps the hazards and the count of waves as it has shown them: a hazard moves on to the frame's state when a cue of its own plays (it primes, holds, fires or stands down), a wave counts once its arrival has played, and `BoardView.Level` brings both level with the state between frames. A hazard's mark and a spawn tile's mark therefore change when the cue is seen, never when the engine resolved it, and a PlayMode test checks a priming hazard before and after its cue. Unit IDs ride on the figures' tags, which already follow the board. The Flamer's scores come from the intents, so they show only while the player can act, as intents do.
- **The panel agrees with the board.** It reads the state on screen (`Flow.Shown`) like the rest of the HUD, and the board's hazards and wave count, so while a frame plays, or is held in step mode, it describes what is on the board. The web panel reads the live state.
- **Step mode holds a frame at its end.** A frame plays out with all its cues and then waits while another is queued behind it; N shows the next at once, cutting the one on screen short (its cues still play), and Shift+N runs on to the last frame before the next phase starts. When nothing is queued the fight goes on as usual, where the web client waits for one more N. Step mode acts only while the overlay is open, so closing the overlay never leaves a fight held, and a test checks that a fight stepped through leaves the same log as one skipped.
- **N also steps a paused replay.** With a frame held, N shows the next frame; otherwise, with a saved fight paused, it plays the next command. P pauses and plays on.
- **Tile names are not written on every tile,** where the web client's IDs toggle does: the Unity board's edges always carry its coordinates.
- **Edits are steppers and a click on the board.** There are no text fields: a focused one would take the game's keys (E, Space, 1 to =) while the player typed. The unit and the kind to spawn are picked with arrows, HP, AP and movement with - and + (`DebugEdits`, no Unity code), and a spawn waits for a click on a tile. The unit picked starts with its own HP, and AP and movement start at a turn's, from the rules.
- **When they work.** The web panel applies an edit whenever the fight is not busy. Here Set HP, Set AP and movement, Spawn and Force next wave work on the player's turn with the board still and no saved fight playing, since an edit to a recording's fight would diverge from it. The buttons are off otherwise, and on the start screen.
- **Holding wave 3 back restarts the fight,** as the web client's checkbox does, with `maxWaves: 2` in the fight's own settings, so it is in the replay. Restart fight starts the same loadout and settings again; for a saved fight playing back it plays the recording again from its first command.
- **A closed overlay leaves nothing waiting:** a spawn waiting for a tile is dropped when the overlay closes, when the fight restarts and when Esc is pressed.

**Content hot reload (2026-11-13, M13's third part).** Saving a content file restarts the fight with the new values, as in the web client, and content that does not validate is refused and the old values stay (CLAUDE.md, invariant 3). It differs from the web client in how it works and in what it does with a fight:

- **It polls.** `ContentWatch` (no Unity code) compares the write time and size of the nine content files twice a second, and reads them once a change has held still for a poll, so a save caught half-way is not read. It is not a `FileSystemWatcher`, which is unreliable under Mono on a Mac and needs a thread. Every failure is a refusal that names the file: invalid values, broken JSON, a missing file. Saving a file with the same content is not a reload, and putting the old content back after a refusal only clears the error.
- **Where it looks.** `GameContent.Folder`: the repository's `content/` in the editor, and in a Mac or Windows build the folder given by `-labContent`, else none. A build with no folder reads its Resources copy and never reloads, and the web build has no disk, so it does not either.
- **What a reload does to a fight.** The player's own fight restarts: a preset from its edited picks, and a loadout that is no preset's as it was. If the preset is gone or the loadout no longer fits (`Engine.LoadoutProblems`), or the fight cannot start, the start screen comes back and says why. A saved fight playing back, or just finished, is stopped at the start screen, where the web client turns it into a fight of the player's own, because a recording is only valid against the content it was made with. The unfinished fight is not saved, as when the player leaves it.
- **Starting on broken content.** The game waits on the start screen with the reasons, and begins once a save makes the content valid.
- **The debug overlay** says where the game looks, or what it refused, under its header, which shows the new hash. Each reload and each refusal is also written to the player log (`[Content] ...`), which is how a build can be checked with no window: run it with `-batchmode -nographics -labContent <a copy of content/>`, save files in the copy, and read the log.

`FightDirector.WatchContentIn` points the watch at another folder, so a test can edit a copy of `content/` (`ScratchContent`) and put the game back after; there is no test of starting on broken content, which only a second director could show, so the watch's own tests cover the file side and the Mac build the rest.

**Unity 6.3 LTS (2026-11-12).** The project was started on 6000.6.8f1, an update release, and moved back to 6000.3.28f1, the LTS the phase plan asks for. 6.3 has no `physicscore2d`, `tetgen` or `timelinefoundation` built-in module, so the manifest no longer lists them, and its test framework is 1.6.0 where 6.6's was 1.8.0. Nothing else changed: the tests, the Mac, Windows and web builds, the system fonts and the sound behave as they did on 6.6, and the web build is 11 MB.

The editor reads `content/` directly. A player cannot, so `ContentBuildStep` copies the files into `Assets/Resources/Content` before each build; that copy is ignored by git.

**The first web build (2026-11-09).** The phase plan names download size and preview speed on the web as risks to check at M13, so the greybox fight was built for the web early. It built in 98 s and is an 11.7 MB download: 8.9 MB of engine and game code and 2.8 MB of data, both gzip-compressed. In a Chromium browser on a 10-core Mac the fight played from the preset picker through an enemy phase, with the mouse and the keys. While an action was selected and its targets hovered, frames stayed at about 8 ms, and none of 370 took longer than 13 ms, so previews are not slow at the Armory's size. One fault showed: the arrow in `Sidearm → E2` and the dash in `1–9` were blank, because the web player has no system font to fall back on (see the [roadmap](roadmap.md#smaller-items)). For the same reason the web build keeps Unity's default face where the Mac and Windows builds take the web client's (see [the fight's presentation](#the-fights-presentation)). The size is before any art or sound, and the project still lists every built-in module, physics included.

The room is built from `RoomDef`, so other rooms should draw without changes, but only the Armory has been looked at. The two far walls follow the room's bounding rectangle, so a non-rectangular room will need its own edges at M14.

### How a frame is animated

The owner asked on 2026-11-10 for animation with weight behind it. Until then every event in a frame started at the same instant: a shot, its damage number and the death it caused appeared together, and each frame held the queue for a flat `rules.ui.secondsPerActor`.

**Staging.** The engine resolves a whole action at once and lists its events in the order they caused each other. `Staging` (in `Logic`, no Unity code) turns that order into moments, called cues: a unit gathers, fires, the shot flies, it lands, the target reacts, and only then does the next thing start. It reads the events and never the rules.

- Every length is in beats, and one beat is `rules.ui.secondsPerActor`, so content keeps its single knob for pace. The lengths themselves are constants at the top of `Staging.cs`.
- A blow from the next tile with nothing in flight is staged as a melee strike. A unit's death is staged at the blow that killed it, wherever its event sits in the list. A blast reaches farther tiles a moment later. A wave falls in one unit after another.
- A shot that a Reflective Shield sends back keeps the source of the shot it returns, but it leaves the shield. It is staged with no actor, so the unit that fired the original neither winds up nor recoils a second time.
- A status's damage is told apart before any of that. The engine gives a tick the source of whoever applied the status, who may be standing next to the unit by now, with the status's own name as what did it. Without the check, a hatchling beside a guard it had poisoned would be shown biting it again each round.
- A frame holds the queue for as long as its stage needs. A frame with nothing to show passes at once, where it used to hold for a tenth of a second: 31 to 46 frames of each winning line are like that.
- Measured over the three winning lines, a fight's animation takes 47.7 s where it took 52.6 s (Tech), 49.5 s where it took 47.5 s (Hybrid) and 58.0 s where it took 52.3 s (Alien), before the stops below. The fight is no slower; the time moved from waiting to blows.

**The board lags the state on purpose.** While a frame plays, the board shows each event as its cue comes. A unit keeps its figure and its HP bar until the blow lands, and a wave unit has no figure until it starts to fall. Between frames, and after a skip or an undo, `BoardView.Level` brings the board level with the state: a figure for every unit and object in it, on its tile, showing its HP, and nothing else. After a skip or an undo it also ends whatever was still on its way, so a mine is not left in the air or a corpse unseen. PlayMode tests check that before every command of a winning line and after a skip part way through a turn.

**The board's clock.** `BoardClock` stops the board for 0.02 to 0.12 s when a blow lands, longer for a bigger one, and plays the blow that ends the fight at about a fifth of its speed. Blows within a fifth of a second share the longest stop, so a blast that reaches three units stops the board once. The interface and the camera stay on real time. The 2x speed doubles this clock instead of shortening each frame, so everything on the board keeps its proportions.

**Weight.** Figures ride on springs (`UnitView`). A hit shoves and tips a body away from where it came from and squashes it; the springs that bring it back are slow and well damped, because a body that is knocked fast and recovers slowly reads as heavy, and one that wobbles reads as rubber. A unit gathers before it acts and recoils after. A wave unit falls stretched and lands squashed. A body that dies is knocked off its feet and falls, a machine or an object breaks into its parts, and a hatchling bursts. How heavy a unit is comes from its data: its HP plus twice its armor, against the player's (`Heft`). The Warden is three times the player and a hatchling a third, so new enemies get a weight without a line of code.

The camera shakes by the square of its trauma, is kicked the way a blow went and punched in by a blast. Sparks and debris fall and bounce, landings throw dust, blasts leave scorch marks, and effects throw light on the room through four point lights in the greybox shader.

Every number that sets how this feels is in `Feel.cs`. None of them is a rule, and none is in `content/`.

**Looking at it.** A screenshot cannot show whether a motion has weight. The smoke run's `-labFilm` saves every frame of chosen animations (README, [Unity client](../README.md#unity-client)); the first pass was tuned from those frames.

Not done here:

- **A reduced-motion setting.** The web client has one. See the [roadmap](roadmap.md#smaller-items).

### The fight's presentation

On 2026-11-12, ahead of the M12 checkpoint, the owner asked for the quick fixes that would do most for the Unity fight's presentation, and chose to bring sound forward from M13. Most of them bring the web client's look of 2026-10-20 across. The project still keeps no image, font or sound files: all of it is made in code.

- **Type.** `Fonts.cs` takes the web client's two faces from the system, as the web client does (`--display` and `--mono` in `src/client/styles.css`): DIN Condensed and Menlo on a Mac; on Windows the lists lead to Bahnschrift and Consolas, which no Windows build has checked yet. An element asks for one with the class `display` or `mono`, and gets the class `face` when it is found, so `Fight.uss` sizes it for that face. The web build has no system fonts and keeps Unity's default face and sizes; so does any build started with `-labNoSystemFonts`.
- **Panels** have the web client's frame, made in code: an accent tick at two corners, a shade from top to bottom and an LED before the title. Key hints are keycaps.
- **Coordinates.** Columns from A along the near-left edge and rows from 1 along the near-right edge, as `Tiles.Name` writes them and the web board shows them. The Scanner names a tile with no unit on it.
- **The room** stands on a dark floor ruled in tiles that fades into the dark, lit by the web client's teal glow from behind, with the slab's shadow on it and hazard stripes on its two near lips (`RoomView`). The floor is shaded at the foot of the far walls and around every block, standing faces darken toward the floor, and the room's light pools in the middle of the floor (`Solid.shader`, set by `Lighting.SetRoom`). Effect lights are added on top, so a blast still lights the room.
- **Flashes.** The alarm and a blow to the player light the board's edges from inside, as the web client's inset glows do, instead of a flat tint and a hard red border.
- **The end card** gives the fight in four figures from its summary (`FightSession.Summary()`): rounds, HP left, kills and damage taken, with how much of it was your own fire.
- **Rules on hover.** Tooltips show only in the editor, so pointing at an action button puts its rule in the Scanner instead, with any reason it cannot be used. An action that cannot be used stays clickable, so a click says why. A reason every action shares, such as "Not your turn", dims the bar instead of being written on each button.
- **Sound.** `Synth` renders the web client's recipes, a swept tone and filtered noise, to samples at start-up, and `SoundPlayer` makes them clips. `SoundCues` says which cue makes which sound: as in the web client, except that each plays on its cue, a heavy landing or footfall thuds, a corpse is silent (its death is heard), and the end of the fight is heard when its card is shown, so a skipped fight still ends with it. A primed hazard is silent, as the spec asks. Sounds asked for in one frame play together at its end, at most four within 0.06 s, the most important first, and a sound already playing is not started again. A skip or an undo plays nothing. M mutes, and the choice is kept in `PlayerPrefs`. The filmed smoke run notes each sound and the output's level in `cues.txt`, which is how the timing was checked: a shot is heard on the frame it leaves and its hit on the frame it lands.

## What the simulator says (greedy bot, 1,000 seeds per preset)

From `npm run sim -- presets --seeds 1000` and `npm run sim -- sweep --seeds 1000`; the full reports are in `reports/`. Rerun them after any rule or content change and update this section with the new figures.

| Preset | Win rate | Median rounds (wins) | Notes |
| --- | --- | --- | --- |
| Tech | 72% (69 to 75) | 14 | 10.5 turrets per fight; Guards shoot the nearest player-side unit, so a 2 AP turret every turn soaks their fire |
| Alien | 46% (43 to 50) | 11 | 205 of 536 losses are to the player's own poison (spread, and Spore Burst from hatchlings and Guards) |
| Hybrid | 83% (81 to 86) | 12 | 66% of the player's damage is friendly fire, over the 35% alarm |

- **Paper check:** passes. Every preset has a winning line; they are saved as `reports/winning-line-*.json` and play back with `?replay=reports/winning-line-hybrid.json`. No Warden HP cut was needed.
- **The Warden** reaches the player in only 3% (Alien) to 28% (Tech) of fights that reach round 7, against the spec's "most". Its approach rule (prefer lining up a charge lane) plus minions in its lanes keep it at bay.
- **Hazards** fire 1.9 (Alien) to 3.8 (Tech) times per fight, meeting the "at least 2" target except on Alien.
- **Loadout sweep** (1,000 random valid loadouts): 76% wins overall. No pick lifts the win rate by more than 12.4 points (Auto-Turret), comfortably under the 25-point target. Next come Parasite (+11.9), Reflective Shield (+11.6), Parasite Jump (+11.1) and Barrier Shield (+9.9). Grapple Pin (−11.7), Grapple Hook (−10.6) and Brood Egg (−10.3) score lowest, partly because a one-step bot uses them badly. Friendly fire is 54% of the player's damage across the sweep (alarm above 35%). Full table: `reports/sweep-8-greedy.txt`.
- **Random bot:** 3,000 fights across the presets, no crashes, illegal states or stalled fights; it never wins and dies around round 3.

The bot sees the same previews and intents a player does, but it checks every candidate exactly. Human win rates will be lower; these figures are the ceiling the room allows a careful player. If first playtests also win too often, the first knobs per the spec's table are Guard damage, wave timing and the minion cap, and the Guard's "nearest unit" rule is what makes turrets so strong.

## What the developer playtest round says (spec round 1)

Written before the later sessions. Where this section looks ahead to rounds 2 and 3, read it with [what the outside testers said](#what-the-outside-testers-said-2026-11-08): playtesting closed without those rounds' metrics being recorded.

The owner played 22 fights to the end, on 2026-10-03 and 2026-10-07. The playtest report counted 21 of them: Alien 10, Tech 6 and Hybrid 5. It left out one custom-loadout fight played with wave 3 held back. Result: 3 wins (Hybrid twice, Alien once) and 18 losses. The owner found no major bugs. The one reported, the Scout Drone dying at once, is the rules as written (see below).

**How the figures were gathered.** Round 1 ran on a checkout from before 2026-10-06: engine 0.1.0, with no tester IDs. `main` has been at engine 0.1.1 since PR #3, which changed only how units that die in the same effect as the player are reported. The playtest report refuses a replay from another engine version (invariant 4 in CLAUDE.md), so the owner copied the fights and restamped them as 0.1.1. That is sound for these figures, which do not use what changed. Every replay also ran to its end without a rejected command. The report was then run with `npm run sim -- telemetry <folder> --include-unlabelled`.

**The owner's verdict:** the round was a success and the game is fun. The Alien kit was the favourite. Hybrid was the easiest, and won again in its fifth fight. Tech was hard to get a footing in, and the owner will revisit it.

**The playtest report** (spec metrics; the first-time and informed metrics need tester IDs and belong to rounds 2 and 3):

| Metric | Round 1 | Target | Status |
| --- | --- | --- | --- |
| Length of winning fights | Median 12 rounds (11, 12 and 15) | 8 to 12 | OK |
| Median turn time with a full swarm | 16.2 s, over the one turn that ended with a full swarm | Under 60 s | OK |
| Share of player damage from own effects | 0.6% | 5% to 30% | Alarm: under 3% |
| Share of deaths from one source | Enemies 100% | No source above 60% | Warning |
| Hazard fires per fight | 0.62 | At least 2 | Alarm: under 1 |

**By preset:**

| Preset | Fights | Wins (round) | Rounds of the losses |
| --- | --- | --- | --- |
| Alien | 10 | 1 (12) | 2, 3, 3, 3, 3, 4, 4, 4, 5 |
| Tech | 6 | 0 | 3, 6, 6, 6, 12, 13 |
| Hybrid | 5 | 2 (11, 15) | 2, 2, 2 |

**Four result screens** the owner shared (times are the telemetry folder names, UTC):

| Fight | Result | Rounds | Damage to the player (enemies, hazards, own) | Peak minions (lost) | Hazard fires | Warden | Median turn |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Hybrid, 20:15 | Win | 11 | 3, 2, 0 | 5 (12) | 2: the gun hit a player-side object in round 3; the panels hit the player and a hatchling in round 9 | Never reached the player; killed in round 11 | 32.0 s |
| Tech, 20:38 | Loss, Warden charge | 13 | 8, 0, 0 | 1 (3) | 1: the gun hit a player-side object in round 4 | Reached the player in round 13 | 18.5 s |
| Alien, 20:54 | Win | 12 | 6, 1, 0 | 3 (15) | 3: vent B5 hit the player in round 4; the gun and the panels hit hatchlings in rounds 11 and 12 | Never reached the player; killed in round 12 | 13.9 s |
| Tech, 21:00 | Loss, Guard shot | 12 | 8, 0, 0 | 1 (1) | 2: the gun hit a Guard in rounds 3 and 7 | Never reached the player | 16.3 s |

**What it says:**

- **Win rate.** 3 of 21, against the bot's 46% (Alien) to 83% (Hybrid). A gap was expected, since the bot checks every action exactly; one player learning the room is not yet a measure of difficulty. Rounds 2 and 3 measure it.
- **Most losses come early.** Half the losses (9 of 18) came by round 3, four of them in round 2, when wave 1's three Guards first fire. Only the two Tech losses in rounds 12 and 13 reached wave 3. All three Hybrid losses came in round 2. The median loss came in round 3.5. Round 2's matching metric is "first-time losses that reach wave 2", with a target that most reach round 4 and an alarm if the median loss comes before round 3. On 2026-10-08 the owner chose to keep early losses as a round-2 watch item, with no tuning yet: one developer's repeated attempts do not settle first-time difficulty.
- **Length of winning fights.** Median 12 rounds, on target; the 15-round Hybrid win is past the target but under the 16-round alarm.
- **Turn time.** Only one turn in the whole round ended with a full swarm (16.2 s). Over whole fights, the four screens' median turns took 13.9 to 32.0 seconds.
- **Friendly fire** was 0.6% of the player's damage across all 21 fights, under the spec's 3% alarm. The bot's alarm points the other way (66% on Hybrid, 54% across the sweep, over 35%). With previews marking friendly hits, a player who knows the rules avoids them. Whether first-time players do is a round 2 question.
- **Deaths.** Every loss came from enemies; hazards and the player's own effects killed nobody.
- **Hazard fires** averaged 0.62 a fight, under the alarm of 1. Short fights account for much of it: a hazard first fires in round 2, a round after its cue, and 12 of the 21 fights were over by round 4. The four longer fights on the screens saw 1 to 3 each. The gun hit a Guard twice in one fight, the bait play the spec wants ("Acceptable losses" played).
- **The Warden** reached the player in 1 of the 4 fights on the screens and killed them with a charge. The bot's figure is 3% to 28% of fights that reach round 7.
- **Tech is played differently from the bot.** The bot places 10.5 turrets a Tech fight on average, over wins and losses together. On the two Tech screens the owner placed 1 to 3 turrets and leaned on the Sidearm (16 and 20 shots), Barrier Shield and mines. Whether Tech is too hard for people or only harder to read is open until round 2.
- **Alien** won on Spore Pod (8 uses), Brood Egg (7), Parasite (6) and Acid Spit (6), losing 15 hatchlings along the way; Lunge was used once.
- **Hybrid** won on Sidearm (9), Parasite (8), Spore Pod (4), Brood Egg (3), Acid Spit (3) and Barrier Shield (2), filling the swarm and losing 12 minions; a Proximity Mine was placed once.

**The Scout Drone often dies within a turn or two.** The owner deployed it a few times, and each time it flew toward the enemies and was shot on its first turn. That is the rules as written, not a code bug. `droneTurn` flies the drone until the nearest enemy is within its zap range of 2 and in sight, which can leave the drone the nearest of the player's units. Guards shoot the nearest player-side unit in sight for 3, and the drone has 3 HP. Deployed from far back, it may not get within range on its first move, and then it dies without zapping, as the owner saw. The greedy bot's fights (150 per preset) show frequent short lifetimes: 76 of 96 Tech drones (79%) and 402 of 535 Hybrid drones (75%) died in the round they were summoned or the next.

| Preset (bot) | Drones summoned | Died | Died in the round summoned | Killed by Guards | Rounds zapped per drone |
| --- | --- | --- | --- | --- | --- |
| Tech | 96 | 84 (88%) | 56 of 96 summoned | 74 of 84 | 1.4 |
| Hybrid | 535 | 410 (77%) | 75 of 535 summoned (327 more the next round) | 358 of 410 | 1.8 |

The bot still summons 3.6 drones a Hybrid fight. Absorbing a Guard shot is a plausible benefit alongside chain damage and, with Plague Drone, poison. Summon frequency alone does not establish why the bot chooses the drone or whether players find it worth 2 AP. Most Hybrid drones survive the summon round; short lifetimes are not universal first-turn failure.

More HP alone is not a clear fix on the bot's figures (greedy bot, 95% intervals), from `npm run sim -- tune --preset tech --param units.drone.hp --values 3,4,5 --seeds 200 --workers 1` and the same with `--preset hybrid`. `--workers 1` is needed while the worker-thread problem on the roadmap stands:

| Drone HP | Tech win rate | Hybrid win rate |
| --- | --- | --- |
| 3 (now) | 75.5% (69 to 81) | 83.5% (78 to 88) |
| 4 | 69.5% (63 to 75) | 80.0% (74 to 85) |
| 5 | 68.0% (61 to 74) | 93.5% (89 to 96) |

At 4 HP the drone survives one Guard shot, yet neither preset moves beyond noise. At 5 HP Hybrid, already the easiest preset for the owner, wins 93.5%. These figures do not establish whether a player would find a tougher drone useful; a longer zap range was not tested.

**Owner decision, 2026-10-08.** For round 2, retain the current drone rules and values and evaluate it as a fragile decoy with chain-damage utility. Decoy usefulness remains a hypothesis to validate with players, not a guarantee that it draws fire: Guards still choose targets by their normal rules. Watch whether players understand its movement and find it useful. Revisit movement if they understand the behaviour and still find it unhelpful; that would be a rule change requiring a spec update and an engine-version bump.

**Found along the way:** the result screen lists hazard fires with internal names: `vent_b5→player`, and `gun→object 5` for a shield segment or egg. It is a roadmap item.

## The owner's first Tech win (2026-10-20)

The owner won with Tech for the first time on 2026-10-20, on the restyled client (engine 0.1.1, content 08a289f83c157f), in round 28 of the 30-round cap: "a slog of a battle", but an enjoyable one. The picks were the Tech preset's exactly, chosen by hand, so the fight is labelled custom; it has no tester ID, so the playtest report leaves it out. The replay is in the owner's `telemetry/` (`2026-10-20T20-21-03-588Z-custom-win`), not in git: it is tied to this content hash and cannot be regenerated like the bot's winning lines. `npm run sim -- replay` reproduces the win.

| Measure | This fight | Spec target |
| --- | --- | --- |
| Length | 28 rounds | 8 to 12; alarm over 16 |
| Median turn | 20.7 s over 28 turns, 10 minutes of turns in all | Under 60 s |
| Damage to the player (enemies, hazards, own) | 6, 1, 0 | |
| Hazard fires | 11: the gun hit shield segments five times and a Guard once; the panels and vent G6 hit drones and turrets four times; vent B5 hit the player and a drone once | At least 2 |
| Peak minions (lost) | 5, the cap (17) | |
| Abilities used | Barrier Shield 19, Sidearm 13, Auto-Turret 12, Scout Drone 8, Proximity Mine 8, Grapple Hook 1, Reload 1 | |
| Warden | Reached the player in round 10; killed in round 15 | |

**The fight came in three phases:**

1. **Rounds 1 to 14, a standoff.** Only the Flamer died, to a drone's zap in round 5. Shields absorbed 5 to 9 damage a round while the enemies lost 0 to 7 HP a round. All three waves had arrived by round 7, as usual.
2. **Rounds 15 to 17, the breakthrough.** The Sidearm finished the Warden in round 15. In round 17 one Guard walked into the two mines laid on C5 and D5 the round before and set off both, killing four Guards: the biggest swing of the fight, and an unplanned combo of the kind the spec's question 5 asks about.
3. **Rounds 18 to 28, the tail.** The last four enemies took 11 rounds. The last Guard stood on D3 from round 18 and fired every round into the shields and turrets in front of it. The same shields stopped three of the player's turret shots (rounds 23, 26 and 27). Turrets finished it in round 28, six rounds after the previous kill.

**What it says:**

- **Tech can win by holding, not trading.** The win came from shields and turrets: 19 Barrier Shields and 12 turrets, against 1 to 3 turrets on the round-1 Tech screens. The bot plays Tech the same way (10.5 turrets a fight).
- **Length is the alarm.** 28 rounds is well past the 16-round alarm for winning fights and two rounds from the cap, where an undecided fight is a loss. The standoff and the tail account for the extra rounds, not late waves.
- **Friendly fire stayed on the player's side.** 13 HP of the player's own damage hit their minions and shields (turret shots into a drone and into shields, a drone zap into a turret, the round-17 mines into a shield, a Sidearm shot into a drone). None hit the player.
- **Hazards keep firing in a long fight.** 11 fires, against round 1's average of 0.62; ten of them hit the player's side.
- **One fight by one experienced player.** It is evidence for the roadmap's Tech item, not a finding. The spec tunes on playtest evidence, and nothing recorded since shows whether first-time players see the same.

## What the outside testers said (2026-11-08)

Other people played further sessions, including people who had never seen the game. On 2026-11-08 the owner reported their feedback, marked the playtests successful and moved the project on to the [full-game phase](phase-2.md).

**What the testers said:**

- The game seems fun, but they want to see more before saying whether they would play a full game.
- They did not understand the eventual roguelite loop.
- The tech trees do not read as separate, and the upgrades are confusing.

**The owner's reading:** these problems should go away when a build is earned by progressing through the game, rather than picked whole at the start. The weapons and abilities need some rework, and the Occult path has to be added.

**What was not measured.** The owner has not received the testers' recordings, and the `telemetry/` folder in the owner's checkout holds only the owner's own fights and one agent test. So there are no figures for these sessions: none of the spec's first-time or informed metrics, no answers to the three questions asked after each attempt, and no record of how many people played or how many attempts they made. The decision rests on the testers' verbal feedback. The measured human evidence is still only the developer round and the owner's Tech win above.

If the recordings arrive, `npm run sim -- telemetry` can read any replay made on the current engine and content, and the results belong in this section.
