# Working in this repo

Instructions for AI agents and people working on Lab Escape. `AGENTS.md` is a symlink to this file, so every agent reads the same rules. If a pull request changes a command, the workflow or an invariant described here, it updates this file too.

## The project

Lab Escape is a tactics roguelite, started as a playable prototype of one room, Armory Lockdown, built to find out whether a single fight is fun before anything else is added. It has a deterministic rules engine in plain C# (`core/`) that runs headless under .NET, a Unity client (`unity/`) for Mac, Windows and the web, telemetry and replays, and a headless simulator with bots. Every number is data in `content/*.json`; behaviour is code.

Milestones M0 to M7 in the spec are done: the owner closed playtesting as successful on 2026-11-08. The project is now in its full-game phase, which moved the game to Unity (M8 to M13) and now builds out runs, rooms, events, the Occult branch and three wings: see [docs/phase-2.md](docs/phase-2.md) for the plan and [docs/roadmap.md](docs/roadmap.md) for what is next. The TypeScript prototype, with its React web client, was retired at M13 on 2026-11-14; git history keeps it.

## Start here, every session

An agent can join at any point, so orient before changing anything.

1. `git fetch --prune`, then `git status` to see which branch you are on and whether it is behind.
2. See what is in flight: `gh pr list --state open` and `git branch -r --no-merged origin/main`.
3. Read [docs/roadmap.md](docs/roadmap.md): where things stand, what is next, and what is waiting on the owner.
4. If you are continuing a branch, read its note in [docs/work/](docs/work/), which says where the work stands and what to do next. To read another branch's note without checking it out: `git show origin/<branch>:docs/work/<note>.md`.
5. `dotnet test core/Tests` (its build also turns on the git hooks), so you know the baseline is green before you change anything.

If the request matches a branch already in flight, continue that branch instead of starting a parallel one. If it would collide with someone else's open branch, say so before going on.

## Where things are written down

Each fact has one home. Link to it rather than copying it.

| What | Where |
| --- | --- |
| The rules of the game (source of truth), with their clarifications and the decisions taken for the spec's open items | [docs/spec.md](docs/spec.md) |
| How the tooling reads the spec where it is silent, simulator and playtest findings, known limitations | [docs/implementation-notes.md](docs/implementation-notes.md) |
| How to run, play, tune and simulate; the code layout | [README.md](README.md) |
| Status, what is next, decisions waiting on the owner | [docs/roadmap.md](docs/roadmap.md) |
| The plan for the full-game phase: the move to Unity, its milestones and what the owner has decided | [docs/phase-2.md](docs/phase-2.md) |
| The one-page rules sheet handed to first-time players | [docs/rules-sheet.html](docs/rules-sheet.html) |
| Work in progress, one note per branch | [docs/work/](docs/work/) |
| How to work here, and the invariants | this file |
| Every number in the rules | `content/*.json` |
| Generated evidence | `reports/` (simulator reports, winning lines) and `golden/` (golden replays) |

## Commands

Everything outside Unity needs the .NET SDK (`brew install dotnet` on a Mac). The simulator's commands are all `dotnet run --project core/Sim -c Release -- <command>`.

| Task | Command | Time |
| --- | --- | --- |
| Build and run all tests (the build also turns on the git hooks) | `dotnet test core/Tests` | about 10 s |
| One test class, or tests by name | `dotnet test core/Tests --filter "FullyQualifiedName~HazardsTests"`, `dotnet test core/Tests --filter "Name~Golden"` | |
| Simulator help | `dotnet run --project core/Sim -c Release -- --help` | |
| Quick balance check while iterating (prints only) | `dotnet run --project core/Sim -c Release -- batch --preset hybrid --seeds 100` | seconds |
| Re-record the golden replays | `dotnet run --project core/Sim -c Release -- golden --record` | |
| Playtest metrics from saved telemetry | `dotnet run --project core/Sim -c Release -- telemetry [DIR]` | seconds |
| Check every committed report is current: regenerate each and compare it byte for byte (what CI runs, with the tests) | `dotnet run --project core/Sim -c Release -- conformance` | about 75 s |
| Unity tests and the Mac build, with the editor closed | The commands in the README's [Unity client](README.md#unity-client) section | under 1 min each |

