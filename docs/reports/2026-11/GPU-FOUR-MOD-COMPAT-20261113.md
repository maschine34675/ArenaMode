# Four optional mods: native compatibility verification

Status: the scoped native checks passed with the final frozen candidate; cleanup
was independently verified and synchronization restored. This is not an exhaustive
compatibility claim.

## Environment and isolation

- Host: SPT-GPU-1, SPT 4.1, EFT 0.16.9.5.40743, UiBridge 0.9.9.
- A disposable profile was cloned before testing; original profiles and PFT data
  were backed up. The normal installation on the development host was not changed.
- GPU-1 synchronization was paused before staging the four release mods. Its
  original state was unpaused. GPU-2 was not used.
- Admission uses the candidate's shipped default whitelist. No personal whitelist
  override was present on the GPU host.
- Ignored evidence: `artifacts/four-mods-20261113/`, including the original 1,593-file
  manifest, prepared-profile metadata, release-stage manifest, loaded identities,
  raw runtime observations and path-only profile comparisons.

## Controls before the fixes

The original installed Arena/Core build was started with the same four foreign
releases. Factory Deathmatch loaded and an actual death produced a new local body.

- Player Encumbrance Bar displayed base limits `(33.8, 100.1)` and walking limits
  `(58.5, 111.8)`, whereas the live Arena body used `(67.6, 200.2)` and
  `(117, 223.6)`. Arena's carry-weight multiplier was 2. This reproduces the
  display mismatch without changing global EFT weight settings.
- Selecting a knife and leaning left after the body replacement reproduced
  `ArgumentOutOfRangeException` from `ProceduralWeaponAnimation.CurrentScope`,
  called by `hazelify.StanceSync.Patches.OnLeanPatchPostfix` at 10:32:49.533.
- The first protected round trip changed only the two health/hideout timestamps
  and appended the newly loaded Bot Callsigns entry to the server's normal
  installed-mod registry. Subsequent comparisons start after that registration.
- No DERP baseline failure was exercised: the initial randomized weapon did not
  provide the intended magnified-optic scenario. Its body-lifetime risk is
  established by the separate release/source review.

Evidence: `baseline-bar.json`, `baseline-physical.json`, `baseline-weight.png`,
`baseline-errors.json`, `baseline-bepinex.log`, `baseline-profile-diff.json`.

## Provisional candidate identity and first load

The first candidate was frozen before the later preparation-rollback improvement:

| Assembly | SHA-256 | Loaded and on-disk MVID |
| --- | --- | --- |
| ArenaMode.Client 0.5.0 | `B86CF03F58B97DC702433213819E7FCA2E65898AAA5B6CEA4DAE04543D007646` | `c6a15a8f-eca8-416c-a485-c4640cfa420a` |
| Redeploy.Core 1.0.23 | `BE06DA9C3E28347AD448E393FE17FB8E700C865151547BFE9F284801DC92F29E` | `b0466e75-8381-4d89-a523-494a0aa9f5d5` |

All four foreign releases loaded. Factory Team Deathmatch with four participants
started without gameplay errors. The chosen Overwatch kit carried an SVDS with a
scope; its physical carry limits remained doubled. The test was cleanly returned
to the main menu when another recently active host session required coordination.
No further input was sent pending host availability confirmation.

That protected profile round trip changed only
`/characters/pmc/Hideout/sptUpdateLastRunTimestamp`. The only error recorded was the
host's existing microphone-output initialization error. The unrelated ArenaMapPort
notice about absent source Arena assets was dismissed by title before using Factory.

Evidence: `candidate-status.json`, `candidate-first-load-errors.json`,
`candidate-short-profile-diff.json`.

The native event ledger retained all three actual TDM bot arrivals: USEC `golden`,
BEAR `_ChrisP_Bacon_`, and USEC `Nikitka_Magogi`. Each is present in that faction's
installed Bot Callsigns name pool. Server startup confirmed 1,011 USEC and 1,027
BEAR names installed. This supplies native Callsigns coverage despite the early
return to the menu. Evidence: `candidate-first-events.json`,
`candidate-first-server.log`, and the staged release's `nameData` files.

The interim filesystem audit confirms all 53 original PFT files and all original
canonical profile files retain their hashes. Startup did close an already pending
reward for another existing profile, at the server-start timestamp; those unrelated
reward changes will be preserved. Automatically rotated old SPT profile backups
are likewise not replaced wholesale.

