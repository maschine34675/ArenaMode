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
The exact left invocation suppresses only its own Firearms.PlayShotEffects;
nested invocations mask that scope and a finalizer restores it.

The left view holds descendant pose briefly on a right shot and adds its own
short kick/flash on a left shot. This is not a second native animator: the native
right slide/root recoil still runs on each shot, and support-hand grip remains
a presentation limitation. The native first-person sound remains one report
per trigger; its outer native Shot call retains the original right-side position.

The view constructs fresh transform/MeshRenderer objects from the held weapon
hierarchy and shares immutable mesh/material references. No native behaviours,
pool objects, colliders, weapon controllers or inventory items are cloned.
Unsupported/skinned geometry fails closed. Native left-arm IK gets a temporary
camera-left target offset after `Player.IkProcess`; native `IkApply` still solves
the limb. Support-hand fingers are an explicit visual-prototype limitation.
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
