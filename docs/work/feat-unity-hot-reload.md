# Content hot reload in the Unity client

- Branch: `feat/unity-hot-reload`
- Pull request: <link, once opened>
- Roadmap item: M13, part 1, "Content hot reload"
- Started: 2026-11-13
- Last updated: 2026-11-13, by Claude

## Goal

Reload `content/` while the Unity game runs, as the web client does: a valid change restarts the current fight with the new values, and invalid content is rejected with its reason while the old values stay (CLAUDE.md, invariant 3). Done when the editor and a Mac build started with `-labContent <folder>` both do this, with tests, and the README and implementation notes describe it.

## Status

In progress: the branch has just been cut.

## Done so far

- Plan agreed with the owner.

## Next steps

1. `GameContent.Folder`, `-labContent` and `Logic/ContentWatch.cs` with its EditMode tests.
2. Wire the watcher into `FightDirector`, then the HUD's toast length and the debug overlay's content line.
3. PlayMode tests, docs and roadmap, then verify in a Mac build.

## Decisions

- 2026-11-13: poll file stamps twice a second instead of using `FileSystemWatcher`, which is unreliable under Mono on macOS and needs a thread.
- 2026-11-13: the web build does not hot reload: it has no folder to read.

## Open questions

- None.

## Notes for whoever picks this up

- The editor reads the repository's `content/`; a player reads `Assets/Resources/Content`, a copy the build step makes and git ignores.
