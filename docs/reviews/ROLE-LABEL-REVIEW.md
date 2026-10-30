# Human PMC scoreboard role correction

## Failure and native contract

GPU acceptance commit `2327f63` records the local USEC player under the correct
team but with F9 `ROLE=Scav`. The classifier previously used only the profile's
bot spawn role. This does not imply a changed faction or saved profile.

Verified in the installed decompiled EFT sources:

- `EFT/ProfileSettings.cs`: `Role` defaults explicitly to `WildSpawnType.assault`.
- `EFT/ProfileInfo.cs`: faction `Side` and bot `Settings` are separate properties.
- `EFT/IPlayer.cs`: exposes `IsAI`, `Profile` and `Side`.
- `EFT/Player.cs`: `Side` delegates to `Profile.Info.Side`; `IsAI` reads AIData and
  otherwise returns false. Native `EPlayerSide` is USEC, BEAR or Savage.

## Scope

`RoleClassifier` now resolves non-AI USEC/BEAR players from their profile faction
before reading bot settings. This applies to both the short label and score
bucket. It also works when a human PMC has no bot settings. The local flag is not
required, so other non-AI PMCs use the same contract.

AI keeps the existing explicit spawn-role mapping, even when its faction is USEC
or BEAR. Raiders, rogues, bosses, followers, cultists and actual Scavs retain their
labels and points. Human Scavs and unknown sides retain the existing role-based
fallback. Missing player/profile/info remains `?` / Other.

The classifier is read-only. No profile, faction, bot generation, team relationship
or native game method is modified. Consumers are Arena match initialization,
scoreboard actor snapshots, local respawn updates and captured kill metadata.
Normal raids do not acquire a new patch or presentation path.

## Checks and limits

`tools/RoleLabelChecks/Verify.ps1` compiles the actual classifier, score rules and
scoreboard with a minimal player-data stand-in. Native faction/spawn-role enums
and `ProfileSettings` compile directly from the sibling `_decompiled` tree; these
sources are not copied into the repository and no EFT/Unity DLL is loaded.

The checks cover default-role local and non-local USEC/BEAR, human Scav, missing
data, 22 distinct AI role cases across all three factions, scoreboard creation,
roster synchronization and respawn, unchanged point values and no profile writes.
The script also compiles the original GPU-baseline classifier and six deliberate
source faults. Each must build successfully and fail its named behavior assertion;
the final rebuild restores the production-linked check binary.

Executed 2026-09-08: **174 assertions passed**, and all **seven negative controls**
(original GPU bug plus six source faults) built and failed the intended behavioral
assertion. The production checks passed again after the mutation run. Evidence:
`artifacts/role-label-checks/59b03f36cbb848eb8ef931fe2bfe83dd/`. The isolated harness
emits six CS0649 warnings for unused linked kill-metadata fields; there were no
compile errors. Independent root adversarial review verified native defaults,
Arena-only consumers, null/AI handling and the production-linked negative controls;
no unresolved P0-P2 finding. Root reran all 174 assertions and seven controls:
`artifacts/role-label-checks/fcfa8c24aa8b441eb195a87c6c481863/`.

No mod build, deployment, native F9 rendering or GPU raid success is claimed by
this bounded correction. GPU follow-up: check local USEC and BEAR labels at match
start, roster refresh and respawn, and confirm friendly/enemy PMC bot labels stay
correct.
