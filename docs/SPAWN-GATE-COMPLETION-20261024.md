# Spawn-gate completion

Independent Team Deathmatch on Equator, Bay5, Air Pit and Bowl now waits for
MapPort's public completion signal before checking that the team boxes connect
to the field. This removes Bowl's fixed 13.5-second minimum on MapPort builds
with the new capability. It does not change the formations, population limits,
spawn shields or preparation countdown.

## Optional API and ownership

`ArenaMode.Client/Compat/ArenaMapPortSpawnGates.cs` resolves the API only from
the registered `com.maschine.ArenaMapPort` plugin's own assembly, after the
existing exact game/world/arm/token checks admit a team-box TDM loading owner.
The required hold API remains `Hold(string)`, `Release(string)`, `IsHeld` and
`ClosedParts`. An existing foreign hold is not borrowed.

The added optional capability is a declared **public static bool `GatesOpen`**
property with a public, non-generic, parameterless getter. Binding caches a
delegate; it does not execute the getter during capability discovery. Arena
does not read MapPort's private state, `IsOpening` or
`OpeningSecondsRemaining`, and does not subscribe to its process-global
`GatesOpened` event. Polling avoids carrying an event callback into a later raid.

MapPort reports `GatesOpen` only when its registered parts and navigation are
installed, no hold remains and no opening is in progress. This is a prerequisite
for Arena's own current-scene checks, not a replacement for them. `IsHeld=false`
alone only says that the hold was released; `ClosedParts` counts registered parts.

## Opening sequence

1. The existing owned hold keeps the gates closed through team assembly and the
   countdown. The narrowly scoped intra-box path allowance remains unchanged.
2. At countdown completion, Arena records `Time.time` and releases that hold
   once. Successful release and a still-current game/world/arm/token are required
   before reading completion. A new hold blocks readiness even if a getter were
   to report true. Stale leases do not read global completion state or release a
   successor's hold.
3. When `GatesOpen` is true, Arena revalidates all twenty formation points:
   current canonical identity, floor/capsule clearance and a complete native
   NavMesh path to the field. A partial path or one blocked point keeps combat
   waiting. Bowl's alternate exits cannot bypass a still-moving gate.
4. Only then does the existing match flow enter combat, resume deferred native
   extraction entries and permit damage, scoring, match time and the start cue.
   Until then, the preparation notice remains OPENING SPAWN GATES.

The watchdog uses the same scaled simulation clock as the drive: twenty seconds
on Bowl, ten on Equator/Bay5/Air Pit. A pause does not consume real-time timeout
budget. A completion signal that remains false does not become ready merely
because the old minimum elapsed; it reaches the existing protected match-abort
path at the watchdog. A signal that is already true after an instantaneous
release can proceed immediately if all scene checks pass.

## Compatibility and failure rules

Only **absence of a `GatesOpen` member** selects legacy timing: at least 13.5
scaled seconds on Bowl and no additional minimum on the other box maps. The
same ownership, held-state, twenty navigation/clearance checks and watchdogs
remain mandatory. When no optional hold was acquired at all, the existing
open-box geometry validation remains in force without an added timer.

A same-name incompatible member is not an old API: wrong return type,
instance/private/inherited property, field or indexer is rejected. The adapter
retains its owned hold and normal cleanup, but the completion delegate raises a
contract error at opening. A real getter that throws likewise fails through the
protected match-abort path. Neither case silently falls back to time or drops
the integration after acquiring ownership.

Successful live respawns never call Hold again and never close the gates.
Cancellation, failed start and teardown keep their existing owned-release rules;
stale cleanup cannot release the next raid. Other modes and ordinary PMC/Scav
raids acquire no Arena gate hold. These ownership and respawn rules are unchanged
from [the original gate integration](EQUATOR-SPAWN-GATES-20261017.md).

## Verification

The targeted production-linked `tools/SpawnGateChecks/Verify.ps1` baseline
passed **324 assertions**, and all compiled negative controls were rejected at
their expected assertions. Coverage includes:

- an actual old-API fixture without `GatesOpen`, retaining legacy timing;
- valid completion on all four maps, instantaneous completion, false completion
  beyond Bowl's old minimum and timeout on simulation time;
- ready completion with one partial path or blocked clearance;
- unreleased/stale owner guards, a new hold, getter exceptions and malformed
  member fixtures;
- unchanged cleanup, preparation extraction and live-respawn behavior.

The full offline runner passed all four gates: 2,307 unit tests passed (one
environment-gated skip), 324 spawn-gate assertions, 424 formation assertions and
382 TDM runtime checks. The gate/formation negative controls passed. Installed
payload hashes stayed unchanged. An initial runner wrapper timed out at 120 s;
the complete rerun used a 900 s budget and exited successfully.
The initial Bowl-box results remain a historical
baseline in [the 2026-10-23 report](GPU-BOWL-TEAM-BOXES-20261023.md).

## Native verification

Source `73c61f7` was built, deployed and tested on SPT-GPU-2 with MapPort's
default duration scale of 0.5. All four 4v4 starts passed: Bowl entered Live
7.01 s after release, Equator/Bay5 4.00 s and AirPit 2.00 s. Both factions and
the player left their boxes; Bowl's death/Killcam/respawn did not re-close them.
All four refreshed DynamicMaps packages rendered, including full overviews.
Three additional short DM controls acquired no Arena hold. See the
[native report](GPU-SPAWN-GATE-COMPLETION-20261024.md) for identities, timeline,
restoration, separate log findings and untested cases.
