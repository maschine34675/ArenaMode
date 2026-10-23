# Shared replacement-body adapters

Arena now requires Redeploy.Core **1.0.9 or newer**. The general Recoil Rework
Legacy, Continuous Load Ammo and Continuous Healing adapters have moved to Core,
where they also protect standalone Redeploy. Arena no longer installs or ticks a
second copy of these hooks.

`ArenaOptionalMods` preserves its existing admission order: whitelist, cached
server refusal, then Core's optional body-replacement contracts. No whitelist
rules or ordinary PMC/Scav admission rules change.

`ArenaMedicalBodyScope` is the only Arena-specific part of this migration. It
registers exact Arena bodies with Core's medical fallback, retaining weak leases
for delayed callbacks. Unrelated bots in ordinary raids do not receive that
fallback. Shutting down Arena disposes only this registration, never the shared
Core hooks.

Implementation, lifecycle rules and standalone test requirements are documented
in the sibling Redeploy repository's `docs/BODY-REPLACEMENT-COMPAT-20261023.md`.
The three adapter harnesses moved there as well. Arena keeps its admission-chain
verification under `tools/OptionalModAdmissionChecks`.

Earlier dated compatibility/GPU reports describe the previous Arena-owned
implementation. Their gameplay observations remain historical evidence, not
native acceptance of this migration. New in-game checks must load the new Core
and rebuilt consumer together; the version guard rejects an older Core.
Conversely, Core refuses an older Arena binary when it still owns the adapter for
an installed Recoil Rework Legacy, Continuous Load Ammo or Continuous Healing.
Install the rebuilt Arena and matching Core together to avoid duplicate ownership.

## Native validation 2026-10-23

SPT-GPU-2 tested ArenaMode `2a5c239` with Redeploy/Core `cac56a8` and Core 1.0.9.
No production code changed during testing. Recoil Rework Legacy 2.0.0,
Continuous Load Ammo 1.2.0 and the existing Continuous Healing 1.6.3 were loaded;
full status inspection confirmed matching loaded/local/GPU module identities.
Only Recoil's client package was used.

The standalone phase ran with Arena's client and server DLLs removed. Arena's
phase used the normal installation and a narrow temporary allow rule. This
does **not** validate the isolated Arena-session launcher, private server or
session switching. All gameplay used the dedicated test profile.

| Scenario | Observed result |
|---|---|
| Standalone Redeploy | Two naked death respawns with correct Recoil/camera and CLA owners; corpse recovery, weapon draw and real firing after the first; continuous multi-limb healing and closed-inventory magazine unloading. A second ordinary PMC in the same process had correct fresh owners and exited normally. |
| Factory Deathmatch | Initial kit and two death replacements retained exact current body/inventory/hands references. Real shots from three weapons, one Car activation healing both arms, and a completed native unload outside inventory were observed. |
| Solo Zombies with a supplied kit | Two living refills restored ammunition and rebound all owners. Wave 1 ended via the existing 100-second watchdog, wave 2 via a real Mosin headshot. After the second refill, actual firing and one Car activation healing both arms from 25 to 60 HP succeeded. |
| Fresh process after removing temporary mods | Recoil and CLA were absent; existing CH remained. Factory Deathmatch completed another death respawn, actual AK-74N/ADAR firing and one continuous heal of both arms. Native exit succeeded. |

The first Zombie refill was administrative, not a combat victory. One fire
command timed out during the immediate second refill; the native kill, score
and wave transition confirmed the kill separately. PFT was not tested.
An initial standalone corpse-interaction miss was resolved by corrected aim,
without a source change.

These results cover current ownership and usable hands/medical operations,
not every asynchronous seam. In particular, the DM unload had **finished before
death**; no pending-load callback race, pending-work living replacement or late
retired-body CH callback was reproduced. Forced failure, Arena-bot CH fallback,
the full recoil/animation matrix and future versions remain outside this native
acceptance. The second ordinary PMC was a world/owner control, not another
shot/load/heal test. Operator action observations are distinguished from the
saved owner-only snapshots in the private notes.

The logs contain no adapter rejection or failed replacement in the reviewed
windows, but are not clean overall. The temporary empty Arena server directory
caused a test-related ModLoader Fatal (`No Assemblies found`); the server
continued. Ordinary exit logged the previously observed inactive-`MenuScreen`
coroutine error. EFT traces retained Factory marker/transit diagnostics,
missing muzzle-jet materials, VSS foldable-component messages, an unknown item
type and six G36 utility-operation messages. Other startup warnings concern
serialization/Harmony, colliders, traders/Fence and companion-profile fallback.
No stack establishes a new adapter cause; absence of attribution is not proof
that all messages are harmless. Missing muzzle-jet materials also appeared in
the control without Recoil/CLA. Three Animator synchronization-layer warnings
appeared during the Zombie raid exit; no failed exit or replacement followed.

Temporary Recoil/CLA payloads, their configurations and the temporary allow rule
were removed from both installations. Existing CH was preserved, and original
CH/Arena/Redeploy configuration files matched their backup hashes after
restoration. The new Core and consumers remain deployed. The fresh post-cleanup
control loaded 40 plugins and six libraries with matching local/GPU/loaded
identities. After that control, the original configurations were restored again;
game and server were stopped. No additional DLL paths remained compared with
the baseline, and pre-existing DLLs were unchanged apart from the intended Core
and consumer updates. This is not a CH-absent control.

Ordinary test-profile death/disconnect lost its original pistol, medical item
and magazine; two temporary probe items were left on a corpse. No personal
profile or raw profile JSON was changed to undo those outcomes. This is not an
unchanged-inventory claim.

Detailed implementation-side results are in sibling Redeploy's
`docs/BODY-REPLACEMENT-COMPAT-20261023.md`. Private captures, execution notes,
log extracts and cleanup receipts remain under
`artifacts/shared-body-gpu-20261023/`, outside release assets.

## Default admission after testing

Following user approval, the shipped `assets/arena-whitelist.json` now includes
Continuous Load Ammo (`com.ozen.continuousloadammo`, reviewed client 1.2.0) and
Recoil Rework Legacy (`com.pein.camerarecoilmod`, reviewed client 2.0.0).
ContinuousHealing 1.6.3 was already listed. No user override is needed for these
clients; Core's version/API validation and explicit user denials remain in force.
Recoil's server package shares the client GUID and is also admitted if installed;
this does not certify that untested server component. The temporary test DLLs
are not reinstalled.