Times are for an 8-core machine.

## Regenerating generated files

The golden replays and the committed reports describe the current rules and content. After any content or rule change, regenerate all of them in the same pull request (under 2 minutes in total), then update the simulator figures in [docs/implementation-notes.md](docs/implementation-notes.md) if they moved. Each command below follows `dotnet run --project core/Sim -c Release --`.

| Files | Command |
| --- | --- |
| `golden/` | `golden --record` |
| `reports/preset-*-greedy.*` and `reports/winning-line-*.json` | `presets --seeds 1000` |
| `reports/random/` | `presets --bot random --seeds 1000 --out reports/random` |
| `reports/sweep-8-greedy.*` | `sweep --seeds 1000 --out reports` |

Content changes alter the content hash, so until this is done the golden tests fail and the winning lines no longer load. CI also regenerates the reports and compares them with the committed files (`conformance`), so a report that is out of step fails there.

The rules sheet (`docs/rules-sheet.html`) is written by hand, but every value it copies from `content/` carries a `data-c` tag with its content path, and `core/Tests/RulesSheetTests.cs` checks them. If a content change fails that test, change the sheet's text to match, keep the tags, and check that it still prints on one page (print preview in a browser, at A4 and at Letter).

## How work flows

These rules are mandatory. `main` is always green and changes only through merged pull requests.

1. **Never commit to `main`, merge into it or push to it.** Locally, the hooks in `.githooks/` refuse commits on `main`, pushes to it, and any other move of `main` (a merge, reset or rebase) except to exactly `origin/main`, which `git pull --ff-only` does. If one refuses a merge or rebase partway, undo it with `git reset --merge` or `git rebase --abort`. GitHub's branch protection refuses any push to `main` and merges only pull requests whose `check` CI job passes on a branch that is up to date with `main`. Never get around them: no `--no-verify`, no unsetting `core.hooksPath`, no editing the hooks to let yourself through. If `git config core.hooksPath` prints nothing, run `dotnet build core/Tests`, which sets it (`core/Directory.Build.targets`).
2. **One branch per task**, cut from an up-to-date `main`:

   ```bash
   git switch main && git pull --ff-only && git switch -c <type>/<short-slug>
   ```

   Types: `feat`, `fix`, `tune` (content values only), `docs`, `test`, `refactor`, `chore`. Make the slug specific: `feat/tester-labels`, `fix/grapple-pin-chain`. If your tool already created a branch or worktree for the session, keep it and name the note after that branch.
3. **Write the work note first.** The branch's first commit copies `docs/work/_template.md` to `docs/work/<branch name with / as ->.md` and fills in the goal. Push it and open a draft pull request at once (`gh pr create --draft`), so the work is visible to everyone else.
4. **Keep the note current.** Rewrite its Status and Next steps whenever they change, and always before you stop: at the end of a session, at a handoff, when blocked, or when running low on context. Commit and push it. The test is that someone with no context could carry on from the note alone.
5. **Commit small and often.** Each commit is one coherent change, passing the build and tests where it can, with an imperative subject that says what changed ("Add tester ID to telemetry folder names"). Push at least at the end of every session; unpushed work is invisible to others and can be lost.
6. **Stay in scope.** Problems you find along the way go into [docs/roadmap.md](docs/roadmap.md) or the pull request description, not into this branch.
7. **Stay current with `main`.** If `main` moves, merge it into your branch (or rebase, if nobody else has the branch) and rerun the tests before asking for review. Never force-push a branch someone else has worked on.
8. **Finish the job** with the definition of done below, then mark the pull request ready (`gh pr ready`).
9. **Ask Codex to review every pull request.** Codex reviews when a pull request is opened or marked ready, but a draft or a later push is not reviewed on its own. Once the branch is pushed and the pull request is open, comment `@codex review`, and comment it again after every later push (a fix, a merge from `main`, an edit the owner asked for). Treat each finding as a claim to check: trace a realistic path to the failure, fix it in this branch if the path is real, and reply on its thread and resolve it either way (one line saying why, when you decline). A pull request is ready to merge when its latest push has a Codex review with no open findings.
10. **Merging is the owner's decision.** Do not merge a pull request, turn on auto-merge or push to `main` unless the owner asks for it in the current conversation. After a merge, delete the branch.
11. **Picking up someone else's branch:** read its note, check what it holds with `git log origin/main..origin/<branch>`, and add yourself to the note's "Last updated" line. Do not start a competing branch for the same task.

