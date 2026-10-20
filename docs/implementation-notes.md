# Implementation notes

How the tooling and the client read the [spec](spec.md) where it is silent, the known limitations, what the simulator found, and what the playtests found.

The rules themselves are in the spec. On 2026-10-13 the owner confirmed every reading the implementation had taken and a decision for every item the spec left open. The readings about the rules and what the player sees moved into the spec, as the Clarifications at the end of each section, and the decisions are under Decisions taken in its "Risks and decisions" section. The readings below are about the tooling and were confirmed at the same time.

## How the tooling reads the spec

### Client, telemetry and replays

- **The dev server's telemetry endpoint** writes only under `telemetry/`. The folder and file names come from the request, so each is cleaned and checked to stay inside that folder, and a request that cannot is refused with a 400.
- **Content is checked before it is used.** A hot reload is accepted only if `validateContent` passes, and anything the game reads without a fallback must be present: a missing collection (`map.hazards`, `presets`, `intercom.lines`, `hazards`, ...) or block (`rules.ui`, `rules.eggs.corpseHosts`, an upgrade's `requires`) is a reported problem and not an empty list, because accepting it crashes the next render or damage roll. Fields read with `!` are listed in `src/content/required.ts`.
- **Saved preferences and replay files are untrusted.** A stored loadout, preset, attempt list or mute setting that does not have the expected shape is ignored and the defaults are used, so a stale or corrupt value cannot stop the app starting. A replay is checked for its header, the shape of the whole record and the field types of every command before it plays (a debug AP of "oops" would otherwise become NaN), and a `?replay=` file is judged against the content as it is when the response arrives, not when the request went out.
- **Effects and sounds are decoration.** The board's combat effects, its shake and alarm flashes, and the sound effects are drawn from each animation frame's events (`src/client/art/fx.tsx`, `src/client/sfx.ts`) and never read a rule. A primed hazard makes no sound and gets no effect beyond its cue, so the spec's "only its cue" stays visual. At Skip speed only the last frame of a sequence shows its effects and plays its sounds.
- **The title and briefing come before the fight exists.** The fight session, and with it the turn clock, starts when the player leaves the briefing, so turn times are measured as before. Retry skips the briefing. The briefing states what the rules sheet states (the goal, the opening intercom line, the kit) and nothing the game hides.
- **Replays stop at a rejected command.** A replay command the engine refuses (a corrupted or edited file) pauses playback there and says which command it was, instead of playing the rest against a state it no longer matches.
- **Action shortcuts** run 1 to 9, 0, then `-` and `=`, because a full loadout (8 abilities) plus the four utilities is 12 actions. With Ctrl, Cmd or Alt held, the action keys are left to the browser; the other shortcuts do not check modifiers yet (see the roadmap). `E` always ends the turn; `Enter` does too unless a button or link has focus, when Enter operates that control instead (a focused button is focused after a mouse click as well, so Enter after clicking a button presses it again).

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
- **One fight by one experienced player.** It is evidence for the roadmap's Tech item, not a finding. The spec tunes on playtest evidence, and round 2 shows whether first-time players see the same.
