# Presentation polish for the Unity fight

- Branch: `feat/unity-presentation-polish`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/24
- Roadmap item: "Sound on the Unity fight's cues" (Smaller items, now deleted); the rest is new, ahead of the M12 owner checkpoint
- Started: 2026-11-12
- Last updated: 2026-11-12, by Claude (Opus 5.5)

## Goal

Quick fixes that close the gap between the Unity fight's presentation and the web client's before the owner's M12 checkpoint, after the combat-weight pass (PR #23). The owner asked for the fixes that make the biggest difference, and chose on 2026-11-12 to pull sound forward from M13. No rule or `content/` change.

## Status

Built and verified by tests, smoke screenshots and filmed frames. Waiting on the owner's hand-played check with a real mouse and keyboard (the definition of done for `unity/`), and on Codex's review of the latest push.

## Done so far

- All eight items, one commit each: tile coordinates; the web client's faces from the system; panel frames and keycaps; the room's backdrop and contact shading; soft flashes; the end card's figures; rules on hover and less repeated text; the web client's sound on the cues.
- What lasts is in the docs: [the fight's presentation](../implementation-notes.md#the-fights-presentation), the README's Unity client section, the roadmap and CLAUDE.md.
- Unity tests: 64 EditMode and 14 PlayMode pass. `npm run build` and `npm test` pass. Codex found nothing in the first push (`ee0cbf3`).

## Next steps

1. The owner plays a fight in the Mac build or the editor, with sound on: check the type, the room, the coordinates, the Scanner on hover, the end card, and that each sound lands on its moment and M mutes.
2. Answer any Codex findings on the latest push.
3. Delete this note in the last commit and mark the pull request ready.

## Decisions

- 2026-11-12: fonts come from the OS, as the web client's do (`--display` and `--mono` in `src/client/styles.css`), so the project still keeps no font files. The web build has no system fonts and keeps Unity's default.
- 2026-11-12: the owner chose to include sound in this branch rather than leave it for M13.
- 2026-11-12: a corpse being placed is silent, unlike the web client, because its unit's death is heard at the same moment; the end of the fight sounds with its card, so a skip still ends with it.

## Open questions

- None.

## Notes for whoever picks this up

- Close the Unity editor before batch-mode tests or builds (README, Unity client).
- The filmed smoke run notes each sound and the output level in `cues.txt`; that is how the timing was checked.
