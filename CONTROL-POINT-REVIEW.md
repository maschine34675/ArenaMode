# Single control point — slice 0 contract

Proposed 2026-09-16. **Nothing here is implemented.** This is the frozen scope and
rule set to work against, written before any code, in the sense of
`TEAM-DEATHMATCH-PLAN.md`. `FUTURE-WORK.md` already recorded "a single
control-point prototype and CTF remain proposed later work"; this document turns
the first half of that sentence into an admissible contract and leaves CTF alone.

The user chose the control point over Capture the Flag as the first objective
mode, and chose exact BigBrain 1.5.0 as a **hard requirement** rather than a
silent degradation.

## Why a control point and not CTF

CTF's new invariant is an objective bound to a living body. Death, kit swap,
give up, respawn, round reset and raid teardown must all drop or return it
without touching inventory or hands — and this project has already paid for
runtime item handling with a latched `IsChangingWeapon` and empty hands for the
rest of a raid. A control point is bound to no one: its whole state is who
stands in a volume, how far the capture has progressed, who owns it, and what
that ownership pays. Teardown is a marker removal, like a pickup.

The point therefore proves presence, ownership and scoring without the carry
state. Whatever comes after it — three-point Domination or CTF — reuses that
proof. This document does not promise either follow-up.

## Two slices, deliberately separated

| Slice | Deliverable | Proves |
|---|---|---|
| 0 | Human-playable capture loop, no bot objective steering | rules, volume, scoring, teardown |
| 1 | Arena-owned bot movement toward the point | navigation and objective AI |

They are separated so a failed playtest is attributable. If capture and steering
ship together and bots never arrive, the cause could be the rule set, the volume,
the NavMesh, BigBrain arbitration or the SAIN handoff, and the raid cannot tell
them apart. In slice 0 bots fight as they do in independent Team Deathmatch and
may hold the point only by standing on it incidentally, exactly as they may
already cross a powerup.

Slice 0 must be honest about this in its own HUD copy: a point that only the
human contests is a prototype, not a finished mode.

## Chassis

Independent Team Deathmatch, not PitFireTeam. USEC versus BEAR, instant
respawns, existing Arena kits, the assembly/countdown/live/result lifecycle from
`TDM-MATCH-FLOW-REVIEW.md`, and `ArenaSession.IsActive` fail-open on every patch.
The profile stays untouched; both existing barriers remain at full strength.
PitFireTeam continues to force protected Tactical and is out of scope here.

### Reusable as-is

- Team population, faction relations, same-team damage suppression, team-colored
  tracers and lasers, the separated scoreboard, and the server's quiet location
  clone.
- `ArenaPickupGeometry.TryPlace` for validating the point: it already samples
  NavMesh within 0.75 m, bounds the final floor, and requires a
  `PathComplete` route from a grounded anchor. An objective point must pass at
  least this before a match may start.
- The area presets with opposing team starts: `customs-factory-center-teams-v1`
  or `customs-factory-east-polygon-v1`. Slice 0 picks exactly one and does not
  add a preset, a contour or an editor.

### Must NOT be reused, and why

**`TeamDeathmatchMatchPolicy` is kill-shaped.** Its terminal decision reads
`friendlyKills`/`opponentKills` against a `KillLimit`. Write a sibling policy
with the same shape — pure, simulation-clock, no actors or UI — rather than
widening it. Independent TDM's match flow is in use and its regressions are the
expensive kind.

**`ArenaPickupState.InReach` is a collection test, not a capture test.** It is a
1 m horizontal radius with a 0.9 m foot tolerance, sampled along a movement
segment so fast motion cannot skip a point. Capture needs sustained presence in
a volume with an explicit per-area height band, which is a different question.
Reuse the finite-value discipline and the segment idea; do not reuse the radius
as an area of control.

**`TeamArenaPursuitLeaseBridge` cannot carry an objective lease.** This is the
most important finding in this document and it contradicts the obvious plan:

