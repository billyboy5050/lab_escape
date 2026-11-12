# Move the Unity project to 6.3 LTS

- Branch: `chore/unity-6.3-lts`
- Pull request: <link, once opened>
- Roadmap item: "Unity version" under Decisions waiting on the owner, and the Windows Build Support module M13 needs
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

The owner decided on 2026-11-12 to stay on Unity 6.3 LTS, as the phase plan asks, and to start M13. The project was made on 6000.6.8f1, an update release, so this branch moves it back to 6000.3.28f1, the current 6.3 LTS, with Windows (Mono) and Web build support installed. Done means: `ProjectVersion.txt` and the package manifest are 6.3's, the EditMode and PlayMode tests pass on 6.3, the Mac, Windows and web builds succeed on 6.3, the fight plays in the Mac build, and the docs name 6.3 and the modules.

## Status

In progress: installing 6000.3.28f1 with Unity Hub.

## Done so far

- 

## Next steps

1. When the install finishes, open the project in 6000.3.28f1 in batch mode, and fix what the package manager or the compiler refuses (the manifest lists built-in modules that 6.6 has and 6.3 may not).

## Decisions

- 2026-11-12: the owner chose 6.3 LTS over staying on 6000.6.8f1, knowing it means moving the project back a version.

## Open questions

- None yet.

## Notes for whoever picks this up

- Close the Unity editor before batch-mode runs (README, Unity client).
- The 6.6 editor stays installed until this merges, so `main` can still be opened.
