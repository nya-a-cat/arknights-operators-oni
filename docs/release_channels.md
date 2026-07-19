# Branch and release channels / 分支与发布通道

This document defines the workflow introduced for the `0.3.3` release line. Version `0.3.3` is the current Stable baseline for GitHub and Steam Workshop; later development returns to `develop` and follows the same gates.

本文规定从 `0.3.3` 版本线开始使用的流程。`0.3.3` 是当前 GitHub 与 Steam 创意工坊 Stable 基线；后续开发继续进入 `develop` 并沿用相同门禁。

## Branch policy / 分支策略

| Branch | Purpose | Merge rule |
| --- | --- | --- |
| `main` | Game-tested stable source and release automation | Accept `develop → main` pull requests after cloud checks and explicit game-test confirmation |
| `develop` | Daily integration and rapid development | Direct focused commits are allowed; every pushed commit must remain buildable by deterministic checks |
| `feature/*` | Isolated high-risk work such as collision profiles, enemy assets, or resource-protocol changes | Merge into `develop` after focused verification, then delete the temporary branch |

`main` rejects direct pushes, force pushes, and deletion. A single-maintainer repository does not require an additional approving reviewer. The required cloud check and the manual game-test result remain release gates.

The recommended local layout uses two lightweight Git worktrees that share the object database:

- the existing development directory checks out `develop`;
- a sibling directory such as `../arknights_oni_mod_stable` checks out `main`.

Each worktree keeps its own index and build output. Changes are committed on the branch that owns the work; generated packages and game caches stay outside Git.

## Release channels / 发布通道

| Channel | Version example | Package identity | Distribution | Gate |
| --- | --- | --- | --- | --- |
| Dev / Nightly | `0.3.3-dev.20260716.abcdef0` | Testing | Local Nightly ZIP | Clean, committed `develop`; deterministic tests and identity checks pass |
| RC | `0.3.3-rc.1` | Testing | GitHub prerelease | Candidate feature set frozen; full game checklist begins |
| Stable candidate | `0.3.3` | Stable | GitHub Draft Release | Install and test the exact final ZIP from the draft |
| Stable | `0.3.3` | Stable | Public GitHub Release and existing Steam Workshop item | The tested draft asset is published unchanged and the same ZIP is uploaded to Steam |

Stable tags and uploaded assets are immutable. A later fix receives a new patch version such as `0.3.4`. Existing Alpha Release pages and assets remain historical records; future publication must never replace an asset under an existing tag and filename.

A second Steam Testing item is deferred until external testers need it. Workshop item `3765340857` receives Stable packages only.

## Stable and Testing identities / Stable 与 Testing 身份

| Field | Stable | Dev and RC Testing |
| --- | --- | --- |
| Install directory | `ArknightsOperatorsMod` | `ArknightsOperatorsMod.Testing` |
| Main DLL / assembly | `ArknightsOperatorsMod.dll` | `ArknightsOperatorsTesting.dll` |
| `staticID` | `local.arknights_amiya_duplicant` | `local.arknights_operators_testing` |
| Title | `Arknights Operators（明日方舟干员）` | Stable title plus `[DEV]` or `[RC]` |
| Configuration and cache | Existing Stable PLib configuration, cache, and temporary-resource directories | Separate Testing PLib configuration, cache, and temporary-resource directories |
| Legacy migration | Keeps the existing Amiya compatibility and migration path | Never runs the legacy Amiya migration |

Stable and Testing may be installed together. Enable only one identity per game start. Testing uses a copied save so a Dev or RC fault cannot alter the Stable validation save.

Build and package the three channels from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build_mod_release.ps1 -Channel Stable -Version v0.3.3
powershell -ExecutionPolicy Bypass -File .\tools\build_mod_release.ps1 -Channel Dev -Nightly
powershell -ExecutionPolicy Bypass -File .\tools\build_mod_release.ps1 -Channel RC -Version v0.3.3-rc.1
```

Install a Dev or RC ZIP with the Testing-only installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\install_testing_mod.ps1 -PackagePath <testing-zip>
```

