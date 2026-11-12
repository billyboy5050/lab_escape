# Roadmap

Where the project stands, what comes next, and what is waiting on a decision. Work in progress is not tracked here: it lives in open pull requests and in [`docs/work/`](work/), one note per branch.

Keep this file current. A pull request that finishes an item deletes it here, and one that turns up new work adds it; git history keeps the record of what was done. Order within "Up next" is priority order.

Last reviewed: 2026-11-12.

## Where things stand

The MVP in the [spec](spec.md) is finished. On 2026-11-08 the owner closed playtesting as successful and started the full-game phase, planned in [phase-2.md](phase-2.md). The code in this repository stays the TypeScript prototype until that phase's Stage A has moved it to Unity. The rules core, telemetry and simulator already exist in C# in `core/` (M9 and M10): they match the TypeScript, CI holds them to it, and the game does not use them yet. Since M11 the engine in both languages can fight in a room of any size, described as data; the game still has only the Armory. M12 added `unity/`, where a full Armory fight plays in greybox; the web game stays the playable version until M13. On 2026-11-10 that fight's animation was reworked for weight, at the owner's request: see [how a frame is animated](implementation-notes.md#how-a-frame-is-animated). On 2026-11-12 its presentation was brought toward the web client's, and the web client's sound was ported to it early: see [the fight's presentation](implementation-notes.md#the-fights-presentation). The same day the owner played it and passed the M12 checkpoint: the Unity fight feels at least as good as the web version, and is "a much more enjoyable experience".

| Milestone | Status |
| --- | --- |
| M0 Foundations | Done: grid, symmetric LOS (with the spec's 88-pair check), content loader with validation and hash, phase loop, movement, command log and replay |
| M1 Combat core | Done: damage types, armor, deaths and corpses, Sidearm, Guard AI, event queue and once-per-chain rule, win and loss, result screen, previews (verified against 100 random states) |
| M2 Tech kit | Done: mines, turret and Reload, drone cycle, shields, grapple, minion phase and cap, intents |
| M3 Enemies and waves | Done: Medic, Flamer, Warden, waves with the arrival round, hidden timer, round cap, intercom feed and log |
| M4 Lab hazards | Done: gun, panel grid, vents, prime-then-fire, the value rule with the combined kill check, cues, debug explanations |
| M5 Alien kit | Done: Acid Spit, Lunge, Spore Pod, Brood Egg on floor and corpses, basic, spitter and burster hatchlings, Parasite, poison and spread |
| M6 Loadout and upgrades | Done: loadout screen, presets, all 12 upgrades |
| M7 Instrumentation and balance | Done by owner decision on 2026-11-08: telemetry, replay, simulator, bots, golden replays, the rules sheet and the developer round (spec round 1), then outside testers judged successful on their verbal feedback. Their recordings have not been received, so the first-time and informed metrics in M7's acceptance criteria were not recorded: see [what the outside testers said](implementation-notes.md#what-the-outside-testers-said-2026-11-08) |
| Stretch | Not built: scrap and Repair, mender hatchling, Creep, search bot, scalable text and remappable keys |

The simulator's findings (greedy bot, 1,000 seeds per preset) are in [implementation-notes.md](implementation-notes.md#what-the-simulator-says-greedy-bot-1000-seeds-per-preset). In short: Tech wins 72%, Alien 46%, Hybrid 83%; the paper check passes; Hybrid's friendly-fire share (66%) and the sweep's (54%) are over the spec's 35% alarm; the Warden reaches the player in only 3% to 28% of fights that reach round 7; Alien sees 1.9 hazard fires per fight against a target of 2.

On 2026-10-20 the client was restyled toward the final look (an industrial sci-fi lab, with combat effects, sound, a title screen and a briefing; spec, Changes since revision 2) because playtesters found the game promising but did not engage with its placeholder presentation. Fights recorded before then, round 1 included, were played on the placeholder look, so a difference between them and later fights may come from the presentation as well as the players.

Since 2026-10-22 the web board can also be drawn in an isometric view, behind a toggle; top-down stays its default. The replay's `meta.view` records which view each fight used. The Unity client will use only a fixed isometric camera.

The developer round (spec round 1) and what it found are in [implementation-notes.md](implementation-notes.md#what-the-developer-playtest-round-says-spec-round-1).

## Up next

The scope and acceptance criteria of every milestone are in [phase-2.md](phase-2.md#milestones). No rule or content value changes until M13, so the port can be compared exactly against the TypeScript engine.

1. **M13: Unity parity, retire the web client.** It needs the Unity version settled and Windows Build Support installed first (see [decisions waiting on the owner](#decisions-waiting-on-the-owner)).

Stages B to D follow: game systems, then one wing shown to testers, then the other two wings and the light meta.

## Inputs to the kit rework (M17)

These were watch items for the playtests. No recorded play has confirmed or ruled out any of them, so they stand as open questions for the kit rework and the first tester round (M22). Do not tune on bot figures alone.

- **Scout Drone:** kept as a fragile decoy with chain-damage utility for the playtests. Whether players understand it or find it useful is unmeasured; see the [owner decision and evidence](implementation-notes.md#what-the-developer-playtest-round-says-spec-round-1).
- **Tech's difficulty:** the owner found Tech hard to get a footing in and played it with few turrets, while the bot wins 72% of Tech fights with 10.5 turrets a fight on average. On 2026-10-20 the owner won with Tech for the first time, by holding with shields and a dozen turrets, but only in round 28 of 30, past the spec's 16-round alarm for winning fights; see [the owner's first Tech win](implementation-notes.md#the-owners-first-tech-win-2026-10-20).
- **Friendly fire:** round 1 tripped the spec's alarm for too little, while the bot trips the one for too much on Hybrid.
- **Hazard fires:** too few in round 1 (an alarm), largely because most fights ended early, and short of the target for the bot on Alien.
- **Early losses:** half of the developer round's losses came in the first three rounds of the fight.
- **The Warden** rarely reaching the player (bot 3% to 28%).

Round 1's evidence for each is in the [developer round](implementation-notes.md#what-the-developer-playtest-round-says-spec-round-1).

## Decisions waiting on the owner

Agents should not settle these on their own. Ask, and record the answer in the spec or the notes.

- **Unity version.** The installed editor, 6000.6.8f1, is an update release, while the plan asks for the current LTS, which is 6.3. Nothing built so far depends on the difference, and moving up to a later LTS is simpler than moving back. Needed before M13, which also needs the Windows Build Support module added.
- **How to source 3D art,** and where it will be stored: bought packs, commission or generated. Needed before M14, so the art can be made while Stage B is built in greybox; the first tester round (M22) waits on about half of it. This repository is public, and paid asset-store packs usually cannot be redistributed, so bought art needs a private home or a private repository.
- **Creep.** Whether Alien gets it, so that all three branches have six abilities. Needed by M17.
- **Unity tests in CI.** CI runs nothing in `unity/`: its EditMode and PlayMode tests and its builds run only on a developer's machine, and once M13 retires the web client that is the whole game client. Running them on GitHub needs a Unity licence stored as a repository secret. Needed by M13. Whatever the answer, client logic with no Unity code (`unity/Assets/Scripts/Logic`) could also be compiled and tested by `dotnet test`, as `core/` is, which needs no licence.
- **The design document is outside the repository.** The spec and the phase plan cite Design Doc v0.1 for the design intent, the enemy rosters and what is deferred, and it is a private document in the owner's Claude account. Codex, a cloud agent or a new contributor cannot read it. Either export it to `docs/`, or copy the parts M17 to M24 rely on into the plan. Needed by M17.
- **Merged branches on GitHub.** The repository's "Automatically delete head branches" setting is off, and on 2026-11-09 the remote still held 11 branches that were already merged into `main`. CLAUDE.md asks for a branch to be deleted after its merge, which an agent cannot do when the owner merges later. Turning the setting on, and deleting the 11, are the owner's call.
- **Stretch items.** Build one only when a playtest finding calls for it (spec, Build order). None had been called for by 2026-10-13.

## Smaller items

Items about the web client stop applying when M13 retires it. Anything that should survive moves into a Unity milestone at that point.

- **Move the Armory into `content/rooms.json`.** Once the freeze ends at M13, M14 can add the file, put the Armory in it with the new rooms, and delete `map.json`, `waves.json`, `rules.roundCap` and `Content.map` and `Content.waves`. The tile types in `map.json` need a home of their own at that point. Until then the Armory is built from its old files so that its content hash does not move.
- **Git hooks after M13.** `npm install` turns the hooks on, through `prepare` in `package.json`. When M13 deletes the TypeScript, the hooks need another way in, CLAUDE.md's "Start here" needs the new step, CI drops its Node steps, and `tests/rulesSheet.test.ts` needs a C# counterpart or the rules sheet retires with the web client.
- **A reduced-motion setting in Unity.** The web client shortens its effects under the system's reduced-motion setting. The Unity fight shakes and kicks the camera, stops the clock and flashes the board's edges, and has nothing to turn them down. M13 should add the setting; `Feel.cs` holds the numbers it would scale.
- **Symbols and faces missing in the web build.** The Unity client's text uses `→`, `–`, `—`, `…`, `−`, `▼`, `✕`, curly quotes and `⚠` (in `FightDirector.cs`, `ActionModel.cs`, `EventFormatter.cs`, `Hud.cs` and `BoardTags.cs`). In the web build of 2026-11-09 the arrow and the en dash drew as blanks, and the others are likely to: the web player has no system font to fall back on. For the same reason the web build keeps Unity's default face, where the Mac and Windows builds take the web client's condensed and monospace faces from the system (`Fonts.cs`). M13 should ship a font that has the symbols, ideally the web client's faces too, or write the symbols in plain ASCII, and the friendly-fire `⚠` needs a mark that survives either way. A shipped font would be the project's first binary asset (see Git LFS below).
- **Web build size.** 11.7 MB in greybox ([the first web build](implementation-notes.md#the-unity-client)). `unity/Packages/manifest.json` still lists every built-in module, and the web player starts PhysX, which nothing uses. Removing unused modules is the first thing to try when M13 or M20 sets the size budget.
- **Git LFS.** Nothing in `unity/` is binary yet. When the first binary asset arrives (a font for the web build at M13, or art at M20; sound is made in code), add a `.gitattributes` for it and call `git lfs pre-push` from `.githooks/pre-push`: with `core.hooksPath` set, `git lfs install` does not put its hook where git will run it.
- **Intercom lines per room.** `content/intercom.json` is written for the Armory and fires in every room, so a room of another size hears about the armory floor. This changes nothing headless. Lines need to belong to a room or a wing by M21.
- **Report targets per room.** The simulator's targets and alarms, such as rounds to win and the Warden reaching the player by round 7, were set for the Armory. They still print for other rooms but mean little there. M16 should replace them.

- **Loadout size in the sweep.** At 8 of 11 any two builds share at least 5 abilities, which weakens the test of whether loadouts play differently. The owner kept 8 for every playtest round (2026-10-13), so 6 of 11 is tried in the simulator instead: run `npm run sim -- sweep --size 6 --seeds 1000 --out <scratch folder>`, compare the win-rate lift and the spread of winning loadouts with the committed 8-ability sweep (`reports/sweep-8-greedy.txt`), and add the findings to the implementation notes.
- **Ability rules during a fight.** The action bar's tooltip shows an ability's name, or why it cannot be used, but never its rule text, so in a fight the full rule is only on the loadout screen and the rules sheet carries a short version. Add each ability's `text` to its action button's tooltip.
- **Simulator worker threads.** In a fresh container (Node 22.22.0, tsx 4.23.15), `npm run sim` with more than one worker fails with "Cannot find module .../src/sim/batch" from `src/sim/worker.ts`, though the parent passes its tsx flags to the workers. `--workers 1` works, and no test runs the real worker pool. The C# simulator does not have this problem, so fix it only if it gets in the way before M13 retires the TypeScript simulator.
- **Readable hazard fires on the result screen.** The fight summary names a hazard by its map ID and an object by its number (`vent_b5→player`, `gun→object 5`), and the result screen prints them as they are. Name them as the board does ("Gas vent B5", "shield segment", "egg") in `src/telemetry/summary.ts` or where `src/client/screens/ResultScreen.tsx` prints them.
- **Tile-by-tile movement animation.** Multi-tile moves animate as one straight slide between the start and end tiles.
- **Modifier keys on game shortcuts.** Only the action keys (`1` to `=`) and V (board view) ignore Ctrl, Cmd and Alt. The others fire with them held: Ctrl or Cmd+Z undoes a move, Ctrl or Cmd+E ends the turn, and Ctrl or Cmd+F and +L change the speed and toggle the log while the browser opens find or the address bar. Check for modifiers before every game shortcut in `src/client/screens/FightScreen.tsx` (and the backtick in `src/client/App.tsx`), with a tested helper in `src/client/actionKeys.ts`.