## Definition of done

A pull request is ready for review when every line that applies is true.

- `dotnet test core/Tests` and the `conformance` check pass. CI runs both on every pull request.
- **`unity/` changed:** the Unity tests pass, and the fight has been played in the editor or a Mac build with a real mouse and keyboard, its shortcuts included. CI has no Unity licence, so neither runs there.
- New or changed rules have tests, and the debug overlay can explain any new behaviour (spec, "Rules for every milestone").
- No number is hard-coded in logic. Values live in `content/`, and any field the code reads with `!` is listed in `core/Core/Content/Required.cs`.
- **Content changed:** every generated file is regenerated (see "Regenerating generated files"), and the simulator figures in the implementation notes are updated if they moved.
- **Rule behaviour changed:** see "Changing a rule" below.
- **Docs:** the README covers anything a user runs or sees; the implementation notes cover any new interpretation of the spec; the roadmap item is deleted and any follow-ups are added; this file is updated if commands, workflow or invariants changed; anything no longer true is deleted.
- **Work note:** its lasting content has moved into the docs above, and the note is deleted in the branch's last commit.
- **Codex review:** requested with `@codex review` on the latest push, and every finding fixed or answered, with its thread replied to and resolved.
- **Pull request description:** what changed, why, how it was verified, and anything the owner needs to decide. Use the template in `.github/pull_request_template.md`.

## Keeping the docs current

- Document as you go, in the same commit as the change it describes, not in a sweep at the end.
- When you change a fact, search for other mentions of it (`grep -rn "<term>" README.md CLAUDE.md docs`) and fix or remove them.
- Delete what is no longer true or needed: finished roadmap items, answered questions, notes for merged branches, fixed limitations, stale examples. Git history keeps the record, so nothing needs to be kept "just in case".
- New documents go in `docs/` and get a row in the table above and in the README's document table. Do not add documents at the top level.
- Write like the existing docs: plain, short sentences; British spelling (colour, behaviour), except game terms spelled as the spec spells them (armor); absolute dates (2026-10-05), never "yesterday" or "last week".

## Architecture in brief

The README's [code layout](README.md#code-layout) has the module table. The essentials:

