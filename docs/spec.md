# Lab Escape Roguelite: MVP Prototype Spec

Oct 2, 2026 · @Dave

Revision 2. This version applies the review of Oct 2, 2026. Every change is listed under Review changes at the end, tagged as a fix, a clarification, or a Proposed design call to confirm.

## Goals and scope

The MVP is one playable room, Armory Lockdown, built to answer whether a single fight in this game is fun before any depth is added. Everything in the design document so far came from hand-run simulations, so this prototype exists to replace opinion with play.

This spec is the implementation reference. The companion design document (Lab Escape Roguelite: Design Doc v0.1) holds the intent behind each system. Every number here is a starting value that lives in a tuning table, and items marked Open are decisions for the implementation session.

**Questions the prototype must answer**

1. Is a turn fun? Do 3 AP plus free movement give meaningful decisions each round?
2. Is autonomous minion chaos readable and satisfying, and does friendly fire feel fair?
3. Do the lab's hazards feel reactive and threatening without feeling invisible or cheap?
4. Does the snowball stay under control, and does the Warden feel like a real threat?
5. Do different loadouts play differently, and do surprising combos appear?
6. Is a fight of about 8 to 12 rounds the right length, and do previews and fast-forward keep turns quick?
7. Is losing common but not frustrating, and can a player tell why they lost?

**In scope**

- One 8x8 room (Armory Lockdown) with three hidden waves and a win or lose result.
- One character with a pre-fight loadout of up to 8 abilities and 3 upgrades, chosen from the Tech and Alien kits.
- Autonomous minions: turrets, drones, hatchlings, mines and shields.
- Four enemy types (Guard, Medic, Flamer, Warden) and three lab hazards (wall gun, floor panels, gas vents) driven by the lab's AI.
- An intercom text feed, previews for every player action, and debug tools.
- Telemetry, replay from a command log, and a headless simulation runner.

**Stretch (Proposed)**

These are specified below so nothing is lost, but none of them helps answer the seven questions. Build them after the first full playtest round, and only if a finding calls for one.

- Scrap and the Repair action.
- The mender hatchling.
- Creep.
- The search bot.
- Scalable text and remappable keys.

**Out of scope**

- The occult branch and debt, which is the first addition after the MVP.
- Cocoons, live capture and the Brood Egg's live-host tier.
- The armored hatchling. It needs the Warden's corpse and an egg that hatches a round later, by which time the fight is usually over.
- The run map, events, other rooms, wings, bosses beyond the Warden, and meta-progression (Alpha, Omega, unlocks).
- Part mods, Wrecked units and the wider scrap economy beyond Repair.
- Story, memory fragments, audio polish, final art, saving and a tutorial.

**Branch choice (Proposed)**

The MVP uses Tech plus Alien. Tech gives readable, set-up-then-trigger tools. Alien adds autonomous swarms and eggs, the most distinctive system, so together they test minion chaos, friendly fire and hazards in one room. Occult's debt rule needs several rooms to test properly, so it waits for phase 2.

**Success criteria (Proposed)**

| Measure | Target |
| --- | --- |
| First attempts lost by first-time players | About 70% of testers (4 of 5, up to 6 of 8) |
| First-time losses that reach wave 2 | Most of them |
| Informed attempts won on the Hybrid preset | 30% to 50% of pooled attempts, 3 per tester |
| Distinct loadouts that can win | At least 3 that share no more than one upgrade, each backed by a human or bot replay |
| Win-rate lift of any single ability or upgrade | Under 25 points in the simulated loadout sweep |
| Length of winning fights | 8 to 12 rounds |
| Median player turn, with a full swarm | Under 60 seconds |
| Hazard fires per fight | At least 2 |
| Testers who report an unplanned combo | Most of them |

With 5 to 8 testers, one person moves a rate by 12 to 20 points, so the human targets are counts and the win-rate questions lean on the simulator. Win-rate lift is the win rate of loadouts that include a pick minus the win rate of those that do not. It replaces share of wins, which cannot work here: choosing 8 of 11 puts every ability in 73% of random loadouts and in every win of its own preset.

## What gets built

The prototype is a loop of loadout, fight and result on a single room, with nothing carried between attempts. A player picks a build, fights Armory Lockdown once, sees why they won or lost, and tries again.

**Session flow**

1. Loadout screen: pick a preset or build your own from the Tech and Alien kits.
2. Briefing: the first intercom line plays and the room appears.
3. Fight: rounds repeat until the player wins or dies.
4. Result screen: outcome, rounds, key statistics, and buttons to retry the same loadout or change it.

**Screens**

| Screen | Purpose | Notes |
| --- | --- | --- |
| Loadout | Choose up to 8 of 11 abilities and up to 3 of 12 upgrades | Presets for quick starts; show each ability's rule text |
| Fight | The grid, HUD, intercom feed and previews | Fast-forward toggle and undo for free movement |
| Result | Outcome and statistics | Shows damage sources, including friendly fire, and the cause of death |
| Debug | Developer overlay, hidden by default | Toggled by a key; see UI and feedback |

**Content counts**

| Content | Count |
| --- | --- |
| Player abilities | 11 in the loadout pool (6 Tech, 5 Alien), plus Reload as a utility action. Repair and Creep are stretch |
| Upgrades | 12 (6 Tech, 4 Alien, 2 hybrid) |
| Loadout presets | 3 (Tech, Alien, Hybrid) |
| Enemy types | 4 (Guard, Medic, Flamer, Warden) |
| Hatchling variants | 3 (basic, spitter, burster), plus the mender as a stretch |
| Lab hazards | 3 types, 4 placed objects (gun, panel grid, two vents) |
| Waves | 3, on a hidden timer |
| Intercom lines | About 12 |

**Constraints**

- The fight is fully deterministic. Ties are broken by rule, not by chance, so a recorded command log replays exactly.
- Placeholder art is fine: flat colored shapes and icons. Readability matters more than polish.
- Engine and platform are Open, and the architecture section keeps the rules engine independent of both.

## Core rules

The game is a round-based system with a fixed phase order, 4-direction movement, line-of-sight shooting and a small set of damage types, and every rule is deterministic. Where this section and the design document disagree, this section wins for the MVP.

**Grid and tiles**

- The room is an 8x8 grid. Columns run A to H left to right, rows run 1 to 8 top to bottom, and tiles are addressed like D5.
- Only one ground unit can occupy a tile. Flying units can pass over other units but cannot end on an occupied tile.
- Distances: ground movement counts 4-direction steps. Range uses Chebyshev distance (the larger of the column and row difference). "Adjacent" means the 8 surrounding tiles and is used for melee, blasts, chains and poison spread.
- Line of sight is a Bresenham line between tile centers. It is blocked by pillars and walls only. The line is always traced from the tile with the lower row, then the lower column, and the same list of tiles is used in both directions, so sight is symmetric. A plain trace is not: a standard integer Bresenham gives different answers in each direction for 88 tile pairs on this map. The implementation session should write unit tests for corner cases and for symmetry.

| Tile or object | Blocks movement | Blocks line of sight | Blocks projectiles |
| --- | --- | --- | --- |
| Floor | No | No | No |
| Pillar, wall | Yes | Yes | Yes |
| Shutter (D1, E1), wall gun (H5) | Yes | No | Yes |
| Shield segment, egg | Yes | No | Yes |
| Mine, vent, floor panel, corpse, scrap | No | No | No |

Projectile attacks (Sidearm, turret shots, Guard shots, Acid Spit, Parasite) travel along the line of sight and hit the first blocking thing or unit on it, friendly or not. Lobbed attacks (Spore Pod, Creep) ignore everything between the attacker and the target tile.

**Round structure**

1. Round start: spawn any wave scheduled for this round, play any intercom hint, and refill the player to 3 AP and 3 movement.
2. Player phase: the player acts until they end the turn.
3. Minion phase: each minion acts, in the order it was summoned. Units spawned this phase do not act until next round.
4. Enemy phase: each enemy acts, in the order it spawned.
5. Environment phase, in this order: primed hazards fire or cancel; hazards prime for next round; poison and parasite tick, and poison spreads; egg timers count down and eggs hatch; hazard cooldowns and corpse decay advance.

