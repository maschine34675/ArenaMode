# Native model repair integration — 2026-10-19

The request covers model/animation failures caused directly or indirectly by
ArenaMode, Redeploy or Killcam. Two own-code issues were confirmed and corrected;
other native signatures were investigated without suppressing their errors.

## Confirmed corrections

- Arena `61a8c9a`: exact owner/body identity replaces the misleading lookup through
  the often-empty optional body profile. Unknown identity and retained destroyed
  registry objects are explicitly distinguished. Fault-only utility snapshots
  include controller/operation/item ownership and bounded animator state, only
  under the existing DeveloperMode, exact-raid admission and report budget.
  [Implementation and checks](MODEL-DIAGNOSTIC-REPAIR-20261019.md).
- Killcam `78e3fa3`: build the replay body from owned visual transforms, meshes,
  remapped skin bones and LODs. Native player/item scripts are never cloned;
  the global Unity logger is never muted. Partial construction/source hiding
  rolls back safely. Recording, camera timing and respawn hand-off are unchanged.
  See the sibling Killcam repository's `docs/SCRIPT-FREE-PUPPET-20261019.md`.
- No Redeploy source or installed binary change was required by the evidence.

## Attribution and remaining limits

The [cold ordinary-PMC/PFT controls](GPU-MODEL-CONTROLS-20261019.md) reproduced
four weapon-preview NRE records and four undisposed preview-body records with
Arena client/companion, Killcam and standalone Redeploy absent. Shared Core was
present but unpatched; Arena server and other mods remained. These clients are
not necessary causes of the reproduced failures. The native stack reaches
UnityAnimatorWrapper.GetParameter; the exact null cache/member was not sampled.
The separately source-proven PFT group-preview lifetime lacks the native Close
registration. No global model cleanup or parameter substitution was introduced.

The historical utility timeouts, zero animation hashes, null-hands message,
JetMaterial, M32 clipping, Benelli foldable and exit-time animator warnings do
not yet have a proven own-mod cause. Source paths and discriminating controls:
[animation](MODEL-ANIMATION-INVESTIGATION-20261019.md),
[pooled assets](MODEL-POOL-INVESTIGATION-20261019.md),
[preview ownership](MODEL-PREVIEW-INVESTIGATION-20261019.md).
A timeout snapshot does not prove animation progress or missing event data.
The candidate reproduced six real Deathmatch CheckMagazine faults with the
correct live owner/body and current, non-disposed hands controller. Different
sampled animator states and valid consumer bindings narrow the investigation,
but do not identify the missing event's cause. The animation report records a
specific, unproven search-sprint interaction and the missing same-operation
temporal evidence. No gameplay workaround was introduced on that hypothesis.

## Independent offline checks and deliberate deployment

The parent inspected production code, native API paths, harnesses and injected
failures, then reran checks independently:

- Native fault diagnostics: 138 assertions; 17 behavioral mutants rejected.
- Puppet construction: 14 behavioral scenarios; 8 defective variants rejected.
- Existing Killcam hand-off: 72 checks.
- Arena unit suite: 1,179 passed, two existing optional skips, zero failures.
- Frozen Arena client/companion Release build: zero errors, four existing
  nullable-context warnings. Killcam Release: zero errors/warnings.

All builds used explicit NoDeploy overrides. Only after review, the standard
helpers deliberately deployed the client/companion and Killcam at 10:31.
Core, standalone Redeploy and Arena server hashes remained unchanged. Private
`artifacts/model-errors-20261019/parent/` contains frozen-build identities and
before/after hashes. Source commits above identify the runtime; later evidence
or index commits do not alter those tested binaries.

## Native candidate acceptance

Fresh GPU2 process `b734328f` loaded the frozen three DLLs with matching VM,
host-disk and process identities. The unowned Raid Review arrival documented
in the control report was also loaded and remained untouched; this is not a
claim of a mod-free process or an unchanged whole plugin directory.

Five genuine Deathmatch deaths exercised the script-free puppet and respawn.
The parent independently inspected all 362 components of the first puppet:
250 Transforms, 44 MeshFilters, 44 MeshRenderers, 13 SkinnedMeshRenderers and
11 LODGroups, with no native scripts or Animator. Sampled skin/root bones point
inside the owned hierarchy. Separate runtime samples show the replay body and
weapon moving. After playback, no KillcamPuppet remains; the new local body
fired a confirmed SKS shot. A late frame of the fifth replay visibly shows the
clothed body, head/helmet and held rifle. Earlier views were occluded/off-axis,
not evidence of missing meshes. This accepts the observed model, not every
equipment variant, shader or LOD transition.

PFT Tactical completed two 5v5 rounds, one complete follower rebind and a genuine
second-round death replay. The first rebind followed a surviving local player;
the second death ended the match. This does not claim a post-death third-round
PFT replacement. See [the full native record](GPU-MODEL-FIXES-20261019.md).

The parent independently recounted the complete 197 warning/error records:
163 native EFT Errors, 23 BepInEx Warnings and 11 server Warnings. These are not
clean logs. Ten duplicate event-consumer records occurred inside Core's sixth
native replacement creation, after the fifth replay. Source review shows that
the setter also logs a repeated subscription by the same instance and then
overwrites the consumer. No own cleanup violation is established without the
missing stack and container/consumer identity. The pool report preserves this
unresolved native-creation signal and distinguishes missing handbook prices
(including the deliberately private Molotov) from model/animation failures.

Before manual restoration, both Arena runs preserved eight protected profile
sections and all twelve PFT files. Health changed only its UpdateTime; HP and
other health values matched. Explicit cleanup then restored the original whole
profile, twelve PFT stores and five configs on VM and GPU2. The parent rehashed
all 36 files independently, with zero differences, and verified eight final
candidate/untouched binary hashes. Party and temporary hooks were cleared;
EFT and server were stopped. The parent separately confirmed EFT unavailable
while the host remained reachable. No push or release was performed.
