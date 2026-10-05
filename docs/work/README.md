# Work in progress

One note per branch that has not been merged yet. A note lets anyone (another agent, the owner, or you after a break) pick the work up from the note alone.

## Rules

- **Name.** The branch name with `/` replaced by `-`: branch `feat/tester-labels` has the note `docs/work/feat-tester-labels.md`.
- **Create it first.** The branch's first commit copies [`_template.md`](_template.md) and fills in the goal. Push and open a draft pull request straight away, so the work shows in `gh pr list`.
- **Keep it current.** Rewrite Status and Next steps whenever they change, and always before you stop: at the end of a session, at a handoff, when blocked, or when running low on context. Commit and push the update. Append to Decisions; don't rewrite history there.
- **Write for a stranger.** Someone with no context must be able to continue from it. "Next steps" starts with an action specific enough to begin without reading anything else.
- **Delete it before merging.** Move what lasts into the permanent docs (README, implementation notes, spec, roadmap, CLAUDE.md), then delete the note in the branch's last commit. On `main` this folder holds only this README and the template; any other file means a branch was merged without cleaning up, and the next agent to notice should fix it.

## Finding work in flight

```bash
git fetch --prune
gh pr list --state open
git branch -r --no-merged origin/main
git show origin/<branch>:docs/work/<note>.md
```
