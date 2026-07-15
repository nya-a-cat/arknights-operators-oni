# Third-party notices

## PLib

- Component: PLib
- Included file: `arknights_oni_mod_work/AmiyaDuplicantMod/lib/PLib.dll`
- Source and version record: `arknights_oni_mod_work/AmiyaDuplicantMod/PLIB-SOURCE.txt`
- License: MIT, copied in `arknights_oni_mod_work/AmiyaDuplicantMod/PLIB-LICENSE.txt`

## Spine C# Runtime 3.8

- Component: Spine C# Runtime 3.8
- Upstream: `https://github.com/EsotericSoftware/spine-runtimes`
- Pinned commit and local compatibility note: `arknights_oni_mod_work/AmiyaDuplicantMod/lib/SPINE-RUNTIME-SOURCE.txt`
- License: `arknights_oni_mod_work/AmiyaDuplicantMod/SPINE-RUNTIME-LICENSE.txt`
- Upstream README snapshot: `arknights_oni_mod_work/AmiyaDuplicantMod/lib/SPINE-RUNTIME-README.md`

The Spine runtime is distributed under Esoteric Software's Spine Runtimes license. That license contains conditions tied to the Spine Editor license. Repository visibility does not replace those conditions; each user and redistributor is responsible for satisfying them.

## Game and operator assets

The Git source repository does not include Arknights images, exported animation sheets, Spine skeleton files, atlas files, or copied PRTS web bundles. They are excluded by `.gitignore`.

The optional GitHub Release fallback snapshot contains operator-scoped copies of the atlas, skeleton, and texture files required by the 449-entry runtime catalog. The snapshot is generated from the PRTS resource host, separated from original program code, versioned independently, and fetched only after the primary PRTS path fails. Runtime extraction remains limited to the appearance selected by the user; the containing operator package follows the configured cache-retention policy.
