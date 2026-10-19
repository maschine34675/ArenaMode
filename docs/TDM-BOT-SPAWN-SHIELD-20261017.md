# Team Deathmatch bot spawn shields

Scope update 2026-10-19: bot protection now applies only to independent Team DM
using the catalog's fixed team boxes (Equator, Bay5 and Air Pit), not ordinary
map starts. Player respawn protection is unchanged. See
[current admission rules and checks](BOT-SPAWN-SHIELD-SCOPE-20261019.md).

Independent Team DM bots now receive damage protection on each admitted physical
spawn. Both factions use `Respawn/SpawnProtectionSeconds`, frozen at match start
(default 4 seconds, disabled at zero, safety maximum 30 seconds). The full timer
starts when combat is live and the body is Active, so loading/assembly cannot
consume it. A pending PreActive replacement remains covered under the existing
bounded activation watchdog; it is not allowed an additional spawn slot.

The first actual gunshot or confirmed grenade throw ends protection immediately.
Attributed outgoing damage also ends it, covering other native attack paths.
A pale cyan/white three-ring cage marks each protected bot during live combat.
It follows the body in world space, including with DLSS, does not depend on the
Arena HUD switch, and is hidden immediately on attack/expiry. It has no collider,
point light, material replacement or reference to pooled bot presentation objects.

The existing Team DM damage admission guard now covers these leases as well as
staging/results: shots stop before armor durability, environmental/explosion
damage before Player mutations, with separate final HP, explosion-armor and
contusion guards. Flash blindness/deafness remains native. No `DamageCoeff` is
modified, no local-player protection lease is replaced, and no protection is
added to other modes, PFT bodies or ordinary raids.

Authority is the captured game/world/match plus exact BotOwner and Player,
captured profile ID and current alive-world registration. History IDs cannot
lend a shield to another body. Leases cannot rearm when polled or when a setting
changes. Death, identity/registration drift, result, native shutdown and match
teardown dispose owned views and retire leases. Administrative retirement still
uses the native direct Kill path.

Native source seams: `Player.OnMakingShot` runs inside `InitiateShot` before
`BallisticsCalculator.Shoot`; successful `CreateAndRegisterGrenade` returns the
registered grenade. `ExplosionSharedMethods` applies environmental body damage
before its separate armor call. The attack observer is last-priority and honors
Harmony's `__runOriginal` for gunfire.

Validation: 380 production-linked TDM runtime checks, including all-team initial
shields, preactive replacement activation, exact deadlines, frozen duration,
immediate attack release, no rearm, ordinary/local exclusions, world/identity
loss and visual disposal. Fault controls remove expiry, attack release and world
registration independently. Broader regression and native test evidence is
recorded with the final map-feature handoff; model checks are not visual proof.