- `HasExactTeamArenaContext()` requires `PitFireTeamCompat.TeamArenaActive`.
- `HasExactPostDeathContext()`, which gates every publish, read and report,
  additionally requires the local player to be **dead**.
- `IsValidUnsafe` requires a non-empty `TargetProfileId` that differs from the
  source, and `ArenaPursuitRuntime.TryGetUsableLease` resolves that id to an
  exact live `EnemyInfo` before the lease is usable at all.

A static control point has no target profile, the human is normally alive, and
PitFireTeam is not involved. Slice 1 therefore needs a sibling authority with
its own context gate, not a widened one. Widening the existing bridge would put
protected Tactical's movement ownership at risk for a mode that does not use it.

`ArenaPursuitLayer` and `ArenaPursuitLogic` remain the proven *shape* to copy:
`BotOwner.GoToPoint` to a NavMesh-validated destination, EFT's own node for
doors, mines, steering, sprint, pose and weapon selection, SAIN suspended
through `ArenaSainMovementHandoff`, and epoch/generation leases that fail closed.
Their live navigation success is **not** established: `PFT-GOAL-HANDOFF-REVIEW.md`
states that offline checks and matching binaries do not prove it, and `AGENTS.md`
still lists the PitFireTeam 5v5 GPU playtest as open. Slice 1 inherits that risk
and must not be described as resting on a proven path.

## BigBrain as a hard requirement

Without the companion Arena owns no bot movement at all, so an objective mode
would quietly become Team Deathmatch with a circle drawn on the floor. The mode
is therefore **locked in the picker** when exact BigBrain 1.5.0 and the Arena
companion are not both present, with a tooltip that names the missing
dependency. The precedent is PitFireTeam's existing lock, which disables the
other four buttons and their CanvasGroups and explains itself in a tooltip.

`ArenaModeCatalog.CanSelect` is the single admission point — `CanSelectIndex`
and `ResolveSelection` both route through it — so the gate belongs there and
nowhere else. Unlike the PitFireTeam lock, which depends on the selected roster,
BigBrain presence is static for the game process and readable from
`Chainloader.PluginInfos` at load, so it can be resolved once.

Bookkeeping traps around that gate, all of which currently resolve **to
Deathmatch** and would otherwise hide the problem instead of reporting it:

- `NormalizeId` and `ResolveSelection` fall back to `DeathmatchId` for anything
  unrecognised, and `ModeRegistry.Create` logs and falls back for an unknown id.
  A persisted control-point selection on a machine without BigBrain must resolve
  away deliberately and be repaired exactly once through
  `CommitInitialSelection`, not silently every frame.
- F12 exposes the mode id as free text. The same admission must reject it there.
- Arming must freeze the decision. A raid may never arm this mode without a
  registered movement consumer, because the picker and the raid are separated by
  the loading screen.

Slice 0 has no bot steering and would technically run without BigBrain. The lock
still applies from the start: shipping the mode as selectable and then adding a
dependency later would break existing selections.

## Population and server

`ArenaPopulationPolicy` must name the new id explicitly. Its own comment is
categorical: a mode the file does not recognise is treated as not owning its
population, which on the server takes the loud branch that raises PMC chances to
certainty and generates assault waves. The control point owns its population for
the same reason Team Deathmatch does, so it belongs in
`OwnsEntirePopulation`.

`SkipsProfilePreload` stays Team Deathmatch only for now. It exists because
TDM's difficulty clock deletes the matching pool entries; the control point
should not claim that behavior until it demonstrably shares that clock. A longer
loading screen is the safe side of this trade.

The shared file is linked into both halves, so this is a **server change**: the
server DLL must be redeployed and the SPT server restarted, not only the game.

## Rules that must be decided before any code

These are the rules that turn "simpler than CTF" into a real contract. Each one
is a gameplay decision, and leaving any of them to the implementation is how the
mode ends up being re-argued mid-raid.

