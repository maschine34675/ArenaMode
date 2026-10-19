# Native pool and asset investigation, 19 October 2026

Source attribution only. No production source, build, deployment, GPU state or
commit was changed by this investigation. No new native acceptance is claimed.
The parent task owns the fresh controls and any subsequent repair decision.

## Result

The recovered evidence and exact local decompilation do **not yet demonstrate
an ArenaMode, Redeploy or Killcam corruption that causes these three families**.
Arena's kit creation and Core's replacement bodies can cause more native asset
construction or equipment binding, exposing the faults more often. That is an
indirect trigger, not evidence that removing preloading or respawning fixes the
underlying object state.

| Family | Established mechanism | Current attribution / repair decision |
| --- | --- | --- |
| `JetMaterial == null` | Native construction of a new weapon pool instance calls `FirearmsEffects.Init`; its selected `MuzzleManager` already compares null for `JetMaterial`. Six contextual samples are three MCX and three Remington 870 instances. | Native asset/reference fault or an unobserved earlier mutation remains unresolved. No owned material writer/deleter found. Do not synthesize a material, suppress the log, or remove required preload. |
| `Unknown custom item type: 0` | Serialized M32 LOD1 `CustomItem._itemType` is zero, absent from the native interest-slot dictionary. The same headset object is later active on Bot19. | Strong invalid prefab-data hypothesis. Native item-pool persistence and repeated dressing explain the lifecycle without requiring corruption. No owned enum/table writer found. Do not guess `Headphones` or call the inactive samples harmless. |
| Benelli `no foldable found` | Nonanimated root `IDress.Init` requests a root-only `FoldableComponent`. The stock item can carry a component while the base Benelli weapon does not. | Strong native view/template-contract mismatch hypothesis; exact failing stock-view class and runtime item still need capture. No own-mod component removal or template-flag rewrite found. Do not add a component or change item semantics merely to satisfy the logger. |

The existing Killcam body clone temporarily disables the **global Unity logger**.
That is an observability defect and a separate source-review finding, not a
demonstrated cause of the three captured families. The investigation does not
endorse that implementation or extend it into a repair.

## Evidence identity and limits

- Current checked bases: Arena `36e72b9b32cd4bc95780aa6dabdbc10238eada0e`,
  Redeploy `d1d7240a5959cb193efe5da4c31bf44d99f8fe2e`,
  Killcam `8c8ee4586a8b9f4cdb531c06f2ca8b4c4045d343`.
- Native source: `D:\SPT41\Development\_decompiled`, EFT 40743. The installed
  database inspected here is the **local**
  `D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json`, not a claim
  about the GPU process's potentially modified runtime template dictionary.
- Historical capture: 18 October GPU2 session beginning 14:59:20 local,
  recovered by the parent into private ignored
  `artifacts/model-errors-20261019/historical-errors.json` and
  `historical-context.json`. Original release/loaded identities and scenario
  chronology remain in [GPU-LOG-FOLLOWUP-20261018.md](GPU-LOG-FOLLOWUP-20261018.md).
- Independently recounted the prior capture through 15:11:31.445: **73** error
  records, including **25** JetMaterial, **3** custom-type and **6** Benelli
  foldable messages. The recovered selection has **74** errors because another
  JetMaterial record at 15:31:42 lies outside that report's window. It must not
  silently change the old report's total.
- No changes between the historical Arena `1ffe0b5` and current base in the
  inspected preloader, optic guard, corpse cleanup/weapon registry, Akimbo view
  or native diagnostic files; Core's inspected RespawnEngine likewise has no
  difference from `aca6e7a`. The compared Killcam replay files changed namespace
  references and the owned visual-root names in the rename since `00939e5`;
  current source was inspected directly.
- The plain JetMaterial, clipping and foldable errors contain no call stack.
  Context supplies exact objects but no creation-request owner. Native call
  chains below are source-derived, not retrospectively measured stacks.

## JetMaterial: construction precedes equipment and replay ownership

