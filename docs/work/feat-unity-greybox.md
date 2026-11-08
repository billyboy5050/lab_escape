# Unity fight in greybox

- Branch: `feat/unity-greybox`
- Pull request: not opened yet
- Roadmap item: M12: Unity fight in greybox
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude

## Goal

A full Armory fight plays in Unity on a fixed isometric camera, with previews, intents and a HUD, driven by the C# rules core in `core/Core`. Everything is greybox: no bought or binary art. Done means the fight plays start to finish in the editor and in a Mac build, and the owner can judge whether it feels at least as good as the web version (the M12 checkpoint in [phase-2.md](../phase-2.md#milestones)).

## Status

In progress: the empty Unity project exists; nothing is built on it yet.

## Done so far

- Confirmed Unity 6000.6.8f1 runs headless and is licensed, and Git LFS is installed.

## Next steps

1. Make `core/Core` a local Unity package and get it compiling in `unity/`.

## Decisions

- 2026-11-08: the project lives in `unity/`, as [phase-2.md](../phase-2.md#the-move-to-unity) lays out.

## Open questions

- The Windows build module is not installed. M12 does not need it; M13 does.

## Notes for whoever picks this up

Unity's editor: `/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity`.
