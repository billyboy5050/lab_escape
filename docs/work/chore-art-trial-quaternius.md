# Trial the Quaternius art packs

- Branch: `chore/art-trial-quaternius`
- Pull request: https://github.com/billyboy5050/lab_escape/pull/34
- Roadmap item: "How to source 3D art", under Decisions waiting on the owner
- Started: 2026-11-14
- Last updated: 2026-11-14, by Claude

## Goal

Give the owner evidence for the 3D art decision before M14. Build the Armory, the Intruder and a Guard from the free tiers of Quaternius's CC0 packs (Modular Sci-Fi MegaKit, Sci-Fi Essentials Kit, Universal Base Characters, Universal Animation Library), look at them through the game's camera beside the greybox, and measure what they add to the web download. This is an experiment, not for merging: the packs stay out of git, and the findings go into the roadmap once the owner decides.

## Status

Waiting on the owner: the trial is done and its findings are below. The owner decides the art's source; then the findings that last go into the roadmap or the phase plan, and this branch is closed without merging.

## Done so far

- Downloaded the four free tiers through itch.io's free-download flow (342 MB of zips), each CC0 by its own licence file, and listed what they hold (see Notes).
- Built the Armory from its room data with the packs, beside the greybox, through the game's camera (`ArtTrial.Capture`): the room, the first wave, the Intruder, and a drone, a turret and a hatchling for the machines.
- Built the game for the web with the trial's scene beside its own (`ArtTrialBuild.Web`), its people and machines carrying the animation clips a fight would play, at three texture budgets.

## Findings

**The room fits.** The MegaKit is built on a 4 m grid, so at a quarter scale its floors fill a tile exactly, and its walls, crates, columns and vents go where the room's data puts them with no hand placement. Its trims are near white, lit for a bright URP scene, so the trial tones them down to the lab's grey.

**The machines fit.** The Essentials kit's three rigged creatures suit the drone (EyeDrone), the turret (QuadShell) and the hatchling (Trilobite), each with its own idle, attack and hit clips.

**The people are the gap.** The free character tier is two bare bodies in underwear, and no Quaternius tier has clothes for a lab. In suits of their team's colour they read as sides but look like mannequins. Their real proportions are thin at the game's camera, so even scaled up they read smaller than the greybox's figures. The animation library's clips play on them with no retargeting, and the Essentials kit's pistol sits in the hand.

**Hazards lose their signal.** The greybox marks the floor panels with orange crosses and the shutters with striped gates. The kit's panel is a slightly different plate and its blocked door reads as solid wall. The fight would keep the greybox's markings on top, or use the MegaKit's decals.

**The download (the `Build` folder, gzip-compressed):**

| Build | Bytes | Added |
| --- | --- | --- |
| The game today | 11,423,459 | |
| Trial, textures at 1024 pixels with normal maps | 23,333,056 | 11.9 MB |
| Trial, 1024 pixels, no normal maps | 17,282,669 | 5.9 MB |
| Trial, 512 pixels, no normal maps | 15,324,743 | 3.9 MB |

The engine code grows by 0.24 MB in each, for animation. Before compression, the full trial adds 27.3 MB of textures (eleven normal maps are 15.4 MB of that), 4.2 MB of meshes and 1.5 MB of animation (36 clips). At the game's camera the 512-pixel build without normal maps looks almost the same: the bodies lose some muscle shading and the floors some panel detail. The MegaKit's pieces share three trim sheets, so further rooms should add mostly meshes.

**The pipeline.** The packs are made for URP, with a packed occlusion, roughness and metal map the built-in pipeline's Standard shader cannot read, so the trial sets roughness and metal as constants. URP would use the maps as made; that is a separate decision.

## Next steps

1. The owner decides: Quaternius for the rooms and machines or not, and where the people come from.
2. Move what lasts (the size budget, the hazards' markings, the people) into the roadmap or the phase plan, then close the pull request without merging and delete the branch.

## Decisions

- 2026-11-14: a separate trial scene, not the live fight. Putting skinned, textured models into `UnitView` and `RoomView` is M20's work; the trial only needs the look and the size.
- 2026-11-14: the packs are not committed. They are large (342 MB zipped) and only for a trial; anyone can fetch them again from the links below.

## Open questions

- For the owner: Quaternius for the rooms and machines? Where the people come from: another source's characters (KayKit's sci-fi characters are in its paid Mystery Series), commissioned ones, or the greybox's figures kept beside art rooms? Whether to buy the Pro or Source tiers: the trial needed nothing beyond the free ones for the room.

## Notes for whoever picks this up

- Packs: https://quaternius.itch.io/modular-sci-fi-megakit, https://quaternius.itch.io/sci-fi-essentials-kit, https://quaternius.itch.io/universal-base-characters, https://quaternius.itch.io/universal-animation-library. Each page's "Download Now", then "No thanks, just take me to the downloads", gives the free Standard zip.
- Free tiers: the MegaKit has 84 walls, 37 floors and doors, 28 props (three vents among them), 8 columns, 29 decals and 3 aliens with an idle only. Sci-Fi Essentials has three rigged, animated enemies (EyeDrone, QuadShell, Trilobite), guns, a mine and props. Universal Base Characters has only the two Superhero bodies, with hair. The animation library has 43 clips, among them idle, walk, pistol aim, shoot and reload, two hits and a death.
- The trial copies these into `unity/Assets/ArtTrial/Packs/` (ignored by git): from the MegaKit the four platforms, `Door_Frame_Square_Blocked`, `ShortWall_Metal2_Straight`, `WallBand_Straight`, `WallBand_Corner_Square_Inner`, `TopSimple_Straight`, `BottomMetal_Straight`, both columns, `Prop_Vent_Big`, `Prop_Crate3`, `Prop_Computer` and `Prop_Light_Wide`; from Essentials the three enemies, `Prop_Mine`, `Gun_Rifle` and `Gun_Pistol`; from the characters both bodies, two hairs and the eyebrows; and `UAL1_Standard.fbx`. Each pack's `Textures` folder goes with it.
- The MegaKit zip unpacks its `Textures` folder read-only. Unity cannot write `.meta` files there and loops in its first scan forever, at full CPU and with nothing in the log after package registration. Run `chmod -R u+w unity/Assets/ArtTrial/Packs` after copying the packs in.
- To rerun: `ArtTrial.Capture` (batch mode with graphics, so not `-nographics`; `-artTrialOut <folder>` for the screenshots, `-artTrialNoNormals` for the lean look), then `ArtTrialBuild.Web` (`-artTrialMaxTexture 512`, `-artTrialBuildOut <folder>`), then set the project back to the Mac with `-buildTarget StandaloneOSX`.
