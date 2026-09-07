# Deferred Arena design work

Recorded 2026-09-07 at the user's request. These are discussion notes and proposed
work unless a section explicitly records implementation or test progress with
a linked review. Recheck current source and saved configuration when resuming.
Read `AGENTS.md` and `HANDOFF.md` first.

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
checks remain individually unconfirmed; further areas, larger teams and an area
picker remain future work.

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
