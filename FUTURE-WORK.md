# Deferred Arena design work

Recorded 2026-09-07 at the user's request. These are discussion notes and proposed
work unless a section explicitly records implementation or test progress with
a linked review. Recheck current source and saved configuration when resuming.
Read `AGENTS.md` and `HANDOFF.md` first.

## Zombie follow-ups from the 2026-10-09 performance test

- Pending wave-clear admission fixed in `5637ce7`, including delayed requests,
  known failure recovery and watchdogs. See `ZOMBIE-WAVE-PENDING-REVIEW.md`
  and the subsequent `GPU-ZOMBIE-WAVE-RETREAT-RESULTS-20261009.md` for acceptance.
- Native retreat planning measured and deliberately left unchanged: about
  0.0036 ms/frame for all 56 infected, with close-range successful checks for
  melee and shooters. No supported-cap increase or FPS gain follows from this.
  Revisit only with evidence of a substantially different failed-path workload;
  see `ZOMBIE-RETREAT-REVIEW.md`.
- PFT's redundant hostile-layer inspection now has an exact native-infected
  fast path (`ae6031a`, corrected for repeated native wait-layer entries by
  `4220c05`). Native wake, custom brains and followers retain PFT. Repetitive
  voice playback now has a six/four-distance budget (`66ab250`), preserving
  nearby attack/critical phrases and AI events. See
  `GPU-ZOMBIE-VOICE-PFT-RESULTS-20261009.md` for source-bound native evidence
  and comparison limits; subjective voice balance remains a listening check.
- SAIN global infected-to-infected pair work is a potential upstream follow-up,
  not an implemented Arena optimization. Required companion/hearing cross pairs,
  early initialization and pending native-array jobs must survive. See
  `ZOMBIE-SAIN-GLOBAL-REVIEW.md`; there is no verified safe owned filtering seam.
  Measure live pair counts, empty sound-cache frequency and allocations before
  requesting a sparse producer API. Native duplicate wait-layer growth is also
  documented, but no native list is deduplicated or AI behavior changed here.
  Population limits remain unchanged; inclusive parent/child profiler times
  must not be added or treated as an FPS gain.
- The already-known shared PFT roster-observer shutdown diagnostic recurred in
  the final squad smoke: a living wrapper disappears immediately after native
  `Stop(Left)`. Protected exit and complete profile/companion preservation pass.
  A separate fix should stop live-roster observation during teardown of its
  exact game, without weakening active-play identity or persistence guards.

## Difficulty controls in the native game menu

Goal: expose understandable, mode-specific settings without conflating bot AI,
equipment, population pressure and victory rules. Actual gameplay balancing
tests remain the user's responsibility.

Current mechanisms established by read-only inspection:

- `ArenaPressure`: independent time/streak curve. Saved settings: full time
  pressure at 12 minutes, up to 35 percentage points at eight consecutive kills;
  death resets the streak contribution but not elapsed match time. Arena's PMC
  requests use hard -> impossible at 80%; Scavs normal -> hard at 50%. The PMC
  floor is hard even when the configured maximum is easy/normal.
- Ambient refills in Endless, Rounds and Gun Game: saved PMC floor 8 plus up to
  4, bounded by the map refill target; base cooldown 8 s -> 4 s at full pressure.
  Map targets are Factory 12, Labs/Labyrinth 10. Additional native/mod waves and
  bosses have separate paths; this is not a universal maximum or AI override.
- `BotTierScalePatch`: separate server generation curve, saved midpoint level
  1 -> 66 over 15 minutes with a +/-5 range. Affects generation context, not
  existing bot gear or an Arena HP/damage multiplier. Protected PFT is excluded;
  TDM is not. Cached, already generated profiles can retain older equipment.
- SAIN preset behavior remains relevant. The Arena filter replaces Rat, Timmy
  and Coward with the preset's Normal personality on assignment. Personality
  Normal is distinct from native BotDifficulty.normal; existing bots are not
  explicitly reassigned when toggling the filter.
- HuntPlayer supplies player positions periodically, not a universal aggression
  multiplier. TDM uses its own mutual opponent awareness and ignores this switch.
- Player kits, skills/movement aids, medical/magazine speed and respawn protection
  independently affect practical difficulty.

Mode distinctions to preserve:

