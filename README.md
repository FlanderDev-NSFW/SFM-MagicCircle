# MagicCircle 0.1.0

Glowing magic circles form around her wrists and ankles - or just before her elbows and knees, where her limbs vanish as if
through a portal. They turn slowly and pulse softly; one stage only shows them, the next lifts her off the ground into a
floating narrow X. A restraint for **Secret Flasher Manaka**, used through RestraintHub.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 2.0.0** or newer

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/MagicCircle/`.

## How to use
Open RestraintHub's panel (`U`) and choose **Magic Circle**: the circles form around her wrists and ankles. While it is the
selected restraint:

| Key | Does |
|---|---|
| `+` / `-` | change the stage: **circles only** (she moves freely) / **floating** (held in the air, arms raised, legs a little apart) |
| tap `F` | move the circles between **wrists and ankles** and **elbows and knees** (her forearms and lower legs hidden past them) |
| hold `F` | struggle free (she sinks down while the circles fade) |

While floating she can't walk, crouch, use her hands or pose, and other restraints that hold her arms, legs or place can't be
added (nor can she float while one is on). Everything else of RestraintHub works as usual: locking with the handcuff key,
holding `V` to take everything off, missions.

## Settings
`BepInEx/config/FlanderDev.SFM.MagicCircle.cfg` (created on the first start): colour (`Look.Color`, hex such as `FF70E0`),
size, spin speed, glow and pulse; how far before the elbows / knees the portal circles form and whether limbs are hidden there;
the floating height, arm and leg angles, the bob and how fast she rises; how hard it is to struggle free.

## Good to know
- The circles' place (wrists / elbows) is remembered in the settings; the stage and whether it is on are kept in the save.
- With the circles at her elbows and knees, each circle is filled with a dark glowing portal surface: her limbs end in it,
  nothing of them shows past it. In the "circles only" stage she still walks as if she had lower legs.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). The circles are drawn in code (no model or image files).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/MagicCircle/MagicCircle.csproj -c Release`.
- The source of the mod is `src/MagicCircle` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared
  [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit) library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
