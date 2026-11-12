# Move the Unity project to 6.3 LTS

- Branch: `chore/unity-6.3-lts`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/25
- Roadmap item: "Unity version" under Decisions waiting on the owner, and the Windows Build Support module M13 needs
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

The owner decided on 2026-11-12 to stay on Unity 6.3 LTS, as the phase plan asks, and to start M13. The project was made on 6000.6.8f1, an update release, so this branch moves it back to 6000.3.28f1, the current 6.3 LTS, with Windows (Mono) and Web build support installed. Done means: `ProjectVersion.txt` and the package manifest are 6.3's, the EditMode and PlayMode tests pass on 6.3, the Mac, Windows and web builds succeed on 6.3, the fight plays in the Mac build, and the docs name 6.3 and the modules.

## Status

Moved and verified on 6.3. Waiting on the owner's play-through of the 6.3 Mac build, and on Codex's review.

## Done so far

- 6000.3.28f1 installed with Windows Build Support (Mono) and Web Build Support; 6000.6.8f1 is still installed.
- `ProjectVersion.txt`, the manifest (three 6.6-only modules dropped) and the lock file are 6.3's. `Builds.Windows` added.
- On 6.3: EditMode 64/64 and PlayMode 14/14; the Mac, Windows and web builds succeed; the Mac build plays the Tech winning line to victory with the system fonts and the sound; the web build runs in a browser through a first enemy wave with no console errors.
- Docs: README, roadmap, phase plan, implementation notes and a CLAUDE.md gotcha (delete `unity/Library` and `unity/Builds` after a version change).

## Next steps

1. The owner plays a fight in the 6.3 Mac build (`unity/Builds/Mac/LabEscape.app`), and, if a Windows PC is at hand, starts `LabEscape.exe` from `unity/Builds/Windows/` on it.
2. Answer Codex's findings, delete this note in the last commit, mark the pull request ready.

## Decisions

- 2026-11-12: the owner chose 6.3 LTS over staying on 6000.6.8f1, knowing it means moving the project back a version.

## Open questions

- None yet.

## Notes for whoever picks this up

- Close the Unity editor before batch-mode runs (README, Unity client).
- The 6.6 editor stays installed until this merges, so `main` can still be opened. Switching between them needs `unity/Library` deleted each time.
- The Windows build has been made but not run: no Windows PC here.