- `Engine.Step(content, state, command)` returns a new state and the events it produced. The engine has no rendering, input, clock or randomness. The client animates the events, and the simulator consumes the same stream.
- Previews and enemy intents run the same engine on a copy of the state (`Previews`, in `core/Core/Preview`). The client never re-implements a rule.
- Abilities and upgrades are small code behaviours keyed by ID (`core/Core/Effects`) that read every number from content. The spec's "Abilities and upgrades in code" explains why this is code and not a data language.
- A fight takes place in a room: its floor plan, hazards, waves and round cap (`RoomDef`). Rules code reads the fight's room through `w.Room`, never `c.DefaultRoom` or `c.Rules.RoundCap`, which exist for the Armory's files. See [rooms as data](docs/implementation-notes.md#rooms-as-data).
- `core/Core` has a folder for each module, and references point one way: `Util` and `Content` at the bottom, then `State`, `Rules`, `Effects` and `Ai`, `Engine`, then `Preview` and `Telemetry`, with `core/Sim` and `unity/` on top. Never refer upward; `Rules` must not use `Engine`, for example. Keep `core/Core` to C# 9 and .NET Standard 2.1 with no Unity dependency, because Unity compiles the same source.
- The C# reproduces some of JavaScript's behaviour, such as its number formatting and sort order, because the TypeScript prototype wrote the first replays, golden logs and reports and the C# must read and write them identically. The implementation notes list these for the [rules core](docs/implementation-notes.md#the-c-rules-core) and the [simulator](docs/implementation-notes.md#the-c-simulator); keep them when you change that code.
- `unity/` is the Unity client, and it decides no rules: legal actions come from `Actions`, and previews and intents from `Previews`. Scenes stay empty, because `Bootstrap` builds everything from data and code. Models and overlays are generated and the interface is UXML and USS, so changes stay reviewable. Its sounds are synthesised (`Synth`) and its type comes from the system's fonts (`Fonts`), so its only binary files are the two fonts a machine without the system's needs, such as the web build, kept in Git LFS. See [the Unity client](docs/implementation-notes.md#the-unity-client).
- The Unity board lags the state on purpose while a frame plays. `Staging` lays a frame's events out in time, and the board shows each as its moment comes, so a unit keeps its figure and its HP until the blow lands. Between frames `BoardView.Level` brings the board level with the state, and a test checks that. Anything new the board shows must come from a cue or from `Level`, never from reading the state mid-frame. See [how a frame is animated](docs/implementation-notes.md#how-a-frame-is-animated).

## Invariants

Breaking one of these breaks replays, previews or the balance data, often silently.

1. **Determinism.** The engine (`State`, `Rules`, `Effects`, `Ai`, `Engine`, `Preview`, `Content` in `core/Core`) uses no `System.Random`, no clock and no floating-point arithmetic in rules: positions and damage are integers. Nothing depends on hash or iteration order (a `Dictionary` promises none, so ordered collections use `OrderedMap`), and ties resolve by explicit orders (the spec and the implementation notes list them, with unit ID last). Bots get randomness only from the seeded generator in `core/Sim/Rng.cs`. The same batch with the same seeds gives identical results whatever the worker count; a test checks it.
2. **Numbers are data.** A value hard-coded in logic is a bug (spec, Build order).
3. **Content is validated before use.** `ContentLoader.Validate` must pass, and hot reload rejects invalid content and keeps the old values. A collection or block the game reads without a fallback is a reported problem, never an empty default.
4. **Replays are tied to the engine version and the content hash.** Any content change alters the hash, so the golden replays and `reports/winning-line-*.json` stop loading until they are regenerated. A rule change that alters what an existing command log produces must bump `EngineVersion` in `core/Core/State/Types.cs`, so old replays (including playtest telemetry) are refused instead of silently replaying differently.
5. **A failing golden test means behaviour changed.** If the change is intended, re-record with `dotnet run --project core/Sim -c Release -- golden --record` and give the reason in the commit message. If it is not intended, it is a bug. Never re-record just to make the test pass.
6. **Previews match results.** A test checks this over 100 random states. Because previews call the engine, they follow rule changes on their own; never special-case a rule inside a preview.
7. **Untrusted input stays checked:** saved settings in the Unity client's `PlayerPrefs`, and replay files: the Unity client's `-labReplay`, the web build's `?replay=` and saved fights, which all pass `Telemetry.LoadReplay`, and the fights the simulator's `telemetry` and `replay` commands read. Keep the shape checks when you touch them.

## Changing a rule

The spec is the source of truth, and the spec itself says to update it when implementation forces a rule change.

1. Check with the owner first if the rule is marked Proposed, or is listed under the roadmap's "Decisions waiting on the owner".
2. Change the rule where the spec states it, and add a row to a "Changes since revision 2" table at the end of the spec saying what changed and why (create the table with the first change).
3. Update the code and its tests, remove or rewrite any implementation note the change settles, and correct the [rules sheet](docs/rules-sheet.html) if it states the rule.
4. Bump `EngineVersion`, regenerate every generated file (see "Regenerating generated files"), and update the figures in the implementation notes.

## Tuning

- Tuning changes only `content/` and goes on a `tune/` branch.
- Gather evidence with the simulator: `dotnet run --project core/Sim -c Release -- tune --param units.warden.hp --values 12,16,20`, then `presets --seeds 1000` for the values you keep. Put the before and after figures in the pull request.
- The greedy bot looks one action ahead and shows the ceiling for a careful player, not how people play. Tune on bot figures alone only when the owner asks; otherwise wait for playtest evidence (see the roadmap).
- The freeze on rules and content during the port ended with M13 on 2026-11-14. New rooms, enemies and rewards may take their first values from the simulator, because the owner has asked for that ([docs/phase-2.md](docs/phase-2.md)). Existing Armory values still wait for evidence.

## Ask the owner before

- merging, or anything that changes `main` directly;
- changing repository settings (branch protection, secrets, Actions);
- changing a spec rule, one of its clarifications or a decision it records;
- adding a dependency (the rules core has one, Newtonsoft.Json, and the Unity client uses only Unity's own packages);
- deleting content, reports or replays you did not generate in this task.

Do not ask about things this file or the docs already answer, or about routine choices inside your branch's scope.

## Gotchas

- `telemetry/` at the top level is where the Unity editor saves fights for the playtest report, and git ignores it.
- `presets` writes to `reports/` unless given `--out`, even with `--set` overrides, another bot or another room, so an experiment can overwrite the committed reports. Give exploratory runs `--out` with a scratch folder. Per-fight CSVs are ignored by git; text reports, stats JSON, the lift CSV and winning lines are committed.
- `golden/rooms.json` is hand-written test data, not game content: rooms of other sizes that the golden replays fight in. After editing it, re-record the golden replays.
- A fight that throws writes its command log to `reports/error-seed-<N>.json`. That is a bug repro, not a report: turn it into a test or attach it to the fix, and do not commit it under `reports/`.
- Saving a content file while the Unity editor plays restarts the current fight. That is deliberate: one fight log never mixes two sets of values. A build does the same with the folder it is given as `-labContent`.
- Binary files (`.gitattributes`: fonts so far) live in Git LFS, so install git-lfs and run `git lfs install --skip-repo` once. With `core.hooksPath` set, `git lfs install` cannot add its own hooks, so `.githooks/pre-push` runs `git lfs pre-push` and refuses to push without git-lfs. A clone made without it has pointer files in place of the fonts, and the Unity font tests fail.
- Unity imports everything under `core/Core`, so .NET's output goes to `core/.build/` (set in `core/Directory.Build.props`). The `.meta` files Unity writes in that folder are ignored by git; nothing refers to them.
- When the project changes Unity version, delete `unity/Library` and `unity/Builds` before building. On the move from 6.6 to 6.3, players built over 6.6's caches crashed or hung on start, though the tests passed.
- `Assets/Scenes/Fight.unity` and `Assets/Resources/UI/Panel.asset` are generated once and committed. The menu item Lab Escape > Recreate Scene And Panel remakes them.
- A change to the Unity fight's animation cannot be judged from a screenshot. Film it with `-labFilm` (README, Unity client) and read the frames in order.
- The Unity Mac build takes key presses while its window is in the background, but reads the mouse only when it is in front. An agent driving it without taking over the screen can check the keys and nothing else.
- The Unity client's buttons take no keyboard focus, so Space always skips and Enter always ends the turn. Which keys the game takes, and which it leaves to the system, is decided in `KeyRules` and tested; keep keyboard handling there and in `FightDirector`.
