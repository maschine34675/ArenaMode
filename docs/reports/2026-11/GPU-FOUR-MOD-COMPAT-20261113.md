# Four optional mods: native compatibility verification

Status: in progress. Do not treat the source/offline checks or the short candidate
raid as native acceptance of every branch below.

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

## Remaining native acceptance

- Revised offline candidate `candidate-r2/`: unchanged Arena client above,
  Core 1.0.23 SHA-256
  `6E4F0BA1D32CACFE584F11C367E97A7957BE1F001142FB422770C712CC7C8696`,
  rebuilt Redeploy client SHA-256
  `D03290B8AF1542707FB1F16951F7A4DE0D37403E9885219F5FA1BFA7941E380D`.
  These revised Core bytes have **not** been loaded for the remaining tests.
- Final frozen candidate identity after the rollback correction.
- Bar thresholds and live inventory-weight updates before and after a real death.
- Firearm shoulder switching and knife leaning without the reproduced exception.
- DERP's actual rendering-scale change on magnified ADS and restoration on release,
  across two physical bodies, plus a friendly-marker observation where practical.
- Standalone Redeploy without ArenaMode loaded: live bar subscription transfer and
  optic behavior after replacement.
- Final profile comparisons and complete temporary-mod/profile/config cleanup,
  including PFT-data hashes and restoration of the original synchronization state.

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