Exact native chain, with paths relative to `_decompiled`:

1. `EFT.AssetsManager/AbstractAssetsPool.cs:144` creates a role model from the
   resource, then invokes its role-model initialization. At `:309-330`, each
   pool instance is cloned, initialized, inherits role-model data, receives
   `OnCreatePoolObject`, and is pushed into the pool.
2. `WeaponPrefab.cs:527-540` caches native objects, creates the managed
   `Firearms` component for a weapon, registers it and calls the base pool
   callback. `EFT.AssetsManager/AssetPoolObject.cs:324-331` invokes each
   registered component's `OnCreatePoolObject`.
3. `EFT/Firearms.cs:207-213` adds `FirearmsEffects` and calls its `Init`.
   `FirearmsEffects.cs:36-51` finds a `MuzzleManager`, assigns its hierarchy,
   clones a **nonnull** JetMaterial, otherwise emits the error, then updates
   muzzle effects. This is not a shot, reload or hands-operation callback.
4. Weapon prefabs are one of the native pool types marked active-in-pool
   (`EFT.AssetsManager/AssetsPoolStaticConfig.cs:100`). Creation uses the
   inactive parent so their activeSelf flag can be true while activeHierarchy
   is false. This exactly fits the captured construction subjects.

Captured identities in the original BepInEx file:

| Local event | Original line | FirearmsEffects / MuzzleManager / weapon object |
| --- | ---: | --- |
| DM 15:03:39 | 16072 | `-1650410` / `-1650020` / `-1650014` (MCX) |
| DM 15:03:39 | 16073 | `-1650994` / `-1650604` / `-1650598` (MCX) |
| DM 15:03:39 | 16077 | `-1652808` / `-1652384` / `-1652378` (MCX) |
| PFT 15:05:47 | 16295 | `-1934326` / `-1933956` / `-1933950` (870) |
| PFT 15:05:54 | 16305 | `-1962114` / `-1961744` / `-1961738` (870) |
| PFT 15:05:54 | 16307 | `-1969512` / `-1965170` / `-1965164` (870) |

All six are under `POOLS/Unloadable/<weapon bundle>/InactiveGameObject`.
They are separate native weapon instances. `body=none` is expected during
construction; it neither identifies a ghost nor proves permanent disuse.
The remaining 19 historical errors within the old window have no weapon
context, so they cannot all be assigned to MCX/870 or a particular mod request.

Null has two possibilities in Unity: an actually missing reference or a
destroyed Unity object that compares null. The observer only tests Unity's null
operator. A fresh control should record managed-reference null separately from
Unity null, the source/role-model material, and the resource key. Source search
finds the only native assignment in `FirearmsEffects.Init` (copying an existing
material) and no Arena/Redeploy/Killcam JetMaterial mutation. In particular,
`MuzzleManager.OnDestroy` (`:171`) unregisters particle containers; it does not
destroy JetMaterial.

`MuzzleManager.SetT`, `Shot` and `UpdateJetsAndFumes` guard material access
(`:183`, `:203`, `:341`). Other particles may still render, but a null material
skips the jet-mesh update. Thus successful shooting does not certify complete
muzzle visuals, and inactive construction does not prove harmlessness.

## M32: a persistent real equipment object, not three ghost copies

`CustomItem.cs:26-29` declares `_itemType` as serialized asset state.
`EClippingCustoms.cs` defines values 1, 2, 4, 8 and 16; zero is not a recognized
type. `CustomItem.Init` (`:68-77`) replaces its subscription set, stores the body,
then logs and returns before registering interest-slot callbacks when the type
is absent. The headset may remain visible while its expected clipping updates
are missing. The captured family is **CustomItem**, not CustomItemSkin; the
latter has an analogous check but is not identified in these three records.

All three context messages identify `CustomItem#-1549464` on
`item_equipment_headset_m32_LOD1#-1549456`, within the same headset
`#-1549430`:

- Original line 16076, DM 15:03:39: inactive headset still under its native
  `POOLS/DontDestroy` container, body `-1647746` supplied to `Init`.