1. **Both teams inside.** Freeze progress, block capture entirely, or let the
   larger group continue? Freeze is the conventional answer and the easiest to
   read on a HUD.
2. **Empty point.** Does an owner keep the point when nobody stands on it, or
   does progress decay? Keeping it is the king-of-the-hill reading and rewards
   map control; decay punishes a lucky first capture.
3. **Capture duration**, and whether additional bodies capture faster. A per-body
   multiplier interacts badly with team sizes up to 5v5 and should probably be
   left out of slice 0.
4. **Height band.** Per area, explicit, and tight enough that no one captures
   through a floor or a ceiling. This is the Domination-specific trap: a bare
   cylinder is wrong on Factory. The pickup layout already rejects wrong-floor
   candidates and that standard applies here.
5. **Scoring.** Ticks while held, a lump sum per capture, or both — and the win
   target. The mode ends on that target or on the existing time limit.
6. **Do kills score at all?** They may feed the scoreboard, but they must not be
   able to end the match, or the objective is decoration.

## Failure behavior

Fail closed, in the project's existing sense. If the point cannot be validated —
no complete path, no level floor, outside the active area — the **match refuses
to start** with a named diagnostic. This deliberately differs from pickups, which
log an invalid point and skip it: there the pickup is a bonus, here the point is
the mode. Starting a control-point match without a control point is the worse
outcome.

Loss of readiness, local death and body handover follow the existing TDM rules.
Ordinary raids and every other mode must remain bit-identical; the capture
runtime is gated on the exact active raid and the exact match, like the pickup
runtime it borrows geometry from.

## Verification boundary

Offline, in the project's production-linked style, with deliberately broken
variants that must fail their named assertion rather than fail to compile:

- The capture and contest state machine as a pure policy, including both-team
  occupancy, empty-point handling, ownership changes and the terminal result.
- Mode admission with BigBrain present and absent, a persisted id on a machine
  without it, the F12 path, and single repair through `CommitInitialSelection`.
- Population policy branch, and the server clone for the new id.
- Point validation against the area, including a rejected wrong-floor candidate.

Not provable offline, and not to be claimed: NavMesh reachability of the chosen
point, actual bot arrival, marker rendering, and whether the capture volume feels
right. Those are the GPU acceptance, on the one selected area, with the human
capturing, losing and recapturing the point, and with a deliberate check that a
player on the floor above or below cannot capture through the geometry.

## Sources checked

`ArenaMode.Client/Modes/ArenaModeCatalog.cs`, `ModeRegistry.cs`,
`TeamDeathmatchMatchPolicy.cs`, `TeamDeathmatchMode.cs`;
`ArenaMode.Client/Session/ArenaModeSelectionState.cs`;
`ArenaMode.Client/Bots/TeamArenaPursuitLeaseBridge.cs`, `TeamArenaGoalHandoff.cs`;
`ArenaMode.BigBrainCompat/Plugin.cs`, `ArenaPursuitLayer.cs`,
`ArenaPursuitLogic.cs`, `ArenaPursuitRuntime.cs`, `ArenaSainMovementHandoff.cs`;
`ArenaMode.Client/Pickups/ArenaPickupGeometry.cs`, `ArenaPickupState.cs`;
`ArenaMode.Shared/ArenaPopulationPolicy.cs`, `ArenaAreaCatalog.cs`;
`PICKUPS-REVIEW.md`, `TEAM-DEATHMATCH-REVIEW.md`, `TDM-MATCH-FLOW-REVIEW.md`,
`AREA-TEAM-SETUP-REVIEW.md`, `PFT-GOAL-HANDOFF-REVIEW.md`, `FUTURE-WORK.md`.

No decompiled game internals were newly verified for this document; slice 0 and
slice 1 each require their own check against `../_decompiled/` before
implementation.
