# Akimbo — persistent pistol mutator

Scope: default-off `Combat / AkimboEnabled`, exposed as `AKIMBO PISTOLS` on the
map screen. Automatically binds the currently held local Arena handgun across
draws, respawns and rounds. Native Pistol/Revolver types also require weapon
class `pistol`; flare guns and launchers are excluded. This admits automatic
pistols and handgun revolvers, but not shotgun revolvers or pistol-named SMGs.
Deathmatch, Gun Game, Tactical and TDM are admitted; the newly added Zombies
mode is not in this allowlist. Ordinary raids and bots are excluded. No pickup,
new equipment slot or persistent second item is added. No powerup expiry. The saved
preference survives cleanup; the old transient prototype request is not migrated.

The native firearm remains the only hands controller and inventory owner.
User decision 2026-10-07: **one projectile per trigger, alternating right/left,
two shots per loaded cartridge** (7 loaded rounds -> 14 shots). No timer or
automatic second projectile. Right uses a detached disposable ammunition clone;
left consumes the retained original. Magazine pistols reuse the existing
non-consuming SingleShotData seam in ArenaInfiniteAmmo, including a separate
Akimbo-only allowance for a lone loaded chamber without a magazine. The timed
infinite-ammo powerup retains its original loaded-magazine-only contract.

Credit is reserved at native ammunition preparation and belongs to the physical
weapon through a weak-key table, independently of view lifetime. Redraw, reload,
OFF/ON or interrupted preparation cannot repeatedly grant the same free half.
A successfully executed paid shot clears that credit, even if its obstructed
left muzzle has to fall back to unchanged native right geometry. Dry-fire,
veto and a retired owner's callback do not settle credit. Free clones are
identified by reference, not by global phase at the later animation event.

Revolver FireCylinderMagOperation.PrepareShot has a separate branch: a free
shot publishes a disposable clone without consuming the chamber, advancing the
cylinder or adding a spent shell. Native firing/animation completion remains.
A weak-key receipt scopes only the corresponding OnFireEvent shell-model move;
the real still-loaded cartridge is never moved into the spent-shell port. This
receipt completes an already prepared operation even if the mutator is toggled
before its native fire animation event. Successful firing retains it through
OnAddAmmoInChamber to preserve the original hammer/cylinder state for the paid
half. Reset, the next preparation or an exceptional finalizer removes it.

Left InitiateShot arguments preserve the native FOV/back-offset correction and
converge on the original spread ray at 50m. Native origin -> left origin, camera
-> left origin, left origin -> visible muzzle and both endpoint occupancies must
be clear. Invalid geometry falls back to the untouched native right shot.
The native shot, statistics, damage, recoil, heat and sound execute once. There
is no direct BallisticsCalculator.Shoot call or second shot-award scope here.
Each exact admitted invocation suppresses only its own Firearms.PlayShotEffects;
nested invocations mask that scope and a finalizer restores it.

Since 2026-10-08 both visible pistols are fresh mesh-only replicas. Native gun
renderers are reversibly hidden with forceRenderingOff; the native animator and
its events continue unchanged. Each replica has its own kick/return and flash;
only the firing side samples the native slide pose, after ArmsLateUpdate emits
its native firing events. Auto-mode VisualPass runs before those events, so it
must retain the last confirmed sample instead of reading the next side's pose.
A camera-relative idle pose
keeps the shared native gun recoil out of the resting replica. Both arm IK
targets follow their respective replica; support-hand fingers remain a
presentation limitation. Shared camera recoil remains native. This is not a
second native animator. The native first-person sound remains one report
per trigger; its outer native Shot call retains the original right-side position.

The view constructs fresh transform/MeshRenderer objects from the held weapon
hierarchy and shares immutable mesh/material references. No native behaviours,
pool objects, colliders, weapon controllers or inventory items are cloned.
Unsupported/skinned geometry fails closed. Dynamic AmmoPoolObject subtrees are
excluded entirely: cartridges and shells change native pool ownership during
firing. Every copied node retains its direct source parent; pose and renderer
writes require the complete original chain to the owned weapon root. Detached
objects are never hidden or restored through a stale reference. Native arm IK
gets temporary targets after `Player.IkProcess`; native `IkApply` still solves
the limbs. Support-hand fingers are an explicit visual-prototype limitation.
No animation assets, permanent bone markers or native controller state are
replaced. Sprint, reload, vault, inventory, interactions, malfunctions and other
busy states hide the replica and omit the IK override; the original animation
has both hands again. ADS is disabled while a valid Akimbo binding is active.
Readiness uses the actual weapon MalfState, not FirearmController.Malfunction:
the latter also latches on ordinary empty/chamber-cycle events, including
revolvers, and can remain true while the weapon is healthy and idle. Native
repair/reload operations remain excluded; alternation still requires an actual
successful native shot from the current firing operation.

