# Working in this repo

Instructions for AI agents and people working on Lab Escape. `AGENTS.md` is a symlink to this file, so every agent reads the same rules. If a pull request changes a command, the workflow or an invariant described here, it updates this file too.

## The project

Lab Escape: Armory Lockdown is a playable prototype of one room of a tactics roguelite, built to find out whether a single fight is fun before anything else is added. It has a deterministic TypeScript rules engine that runs headless in Node, a React and Vite web client, telemetry and replays, and a headless simulator with bots. Every number is data in `content/*.json`; behaviour is code.

Milestones M0 to M6 in the spec are done and M7's tooling is built, but M7 itself is not done: its acceptance criteria need the first full playtest round and its recorded metrics. The project is now in its playtest and tuning phase: see [docs/roadmap.md](docs/roadmap.md).

## Start here, every session

An agent can join at any point, so orient before changing anything.

1. `git fetch --prune`, then `git status` to see which branch you are on and whether it is behind.
2. See what is in flight: `gh pr list --state open` and `git branch -r --no-merged origin/main`.
3. Read [docs/roadmap.md](docs/roadmap.md): where things stand, what is next, and what is waiting on the owner.
4. If you are continuing a branch, read its note in [docs/work/](docs/work/), which says where the work stands and what to do next. To read another branch's note without checking it out: `git show origin/<branch>:docs/work/<note>.md`.
5. `npm install` (it also turns on the git hooks), then `npm test`, so you know the baseline is green before you change anything.

If the request matches a branch already in flight, continue that branch instead of starting a parallel one. If it would collide with someone else's open branch, say so before going on.

## Where things are written down

Each fact has one home. Link to it rather than copying it.

| What | Where |
| --- | --- |
| The rules of the game (source of truth) | [docs/spec.md](docs/spec.md) |
| How the code reads the spec where it is silent, defaults for its open decisions, simulator findings, known limitations | [docs/implementation-notes.md](docs/implementation-notes.md) |
| How to run, play, tune and simulate; the code layout | [README.md](README.md) |
| Status, what is next, decisions waiting on the owner | [docs/roadmap.md](docs/roadmap.md) |
| The one-page rules sheet handed to first-time players | [docs/rules-sheet.html](docs/rules-sheet.html) |
| Work in progress, one note per branch | [docs/work/](docs/work/) |
| How to work here, and the invariants | this file |
| Every number in the rules | `content/*.json` |
| Generated evidence | `reports/` (simulator reports, winning lines) and `golden/` (golden replays) |

## Commands

| Task | Command | Time |
| --- | --- | --- |
| Install dependencies and turn on the git hooks | `npm install` | |
| All tests | `npm test` | about 3 s |
| One test file, or tests by name | `npx vitest run tests/hazards.test.ts`, `npx vitest run -t "value rule"` | |
| Typecheck | `npm run typecheck` | about 1 s |
| Typecheck and static build (what CI runs, with the tests) | `npm run build` | about 2 s |
| Dev server on port 5173 | `npm run dev`, or the `lab-escape-dev` entry in `.claude/launch.json` for preview tools | |
| Simulator help | `npm run sim -- --help` | |
| Quick balance check while iterating (prints only) | `npm run sim -- batch --preset hybrid --seeds 100` | seconds |
| Re-record the golden replays | `npm run golden:record` | |
| Playtest metrics from saved telemetry | `npm run sim -- telemetry [DIR]` | seconds |

Times are for an 8-core machine.

## Regenerating generated files

The golden replays and the committed reports describe the current rules and content. After any content or rule change, regenerate all of them in the same pull request (about 3 minutes in total), then update the simulator figures in [docs/implementation-notes.md](docs/implementation-notes.md) if they moved.

| Files | Command |
| --- | --- |
| `golden/` | `npm run golden:record` |
| `reports/preset-*-greedy.*` and `reports/winning-line-*.json` | `npm run sim -- presets --seeds 1000` |
| `reports/random/` | `npm run sim -- presets --bot random --seeds 1000 --out reports/random` |
| `reports/sweep-8-greedy.*` | `npm run sim -- sweep --seeds 1000 --out reports` |

Content changes alter the content hash, so until this is done the golden test fails and the winning lines no longer load.

The rules sheet (`docs/rules-sheet.html`) is written by hand, but every value it copies from `content/` carries a `data-c` tag with its content path, and `tests/rulesSheet.test.ts` checks them. If a content change fails that test, change the sheet's text to match, keep the tags, and check that it still prints on one page (print preview in a browser, at A4 and at Letter).

