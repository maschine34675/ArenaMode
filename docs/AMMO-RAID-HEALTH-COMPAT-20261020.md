# Continuous Load Ammo, SVM and Deminvincibility integration

Date: 2026-10-19/20. This is the third bounded implementation batch following the
[Forge top-200 survey](COMPATIBILITY-SURVEY-20261019.md). It does not turn that
source audit into native acceptance of every optional mod or setting.

## Behavior

- **Continuous Load Ammo 1.2.0+:** bind the loading controller to the exact local
  Arena body, world and inventory. Dispose the old controller before registering
  its replacement. Delayed state restoration and magazine-process completion
  carry body/controller and operation ownership; they cannot finish a newer
  load or clear the new static controller. The foreign continuous-loading UI,
  cancellation and ordinary raid path remain in use.
- **SVM 2.2.3+:** bypass the foreign SaveLoot/Ignore short-circuit only for the
  exact protected Arena end callback. The existing snapshot/end service remains
  authoritative, including rejection of stale or invalid identities. Ordinary
  tracked ends run the real SVM callback once within their existing transaction;
  concurrent requests share it and completed retries retain its response. This
  is necessary because skipping the outer callback otherwise leaves Arena's
  ordinary lifecycle open. Unknown callback topology cannot be replayed.
- **Deminvincibility 2.2.0+:** exact local Arena health controllers retain native
  death and body-part destruction. The one-HP clamp is bypassed for that body;
  foreign Second Chance/global debounce state is never reset. Custom damage,
  medicine and fracture settings remain effective. Retired-body leases cannot
  be borrowed by a different body/controller/world.

Each adapter validates published identities and native/foreign APIs. Newer
versions are attempted with a warning only while those checks pass. Missing
unsafe contracts reject Arena, not ordinary PMC/Scav selection. No foreign mod
or configuration is bundled or edited. The SVM server adapter is optional;
Continuous Load Ammo and Deminvincibility are soft client load-order entries.

Detailed contracts, source pins and harness boundaries:

- [Continuous Load Ammo](CONTINUOUS-LOAD-AMMO-COMPAT-20261019.md)
- [SVM](SVM-COMPAT-20261019.md)
- [Deminvincibility](DEMINVINCIBILITY-COMPAT-20261019.md)

## Verification boundaries

Builds in this batch use `tools/AirPitChecks/NoDeploy.targets`. No current batch
DLL has been deployed, no GPU process has been controlled, and no new optional
test mod has been installed. Read-only installation hashes are recorded in
`artifacts/ammo-raid-health-compat-20261019/installation-baseline.json`.

The client harnesses compile the production adapter against controlled native
boundaries and inspect the published foreign DLL's metadata. They do not prove
Mono detours, animation, or UI behavior in EFT. The SVM harness additionally
executes the actual published SVM DLL with installed SPT/Harmony assemblies,
in-memory fixtures and stubbed persistence. It is still an offline process.

Integrated regression results:

| Check | Result |
|---|---|
| CLA production-linked checks | 81 assertions and 25 compiled negative controls passed in the independent rerun |
| SVM published-DLL checks | 124 assertions, 8 absent-SVM and 8 unsupported-topology assertions; 14 compiled negative controls passed in the independent rerun |
| Demi production-linked checks | 121 assertions and 27 compiled negative controls passed in the independent rerun |
| Unit suite | 1,232 passed; 2 pre-existing installation-dependent cases skipped; no failures |
| Native menu harness | 752 assertions passed |
| PitFireTeam harness | 370 assertions passed after the ordinary-end changes |
| Optional-mod admission | 58 assertions and 14 compiled negative controls passed |
| Client clean build | Passed; four existing CS8632 nullable-context warnings |
| BigBrain companion build | Passed; no warnings |
| Server clean build | Passed; existing CS8620 and MUD0002 warnings |
| Installation comparison | All 16 recorded DLL/config hashes unchanged across development and GPU2 |

The two skipped unit cases are
`InstalledFactoryCandidateHasSeparatedGroundSpawns` and
`InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline`; they are not
claimed as passed. Final logs and the before/after installation manifests live
under `artifacts/ammo-raid-health-compat-20261019/`.
The separate implementations are committed as `0005532` (Demi), `8cd9881` (CLA)
and `e4f37d6` (SVM). Shared client startup/admission and this integration record
follow in their own commit. No push or release is part of this batch.

Independent reviews covered the other implementer's production files and test
boundaries, followed by root source review and reruns. Concrete findings fixed:

- An old CLA task could broadcast into a successor or a newer same-body load.
  Capture by inventory/controller and separate task epochs replace that global
  notification for Arena-owned tasks. Effective postfix order is checked even
  for synchronously completed tasks. A revoked registry cannot revive a ticket.
- HarmonyX still invokes later prefixes after an earlier prefix returns false.
  SVM's outer invocation therefore needs exact deferral while the inner callback
  runs its rules. Physical callback/request/route identity and one-use permits
  prevent an unrelated nested call from borrowing the transaction.
- Known patch identities alone did not prove their effective order. SVM now
  checks the actual sorted list, rejects unknown outer hooks, and revokes future
  bridge use after drift. Pending/completed transactions keep a frozen decision
  and exact retry ownership; ordinary ends also validate without an Arena poll.
- Future foreign HarmonyX IL manipulators must be part of the inventory, not
  silently omitted. The client contracts and negative controls cover them.

No remaining blocker was found in the reviewed offline scope. Native Unity
timing, Mono patch behavior, actual SVM presets and complete raid transitions
still require the following separate acceptance work.

## Native followup, after host coordination

First finish the interrupted Classic Movement/StatTrack clean lifecycle probe
and its temporary-mod/settings cleanup, following
[the existing handoff](GPU-MOVEMENT-STATS-COMPAT-20261019.md). The prior server
restart invalidated the process-local arm; never bypass that rejection or reuse
it as a successful protected end. Do not overwrite an occupied GPU host.

For this batch, capture exact build IDs, foreign versions, original settings,
profile/snapshot hashes and mod-owned files. Test each combination separately,
then together, through ordinary PMC -> Arena -> ordinary PMC in one process:

1. **CLA:** start/stop load and unload; close inventory while loading; replace
   the live kit/body and die twice while work is pending. Start a new load before
   an old 800-ms continuation resumes. Confirm weapon return, movement limit,
   preset/MultiSelect cancellation and no stale controller/event errors.
2. **SVM:** enable SaveLoot, then Ignore result conversion, separately. Ordinary
   ends must retain their configured outcome and permit the next start. Arena
   must restore its entry profile and release its arm. Repeat end requests only
   through the legitimate test path; a stale protected marker must still reject.
   Include an unmodified SVM-disabled baseline and a normal Scav end.
3. **Demi:** enable Keep1Health and SecondChance separately. Arena damage,
   destroyed limbs, Give-up, respawn and solo-Zombie terminal death must reach
   the normal Arena rules. Normal PMC must retain the configured protection.
   Exercise a normal-raid debounce that completes near a world change; keep
   custom damage/medicine/fracture values and their saved files unchanged.
4. Where applicable, include a living and dead-player PFT round/wave transition,
   replay, real protected exit and a subsequent ordinary raid. Record unsupported
   future-hook rejection separately from native feature acceptance.

Restore settings and archive/remove only owned temporary test files afterwards.
Do not publish/release these changes solely on the offline results.