- Line 16306, PFT 15:05:54: the **same headset** under that same persistent
  item pool, body `-1954238` supplied to `Init`.
- Line 16451, PFT 15:07:13: same headset parented under the head bones of
  body `-1954238`, active Bot19 owner `-1955284`, `inPool=False`, `stub=False`.

This ordering has a precise native explanation:

- `EFT/PlayerBody.cs:417-427` invokes each `Dress.Init(playerBody)` **before**
  setting the model parent and active flag in the ordinary non-DressItem
  equipment branch. `EFT.Visual/Dress.cs:20-36` calls `CustomItem.Init` on its
  renderers. A legitimate equip can therefore log while the acquired model
  still looks like an inactive pool child.
- `SlotView.CreateAndParent` can run again for an existing model, including
  `ChangePositionAfterBackpackChanged` (`PlayerBody.cs:334-338`). A later active
  observation is consistent with reinitialization; the historic log does not
  specify which trigger caused that particular call.
- `EFT/ObjectsFactory.cs:593-635` retains `ResourceType.Item` pools across raids
  but not weapon pools. `UnloadPools` (`:394-425`) returns objects from retained
  pools rather than destroying those pools. The same M32 object across DM/PFT
  therefore does not establish an Arena or Core leak.

The emitted `currentWorldBody=unregistered` is the diagnostic lookup's result,
not authority to declare Bot19 invalid. It is not used in this attribution.
No own-mod setter of `_itemType` or mutation of `CustomItem.InterestSlots` was
found. The diagnostic hooks only read these fields. A source-prefab/role-model
inspection and ordinary-PMC equip are the discriminating controls; rewriting
zero to 8 from the object's display name would be unsupported asset repair.

## Benelli: root-only component lookup versus stock-owned folding

Both `EFT.Visual/FoldableStockView.cs:29-39` and
`EFT.Visual/RetractableStockView.cs:29-39` return immediately for animated views.
For a nonanimated view they call `item.GetItemComponent<FoldableComponent>()`
and emit the identical error if it is absent. This lookup is on the **passed
item**, not its descendants: `EFT.InventoryLogic/Item.cs:740-758` checks its own
component list and its own template's readonly components.
`EFT/ObjectsFactory.cs:1131-1134` supplies that item
to each root `IDress` after creating the view and assembling its mods.

The local database gives:

| Template | Native fields | Consequence under native constructors |
| --- | --- | --- |
| Benelli M3 root `6259b864ebedf17603599e88` | `Foldable=false`, `Retractable=false`, `FoldedSlot=mod_stock`; both stocks allowed | `Weapon` adds no own FoldableComponent. |
| Telescoping stock `6259c3387d6aab70bc23a18d` | `Foldable=true`, `Retractable=false` | `Stock` adds a component to the stock item. |
| Fixed stock `625eb0faa6e3a82193267ad9` | both flags false | Stock adds no component. |

`EFT.InventoryLogic/Weapon.cs:975-982` and `Stock.cs:14-20` implement those
constructors. The separate `Weapon.GetFoldable()` (`Weapon.cs:970-972`) searches
children, but neither failing stock-view initializer uses it. Consequently,
having a valid telescoping stock does not satisfy a root-only lookup on the
weapon. The captured object's name is the Benelli container, consistent with
that mismatch. The record alone cannot distinguish the two identical log sites
or establish the runtime template flags; those must be inspected before calling
this a confirmed intrinsic prefab defect.

Arena's `KitBuilder` creates items through native `ItemFactory.CreateItem`
(`Kits/KitBuilder.cs:106`, `:306`, `:399`) and only restores `Folded` on an
already existing component (`:475-479`). It does not remove components or
rewrite Foldable/Retractable template flags. Core creates native replacement
players, and Killcam's mesh weapons never initialize IDress. No own-mod cause
of a missing Benelli root component was identified.

## Owned lifecycle audit