## How work flows

These rules are mandatory. `main` is always green and changes only through merged pull requests.

1. **Never commit to `main`, merge into it or push to it.** Locally, the hooks in `.githooks/` refuse commits on `main`, pushes to it, and any other move of `main` (a merge, reset or rebase) except to exactly `origin/main`, which `git pull --ff-only` does. If one refuses a merge or rebase partway, undo it with `git reset --merge` or `git rebase --abort`. GitHub's branch protection refuses any push to `main` and merges only pull requests whose `check` CI job passes on a branch that is up to date with `main`. Never get around them: no `--no-verify`, no unsetting `core.hooksPath`, no editing the hooks to let yourself through. If `git config core.hooksPath` prints nothing, run `npm install`.
2. **One branch per task**, cut from an up-to-date `main`:

   ```bash
   git switch main && git pull --ff-only && git switch -c <type>/<short-slug>
   ```

   Types: `feat`, `fix`, `tune` (content values only), `docs`, `test`, `refactor`, `chore`. Make the slug specific: `feat/tester-labels`, `fix/grapple-pin-chain`. If your tool already created a branch or worktree for the session, keep it and name the note after that branch.
3. **Write the work note first.** The branch's first commit copies `docs/work/_template.md` to `docs/work/<branch name with / as ->.md` and fills in the goal. Push it and open a draft pull request at once (`gh pr create --draft`), so the work is visible to everyone else.
4. **Keep the note current.** Rewrite its Status and Next steps whenever they change, and always before you stop: at the end of a session, at a handoff, when blocked, or when running low on context. Commit and push it. The test is that someone with no context could carry on from the note alone.
5. **Commit small and often.** Each commit is one coherent change, passing typecheck and tests where it can, with an imperative subject that says what changed ("Add tester ID to telemetry folder names"). Push at least at the end of every session; unpushed work is invisible to others and can be lost.
6. **Stay in scope.** Problems you find along the way go into [docs/roadmap.md](docs/roadmap.md) or the pull request description, not into this branch.
7. **Stay current with `main`.** If `main` moves, merge it into your branch (or rebase, if nobody else has the branch) and rerun the tests before asking for review. Never force-push a branch someone else has worked on.
8. **Finish the job** with the definition of done below, then mark the pull request ready (`gh pr ready`).
9. **Ask Codex to review every pull request.** Codex reviews when a pull request is opened or marked ready, but a draft or a later push is not reviewed on its own. Once the branch is pushed and the pull request is open, comment `@codex review`, and comment it again after every later push (a fix, a merge from `main`, an edit the owner asked for). Treat each finding as a claim to check: trace a realistic path to the failure, fix it in this branch if the path is real, and reply on its thread and resolve it either way (one line saying why, when you decline). A pull request is ready to merge when its latest push has a Codex review with no open findings.
10. **Merging is the owner's decision.** Do not merge a pull request, turn on auto-merge or push to `main` unless the owner asks for it in the current conversation. After a merge, delete the branch.
11. **Picking up someone else's branch:** read its note, check what it holds with `git log origin/main..origin/<branch>`, and add yourself to the note's "Last updated" line. Do not start a competing branch for the same task.

## Definition of done

A pull request is ready for review when every line that applies is true.

- `npm run build` and `npm test` pass. CI runs both on every pull request.
- New or changed rules have tests, and the debug overlay can explain any new behaviour (spec, "Rules for every milestone").
- No number is hard-coded in logic. Values live in `content/`, and any field the code reads with `!` is listed in `src/content/required.ts`.
- **Content changed:** every generated file is regenerated (see "Regenerating generated files"), and the simulator figures in the implementation notes are updated if they moved.
- **Rule behaviour changed:** see "Changing a rule" below.
- **UI changed:** checked in the browser on the dev server, in light and dark themes, with the keyboard shortcuts still working.
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

- `step(content, state, command)` returns a new state and the events it produced. The engine has no rendering, input, clock or randomness. The client animates the events, and the simulator consumes the same stream.
- Previews and enemy intents run the same engine on a copy of the state (`src/preview`). The UI never re-implements a rule.
- Abilities and upgrades are small code behaviours keyed by ID (`src/effects`) that read every number from content. The spec's "Abilities and upgrades in code" explains why this is code and not a data language.
- Imports point one way: `util` and `content` at the bottom, then `state`, `rules`, `effects` and `ai`, `engine`, then `preview` and `telemetry`, with `sim` and `client` on top. Never import upward; `rules` must not import `engine`, for example.

