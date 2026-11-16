# MagicCircle

> Developer notes (written in sfm-modkit, where the mod is developed with an in-game test bridge; paths like `./tools/dev.ps1`
> and `docs/knowledge/` refer to that kit). The player readme is [`README.md`](../README.md).

A RestraintHub wearable (`Id` `MagicCircle`, hub `^2.0.0`): four glowing circles on her limbs, stages (+ / -), two placements
(tap F), struggle (hold F). Status 0.1.0: L3 (verified in game by the agent, 2026-11-16); not released.

## Files
- `Plugin.cs`: config, the `Wearable` (`Holds` changes with the stage: none / `Arms | Legs | Spot`; `Blocks` likewise:
  none / Moving, Crouching, HandActions, PoseSkills), mission settings `stage` (0, 1) and `placement` (0, 1).
- `CircleController.cs`: lock (`LockMachine`, Close/OpenSeconds 0.8 = the circles grow in / fade), stage, placement, the
  pose weight (blends over `Float.BlendSeconds`), the pose in `LateUpdate`, the circles in `Camera.onPreCull`.
- `FloatPoser.cs`: measuring, the floating pose (hips placed, bones aimed from the hips outwards, local rotations slerped with
  the animation by the weight), the portal hiding.
- `CircleRig.cs`: quads, generated textures, materials.
- `Logic/` (tested): `CirclePattern` (the circle and its glow as alpha masks), `FloatPose` / `CircleSpot` (pose and circle
  maths), `Stages`.

## How it works
- **Circles:** a unit quad per circle with the pattern (rings, six-pointed star, 20 seeded runes, dots; the middle inside
  `CirclePattern.ClearRadius` 0.56 empty) and a larger soft glow quad behind, turned to face along the limb segment
  (`FromToRotation(forward, joint - parent)`), spinning (neighbours opposite ways), the glow pulsing. Placed on the drawn
  bones in onPreCull. Sizes from bone lengths: wrist 12 % of the forearm, ankle 9 % of the shin, above the elbow 13 % of the
  upper arm, above the knee 15 % of the thigh; the hole is `Look.Size` (1.35) times that.
