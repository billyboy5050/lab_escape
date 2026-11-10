# Weight in the Unity fight's animation and presentation

- Branch: `feat/unity-combat-weight`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/23
- Roadmap item: none. It feeds the M12 owner checkpoint ("feels at least as good as the web version")
- Started: 2026-11-10
- Last updated: 2026-11-10, by Claude

## Goal

The owner asked for the game's presentation and animation to hook people from the first battle, with weight behind the animation, and chose the Unity client for the work (2026-11-10), since the web client retires at M13. Done means a hit reads as cause then effect (wind-up, release, flight, impact, reaction), bodies have mass (knockback, squash, falls, landings), heavy moments stop the clock and kick the camera, and a fight opens with an entrance instead of a cut.

No rule or content value changes: the engine, `content/` and the generated files are untouched, so the freeze until M13 holds.

## Status

Built, tested and documented. Two things stand between this and ready for review: the mouse has not been tried by hand, and Codex has not reviewed the latest push.

## Done so far

- `Staging` lays each frame's events out in time; `FightFlow` holds a frame for as long as its stage needs and writes each log line as its event is seen.
- The board plays cues as their moments come and is brought level with the state between frames (`BoardView.Level`).
- Figures on springs, with weight from their data; drop-in waves, toppling bodies, machines that break apart; hit-stop and a slow final blow on `BoardClock`; camera shake, kick and punch; effects that fall, bounce, scorch and throw light; damage numbers that are thrown; HP bars that keep what was lost for a moment; banners that are set down; the fight's entrance.
- `-labFilm` on the smoke run, which is how the motion was looked at and tuned.
- 49 EditMode and 11 PlayMode tests pass. The web build was made and played in a browser; the Mac build was played with the keyboard.
- Docs: README (Unity client), implementation notes ([how a frame is animated](../implementation-notes.md#how-a-frame-is-animated)), roadmap (two follow-ups), CLAUDE.md (one architecture point, two gotchas).

## Next steps

1. Play the Mac build with the mouse: move by clicking a tile, pick an action and click a target, click End turn. An agent can only do this with the game's window in front (CLAUDE.md, Gotchas). The mouse code did not change, and the PlayMode tests cover picking tiles from screen points, but the definition of done asks for it by hand.
2. Answer Codex's review of the latest push: check each finding, fix or decline it, reply and resolve.
3. Delete this note in the last commit, then `gh pr ready 23`.

## Decisions

- 2026-11-10: Unity client, not the web client (owner's answer). The web client is deleted at M13, so work there would not last.
- 2026-11-10: No sound in this branch. M13 ports it, and an agent cannot judge how it sounds. Each blow and landing now has its own moment, which is what the sound will hang on (roadmap, Smaller items).
- 2026-11-10: Timings are constants in the client's code (`Staging.cs` in beats, `Feel.cs` for the rest), as its effect durations already were. `rules.ui.secondsPerActor` stays the beat, so content keeps one knob for pace, and nothing in `content/` changes.
- 2026-11-10: A unit's weight comes from its data (HP plus twice its armor, against the player's), so new enemies need no animation numbers of their own.

## Open questions

- None for the owner beyond judging the feel, which is the M12 checkpoint's question anyway.

## Notes for whoever picks this up

- Close the Unity editor before any batch-mode command (README, Unity client).
- `unity/Assets/Scripts/Logic` must stay free of Unity code: its tests run in EditMode and could later run under `dotnet test`.
- To look at any change to the animation: build the Mac player, then run it with `-labSmoke <repo>/reports/winning-line-tech.json <folder> -labFilm 0,2,8,17,41,50`. In that line command 0 is the entrance, 2 a Sidearm shot, 8 an enemy phase with two kills, 17 a mine blast, 41 the Warden's charge and 50 the final blow.
- A web build leaves the project targeting the web. Run the editor once with `-batchmode -quit -buildTarget StandaloneOSX` afterwards.
