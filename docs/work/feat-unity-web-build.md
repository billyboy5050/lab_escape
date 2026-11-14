# The Unity web build

- Branch: `feat/unity-web-build`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/32
- Roadmap item: M13, "The three builds" (the web build's half), with "Symbols and faces missing in the web build" and "Web build size" under Smaller items
- Started: 2026-11-14
- Last updated: 2026-11-14, by Claude (Opus 5.5)

## Goal

Make the Unity web build good enough to replace the TypeScript web client in the browser: every symbol the interface uses draws, the type matches the Mac and Windows builds as closely as it can, the download is smaller, and previews stay fast. Done means a web build that plays a fight with no blank characters, a measured size and preview speed recorded in the implementation notes, and the roadmap items above deleted. Running the Windows build on a Windows PC is the other half of "The three builds" and needs the owner.

## Status

In progress: branch and note created.

## Done so far

- 

## Next steps

1. Run `npm install` and `npm test`, then `dotnet test core/Tests`, to confirm the baseline.
2. List every non-ASCII character the Unity interface draws (`unity/Assets/Scripts`, `unity/Assets/UI`) and how `Fonts.cs` picks faces.

## Decisions

- 

## Open questions

- Ship a font (the project's first binary asset, which brings Git LFS) or write the symbols in plain ASCII?

## Notes for whoever picks this up

- How to build, serve and drive the web build from an agent session: the build's files are gzip-compressed with no fallback, so serve them with `Content-Encoding: gzip`; hover before clicking in the browser pane; run the editor once with `-buildTarget StandaloneOSX` afterwards.
