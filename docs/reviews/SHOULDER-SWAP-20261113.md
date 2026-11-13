# Auto Shoulder Swap Arena input guard

Reviewed release: [Auto Shoulder Swap 1.1.1](https://github.com/minihazel/StanceSync/releases/tag/1.1.1),
source `fddb7171527c1eea9a91a394767f0d9d7cd8345e`.
The published DLL declares `hazelify.StanceSync`, not the GUID on Forge.
SHA-256 `96FBF674BFD39E428033A75E8791CDFBE108EC9D13EE75298533219CA668F8F2`.
The November release/source inspection found no upstream change since the
[October native failure](../reports/2026-10/INPUT-MOD-COMPAT-20261025.md).

## Cause and ownership

The mod's lean postfix reads `CurrentScope` before dereferencing its unchecked
firearm-controller cast. Native leaning also works with knives and other hands.
EFT's `ProceduralWeaponAnimation.CurrentScope` indexes element zero when the
scope list is empty, so the exception can precede the null firearm dereference.
The mod's camera reset callback also reads `CurrentScope` without checking its
list. A firearm being drawn can temporarily have no valid scope entry.

The mod does not cache the local player or camera. This is an upstream input
precondition defect, not a shared body-replacement defect, so the guard belongs
to ArenaMode. Redeploy.Core receives no shoulder adapter. No foreign code or
binary is distributed.

## Contract

`ArenaShoulderSwapCompat` resolves the loaded plugin's actual metadata and both
exact static callback signatures. Binding and rejection are cached per physical
plugin instance. Missing/unready/changed contracts refuse Arena admission.
An absent plugin returns before reflection or Harmony work. Cleanup removes only
the two owned prefixes from their exact target methods.

The prefixes wrap the foreign callbacks rather than native lean/camera methods.
Native input and camera updates always continue. Suppression requires an active
Arena, its exact local game and world, the same committed `LocalPlayer` and
`GameWorld.MainPlayer`, and the physical player passed to the lean callback.
The camera guard also requires the current `PlayerCameraController` singleton
bound to that body. Fika, ordinary PMC/Scav raids, unrelated players, detached
cameras and incomplete ownership matches pass through.

Within that scope, the foreign callback is skipped for non-firearm hands, a
dead/missing health controller, missing movement/camera/PWA state, an absent or
empty scope list, a negative aim index or a null selected sight. Ready firearms
retain the mod's own configuration, ADS exclusions and stance logic. EFT's
modulo scope indexing is retained. No player state, settings or foreign plugin
settings are changed.

## Verification

`tools/ShoulderSwapChecks` links the production adapter, applies actual Harmony
prefixes in a standalone .NET Framework process and verifies native/foreign
call counts, ownership isolation, transient hands, replacement, binding cache
and cleanup. Optional release inspection checks the actual DLL's metadata,
callback signatures and scope accesses. Seven injected faults exercise the
guards and cache behavior. These checks use EFT doubles and do not certify
Unity shoulder animations.

Native acceptance must separately exercise firearm lean/reset, knife lean,
empty/medical/grenade hands, a firearm switch and at least one real respawn.
Include ADS with the mod's options retained and verify the native lean still
works when the foreign shoulder action is suppressed. Ordinary-raid isolation
is covered offline; the known upstream knife error intentionally remains
unchanged outside Arena.
