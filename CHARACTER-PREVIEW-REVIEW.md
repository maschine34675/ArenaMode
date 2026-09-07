# Arena character preview — 2026-09-07

Implementation and GPU menu review. Final installed identity is recorded below. Earlier
character-row screenshots and validation in `CHARACTER-SELECTION-REVIEW.md` do not
show or validate this new model, camera or asynchronous loader owner.

## Intended menu behavior

The existing ARENA toggle gains a third native posed character above it. Its face
and clothing follow the current PMC customization; its equipment is a separate,
fixed preview loadout. USEC shows an M4A1, BEAR an AK-74M. Both have a small reflex
sight, an open-face helmet, headset and armored rig with class-4 plates.

The full native PMC and SCAV columns move outward together, including their model,
click target, label, cooldown and health UI. The original native toggle handlers
remain in place. The added image and caption do not receive pointer input. Showing
this mannequin does not choose a gameplay kit or promise the next match's loadout;
protected PFT Tactical continues to use the PMC entry equipment.

Implementation files:

- `ArenaMode.Client/Session/ArenaPreviewEquipment.cs`: detached fixed item trees.
- `ArenaMode.Client/Session/ArenaCharacterPreview.cs`: model, camera, bundle and
  image ownership.
- `ArenaMode.Client/Session/ArenaSideSelectionLayout.cs`: third-column placement
  and restoration of native column positions.
- `ArenaMode.Client/Session/ArenaSideSelectionPatch.cs`: passes the current PMC
  profile to the presentation owner and restores it at the native Show/Close
  boundaries. PFT squad management restores before its guard and adds no preview.

## Fixed equipment and local database evidence

`ArenaPreviewEquipment.Build(EPlayerSide)` accepts only USEC or BEAR and returns a
new `InventoryEquipment`. Root template `55d7217a4bdc2d86028b456d` is the concrete
Default Inventory item. Every item, including the root and duplicate plate
templates, receives a fresh `ItemFactory.NextId`. Static definitions retain only
template IDs and symbolic parent/slot names.

| Part | USEC | BEAR |
| --- | --- | --- |
| Weapon | M4A1 `5447a9cd4bdc2dbd208b4567` | AK-74M `5ac4cd105acfc40016339859` |
| Reflex sight | PK-06 `57ae0171245977343c27bfcf` on the upper's `mod_scope` | Same PK-06 on Bastion `5d2c76ed48f03532f2136169` |
| Armored rig | AVS `544a5caa4bdc2d1a388b4568` | TV-110 `5c0e746986f7741453628fe5` |
| Front/back plates | Two class-4 SPRTN Omega plates `656f9fa0498d1b7e3e071d98` | Two class-4 Global Armor plates `656fa0fb498d1b7e3e071d9c` |
| Helmet | Black ULACH `5b40e1525acfc4771e1c6611` | 6B47 `5a7c4850e899ef00150be885` |
| Headset | ComTac II `5645bcc04bdc2d363b8b4572` | GSSh-01 `5b432b965acfc47a8774094e` |
| Complete tree, including root | 25 items | 20 items |

The weapon magazine is present but empty; chambers are empty. There are no spare
magazines, ammunition nodes, grenades, backpack, face covering, secure container
or borrowed personal equipment. Required armor/helmet inserts are explicit, not
inferred from the carrier's own armor class.

Database sources inspected on this GPU host:

- `C:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json`: all templates,
  slot filters, required parts, armor classes, blocking flags and conflicts.
- `C:\SPT41\SPT_Runtime\SPT_Data\database\globals.json`: native presets below.
- `C:\SPT41\SPT_Runtime\SPT_Data\database\locales\global\en.json`: item names.

M4 structure follows preset `5af08cf886f774223c269184` (`M4A1_Std`) with its
optional carry handle omitted and the PK-06 mounted directly on the upper.
AK structure follows preset `5acf7db286f7743a9c7092e3` (`AK-74M Default`), replacing
the dust cover with Bastion. The original rear sight `5ac72e475acfc400180ae6fe`
is deliberately omitted because Bastion lists it as a conflict.