| Mode | Existing relevant rules / constraints |
| --- | --- |
| Endless | Continuous population and shared time/streak progression. |
| Rounds | Saved 15 kills OR 300 s, 15 s break; world/bots and global progression remain across rounds. |
| Gun Game | Saved 24 weapon rungs; local kills advance, final-rung kill starts another ladder; optional death demotion currently off. Enemy progression is time-based, not tied to the player's rung. |
| Solo Tactical | Saved 5 PMCs, selectable 1-15 or Scavs; 20 s buy, 240 s fight; saved economy 250,000 start / 35,000 kill / 150,000 win / 80,000 loss. |
| Protected PFT Tactical | Fixed player+4 vs5; saved 3 wins; protected entry loadout, no buy economy, no Arena gear ramp. Existing PFT follower lifecycle requires hard AI. |
| Team Deathmatch | Saved 5v5, selectable 2-12 per team before match; pressure and equipment progression affect both factions, including allies. No separate enemy-only strength slider. |

Proposed UI: AI strength, equipment, population/pace, and mode rules, with optional
mode-specific Easy/Normal/Hard presets built on top. A real easier PMC baseline
needs new logic; relabeling the existing maximum would be misleading. Hide
irrelevant controls (ambient PMC floor in Tactical/TDM, HuntPlayer in TDM).

Explicit application labels are required:

- Live/next update: pressure parameters, future position reports, refill choices.
  Existing knowledge/population/AI is not automatically undone.
- New bots/profiles: AI choice, personality assignment and equipment generation.
- Next round/life: proposed safe boundary for Tactical size/type, timer/rewards,
  kit and respawn protection; some current readers otherwise react immediately.
- Next match: mode, TDM team size, PFT win target, map clone limits/waves/bosses,
  frozen kit/ladder catalog and rung count.

The web client-settings sync polls in the menu, not during active Arena raids.
Native-menu server settings need an explicit validated save/override path; the
current config route reads config. Native difficulty datasets are process-cached:
do not clear global caches to force an Arena-only tuning change. Relevant entry
points: `Config/Settings.cs`, `Config/SettingsSync.cs`, `Bots/ArenaPressure.cs`,
`Bots/ArenaSainPersonalities.cs`, `Session/ArenaClientConfig.cs` under the client;
server `Patches/BotTierScalePatch.cs` and `Services/ArenaSessionService.cs`.

## Smaller playable areas within larger maps

User question: mark an area visibly in-game, prevent the player from crossing its
boundary, and restrict bots to the same area. Implementation was authorized on
2026-09-07; the first opt-in prototype is now Factory South v1 for independent
TDM 2v2. See [BOUNDED-AREA-REVIEW.md](BOUNDED-AREA-REVIEW.md). Source/build tests
are separate from user GPU evidence. The user subsequently confirmed a visible,
impassable grid, contained player/bot spawns and bots staying inside without
observed stalls. Escape/re-entry and teardown into full-map/ordinary raids still
need explicit confirmation. A first Customs Factory Center 2v2 candidate now
follows in [CUSTOMS-AREA-REVIEW.md](CUSTOMS-AREA-REVIEW.md). The user subsequently
reported that Customs also works overall without problems, while its boundaries
should eventually be refined manually. Keep that contour polish as a separate
scene-guided task, including renewed spawn/connectivity validation and a new
preset version when geometry changes. Detailed respawn/edge, menu and teardown
checks remain individually unconfirmed; further areas and larger teams remain
future work. A native area picker is now implemented below the mode panel:
Factory day offers Whole map/South, and Customs automatically selects Factory
Center in independent TDM without an F12 prerequisite. Both bounded choices
remain fixed 2v2 with unchanged geometry. See `NATIVE-AREA-MENU-REVIEW.md` for
verification; the user subsequently reported "ja passt" for the picker. This
does not independently confirm each detailed menu/PFT transition.

The next authorized step now implements simple concave polygon prisms and a small
local-raid point capture/undo/export aid; see `POLYGON-AREA-REVIEW.md`. Following
the user's finding that Arena-only capture prevents walking a new outline,
normal PMC surveying has its own opt-in switch alongside the capture master.
It changes no ordinary raid rules/profile behavior and never opens Arena borders.
The user explicitly has no Customs coordinates yet and chose capture help first.
Factory/Customs preset coordinates and IDs remain unchanged. JSON drafts never
activate themselves; later scene fitting, spawn/connectivity checks and a new
shared preset version are required. No duplicate Waypoints spawn viewer or large
editor is included. Polygon capture/UI and actual Unity collision/navigation
still require the user's GPU test; offline evidence alone cannot establish them.