| Area inspected | What it actually does | Causal conclusion / remaining limit |
| --- | --- | --- |
| `Kits/KitBundlePreloader.cs:215-248` and loading-screen `:331-341` | Collect native template resources, call `LoadBundlesAndCreatePools(Raid, Local, ...)`, await the task and retain native ownership. In-raid requests wait for overlapping loading work and cache warmed templates. | Can request additional weapon pool construction and thereby expose JetMaterial. It neither edits the material nor supplies a replacement prefab. Removing it risks genuine missing-pool hands failures. Historic context cannot distinguish its own load from native pool expansion. |
| `Kits/KitOpticPreloadGuard.cs:164-218` | Identifies the exact requested registered WeaponMod pool and brackets only unmounted OpticSight enable/disable publication. A containing WeaponPrefab excludes suppression. | No writes to model materials, clipping values or foldable components; does not skip weapon initialization. No bridge from this guard to the observed fields found. |
| Redeploy `Redeploy.Core/Engine/RespawnEngine.cs:150-203` | Unregisters the old body, invokes the driver's equipment strategy, and calls native `game.vmethod_3` with the profile to create a replacement. | Additional native model/equipment creation is expected. No weapon-prefab, JetMaterial, CustomItem enum or Foldable deletion was found. Core timing near an error does not identify an invalid field writer. |
| Arena corpse cleanup `Bots/CorpseCleanupService.cs:1489-1545`, `:1632-1697` | Disposes a releasable Player before native corpse destruction; uses native IsStub destruction routing for the body. Gear stripping removes top-level items with paired notifications and lets native SlotViews return the views; held weapon and secure container are excluded. | Returns models for reuse but does not edit the three fault-producing fields. Existing checks retain live/native ownership. No source path was found that converts a healthy role-model field into these bad values. This is not exhaustive runtime certification of cleanup. |
| `Bots/CorpseWeaponRegistry.cs:78-110` and corpse visibility | Validates current held item/loot identity before manipulating remembered weapon visibility; pooled views can leave the corpse. | Visibility ownership is not a JetMaterial/enum/component writer. No evidence connects these changes to the three fields. |
| `Combat/Akimbo/AkimboView.cs:85-120`, `:288-311` | Builds new transform and MeshRenderer/MeshFilter objects with shared mesh/material references; skips native ammunition pool children; owns and destroys only its visual roots/material. | No native WeaponPrefab, FirearmsEffects, CustomItem or IDress components are copied or initialized. Cannot directly produce these native init messages. |
| Killcam `src/Replay/WeaponGhost.cs:75-100` and `GhostPuppet.AttachWeaponMeshes:121-149` | Create plain mesh/material-reference copies of held weapons. | No native weapon pool construction or IDress/effect initialization. Destroying these copies does not destroy shared materials. |
| Killcam `src/Replay/GhostPuppet.cs:54-101`, `:250-300`, `:421-440` | Raw victim-body clone under an inactive holder, strips MonoBehaviours/physics before activation; eventually destroys its own holder. It never returns that clone to a native pool. | Raw body cloning can invoke serialization callbacks even while inactive. The code explicitly acknowledges errors during that call and disables the global logger. Do not infer successful serialization from silence. None of the captured six weapon instances or M32 instance has the puppet hierarchy. No demonstrated mutation of original pool data was found. |
| Killcam `src/Replay/LivePlayerHider.cs:77-149`, `:160-240` | Toggles and restores renderer/light enabled state. | It does not clear materials, alter enums, construct model pools or remove item components. Visibility and stale-reference behavior would require their own evidence, not these messages. |

Killcam's `Debug.unityLogger.logEnabled=false` window (`GhostPuppet.cs:64-72`)
is global even though synchronous and restored in a finally. It is not a
diagnostic filter keyed to known harmless errors. A bounded future correction,
if the parent includes this established own-code issue, is to replace raw
script-bearing body cloning with owned transform/mesh/skinned-mesh visual
construction, then remove the logger toggle. That requires native body,
equipment, bone mapping and destruction checks. Simply removing the toggle
would reveal the acknowledged serialization errors, not repair them; leaving
it in place prevents a clean-log proof for that interval. No such implementation
was made in this source-only leaf.

