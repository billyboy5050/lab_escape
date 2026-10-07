# Roadmap

Where the project stands, what comes next, and what is waiting on a decision. Work in progress is not tracked here: it lives in open pull requests and in [`docs/work/`](work/), one note per branch.

Keep this file current. A pull request that finishes an item deletes it here, and one that turns up new work adds it; git history keeps the record of what was done. Order within "Up next" is priority order.

Last reviewed: 2026-10-07.

## Where things stand

The code for the MVP in the [spec](spec.md) is built and merged (PR #1), and typecheck, build and all tests pass. M7 still needs its first-time player round, so not every milestone is done.

| Milestone | Status |
| --- | --- |
| M0 Foundations | Done: grid, symmetric LOS (with the spec's 88-pair check), content loader with validation and hash, phase loop, movement, command log and replay |
| M1 Combat core | Done: damage types, armor, deaths and corpses, Sidearm, Guard AI, event queue and once-per-chain rule, win and loss, result screen, previews (verified against 100 random states) |
| M2 Tech kit | Done: mines, turret and Reload, drone cycle, shields, grapple, minion phase and cap, intents |
| M3 Enemies and waves | Done: Medic, Flamer, Warden, waves with the arrival round, hidden timer, round cap, intercom feed and log |
| M4 Lab hazards | Done: gun, panel grid, vents, prime-then-fire, the value rule with the combined kill check, cues, debug explanations |
| M5 Alien kit | Done: Acid Spit, Lunge, Spore Pod, Brood Egg on floor and corpses, basic, spitter and burster hatchlings, Parasite, poison and spread |
| M6 Loadout and upgrades | Done: loadout screen, presets, all 12 upgrades |
| M7 Instrumentation and balance | Tooling done (telemetry, replay, simulator, bots, golden replays), the rules sheet written, and the developer round (spec round 1) played and recorded. Not done: the first-time player round and its recorded metrics, which M7's acceptance criteria require |
| Stretch | Not built: scrap and Repair, mender hatchling, Creep, search bot, scalable text and remappable keys |

The simulator's findings (greedy bot, 1,000 seeds per preset) are in [implementation-notes.md](implementation-notes.md#what-the-simulator-says-greedy-bot-1000-seeds-per-preset). In short: Tech wins 72%, Alien 46%, Hybrid 83%; the paper check passes; Hybrid's friendly-fire share (66%) and the sweep's (54%) are over the spec's 35% alarm; the Warden reaches the player in only 3% to 28% of fights that reach round 7; Alien sees 1.9 hazard fires per fight against a target of 2.

The developer round is in [implementation-notes.md](implementation-notes.md#what-the-developer-playtest-round-says-spec-round-1). In short: 21 fights on 2026-10-03 and 2026-10-07, 2 wins (Alien and Hybrid); the owner enjoyed it, liked Alien best, found Hybrid easiest and Tech hard to get a footing in. In the four fights reviewed (two Tech, one Alien, one Hybrid), friendly fire was 0% of the player's damage, median turns took 14 to 32 seconds, the Scout Drone was never deployed, and Tech was played with few turrets, unlike the bot.

## Up next

The spec's next step is playtesting (its "Test plan and tuning" section). The tooling for the rounds is built; the rounds themselves are run by the owner.

1. **First-time player round** (owner; spec round 2): 5 to 8 people, one at a time, with the printed [rules sheet](rules-sheet.html) only, screen and telemetry recorded, and the three questions after each attempt.
2. **Informed player round** (owner; spec round 3): the same people after watching a replay of one of their losses, 3 attempts each on the Hybrid preset.
3. **Tune against the alarms once play confirms them.** Do not tune on bot figures alone: the greedy bot plays one action ahead and is a ceiling, not a player. When a playtest confirms an alarm, use the spec's "Symptom to first knob" table, change values in `content/`, and include before and after simulator reports in the pull request. Candidates today:
   - **Tech's difficulty:** the owner found it hard to get a footing in round 1, while the bot wins 72% by placing 10.5 turrets a fight. The owner will revisit it.
   - **Friendly fire:** the bot's Hybrid alarm (66%) did not show in round 1. All four fights reviewed, the Hybrid win included, were at 0%, which is under the spec's 3% alarm instead.
   - **The Scout Drone:** never deployed in the three round 1 fights whose loadout had it, so its upgrades never came into play.
   - **The Warden** rarely reaching the player (bot 3% to 28%; 1 of 4 in round 1).
   - **Hazard fires on Alien** (bot 1.9 a fight; 3 in the round 1 Alien win).

## Decisions waiting on the owner

Agents should not settle these on their own. Ask, and record the answer in the spec or the notes.

- **Interpretations to confirm.** Every item under "Interpretations to confirm" in [implementation-notes.md](implementation-notes.md#interpretations-to-confirm). Once confirmed, fold them into the spec as clarifications, since the spec is the source of truth; anything overturned becomes a rule change (see CLAUDE.md for what that involves).
- **Defaults for the spec's open decisions** in the same file (arrival round on every wave, loadout of 8, Corrode for the whole fight, hazard cooldowns hidden, and the rest).
- **Unexplained differences from the design document.** Six rows in the spec's "Changes from the design document" table say "Reason not recorded. Confirm it is intended": two hatchlings from a floor egg, no minion commands, armor reducing fire, Barrier Shield fixed once placed, Corrode stripping armor only, and enemies moving then taking one action.
- **The Warden's charge range.** The spec reads range 4 as distance moved, so a target 2 to 5 tiles away can be charged, and asks for that reading to be confirmed.
- **Golden replays.** They are greedy-bot fights frozen into scripts because the hand simulations were only summaries. Replace them with transcribed hand lines if those are ever written.
- **Stretch items.** Build one only when a playtest finding calls for it (spec, Build order).

## Smaller items

- **Record the playtest round in telemetry.** The report cannot tell a tester's first-time fights from their informed ones, so it uses one attempt number for everyone (`--informed-from`) and `--exclude-tester` for the developer's round. Testers who needed different numbers of first-time fights are then classified wrongly. Add a round field to the replay's `meta` (set on the loadout screen next to the Tester ID, or by a `?round=` parameter), check it in `recordProblems`, and use it in `src/sim/playtest.ts` in place of the cutoff.
- **Ability rules during a fight.** The action bar's tooltip shows an ability's name, or why it cannot be used, but never its rule text, so in a fight the full rule is only on the loadout screen and the rules sheet carries a short version. Add each ability's `text` to its action button's tooltip.
- **Finish the round 1 record.** Run `npm run sim -- telemetry --include-unlabelled` on the round 1 telemetry folder (it lives on the owner's machine, and round 1 fights have no tester ID) and add its figures to the [developer round](implementation-notes.md#what-the-developer-playtest-round-says-spec-round-1) in the implementation notes.
- **Readable hazard fires on the result screen.** The fight summary names a hazard by its map ID and an object by its number (`vent_b5→player`, `gun→object 5`), and the result screen prints them as they are. Name them as the board does ("Gas vent B5", "shield segment", "egg") in `src/telemetry/summary.ts` or where `src/client/screens/ResultScreen.tsx` prints them.
- **Tile-by-tile movement animation.** Multi-tile moves animate as one straight slide between the start and end tiles.
- **Phone-width layout.** The narrow breakpoint exists but has not been playtested.
- **Modifier keys on game shortcuts.** Only the action keys (`1` to `=`) ignore Ctrl, Cmd and Alt. The others fire with them held: Ctrl or Cmd+Z undoes a move, Ctrl or Cmd+E ends the turn, and Ctrl or Cmd+F and +L change the speed and toggle the log while the browser opens find or the address bar. Check for modifiers before every game shortcut in `src/client/screens/FightScreen.tsx` (and the backtick in `src/client/App.tsx`), with a tested helper in `src/client/actionKeys.ts`.
- **Vite's native config loader.** `npm test` warns that `vite.config.ts` imports `./src/dev/telemetryPath` without a file extension, which the native config loader (planned as Vite's default) will not accept. Adding `.ts` also needs `allowImportingTsExtensions` in `tsconfig.json` (allowed because it sets `noEmit`).

## After the MVP

From the spec, and out of scope until the playtests have answered its seven questions: a small Occult slice (Borrowed Time with a visible payback forecast, a limited Fate Swap, and Hex) with a minimal two-room chain to test debt carrying between rooms; then the run map, events and meta-progression from the design document.