Design direction (partly implemented by the prototype; broader support remains):

- Named per-map area definitions, selected before the raid and frozen for both
  halves of the exact Arena session. Start with one connected, manually authored
  area and its allowed entrances. Later an opt-in development editor could place
  corner/barrier points in the actual scene and save a reusable preset.
- Use real 3D volumes or floor-specific polygons, not only an X/Z rectangle:
  ceilings, stairs, ramps, underground passages and nearby separated rooms matter.
- A visible boundary (for example a restrained translucent grid plus floor
  markers) and matching physical barriers are separate pieces. Validate collision
  layers, sprint/jump/vault routes and projectile/grenade/line-of-sight behavior;
  visual transparency alone must not imply a particular collision rule.
- Make the barrier a navigation obstacle as well. Tarkov's `NavMeshDoorLink`
  already creates NavMeshObstacle objects and `BotDoorsController` toggles carving.
  This is a usable mechanism, not proof that every SAIN/PFT decision respects a
  newly cut area. Carving updates are delayed, so settle and validate navigation
  before releasing actors.
- All spawn entry points must share the same area membership rules: initial
  local spawn, respawns, server waves, client bursts, Tactical squads, TDM refills,
  PFT leader/follower placement and recovery candidates. Do not fall back to an
  unrestricted spawn when the region has no valid choices; reject that setup.
- Restrict movement destinations as well as physical access: patrol/search,
  cover/retreat, enemy pursuit, doors/traversal links and stale cached routes can
  still point outside. Check connected routes inside the allowed area, not merely
  whether both endpoints happen to be inside. Do not solve stuck bots with an
  unconditional every-frame movement override.
- Prevent ambient/external spawns and targets outside the area from keeping a
  match unwinnable or attracting bots to the wall. Add a bounded out-of-area
  recovery path for unexpected displacement, preserving exact actor/epoch and
  protected teammate ownership. Physics is primary, recovery is a safeguard.
- Create only Arena-owned temporary scene objects and remove them at teardown;
  normal raids and map assets remain unchanged. Choose and validate preset data
  before loading; validate the actual scene and navigation after loading, before
  releasing actors. Do not shrink or move boundaries during active combat in the
  first implementation.

Suggested first slice: a connected wing bounded by existing walls and a few
closed passages, with enough separated spawns for one supported mode. Inspect
boundary rendering without autonomous gameplay; the user checks player collision,
vaulting, bots fighting/retreating and round/respawn transitions in actual raids.
PFT compatibility needs its own explicit placement/navigation validation before
claiming that mode supported by a region.

