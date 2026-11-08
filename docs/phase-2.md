# Phase 2: the full game

The plan for the phase after the MVP, agreed with the owner on 2026-11-08. It moves the game to Unity, then builds a complete, replayable roguelite on top of it.

This file is the build plan. [spec.md](spec.md) stays the source of truth for the rules, and each milestone that changes a rule updates the spec in its own pull request. [Design Doc v0.1](https://claude.ai/code/artifact/d8804d7d-d61e-412c-8d4e-6b06a5496111) holds the design intent. [roadmap.md](roadmap.md) says which milestone is next.

## Why this phase

Outside testers found the game fun but want to see more before saying they would play a full game. They could not picture the roguelite loop, the tech trees did not read as separate, and the upgrades confused them. The owner's read is that most of this comes from choosing a whole build up front, and that earning it through a run would fix it. See [what the outside testers said](implementation-notes.md#what-the-outside-testers-said-2026-11-08).

**Goal.** Answer the question testers left open: would you play a full game? Put a complete, replayable roguelite in front of new people, built in Unity.

## The owner's decisions

| Question | Decision |
| --- | --- |
| Scope of a first full game | Three short wings, about 5 rooms each |
| How a run starts | Pick a branch: its weapon and one ability |
| Weapons and abilities | One signature weapon per branch, and upgrades become paths on abilities |
| Persistence between runs | A light version, late in the phase |
| Rooms | Beyond 8×8: larger rooms, hallways and bigger hazards |
| When events happen | Between battles, not between waves |
| How events decide upgrades | The player's reaction shapes the offer they then choose from |
| Engine | Unity |
| Rules engine | Ported to plain C#, with no Unity dependency |
| When to move | Port first, before any new feature |
| Look | 3D with a fixed isometric camera; the top-down view goes away |
| Platforms | Mac and Windows, plus a web build for testers |

## The move to Unity

| Today | Size | In Unity |
| --- | --- | --- |
| Rules core (`state`, `rules`, `effects`, `ai`, `engine`, `preview`, `content`) | 4,200 lines | C# library with no Unity dependency |
| Simulator and telemetry | 1,900 lines | C# console tool on the same library |
| Rule tests | Most of 3,700 lines | NUnit, which Unity's test tools also use |
| Web client and styles | 8,400 lines | Rewritten as the Unity client |
| `content/*.json` | | Unchanged, still the single source of every number |

```text
content/            shared JSON
core/Core/          rules library (also a local Unity package)
core/Sim/           headless simulator
core/Tests/         rule and conformance tests
unity/              the game: camera, board, HUD, audio
```

The library keeps to the C# and .NET level that the chosen Unity LTS supports, so the same source compiles in both places.

- **Proving the port.** The content hash must match, the golden event logs must be identical, and the same seeds must reproduce the committed simulator reports. That compares thousands of bot fights against the TypeScript engine.
- **Freeze during the port.** No rule or content value changes until M13, so the port never chases a moving target. TypeScript stays authoritative until the C# passes, and the web game stays playable for anyone testing meanwhile.
- **Determinism traps in C#.** 22 places rely on JavaScript's insertion-ordered maps and sets, and `Dictionary` gives no such promise. `structuredClone` needs explicit clones, and `Math.imul` needs unchecked integer maths. The hash must also sort keys as JavaScript does, by UTF-16 code unit.
- **Text-first Unity.** Unity scenes and prefabs merge badly and are hard to review. Keep scenes few, build rooms from data, and write the interface in UI Toolkit's text formats, so changes stay reviewable.
- **Fixed isometric camera.** Orthographic, no rotation, with pan and zoom for big rooms. Walls fade when they would hide something, as the isometric board does now. HP bars and intent chips draw above everything, and a unit's side is still shown by base shape as well as colour.

## The game

### A run

Title, choose a branch, choose a wing, about 5 rooms, run end, start again. Each run is one wing, as the design document decides. The target is about 25 to 35 minutes, measured from M16.

- The loop is **fight, event, door, fight**: 3 standard fights, 1 non-fight room and 1 finale.
- HP, debt and the build carry between rooms. Minions, mines and eggs do not in this phase.
- The current loadout flow stays as a Sandbox mode, for the simulator, presets and golden replays.

### Room shapes

Each wing has one layout of each shape, plus its finale. Doors show the shape, so choosing a door becomes a build decision: turrets want hallways, swarms want halls.

| Shape | First-pass size | What it is for |
| --- | --- | --- |
| Skirmish | 8×8 or smaller | Today's game: 1 or 2 waves, 4 to 6 rounds |
| Hall | About 12×12 | Waves from several directions, room for swarms |
| Hallways | About 14×8, non-rectangular | Choke points, long shot lanes, friendly fire |
| Hazard room | About 10×10 | One big hazard dominates the floor |

Bigger hazards keep prime-then-fire and the value rule, and add two shapes: `zone` (an exact tile list, such as a floor-wide panel field or a gas-filled corridor) and `beam` (hits everything in its lane, where the wall gun hits only the first thing). The room itself shows their extent, as panels and vents do now, so the "cue only, no danger zone" rule stays fair.

### Events decide upgrades

After each battle, before the door, the player gets an event. It is the only source of upgrades, and every event is the same shape:

```text
SEALED LOCKER
A requisition locker, still locked.

> Hack the lock    [safe]   needs Tech      pick 1 of 3 Tech rewards
> Force it open    [cost]   -2 HP           pick 1 of 4 from any branch
> Leave it         [safe]                   level up something you hold
```

- Events are data in `content/events.json`: an archetype, text, and options with a risk tag (`safe`, `cost`, `gamble`), optional `requires` (a branch held, or a flag) and a list of outcomes.
- The outcome list is small and fixed: `offer` (pick 1 of N, filtered by branch and by ability, level or fusion), `hp`, `debt`, `setFlag`. Later systems such as meters and room modifiers add outcomes, not a new structure.
- Safe and cost options show what they give. Gamble options show only that they are a gamble, and resolve from the run's seed.
- One reward-card screen serves every `offer`. Workshop and event-only rooms are larger events on the same system.
- Flags let one event set up a later one, as the design document decides, without building meters yet.
- It starts simple: 3 archetypes and about 6 events in M15, then each wing adds its own.

### Earning a build

A run starts with the branch's weapon and one ability. Offers are weighted toward branches the player holds, with the weights in content.

### Kit rework

- Each branch has one signature weapon in its own slot. Tech: Sidearm. Alien: Acid Spit. Occult needs a new 1 AP attack, because none of its six abilities is a basic attack.
- The flat upgrade list becomes level paths on abilities. 8 of the 12 upgrades already belong to one ability, and Piercing Rounds fits two.
- Shot-Triggered Mines, Spore Mines and Plague Drone need two abilities, so they become fusions, offered only once the player holds both.
- Proximity Mine, Acid Spit and Lunge have no upgrades yet and need new ones.
- One or two levels per ability is enough: a run gives about 4 rewards, so deeper tracks would never be seen.
- Each branch gets a recovery option. The design document has decided on no rest sites, and 8 HP now has to last 5 rooms.
- Presentation: branch colour, icon and name on every card and action, always with a shape as well as colour; a three-column Build screen; full rule text in the in-fight tooltip.

The watch items from the MVP are inputs to this work: see the roadmap.

### Occult

All six abilities, in two steps.

1. Debt and Borrowed Time (a payback forecast for 3 turns, debt carries between rooms, minimum 1 AP on entering a fight), Fate Swap (range and cooldown), Hex, and the `occult` damage type.
2. Blood Sigil, Offering (with a refund cap) and Spirit Thrall, with level paths and fusions.

Mass Swap, Grand Sigil and gambler contracts wait, since 5-room runs are too short for them.

### Three wings

From the design document's Proposed roster: three roles plus an elite each.

| Wing | Enemies | Signature entity | Finale |
| --- | --- | --- | --- |
| Weapons | Guard, Medic, Flamer, Warden, plus Sapper | Security node | Armory Lockdown, retuned |
| Xeno | Rusher, Brood tender, Spore sprayer, Hive guard | Loose specimen | New room |
| Ritual | Cultist, Binder, Swapper, High Priest | Binding circle | New room |

The Armory was balanced around 11 picks, and a run gives about 5 by its finale, so it gets a finale variant. The original stays in Sandbox.

### Light meta, late

Wing clears persist, and clearing all three shows a placeholder ending. Lost runs still advance use-based unlocks that widen the reward pool, plus a short memory fragment.

### Deferred

Alpha and Omega energy, fusion costs, part mods, scrap, Repair and Wrecked, cocoons and live capture, run meters, signature story outcomes, two bosses per wing, wing switching.

### Where this differs from the design document

| Area | Design document | This phase | Reason |
| --- | --- | --- | --- |
| Run length | A first guess of 10 to 15 rooms plus bosses | About 5 rooms per wing | Three short wings show more of the game sooner |
| Starting options | Engineer, Hybrid, Cultist archetypes | Pick Tech, Alien or Occult | Keeps the branches separate at the start |
| The loop | Event, battle, then a results pick | Fight, then an event whose reaction shapes the pick | One system for upgrades |
| Paying for levels | Alpha energy | Levels come from event offers | Too many systems to learn at once; Alpha is deferred, not dropped |
| Between rooms | Tech damage carries, and destroyed units become Wrecked | Minions do not carry | Wrecked needs scrap and Repair, which are deferred |
| Bosses | Two per wing | One finale per wing | Short wings |

## Milestones

Each is one branch and pull request. Sizes are relative effort.

| # | Milestone | Done when | Size |
| --- | --- | --- | --- |
| M8 | Close playtests, write the plan | Docs merged | S |
| | **Stage A: move to Unity** | | |
| M9 | C# rules core | Content hash matches; golden event logs identical; rule tests ported; previews match results over 100 random states | L |
| M10 | C# simulator and CI | Same seeds reproduce the committed reports; C# tests run inside the existing `check` job | M |
| M11 | Rooms as data, any size | Armory plays identically from the new room model; rooms of other sizes load and play headless | M |
| M12 | Unity fight in greybox | A full Armory fight plays on the isometric camera with previews, intents and HUD. **Owner checkpoint:** it feels at least as good as the web version | L |
| M13 | Unity parity, retire the web client | All screens, debug overlay, telemetry, sound, shortcuts, content hot reload; Mac, Windows and web builds run; preview speed acceptable on web | L |
| | **Stage B: game systems** | | |
| M14 | Room shapes: halls, hallways, big hazards | One room of each shape plays and stays readable; `zone` and `beam` have tests | L |
| M15 | Run skeleton with events | A 3-room run plays, events grant its upgrades, it resumes after a restart; same seed and choices give the same run | L |
| M16 | Run simulator | Per-room survival, HP on entry, pick rates, run length; identical at any worker count | M |
| M17 | Kit rework and branch presentation | Weapons, paths, fusions and Build screen in; spec updated, `ENGINE_VERSION` bumped | L |
| M18 | Occult core | Debt previews match results; forecast shows; debt carries between rooms | L |
| M19 | Occult complete | Offering and sigil loops suppressed in tests; an Occult start can win a room | M |
| | **Stage C: look and content** | | |
| M20 | Art slice | One room, the player and one enemy at final quality; style guide; web size budget held | M |
| M21 | Weapons wing | Four shapes, finale, its events and workshop. **Owner checkpoint:** the owner plays it end to end before the other two are built | L |
| M22 | Xeno wing | Enemies, AI, models, rooms, events, signature, finale | L |
| M23 | Ritual wing | Same | L |
| | **Stage D: loop and test** | | |
| M24 | Failure pays | Progress survives restarts and rejects corrupt saves | M |
| M25 | In-game onboarding, run export, tester round | Testers can send one file; round played | M |

Rooms as data comes before the Unity client, so the client is written once for many rooms. Armory's existing map and wave files stay where they are until M13, so the web game keeps working. M18 depends on M17, because Occult is authored on the new weapon and path schema. Greybox models carry gameplay through Stage B, so game work never waits on art.

## Technical notes for Stage B onward

- `FightSettings` gains room, starting HP and debt, so every fight stays a standalone replay.
- A `run` layer above `engine`: a pure `RunStep` mirroring `Step`, with its seeded generator in `util`.
- A `void` terrain makes non-rectangular rooms. The round cap moves from `rules` to per-room.
- Saves and progress are shape-checked (invariant 7 in CLAUDE.md).
- Weapons are abilities with a slot field, and level paths gate existing upgrade IDs, so upgrade behaviour code carries over.

## How success is measured (M25)

After one run, testers can explain the loop, name the three branches and say what each upgrade they took did. Most start another run after a loss, and most say they would play the full game. In the simulator, every branch and wing pair has a winning run and no reward lifts win rate by more than 25 points.

## Risks

- Stage A is five milestones with nothing new for players. The M12 checkpoint is where to stop if the 3D version does not feel right.
- 3D art is the largest new cost: roughly 25 models and 3 room kits. Agents can produce only greybox.
- **This repository is public.** Paid asset-store packs usually cannot be redistributed, so bought art needs a private home or a private repository before M20.
- A 3D web build risks download size and slow previews. Both are checked at M13.
- Content volume: 14 new rooms, 8 new enemies and 3 signature entities. Cut signature entities first, then one room shape per wing.
- Large rooms can drag, because moves and ranges were set for 8×8. Spawn points set the pace, and M16 measures rounds to first contact.
- HP attrition over 5 rooms is measured from M16.
- The recordings from the last tester round have not been received, so M25 adds a one-file export.

## What the owner needs to do

- **Before M9:** approve installing the .NET SDK, which is not on the owner's Mac (Homebrew works).
- **Before M12:** install Unity Hub and the current LTS editor with Mac, Windows and Web build support, and sign in with a Unity account. Git LFS is also needed then.
- **Before M20:** decide how to source 3D art (bought packs, commission or generated) and where it will be stored.

## What the owner has authorised

Approved on 2026-11-08 with this plan:

- **New dependencies** the port needs: Newtonsoft.Json (Unity's supported JSON package), NUnit, and Unity's own packages.
- **Bot-based first values for new content only.** New rooms, enemies and rewards have no player data, so they may be set from the simulator. Existing Armory values still wait for evidence, apart from the finale variant.
- **The rule changes named in this plan**, each recorded in the spec in its own pull request.

Still to be asked at the time: deleting the web client and the TypeScript code at M13. Still open: whether Alien gets Creep, so that all three branches have six abilities.
