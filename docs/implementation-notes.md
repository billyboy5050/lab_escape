# Implementation notes

What the implementation had to decide where the [spec](spec.md) (revision 2) is silent or ambiguous, the defaults it took for the spec's open decisions, and what the simulator found. Each interpretation is a call to confirm. If one is wrong, it is a small code change; numbers are already data in `content/`.

The spec says to update it when implementation forces a rule change. Nothing here contradicts the spec, so it is untouched; the items under Interpretations are candidates to fold into it as clarifications.

## Open decisions: defaults taken

| Open decision | Implemented as | Where to change it |
| --- | --- | --- |
| Engine, language, platform | TypeScript rules core (runs headless in Node), React web client built with Vite | n/a |
| Input scheme | Mouse first, a keyboard shortcut for every control (see [Playing](../README.md#playing) in the README); touch is tap to preview, tap again to commit | `src/client/screens/FightScreen.tsx` |
| Art direction | Flat shapes; units are told apart by shape and letter as well as colour (circles and squares are yours, diamonds and the hexagon are enemies); Okabe-Ito palette; light and dark themes | `src/client/components/Board.tsx`, `styles.css` |
| Empty room brings the next wave early | Off (fixed schedule) | `rules.earlyWaveWhenEmpty` |
| Show hazard cooldowns | Hidden | `rules.ui.showHazardCooldowns` |
| Player-side units leave corpses | No | Code (`placeCorpse` is enemy-only) |
| Creep | Not built (stretch) | n/a |
| Levels of undo | Unlimited, back to the last commit (AP spent or something set off) | n/a |
| Spawn placement in crowded rooms | Nearest free tile by range distance, ties lowest row then column; every unit of a wave or egg is placed before any mine under one goes off | `World.findSpawnTile` |
| A primed hazard commits | Yes | n/a |
| Player-side objects draw the gun | Yes (a shield segment or egg first in the lane ranks as a minion) | n/a |
| Arrival round | Every wave | `rules.arrivalRound`: `all`, `first`, `none` |
| Loadout size | 8 of 11 | `rules.loadout.abilities` (6 for the informed round) |
| Corrode duration | Rest of the fight | `rules.statuses.corrode.duration` (0 = permanent) |
| Stretch items | None built | n/a |

## Interpretations to confirm

### Grid and movement

- **Flying units** pass over units only. Shield segments, eggs, walls, pillars, the shutter and the wall gun block them like everyone else. The spec only says "pass over other units".
- **Path ties.** Breadth-first search expands north, east, south, west in that order, so equal-length paths always resolve the same way. Enemies ignore mines and walk that path.
- **"Nearest" for ground melee units** (hatchlings, bursters, the Warden, and the Flamer, which is treated as melee) is the path length to a tile adjacent to the target. It is computed ignoring units, which move, but respecting terrain, shield segments and eggs. If nothing is reachable it falls back to range distance.
- **"Move toward" or "as close as possible"** picks the reachable tile with the smallest key: path distance to a tile adjacent to the target, then range distance, then steps, then row, then column.
- **Mines don't interrupt movement.** A unit that sets one off keeps walking if it survives. This applies to the Warden's charge and to pulls too.
- **Spawning counts as entering.** A ground unit that appears on a mine sets it off: wave units, hatchlings and debug spawns. This rewards learning the spawn tiles, but your own mine next to your egg can kill both hatchlings. The egg preview shows where they will land.
- **"Empty tile"** for placement means passable terrain with no unit and no object (mine, egg, shield or corpse). Brood Egg also accepts a tile whose only object is a corpse that can host.
- **Default player path.** The client's default path for a free move avoids stepping on mines when the movement budget allows. Commands carry explicit paths.

### Abilities

- **Projectile targets.** Projectile abilities can target any unit in range and sight, friendly ones included, and the shot hits the first unit or blocking object on the line. A target is not offered if the shot would stop in the shutter or the wall gun.
- **Grapple Hook:**
  - Pull self accepts any tile in range and sight, including a wall or pillar to pull toward. The player stops on the last free tile before a blocker.
  - Pull unit is offered only when the first thing on the line is a pullable unit with at least one tile of room.
  - The hook is not reflected by Reflective Shield and does not damage objects.
- **Lunge** cannot leap over eggs, the shutter or the wall gun, the same as shields and pillars. Landing on a mine sets it off. The strike on an adjacent unit is optional.
- **Spore Pod** can land on any tile within 5 (it is lobbed, so sight is not needed). It is offered only where its 3x3 area holds at least one unit.
- **Barrier Shield** "adjacent" includes diagonal pairs. A placement counts toward the cap of 2 while either of its segments survives.
- **No per-type limits** on turrets or drones; only the minion cap (5) limits them.
- **Pick Up** removes the mine with no refund; "returns to hand" is read as "can be placed again for 1 AP".
- **Redeploy** names the docked drone by ID and restores its charges to the upgrade-adjusted maximum.
- **Utilities** (Sprint always; Reload, Pick Up and Redeploy with their ability) do not count toward the 8 abilities.

### Statuses and timing

- **One tick per status.** Every status present at the start of the tick step ticks once.
- **Death effects wait for the spread.** Effects set off by deaths during the tick step (Spore Burst, Parasite Jump, a Parasite hatchling) run after poison spreads, so anything they apply first ticks next round. The spec says this for spread and Parasite Jump; it is applied to Spore Burst too.
- **The dead keep their statuses.** A unit killed by a tick keeps its statuses, so a Poison that would have expired on that tick still triggers Spore Burst.
- **Pinned and Slowed** count down at the status step, so they cover exactly the unit's next phase.
- **The Warden's charge.** Pinned stops it. Slowed shortens it by 1 tile.
- **Docked drones** are off the board, so their statuses do not tick until they redeploy.
- **Purge** happens before the Warden's action and is skipped in its arrival round. Its 2-round cooldown ticks at step 5, so it is available every other round.

### Deaths and the chain rule

- **Death hook order:** upgrade hooks in table order (Spore Burst, Acid Brood, Parasite Jump), then the base Parasite hatchling. A host's Spore Burst therefore does not poison the hatchling that bursts out of it.
- **Effect instance keys** for the once-per-chain rule:
  - the dead unit, for its death hooks;
  - the mine, for its blast;
  - the shield segment, for a reflection;
  - the player, for Grapple Pin.
- **Simultaneous win and loss.** The player's death ends the fight at once, even mid-chain, and a loss beats a simultaneous win. Killing every enemy before the last wave has spawned is not a win.
- **A win waits for the action to finish.** The win is checked once per root action, after the action and the chain's queued effects have both completed. If a mine or another immediate effect kills the last enemy partway through an action, the rest of the action still happens: a multi-tile move crosses its second mine, and the status tick finishes the rest of its units. The spec does not say either way. The player's death is the exception and ends the fight on the spot, so a loss in the same action beats the win. An earlier revision ended the fight at the first kill; it was changed after review because a committed path could skip a mine that would have killed the player and score the move as a win.
- **The last enemy's death effects.** A win is declared only after the chain's queued death effects have played, so the last enemy's Spore Burst, Acid Brood, Parasite Jump or Parasite hatchling still happens and is in the event log. If one of them adds an enemy, the fight goes on; if one kills the player, the fight is lost.
- **Corpses.** A new corpse replaces an older one on the same tile.
- **The loop test.** The rules as written cannot loop: Spore Burst and Acid Brood never deal damage directly. The test for the M6 criterion "the once-per-chain rule suppresses a loop built from Spore Burst and Acid Brood" therefore adds a test-only hook that re-raises the same deaths, and checks that the repeats are suppressed. A second test checks the depth cap of 20.

### Enemies

- **Guard:**
  - A tile gives a shot only if the first thing on the line is on the player's side: a player-side unit, a shield segment or an egg. An enemy ally, the shutter or the wall gun first on the line spoils it.
  - Step 2's "closest to the target" uses the unit the Guard would shoot from that tile.
  - After moving, it shoots the nearest valid target from where it stands.
- **Medic:**
  - "Ally" excludes itself.
  - It prefers tiles at least 4 from the nearest player-side unit, taking the one with the fewest steps; with none available, it takes the farthest tile.
  - With nobody hurt, it follows the nearest Guard (any ally if no Guard is left).
  - After moving, it heals the lowest-HP damaged ally in range and sight.
- **Flamer:**
  - Walls, pillars, the shutter and the wall gun end the sweep line. Shield segments and eggs on the line are hit but do not stop it.
  - Direction ties resolve north, east, south, west.
  - It scores the directions again from the tile it ends on before sweeping.
- **Warden:**
  - A charge lane is blocked by terrain or an enemy ally. A body or object directly in front means no charge in that direction, since the charge must move at least one tile.
  - Among several open lanes, it chooses by the unit that opened the lane: nearest, then lowest HP, then lowest ID.
  - Step 4 considers only tiles strictly closer to the player (path distance to an adjacent tile). Among those, it prefers tiles that line up a clear lane at the player for next round, and it does not attack after an approach move.
  - Step 5: when no tile is closer, it melees an adjacent player-side unit (lowest HP, then lowest ID).
  - In its arrival round it makes only the approach move: no Purge, charge or melee.

### Hazards

- **Wall gun.** The gun's shot is a projectile from H5 travelling west. Reflective Shield sends it back into the gun, which absorbs it.
- **Panel grid and vents** cover the 3x3 around each of their tiles and damage units only.
- **Order and checks.** Hazards prime and fire in the order they are listed in `map.json` (gun, panel grid, vent B5, vent G6). The firing-time checks use the same targeting as priming.

### Intercom

- **Timing within an action.** Lines raised during one action wait until it finishes, then the first in table order plays. The rest queue to later phases, so "at most one line per phase" holds. The player's death line plays at once and clears the queue.
- **Low-HP lines** ("Warden, report.", "The intruder is wounded.") fire when a unit drops below the threshold and survives the hit.

### Previews, intents and telemetry

- **Intents** are the minion and enemy phases dry-run on a copy of the state, so each unit's intent already accounts for the units that act before it. The environment phase is not run, so hazards stay hidden.
- **Hover previews** also show the intents as they would be after the hovered action.
- **Summaries count HP actually lost** (no overkill). Events carry both the hit size (`amount`) and HP lost (`hpLost`).
- **Turn times** measure the player's own time. A turn's clock starts when the client has finished animating and the player can act (the opening, or the enemy and environment phases after End Turn), and stops at End Turn or at the action that ends the fight, so the last turn of a fight is counted. Without the client's `turnReady()` call (the simulator, tests) a turn is timed from the end of the previous one.
- **The dev server's telemetry endpoint** writes only under `telemetry/`. The folder and file names come from the request, so each is cleaned and checked to stay inside that folder, and a request that cannot is refused with a 400.
- **Content is checked before it is used.** A hot reload is accepted only if `validateContent` passes, and anything the game reads without a fallback must be present: a missing collection (`map.hazards`, `presets`, `intercom.lines`, `hazards`, ...) or block (`rules.ui`, `rules.eggs.corpseHosts`, an upgrade's `requires`) is a reported problem and not an empty list, because accepting it crashes the next render or damage roll. Fields read with `!` are listed in `src/content/required.ts`.
- **Saved preferences and replay files are untrusted.** A stored loadout, preset or attempt list that does not have the expected shape is ignored and the defaults are used, so a stale or corrupt value cannot stop the app starting. A replay is checked for its header, the shape of the whole record and the field types of every command before it plays (a debug AP of "oops" would otherwise become NaN), and a `?replay=` file is judged against the content as it is when the response arrives, not when the request went out.
- **Pick Up Mine** is counted in `abilitiesUsed` as `pick_up_mine`, like Sprint, Reload and Redeploy.
- **Action shortcuts** run 1 to 9, 0, then `-` and `=`, because a full loadout (8 abilities) plus the four utilities is 12 actions. With Ctrl, Cmd or Alt held, the action keys are left to the browser; the other shortcuts do not check modifiers yet (see the roadmap). `E` always ends the turn; `Enter` does too unless a button or link has focus, when Enter operates that control instead (a focused button is focused after a mouse click as well, so Enter after clicking a button presses it again).
- **Replays stop at a rejected command.** A replay command the engine refuses (a corrupted or edited file) pauses playback there and says which command it was, instead of playing the rest against a state it no longer matches.
- **Incoming-damage badges** in the intent forecast sit on the unit's current tile, not on the tile where the forecast says it is hit. The badge belongs to the unit the player can see, and a minion's destination is already drawn as its dashed path; the hit tile is used only for things not on the board yet.

- **The playtest report reads replays, not `summary.json`.** `npm run sim -- telemetry` replays every `replay.json`, because the summary does not record the minion count per turn and a replay is checked against the engine version and content hash. A fight with debug commands, or with wave 3 held back (a different win condition), is not counted as an attempt. The preset of a fight comes from its loadout (a match with a preset's abilities and upgrades), not from the replay's `meta.preset`. Telemetry does not record the playtest round either, so `--exclude-tester` leaves out testers such as the developer. A fight with no tester ID is not counted, unless `--include-unlabelled` is given: ordinary play is saved in the same folder.
- **"Full swarm" means the minion cap.** A turn counts toward the median turn time when the player had the maximum number of minions (`caps.minions`, 5) alive when they ended it. The turn that ends the fight counts like any other. Confirm this reading; the spec does not define a full swarm.
- **Informed attempts are chosen by attempt number.** Telemetry does not record which playtest round a fight belonged to, and a tester's attempt numbers carry on from round 2 into round 3, so the report counts a tester's fights on the informed preset from `--informed-from` (default attempt 2) for `--informed-attempts` attempts (default 3, the spec's number) as informed. If a tester plays more than one first-time attempt, set `--informed-from` to the number of their first informed attempt.
- **Playtest metric details.** The first attempt of each tester, and each informed attempt, is their earliest fight with that number, since attempt numbers are counted per browser. "First-time losses" are all lost attempts before `--informed-from`, one per tester and attempt number. Figures print with one decimal, so a value never reads as the other side of its alarm. The friendly-fire mean leaves out fights where the player took no damage, as the simulator's report does. "Share of deaths from one source" groups by category (player, minion, enemy, hazard), like the simulator's report, and a loss with no killer is the round cap. The quit-after-one-loss alarm cannot be told from a tester who has not played again yet, so the report states how many testers have no fight after their first loss and does not set the status from it.

### Simulator

- **Greedy bot** looks one action ahead. It plays out the round's minion and enemy phases after each candidate and scores what is left.
  - **Pruning:** shield pairs only within 2 tiles, grapple self-pulls deduplicated by landing tile, Spore Pods only on enemies.
  - **Sprint** is scored by its best follow-up move.
  - **Cues:** by default it treats a hazard showing its cue as a threat to the tiles that hazard covers, as a player who has learned the room would. `--bot greedy-naive` turns that off.
- **Scripted bots.** The spec's scripted bots reproduce the hand simulations, but the design document has only their summaries, not move-by-move lines. The golden replays are therefore greedy-bot fights frozen into command scripts (`golden/`); replace them with transcribed hand lines when those exist.
- **Search bot** (stretch): not built.
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