## Invariants

Breaking one of these breaks replays, previews or the balance data, often silently.

1. **Determinism.** The engine (`state`, `rules`, `effects`, `ai`, `engine`, `preview`, `content`) uses no `Math.random`, no clock and no floating-point arithmetic in rules: positions and damage are integers. Nothing depends on hash or iteration order, and ties resolve by explicit orders (the spec and the implementation notes list them, with unit ID last). Bots get randomness only from the seeded generator in `src/sim/rng.ts`. The same batch with the same seeds gives identical results whatever the worker count; a test checks it.
2. **Numbers are data.** A value hard-coded in logic is a bug (spec, Build order).
3. **Content is validated before use.** `validateContent` must pass, and hot reload rejects invalid content and keeps the old values. A collection or block the game reads without a fallback is a reported problem, never an empty default.
4. **Replays are tied to the engine version and the content hash.** Any content change alters the hash, so the golden replays and `reports/winning-line-*.json` stop loading until they are regenerated. A rule change that alters what an existing command log produces must bump `ENGINE_VERSION` in `src/state/state.ts`, so old replays (including playtest telemetry) are refused instead of silently replaying differently.
5. **A failing golden test means behaviour changed.** If the change is intended, re-record with `npm run golden:record` and give the reason in the commit message. If it is not intended, it is a bug. Never re-record just to make the test pass.
6. **Previews match results.** A test checks this over 100 random states. Because previews call the engine, they follow rule changes on their own; never special-case a rule inside a preview.
7. **Untrusted input stays checked:** saved preferences in local storage, replay files (including `?replay=` URLs) and the dev server's telemetry paths (`src/dev/telemetryPath.ts`). Keep the shape checks when you touch them.

## Changing a rule

The spec is the source of truth, and the spec itself says to update it when implementation forces a rule change.

1. Check with the owner first if the rule is marked Proposed, or is listed under the roadmap's "Decisions waiting on the owner".
2. Change the rule where the spec states it, and add a row to a "Changes since revision 2" table at the end of the spec saying what changed and why (create the table with the first change).
3. Update the code and its tests, remove or rewrite any implementation note the change settles, and correct the [rules sheet](docs/rules-sheet.html) if it states the rule.
4. Bump `ENGINE_VERSION`, regenerate every generated file (see "Regenerating generated files"), and update the figures in the implementation notes.

## Tuning

- Tuning changes only `content/` and goes on a `tune/` branch.
- Gather evidence with the simulator: `npm run sim -- tune --param units.warden.hp --values 12,16,20`, then `presets --seeds 1000` for the values you keep. Put the before and after figures in the pull request.
- The greedy bot looks one action ahead and shows the ceiling for a careful player, not how people play. Tune on bot figures alone only when the owner asks; otherwise wait for playtest evidence (see the roadmap).

## Ask the owner before

- merging, or anything that changes `main` directly;
- changing repository settings (branch protection, secrets, Actions);
- changing a spec rule, a Proposed design call or a default for an open decision;
- adding a dependency (the app has two at runtime, React and React DOM);
- deleting content, reports or replays you did not generate in this task.

Do not ask about things this file or the docs already answer, or about routine choices inside your branch's scope.

## Gotchas

- `telemetry/` at the top level is dev-server output and ignored by git; `src/telemetry/` is source.
- `presets` writes to `reports/` unless given `--out`, even with `--set` overrides or another bot, so an experiment can overwrite the committed reports. Give exploratory runs `--out` with a scratch folder. Per-fight CSVs are ignored by git; text reports, stats JSON, the lift CSV and winning lines are committed.
- A fight that throws writes its command log to `reports/error-seed-<N>.json`. That is a bug repro, not a report: turn it into a test or attach it to the fix, and do not commit it under `reports/`.
- A test that plays whole bot fights needs `BOT_FIGHT_TIMEOUT_MS` from `tests/helpers.ts` as its timeout. Such tests take a second or two locally but several times that on the CI runner, past Vitest's 5 s default.
- Saving a content file while the dev server runs restarts the current fight. That is deliberate: one fight log never mixes two sets of values.
- `npm test` prints a Vite warning about an extensionless import in `vite.config.ts`. It is harmless for now and listed in the roadmap.
- Enter and Space behave differently when a button has focus. That logic lives in `src/client/actionKeys.ts` and is tested; keep keyboard handling there and in `src/client/screens/FightScreen.tsx`.
