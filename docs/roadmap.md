# Roadmap

Where the project stands, what comes next, and what is waiting on a decision. Work in progress is not tracked here: it lives in open pull requests and in [`docs/work/`](work/), one note per branch.

Keep this file current. A pull request that finishes an item deletes it here, and one that turns up new work adds it; git history keeps the record of what was done. Order within "Up next" is priority order.

Last reviewed: 2026-10-05.

## Where things stand

The MVP from the [spec](spec.md) is built and merged (PR #1). Typecheck, build and all tests pass.

| Milestone | Status |
| --- | --- |
| M0 Foundations | Done: grid, symmetric LOS (with the spec's 88-pair check), content loader with validation and hash, phase loop, movement, command log and replay |
| M1 Combat core | Done: damage types, armor, deaths and corpses, Sidearm, Guard AI, event queue and once-per-chain rule, win and loss, result screen, previews (verified against 100 random states) |
| M2 Tech kit | Done: mines, turret and Reload, drone cycle, shields, grapple, minion phase and cap, intents |
| M3 Enemies and waves | Done: Medic, Flamer, Warden, waves with the arrival round, hidden timer, round cap, intercom feed and log |
| M4 Lab hazards | Done: gun, panel grid, vents, prime-then-fire, the value rule with the combined kill check, cues, debug explanations |
| M5 Alien kit | Done: Acid Spit, Lunge, Spore Pod, Brood Egg on floor and corpses, basic, spitter and burster hatchlings, Parasite, poison and spread |
| M6 Loadout and upgrades | Done: loadout screen, presets, all 12 upgrades |
| M7 Instrumentation and balance | Tooling done (telemetry, replay, simulator, bots, golden replays). Not done: the first full playtest round and its recorded metrics, which M7's acceptance criteria require |
| Stretch | Not built: scrap and Repair, mender hatchling, Creep, search bot, scalable text and remappable keys |

The simulator's findings (greedy bot, 1,000 seeds per preset) are in [implementation-notes.md](implementation-notes.md#what-the-simulator-says-greedy-bot-1000-seeds-per-preset). In short: Tech wins 72%, Alien 46%, Hybrid 83%; the paper check passes; Hybrid's friendly-fire share (66%) and the sweep's (54%) are over the spec's 35% alarm; the Warden reaches the player in only 3% to 28% of fights that reach round 7; Alien sees 1.9 hazard fires per fight against a target of 2.

## Up next

The spec's next step is playtesting (its "Test plan and tuning" section). The first four items make the playtest rounds possible to run and to measure; the rounds themselves are run by the owner.

1. **Tester and attempt labels in telemetry.** Rounds 2 and 3 report metrics per tester and pooled by attempt number, but a telemetry folder is named only `<time>-<preset>-<outcome>`. Add a tester ID (for example a `?tester=P3` URL parameter or a field on the loadout screen), store it with the attempt number in the replay's `meta`, and include it in the folder name.
2. **Telemetry report command.** A `npm run sim -- telemetry [DIR]` command that reads the `summary.json` files under `telemetry/` and prints the spec's playtest metrics against their targets and alarms: first attempts lost, rounds per lost fight, informed win rate pooled by attempt, length of winning fights, median turn time with a full swarm, friendly-fire share, deaths by source and hazard fires per fight. Check first whether the summary records enough to compute turn time "with a full swarm"; it may need the minion count per turn.
3. **One-page rules sheet** for first-time players (spec, playtest round 2: "a one-page rules sheet and no coaching"). It does not exist yet. Write it from the spec and the rule text in `content/abilities.json`, as a printable page in `docs/`.
4. **Developer playtest round** (owner; spec round 1): 5 to 10 attempts per preset to catch bugs and obvious balance failures. Telemetry saves itself while `npm run dev` runs. Record findings in [implementation-notes.md](implementation-notes.md) and turn bugs into items here.
5. **First-time player round** (owner; spec round 2): 5 to 8 people, one at a time, rules sheet only, screen and telemetry recorded, and the three questions after each attempt.
6. **Informed player round** (owner; spec round 3): the same people after watching a replay of one of their losses, 3 attempts each on the Hybrid preset.
7. **Tune against the alarms once play confirms them.** Do not tune on bot figures alone: the greedy bot plays one action ahead and is a ceiling, not a player. When a playtest confirms an alarm, use the spec's "Symptom to first knob" table, change values in `content/`, and include before and after simulator reports in the pull request. Candidates today: friendly fire on Hybrid, the Warden rarely reaching the player, hazard fires on Alien, and Tech leaning on turrets (10.5 per fight, because Guards shoot the nearest player-side unit).

## Decisions waiting on the owner

Agents should not settle these on their own. Ask, and record the answer in the spec or the notes.

- **Interpretations to confirm.** Every item under "Interpretations to confirm" in [implementation-notes.md](implementation-notes.md#interpretations-to-confirm). Once confirmed, fold them into the spec as clarifications, since the spec is the source of truth; anything overturned becomes a rule change (see CLAUDE.md for what that involves).
- **Defaults for the spec's open decisions** in the same file (arrival round on every wave, loadout of 8, Corrode for the whole fight, hazard cooldowns hidden, and the rest).
- **Unexplained differences from the design document.** Six rows in the spec's "Changes from the design document" table say "Reason not recorded. Confirm it is intended": two hatchlings from a floor egg, no minion commands, armor reducing fire, Barrier Shield fixed once placed, Corrode stripping armor only, and enemies moving then taking one action.
- **The Warden's charge range.** The spec reads range 4 as distance moved, so a target 2 to 5 tiles away can be charged, and asks for that reading to be confirmed.
- **Golden replays.** They are greedy-bot fights frozen into scripts because the hand simulations were only summaries. Replace them with transcribed hand lines if those are ever written.
- **Stretch items.** Build one only when a playtest finding calls for it (spec, Build order).

## Smaller items

- **Tile-by-tile movement animation.** Multi-tile moves animate as one straight slide between the start and end tiles.
- **Phone-width layout.** The narrow breakpoint exists but has not been playtested.
- **Vite's native config loader.** `npm test` warns that `vite.config.ts` imports `./src/dev/telemetryPath` without a file extension, which the native config loader (planned as Vite's default) will not accept. Adding `.ts` also needs `allowImportingTsExtensions` in `tsconfig.json` (allowed because it sets `noEmit`).

## After the MVP

From the spec, and out of scope until the playtests have answered its seven questions: a small Occult slice (Borrowed Time with a visible payback forecast, a limited Fate Swap, and Hex) with a minimal two-room chain to test debt carrying between rooms; then the run map, events and meta-progression from the design document.
