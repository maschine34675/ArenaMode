# Dedicated Molotov bottle presentation — 2026-10-12

The private Molotov template keeps EFT's installed RGD-5 rig, animation and
projectile physics as a dependency. Only returned instances for the exact
`ArenaMolotovItem.TemplateId` receive original bottle geometry/materials. No
native prefab, shared mesh, shared material, animation or game bundle is copied
into this repository or changed in memory.

## Visual ownership

`ArenaMolotovModel` wraps `ObjectsFactory.CreateItemAsync` for the exact custom
item during active Arena or its armed loading phase. The continuation retains
Unity's synchronization context, preserves native failures/cancellation, and
checks the Arena scope again on completion. This is a presentation-only loading
exception; a main-menu or ordinary-raid request is untouched. The server does
not give the item to permanent profiles or ordinary bot inventories.

The completed pool instance must be a non-stub `GrenadePrefab`, outside its pool
and with a valid native hierarchy. The role-model creation path sets `IsInPool`,
so shared prototypes are rejected. Only the instance's
`weapon_grenade_rgd5_LOD0` mesh and materials change. Native throwing transforms,
colliders and animator stay intact. Pin/lever renderers are hidden locally;
originally disabled renderers remain disabled after cleanup. The original
bottle mesh has three surfaces: amber body, beige cloth wick, orange label.
All geometry and materials are made by our code, with no imported art.

The native `ThrowingParts[0]` points at this same body transform, so EFT itself
hides the bottle in the hand when throwing. The separate
`CreateAndRegisterGrenade` postfix decorates only the custom item's new physical
projectile. The hand and world meshes have different baked offsets. Decoration
retains each original local mesh center rather than copying the hand model's
pivot onto the world model; the latter would otherwise be displaced by 39 cm.
Native projectile colliders retain the donor's dimensions. Bottle appearance is
slightly larger; it does not expand collision or damage geometry.

`ArenaMolotovModelLease` stores the original mesh/material array and hidden pin
renderers per instance. The base pool return prefix restores them before the
native push. A pool-get prefix also removes a stale lease if a caller skipped
normal return. Both run without an active-Arena requirement, and a lease's
`OnDestroy` releases generated resources for nonpooled projectiles or pool
teardown. Cleanup is idempotent; a later mod's replacement mesh/material array
is not overwritten. Mixed material arrays are restored per slot, preserving
foreign slots while removing every remaining owned material reference before
disposal. A failed native setter cannot interrupt native pool return/get; still
bound resources remain owned for retry instead of being destroyed under a live
renderer. There is no global model-instance registry and no owned
child object attached to the pooled native rig.

## Native icon path

EFT's `ItemIconCreator.RenderModel` creates a clean loot model through the same
`CreateItemAsync` call, so its normal camera renders the original bottle into
the inventory icon. `IconsHash.HashForItem` includes the template ID; ordinary
RGD-5 icons use a different cache key. No custom icon patch, embedded image,
bundle registration or inventory UI replacement is necessary. Appearance is
limited to active/armed Arena; native acceptance must still inspect actual
framing, material availability, hand clipping and the generated icon.

## Offline evidence and boundaries

`tools/MolotovModelChecks/Verify.ps1` compiles the actual model implementation
against explicit Unity/EFT doubles and exercises request isolation, armed
previews, disarming during async creation, role-model/stub rejection, native
fault/cancellation propagation, idempotence, failed-decoration rollback,
ordinary donor reuse, both pool hooks, destruction, third-party replacement
preservation, geometry bounds/index streams and the thrown model's baked offset.
It also inspects the installed Assembly-CSharp IL for the async factory/icon
call chain, concrete method signatures, role-model pool flag, native grenade
instantiation and virtual pool cleanup chain.

The first full run passed 40 production assertions plus 12 native IL contracts,
and rejected six separately compiled mutations (wrong-template admission,
missing completion scope, decorating a role model, missing mesh restoration,
lost world offset and leaked generated mesh). Evidence:
`artifacts/molotov-model-checks/e0168d3991504490831770fe0f314b1b`.
The harness neither builds nor deploys the Arena client/server. Source/IL checks
do not prove GPU rendering or native hand animation. Root integration owns the
real-reference client build, independent review and native test report.

Root adversarial review identified a partial-material replacement cleanup risk.
The follow-up restores individual still-owned slots without overwriting foreign
ones, retains bound resources after failed native setters and catches cleanup
errors at native hooks. Four additional production checks cover partial arrays
and failed-return/get retry; a seventh compiled mutation removes per-slot repair.
Final run evidence is recorded below after verification.

Follow-up verification passed 44 production assertions and 12 native IL
contracts, with all seven compiled negative controls rejected at their intended
assertion. Evidence: `artifacts/molotov-model-checks/
6af701be10df4654bbb6546204f485d4`. Pool cleanup still does not promise recovery
from permanently failing third-party native setters; it preserves ownership and
retries rather than freeing a referenced mesh/material or breaking the native
pool method. The final integration report owns deployment and visual acceptance.

Independent root review accepted the corrected model on 2026-10-12 with no
unresolved P0-P2. The review checked exact-template/loading scope, async native
semantics, private thrown instances, per-slot material preservation, retained
ownership after cleanup failure and the unconditional pool restoration hooks.
