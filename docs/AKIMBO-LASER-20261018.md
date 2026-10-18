# Automatic Akimbo aiming laser — 2026-10-18

The existing Akimbo mutator now supplies an automatically enabled aiming aid on
both held pistol replicas, including supported revolvers without attachment
rails. It is an Arena-owned visual laser, **not an inventory attachment**. No
weapon item, adapter, native `LightComponent`, template, encyclopedia entry,
native tactical-device on/off state or profile is changed. Existing mounted
lasers remain under their original controls. The aid disappears with Akimbo;
there is no extra preference or additional mutator button.

## Rendering and ownership

Each owned view creates two fresh world-layer `LineRenderer` objects and two
small surface points, using one private `Unlit/Color` material. Depth testing
stays ordinary; neither beam nor point is an overlay. The points have their
fresh primitive colliders disabled and destroyed immediately. No pooled object,
native component, material, mesh or scene light is copied or destroyed.

The default is green. With team lasers enabled, independent TDM and protected
team Tactical use the configured friendly color. Existing Akimbo eligibility
and native readiness stay authoritative: local supported pistol/revolver in a
live Arena DM, Gun Game, Tactical or TDM round, first-person view and exact
current body/hands/weapon/magazine/world/match/epoch/round. The existing mutator
does not admit Zombies, bots, ordinary raids or Hideout.

The aiming aid is part of `AkimboView`. Solved-hand presentation updates its
origins from the two actual visible fireports. `Hide` immediately disables it
for inventory, reload, interactions, sprint, vaulting and other busy states.
Context loss, replay/spectating, death, respawn, mutator OFF, weapon replacement
and session teardown retire the same existing view and dispose its own geometry.
No old view can reactivate a hidden aid through a late callback. A native muzzle
whose captured ancestry changed is rejected even within the same frame.

## Direction and occlusion

The native `WeaponDirection` and `AdjustShotVectors` are read without changing
them. This retains each pistol's actual shot axis, zeroing and FOV/back-offset
correction. A camera-forward or generic transform-forward laser would point in
the wrong direction on some native models. `AkimboNativeAim.Read` is compiled in
an isolated contract check against the installed EFT assemblies; it uses the
native `Player.Transform.Original`, not EFT's forbidden `Player.transform`.

The paid left shot and its aid now call the same `AkimboShotGeometry.TryLeft`.
The geometry retains the previous 0.65 m maximum muzzle displacement, native
origin-to-left origin and camera-to-origin checks, origin-to-muzzle clearance,
both endpoint occupancies and 50 m convergence. A rejected left path hides the
left aid while the real shot retains its previous native-right fallback. Right
shots, alternating free/paid ammunition, cadence, animation and damage are not
otherwise changed.

The aid traces the ideal native shot ray to at most 100 m. A second segment
check from the displayed muzzle prevents a visibly offset replica from drawing
through intervening cover. Walls and real hit colliders stop the beam; the
shooter's exact root and descendants are ignored. Non-combat trigger volumes
are ignored while `HitCollider` triggers remain valid targets. An unsorted hit
buffer is explicitly searched for the nearest valid collision; overflow,
invalid vectors, camera occlusion or an occupied origin fail closed for that
frame. Misses keep the beam but remove the previous surface dot.

There are four bounded non-allocating ray queries for two visible beams, plus
short existing geometry guards. There is no full-scene scan, inventory search,
asset/pool load, asynchronous operation or native laser-setting enforcement.
The private hit buffer is cleared after each query and on hide/disposal.

This is an aiming aid, not a promise of a hit: native dispersion, projectile
drop, recoil and animation remain effective. It does not grant native tactical
device accuracy bonuses or compensate ammunition ballistics.

## Verification

`pwsh -NoProfile -File tools/AkimboChecks/Verify.ps1` runs the existing exact-owner,
ammunition, revolver, timing and view checks, followed by the linked production
laser/geometry tests and installed native API compile check.

The expanded real-view harness covers 88 assertions, including both origins,
nearest/unsorted collisions, own-root and descendant exclusion, trigger handling,
the corrected native ray, visible-segment clipping, stale-dot removal, all
camera/occupancy guards, overflow, invalid input, hide/reuse/disposal and the
left-shot convergence contract. Eighteen new deliberate faults include the
former obsolete native API call; the other seventeen compile and must fail
their intended behavioral assertion. Existing fault controls remain separate.

Native rendering, perceived aim alignment during movement and operation on
actual handgun models require GPU acceptance. They are not certified by the
boundary doubles or native API compile. The root task owns that acceptance and
the final deployment report; no deployment is performed by this leaf.