Run the local complete-artifact probe with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\test_packaging_artifacts.ps1
```

The probe validates Stable, Dev, and RC ZIP contents and identities, embedded/sidecar hashes, the isolated Testing installer, Stable-package rejection, and three-package Nightly retention. It uses existing local DLLs with `-SkipCompile`, writes only below the repository `.cache` directory, creates a temporary `LocalModsRoot` inside that probe directory, and removes the probe directory in `finally`. It does not open the real ONI Local Mods directory or start the game.

Every package contains exactly one main DLL. Its embedded `<ModDirectory>/build-info.json` records:

- version and channel;
- source commit SHA and dirty state;
- assembly filename and `staticID`;
- DLL SHA-256;
- `zipSha256: null` plus the expected adjacent sidecar filename in `zipSha256Source`.

The adjacent sidecar records the final ZIP SHA-256, DLL SHA-256, and embedded-manifest SHA-256. For example, `arknights-oni-v0.3.3-rc.1.zip` is paired with `arknights-oni-v0.3.3-rc.1.build-info.json`. This two-file contract avoids a self-referential ZIP hash. Release validation keeps the ZIP and its sidecar together.

Every dirty package adds `local-dirty` to its effective version and filename, writes `dirty: true`, and writes `eligibleForUpload: false` to both build-info records. Examples include `0.3.3-local-dirty` for Stable and `0.3.3-rc.1.local-dirty` for RC. Dirty packages are diagnostic output only. A clean Stable build runs only from `main`, and a clean RC build runs only from `develop`; a clean build from the wrong branch fails. Any package with `eligibleForUpload: false` cannot be uploaded, promoted, or treated as release evidence.

## Nightly and cloud checks / Nightly 与云端检查

GitHub Actions runs deterministic tests and package-identity checks for pushes to `develop` and pull requests targeting `develop` or `main`. The workflow uses `contents: read`, does not receive game assemblies, and does not upload PRTS artwork or runtime caches.

The one-time history-cleanup push places the workflow infrastructure on `main`. In the target completed repository state, GitHub enables the schedule from `main` at `02:37` Beijing time (`18:37 UTC` on the previous calendar day), and every daily run explicitly checks out `develop`. It runs the small PRTS integration probe and retains its text report for seven days. This probe supplies an integration signal only; full Mod DLL and game testing remain local.

The complete DLL continues to build locally with the existing WSL, Mono, and locally installed ONI assemblies. No self-hosted runner is part of the `0.3.3` workflow.

An official Nightly comes only from a clean, committed `develop` checkout. Its filename follows `arknights-oni-v0.3.3-dev.YYYYMMDD.<short-sha>.zip`. Local retention deletes older files only inside `.cache/nightly` and keeps the newest three packages.

## Promotion checklist / 晋升检查

1. Freeze the candidate on a clean `develop`, run deterministic tests, and create `0.3.3-rc.N` with the Testing identity. Require `dirty: false` and `eligibleForUpload: true` in its sidecar.
2. Install the RC beside Stable, enable only RC, and test a copied save with four different operators, save/reload, movement, work, attack, sleep, stun, death, original-duplicant hiding, and ground alignment.
3. Resolve defects on `develop`, issue a new RC, and repeat the affected checks plus the full final checklist.
4. Open a `develop → main` pull request only after the RC result is recorded. Merge after required cloud checks and explicit manual game-test confirmation.
5. From a clean `main`, require `dirty: false` and `eligibleForUpload: true`, then stage `0.3.3` as a GitHub Draft Release with the Stable identity. Download and install the draft ZIP, then repeat the final game checklist against that exact file.
6. Compare the tag, package version, source SHA, DLL hash, sidecar ZIP hash, and embedded `build-info.json` hash. Publish the same draft asset and upload that same ZIP to Steam Workshop item `3765340857`.

The Steam package remains unchanged during Dev, Nightly, RC, and Stable-candidate work. Promotion happens only after the final Stable ZIP has passed the full checklist.

## Rollback / 回滚

- Before public Stable publication, discard the draft or issue a newer RC; the Workshop Alpha remains available.
- After a Stable publication, preserve its tag and asset and publish a higher patch version for any repair.
- If Testing causes a game or save problem, disable Testing, restore the copied test save, and return to the separately installed Stable identity.
