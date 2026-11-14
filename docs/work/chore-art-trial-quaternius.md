# Trial the Quaternius art packs

- Branch: `chore/art-trial-quaternius`
- Pull request: <link, once opened>
- Roadmap item: "How to source 3D art", under Decisions waiting on the owner
- Started: 2026-11-14
- Last updated: 2026-11-14, by Claude

## Goal

Give the owner evidence for the 3D art decision before M14. Build the Armory, the Intruder and a Guard from the free tiers of Quaternius's CC0 packs (Modular Sci-Fi MegaKit, Sci-Fi Essentials Kit, Universal Base Characters, Universal Animation Library), look at them through the game's camera beside the greybox, and measure what they add to the web download. This is an experiment, not for merging: the packs stay out of git, and the findings go into the roadmap once the owner decides.

## Status

In progress: the packs are downloaded and listed; the trial scene is next.

## Done so far

- Downloaded the four free tiers through itch.io's free-download flow (342 MB of zips), each CC0 by its own licence file.
- Listed what the free tiers hold (see Notes).

## Next steps

1. Copy the models the trial needs into `unity/Assets/ArtTrial/Packs/` (ignored by git) and write an editor script that builds the Armory from the room's data with them, poses the Intruder and a Guard with the animation library, and saves a screenshot through the game's camera angle.

## Decisions

- 2026-11-14: a separate trial scene, not the live fight. Putting skinned, textured models into `UnitView` and `RoomView` is M20's work; the trial only needs the look and the size.
- 2026-11-14: the packs are not committed. They are large (342 MB zipped) and only for a trial; anyone can fetch them again from the links below.

## Open questions

- For the owner, after the trial: Quaternius or another source, and whether to buy the Pro or Source tiers.

## Notes for whoever picks this up

- Packs: https://quaternius.itch.io/modular-sci-fi-megakit, https://quaternius.itch.io/sci-fi-essentials-kit, https://quaternius.itch.io/universal-base-characters, https://quaternius.itch.io/universal-animation-library. Each page's "Download Now", then "No thanks, just take me to the downloads", gives the free Standard zip.
- Free tiers: the MegaKit has 84 walls, 37 floors and doors, 28 props (three vents among them), 8 columns, 29 decals and 3 aliens with an idle only. Sci-Fi Essentials has three rigged, animated enemies (EyeDrone, QuadShell, Trilobite), guns, a mine and props. Universal Base Characters has only the two Superhero bodies, with hair. The animation library has 43 clips, among them idle, walk, pistol aim, shoot and reload, two hits and a death.
- The MegaKit's textures are trim sheets with a packed ORM map, made for URP; the project uses the built-in pipeline, so the trial sets roughness and metal as constants.
