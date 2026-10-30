# SAIN 4.5.1 and PitFireTeam beta-0.10.1: Arena impact

Review only: neither third-party update was installed, nor were any Arena
version gates relaxed. Official tagged sources and release binaries were
downloaded to temporary review directories; no third-party code or DLL is
included in this repository. Binary metadata was inspected without executing
the downloaded assemblies. Live GPU behavior remains unverified.

## Recommendation

| Update | Result for current Arena |
|---|---|
| SAIN 4.5.0 to 4.5.1 | Existing reflected contracts pass against the candidate; suitable for a separate playtest without an Arena adapter change. |
| PFT 0.10.0 to 0.10.1 | Current exact-version checks reject it. Add complete persistence/transaction coverage before explicitly supporting this version. Keep 0.10.0 for the currently tested 5v5 setup. |

## SAIN

Installed product version is `4.5.0+66e6740a6c91e6b618ee7ab4c7a3ff0bcbaacdf7`.
The reviewed release is `4.5.1+2d1d9257dbb3af535e9189e6181311f6355aec27`.
[Official SPT 4.1 release](https://github.com/ArchangelWTF/SAIN/releases/tag/v4.5.1).

- The weapon-selection correction excludes unusable weapons from distance
  comparisons instead of treating missing distance values as zero. This is
  relevant to cycling weapons, but does not prove that the four historical
  native weapon-switch exceptions in the long-raid report are fixed.
  [Exact change](https://github.com/ArchangelWTF/SAIN/commit/936b22d596e82b37bec11ec597d64f11859aa714).
- SAIN improves finding firing positions for enemies that cannot be reached
  directly. This may help ordinary SAIN combat. Arena's optional BigBrain
  pursuit still owns movement during its post-death lease; keep that handoff
  and do not assume SAIN can solve arbitrary NavMesh obstacles.
  [Engagement change](https://github.com/ArchangelWTF/SAIN/commit/4aadb876c7f195bdcad97753f23b5268f19b1181).
- Particularly relevant to repeated Arena respawns, `OtherPlayersData` replaces
  a different player component under the same profile ID by disposing the old
  record and removing it from dictionary, hash set and list. The prior
  replacement omitted disposal/list removal. This is useful lifecycle cleanup,
  not evidence that it caused our measured respawn-time increase. Exact-body
  checks in Arena remain necessary.
  [Tagged source](https://github.com/ArchangelWTF/SAIN/blob/2d1d9257dbb3af535e9189e6181311f6355aec27/SAIN/Classes/Player/OtherPlayersData.cs).
- Weighted body-part targeting is enabled by default and can change difficulty
  and hit distribution. Recheck aim settings after updating.
  [Target settings](https://github.com/ArchangelWTF/SAIN/blob/2d1d9257dbb3af535e9189e6181311f6355aec27/SAIN.Preset.Shared/GlobalSettings/Categories/AimTargetSettings.cs).

The new MoreBots interop does not require an Arena change: Arena uses existing
PMC bot roles rather than registering additional SAIN role types. Tactical
device and impact-hearing changes affect behavior, not our reflected API.
[Release notes](https://github.com/ArchangelWTF/SAIN/releases/tag/v4.5.1).

The existing `TeamArenaExpansionChecks` were copied to TEMP with only repository,
linked-source and candidate-assembly paths adjusted. No assertion was removed.
Both installed and candidate SAIN passed **767 assertions**, including
`SAIN_RUNTIME_CONTRACT_PASSED`; root inspected the path-only diff and reran
the candidate check. Coverage includes GetSAIN, bot/player identity, Enemy
creation/removal, private GoalEnemy setter, last-seen update, and movement/layer
release. This is metadata/source-contract evidence, not a raid simulation.

A bounded cross-check of PFT 0.10.0 found no references to the removed SAIN
CenterMass APIs or changed GetAimTarget signature. Its core TryChangeWeapon
patch still targets an unchanged method, and follower aiming setup uses EFT
fields. No concrete new aiming-API break was found. The 767 assertions cover
Arena-to-SAIN contracts, not every PFT-to-SAIN hook; combined 5v5 behavior and
hit distribution still require a playtest.

## PitFireTeam

Installed client/server assembly versions are `0.10.0.0`; candidate versions
are `0.10.1.0`. Exact source tags resolve to
`109760c7a5aad3fdb181941e10ce035c3840bb4d` and
`75277f0a3fb540174b7a3ab0fd97a28992a9ce7b`, respectively. The new release
still targets SPT 4.1. Its combat, cover, healing and multi-follower recovery
improvements are relevant to our earlier idle-team observations, but must be
tested with Arena's post-death movement ownership.
[Official release](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/beta-0.10.1).

Concrete work before supporting the new version:

1. Add the new proficiency transaction to the explicit management and outer
   operation contracts in `PitFireTeamDynamicPersistencePatches`:
   `FriendlyTeammateService.SetTeammateProficiency` and
   `FriendlyTeammateCallbacks.SetProficiency`. The existing file-write guards
   are a final safety boundary, not a substitute for suppressing a complete
   transaction. Merely replacing version literals would leave coverage
   incomplete.
   [Callback](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/server/Callbacks/FriendlyTeammateCallbacks.cs#L190),
   [Service](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/server/Services/FriendlyTeammateService.cs#L1338).
2. Audit the new clothing migration in `GetProfileOptions`: a nominal read
   now updates and saves settings when owned-clothing data is missing. Cover
   Arena preparation, active raids and postraid finalization races. This is a
   prospective migration requirement; current version rejection prevents
   claiming an already active 0.10.1 profile-loss bug.
   [Read-side write](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/server/Services/FriendlyTeammateService.cs#L819).
3. Verify proficiency across respawns and rounds. `SpawnGroupBots` now carries
   the proficiency data into an extended `AddFollower` call. Arena does not
   reflect AddFollower directly, so that parameter is not itself a broken
   Arena hook. Normalizing follower skills independently of the SAIN preset
   can change 5v5 balance.
   [Spawn implementation](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/client/Patches/BotsControllerPatch.cs#L937).

Metadata comparison found the required signatures intact for OnDead,
SaveFollowersProgress, boss/follower access and removal, Dismiss, death unhook,
profile creation, group spawn, private activation, Stop prefix and squad UI;
33 directly declared registry/cleanup member signatures/layouts are unchanged. The asynchronous
ActivateBotAtPosition implementation is unchanged. SpawnGroupBots retains its
state-machine identity, with added proficiency handling. These findings reduce
migration risk but are not an approval to bypass version checks.

Dismiss now restores cached SAIN proficiency in its finally block. Arena
already dismisses old live/dead wrappers before replacing bodies; preserve
that ordering. New combat-layer accounting is cleared when disposing the old
boss, which Arena also does per round. No confirmed new teardown break was
found in those additions.
[Dismiss](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/client/Components/BotFollowerPlayer.cs#L1154),
[Boss disposal](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/client/Components/AIBossPlayer.cs#L4799).

The new teammate kill lists are local copied reports/UI, not a new server
persistence route. Check aggregation/reset behavior across Arena rounds and
reused teammate profile IDs; do not replace Arena scoreboard or profile
protection with the PFT report.
[Report collection](https://github.com/pitAlex/SPT-PitFireTeam/blob/75277f0a3fb540174b7a3ab0fd97a28992a9ce7b/client/Modules/SquadRaidKillReport.cs#L39).

A future compatibility change needs explicit old/new version contracts,
persistence regression coverage, then live tests with a survived round, a
player respawn, dead/live teammates, post-death pursuit and saved-file/profile
comparison. No 0.10.1 support was implemented during this review.

## Artifact fingerprints and reproduction material

SHA256:

| Artifact | Hash |
|---|---|
| Installed SAIN 4.5.0 client | `B0B8B2649D8360553169980D7EDDA44C1AC83119356F223E5D4F3FF819D30072` |
| Candidate SAIN 4.5.1 client | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` |
| SAIN 4.5.1 release ZIP (matches GitHub digest) | `DA47AEB7BA63EEC8564198BFDAC863CBD1BAB3D819457F9BC8A5B29CDFDE4F7E` |
| Installed PFT client | `DB04A0D61458BB6671E14B46FA803A9D080DDDA01C503A0088C80B1EF3C2F27D` |
| Candidate PFT client | `171F4BECD5B70C5555F0F53BA3C59BD2528A3340019B6B1377BC82D1334809C9` |
| Installed PFT server | `A354974CBD89706C576E39FF22FBBA6BC995844FED3906AA1337D7C035D9ED4B` |
| Candidate PFT server | `F690B1C84F176D18AA3B2747A5FF889067635D5364319693A4687EADF039A4F2` |

Temporary review directories (not permanent release assets):
`%TEMP%/arena-sain-451-review-f194eead845c46fcae0b11e8f62f29fa` and
`%TEMP%/arena-pft-0101-review-0d53ea6056904bec95db53b704d105c5`.
The former contains `harness/CandidateChecks.csproj`, the path-only Program
copy and official `release/BepInEx/plugins/SAIN/SAIN.dll`. Set process-local
`ARENA_SAIN_CANDIDATE` to that DLL for the candidate run, or to the installed
SAIN DLL for the baseline. The PFT directory contains both tagged source
archives, original release, and client method/field metadata snapshots.

The historical long-raid exceptions are assessed from the committed report;
its raw log has since rotated. This review makes no new runtime-performance
claim based on those previous log numbers.

Independent reviewers checked their respective SAIN/PFT conclusions against
the tagged sources and metadata, with no unresolved material findings.
