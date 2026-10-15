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