Exact body, hands, weapon, current magazine (including absence), weapon-model root, health, world, match, raid epoch
and round identities bind each lifetime. Death, weapon/controller replacement,
invalid match ownership, disabling or shutdown retire it. A new eligible owner
can bind automatically. A failed owner is quarantined across temporary context
gaps; it is retried only after owner replacement or explicit OFF→ON. Partial
views are disposed before publication. Ordinary busy animation states hide the
existing view instead of rebuilding it. A changed magazine retires the old view;
rebinding waits for native reload completion and copies the replacement model.
The current native fireport is remapped
to its owned mesh copy before emission. No ordinary PMC/Scav/Hideout
modification, bot use or profile/inventory mutation is permitted.

## Doubled firing cadence (2026-10-08)

Only the exact bound local live Arena firing operation is accelerated: native
FireOperation (including its revolver subclass) and AutomaticFireOperation
receive twice the update delta. Their native input/queued-click rules remain;
there is no automatic second trigger for single fire. Animator event timing must
also double, because native single fire waits for OnIdleStartEvent as well as
its cooldown. Unity hands use a compare-before-restore speed lease; it is
released before the owned firing operation becomes Finished, on context loss,
OFF, owner replacement, exception and disposal. Another writer's speed is
preserved and not fought for that operation. FastAnimatorProcessor's speed setter
is unused, so only its exact manually updated hands instance receives twice its
Update delta instead. Unsupported animator modes keep native timing.

Native evidence: Player.cs FireOperation.Start/Update/OnIdleStartEvent at
4372/4499/4525, revolver event at 4185, automatic scheduler at 2686/2723,
ArmsUpdate at 29064 and ArmsLateUpdate at 29104. Never multiply ArmsUpdate or
ArmsLateUpdate: the latter also advances ballistics. No shared WeaponTemplate,
FireRate getter, projectile time, reload operation or bot animator is modified.

Production-linked Timing checks cover simultaneous timer/animator scaling,
identity, foreign writes, state-boundary restore, FastAnimator mode and per-side
presentation. View checks cover native renderer/pool lifetime; these doubles do
not substitute for Unity and actual native firing tests.

Source seams: `Player.InitiateShot`, `Player.IkProcess`, `Player.VisualPass`,
`FirearmController.IsAiming`; actual installed-source contract verified in
`../_decompiled`. The native projectile holds its ammo clone until pooled shot
cleanup; never reissue after an ambiguous emission failure. No second call to
`Shot`, `InitiateShot`, `OnMakingShot`, `OnStatisticsShot`, shared audio or shared
right-hand muzzle effects.

Offline tests exercise the real lifetime/shot-admission state. They do not certify
Unity rendering, finger placement, animator transitions or native damage.
Native evidence and final scope limits belong in the dated GPU report/HANDOFF.
Custom skinned geometry remains unsupported; a changed child model hierarchy may
fail closed until a weapon switch or OFF→ON. Pickup integration remains separate.

Historical simultaneous-fire prototype, superseded above (2026-10-05):
`3c16748` passed a bounded PM test
on GPU-2, including two visible pistols, bonus projectile emission, reload,
inventory, sprint, ADS suppression and lifetime cleanup. This accepts the rough
presentation experiment only: the left support-hand fingers still do not form a
correct firing grip, two bullets share one magazine/audio/recoil cycle, and no
independent offhand reload exists. See `GPU-AKIMBO-RESULTS-20261005.md` for evidence
and untested cases. The user accepted this presentation and requested expansion
to a pistol mutator. Simultaneous bullets/shared ammunition remain explicit,
not independent alternating dual wield. Expansion evidence is recorded separately
in `GPU-AKIMBO-MUTATOR-RESULTS-20261005.md`; old PM evidence cannot certify it.