Native/source anchors: `../_decompiled/NavMeshDoorLink.cs`,
`../_decompiled/BotDoorsController.cs`, `../_decompiled/EFT.Interactive/BorderZone.cs`,
`ArenaMode.Shared/ArenaBotSpawnPolicy.cs`, and the client's spawn/pursuit services.
Native BorderZone is trigger/threat handling, not a ready-made enclosing hard
barrier. Unity's obstacle documentation explains why carving is distinct from
short-range avoidance; see [NavMesh Obstacle (Unity 2022.3)](https://docs.unity3d.com/cn/2022.3/Manual/class-NavMeshObstacle.html).

## More complex objective-based modes

2026-09-08 follow-up: independent TDM match limits, shared scoring countdown and
terminal results are implemented in `TDM-MATCH-FLOW-REVIEW.md`; GPU acceptance is
separate. Respawn line-of-sight fairness, a single control-point prototype and CTF
remain proposed later work. This feature adds no objective bot steering.

2026-09-16 follow-up: the user chose the single control point as the first
objective mode, ahead of CTF, and chose exact BigBrain 1.5.0 as a hard
requirement rather than a silent degradation. `CONTROL-POINT-REVIEW.md` holds
that frozen scope: the king-of-the-hill rule set, the split into a human-playable
slice and a later bot-steering slice, the rules still to be decided, and the
finding that `TeamArenaPursuitLeaseBridge` cannot carry an objective lease
because it is gated on protected Tactical with a dead local player and requires
an exact live enemy profile. Still a proposal; nothing is implemented.

Recorded 2026-09-07: the user wants to revisit more complex game modes, with
**Capture the Flag** as an explicit example. Deferred idea only; no implementation
or final rules selected.

For a later CTF design, consider two team bases, flag pickup/carry/drop/return and
capture rules, team scoring, win conditions and clear objective markers. Bots
would need objective-aware roles such as attacking, defending, escorting the
carrier and recovering a dropped flag; ordinary opponent pursuit alone is not
enough. Define flag handling on death, respawn, round reset and match teardown.

Map-area presets could also store bases and objective positions, with reachable
routes and suitable team spawns. Assess reuse of independent Team Deathmatch team
handling without assuming PFT is required. Preserve Arena-only state and profile
isolation; detailed feasibility, rules and other candidate modes remain open.

## Arena flow, pickups and later variants

Accepted for this roadmap on 2026-09-23. Only **health pickups and purposeful bot
movement to useful available powerups** are the next implementation task. The
other ideas below remain proposals, not enabled game rules or promised dates.

1. **Contested powerups:** make Double Damage and strong weapon pickups valuable
   map destinations, with recognizable places and eventually a short notice before
   they return. Bots should deliberately seek eligible useful powerups rather than
   collect them only by chance. Do not send bots to local-only weapon pickups.
   Preserve immediate combat/danger responses and bound failed navigation attempts.
2. **Small health pickups:** Deathmatch and independent TDM only; a limited amount
   of HP without automatically curing bleeding, fractures or every injury. Keep
   damage/bleeding meaningful. Use the existing placement/cooldown infrastructure;
   healing must not alter a persistent profile or require an inventory item.
3. **Classic weapon-pickup Deathmatch:** a separate optional variant with the same
   basic starting equipment for everyone and stronger weapons acquired in the map.
   Do not silently replace the existing kit-based Deathmatch.
4. **King of the Hill before CTF:** start with one contested control point and
   objective-aware bots. The earlier `CONTROL-POINT-REVIEW.md` decision remains;
   pickup navigation is a useful bounded precursor, not proof of a complete
   objective mode or permission to widen the PFT pursuit contract.
5. **Jump pads and occasional teleporters:** possible later additions for faster
   routes and vertical play. Safe landings, bot navigation, area boundaries and
   respawn/round cleanup need separate design and native tests first.
6. **Instagib:** optional fun variant with the same precise weapon, one-hit kills
   and immediate respawns. Separate balance/profile-safe rules; not a change to
   ordinary Deathmatch damage or the existing Double Damage effect.

Sequence: health pickups and bot powerup objectives first; the control-point
prototype afterwards. Avoid a large new configuration surface.

2026-09-23 delivery: health pickups and optional BigBrain pickup objectives are
implemented in `aac5822`; see `PICKUP-OBJECTIVES-REVIEW.md` and the dated handoff.
Offline verification and local/GPU disk delivery are complete; native gameplay
acceptance remains open. The remaining variants/announcements above are deferred.

## Private online Arena with an optional Fika integration

Recorded 2026-09-21 at the user's request. **Planning only; no multiplayer
implementation or compatibility claim.** The goal is an uncomplicated way to
play Arena with or against friends over the internet, preferably without any
VPN client. Global matchmaking, public ranked play and large public servers are
explicitly outside the intended scope.

Fika is a plausible networking foundation: it supplies lobby/raid networking,
base-game replication and documented mod events/custom packets. It is designed
for cooperative play with trusted friends, not a complete PvP ruleset or an
anti-cheat service. Keep it an optional dependency through a separate Arena
adapter; the existing solo path must continue to work without Fika. Select and
audit a concrete SPT 4.1-compatible Fika version before implementation; current
documentation is not proof of compatibility with our installed builds.

### Connection goal: no VPN requirement for friends

Fika already supports internet play without a VPN, but two connections must be
considered separately:

- **SPT backend:** all participants need access to the same backend for profiles
  and lobby operations. With direct hosting, the documented default is TCP 6969.
  It needs an externally reachable address and appropriate host/router setup.
- **Raid host:** the documented direct-hosting default is UDP 25565. This may be
  a player's game or a separate Fika Headless instance; backend and raid traffic
  need not terminate on the same machine. Forward each port to its actual owner.
- **NAT punching:** Fika offers a public introduction service and a self-hosted
  alternative. This can establish the raid connection without manually forwarding
  the raid port, but does **not** make the SPT backend reachable. The public-service
  guide requires compatible Full-Cone NAT on both sides; it is router-dependent,
  and service availability is not guaranteed. This is not a guaranteed traffic
  relay or a replacement for backend hosting. It shares connection IP addresses
  with the introduction service.

Preferred later setup: a private, externally reachable SPT backend and raid host,
with one-time configuration by the host and a saved server address for friends.
Assess a small join/setup UI with separate backend and raid connectivity checks,
clear diagnostics and mod/version checks. A shareable address or future invite
helper is a convenience feature, not matchmaking; a code alone cannot fix an
unreachable host. Friends still need legitimate game copies and compatible
SPT/Fika/Arena installations.

Do not promise zero configuration on every connection. CGNAT/shared public IPs
can prevent host-side port forwarding, and NAT punching can also fail. A publicly
reachable dedicated host is one alternative; a transparent relay/tunnel fallback
would require a separate feasibility/security/operating-cost decision, not an
assumed Fika feature. No firewall rules, router mappings or public exposure are
authorized by this planning note. Before hosting, review access restrictions,
backend/profile security and exactly which services must be reachable; do not
expose development/test/admin tools as part of the game endpoint.

Fika Headless is a possible later raid host, including on the existing machine
without a GPU: its documentation describes a full SPT/Fika game instance with
rendering disabled, still requiring game files and substantial RAM. Running only
SPT.Server does not move the raid simulation or AI to that machine. Hardware,
mod compatibility and actual performance need separate validation.

### Arena work and first acceptance milestones

The current code deliberately rejects Fika. Removing that check alone is unsafe:

- One authoritative raid host must own match rules, readiness, team membership,
  scoring, timers, bot population and result decisions; clients consume confirmed
  events. Freeze the selected map/area, kits and gameplay settings for everyone.
- Replace the current single-human roster assumption with stable participant
  identities and explicit life/round identities. Network respawns must replace
  the body, equipment and remote representations without stale packets affecting
  a later life. This is the principal technical risk, not merely lobby creation.
- Preserve every participant's profile, including Fika save/death/disconnect
  paths and recovery after host failure. Our exact local-raid end interception
  is not proof that those paths are protected.
- Arbitrate pickups and effects on the host, verify inventory replication, and
  test double jump/air control, damage, team markers and spectating from another
  player's view. Reuse existing kits, area definitions, rules and HUD where
  appropriate. Human teams do not require PitFireTeam; bot/PFT support needs its
  own later scope rather than inheriting a compatibility claim.

Suggested sequence, with native multi-client evidence at each step:

1. **Private 1v1 proof of concept:** fixed kits, separated starts, one elimination
   round, no respawns, bots or pickups. Both clients see the same start and result;
   both profiles remain unchanged. Test the VPN-free connection from separate
   internet connections, not just two clients on the same LAN.
2. **Network respawn and round reset:** repeat death, body replacement and kit
   assignment from both perspectives; test delayed/duplicate events, disconnects
   and host loss. Verify profile recovery and regression-test ordinary/solo raids.
3. **Mode expansion:** TDM, Gun Game and cooperative matches, then bot-filled
   slots, assists, pickups and additional movement rules. Decide reconnect and
   join-in-progress behavior explicitly instead of assuming Fika provides Arena
   state restoration. Reassess larger teams only after the small setup is stable.

Sources checked 2026-09-21; recheck before implementation:

- [Fika features and limitations](https://wiki.project-fika.com/general-information)
- [Fika mod events and custom packets](https://wiki.project-fika.com/modding-fika)
- [Direct hosting and default ports](https://wiki.project-fika.com/hosting-a-fika-server/host-using-port-forwarding)
- [Hosting methods and CGNAT limits](https://wiki.project-fika.com/hosting-a-fika-server/choose-your-hosting-method)
- [NAT punching options](https://wiki.project-fika.com/advanced-features/nat-punching)
- [Public NAT punch service: backend exclusion and router requirements](https://wiki.project-fika.com/advanced-features/nat-punching/fika-nat-punch-server)
- [Headless host requirements](https://wiki.project-fika.com/advanced-features/headless-client)
- [Fika project/license guidance](https://wiki.project-fika.com/): CC BY-NC-SA 4.0;
  respect the published restrictions on monetization, large public servers and
  artwork. Prefer integration over copying or bundling Fika code/assets.
