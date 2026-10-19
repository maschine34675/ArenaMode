# Smoothie compatibility — 2026-10-19

Arena baseline: `ab5243c38584047599876d01789118fb1af594cf`.
This adapter does not require or distribute Smoothie. Its physics, animator,
occlusion, distant-detail and profile-loading optimizations remain untouched.
Ordinary PMC/Scav raids retain Smoothie's original behavior.

## Reviewed version and provenance

- [Forge Smoothie](https://sp-mod.com/mod/3132/smoothie): version 1.0.8 for SPT 4.1.6.
- [Published release 1.0.8](https://github.com/TominoCZ/Smoothie/releases/tag/1.0.8),
  source commit `87f7a315b6ce0ce2b984ed69af3d84e159050e5e`.
- Plugin GUID `com.tominocz.smoothie`; assembly `Smoothie`, version `1.0.8.0`;
  BepInPlugin version `1.0.8`.
- Published DLL MVID `2ed8f62b-c023-451c-9e2c-0e0b91c5dd68`, SHA-256
  `4bc9e1276dbd364a71abb9328ec3976f0d2fd05a6754e71a89ddfde6921a1106`.
- Source and separately downloaded release are under ignored
  `artifacts/compat-audit-20261019b/smoothie/`. Binary verification uses Cecil
  metadata only; it does not execute either published DLL.
- Smoothie's CC BY-NC-SA 4.0 source was studied, not copied. No foreign source,
  DLL or native binary is bundled. The harness uses original boundary doubles.

Newer versions are attempted only after matching plugin/assembly metadata and
the concrete GC method contract; they receive a bounded advisory. This is not
native acceptance of an unknown version or proof its renderer skip list is
unchanged. Older/malformed contracts or unavailable hooks disable this optional
adapter and warn; they do **not** block Arena. Leave Smoothie's optional
**Prevent memory cleanup stutters in raids** setting off (its default) if the
adapter reports a failure. No setting is changed automatically.

## Owned pickup geometry

Smoothie performs one static MeshRenderer scan, starting after ten seconds;
it excludes the `Loot` and `Interactive` layers (among others). Its movement
test recognizes native player/interactive/animated/rigid-body parents, not our
manual transform animation. Our rotating cubes, health-cross bars and billboard
labels could therefore be included with their initial bounds. This was a
candidate-selection finding, not proof of an observed visual disappearance.
See [DepthCulling scan and skip list](https://github.com/TominoCZ/Smoothie/blob/87f7a315b6ce0ce2b984ed69af3d84e159050e5e/src/Smoothie/DepthCulling.cs#L350-L420)
and [candidate/movement tests](https://github.com/TominoCZ/Smoothie/blob/87f7a315b6ce0ce2b984ed69af3d84e159050e5e/src/Smoothie/DepthCulling.cs#L867-L910).

`ArenaPickupView` now keeps a newly constructed private marker inactive until
its first visibility update. It chooses `Loot`, then `Interactive`, only if
that actual layer exists and is visible in both the main camera mask and the
available optic camera mask. The layer is applied to the private marker tree
before activation and rechecked on subsequent ticks. It never changes camera
masks, shaders, collision checks, pooled weapon models or area walls. The
native weapon remains outside the marker hierarchy and retains native ownership.

The native camera is instantiated from a serialized prefab, rather than having
a reliable source-code literal mask: decompiled `EFT.CameraControl/CameraManager`
exposes `Camera` / `OpticCameraManager` and instantiates the prefab at lines
324–345; `OpticCameraManager.Camera` is its actual camera property at line 68.
The adapter therefore reads live masks instead of assuming layer numbers or
using `Camera.main`. Arena's spectator/replay also operate on CameraManager's
camera. This preserves their current visibility while checking replacements.

No camera means an inactive marker until a later tick. No common visible
excluded layer means the ordinary Default layer and one warning: visibility is
preferred over an unverified exclusion. That fallback can still enter Smoothie's
static scan. The adapter does not remove captured renderer records or overwrite
foreign `forceRenderingOff` state. Later-created markers normally are not in
the one-time scan anyway. Native tests must cover initial markers, relocation,
optics and death-camera transitions; the layer policy is not a rendering test.

## Optional GC policy isolation

The concrete conflict is optional and default-off: Smoothie's
[`Diagnostics.KeepCleanupPaused()`](https://github.com/TominoCZ/Smoothie/blob/87f7a315b6ce0ce2b984ed69af3d84e159050e5e/src/Smoothie/Diagnostics.cs#L557-L591)
can turn Unity's collector off every frame, while Arena's `GcThresholdMonitor`
enables it after its heap threshold and reasserts that choice. The foreign
routine is called by a guarded delegate from
[`Plugin.Update`](https://github.com/TominoCZ/Smoothie/blob/87f7a315b6ce0ce2b984ed69af3d84e159050e5e/src/Smoothie/Plugin.cs#L781-L805).

The adapter prefixes that single internal static `void` method, rather than
Smoothie's whole Update, its configuration reader, or Unity's global GC setter.
It skips only when all current observations agree:

- current AbstractGame is the exact LocalGame used by the current IBotGame;
- its GameWorld equals the current GameWorld singleton;
- `ArenaSession.IsExactActiveRaid(game, world)` proves the confirmed Arena pair;
- native status is Started, native settings are PMC, and the nonempty native
  settings/world map keys match.

There is no retained "last was Arena" flag, body-specific dependency, temporary
foreign setting, or restoration write. Respawns keep the same confirmed world;
a later normal raid cannot inherit authority from a stale IsActive flag. At
raid end/menu, the original routine runs again, including its own lifecycle
reset. The next normal raid gets Smoothie's normal start/reset behavior. Because
we do not alter any shared field, no Harmony finalizer restoration is needed.

Identity validation checks the plugin GUID/type/base, assembly name/version and
BepInPlugin attribute agreement. The target must be the declared, internal,
static, nongeneric `void KeepCleanupPaused()` on static `Smoothie.Diagnostics`
with a managed method body. Native `_raidSettings` shape is also checked before
installation. Admission refresh verifies the actual Harmony receipt, not only
reflection. An unavailable target/receipt disables our adapter with a bounded
warning; normal foreign calls continue. A runtime identity-read fault also
makes the prefix inert until restart. Shutdown invalidates readiness **before**
unpatching and catches unpatch failures, so a surviving prefix cannot suppress
ordinary raids. Only our own Harmony owner is removed.

## Verification and review passes

Run `pwsh -NoProfile -File tools/SmoothieCompatChecks/Verify.ps1`.
Optionally provide `-PublishedAssembly <separately downloaded Smoothie.dll>`;
this adds metadata checks against the actual reviewed release. The final token
is `SMOOTHIE_COMPAT_VERIFIED` only after baseline, compiled faults and final
baseline succeed. Evidence is placed in `artifacts/smoothie-checks/<run>/`.

The harness links the production adapter, contract, scope and complete
`ArenaPickupView`. It checks original-call/GC-write effects and real private
object-tree mutations using external boundary doubles. It is not a native
Harmony-detour or Unity-rendering test.

Final leaf run: **138 assertions** including eight release metadata checks,
**18 compiled fault controls rejected**, and the unchanged final baseline green.
Evidence: `artifacts/smoothie-checks/84224658f8ba428ab8b43f130b9b9db3/`.
Without the optional downloaded binary the same runtime harness has 130
assertions. The source clone was rechecked at the release commit above.

1. **Contract/source pass:** published tag and metadata, optional default,
   concrete caller/seam, static discovery, actual native camera accessors.
2. **Behavior pass:** absent/malformed/future matching versions; ordinary PMC
   and Scav → Arena → menu → ordinary; exact maps/worlds/status; repeated calls;
   body-independent authority; main/optic mask intersection; private descendants;
   pool/area-wall ownership; initial inactive marker; camera changes and disposal.
3. **Fault pass:** compiled production mutations must fail the intended
   assertions, not fail compilation. Controls remove scope checks, GC protection,
   camera-mask checks, metadata validation, child exclusion, construction/tick
   updates and inert shutdown; one deliberately mutates the pooled weapon layer.
   Pending Chainloader metadata is also tested through later successful loading.
4. **Adversarial self-review:** no foreign config/GC/renderer writes, no persistent
   raid lease, no admission block for an optional optimization, receipt and
   runtime fault handling, bounded warning paths, and failed-unpatch behavior.
   Independent parent review/build/native results are separate gates.

Native acceptance remains outstanding at leaf completion. In particular test
Smoothie's GC option both off and on, normal raid before/after Arena, initial
and relocated pickups, optics, replay/spectator transitions, replacement bodies
and zombie culling. No speculative movement, ragdoll, AI or global renderer
disable was added.
