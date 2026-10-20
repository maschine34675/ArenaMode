# Recoil Rework Legacy compatibility review

Reviewed on 2026-10-20 against ArenaMode's current SPT 4.1.6 client, local
decompiled game code and Redeploy.Core replacement pipeline. This document is
an investigation, not a claim of native gameplay acceptance or an implemented
compatibility adapter.

## Result

The reported incompatibility has a concrete, serious explanation in the
published client: **creating another local player while the previous player
object still exists schedules destruction of the new player's entire
GameObject.** Both recoil components independently enforce a process-wide
singleton this way. Arena's initial kit replacement, death respawns and live
round/wave replacements deliberately create the new body before releasing the
old one.

This is a demonstrable source/binary collision with Arena's lifecycle. It has
not been reproduced in a native game session here, and the user's report did
not specify their exact symptom or installed version. It therefore establishes
a valid fix target, rather than proving the cause of that particular report.

Recommended action: a narrow Arena-only component handoff and weapon-controller
cache adapter, with admission failure if its required foreign contracts cannot
be validated. Do not disable recoil globally, destroy a player to satisfy a
foreign singleton, or change the user's saved recoil configuration.

## Official version and binary evidence

- [Forge listing](https://sp-mod.com/mod/2190/recoil-rework-legacy):
  Recoil Rework (Legacy) 2.0.0, SPT 4.1.6 on the live page. Search-engine results
  still advertised the older 1.10.0/4.0.13 release; those were not used as the
  current version.
- [Official 2.0.0 release](https://github.com/peinwastaken/SPTRecoilRework/releases/tag/2.0.0):
  published 2026-09-09, notes state an update to SPT 4.1.x.
- [Tagged source](https://github.com/peinwastaken/SPTRecoilRework/tree/13d5624938a11d29fdfb26a3806630b69a0ea85a):
  commit `13d5624938a11d29fdfb26a3806630b69a0ea85a`.
- Both released DLLs embed commit
  `8240f3506f827638a1fc0e1b86a4fd53a5f96a09`. The official GitHub comparison
  reports the tag one commit ahead with **zero changed files**. Relevant paths
  were independently checked in the actual released DLLs.

| Item | Identity/version | SHA-256 |
|---|---|---|
| `SPTRecoilReworkLegacy.zip` | Official 2.0.0 asset | `B870C2A989DCE9A3F1B9F5EBA0F930C89E7A2B6791CFD454A8402F4E20E70F76` |
| `SPTRecoilReworkLegacyClient.dll` | BepInEx GUID `com.pein.camerarecoilmod`, plugin 2.0.0, file 2.0.0.0 | `EF8C2C77797001A9888CBD6871D83CA72A631C56430028292544E2A66076B516` |
| `SPTRecoilReworkLegacyServerMod.dll` | Same server GUID, metadata 2.0.0, SPT range `~4.1.0`; file/assembly 1.0.0.0 | `D949E4A765EC9013A271E620C8B71150AA8007C1300D2A0E85F6CDB9BC9E8107` |

Use plugin/server metadata for the release version, not the server file version.
The server metadata constructor was checked in IL because the initial
dependency-incomplete C# decompilation omitted its initializer values.

Downloads, API receipts, decompiled DLLs and native excerpts are kept only under
ignored `artifacts/compat-first-20261020/recoil/`. No foreign executable or
source has been installed or added to the release payload.

## 1. New-player destruction on replacement

The [Player.Init postfix](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Client/Patches/PlayerPatches.cs)
adds `RealRecoilComponent` and `CameraOffsetComponent` to every
`IsYourPlayer` body. This is unconditional with respect to the F12 recoil
enable switches.

Each component's `Awake` checks its static `Instance`. When an earlier Unity
component still exists, it calls `Destroy(gameObject)` on **its own newly
created player's object**, then returns without initializing its springs:

- [RealRecoilComponent](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Client/Components/RealRecoilComponent.cs)
- [CameraOffsetComponent](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Client/Components/CameraOffsetComponent.cs)

The released client has the same branches. Local decompilation:
`CameraOffsetComponent.Awake`, lines 2726-2737;
`RealRecoilComponent.Awake`, lines 2760-2777.

Native `LocalPlayer.Create` sets `IsYourPlayer` before calling `Player.Init`.
Redeploy.Core's `RespawnEngine.RunRespawn` unregisters the old player and starts
`LocalGame.vmethod_3` without destroying that player's GameObject. Arena's
`LiveKitRespawn.SwapRoutine` calls `ArenaRetiredBodyEffects.TryDestroy` only
after the replacement pipeline succeeds. A death replacement deliberately
retains the old corpse instead. Both paths therefore satisfy the foreign
singleton's destructive condition.

Disabling the old `Player` component alone does not stop another MonoBehaviour
on that GameObject. `RealRecoilComponent.FixedUpdate` continues rotating its
captured old player; `CameraOffsetComponent.Update` continues updating its old
springs. An adapter should retire those exact components without destroying the
old player, corpse, inventory or render hierarchy.

The unused `WeaponSwayComponent` contains the same singleton pattern, but this
release does not add it or enable its configuration binding. It is not a reason
to broaden the active adapter.

## 2. Recoil state uses the previous firearm controller

The [weapon-switch postfix](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Client/Patches/NewRecoilShotEffectPatches.cs)
computes recoil values from `WeaponHelper.CurrentFirearmController` before
assigning the incoming `firearmController`. The current fields are only updated
after its player reference equals `GameWorld.MainPlayer`.

Consequences:

- Ordinary weapon changes can derive dynamic settings from the preceding
  weapon. This is existing upstream behavior, not an Arena-specific regression.
- `Player.Init(async:false)` registers the new `GameWorld.MainPlayer` before
  the foreign Init postfix adds its components. `LocalGame.LocalPlayer` remains
  old until Core's later rewire. The firearm cache is separate from both:
  until a valid local recalculation it can still reference the previous body.
- The earlier computation occurs before that check. Retaining an old controller
  across body disposal also exposes old inventory/player/PWA reads, particularly
  with the optional 1.7.2 recoil calculation enabled.

The live local body and incoming physical firearm controller must be the
adapter's authority. A scoped cache bracket should restore its previous value
for rejected/stale/nonlocal calls and keep current values only after a valid
local recalculation. Once Core has committed the replacement, arrange a native
recalculation if construction occurred before the new MainPlayer assignment.
Do not replay the whole world-start event.

`Util.GetLocalPlayer` already reads the current world each time. Its separate
cached `GetFirearmController` and `GetGameWorld` helpers have no callers in the
reviewed client; they are not active compatibility seams.

## 3. Other overlap and counterevidence

All twelve installed patch targets were located in the current native
decompilation: `Player.Init`, firearm `SetAim` and `UpdateHipInaccuracy`, PWA
`Shoot`, `LerpCamera`, `CalculateCameraPosition`,
`AddHandRecoilRotateToCamera`, `NewRecoilShotEffect` weapon recalculation,
`NewRotationRecoilProcess.SetStableMode`, `ShotEffector.RecoilShotVal.Process`,
`WeaponRecoil.Initialize` and `PlayerBones.ShiftWeaponRoot`.

The seven reflected `PlayerBones` fields also exist. There is no demonstrated
missing-method/renamed-field startup failure for these contracts on this build.
This checks target existence, not successful Harmony application in Mono.

Further native testing is needed for these paths:

- The [PWA patches](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Client/Patches/ProceduralWeaponAnimationPatches.cs)
  add camera offsets from the current local player's component. `LerpCamera`
  does not guard a missing local player, and the shooting postfix tests the
  real-recoil component twice instead of checking the camera component on the
  second test. These are upstream edge cases, not reproduced Arena failures.
- [ShiftWeaponRoot](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Client/Patches/PlayerBonesPatches.cs)
  replaces the whole native method. Its signature omits the current
  `isLeftHandUsing` input, and its replacement lacks that input in the native
  animated-interaction branch. Omission is valid Harmony binding, so it is not
  itself a missing-argument startup failure. Left-hand use needs visual testing.
- Several recoil/stance patches act globally and consult local static weapon
  state. That is upstream behavior; the adapter must not rewrite ordinary raids
  or change bot recoil globally to repair Arena replacement.
- Arena Akimbo keeps one native firearm controller and one native
  shot/recoil/audio cycle per admitted projectile. Its two models are visual
  copies with per-side animation. It does not duplicate these recoil components
  or directly patch the foreign recoil methods, so there is no demonstrated
  double-projectile or double-recoil conflict from Akimbo alone. Camera recoil,
  left-shoulder pose and viewmodel recovery still need native checks after the
  lifecycle fix.
- Arena's elite skill/weapon mastery values can influence recoil through native
  weapon buffs, while Legacy's optional calculation uses template values and
  attachment deltas. This is a balance difference, not evidence of broken
  profile isolation.

No external progression journal, profile-save hook, inventory award or
raid-end persistence path was found in this mod. Its server component reads
configurations and changes common weapon templates at startup. Arena should
preserve those chosen server-wide changes rather than undo them for ordinary
raids.

## 4. Separate server-package concern

The published server
[loader](https://github.com/peinwastaken/SPTRecoilRework/blob/13d5624938a11d29fdfb26a3806630b69a0ea85a/Server/RecoilRework.cs)
always reads each weapon file as `List<WeaponOverrideConfig>`. Seven of the ten
files in the actual release have a single JSON object at the root:
`bolt_actions`, `deagle`, `hk416`, `m4a1`, `m60`, `mdr` and `sa58`.
Only `aug`, `autopistol` and `vector` have arrays.

Current installed SPT's `JsonUtil.DeserializeFromFile` directly calls
`System.Text.Json`. Its built-in converter registry does not add a
single-object converter for `List<T>`: `ListOrTConverterFactory` applies to
SPT's distinct `ListOrT<T>` type, and `EnumerableConverterFactory` to
`IEnumerable<T>`. The mod has no per-file exception handler.

This is a separate static packaging/loader mismatch that can fail server
startup without Arena installed. No server was started with the package in this
review, and a third-party JSON converter could change that behavior. Do not
attribute such a startup error to the Arena respawn collision or silently edit
the user's third-party package in an Arena compatibility adapter.

## Adapter acceptance requirements

1. Reflectively validate exact component types, static properties/setters,
   methods, foreign patch seams and firearm-cache contracts against the loaded
   assembly. Future versions may use matching contracts, but are not certified
   by the 2.0.0 review.
2. Handoff only during an exact confirmed Arena local-body replacement.
   Validate old/new physical identities and the corresponding game/raid.
   Never treat equal profile IDs alone as ownership.
3. Prevent both singleton branches before either component can schedule
   destruction. Retire old component updates without destroying the old body;
   handle creation failure and live-body recovery explicitly.
4. Keep all ordinary PMC/Scav/Hideout construction and recoil configuration
   untouched, including ordinary objects still referenced when an Arena
   candidate cannot establish ownership.
5. Keep weapon cache writes tied to the current physical controller and restore
   temporary state on exceptions, stale calls and nonlocal invocations.
6. Native follow-up: initial kit swap; multiple death replacements; live kit
   changes; PFT wave refill; failed creation recovery; replay/spectator
   transitions; weapon switching; ADS and left shoulder; Akimbo alternating fire;
   then an ordinary PMC/Scav raid in the same process.

## Review passes and limits

The review covered (1) live official release/source identity, (2) published
client/server DLL behavior and package contents, (3) native/Arena/Core call
ordering and (4) counterexamples/scope limits. It did not install the mod,
change its settings, deploy a build or run the game. A later adapter's tests and
native results must be recorded separately from this investigation.
