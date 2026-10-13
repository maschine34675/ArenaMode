# Zombie crowd — nearby horde, cheaper melee vision

Factory-first. The goal is more infected in view of the player, not a fixed
frame-time budget. Clumsy motion is acceptable. `Zombie / CrowdEnabled`
defaults on and applies immediately; turn it off to compare against native
placement and vision.

## What it changes

`ZombieCrowdRuntime.Tick`, from `ZombieMode.Tick` while the raid has no
result yet:

- Once per frame, the farthest generic infected farther than 26 m, and not
  recalled in the last 2 s, is stopped and teleported onto a 16 m ring behind
  the local player's current look direction. The yaw list starts at a stable
  slot of that body on a 12, 16 or 22 m ring. A slot another bot already
  occupies is skipped, and if every ring is taken the body still moves to
  its own angle. The move requires
  `ArenaAreaRuntime.AllowTeleport`. On the full Factory map, where that runtime
  is not active, the same recall instead requires a NavMesh sample within 2 m
  and a complete path to the player. A player standing in a pocket the bots
  cannot enter still produces no move.
- The same tick cheapens one not-yet-seen body: child renderers cast no
  shadows, skinned meshes use `SkinQuality.Bone2` and do not update off
  screen. Turning the setting off does not restore a body already cheapened.

`EnemyPartVision.CheckLineOfSight` prefix, melee difficulties only (`easy`
and `normal`; `hard` shooters keep the native HighPoly cast):

- Inside 80 m, `HasLineOfSight` is set and the cast is skipped, including
  through walls and outside the view cone. The rest of the native visibility
  ramp still runs. Beyond 80 m the original cast runs.
- Followers, inactive or dead bodies, non-generic roles and any raid that is
  not the active Zombie raid keep the original method. Exceptions fail open
  and warn once.

Both paths use the four generic infected roles. `infectedTagilla` is not
included. A recall also repeats the existing `AddPointToSearch` toward the
local player so the old path is not resumed.

## What it does not change

Population caps, spawn rings, the attack limit of eight, shooter weapons,
voice, hearing, the PFT fast path, corpse release, profile writes, and
ordinary raids. Recall does not run during a respawn swap. The 26 m ring is
not a guarantee that every infected is on screen: a player standing on the
area boundary can reject every candidate, and the body then waits out its
cooldown.

## Offline checks

`ZombieCrowdPolicyTests` covers the recall threshold and cooldown, eligibility,
the melee sense range, and the behind-the-player offset including a
degenerate look direction. It does not execute the teleport, the cast skip,
or the renderer write.