## Final candidate: Arena

Frozen files in `candidate-r2/` were loaded on GPU-1, not built or installed into the
normal development installation. Loaded and on-disk MVIDs matched:

- Arena client: unchanged `B86CF03F…` and MVID above.
- Core 1.0.23: SHA-256
  `6E4F0BA1D32CACFE584F11C367E97A7957BE1F001142FB422770C712CC7C8696`,
  MVID `c66effad-400f-4697-8788-e94d777750e2`.
- Redeploy client 1.1.2: SHA-256
  `D03290B8AF1542707FB1F16951F7A4DE0D37403E9885219F5FA1BFA7941E380D`,
  MVID `746efa95-53cd-42a4-818b-a7492d748c84`.

Factory TDM, four participants, no score/time limit, killcam disabled. The final
process was `38095ab3…`. A deterministic death hook killed native player 2; the
native death/body ledger then recorded replacement player 8. The replacement's
UiBridge inspection handle was `bc43901a:6`, also returned by DERP's private cache.

- The inventory bar displayed the body's actual doubled limits: base
  `(67.6, 200.2)`, walking `(117, 223.6)`.
- After the death, installing the PSO-1 from the backpack onto the held SVDS through
  the native inventory action changed strength-adjusted weight from `15.889` to
  `15.2890005` kg and bar fill from `0.07936563` to `0.07636864`, without closing
  the inventory. The 0.6 kg change follows the profile's elite weapon-weight rule.
  This exercises the new inventory subscription, not just initial rendering.
- The earlier `giveItem` before/after exports were identical and are **inconclusive**
  for event delivery. They are retained as diagnostic artifacts and are not counted
  as acceptance. The native-install pair above is the actual event evidence.
- Leaning with the firearm set `Tilt=-5` and `LeftStance=true`. After switching to
  the knife, leaning still set `Tilt=-5`, with zero firearm scopes and
  `LeftStance=false`. Both paths were exercised again after the death. The original
  `CurrentScope` exception did not recur.
- Overwatch initially supplied a collimator, so the native inventory UI was used to
  remove its B-13V mount and install a PSO-1. Magnified ADS changed applied rendering
  from DLSS quality 1, ratio `0.580208361`, input `1114x627`, to quality 3, ratio
  `0.333333343`, input `640x360`. Releasing ADS restored `1114x627`. The same changes
  occurred on the replacement body.
- The complete protected profile comparison after leaving changed only
  `/characters/pmc/Hideout/sptUpdateLastRunTimestamp`.

Evidence: `r2-loaded-arena.json`, `r2-arena-events.json`,
`r2-bar-before-native-install.json` / `r2-bar-after-native-install.json`,
`r2-weight-before-native-install.json` / `r2-weight-after-native-install.json`,
`r2-respawn-firearm-lean.json`, `r2-respawn-knife-lean.json`,
`r2-ssaa-before-ads.json`, `r2-ssaa-ads.json`, `r2-ssaa-after-ads.json`,
`r2-respawn-ssaa-ads.json`, `r2-respawn-ssaa-after.json`,
`r2-respawn-derp-owner.json`, `r2-arena-profile-diff.json`.

The attempted friendly-marker screenshot was obstructed by a wall and did not show
the teammate or marker. Marker alignment is not claimed by this test.

## Final candidate: standalone Redeploy

ArenaMode.Client and its BigBrain helper were outside the plugin tree. The loaded
inventory confirms Arena was absent while the same final Core and Redeploy driver
were running. A disposable-only fixture equipped an existing scoped SR-25 on the
PMC and a copy with fresh item IDs on that profile's own Scav. No real profile was
edited. Factory PMC was started with bots disabled.

The PMC died through the native death hook. Selecting **Continue as Scav** created
a new physical player with the Scav profile. DERP's cache returned that new body.
The bar retained ordinary limits `(26, 77)` and `(45, 86)`, matching the new body's
physical values; Arena's doubled limits did not apply. Discarding the existing
water bottle through native inventory input, with the inventory kept open, changed
Scav weight `19.244` → `18.594` kg and fill `0.249922067` → `0.241480514`.
This exercises a profile distinct from the backend PMC and its live weight event.