Armor assembly was checked against AVS preset `65719d367a553968340d88b8`, ULACH
preset `657120b36fe59548840cb542`, TV-110 preset `657b351d306ad0bf99008208` and
6B47 preset `657bc68d26820ea7da04f2f0`. AVS contains its three required soft/groin
inserts, TV-110 its two soft inserts, and each helmet its three required inserts.
Both helmets allow an earpiece; neither chosen headset conflicts with its helmet.

A read-only comparison parsed the final source tables against the local item DB:
25 USEC and 20 BEAR nodes, with no missing templates, late/missing parents,
duplicate slots, rejected slot filters, missing required parts or remaining
`ConflictingItems`. The complete per-part IDs and exact direct-parent slots are
in `ArenaPreviewEquipment.cs`. An independent source/DB review found no functional
equipment-builder issue. This is structural evidence, not a rendered-item check.

## Native APIs and ownership

Paths below are relative to the repository's sibling `../_decompiled/` directory,
verified locally at `C:\SPT41\Development\_decompiled`.

| Native source | Verified contract used here |
| --- | --- |
| `EFT/BodyCustomization.cs`, `EFT/PlayerVisualRepresentation.cs` | The customization constructor copies the dictionary. The visual representation accepts a new `PlayerInfo`, copied customization and detached equipment. |
| `EFT/ItemFactory.cs` | `NextId` allocates new IDs; `CreateItem` constructs from a template. `CreateAndFillItem` uses `AddWithoutRestrictions` for locked armor inserts. |
| `EFT.InventoryLogic/InventoryEquipment.cs`, `CompoundItem.cs` | Root slots are newly created; `MissingVitalParts` walks required nested slots through `AllSlots`. |
| `EFT.InventoryLogic/Slot.cs` | Direct slot compatibility, conflicting-item and blocking checks are available. `AddWithoutRestrictions` still calls native local slot-change callbacks. |
| `EFT.UI/PlayerModelLoader.cs` | `GetItemInHands(visual, null)` chooses from the supplied equipment; `CreatePlayerBody` and `CreateWeapon` create the native menu pose/weapon. `Clear` returns the weapon and disposes its body. |
| `EFT/ThirdPersonCustomizationFilter.cs`, `EFT/EasyAssetsExtensions.cs` | Native third-person customization filtering and retained bundle loading. The required bundle key set follows `PlayerModelLoader.LoadBundles`. |
| `EFT.UI/CameraImage.cs` | Owns the image render texture, target-camera registration and temporary native render settings; `InitCamera(null)`, disable and destruction clean them up. |
| `CameraLightSwitcher.cs`, `PreviewFilter.cs`, `Undithering.cs` | Native per-camera lighting and preview image effects; owned effect components/materials retain their normal cleanup behavior. |

Only PMC side and a copy of its customization enter the visual representation.
`ArenaCharacterPreview` creates a new `PlayerInfo` containing the side; it does
not retain the profile's Info, health controller, inventory controller or equipment.
The builder never calls `KitCatalog`, `KitBuilder.ApplyToProfile`,
`EquipmentSurgeon`, `ArenaEvents` or settings/configuration writers. It fails on
an unsupported faction, missing factory/template, incompatible slot/conflict or
missing required part; it does not fall back to real PMC equipment.

The builder creates children before attaching completed subtrees to the root.
It validates direct slots and item constraints before using native unrestricted
assembly for locked inserts. Since no inventory owner or model is bound yet,
it does not raise inventory operation events against a profile/controller.
Native internal slot callbacks still run as part of constructing the new tree.

## Current rendering and lifetime design

`ArenaCharacterPreview` owns a separate `PlayerModelLoader`, world root, camera,
UI image, spinner, filter material and retained bundle-release callback. It does
not clone an active `PlayerModelView` or camera GameObject. A fresh camera copies
the verified PMC preview camera settings, resets its world-to-camera matrix and
receives its own render target through a new native `CameraImage`.

`Camera.CopyFrom` also copies transform properties. The private local camera pose
is therefore applied **after** CopyFrom. This corrected the first GPU iteration,
where loading succeeded but the camera looked at the original UI's world position.
See Unity's [Camera.CopyFrom contract](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera.CopyFrom.html).
The second iteration showed all 51 USEC model renderer bounds inside the private
camera frustum and rendered the expected M4/AVS/ULACH character.

