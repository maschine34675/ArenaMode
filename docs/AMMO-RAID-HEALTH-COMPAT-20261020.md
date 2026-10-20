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

## Initial offline verification

The initial implementation phase used `tools/AirPitChecks/NoDeploy.targets`.
It deployed no DLL, controlled no GPU process and installed no optional test mod.
Its read-only installation hashes are recorded in
`artifacts/ammo-raid-health-compat-20261019/installation-baseline.json`.
This describes that offline phase, not the later native tests below.

The client harnesses compile the production adapter against controlled native
boundaries and inspect the published foreign DLL's metadata. They do not prove
Mono detours, animation, or UI behavior in EFT. The SVM harness additionally
executes the actual published SVM DLL with installed SPT/Harmony assemblies,
in-memory fixtures and stubbed persistence. It is still an offline process.

Integrated regression results from that phase:

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
were committed in `235cc69`. No push or release is part of this batch.

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
were then examined separately rather than inferred from these fixtures.

## Native followup — 2026-10-20

The user released SPT-GPU-2 for deployment and testing. The
[dated native report](GPU-OPTIONAL-COMPAT-20261020.md) records exact versions,
builds, native observations, test-driver failures and attribution limits. Its
Factory scope does not certify other maps, every option or future versions.

The initial deployed client, companion and server were `235cc69`. The interrupted
Classic Movement/StatTrack lifecycle from the prior night was repeated without
a server restart: two protected Arena ends, four physical respawns, unchanged
StatTrack data across actual Arena shots, and ordinary tracking afterwards.
The stale end from the first probe was not reclassified or bypassed.

| Adapter / case | Bounded native result |
|---|---|
| CLA 1.2.0 | Ordinary load/unload/cancel and inventory-close continuation worked before/after Arena. Two confirmed pending-unload deaths completed replacement, followed by actual firing. Two living Deathmatch round swaps also completed, without proven pending work at those swaps. |
| SVM 2.2.3 | SaveLoot/Ignore ordinary ends, a disabled-override control and an ordinary Scav end completed. Both protected Arena intervals restored their entry snapshots and subsequent ordinary starts remained usable. |
| Demi 2.2.0, 1 HP Mode | Ordinary head-damage/native-kill controls preserved the player before/after Arena; Arena's native death and actual Give Up input reached normal death/respawn. |
| Combined one-companion PFT Zombies | Actual combat cleared the first wave; a living refill and a later dead-player wave restoration rebound CLA to the current body. Live ally spectating worked; entry gear/440 HP returned and the protected end completed. Demi's Second Chance remained unconsumed. |
| Solo Zombies and ordinary Second Chance | After the terminal-exit fix below, immediate and delayed solo deaths both completed protected exits. The following ordinary PMC still survived its first native kill, then consumed Second Chance and completed its debounce while remaining alive at 440 HP. |

The early solo-Zombie death exposed an Arena terminal-exit race: the delayed
native intro fade could replace the coroutine carrying GameEnd. It was not a
demonstrated optional-adapter failure. Commit `5780942` extends the existing
exact intro guard to a committed terminal exit. After its production-linked
checks and independent review, the new client was deployed to both roots:

- Client: `5780942`, SHA256
  `8A5C9A9852E2496095E838C70201E900ACB49FB0BEC91BA3BB90E4DF867455BD`.
- BigBrainCompat and server: unchanged `235cc69` builds.

The native retest exercised the intro guard, both protected exits and ordinary
Second Chance consumption. Its 21 driver steps had no automatic expectations:
actual returned values and separate log observations provide the evidence.
See [the intro-guard review](FAILED-START-INTRO-REVIEW.md) for implementation
and offline fault controls.

### Remaining native boundaries

CLA's pending-work **living** swap, 800 ms newer-operation race, preset/MultiSelect
cancellation, quick-load hotkey and measured movement-limit reset were not
established. The PFT observation covers one-companion Zombies and live spectating,
not a Tactical match or an actual killer replay. Demi's ordinary debounce was
observed after one second, not across a world/end transition within its 350 ms
window. Native kill hooks and guard diagnostics do not certify every lethal
damage type or destroyed-limb/medical interaction. Retry/late-callback cases
covered only by the offline harness remain offline evidence.

### Restoration and cleanup

At 10:10:43–10:10:53, six bounded helpers completed cleanup on development and
GPU2: CLA (6 files), Demi (4), SVM (43) and reviewed test sync duplicates were
removed after the earlier CM/StatTrack cleanup. Original files/data were
archived before removal. At the user's request, old SVM 2.2.1 was **not restored**;
its original backup remains outside mod discovery and no legacy adapter ships.
Eleven Arena test settings were restored. The test MP5 was returned through
native inventory UI to its original stash position (x=6, y=19), verified in
persisted data; the PFT group is empty. Unrelated mods and the permanent
Core/CH/UiBridge installation were preserved.

The independent final verification and root rerun passed 100 file/settings
checks. A clean restart reached MainMenu: 41 plugins and six libraries matched
both installations and the loaded modules. ARENA selection reached the map
screen; no additional raid was started in this clean smoke check. Client startup
had no errors. The server still reports two preserved, preexisting file-empty
LTTP/LootingBots directories. EFT and SPT are stopped again. No push or release
was performed.
