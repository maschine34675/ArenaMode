# Dedicated Molotov integration — 2026-10-15

The existing `feature/molotov-item` work through `93d614e` had not reached master.
The earlier October 15 live-supply change therefore still created a separate
RGD-5 instance. This integration replaces that placeholder with the existing
private `a12e00000000000000000001` **Arena Molotov**, including its original
bottle geometry, native-rendered icon, fire presentation and synthesized audio.
The historical branch's bounded GPU1 results remain historical evidence, not
acceptance of this new integrated build.

## One supply authority

`ArenaFireSupply` remains the only bonus supplier. It grants one private item
per live equipment tree, only with the enabled shared mutator, supported exact
Arena world/match/body, the BigBrain companion and usable native inventory/pools.
The historical server kit-supply addition was deliberately not merged. Kits
retain their ordinary two RGD-5s. Own gear follows the same one-item grant after
protected entry capture; a fresh restored equipment tree gets its one bonus.
No duplicate is granted on toggle, consumption or a round-number change.

Placement stays in free rig/pocket space without eviction. G initially selects
the added Molotov; later manual selection remains authoritative. The saved
`Arena incendiary prototype/Enabled` key stays shared by menu and F12 so an
existing opt-out is not reset by the old branch's different key. There is no
ordinary-RGD fallback when the private item or its native resources are missing.

## Registration and admission

The server's existing Preload registration and private flea exclusion precede
the native profile snapshot. It creates no trader, world loot or persistent
profile stock. Its private clone has zero native blast/fragment/flash/concussion
effects and no fuze audio or tripwire planting. The original RGD template is
unchanged. A finite native fuse remains for cleanup.

The confirmed raid response now includes `molotovAvailable`, true only for the
exact successfully registered object still present in the native item table
and passing all safety checks. The client accepts only a boolean true after a
successful raid confirmation and binds it to that exact active arm token.
Older servers, failed registration, an occupied/replaced private ID and stale
tokens cannot authorize a grant. Native client template properties are also
checked before resource preparation; a warm resource cache alone is insufficient.
The same ownership/readback gate protects model decoration and the untracked
explosion fallback, so a rejected foreign ID collision keeps its native behavior.
Already tracked own throws retain suppression through shutdown or revoked supply.

Only the exact granted private item can bind Arena fire. All explosions of the
private template remain natively harmless, even when no fire can be registered.
Ordinary RGD-5s always retain their native explosive behavior. Fire damage uses
the current common friendly-fire and spawn-protection boundaries; the merged
attributed-fire score guard prevents rewarding teammate kills. Optional visual
or audio failure does not remove an otherwise valid hazard.

## Model and release boundaries

The dedicated bottle uses original procedural meshes/materials and the installed
native grenade rig/animation/physics. Generated hand, icon and projectile
instances are decorated individually; native shared prefabs are untouched and
pool return restores original resources. This still references the installed
RGD rig internally, but never supplies an RGD item disguised as a Molotov.
No external sound or game asset is bundled. Existing reference/licensing and
pool lifetime details remain in the linked model and presentation reviews.

Both server and client must restart after deployment. No Core, Killcam, audio
file or configuration deployment is needed. Focused checks and exact fresh-build
native acceptance are recorded in the dated handoff and GPU integration report;
they do not imply every map, full-rig or PFT/fire-combat combination was repeated.

## Integration verification

Merge commit `b06d8fe` contains the integrated source. Its clean staged build
completed with zero errors (four existing client nullable warnings and two
existing server warnings). Only client and server were deployed:

| Artifact | SHA256 |
|---|---|
| Client | `74694DA3CFF91869B9C70239D6AA2743EBFB756C4A9AC57A22D1A82FC56F9E27` |
| Server | `A5921A299E35EA6C969D104FA12AA3743FB46F34D60EE9550702E0AB02116C53` |

Offline results: units **968 passed / 2 skipped**; fire/supply **138 assertions
and 25 rejected compiled mutants**; model/native IL **57 and 8**; item/native
registration **1807 and 12**; historical kit packing **1605 and 2**; native menu
**654**; presentation/audio **28 and three invalid-audio controls**. The final
model transcript's old success footer still says seven mutants; all eight
individual rejection records are present and the footer is corrected in source.

Independent adversarial review identified an unverified private-ID collision in
the global model/explosion hooks. Both now require the same confirmed capability
and harmless template readback as supply; tracked own throws remain suppressed
and pool cleanup remains unconditional. The reviewer repeated the affected
production checks after the fix and found no remaining P0–P2 issue. This is
source/offline evidence; native acceptance is recorded separately.

Fresh GPU2 Factory Deathmatch testing passed the focused supply/presentation
contract on these exact artifacts: two original RGDs off, one additional private
bottle on, no duplicate after repeated toggling, real G selection, bottle
icon/hand/projectile and floor fire. The subsequent original RGD retained its
native model and explosion with no Arena fire. Protected inventory/profile
values, all 304 saved config values and 12 PFT files were preserved; only the
native health-update timestamp changed. Parent verification repeated the saved
state comparison and inspected the screenshots/throw frames. See the
[native report](GPU-MOLOTOV-INTEGRATION-20261015.md) for capture identities,
log findings and the limited test scope; this was not a repeat of own-gear cold
pool, fresh-body refill or PFT fire-combat acceptance.

Ignored local evidence: `artifacts/molotov-native-item-20261015/`, the dated
`molotov-native-item-20261015-*.log` files, staged build
`artifacts/double-jump-build/569ce76bd2914cd6b827f149c3e6ef7a/manifest.json`,
fire mutation run `artifacts/molotov-checks/4ca2d22d632e44439c0e3aa2466ec1d2`,
model mutation run `artifacts/molotov-model-checks/87ff6461a14c4e739b54b6acfcdbbedf`,
and item mutation run `artifacts/molotov-item-checks/764618259cc54a0eb050ea21ee0a3fce`.