The current world root is placed at `(10000, -10000, 10000)` with a 25-unit camera
far plane on the native `WeaponPreview` layer. This spatial separation is intended
to keep the new body outside the ordinary menu/item preview cameras despite the
shared layer. Lights are separate instances using a new native camera light
switcher; native shader assets are reused with owned effect instances/materials.
The image remains disabled while it has no texture so it cannot produce a white
rectangle over the character menu during loading.

The owner retains the menu-player, customization/watch and visible equipment
bundles before its first await and stores the release callback immediately.
It then uses native `CreatePlayerBody` and `CreateWeapon`. It deliberately owns
this sequence instead of calling the full native `Load` convenience method:
that method assigns its retained token only after an await/cancellation check.
No raid `KitBundlePreloader` or live player object is involved.

Dispose first detaches the image/camera and hides this owner's view/world/model.
If loading is in progress, cleanup waits for that async continuation rather than
destroying `PlayerBody` during its awaited initialization. The continuation checks
disposal before proceeding; its `finally` performs the deferred release. Release
is guarded against repetition, clears the native loader, releases retained
bundles, and destroys only this owner's image/world/material. Normal Show/Close
restoration and an owned `OnDisable`/`OnDestroy` hook both request disposal.
Load failure requests native-column restoration and leaves the existing toggle
available. The GPU observations below distinguish production behavior from the
temporary delayed-load fixture; they are not a general proof of all async races.

## Review and validation scope

- The already-run **29 native-side helper assertions and 53 menu-flow assertions**
  cover existing toggle ownership, eligibility, identity, normal-route restoration
  and flow guards. They do **not** execute the new `ArenaCharacterPreview` class,
  its rendering/camera geometry, native item construction or asynchronous lifetime.
- The equipment tables and native construction contracts received the read-only
  checks described above. No fake Unity geometry is used as visual evidence.
- Independent adversarial review found two failure-path issues before completion:
  preview cleanup could propagate exceptions into native Close/Show, and a preview
  setup failure could undo the already working character-choice row. Cleanup now
  protects each step independently, preserves an owned Poser reference across
  native Clear, restores native positions independently of preview disposal and
  confines preview failure to the added presentation. Follow-up review found no
  remaining concrete issue. Old async callbacks cannot restore a newer layout:
  old owners clear their saved column references after restoration.
- Required menu observations: third figure and weapon visibility; PMC appearance;
  readable labels/descriptions; preserved PMC/SCAV input and warnings; Arena
  selection/map navigation; repeated Back/reopen and leaving while loading;
  PFT squad-management exclusion; and absence of the owned body/lighting in other
  previews after return. Verify each faction with a corresponding test profile
  when available and record any missing faction coverage explicitly.
- No raid-start path is added by this presentation. Automated inspection stops
  before raid start; actual gameplay and protected-team loadout behavior remain
  the user's raid tests.

## GPU evidence and final installed identity

Host `WIN-Q470OL2A14A`, installation `C:\SPT41`, branch
`feature/arena-native-menu-options`. The work started from character-row commit
`328665d0c0ad62760dd0c34ed886efd21eff0512`; this report is prepared for the new
preview commit. Client log times below are local **MESZ / UTC+02:00** on
2026-09-07. The manifest stores capture timestamps in UTC. Actual window captures
are **1920 x 945**, with native UI scale **0.875** (logical height 1080); do not
substitute the configured Graphics JSON resolution for the captured dimensions.

| Build/run | Client SHA-256 | Evidence |
| --- | --- | --- |
| Production menu/navigation run | `31AC4DD0ABF5B5A7E5F464CA80EE96099DF5157FC15A6E54B1B1D859759C048A` | Screenshots 02-09; USEC rendering and normal/Arena/PFT navigation below. |
| Temporary BEAR-equipment and delayed-load fixture | `4F06F6A1E22E3E4B9EFB0CC5876FDE327DADC3C8E2A3EBA8B784F8CE5B34DFAC` | Screenshots 10-11; only the detached kit was switched to BEAR, plus an eight-second bundle delay. |
| Final production Release and installed client | `2FAE06042ED6D43BFD2D33F71414087C34281DD2C338E35385B00C12F1482044` | Identical output/install hashes; standard-M4 smoke passed, screenshots 12-13. |