**Timers and cooldowns**

Every timer ticks at its Environment step in every round, including the round in which it starts. Worked through:

- A 3-round status ticks three times. Poison applied in the Player phase of round N deals damage in the Environment phase of rounds N, N+1 and N+2, then ends. A status applied during the tick step itself, by spread or by Parasite Jump, first ticks in the next round.
- A floor egg (1 round) laid in round N hatches at the end of round N, and its hatchlings first act in round N+1. A corpse egg (2 rounds) hatches at the end of round N+1.
- A corpse created at any point in round N is removed at step 5 of round N+1. It is always available for the whole of the next Player phase, and a kill made on your own turn can also be egged that same turn.
- A hazard that fires in round N can prime again in round N+3 and fire in round N+4. A hazard that cancels in round N can prime again in round N+1.

**The player's turn**

- 3 AP and 3 free movement per round. Movement can be spent before, between or after actions. Unspent AP and movement do not carry over.
- Sprint (1 AP) gives 2 extra movement. This is how "AP as movement" works in the MVP.
- Free movement can be undone until the player spends AP. Spending AP commits the turn so far. A move that triggers anything, such as a mine or a scrap pickup, also commits.

**Damage types and armor**

| Type | Used by | Armor applies |
| --- | --- | --- |
| Kinetic | Sidearm, turret, Guard, melee, wall gun | Yes |
| Explosive | Mines, burster hatchlings | Yes |
| Fire | Flamer sweep | Yes |
| Electric | Drone, floor panels | No |
| Acid | Spit, spitter hatchlings, acid effects | No |
| Poison | Spore Pod, poison status, vent gas | No |
| Parasite | Parasite ticks | No |

Armor subtracts from damage of the types marked Yes, to a minimum of 0. Each Corrode stack removes 1 armor, down to 0. Healing cannot exceed maximum HP.

**Statuses**

| Status | Effect | Duration |
| --- | --- | --- |
| Poison | 1 damage per round at the poison tick. After the tick it spreads to adjacent units that are not poisoned, up to 3 new infections per round across the whole board. A spread infection takes the remaining duration of its source, not a fresh 3 | 3 rounds, refreshed to 3 on direct reapplication |
| Parasite | 2 damage per round. Cannot be removed. A unit carries at most one Parasite, and reapplying it restarts the 3 rounds without stacking (Proposed). When the host dies, a basic hatchling spawns on its tile if the minion cap allows | 3 rounds |
| Corrode | Each stack reduces armor by 1 | Rest of the fight |
| Pinned (upgrade) | Cannot move during its next phase | 1 round |
| Slowed (upgrade) | Has 1 less movement during its next move | 1 round |

Spread order is fixed. After the tick, every unpoisoned unit adjacent to a poisoned unit is a candidate. Candidates are infected in order of the duration they would receive (highest first, taking the best adjacent source), then by unit ID, until 3 are infected. Because duration is inherited, one Spore Pod dies out after three rounds even in a tight cluster: its direct targets take 3, the units they infect take 2, and the next ring takes 1. With a fresh 3 on every spread, two adjacent units would reinfect each other forever.

Poison follows the universal friendly-fire rule, so it can spread to the player and to hatchlings. If that proves too punishing in playtests, the first fallback is to make hatchlings immune to poison.

**Death, corpses and scrap**