## Discriminating controls for the native owner

These are proposed controls, **not completed runs**. Use a fresh process for
cold-asset controls; native retained Item pools otherwise contaminate comparisons.
Record loaded module identities, exact runtime item trees and merged logs for
each bounded window. Keep profile/store protection and temporary-setting
restoration under the parent task's existing protocol.

| Control | Required observation | What it discriminates |
| --- | --- | --- |
| Ordinary PMC, Arena unarmed, no replay or respawn; native MCX and 870 equipment | Exact bundle/resource, source and role-model JetMaterial reference/null state before each new instance; first construction, successful reuse, and real muzzle visuals when firing. | Error before any own-mod lifecycle and matching source-null state supports native asset fault. First healthy then damaged only after a specific owned action points to a lifetime bug. Merely shooting without an error is inadequate because logging occurs at construction. |
| Ordinary PMC equip M32, then native unequip/re-equip | `_itemType` on source, role model and equipped LOD1, object IDs, native body/slot ownership. Include another headset with a valid enum as positive observation control. | Source zero supports bad asset data; healthy source then zero after owned processing supports a mutation path. Reuse separates the same retained object from independent bad clones. Inspect clipping with compatible conflicting headwear as well as log presence. |
| Native Benelli nonanimated sling/equipment or preview, each allowed stock | Actual IDress concrete type, passed item/template, root versus descendant Foldable component, runtime template flags; compare held animated view. | Tests the root-only mismatch directly. Animated silence is expected by source and cannot clear the nonanimated failure. A normal native item that carries the same flags and errors excludes Arena KitBuilder as necessary cause. |
| Arena matching same templates, first equip then replacement, replay disabled | Exact request chain, construction count, first/reused instance state, old/new body identity. | Separates Arena preload/respawn exposure from Killcam. Pool expansion is a new constructor call; pool reuse is not. Counts alone need a denominator of constructed/equipped instances. |
| Same bounded Arena scenario with replay enabled | Same inputs and source-field checks before and after clone; logger state restored; original source objects/material references survive. | Tests a specific Killcam contribution. A clean log while its global mute is active cannot exclude errors. Use the baseline's confirmed faulty path as an observation positive control. |

Runtime diagnostics should remain observers: bounded opt-in field/identity and
request-stack capture, no suppression or replacement return values. No unsupported
prefab edits, invented assets, extra Foldable components, component destruction,
or whole-feature disabling is proposed as a production fix for these families.
No ordinary-PMC gameplay changes are needed for attribution.

## Four-pass review and leaf handoff

1. Completed the native pool/equip paths, all requested own-mod lifecycle seams,
   recovered object identities, local template comparison and controlled-test
   plan without runtime changes.
2. Domain review corrected the interpretation of inactive M32 objects: native
   Dress initialization precedes parenting, and retained Item pools explain
   cross-raid identity. Also separated root Foldable lookup from child lookup.
3. Evidence review independently recounted the historical time window, separated
   observed subjects from source-derived stacks and hypotheses, retained Unity
   destroyed-object null ambiguity, and surfaced Killcam's existing global mute.
4. Final scope review checked source revisions and line references, distinguished
   local database from GPU runtime state, excluded fresh native/build acceptance,
   and found no additional change required to this source-only deliverable.

Manual source gates ready for parent verification:

- **leaf-1.3:L1 met for source attribution:** three error sites, native lifecycle,
  exact historical subjects, and Arena/Core/Killcam paths are documented above.
- **leaf-1.3:L2 met for source attribution:** no unsupported repair is declared
  proven; separate Killcam observability finding, bounded controls, negative
  controls and explicit uncertainty are documented. Native validation and
  production correction remain the parent's later gates, not this leaf's claim.

Only this report is owned by the leaf. The parent records its independent gate
decision in the shared orchestration ledger.