The final client build succeeded with zero errors and four existing `CS8632`
warnings in `ArenaMode.Shared/ClientSettingsModels.cs`. The temporary fixture was
removed before this build: the restored production source matched its saved
production backup hash, with neither equipment-side override nor artificial delay
remaining. The server was not rebuilt; its installed SHA-256 remains
`FCE6C394DF9184E81FE6D43B0D236380DE71E3BB110708497E2471D395B2DBE8`.
Redeploy.Core remains
`F923B0D709908F75969B3101E9F8290CD2BB06DF457F64B1FFF8876A7BBFEA00`.

Observed production run:

- The third figure rendered the test PMC's USEC appearance with the detached M4,
  AVS and ULACH kit. Ready diagnostics reported `visibleBounds=51/51`, with
  camera `(10000.62, -10000.60, 10000.96)` and body
  `(10000.00, -10001.00, 10005.00)`; screenshot `03-usec-arena.png` records the
  actual rendered result. Frustum counts alone are not visual acceptance.
- PMC, SCAV and ARENA selection through their **labels** worked. Arena -> map ->
  Back and SCAV -> ordinary map -> Back worked, including repeated reopening.
  This does not claim successful selection by clicking the 3D portraits.
- MY SQUAD displayed the populated seven-member PFT roster without an Arena
  toggle, caption or third preview; returning restored the normal choice screen.
- After these transitions, an ordinary inventory M60 item inspection showed no
  foreign Arena mannequin (`09-waffenvorschau.png`). This clean-menu observation
  does not resolve the separate post-raid contamination investigation in
  `PREVIEW-DIAGNOSTICS-REVIEW.md`.

Fixture-only observations:

- The AK-74M/Bastion/TV-110/6B47/GSSh BEAR equipment was visibly rendered on the
  **unchanged USEC test character**, with `visibleBounds=47/47`
  (`10-bear-kit.png`). All available test profiles were USEC: genuine BEAR profile
  customization/appearance remains untested. The fixture's `side=Usec` ready log
  is consistent with this equipment-only override.
- With the eight-second delay, Show was logged at **13:33:52.367** and disposal
  at **13:33:59.946**, explicitly `loading=True`. After the await elapsed, no
  later ready event, Arena warning or Arena error appeared for that load. The
  main menu remained clear in `11-laden-abgebrochen.png`, captured at
  **13:34:18.235**. This covers closing during the retained-bundle await; it does
  not separately inject failure or closure into every native body-init stage.

Final-production standard-M4 smoke: **passed**. Show was logged at
**13:38:27.784**, followed by ready at **13:38:30.055**, `side=Usec` and
`visibleBounds=51/51`. All three figures were fully visible; selecting the ARENA
label produced the expected selection/description (`12-final-arena.png`). Back
returned to the main menu without the Arena figure or overlay
(`13-final-hauptmenue.png`). The saved final client log contains no error-level
entries, Arena warnings or temporary-fixture markers. This final smoke verifies
the restored production M4 build; the broader navigation matrix above belongs to
the separately identified production run.

The game and owned test server were stopped after the final run. No raid was
started in any of these runs.

Private artifacts on this GPU host are under
`C:\Users\Administrator\Documents\Codex\2026-09-07\lies-agents-md-und-handoff-md`:

- `outputs/Arena-Charaktervorschau/manifest.json` associates screenshots 02-13
  with their actual build hash, timestamp and dimensions; images are in its
  `screenshots/` subdirectory. The earlier baseline image 01 is shown separately
  in the gallery without an invented build hash or capture timestamp.
- `work/arena-character-preview/client-usec.log` and `client-bear-fixture.log`
  retain the production navigation and temporary fixture logs respectively.
- `work/arena-character-preview/final-build.log`, `side-checks.log` and
  `flow-checks.log` retain the build and helper-check outputs; `final-client.log`
  retains the final production smoke and shutdown log.

Profiles and raw runtime logs are not committed with this report. The new
character preview does not change the user's responsibility for actual raid tests.
