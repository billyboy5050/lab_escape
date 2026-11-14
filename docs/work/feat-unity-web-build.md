# The Unity web build

- Branch: `feat/unity-web-build`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/32
- Roadmap item: M13, "The three builds" (the web build's half), with "Symbols and faces missing in the web build", "Web build size" and "Git LFS" under Smaller items
- Started: 2026-11-14
- Last updated: 2026-11-14, by Claude (Opus 5.5)

## Goal

Make the Unity web build good enough to replace the TypeScript web client in the browser: every symbol the interface uses draws, the type matches the Mac and Windows builds as closely as it can, the download is smaller, and previews stay fast. Done means a web build that plays a fight with no blank characters, a measured size and preview speed recorded in the implementation notes, and the roadmap items above deleted. Running the Windows build on a Windows PC is the other half of "The three builds" and needs the owner.

## Status

Waiting on the owner: everything is done and documented except playing the shipped faces with a real mouse and keyboard (CLAUDE.md's definition of done for `unity/`), and Codex's review.

## Done so far

- Two fonts shipped in `unity/Assets/Resources/Fonts`, in Git LFS, with their licences; `.gitattributes`; `.githooks/pre-push` runs `git lfs pre-push`.
- `Fonts.cs` uses them where the system has none of a face; with no system fonts at all every face falls back to DejaVu Sans Mono for symbols, and the root gets Unity's default face with that fallback. Barlow is drawn at 0.9.
- `FontTests` (4 tests). Unity EditMode 181 and PlayMode 68 pass; `npm run build`, `npm test` pass.
- Physics backend set to Unity's empty one: the web wasm is 1.09 MB smaller. Trimming the manifest was tried and saved nothing, so it was put back.
- Web build checked in the browser pane: arrow, en dash, curly quotes draw; names break between words; previews 8.3 ms median, 10.4 ms worst over 987 frames.
- README, CLAUDE.md, implementation notes and roadmap updated.

## Next steps

1. When the owner has played the shipped faces (`open unity/Builds/Mac/LabEscape.app --args -labNoSystemFonts`, or the web build) and is happy, delete this note, push, mark the PR ready and comment `@codex review`.
2. Answer every Codex finding on its thread.

## Decisions

- 2026-11-14: the owner chose to ship fonts used only where the system has none (over shipping them for every build, or writing the symbols in ASCII), so the Mac look passed at M12 is unchanged.
- 2026-11-14: Barlow Condensed SemiBold and DejaVu Sans Mono. DejaVu Sans Mono has every symbol the game writes, so it is the one fallback; Barlow lacks four of them.
- 2026-11-14: the manifest's unused modules stay listed, since removing them saved nothing (engine code stripping already drops them).

## Open questions

- None.

## Notes for whoever picks this up

- How the web build was checked: serve `unity/Builds/Web` with `Content-Encoding: gzip` on `.gz` files; to read text at full size, add `webglContextAttributes: { preserveDrawingBuffer: true }` to the built `index.html` and post `canvas.toDataURL()` to the server. Hover before clicking. Set the project back to the Mac target after a web build.