- A unit is marked dead the moment its HP reaches 0. It is removed, and its death effects are queued, once the effect that killed it has finished applying its damage.
- Enemy corpses stay on their tile for 2 rounds (see Timers and cooldowns) and can host Brood Eggs. Player-side units leave no corpse in the MVP.
- Scrap (stretch): each destroyed enemy drops 1 scrap on its tile (the Warden drops 3). The player picks scrap up by entering the tile. Carry limit is 5. Nothing drops unless Repair is built.
- The player dying is an immediate loss. Units that die in the same effect are still removed and reported first (in unit ID order, so after the player's own death is recorded), and leave their corpses, but no death effect or intercom line they would trigger plays, since the fight is over.

**Chains and friendly fire**

- Everything damages everything unless an effect says otherwise. Damage code does not check teams. Only targeting logic does, such as the Medic choosing allies to heal.
- Every root action (a player command, a minion or enemy action, or an environment step) starts a chain. Within one chain, each effect instance (source unit plus effect) may trigger at most once, and later triggers are ignored and logged. A depth cap of 20 trigger generations from the root action is a safety net.
- Area effects apply damage to every target first. Deaths and their triggers are then processed in unit ID order.

**Tie-breaks**

Whenever something picks a target, it chooses the nearest, then the lowest current HP, then the lowest unit ID. The player has ID 0, and every other unit takes the next ID when it spawns. Units in a wave take IDs in the order the wave table lists them. "Nearest" is path length for ground melee units and Chebyshev distance for ranged and flying units.

## Armory Lockdown

Armory Lockdown is a fixed 8x8 room with three hidden waves and a Warden that arrives last. The layout is the same on every attempt, so players can learn it.

**Terrain**

```text
     A  B  C  D  E  F  G  H
 1   .  .  .  S  S  .  .  .
 2   .  .  .  .  .  .  .  #
 3   .  .  .  .  .  .  .  .
 4   #  P  .  .  .  .  .  .
 5   .  v  .  z  z  .  .  G
 6   .  .  P  .  .  P  v  .
 7   .  .  .  .  .  .  .  .
 8   .  @  .  .  .  .  .  .
```

Legend: `.` floor, `#` wall, `P` pillar, `S` shutter, `z` floor panel, `v` gas vent, `G` wall gun, `@` player start.

| Element | Tiles | Notes |
| --- | --- | --- |
| Player start | B8 | 8 HP, 3 AP, 3 movement |
| Walls | A4, H2 | Block movement, sight and projectiles |
| Pillars | B4, C6, F6 | Block movement, sight and projectiles |
| Shutter | D1, E1 | Impassable. Wave 3 appears just inside it |
| Floor panels | D5, E5 | One hazard, called the panel grid |
| Gas vents | B5, G6 | Two independent hazards |
| Wall gun | H5 | Fires west along row 5 |

The middle crossing (D5 and E5) sits inside the gun's lane, so crossing it has a cost. The pillars give cover and decide where you can stand with a clear line to the enemy.

**Waves**

Waves arrive on a hidden timer, at the start of rounds 1, 4 and 7, whether or not earlier waves are dead. If a spawn tile is occupied, the unit appears on the nearest free tile (ties broken by lowest row, then lowest column).

| Wave | Round | Enemies and spawn tiles |
| --- | --- | --- |
| 1 | 1 | Guard at C3, Guard at G3, Guard at B2, Medic at E2 |
| 2 | 4 | Guard at A3, Guard at A2, Flamer at H3. They flank from the side edges |
| 3 | 7 | Warden at D2, Guard at C2, Guard at E2. They come through the shutter |

The debug overlay shows the wave timer. Players never see it.

**Arrival round (Proposed)**

In the round a wave arrives, its units move but do not act. Each uses only the movement part of its plan: a Guard walks to its firing tile, the Medic to its ally, the Flamer to its best-scoring tile, and the Warden makes its approach move. They attack from the following round.

Without this rule the opening has no safe tile. Guards cover 8 tiles of reach (3 movement plus range 5) on an 8x8 board, and a scripted check of round 1 under the Guard rule in this spec found that, with no decoy placed, every tile the player can reach on free movement takes 6 or 9 damage against 8 HP. The Alien kit cannot field a decoy in round 1 at all. With the rule, round 1 is a setup turn for every kit, and a player who still has no decoy or cover in round 2 takes 6 or 9 then, so the pressure is intact. Moving the spawn tiles or adding one pillar does not solve it without packing all three Guards into the top-left corner.

**Win and lose**

- Win: wave 3 has spawned and every enemy is dead.
- Lose: the player's HP reaches 0.
- Safety cap: if round 30 ends with the fight unresolved, the attempt is a loss.
- Open: if the room is empty at the start of a round, the next wave could arrive early so players do not wait. The default is a fixed schedule. Test both.

## Player and abilities

The player is a squishy single character with 8 HP, and every ability's numbers live in data so values can be tuned without code changes.

| Stat | Value |
| --- | --- |
| HP | 8 |
| Armor | 0 |
| AP per round | 3 |
| Free movement per round | 3 |
| Scrap carry limit (stretch) | 5 |
| Healing | None in the MVP room |

**Tech kit**

| Ability | AP | Targeting | Effect |
| --- | --- | --- | --- |
| Sidearm | 1 | Projectile, range 6 | 2 kinetic damage to the first unit hit |
| Grapple Hook | 1 | Projectile, range 5 | Works along any line of sight, and every pull steps tile by tile through that line's tile list. Pull self: the player travels toward the target tile and stops on the last free tile before any blocker. Pull unit: the first unit hit is dragged until adjacent to the player. The hook pulls units only, never objects. The Warden cannot be pulled |
| Proximity Mine | 1 | Empty tile within 3 | Places a mine that triggers when any ground unit enters it, friend or foe. It deals 3 explosive damage to its tile and the 8 neighbours, then is consumed. At most 4 mines are active. Pick Up (1 AP, adjacent) returns a mine |
| Auto-Turret | 2 | Empty tile within 2 | Minion with 4 HP and 4 ammo. See Minions, eggs and mines |
| Scout Drone | 2 | Spawns adjacent to the player | Flying minion with 3 HP. See Minions, eggs and mines |
| Barrier Shield | 1 | Two adjacent free tiles within 4 | Two shield segments of 4 HP each. They block movement and projectiles but not sight. At most 2 placements (4 segments) are active |

Reload is a utility action, always available when it has a valid target. It costs 1 AP, targets a turret within 2, and refills its ammo. Repair is a stretch utility action: it costs 1 AP and 1 scrap, targets a turret, drone or shield segment within 2, and restores 3 HP.

**Alien kit**

| Ability | AP | Targeting | Effect |
| --- | --- | --- | --- |
| Acid Spit | 1 | Projectile, range 5 | 1 acid damage and 2 Corrode stacks to the first unit hit |
| Lunge | 1 | Straight orthogonal line, up to 3 tiles (Proposed) | The player leaps over units, not over pillars, walls or shields, and lands on a free tile. Then they may hit one adjacent unit for 2 kinetic damage. Can be used purely to move |
| Spore Pod | 1 | Lob, range 5 | Bursts on the target tile and poisons every unit in the 3x3 area, friend or foe |
| Brood Egg | 2 | Empty tile or enemy corpse within 3 | Places an egg. See Minions, eggs and mines |
| Parasite | 1 | Projectile, range 5 | Applies Parasite to the first unit hit |
| Creep (stretch) | 1 | Lob, range 4 | Creates a patch of 3 tiles that spreads one adjacent floor tile per round up to 6 tiles. Enemies ending their move on creep lose 1 movement next phase, and hatchlings on creep heal 1 HP per round |

**Rules for every ability**

- An ability that finds no valid target at cast time cannot be committed.
- Every ability must support a dry-run preview from the same code that resolves it, so the preview can never disagree with the result.
- Friendly units that would be hit are listed in the preview and highlighted in the UI.
- AP costs, ranges and damage values are Proposed starting values stored in data. See the tuning table in the test plan.

## Minions, eggs and mines

Minions act on their own with one readable behaviour each, and the player steers them only through placement. Minions never pick a friendly unit as a target. Friendly fire comes only from area effects, chains, projectile lines and hazards.

**Caps and order**

- At most 5 minions are alive at once. Turrets, drones (including a docked drone) and hatchlings count. Mines, shield segments and eggs do not.
- At most 3 eggs are on the board at once. This cap cannot bind at the current values: an egg costs 2 of the 3 AP and the longest timer is 2 rounds, so at most 2 eggs ever exist together. It stays as a guard for later changes to egg cost or timers.
- Minions act once per round in the Minion phase, in unit ID order, which is summon order. A redeployed drone keeps its ID. A minion moves first, then acts.
- Each minion's intended target or destination is shown to the player at all times (see UI and feedback).

**Minion stat blocks**

| Minion | HP | Armor | Move | Action and behaviour |
| --- | --- | --- | --- | --- |
| Auto-Turret | 4 | 0 | 0 | Fires at the nearest enemy within range 5 and line of sight: 2 kinetic, uses 1 ammo of 4. At 0 ammo it stays as a blocker |
| Scout Drone | 3 | 0 | 3, flying | Moves toward the nearest enemy until it is within range 2 with line of sight, then zaps it: 1 electric, chaining 1 electric to every other unit adjacent to the target. The chain never hits the drone that fired it (Proposed). See the drone cycle below |
| Hatchling | 2 | 0 | 3 | Moves toward the nearest enemy and hits one adjacent enemy for 1 kinetic |
| Spitter hatchling | 4 | 0 | 3 | Moves until an enemy is within 3 with line of sight, then fires 1 acid damage |
| Mender hatchling (stretch) | 4 | 0 | 3 | Moves toward the most damaged hatchling and heals one adjacent damaged hatchling for 1 HP. No attack |
| Burster hatchling | 4 | 0 | 3 | Moves toward the nearest enemy. When adjacent to one, it detonates and dies: 3 explosive damage to its tile and the 8 neighbours |

**Drone cycle**

1. Deployed: the drone has 2 charges. Each zap uses 1 charge.
2. Returning: at 0 charges the drone flies up to 4 tiles toward the player and makes no attack. It is vulnerable here.
3. Docked: when it is adjacent to the player, or after 2 Minion phases returning, it docks and is removed from the board.
4. Redeploy: the player spends 1 AP to place it adjacent to the player with full charges.

**Eggs**

An egg is an object with 3 HP that blocks movement. Any damage can hit it, friendly or not. Eggs count down at step 4 of the Environment phase, starting in the round they are laid, and hatch when the timer reaches 0. A floor egg therefore sits through one Enemy phase and hatches at the end of the round it was laid. Hatchlings first act in the following Minion phase.

| Host tile | Timer | Result |
| --- | --- | --- |
| Empty floor | 1 round | 2 basic hatchlings, one on the egg's tile and one on the nearest free adjacent tile |
| Guard corpse | 2 rounds | 1 spitter hatchling. Consumes the corpse |
| Medic corpse | 2 rounds | 1 mender hatchling (stretch). Consumes the corpse. Until the mender is built, a Medic corpse cannot host |
| Flamer corpse | 2 rounds | 1 burster hatchling. Consumes the corpse |
| Warden corpse | None | Cannot host in the MVP. The armored hatchling is out of scope |

- Hatchlings that would exceed the minion cap do not spawn, but the egg still hatches.
- An egg destroyed before it hatches produces nothing, and a corpse it was placed on is lost.
- Live hosts and cocoons are out of scope. Brood Egg cannot target a living unit.

**Mines**

- A mine does not block movement. Enemies do not avoid mines. Flying units do not trigger them.
- It triggers when any ground unit enters its tile, including during a pull, then explodes and is consumed.
- A mine is visible to everyone. The Pick Up action returns a mine to the player's hand.
- Projectiles pass over mines. With Shot-Triggered Mines, the Sidearm can target a mine directly (see Loadout and upgrades).

## Enemies

Four enemy types are enough to test the shape of the fight: a ranged soldier, a healer, an area-denial unit and an armored elite. Each acts once per round, moving first and then acting.

| Enemy | Role | HP | Armor | Move | Action |
| --- | --- | --- | --- | --- | --- |
| Guard | Ranged soldier | 6 | 0 | 3 | Projectile, range 5: 3 kinetic damage |
| Medic | Support | 5 | 0 | 3 | Heals one damaged ally within 3 and line of sight for 2 HP. No attack |
| Flamer | Area | 6 | 1 | 2 | Sweep: 3 fire damage to every unit and object in a straight line of 3 tiles starting adjacent, allies included. Walls and pillars end the line |
| Warden | Elite | 20 | 2 | 3 | Charge: up to 4 tiles in a straight line, then 4 kinetic damage to what it stopped against. Melee: 3 kinetic to an adjacent unit. Purge: every other round clears its own Poison |

The Flamer's 6 HP and armor 1, and the Warden's armor 2, are Proposed.

**Shared behaviour**

- Enemies move along shortest 4-direction paths, cannot pass through units, and ignore mines and hazard tiles. They do not avoid your traps.
- Enemies target units on the player's side (the player and minions). They do not aim at eggs or shield segments, but projectile lines and sweeps still hit them.
- Wave units spawn at the start of the round. In that round's Enemy phase they move but do not act (see Arrival round), so the player always gets a full turn before a new wave can attack.
- AI planning is a pure function of the game state. Enemy intents are recomputed after every player action and shown on screen.

**Guard**

A tile gives a shot if a player-side unit is within range 5 with line of sight and no enemy ally is the first thing on the line. Player-side minions, shields and eggs in the way do not stop a Guard from firing, and the shot hits them instead.

1. If the current tile gives a shot, stay and shoot the nearest such unit (ties: lowest HP, then lowest ID).
2. Otherwise move to the reachable tile that gives a shot in the fewest steps (ties: closest to the target, then lowest row, then lowest column), then shoot.
3. If no reachable tile gives a shot, move as close as possible to the nearest player-side unit.

**Medic**

1. Find the damaged ally with the lowest HP (ties: lowest ID).
2. Move to a reachable tile within 3 and line of sight of that ally, preferring tiles that keep distance from the nearest player-side unit (at least 4 if possible).
3. Heal that ally for 2. If nobody is damaged, follow the nearest Guard and stay within 3 of it.

The Medic out-heals poison (1 per round), so it is the key early target.

**Flamer**

1. Evaluate every reachable tile and each of the 4 sweep directions.
2. Score each option: +2 for each player-side unit hit, +4 more if the player is hit, and -3 for each of its own allies hit.
3. Take the highest positive score (ties: shortest move, then lowest tile). If none is positive, move toward the nearest player-side unit.

This makes the Flamer a counter to clustered swarms. With armor 1, basic hatchlings cannot hurt it until it is Corroded, and it is the first enemy against which Corrode and Piercing Rounds pay off.

**Warden**

1. If Purge is off cooldown and the Warden is poisoned, Purge. This is free, starts a 2-round cooldown, and makes the Warden immune to poison spread for the rest of the round. Without the immunity, a poisoned neighbour would reinfect it in the same round's spread step.
2. If a player-side unit stands 2 to 5 tiles away in a straight orthogonal line with a clear lane, charge: move 1 to 4 tiles along the line, stop against the first player-side unit or blocking object in the way, and hit it for 4 kinetic. A lane is clear if no wall, pillar or enemy ally lies in it. A body in the lane stops the charge, even if it is a hatchling.
3. Otherwise, if adjacent to the player, melee the player.
4. Otherwise, move toward the player, preferring a reachable tile that lines up a clear charge lane for next round.
5. If it cannot move any closer to the player, melee an adjacent player-side unit, chosen by the tie-break. This stops a ring of hatchlings from stalling it.

The Warden always heads for the player. Armor 2 stops the Sidearm, turret shots, Lunge and hatchling bites outright and cuts a mine to 1. The answers are Corrode, Piercing Rounds, and damage that ignores armor: acid, electric, poison and Parasite. One Acid Spit removes the armor for the rest of the fight. If that makes Acid Spit a fixture of every win, the first knob is to let Corrode expire.

## Lab hazards and intercom

The lab's defences are controlled by an AI that decides when to fire, gives a subtle cue first, and values its own units in a fixed order. Hazards never show a danger zone. They give a cue and nothing more.

**Hazard specs**

| Hazard | Tiles | Effect when it fires | Damage | Cooldown |
| --- | --- | --- | --- | --- |
| Wall gun | H5 | Shoots west along row 5 and hits the first unit or blocking object in the lane | 2 kinetic | 3 rounds |
| Panel grid | D5, E5 | Discharges onto every unit on or adjacent to D5 or E5 (tiles C4 to F6) | 2 electric | 3 rounds |
| Gas vent | B5, G6 (each is independent) | Releases gas onto the vent tile and its 8 neighbours. The gas is a single hit of poison-type damage, which ignores armor. It does not apply the Poison status | 1 poison | 3 rounds |

**Prime, then fire**

1. Ready: the hazard is off cooldown.
2. Prime: at Environment step 2, the lab AI checks the value rule against the current positions. If it allows firing, the hazard becomes primed and shows its cue: a wisp from a vent, a glowing gun barrel, or crackling panels.
3. Fire: at Environment step 1 of the next round, a primed hazard is committed (Proposed). It fires on everything then in its area, soldiers included, and starts its cooldown. It cancels, with a 1-round cooldown, only if an elite would be hit or if nothing would be hit.

This gives the player one full round to react to a cue, and nothing tells them exactly which tiles are at risk.

The lab decides at priming and cannot call the shot back, which is what makes bait possible: draw the cue, then leave while a soldier walks in or is pulled in. Apart from a combined kill, this is the only way a hazard hurts the lab's own troops, as the design document intends.

**The value rule**

The lab ranks targets as elite, then your life, then soldier, then minion. A soldier is any enemy that is not an elite. The rule is checked when a hazard primes. For a hazard that would hit a set of victims V:

1. If V contains the Warden or any other elite, hold.
2. If V contains the player and the combined kill check passes, prime. This is a combined kill, and it primes even if enemy soldiers are in V.
3. Otherwise, if V contains no enemy soldiers and at least one player-side unit, prime.
4. Otherwise, hold.

The combined kill check is computed once per priming step, so no hazard's decision depends on another's. Take every ready hazard whose V contains the player and no elite, in the fixed order gun, panel grid, vent B5, vent G6, and add the damage each would deal to the player. If the total is at least the player's current HP, all of those hazards pass step 2 together.

For the panel grid and the vents, V counts units only, not objects. For the wall gun, V is the first unit or blocking object in its lane, and a player-side object there (a shield segment or an egg) ranks as a minion (Proposed). A shield in row 5 therefore draws the gun's fire. Without this, one shield segment would leave the gun with no victim and switch it off for the whole fight.

The gun only hits the first thing in its lane, so its V never holds both the player and a soldier. The debug overlay must show which step produced each decision.

**Lexicon**

The intercom uses a small fixed vocabulary, so the player can learn it over repeated attempts. The same word always means the same thing.

| Code | Means |
| --- | --- |
| Sentry | Guard, a ranged soldier |
| Medic | Medic, a healer |
| Burner | Flamer, an area attacker |
| Warden | The elite |

**Intercom lines**

Staff are truthful and speak in one or two sentences. Each line plays at most once per fight, at most one line plays per phase, and all lines go to a log the player can reopen. Lines before waves are deliberately vague, so they act as a cue without revealing the hidden timer. If several lines trigger in the same phase, they queue in table order and play one per phase. The line for the player's death plays at once and clears the queue.

| Trigger | Line |
| --- | --- |
| Round 1 start | "Containment grade 2, armory floor. Sentries and a Medic to sector three." |
| Round 3 start | "Security, flank the intruder from the side corridors. Send a Burner." |
| Round 6 start | "Warden to the armory floor." |
| First hazard primed | "Defence systems are tracking the intruder." |
| First hazard hits the player | "Defences engaged." |
| A hazard hits an enemy soldier | "Acceptable losses." |
| Medic dies | "Medic is down." |
| Flamer dies | "Burner is down." |
| Warden below 10 HP | "Warden, report." |
| Player below 4 HP | "The intruder is wounded. Keep the pressure on." |
| Warden dies | "Warden is down. The armory is compromised." |
| Player dies | "Intruder neutralised." |

## Loadout and upgrades

Before each fight the player builds a small loadout, and the upgrades change rules, not numbers, so builds differ in how they play. The loadout screen stands in for the run's upgrade choices, which the MVP does not have.

**Loadout rules**

- Choose up to 8 abilities from the 11 in the pool (6 Tech, 5 Alien).
- Choose up to 3 upgrades from the 12 below. An upgrade can only be chosen if the loadout contains the abilities it requires.
- No duplicates. Reload is available automatically whenever the loadout has a turret. Repair (stretch) would come with a turret, drone or shield.
- Loadout size is Open. At 8 of 11, any two builds share at least 5 abilities, which weakens the test of whether loadouts play differently. Try 6 of 11 in the informed playtest round.
- The loadout is locked once the fight starts.

**Presets**

Presets are quick starts and also the regression fixtures for the headless simulation. The expected results come from the earlier hand simulations and are hypotheses, not targets.

| Preset | Abilities | Upgrades | Expected from earlier sims |
| --- | --- | --- | --- |
| Tech | Sidearm, Grapple Hook, Proximity Mine, Auto-Turret, Scout Drone, Barrier Shield | Piercing Rounds, Shot-Triggered Mines, Overcharged Drone | Lost to armor 3 in the hand simulations. At armor 2, Piercing Rounds restores full Sidearm and turret damage. Untested |
| Alien | Acid Spit, Lunge, Spore Pod, Brood Egg, Parasite | Spore Burst, Parasite Jump, Acid Brood | Strong early, short of burst |
| Hybrid | Sidearm, Proximity Mine, Scout Drone, Barrier Shield, Acid Spit, Spore Pod, Brood Egg, Parasite | Spore Mines, Plague Drone, Parasite Jump | Unknown. The nearest hand-simulated build, Plague Engine, lost, and every hand-simulated win used Occult or live capture |

**Paper check before M0 (Proposed)**

No build made only of Tech and Alien tools has won this room on paper. In the design document's results, every win used Occult or live capture, and the enemies here are the boosted versions that were tuned against those builds. Before any content is built, re-run the three presets by hand under this spec's rules. The check passes when the Hybrid preset has a written winning line. If it does not, lower Warden HP, then cut wave 3 to one Guard, until it does. Once the simulator exists, the bots repeat the check after every tuning change.

**Upgrades**

| Id | Upgrade | Requires | Rule |
| --- | --- | --- | --- |
| T1 | Piercing Rounds | Sidearm or Auto-Turret | Kinetic damage from your Sidearm and turrets ignores 2 armor |
| T2 | Shot-Triggered Mines | Proximity Mine and Sidearm | The Sidearm can target one of your mines within range and line of sight, and a shot that reaches the mine detonates it. Other projectiles still pass over mines (Proposed). |
| T3 | Suppressing Fire | Auto-Turret | An enemy hit by a turret shot is Slowed |
| T4 | Grapple Pin | Grapple Hook | A unit pulled by the grapple takes 1 kinetic damage on landing and is Pinned for 1 round |
| T5 | Reflective Shield | Barrier Shield | The first projectile to hit a shield segment each round is reflected back along its line |
| T6 | Overcharged Drone | Scout Drone | The drone has 3 charges, and its chain jumps one extra hop to units adjacent to chained targets |
| A1 | Spore Burst | Spore Pod | When a poisoned unit dies, it poisons its 3x3 area |
| A2 | Acid Brood | Brood Egg | When any hatchling dies, adjacent units gain 2 Corrode |
| A3 | Parasite Jump | Parasite | When a Parasite host dies, the Parasite jumps to the nearest enemy within 3 with line of sight and restarts its 3 rounds |
| A4 | Fast Brood | Brood Egg | Corpse eggs hatch in 1 round |
| H1 | Spore Mines | Proximity Mine and Spore Pod | Mine explosions also poison every unit in their blast area |
| H2 | Plague Drone | Scout Drone and Spore Pod | Drone zaps also poison the target and every unit the chain hits |

Every upgrade follows the universal friendly-fire rule. Reflected shots, spores and acid can hit the player and minions, and previews must show that.

**Design check**

The upgrade list is deliberately small. If playtesters keep picking the same three, or if one build wins almost every attempt, the fix is in the upgrade rules and not in the enemy numbers. Log every loadout with its result so the pattern is visible.

## UI and feedback

The interface has one job: let the player predict the chaos before they commit to it. Everything the player does is previewed, enemy and minion intentions are visible, and only the lab's hazards stay deliberately hard to read.

**Layout**

- The 8x8 grid fills the center, with column letters and row numbers on the edges.
- A status strip shows HP, AP pips, movement pips, the round counter and the current phase, plus scrap if Repair is built.
- An ability bar shows each loadout ability with its AP cost. A disabled ability states why: not enough AP, no valid target, or a cap reached.
- A minion panel lists every minion with HP, turret ammo pips and drone charge state.
- An intercom panel shows the latest line, with a button to open the log of earlier lines.
- Controls: End Turn, Undo Movement, and a fast-forward toggle with 1x, 2x and skip.
- Platform and input are Open. Design mouse-first with keyboard shortcuts for every control.

**Previews for every player action**

- Selecting an ability highlights valid targets. Hovering a target shows the full effect.
- Area effects outline every affected tile. Chains and ricochets are drawn as lines.
- Each affected unit shows predicted damage after armor, and a marker if it would die.
- Any friendly unit that would be hit gets a red outline and a warning icon, including the player. The preview must show friendly fire before commit.
- Hovering an egg tile shows what it will hatch into and how many rounds it needs. Hovering a placed egg shows the time left.
- A grapple or lunge shows the destination tile.

**Intents for enemies and minions**

- Each enemy shows its planned destination and its target line, with predicted damage. The Warden's charge lane is highlighted when a charge is available, and the Flamer's sweep line is drawn.
- Each minion shows the same.
- A unit in its arrival round shows its destination only.
- Intents are recomputed after every player action, because AI planning is a pure function of the state.

**Hazard cues**

- A primed hazard shows only its cue: a wisp from a vent, a glowing barrel on the gun, crackling on the panels. No danger zone is drawn.
- Whether to show a hazard's cooldown to the player is Open. The default is not to show it.

**Status display**

- Icons for Poison, Parasite, Corrode (with stack count), Pinned and Slowed. Armor is shown as a number on the unit.
- Corpses show how many rounds they have left, and eggs show their timer.
- Scrap pickups appear on their tile (stretch).

**Turn flow and speed**

- After End Turn, a banner names each phase as it plays: minions, enemies, then environment.
- Every animation is skippable. The target budget is about 0.4 seconds per actor at 1x. A full swarm turn should complete in under 10 seconds at 2x.
- A combat log lists every event with its source, so a player can see why something happened.

**Result screen**

- Shows the outcome, rounds survived and the loadout used.
- Shows damage taken and dealt, split by source: enemies, hazards, and the player's own minions and effects. The friendly-fire share is always visible.
- Shows the cause of death, so the player can tell why they lost.

**Accessibility**

- Never rely on color alone. Pair color with icons, outlines or shapes, and use a color-blind-safe palette.
- Scalable text and remappable keys are stretch.

**Debug overlay**

Toggled by a key and hidden by default. It shows:

- Unit IDs and tile coordinates.
- The wave timer and spawn tiles.
- Each hazard's cooldown and primed state, and which step of the value rule produced its last decision.
- The Flamer's scored options.
- Step-through controls for phases and single actions, replay playback, and the ability to set HP and AP, spawn a unit or force a wave.
- Hot reload of the data files, so tuning does not need a rebuild. A reload restarts the current fight.

## Technical architecture and data

The rules engine is a deterministic library that knows nothing about rendering, input or time, and every number in the rules comes from data. This is what makes previews reliable, balance testable in bulk, and bugs replayable.

**Principles**

1. The core is a pure function: `step(state, command)` returns a new state and a list of events. No rendering, input or clock dependencies.
2. Numbers are data (JSON): unit stats, ability costs, ranges and damage, upgrade values, hazards, waves and intercom lines. Tuning needs no code change. Behaviour is code (see Abilities and upgrades in code).
3. Previews and enemy intents come from the same engine, run on a cloned state with no side effects. The UI never re-implements a rule.
4. All outputs are an event stream. The client animates events and the headless simulator consumes the same stream.
5. Determinism: integer positions, integer damage, no floating point in rules, no wall-clock, no dependence on hash or iteration order, and ties resolved by unit ID.

**Modules**

| Module | Responsibility | Depends on |
| --- | --- | --- |
| state | Grid, units, objects, statuses, hazards, IDs | None |
| rules | Round and phase loop, movement, line of sight, damage and armor, statuses, death and corpses | state |
| effects | Ability and upgrade behaviours, shared helpers, the event queue, hooks and the once-per-chain rule | rules |
| ai | Pure planning functions for enemies, minions and the hazard AI | rules |
| content | JSON data, loaders and schema validation | None |
| preview | Dry-run wrappers that clone state, resolve and diff | effects, ai |
| telemetry | Event log, command log and replay | state |
| sim | Headless runner, bots and batch statistics | all of the above |
| client | Rendering, input, animation, HUD and debug overlay | preview, telemetry |

**Engine and language (Open)**

The spec only requires that the core can run headless for tests and simulations, load JSON, and use integer math. Two reasonable paths are a TypeScript core with a web client, which makes headless simulation in Node easy, or a game engine with the core kept as an engine-independent module. Choose by team skill.

**State model**

- Unit: id, definition id, team (player side or enemy), position, HP, max HP, armor, statuses, flags (flying, elite), ammo, charges and cooldowns.
- Object: id, kind (mine, egg, shield segment, corpse, scrap), position, HP, and data such as an egg's timer and host variant.
- Hazard: id, definition id, tiles, state (ready, primed or cooldown) and cooldown remaining.
- Game: round, phase, wave index, scrap, intercom lines already shown, and the active loadout.

**Commands and events**

- Commands from the player: Move, Sprint, UseAbility (with target tiles and mode), Reload, PickUpMine, Redeploy and EndTurn, plus Repair if it is built.
- Events include PhaseStarted, UnitMoved, DamageDealt (with source, type, amount and armor absorbed), UnitDied, StatusApplied, StatusTick, ObjectPlaced, ObjectTriggered, EggHatched, HazardPrimed, HazardFired, HazardHeld, WaveSpawned, IntercomLine and ChainSuppressed.

**Abilities and upgrades in code (Proposed)**

For the MVP, each ability and upgrade is a small code behaviour keyed by its id. It is built from shared helpers (damage, heal, applyStatus, move, pull, spawn, placeObject, area, line, chain) and reads every number from data. This is the split the spec already uses for AI.

A generic effect language in data was considered and set aside. Five of the 12 upgrades need things a list of effect operations cannot express: changing a value where it is read (Piercing Rounds, Overcharged Drone, Fast Brood), intercepting a projectile (Reflective Shield) and a movement status (Suppressing Fire). With 11 abilities and 12 upgrades, code is cheaper than growing a language. Revisit the choice after the MVP, when the content count rises.

Upgrades attach in two ways. A modifier changes a value or a calculation where it is read, such as armor ignored, drone charges or an egg timer. A hook runs a behaviour when an engine event happens. The two examples below show the data for an ability and for an upgrade that hooks an event.

```json
{
  "id": "spore_pod",
  "ap": 1,
  "targeting": { "kind": "lob", "range": 5 },
  "tags": ["poison", "spread"],
  "area": { "shape": "square", "radius": 1 },
  "status": { "id": "poison", "duration": 3 }
}
```

```json
{
  "id": "A1_spore_burst",
  "requires": ["spore_pod"],
  "hook": "unitDied",
  "area": { "shape": "square", "radius": 1 },
  "status": { "id": "poison", "duration": 3 }
}
```

Hook triggers are limited to a small set: unitDied, damageTaken, projectileHit, unitPulled, objectTriggered, statusApplied, roundStart and roundEnd. AI behaviours are written in code but take their numbers from data.

**Chain rule**

Each root action opens a chain. The engine keeps a set of effect instances (source entity plus effect id) that have already fired in that chain, ignores repeats, and emits ChainSuppressed so the debug overlay can show it. The queue is first in, first out, with a depth cap of 20 trigger generations.

**Telemetry and replay**

- Every fight is stored as its loadout plus the ordered list of player commands. Replaying that file reproduces the fight exactly, so bug reports are one file. Each file also stores the engine version and a hash of the content files, and a replay is only valid against that pair. Hot reload restarts the current fight, so one log never spans two sets of values.
- Each fight also writes a summary: loadout, outcome, rounds, damage by source (including friendly fire), abilities used, peak minion count, hazard fires with victims, and the cause of death.
- The event stream can be written as one JSON line per event for analysis.

**Automated tests**

- Table-driven tests for line of sight corner cases and symmetry, armor and Corrode math, the value rule (one test per case), a committed hazard hitting a baited soldier, wave spawn collisions, the arrival round, the minion cap, and poison spread (the cap, the fixed order, and dying out in a cluster).
- Golden replays for the three presets. Their event logs must not change between builds unless a rule change is intended. They are re-recorded on purpose whenever content values change.

## Test plan and tuning

The prototype succeeds if it answers the seven questions in Goals and scope, and the answers come from telemetry and observation, not impressions. Testing runs in three rounds, with a headless simulator running alongside to find dominant strategies and bugs at scale.

**Playtest rounds**

1. Developer play: 5 to 10 attempts per preset, to catch bugs and obvious balance failures before anyone else sees it.
2. First-time players: 5 to 8 people, one at a time, with a one-page rules sheet and no coaching. Record the screen and telemetry. Ask three questions after each attempt: why did you lose, what surprised you, what would you try next.
3. Informed players: the same people after watching a replay of one loss, to test whether knowledge is rewarded. Each plays 3 attempts on the Hybrid preset, and results are pooled by attempt.

**What observers watch for**

- Hesitation: where turns slow down and why.
- Misreads: previews or intents the player misunderstood.
- Deaths the player called unfair, and the cause.
- Combos the player discovered without being told.
- Whether friendly fire feels like a choice or a punishment.
- Whether the hazards feel reactive and whether the cues were noticed.

**Metrics and alarms (Proposed)**

| Metric | Source | Target | Alarm |
| --- | --- | --- | --- |
| First attempts lost | First attempt outcomes | About 70% of testers (4 of 5, up to 6 of 8) | Fewer than half, or players quit after one loss |
| First-time losses that reach wave 2 | Rounds per lost fight | Most reach round 4 | Median loss before round 3 |
| Informed win rate (Hybrid preset) | Pooled informed attempts, 3 per tester | 30% to 50% | Below 15% or above 70% |
| Length of winning fights | Rounds per won fight | 8 to 12 | Over 16 |
| Median turn time with a full swarm | Timestamps | Under 60 seconds | Over 90 seconds |
| Share of player damage from own effects | Damage by source | 5% to 30% | Over 35%, or under 3% |
| Share of deaths from one source | Cause of death | No source above 60% | Hazards above 60% |
| Hazard fires per fight | Event log | At least 2 | Under 1 on average |
| Win-rate lift of each ability and upgrade | Simulated loadout sweep | Under 25 points | Any above 40 points |
| Warden reaches the player | Event log | In most fights that reach round 7 | Dies before round 9 in most fights |

**Tuning knobs**

Every value below lives in a data file and can be hot reloaded.

| Knob | Default |
| --- | --- |
| Player HP, AP, movement | 8, 3, 3 |
| Sprint | 1 AP for 2 movement |
| Minion cap, egg cap, mine cap | 5, 3, 4 |
| Mine damage | 3 explosive |
| Turret ammo and damage | 4 shots, 2 kinetic |
| Drone charges | 2 |
| Shield segment HP | 4 |
| Poison damage, duration, spread cap | 1, 3 rounds, 3 per round |
| Parasite damage and duration | 2, 3 rounds |
| Corrode per Acid Spit, and its duration | 2 stacks, rest of the fight |
| Corpse decay | 2 rounds |
| Egg timers (floor, corpse) | 1 round, 2 rounds |
| Hazard cooldown and prime delay | 3 rounds, 1 round |
| Gun, panel and vent damage | 2, 2, 1 |
| Guard HP and damage | 6, 3 |
| Medic heal | 2 |
| Flamer HP, armor and damage | 6, 1, 3 |
| Warden HP, armor, charge range, charge damage, Purge cooldown | 20, 2, 4, 4, 2 |
| Wave rounds | 1, 4, 7 |
| Arrival round | On for every wave |
| Loadout size | 8 abilities, 3 upgrades |
| Scrap per kill (stretch) | 1 (Warden 3) |

**Symptom to first knob**

| Symptom | First knob to try |
| --- | --- |
| Fights too easy | Raise Guard damage, shorten the gap between waves, lower the minion cap |
| Fights too hard | Raise player HP, lower Guard damage, raise the minion cap |
| Swarm snowballs | Lower the minion cap or corpse decay, raise egg cost |
| Poison dominates | Cut poison duration or the spread cap |
| Warden is a pushover | Raise HP or armor, or shorten the Purge cooldown |
| Hazards feel cheap | Lengthen the prime delay, or show the cooldown |
| Hazards never matter | Shorten cooldowns, or make hazards cover more tiles |
| No preset has a winning line | Lower Warden HP, then cut wave 3 to one Guard |
| Acid Spit is in every win | Make Corrode expire after 3 rounds |
| Parasite dominates | Cut Parasite damage to 1, or its duration to 2 rounds |
| Waves feel toothless on arrival | Keep the arrival round for wave 1 only |

The minion cap was never reached in the hand simulations, because AP ran out first. Check peak minion count in telemetry before using the cap as a knob.

**Headless simulation**

The simulator runs the core without a client and uses the same content files.

1. Random legal bot: finds crashes, infinite loops and exploits. It draws from a seeded generator.
2. Greedy bot: each action maximises its immediate preview score, which is a weak baseline. One action in ten is a random legal action from a seeded generator, so a batch is a spread of fights and not one fight repeated.
3. Scripted bots: command scripts that reproduce the earlier hand simulations for each preset, used as regression tests. Each is a single deterministic fight, run once per build.
4. Search bot (stretch): searches action sequences within a turn using the preview engine, to show the ceiling for a loadout.

The game has no randomness, so all variation comes from the bot's seed, which is stored with each fight. Batch reports run 1,000 seeds per preset for the random and greedy bots, and sweep random valid loadouts and tuning values to find dominant abilities and upgrades. Each report gives win rate, rounds, causes of death, damage by source and the win-rate lift of every pick, and exports to CSV. Running the same batch with the same seeds must give identical results.

## Build order

Build in eight milestones, each ending in something runnable and tested. The paper check in Loadout and upgrades comes before M0. The first playtest is at the end of M3 and uses the Tech kit against waves 1 and 2 only: the Warden is built in M3 but held out of that session, because the hazards and the armor answers arrive later. The debug overlay holds back wave 3, and clearing wave 2 counts as the win. Sizes are relative effort (S, M, L), not time estimates.

| Milestone | Scope | Acceptance criteria | Size |
| --- | --- | --- | --- |
| M0 Foundations | Core skeleton, state model, grid, content loader, round and phase loop, movement, symmetric line of sight, command log with content hash, placeholder client | The player can move on the Armory map. Line of sight tests pass, including symmetry. Replaying a command log reproduces the same positions | M |
| M1 Combat core | HP, damage types, armor, death, corpses, Sidearm, Guard AI, the event queue and once-per-chain bookkeeping, event stream, win and lose, result screen, preview for the Sidearm | A Sidearm-only player can win or lose against wave 1. Across 100 randomised states, the dry-run preview matches the real result every time | M |
| M2 Tech kit | Mines, turret, drone cycle, shield, grapple, Reload, the Minion phase, minion cap, intent lines | Each ability has tests. Friendly fire works (a mine hurts your own minion). The drone follows its cycle exactly. Minion intents are visible | L |
| M3 Enemies and waves | Medic, Flamer, Warden, the wave scheduler, arrival round, hidden timer, round cap, intercom feed and log | All three waves run end to end with a scripted player. A wave deals no damage in its arrival round. Flamer scoring, the Warden charge stopping on a body, and Medic healing match the spec in tests. Intercom lines fire on their triggers. First playtest: Tech kit, waves 1 and 2 | L |
| M4 Lab hazards | Gun, panel grid, vents, prime-then-fire, the value rule, cues, debug explanations | Table-driven tests cover every value rule case, including elite hold, soldier sacrifice and combined kill, plus a committed hazard hitting a baited soldier. Cues display. The debug overlay explains each decision | M |
| M5 Alien kit | Spit, Lunge, Spore Pod, Brood Egg on floor and corpses, basic, spitter and burster hatchlings, Parasite, poison and spread | Egg timers, caps and hatchling variants behave as specified. Poison spread respects its cap and dies out in a cluster. Parasite spawns a hatchling on host death | L |
| M6 Loadout and upgrades | Loadout screen, presets, the 12 upgrades through modifiers and hooks | Every upgrade has a test. The once-per-chain rule suppresses a loop built from Spore Burst and Acid Brood. Presets load correctly | M |
| M7 Instrumentation and balance | Telemetry, replay, headless simulator, bots, golden replays, first full playtest round | A batch run is reproducible. Golden replays pass for all presets. The first-time player round is complete and its metrics are recorded | M |
| Stretch | Scrap and Repair, the mender hatchling, Creep, the search bot, scalable text and remappable keys | Built only after M7, and only if a playtest finding calls for one | S each |

**Rules for every milestone**

- A milestone is done when its acceptance criteria pass, the new rules have automated tests, and the debug overlay can explain the new behaviour.
- Every number comes from data. A value hard-coded in logic is a bug.
- Update the spec when implementation forces a rule change, so the document stays the source of truth.

**Why this order**

The Tech kit is the most readable and testable, so it comes first and lets playtesting start early. That first session stops at wave 2, because both documents expect the Tech kit to struggle against the Warden and a first playtest should not be an unwinnable fight. The event queue and chain bookkeeping are built in M1, because the mines, drone chains and death triggers of M2 already depend on them. Hazards need enemies and minions to react to, so they follow. The Alien kit adds the most complex interactions (eggs, poison spread, corpses), so it arrives once the combat core is stable. Telemetry and the simulator are listed last, but their event stream and replay support should be built in from M0, because retrofitting determinism is painful.

## Risks and open decisions

The main risk is that the numbers and AI rules here come from hand simulation, so the first playtests may overturn some of them. The spec is built so those changes are data edits, not rewrites.

**Risks**

| Risk | Why it matters | Mitigation |
| --- | --- | --- |
| No in-scope build can win | Every hand-simulated win used Occult or live capture, and the enemies are the boosted versions tuned against those builds | The paper check before M0, then bot runs once the simulator exists, with Warden HP as the first knob |
| Hazards feel cheap or invisible | They are meant to be hard to read but not hidden | One-round prime delay, cues, a debug explanation, and the fallback of showing cooldowns |
| Hazards rarely fire | The lab holds whenever a soldier is in the area, and vents never fired in the hand simulations | The hazard-fires metric and its alarm, the bait rule, and shorter cooldowns |
| Poison hurts the player's own side too much | The universal friendly-fire rule lets it spread to hatchlings and the player | Hatchling poison immunity is the first fallback |
| The swarm snowballs again | It was the main balance problem in the simulations | Minion cap, corpse decay, egg cost and the knobs table |
| The Tech preset is unwinnable, not just hard | Even at armor 2, the Sidearm and turret deal 0 to the Warden without Piercing Rounds, and a mine deals 1 | Piercing Rounds restores full Sidearm and turret damage, and the drone ignores armor. Confirm in the paper check that this is a real path. Losing is acceptable, having no path is not |
| One ability dominates | Parasite deals 6 armor-ignoring damage for 1 AP and leaves a hatchling, against 2 for a Sidearm shot | The win-rate lift metric and the Parasite knobs |
| Turns are slow with large swarms | Previews and intents add clutter and thinking time | Fast-forward, the minion cap, and grouped intent drawing |
| Scope creep | Occult, capture and the run map are tempting | The out-of-scope list in Goals and scope |
| Rule ambiguity | Line of sight corners and simultaneous deaths cause disputes | Written tie-break rules and unit tests |
| Determinism breaks across platforms | Replays and bot results depend on it | Integer math only and golden replays |

**Changes from the design document**

The MVP makes some choices that differ from, or are new to, the design document. Review these first.

| Area | Design document | This spec | Reason |
| --- | --- | --- | --- |
| Branches | Tech, Alien and Occult | Tech and Alien only | Debt needs several rooms to test properly |
| Hazard triggers | Reactive, with panels armed by borrowing AP | AI-fired with a prime-then-fire cycle | There is no debt in the MVP, and AI control is the agreed default |
| AP as movement | Abilities convert AP into movement | A Sprint action, 1 AP for 2 movement | The simplest rule that still lets AP buy movement |
| Corpse eggs | A stronger hatchling with an inherited trait | One variant hatchling per corpse type, in 2 rounds: spitter and burster, with the mender as a stretch. The Warden's corpse cannot host | Keeps the trait list short |
| Poison spread | Capped spread | Spreads to all units, up to 3 new infections per round, each inheriting its source's remaining duration | Makes the cap concrete, keeps friendly fire universal, and stops clusters reinfecting each other forever |
| Enemy intents | Not specified | Always shown, recomputed after each action | Supports predictable chaos |
| Primed hazards | The lab will not fire through its own troops, but the player can bait it | The value rule is checked at priming, and a primed hazard is committed | Makes the Decided bait play possible |
| Wave arrival | Not specified | Units move but do not act in the round they arrive | Round 1 had no safe tile |
| Warden armor | 3 | 2 | At 3, every kinetic and explosive source in the game dealt 0 |
| Flamer | 8 HP, no armor | 6 HP, armor 1 | Exercises armor, Corrode and Piercing Rounds before round 7 |
| Floor eggs | One basic hatchling | Two basic hatchlings | Reason not recorded. Confirm it is intended |
| Minion commands | Optional AP commands (focus target, trigger special) | None. Placement only | Reason not recorded. Confirm it is intended |
| Armor | Reduces kinetic and explosive damage | Also reduces fire | Reason not recorded. Confirm it is intended |
| Barrier Shield | Can be pushed or pulled | Fixed once placed | Reason not recorded. Confirm it is intended |
| Corrode | Strips armor and shields | Strips armor only | Reason not recorded. Confirm it is intended |
| Enemy turns | Same movement and AP rules as the player | Move, then one action | Reason not recorded. Confirm it is intended |

**Open decisions for the implementation session**

- Engine, language and platform.
- Input scheme and keyboard shortcuts.
- Art direction. Placeholder shapes are fine for the MVP.
- Whether an empty room brings the next wave early.
- Whether to show hazard cooldowns.
- Whether player-side units should leave corpses.
- Whether Creep is included or left as a stretch.
- How many levels of undo for free movement.
- Exact enemy and hatchling spawn placement rules in crowded rooms.
- Whether a primed hazard commits. The default is that it does.
- Whether player-side objects draw the wall gun's fire. The default is that they do.
- Whether the arrival round applies to every wave or only to wave 1. The default is every wave.
- Loadout size: 8 of 11, or 6 of 11.
- Whether Corrode lasts the whole fight. The default is that it does.
- Which stretch items, if any, get built.

**After the MVP**

The recommended next addition is a small Occult slice: Borrowed Time with a visible payback forecast, a range-limited and cooldown-limited Fate Swap, and Hex. Both winning builds in the hand simulations depended on swaps and debt. Testing debt carrying between rooms needs a second room, so add a minimal two-room chain at the same time. After that, build the run map, events and meta-progression from the design document.

The companion document is [Lab Escape Roguelite: Design Doc v0.1](https://claude.ai/code/artifact/d8804d7d-d61e-412c-8d4e-6b06a5496111).

## Review changes

Revision 2 applies the review of Oct 2, 2026, which read this spec against Design Doc v0.1. A fix repairs a rule that did not work as first written. A clarification pins down something the first draft left open. A Proposed change is a design call and needs confirming.

| Change | Section | Kind | Why |
| --- | --- | --- | --- |
| Paper check before M0, with a matching risk row | Loadout and upgrades, Risks | Proposed | Every hand-simulated win used Occult or live capture, and the build nearest the Hybrid preset lost |
| Arrival round: a wave moves but does not act in the round it spawns | Armory Lockdown, Enemies | Proposed | Round 1 had no safe tile. Moving spawns and adding one pillar were checked and do not fix it |
| Warden armor 3 to 2 | Enemies | Proposed | At 3, every kinetic and explosive source in the game dealt 0, mines included, so Acid Spit was close to mandatory |
| Flamer to 6 HP and armor 1 | Enemies | Proposed | Armor existed on one enemy, so Corrode, Piercing Rounds and Acid Brood did nothing before round 7 |
| Primed hazards are committed | Lab hazards and intercom | Proposed | With a full recheck before firing, a hazard could only hit a soldier in the instant it killed the player, so the Decided bait play was impossible and "Acceptable losses" could never play on its own |
| Player-side objects draw the wall gun's fire | Lab hazards and intercom | Proposed | One shield segment in row 5 switched the gun off for the whole fight |
| Abilities and upgrades as code that reads data | Technical architecture and data | Proposed | The listed effect operations and triggers could not express 5 of the 12 upgrades |
| Stretch list, and the armored hatchling out of scope | Goals and scope, Build order | Proposed | None of these items helps answer the seven questions |
| First playtest stops at wave 2, and the event queue moves to M1 | Build order | Proposed | The Tech kit is expected to lose to the Warden, and M2 content already needs the queue |
| Shot-Triggered Mines: the Sidearm can target a mine | Loadout and upgrades | Fix | Nothing could hit a mine: mines do not block projectiles, and shots only hit units |
| Poison spread inherits remaining duration, in a fixed order | Core rules | Fix | Adjacent units reinfected each other as their timers ran out, so poison never ended in a cluster |
| Purge gives immunity to spread for the round | Enemies | Fix | A poisoned neighbour undid Purge in the same round |
| The Warden attacks an adjacent minion when it cannot advance, and its charge ignores allies and needs at least 1 tile | Enemies | Fix | It spawns beside its own two Guards, "a unit" included them, and a ring of hatchlings could stall it |
| Success criteria and metrics: win-rate lift, fight length split by outcome, counts in place of rates, hazard fires | Goals and scope, Test plan and tuning | Fix | Share of wins sat at 73% by construction, fight length mixed early losses with wins, and 5 to 8 testers cannot resolve a 20-point band |
| Bots take a seed, and deterministic bots run once | Test plan and tuning | Fix | 1,000 runs of a deterministic bot in a deterministic game are one fight repeated |
| Replays store the engine version and a content hash | Technical architecture and data | Fix | Hot-reloaded tuning invalidates command logs and golden replays |
| Timers and cooldowns tick in the round they start, with worked cases | Core rules | Clarification | The first draft did not say, and the answer changes egg, corpse and hazard timing by a round |
| Symmetric line of sight | Core rules | Clarification | A plain Bresenham trace differs by direction |
| A Guard needs a clear shot past its own allies, and picks its firing tile by a fixed rule | Enemies | Clarification | Guards checked sight only, so they fired into each other, and "the reachable tile" was not unique |
| Combined kill check computed once per priming step, and soldier defined | Lab hazards and intercom | Clarification | Step 2 depended on what other hazards would decide by the same rule |
| Vent gas is one hit of poison-type damage, not the Poison status | Lab hazards and intercom | Clarification | Follows the design document's "1 damage in a 3x3 area" |
| Parasite does not stack, the drone zaps at range 2 and cannot hit itself, Lunge is orthogonal, Grapple follows the sight line | Core rules, Player and abilities, Minions | Clarification, values Proposed | Each was unspecified |
| The mender targets hatchlings, undo stops at a triggered move, intercom lines queue | Minions, Core rules, Lab hazards and intercom | Clarification | Each was inconsistent or unspecified |
| Six unlisted differences from the design document added to the changes table | Risks and open decisions | Clarification | The table asks to be reviewed first, so it should be complete |

Two notes on the evidence behind these changes:

- The round-1 figures come from a short script of the Enemy phase under this spec's Guard rule, not from a build. Treat them as indicative until the M3 tests reproduce them.
- The first draft described the Warden's charge two ways: "within 4 tiles" and "up to 4 tiles". This revision reads range 4 as distance moved, so a target 2 to 5 tiles away can be charged. Confirm that reading.

## Changes since revision 2

| Date | Change | Why |
| --- | --- | --- |
| 2026-10-06 | Units that die in the same effect as the player are reported and leave corpses; their death effects and intercom lines are skipped. The loss itself is unchanged. | The engine stopped at the player's death (unit ID 0), so enemies killed by the same blast went unreported and telemetry undercounted kills in lost fights. |