The initial standalone ADS attempt is **inconclusive**: drawn hands and actual
optic ADS were not recorded, and its saved resolution remained `1114x627`. That
file is not ADS evidence. During the bounded rerun, the new Scav was observed with
empty hands despite carrying the scoped rifle. This explains why an ADS command
alone is insufficient, but does not retrospectively prove the first attempt's
hands state. The rerun explicitly drew the rifle, then verified `IsAiming=true`,
`CurrentScope.IsOptic=true`, one scope, and the SR-25 as the actual hands item.
Two separate saved snapshots showed quality 3, ratio `0.333333343`, `640x360` input.
Releasing ADS restored quality 1, ratio `0.580208361`, `1114x627`. The rerun process
was `f3accfb7…`; its native ledger records PMC death and replacement Scav player 2.

Evidence: `r2-loaded-standalone.json`, `standalone-events.json`,
`standalone-scav-bar-before.json` / `standalone-scav-bar-after.json`,
`standalone-scav-body-before.json` / `standalone-scav-body-after.json`,
`standalone-repeat-status.json`, `standalone-repeat-events.json`,
`standalone-repeat-scav-hands-ads.json`, `standalone-repeat-scav-hands-ads2.json`,
`standalone-repeat-scav-ssaa-ads1.json`, `standalone-repeat-scav-ssaa-ads2.json`,
`standalone-repeat-scav-ssaa-after.json`, `standalone-scav-ads.png`.

The final Arena and both standalone error exports contain only the host's known
microphone initialization error. No new compatibility gameplay exception appeared.
Preparation-abort rollback is covered by the fault-injection harness; it was not
artificially forced in the live client.

## Cleanup

EFT and the SPT server were stopped before cleanup. The reviewed
`cleanup-owned.ps1` removed the four temporary mods, their generated configuration
and name-list files, the disposable profile and its backups/reward data, BPS data,
and the three exact registry keys. A follow-up ownership audit found the profile's
PFT allegiance database and two own-Scav continuation journals. Their session,
PMC and Scav identities were checked before removing those three files and the
empty journal directory. No general journal/database directory was reset.

Independent verification found all 53 original PFT files and all three original
canonical profiles byte-identical, all 465 original plugin manifest entries present,
no additional plugin payloads, and no remaining probe identity in user data outside
logs/cache. Launcher settings were unchanged. The other profile's normal startup
reward closure and the server's automatic backup rotation were preserved.

The binaries/configs touched by this test were first restored, then reconciled with
the **current** normal development installation: another session had advanced its
Arena client, BigBrain helper and settings while this host was isolated. Copies kept
the normal files' modification times and were verified by SHA-256. This prevented an
older GPU backup from propagating over newer parallel work. This task did not write
the normal development installation or deploy the tested Core 1.0.23 there.

After the independent audit cleared cleanup, synchronization returned to its original
unpaused state. All 12 touched paths still matched the normal installation, whose
hashes remained unchanged after resuming. EFT and the server remain stopped.

Evidence: `cleanup-stopped.txt`, `cleanup-plan.json`, `cleanup-followup-owned.json`,
`cleanup-final-installation-audit.json`, `cleanup-final-catchup.json`,
`restore-sync-result.json`, `cleanup-after-sync.json`. Original profile/reward data
and raw diagnostic logs remain only in the ignored local artifact directory.

## Offline verification

Arena: 3,894 passed, three known environment-gated skips. Shoulder checks passed
37 behavioral assertions (42 including released-DLL inspection) and rejected
seven fault variants. Arena's encumbrance display passed nine assertions and
three fault variants, including a rerun after the shared fixture changed.
The verification runner confirmed the normal installation was unchanged.

Core: 628 unit tests passed on the revised source. DERP passed 29 runtime-Harmony
assertions and five fault variants. Encumbrance passed 128 assertions and eight
fault variants after the independent review found a preparation-cancellation
gap. Its final checks distinguish hidden subscribed panels from already closed
ones and restore the prior ownership after a refused body switch. Builds
succeeded with the pre-existing nullable/obsolete-API warnings.

The initial Core suite caught an unregistered committed-profile reader in the
source guard. Native engine order and focused ownership tests established that
this getter runs only after the game, body and player owner are committed; it
was added to the existing explicit reader registry. The final full suite passed.
