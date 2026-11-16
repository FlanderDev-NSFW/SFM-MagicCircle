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
  the shin) + the legs' drop + hip drop` (the legs' drop from their untilted directions), thigh joints level; the whole body
  leaned back `Tilt` (12) about the hips: every direction turned about her right axis, so the spine bones aim along the tilted
  up. Legs `LegAngle` (14) out, thighs `KneeLift` (22) forward, shins `KneeBend` (45) back from them, toes pointed (down and a
  little forward); upper arms `ArmAngle` (32) out from vertical, forearms `ElbowBend` (30) in towards the head, a little
  forward. Bob 3.2 s. (0.1.0 first had straight limbs, 25 / 8 degrees, no tilt: "too stretched".) Pointed toes use
  about 10 cm of the height: at 15 cm the toe joints were 10.4 cm up (standing: 1.9 cm), so the default is 20 cm.
- **Portal (ElbowsAndKnees + `HideLimbs`):** at the end of LateUpdate the forearm and lower-leg bones are moved
  `PortalBeforeCm` back towards the upper bone and scaled to 1e-4 (the skin of forearm, hand, shin and foot collapses into that
  point; the circle sits there). Restored at the start of the next LateUpdate (posing needs full bones) and on release.
  **The game's `PlayerBoneScaleAdjuster` child nodes compensate the parent's scale when her body settings apply (on a load):
  they went to 10000, hands and feet showed again at full size on thin stretched skin, and would have been 10000x after
  release.** Fixed by scaling any child of a hidden bone that is over 100x back by 1e-4 each frame (log "portal: undid the
  game's scale compensation on ...").

## Diagnostics (bridge, `--allow-invoke`)
`game get MagicCircle.CircleController::Status` ("Locked, floating (w 1.00), elbows and knees, hidden yes, shader ..."),
`::ToesUp` (drawn toe height above her root); `game call MagicCircle.CircleController PutOnNow | Step 1 | Switch | ReleaseNow |
Shaders <part>`. `Debug.Disable`: 1 = no pose, 2 = no hiding. Log markers: `put on:`, `lock:`, `stage:`, `placement:`,
`attempt:`, `release:`, `portal:`, `save:` / `load:`, `shader:`.

## Verification
- L1: 27 tests (pattern: empty middle, rings drawn, deterministic; pose: heights, lengths, angles, bob; stages; circle spot).
- L3 0.1.0 (2026-11-16, first save, apartment): put on -> "circles formed", circles at the ankles (close-up screenshot: pink
  rings, runes, star, glow). Step 1 -> "The circles lift her off the ground.", floating narrow X with circles at wrists and
  ankles (screenshot from behind), toes 10.4 cm up (1.9 standing). Switch -> elbows and knees, forearms / lower legs hidden
  (screenshots); with the joint pulled back 4 cm the knee stump ends at the circle (a small tip remains on one knee). Struggle
  with chance 1 -> "attempt: success", stage back to 0, "RtHub_Escaped MagicCircle by struggle", standing with all limbs.
  PC -> Save while floating: "save: locked=True stage=1"; restart + load -> floating, portal hidden; the adjuster
  compensation found and undone (above), screenshot; release -> all scales 1, normal hands and feet (screenshot). Yoke on,
  + -> "The circles can't hold her while the Yoke is on.".
- L3 pose rework (2026-11-16, user: "less stretched, slightly tilt her whole body backwards, spread & pull in legs a bit"):
  side and back screenshots - leaning back, knees forward and shins back, legs wider, forearms bent in over her head;
  toes 19 cm up.
- Not tested: the RestraintHub panel row (bridge diagnostics used instead of `U`; same `PutOn`), the closet changing her body
  while hidden (same adjuster mechanism), missions, other characters' proportions, outdoor maps.
