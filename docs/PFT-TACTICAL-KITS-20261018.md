# PFT Tactical: PMC equipment choice

Accepted scope: own gear remains the default. The map screen offers **Own gear**
and **Arena kits** for protected Tactical. Only the PMC receives a supplied kit;
companions keep their equipment. Solo Tactical's buy phase and PFT Zombies remain
unchanged. This is not a team economy or a per-round kit purchase feature.

## Lifetime and protection

`Tactical/TeamOwnGear` is independent of `Zombie/ZombieOwnGear`; menu policy and
the confirmation/loading preview use the current party, then the frozen arm.
Changing F12 during loading cannot change the raid's captured equipment choice.

The native `TarkovApplication.LocalGameMatching` preloads the original profile's
items. It later awaits `LocalRaidStarted` before `LocalGame.Create/Run` constructs
the first PMC body. PFT binds that body in its AddActivePlayer patch, so replacing
an already spawned leader is unsafe. Arena instead awaits one issuance inside
its native-start observer, after binding the exact protected native-end identity.
Required kit pools load even when optional kit warmup is disabled; the existing
optic role-model guard is explicitly admitted during loading. Malformed resource
metadata fails the start instead of treating partial loading as readiness.

`TacticalTeamKitEntry` captures the arm epoch/token, exact profile/account,
inventory root and matching world before awaiting. Both sides of the wait require
the same protected Tactical start and no local body. A stale HTTP result cannot
bind an identity; a stale load failure cannot cancel a newer ordinary or Arena
start, even when its numeric epoch did not change. Issuance is one-shot.

`TeamArenaEntryKit` constructs the base kit off-profile with fresh item IDs and
the original pockets template. It rejects duplicate/orphan graphs, incomplete
weapons, occupied protected destinations and missing required supplies. Overflow
spare magazines and optional grenades retain the existing placement policy.
The snapshot's existing transaction transfers the actual secure container,
scabbard, armband and pocket special-slot items, sanitizes quickslot/magazine
references and registers/examines only newly issued items. Failure rolls back
equipment, transfers and metadata; no live body, corpse, profile clone or follower
inventory is used. Consumed preparation records release the old combat tree.

OnMatchStart verifies the issued root before the normal protected team snapshot
captures it. After PMC death, that existing round lifecycle restores the issued
entry loadout. A survivor retains their current gear as before. The server's
profile protection and PFT's separate persistence suppression are unchanged.

## Verification

Production-linked menu, loading coordinator, native-start observer and inventory
transaction checks include deliberately compiled faults. Real EFT API compilation
and the existing loadout/kit/Zombie/PFT regression checks remain separate from
native acceptance. Deployment identities and actual GPU observations are recorded
in the accompanying handoff/native report, never inferred from these unit checks.
