# Roadmap

Where the project stands, what comes next, and what is waiting on a decision. Work in progress is not tracked here: it lives in open pull requests and in [`docs/work/`](work/), one note per branch.

Keep this file current. A pull request that finishes an item deletes it here, and one that turns up new work adds it; git history keeps the record of what was done. Order within "Up next" is priority order.

Last reviewed: 2026-11-14.

## Where things stand

The MVP in the [spec](spec.md) is finished. On 2026-11-08 the owner closed playtesting as successful and started the full-game phase, planned in [phase-2.md](phase-2.md). Its Stage A, the move to Unity, is done: the rules core, telemetry and simulator were ported to C# in `core/` (M9 and M10) and proven against the TypeScript prototype. Since M11 the engine can fight in a room of any size, described as data; the game still has only the Armory. M12 added `unity/`, where a full Armory fight plays in greybox. On 2026-11-10 that fight's animation was reworked for weight, at the owner's request: see [how a frame is animated](implementation-notes.md#how-a-frame-is-animated). On 2026-11-12 its presentation was brought toward the web client's, and the web client's sound was ported to it early: see [the fight's presentation](implementation-notes.md#the-fights-presentation). The same day the owner played it and passed the M12 checkpoint: the Unity fight feels at least as good as the web version, and is "a much more enjoyable experience". M13 started the same day: the project moved to Unity 6.3 LTS, and the Unity client now saves each fight's telemetry, plays saved fights back with pause and step, has the debug overlay with step mode and its edits, reloads `content/` while it runs, has the title, loadout, briefing and report screens, and (2026-11-13) a reduce-motion setting and the web client's keys. On 2026-11-14 the Unity web build gained the fonts it lacked and lost PhysX, which nothing used: see [the web build's type and size](implementation-notes.md#the-unity-client). The same day the owner ran the Windows build and found it works, which finished M13's three builds, and the TypeScript prototype was retired with the owner's approval: its web client, engine, simulator and tests. The C# in `core/` is now the only engine and simulator, the Unity client the only client, and the freeze on rules and content during the port is over.

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

From 2026-10-22 the web board could also be drawn in an isometric view, behind a toggle, with top-down as its default; the replay's `meta.view` records which view each of its fights used. The Unity client has only a fixed isometric camera.

The developer round (spec round 1) and what it found are in [implementation-notes.md](implementation-notes.md#what-the-developer-playtest-round-says-spec-round-1).

## Up next

The scope and acceptance criteria of every milestone are in [phase-2.md](phase-2.md#milestones). Stage B, game systems, comes next.

1. **M14: Room shapes: halls, hallways, big hazards.** One room of each shape plays and stays readable, and the `zone` and `beam` hazard shapes have tests. It starts by moving the Armory into `content/rooms.json` (Smaller items). The owner's decision on how to source 3D art is due before M14 starts, so the art can be made while Stage B is built in greybox (Decisions waiting on the owner).
2. **M15 to M19** follow in the phase plan's order: the run skeleton with events, the run simulator, the kit rework, then Occult.

Unity's tests stay off CI, by the owner's decision, so they are the client's only tests and run on a developer's machine before every merge that touches `unity/`.

Stages C and D follow: one wing shown to testers, then the other two wings and the light meta.

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

- **How to source 3D art,** and where it will be stored: bought packs, commission or generated. Needed before M14, so the art can be made while Stage B is built in greybox; the first tester round (M22) waits on about half of it. This repository is public, and paid asset-store packs usually cannot be redistributed, so bought art needs a private home or a private repository.
- **Creep.** Whether Alien gets it, so that all three branches have six abilities. Needed by M17.
- **The design document is outside the repository.** The spec and the phase plan cite Design Doc v0.1 for the design intent, the enemy rosters and what is deferred, and it is a private document in the owner's Claude account. Codex, a cloud agent or a new contributor cannot read it. Either export it to `docs/`, or copy the parts M17 to M24 rely on into the plan. Needed by M17.
- **Merged branches on GitHub.** The repository's "Automatically delete head branches" setting is off, and on 2026-11-09 the remote still held 11 branches that were already merged into `main`. CLAUDE.md asks for a branch to be deleted after its merge, which an agent cannot do when the owner merges later. Turning the setting on, and deleting the 11, are the owner's call.
- **Stretch items.** Build one only when a playtest finding calls for it (spec, Build order). None had been called for by 2026-10-13.

## Smaller items

- **Move the Armory into `content/rooms.json`.** The freeze is over, so M14 can add the file, put the Armory in it with the new rooms, and delete `map.json`, `waves.json` and `rules.roundCap`, with the code in `core/Core/Content` that builds the Armory's room from them. The tile types in `map.json` need a home of their own at that point. Until then the Armory is built from its old files so that its content hash does not move.
- **Read the system's reduce-motion setting in Unity.** The Unity client has a setting of its own, off until it is turned on, where the web client followed the system's. Reading the system's would start it on for people who have it on: the web build through `matchMedia('(prefers-reduced-motion: reduce)')` in `LabDownload.jslib`, a Mac build through `NSWorkspace.accessibilityDisplayShouldReduceMotion` and a Windows build through `SystemParametersInfo`. The web call can only be tried in a browser.
- **Intercom lines per room.** `content/intercom.json` is written for the Armory and fires in every room, so a room of another size hears about the armory floor. This changes nothing headless. Lines need to belong to a room or a wing by M21.
- **Report targets per room.** The simulator's targets and alarms, such as rounds to win and the Warden reaching the player by round 7, were set for the Armory. They still print for other rooms but mean little there. M16 should replace them.

- **Loadout size in the sweep.** At 8 of 11 any two builds share at least 5 abilities, which weakens the test of whether loadouts play differently. The owner kept 8 for every playtest round (2026-10-13), so 6 of 11 is tried in the simulator instead: run `dotnet run --project core/Sim -c Release -- sweep --size 6 --seeds 1000 --out <scratch folder>`, compare the win-rate lift and the spread of winning loadouts with the committed 8-ability sweep (`reports/sweep-8-greedy.txt`), and add the findings to the implementation notes.
- **Unity client logic under `dotnet test`.** The owner decided on 2026-11-12 not to run Unity's tests in CI. `unity/Assets/Scripts/Logic` has no Unity code, though, so CI could compile and test it with `dotnet test`, as it does `core/`, with no licence. Ask the owner before adding it.
- **Readable hazard fires in the incident report.** The fight summary names a hazard by its map ID and an object by its number, and the report's Hazard fires fact prints them as they are (`R3 vent_b5->player`, `gun->object 5`). Name them as the board does ("Gas vent B5", "shield segment", "egg") where `ReportModel` writes that fact, leaving the summary's saved form alone.
- **A primed hazard's glow in Unity** is drawn from the frame's state (`FxSystem.DrawCues`), so it can light up a moment before the hazard's priming cue plays. The board's own hazards (`BoardView.Hazards`, which the debug overlay uses) move on at the cue; the glow should read them too, as CLAUDE.md asks of anything the board shows.
- **An unsupported selector in `Fight.uss`.** `.loadout-col:last-child` sets the loadout's last column's margin, but UI Toolkit has no `:last-child`, so the rule does nothing and every player logs "Unknown pseudo class". Give that column a class of its own, or drop the rule if the screen looks right without it.
