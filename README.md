<div align="center">

# arknights-oni

**Bring Arknights operators into Oxygen Not Included.**

Operators are available today. Voice, base furniture, enemies, and visual effects are on the roadmap. Arknights is the first reference implementation for a future reusable ONI content framework.

[English](./README.md) · [简体中文](./README.zh-CN.md) · [Roadmap](#current-progress--roadmap) · [Installation](#installation)

[![Version](https://img.shields.io/badge/version-0.3.2--alpha.1-6d5dfc)](https://github.com/nya-a-cat/arknights-oni/releases/tag/v0.3.2-alpha.1)
![ONI tested](https://img.shields.io/badge/ONI_tested-740622-ea6b35)
![C#](https://img.shields.io/badge/C%23-Unity-512BD4?logo=csharp&logoColor=white)
[![Repository](https://img.shields.io/badge/GitHub-arknights--oni-181717?logo=github)](https://github.com/nya-a-cat/arknights-oni)

</div>

![Arknights Operators Alpha gameplay montage](./docs/images/arknights-oni-alpha-v0.3.2-workshop.png)

> [!IMPORTANT]
> Version `0.3.2-alpha.1` currently implements the **Arknights Operators** module. It replaces duplicant visuals with selectable operator Spine models and maps movement, work, rest, sleep, stress, and death states to matching animations.
>
> The selected appearance is currently global and applies to every duplicant.

The current release has been smoke-tested in a four-duplicant isolated save on Oxygen Not Included build 740622. Live switching was verified with `Surtr`, `阿米娅`, and `テキサス` searches.

## What makes it special?

- Search a catalog of 449 operators by Chinese, English, or Japanese name, PRTS redirect alias, or `char_id` inside the game.
- Use automatically selected Chinese or English option labels; operator display names prefer Chinese, Japanese, or English according to the current game language and available PRTS metadata.
- Select an operator, skin, and model through linked controls.
- Open the same selection interface from Mod Options or in a loaded save with `Ctrl+F8`, then apply the new appearance live.
- Render Spine 3.8 Region/Mesh attachments, clipping, multiple atlas pages, and common blend modes directly in C#.
- Map ONI movement, work, rest, sleep, stress, and death states to available operator animations.
- Choose between a bounded 512 MiB on-demand LRU cache and permanent retention of downloaded resources.
- Merge concurrent requests for the same resource while allowing each duplicant to cancel its own wait independently.
- Verify downloads with HTTPS source restrictions, temporary files, a SHA-256 index, and a 64 MiB per-file limit.
- Fall back through the original duplicant visual, an optional bundled Spine asset, and the legacy frame path when an appearance cannot be loaded.

## Installation

### Prerequisites

- Oxygen Not Included for Windows installed through Steam.
- WSL with Mono `mcs` available.

The repository does not install a compiler, browser, or large dependency automatically.

### Build and install

```bash
cd arknights_oni_mod_work/AmiyaDuplicantMod
./build.sh
./install_local.sh
```

The default local Mod directory is:

```text
C:\Users\<you>\Documents\Klei\OxygenNotIncluded\mods\Local\AmiyaDuplicantMod
```

Set `ONI_GAME_ROOT` to override the game directory or `ONI_LOCAL_MOD_DIR` to override the installation target.

> [!TIP]
> Start the game through Steam, enable **Arknights Operators（明日方舟干员）** in the Mods menu, and restart when prompted. Launching the game executable directly can trigger Klei's Mod Safe Mode in some Steam environments.

The repository does not distribute Arknights artwork, Spine skeletons, atlases, or PRTS web bundles. When an appearance is selected for the first time, the Mod fetches only the small files required for that appearance from the PRTS resource domain. A single file is capped at 64 MiB, and the workflow does not introduce an individual dependency larger than 100 MB.

## Resource strategies

| Mode | Behaviour | Best for |
| --- | --- | --- |
| On-demand cache (recommended) | Fetch only the selected appearance. When the cache exceeds 512 MiB, evict the least recently used files that are not referenced. | Keeping disk usage bounded |
| Keep downloaded resources | Fetch only the selected appearance and retain successfully cached files without capacity eviction. | Reusing visited appearances offline |

Neither mode pre-downloads the full operator catalog.

## Current progress & Roadmap

### Operators

- [x] Searchable 449-operator catalog with Chinese, English, Japanese, redirect-alias, and `char_id` lookup
- [x] Linked operator, skin, and model selection
- [x] Live switching from Options and `Ctrl+F8`
- [x] Runtime animation mapping and ground alignment
- [ ] Per-duplicant appearance and voice settings
- [ ] Operator voice with language selection, preview, cooldown, and priority
- [ ] Appearance preview, favourites, presets, and Printing Pod assignment pools

### Arknights content

- [ ] Base furniture, room themes, and animated decorations
- [ ] Enemy and creature appearance packages
- [ ] Skill, combat, work, and environmental effects
- [ ] Typed content packages: `operator`, `voice`, `furniture`, `enemy`, and `effect`

### Platform quality

- [x] Automatic Chinese/English localization for the operator options interface
- [x] Chinese/English/Japanese operator-name search from PRTS encyclopedia metadata
- [ ] Move remaining runtime errors and diagnostics into ONI `STRINGS` resources and add more interface locales
- [ ] Cache manager, download status, and diagnostics export
- [ ] Versioned configuration migration and catalog updates
- [ ] Compatibility controls for other appearance Mods

### Long-term framework direction

- [ ] Re-evaluate `arknights-oni` as a reusable ONI content framework after the Arknights content pipeline matures
- [ ] Extract stable content lifecycle, cache, selection, event mapping, and package contracts into a reusable core
- [ ] Keep Arknights as the first reference content pack and compatibility suite
- [ ] Evaluate content packs inspired by other games, with **BanG Dream!** as an example candidate

See the [complete code review and roadmap](./docs/code_review_and_roadmap_20260715.md) for priorities, acceptance criteria, performance limits, and resource boundaries.

## Development

```bash
cd arknights_oni_mod_work/AmiyaDuplicantMod
./build.sh
./tests/run_operator_animation_mapper_tests.sh
./tests/run_operator_appearance_catalog_tests.sh
./tests/run_mod_localization_tests.sh
./tests/run_resource_index_tests.sh
./tests/run_operator_asset_resolver_integration.sh
```

The final integration test downloads a real, small PRTS fixture. The remaining tests use only local code and fixtures.

## Repository layout

- `arknights_oni_mod_work/AmiyaDuplicantMod/src`: Mod entry points, settings, cache, resource resolution, rendering, and animation mapping.
- `arknights_oni_mod_work/AmiyaDuplicantMod/tests`: Logic tests and the real small-resource integration test.
- `arknights_oni_mod_work/AmiyaDuplicantMod/lib`: PLib plus the pinned Spine C# runtime sources and provenance notes.
- `docs`: PRTS asset research, architecture and acceptance notes, and the detailed roadmap.
- `PROGRESS.md`: Append-only implementation and verification log.

## Project boundaries & third-party components

This is a non-commercial fan project with no affiliation with or endorsement by Klei, Hypergryph, or PRTS Wiki. Game and character rights belong to their respective owners. The public repository contains original Mod source code, tests, development notes, lightweight catalog metadata, separately licensed third-party code, and a promotional montage made from real gameplay screenshots. Runtime artwork and animation assets are retrieved by the user on demand.

No additional open-source license is currently granted for the original code. PLib, the Spine runtime, and catalog metadata remain subject to their respective licenses and source notices. See [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) and [DATA_NOTICE.md](./DATA_NOTICE.md).