- **Shader:** the game has no additive particle shader loaded (`Shaders` diagnostic: Particles/Standard Unlit, Legacy
  Particles/Alpha Blended (+ Premultiply), Sprites/Default, Unlit/*, UI/Default). `Particles/Standard Unlit` set to additive
  (`_SrcBlend` 5, `_DstBlend` 1, `_ZWrite` 0, `_Cull` 0, `_ALPHABLEND_ON`), colour in `_Color` (alpha = strength) works.
  `Shader.Find("Legacy Shaders/Particles/Additive (Soft)")` returned a shader that drew white without a tint.
- **Floating:** frame captured when the stage starts (her root position and yaw). Hips at `HeightCm + ankle height (18 % of
  the shin) + straight legs + hip drop`, upright, thigh joints level; spine bones aimed straight up; legs at `LegAngle` out,
  toes pointed (down and a little forward); arms `ArmAngle` out from vertical, a touch forward. Bob 3.2 s. Pointed toes use
  about 10 cm of the height: at 15 cm the toe joints were 10.4 cm up (standing: 1.9 cm), so the default is 20 cm.
- **Portal (ElbowsAndKnees + `HideLimbs`):** at the end of LateUpdate the forearm and lower-leg bones are moved
  `PortalBeforeCm` back towards the upper bone and scaled to 1e-4 (the skin of forearm, hand, shin and foot collapses into that
  point; the circle sits there). Restored at the start of the next LateUpdate (posing needs full bones) and on release.
  **The game's `PlayerBoneScaleAdjuster` child nodes compensate the parent's scale when her body settings apply (on a load):
  they went to 10000, hands and feet showed again at full size on thin stretched skin, and would have been 10000x after
  release.** Fixed by scaling any child of a hidden bone that is over 100x back by 1e-4 each frame (log "portal: undid the
  game's scale compensation on ...").
- **No stump visible (`StumpMeter`, `Stump.Reach`):** the skin around the joint is partly bound to the upper bone and does not
  all collapse - it ended in a tip a few cm past the collapse point. Every 0.5 s while hidden, all her enabled skinned meshes
  (body and clothes, ~60 000 points) are baked in onPreCull (the shrunk bones as drawn) and, per limb, the farthest point within
  1.1x the hole's radius of the limb axis and at most 10 cm past the collapse point is found (the larger of the last two bakes).
  First save: knees 2.5 cm, elbows 3.5 cm (the coat's sleeves).
  **Only each limb's own skin counts:** the first version took every point near the limb axis; walking, the other knee's stump
  (7-8 cm off the axis) entered and the circles jumped (2.5 -> 3.8 / 6.7 cm). Now each bake is done twice, the second with the
  shrunk bones grown back (`FloatPoser.WithLimbsGrown`); vertices that move > 2 mm are the limbs' own and go to the limb whose
  full-size lower segment is nearest (`Stump.Owner`). The knee's own skin still reaches up to 6.6 cm past the joint while walking
  (real: it moves with the shin, on the shin's surface when grown back), so the reach is steadied (`ReachHold`: largest of the last
  20 s, rising at 25 cm/s, sinking at 1 cm/s): walking, the knee circles settled once (2.5 -> 4.3 / 6.7 cm) and then stayed.
  Diagnostics: `game get MagicCircle.StumpMeter::Farthest` (per limb the renderer, vertex, reach, off-axis distance, how far it
  moved, its distance from its segment). The circle goes 0.4 cm beyond that, and an opaque **portal
  surface** fills its hole up to the inner ring (r 0.6 of the circle): `Unlit/Texture` (opaque, writes depth like a solid, so it
  hides the limb's end seen from beyond the circle), a two-sided disc, dark middle brightening to the rim with a faint swirl,
  tinted with `Look.Color`, turning slowly. No depth-mask shader is available (none loaded, none can be compiled without the
  Unity editor), so the opaque surface is the occluder.

## Diagnostics (bridge, `--allow-invoke`)
`game get MagicCircle.CircleController::Status` ("Locked, floating (w 1.00), elbows and knees, hidden yes, shader ..."),
`::ToesUp` (drawn toe height above her root); `game call MagicCircle.CircleController PutOnNow | Step 1 | Switch | ReleaseNow |
Shaders <part>`. `Debug.Disable`: 1 = no pose, 2 = no hiding. Log markers: `put on:`, `lock:`, `stage:`, `placement:`,
`attempt:`, `release:`, `portal:`, `save:` / `load:`, `shader:`.

## Verification
- L1: 22 tests (pattern: empty middle, rings drawn, deterministic; pose: heights, lengths, angles, bob; stages; circle spot).
- L3 0.1.0 (2026-11-16, first save, apartment): put on -> "circles formed", circles at the ankles (close-up screenshot: pink
  rings, runes, star, glow). Step 1 -> "The circles lift her off the ground.", floating narrow X with circles at wrists and
  ankles (screenshot from behind), toes 10.4 cm up (1.9 standing). Switch -> elbows and knees, forearms / lower legs hidden
  (screenshots); with the joint pulled back 4 cm the knee stump ends at the circle (a small tip remains on one knee). Struggle
  with chance 1 -> "attempt: success", stage back to 0, "RtHub_Escaped MagicCircle by struggle", standing with all limbs.
  PC -> Save while floating: "save: locked=True stage=1"; restart + load -> floating, portal hidden; the adjuster
  compensation found and undone (above), screenshot; release -> all scales 1, normal hands and feet (screenshot). Yoke on,
  + -> "The circles can't hold her while the Yoke is on.".
- L3 portal surface (2026-11-16): "portal surface: Unlit/Texture", "stump: reach 3.5 / 3.5 / 2.5 / 2.5 cm ... from 60119 skin
  points"; floating, close-ups of the knees from the front, the side and below and of the elbows from below and above: the limbs
  end in the surface, no skin past the circles, the stump ends hidden from beyond (screenshots); standing in the "circles only"
  stage likewise. Not tested: deep knee bends (crawling), other clothes.
- Not tested: the RestraintHub panel row (bridge diagnostics used instead of `U`; same `PutOn`), the closet changing her body
  while hidden (same adjuster mechanism), missions, other characters' proportions, outdoor maps.
