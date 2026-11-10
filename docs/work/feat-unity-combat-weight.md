# Weight in the Unity fight's animation and presentation

- Branch: `feat/unity-combat-weight`
- Pull request: to be opened with this note
- Roadmap item: none. It feeds the M12 owner checkpoint ("feels at least as good as the web version")
- Started: 2026-11-10
- Last updated: 2026-11-10, by Claude

## Goal

The owner asked for the game's presentation and animation to hook people from the first battle, with weight behind the animation, and chose the Unity client for the work (2026-11-10), since the web client retires at M13. Today every event in a frame plays at the same instant: a shot, its damage number and the death all start together, units slide and fade, and the camera jitters on a sine wave. Done means a hit reads as cause then effect (wind-up, release, flight, impact, reaction), bodies have mass (knockback, squash, falls, landings), heavy moments stop the clock and kick the camera, and a fight opens with an entrance instead of a cut.

No rule or content value changes: the engine, `content/` and the generated files are untouched, so the freeze until M13 holds.

## Status

In progress: design settled, nothing built yet.

## Done so far

- Read the Unity client's animation path: `FightFlow` holds each frame for a flat `rules.ui.secondsPerActor`, `BoardView.Show` and `FxSystem.Play` start everything at once, `UnitView` has a small flinch, `IsoCamera.Shake` is two sine waves.

## Next steps

1. Add `unity/Assets/Scripts/Logic/Staging.cs`: a pure function from one frame's events to timed cues (wind-up, release, impact, death, landing) and the frame's hold time, with EditMode tests. `FightFlow` takes its hold time from it.
2. Rebuild `UnitView` on springs: knockback along the hit, squash and stretch, steps with footfalls, wind-up and recoil, drop-in spawns, toppling deaths, blob shadows.
3. Camera: trauma shake, a directional kick and a zoom punch. Hit-stop and a slow final blow, on a board clock that the HUD does not share.
4. Effects with gravity: debris that falls and bounces, directional sparks, floor shockwaves, scorch marks, landing dust, and light from blasts on the room.
5. HUD: damage numbers that pop and arc, HP bars that keep a white chunk for a moment, banners that slam in, a fight opener.
6. Film it: a burst-capture mode for the smoke run, so the motion can be read frame by frame. Then the Unity tests, a Mac build played with mouse and keyboard, and the docs.

## Decisions

- 2026-11-10: Unity client, not the web client (owner's answer). The web client is deleted at M13, so work there would not last.
- 2026-11-10: No sound in this branch. M13 ports it, and an agent cannot judge how it sounds. The staging gives each impact a time, which is what the sound will hang on.
- 2026-11-10: Timings are constants in the client's code, as its effect durations already are. `rules.ui.secondsPerActor` stays the base unit, so content keeps one knob for pace, and nothing in `content/` changes.

## Open questions

- None yet.

## Notes for whoever picks this up

- Close the Unity editor before any batch-mode command (README, Unity client).
- `unity/Assets/Scripts/Logic` must stay free of Unity code: its tests run in EditMode and could later run under `dotnet test`.
