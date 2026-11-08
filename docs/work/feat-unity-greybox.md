# Unity fight in greybox

- Branch: `feat/unity-greybox`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/20 (draft)
- Roadmap item: M12: Unity fight in greybox
- Started: 2026-11-08
- Last updated: 2026-11-08, by Claude

## Goal

A full Armory fight plays in Unity on a fixed isometric camera, with previews, intents and a HUD, driven by the C# rules core in `core/Core`. Everything is greybox: no bought or binary art. Done means the fight plays start to finish in the editor and in a Mac build, and the owner can judge whether it feels at least as good as the web version (the M12 checkpoint in [phase-2.md](../phase-2.md#milestones)).

## Status

In progress: the fight plays to the end in a Mac build. Real mouse and keyboard input is not yet checked, and the docs are not yet written.

## Done so far

- `core/Core` is a local Unity package, and .NET build output moved to `core/.build/` so Unity does not import it. `dotnet test core/Tests` passed (578) after the move.
- `unity/`: animation queue, targeting and log text (`Assets/Scripts/Logic`), isometric board, figures and effects (`Board`), UI Toolkit HUD (`Hud`, `Resources/UI`), `FightDirector`.
- Unity tests passed, 18 EditMode and 3 PlayMode: the six golden event logs are identical under Unity's runtime, and `reports/winning-line-hybrid.json` plays to Victory on the real screen. They were run before the last visual fixes (figure colours, action button text, banner position, combat log spacing).
- A Mac build played the same line to a win in round 11 with content hash `08a289f83c157f`, and its screenshots were reviewed. The final build compiled but was not played or looked at.

## Next steps

1. Launch `unity/Builds/Mac/LabEscape.app` and check real input: hover previews, clicking a tile, picking an action by key and by click, E, Z, Esc, Space, F, I, L, wheel zoom and right-drag pan. Scripted runs set the pointer directly, so none of this has been exercised yet.
2. Check the final action bar and banners by eye, and try a smaller window (1280x720).
3. Rerun the Unity tests (commands below).
4. Docs: README (how to open, run and build `unity/`), the document and command tables in CLAUDE.md, `.gitattributes` for Git LFS if binary art is near, `docs/roadmap.md` (delete M12 once the owner has judged it), implementation notes.
5. Delete this note, mark the pull request ready, comment `@codex review`, and hand the owner the M12 checkpoint.

## Decisions

- 2026-11-08: the project lives in `unity/`, as [phase-2.md](../phase-2.md#the-move-to-unity) lays out.
- 2026-11-08: models and overlays are built from code and shaders are text, so the project has no binary assets yet. Git LFS is installed but not used so far.
- 2026-11-08: sound, the title, briefing and result screens, the debug overlay, telemetry saving and hot reload are left for M13, as the milestone table says. The start screen is a plain preset picker.
- 2026-11-08: one dark theme only for now, using the web client's dark colours.

## Open questions

- Unity 6000.6.8f1 is an update release; 6.3 is the current LTS, and phase-2.md asks for the LTS. Nothing here depends on the difference. The owner should say whether to stay or switch.
- The Windows build module is not installed. M12 does not need it; M13 does.

## Notes for whoever picks this up

Close the Unity editor before running these; batch mode cannot share a project with an open editor.

```bash
U=/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults /tmp/edit.xml
$U -batchmode -projectPath unity -runTests -testPlatform PlayMode -testResults /tmp/play.xml
$U -batchmode -quit -projectPath unity -executeMethod LabEscape.Client.Editor.Builds.Mac
```

A scripted fight with screenshots: `"unity/Builds/Mac/LabEscape.app/Contents/MacOS/Lab Escape" -labSmoke "$PWD/reports/winning-line-hybrid.json" <folder> -screen-fullscreen 0 -screen-width 1600 -screen-height 900`.
