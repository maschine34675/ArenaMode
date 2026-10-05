# Akimbo — persistent pistol mutator

Scope: default-off `Combat / AkimboEnabled`, exposed as `AKIMBO PISTOLS` on the
map screen. Automatically binds the currently held local Arena handgun across
draws, respawns and rounds. Native Pistol/Revolver types also require weapon
class `pistol`; flare guns and launchers are excluded. This admits automatic
pistols and handgun revolvers, but not shotgun revolvers or pistol-named SMGs.
All Arena modes are admitted; ordinary raids and bots are excluded. No pickup,
new equipment slot or persistent second item is added. No timer. The saved
preference survives cleanup; the old transient prototype request is not migrated.

The native firearm remains the only hands controller and inventory owner.
Each successfully emitted ordinary pistol bullet may generate one free offhand
bullet with the same ammunition template and its own detached cartridge ID.
Native magazine consumption, reload, malfunctions, heat, recoil and audio still
run once. Two synchronous bullets share one native gunshot report/AI hearing
event. Bonus ballistics use the native calculator and Arena shot-award scope;
ordinary armor, hitmarkers, assists, team effects and kill attribution remain
in the normal damage path. A blocked offhand muzzle suppresses only the bonus.

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

Native first-stage result (2026-10-05): `3c16748` has passed a bounded PM test
on GPU-2, including two visible pistols, bonus projectile emission, reload,
inventory, sprint, ADS suppression and lifetime cleanup. This accepts the rough
presentation experiment only: the left support-hand fingers still do not form a
correct firing grip, two bullets share one magazine/audio/recoil cycle, and no
independent offhand reload exists. See `GPU-AKIMBO-RESULTS-20261005.md` for evidence
and untested cases. The user accepted this presentation and requested expansion
to a pistol mutator. Simultaneous bullets/shared ammunition remain explicit,
not independent alternating dual wield. Expansion evidence is recorded separately
in `GPU-AKIMBO-MUTATOR-RESULTS-20261005.md`; old PM evidence cannot certify it.
