# Arena powerup contract

Collection refreshes the effect for up to **20 seconds of game time**; collecting
twice never stacks the multiplier, timer or shield. Shield refills to at most
**100 impact points**. Effects belong to the exact physical body and health
controller, are cleared on native death, and cannot survive runtime generation or
round/raid reset. State uses weak body keys so discarded corpses are not retained.

Double damage applies to **non-launcher firearm bullet health damage at impact
time** from the current living Arena body. It is not a firing-time snapshot.
Grenades are excluded from the
outgoing bonus because native detonation can resolve a newly respawned owner's
bridge by profile ID. Physical grenade fragments can also be native Bullet damage;
the outgoing weapon guard excludes thrown grenades and grenade launchers. A dead
owner's grenade still encounters an eligible victim's
shield. Incoming shield admits Bullet, Explosion and GrenadeFragment only; self,
friendly, ownerless, administrative, fall, bleed and other damage stay native.
Deathmatch requires the exact retained world source; independent TDM additionally
uses its exact-body faction registry, which supports dead grenade owners.

Native armor resolves before this health hook. Double damage therefore doubles the
remaining impact, not penetration or armor durability. Residual bullet blunt damage
that actually reaches health is eligible. Shield subtracts impact points **after
the native incoming DamageMultiplier**, including the enemy-head reduction
exemption. The resulting argument is converted back so native applies its own
multiplier once. One shield point is not promised to equal one final total HP:
per-part DownMult, body-part caps and blacked-limb overflow remain native. The pool
absorbs the primary impact once, before overflow redistribution. It may absorb an
entire impact that would otherwise have killed a low-HP head/chest.

Shield does not set invulnerability or alter DamageCoeff, DamageMultiplier, ammo or
templates. Native invulnerability and mutual AI friendly-ignore flags are checked
before charging it; the last Harmony prefix respects earlier vetoes. Player-level
relapse, stamina/hit reactions and armor durability remain native. Health-level
side effects run with the reduced damage argument; no bleed immunity is created.
Hitmarker and assist observations retain actual HP loss, rather than absorbed
shield points. A completely absorbed hit therefore does not create HP assist damage.

## Native source basis

All references below are repository-adjacent decompiles, verified against installed
Assembly-CSharp IL by `Verify-Native.ps1` where the check is executable:

- `../_decompiled/EFT/Player.cs`: ApplyShot at 29961, armor at 29978, health route
  at 29989; ApplyDamageInfo at 30020 and health call at 30037. Explosion armor is
  separate at 29591; its durability calculation is not hooked.
- `../_decompiled/EFT.HealthSystem/ActiveHealthController.cs`: ApplyDamage at 3347
  checks alive, DamageCoeff and mutual AI flags; native multiplier/head condition
  at 3355; primary ChangeHealth at 3367; overflow ChangeHealth at 3410 never calls
  ApplyDamage again. ChangeHealth at 3559 emits the actual clamped LastDiff.
- `../_decompiled/EFT.HealthSystem/HealthValue.cs`: Current setter at 11 applies
  DownMult before native health clamps. It is not a shield mutation surface.
- `../_decompiled/EFT.HealthSystem/DamageHelper.cs`: admitted impact types are all
  enemy damage and none are environmental damage, so the native environment
  multiplier does not apply to these three types.
- `../_decompiled/EFT/Grenade.cs`: GetDamageInfo at 363 creates detonation damage
  from the thrower's profile ID; `EFT.Ballistics/Shot.cs` at 460 resolves the ever
  existed bridge. A grenade must not inherit the next physical life's powerup.

## Verification

Run `pwsh -NoProfile -File tools/ArenaPowerupChecks/Verify.ps1` from the repository.
The project source-links both production powerup files and the real administrative
death gate, exercises the actual patch prefix via reflection, and compiles eight
faulty production copies to prove important tests reject their named bugs. Installed
IL contracts check signatures, native ordering and the relevant internal vetoes.
Native/world/mode boundaries are explicit doubles, so these checks do not claim a
Unity/Harmony raid test. The verifier records source and installed DLL hashes and
refuses success if they change during the run. It never builds or deploys the client.

The runtime owner must verify exact current raid/mode/living actor admission in
`ArenaPickupRuntime.IsLiveActor`, reset Generation on every round/raid boundary,
and clear effects when the runtime becomes non-live. The root integration checks
own this dependency; it is not replaced by the doubles in this project.
