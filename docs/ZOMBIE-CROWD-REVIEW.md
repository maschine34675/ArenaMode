# Optional Zombie Crowd

Updated 2026-10-14. Crowd is a harder horde variant, not a promised FPS boost.
`Zombie / CrowdEnabled` defaults **off for new configurations**; saved choices
are preserved. The map-screen `ZOMBIE CROWD` switch occupies the slot used by
`FAST BOTS` in other modes, retaining 22 visible controls and their existing size.
The menu locks after arming; F12 changes apply immediately during the raid.

## Behavior when enabled

- At most one farthest eligible infected per frame is considered beyond 26 m,
  with a two-second per-body recall cooldown. Its stable yaw covers the full
  360-degree ring; candidates are 12, 16 and 22 m from the local living player.
  Shooters can also be recalled; followers and non-generic roles cannot.
- The actual sampled NavMesh point is used. Bounded maps require the exact
  active body, ready area, navigation inset, connected inside path and an
  unoccupied/unreserved sample. There is no full-map fallback for a bounded area.
  Full maps require a sample within 2 m and a complete path to the player.
- Both paths require a physical floor and free standing capsule. Living actors
  on the same floor must be at least 3 m away. A saturated collision buffer or
  three occupied candidates rejects the recall; no crowded fallback stacks bots.
- A successful recall stops the old movement and adds a search point toward
  the player. Respawn swaps and a dead local player never drive recall.
- Generic infected skip local avoidance and clear both native displacement
  offsets every time. This also removes new offsets written by nearby followers.
- For generic melee infected (easy/normal), the native sight cast is skipped
  within 80 m, including walls/view cone. Other visibility progression stays
  native. Shooters, targets beyond 80 m, followers, dead/inactive bots and
  non-Zombie raids retain the native method.

## OFF and ownership

OFF skips all three Crowd paths before they can teleport, stop a mover, add a
search point, override sight or clear avoidance. Zombies use the pre-Crowd
movement/sight/avoidance behavior. Previously moved bots keep their current
position; OFF does not teleport them back. Existing area safety and native
spawn placement remain independent of Crowd.

**Rendering is entirely native, with Crowd either ON or OFF.** The old custom
`forceRenderingOff`, skin quality, offscreen skin updates, shadows and animator
culling writes were removed. There is no cached renderer state to restore on
local death, spectating, body retirement or pooled equipment reuse. Install
this version with a fresh EFT process; do not try to repair an old process's
pooled state by guessing original values.

Population caps/waves, attack limits, voice/hearing budgets, PFT fast paths,
corpse release, protected profiles and normal raids are unchanged.

## Verification

See [the fix record](ZOMBIE-CROWD-FIXES-20261014.md) for current build/native
acceptance and [the earlier performance comparison](GPU-ZOMBIE-REVIEW-20261014.md)
for the measured higher combat density. Those earlier numbers do not certify
this updated version's performance.

`ZombieCrowdPolicyTests` covers pure recall/eligibility/range math.
`tools/ZombieCrowdChecks` executes the production runtime and bounded adapter
against controlled native doubles; five behavioral mutants must fail.
`tools/TdmMatchMenuChecks` executes the real settings/selection/view for saved
choices, menu locking, hidden callbacks and the unchanged 22-row layout.
None of these doubles proves Unity collision geometry or a PFT spectator raid.
