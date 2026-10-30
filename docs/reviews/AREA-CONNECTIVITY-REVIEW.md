# Team-start contour connectivity correction — 2026-09-08

## Evidence and scope

The GPU report in `GPU-ACCEPTANCE-2026-09-08.md` records five failed starts of
`customs-factory-center-teams-v1`: the west-anchored query found zero suitable
BEAR PMC starts and only three or four of the five eastern bot starts. A legacy
raid of the same contour reached 17 PMC and 11 bot markers. Therefore these
results do not establish that the eastern markers are intrinsically invalid.

The old preparation used one native shortest-path query from the initial local
player to each marker. A rejected shortest route does not prove that no other
route through the contour exists. This correction checks that alternative,
without widening the contour or treating an outside route as valid. The exact
rejected GPU corner/segment was not previously logged; origin-dependent route
selection remains a diagnosis supported by the differing runs, not a replay of
the actual rejected native path.

Only the new team-start preset uses waypoint connectivity. Existing marker
objects/IDs, category and faction masks, bot zones, sniper exclusion, area
geometry, 0.75m sampling radius, 0.5m height tolerance, 0.4m navigation inset and
12-second preparation deadline remain unchanged. Legacy areas retain their
direct validation path. No PMC marker is converted into a bot spawn.

## Production behavior

`ArenaAreaConnectivityGraph` builds a bidirectional tree from the initial sampled
player anchor and the sampled native scene markers. Both directions of every
edge independently require `NavMesh.CalculatePath`, `PathComplete`, and the
existing full polygon corner/segment containment check. A reverse-only path,
one-way cycle, partial path, nearby point or disconnected island is not proof.

Preparation performs at most 24 directed discovery probes per frame. Scanning
continues on subsequent frames rather than waiting 0.5 seconds between slices.
There is one additional **linear finalization sweep** per finished attempt:
at most two directed native queries per reached marker (56 for the current
28-marker corpus, hard maximum 256 at the 128-marker cap). Thus a finalization
frame can cost up to 80 directed path queries for the current corpus, not 24.
Sampling/current-marker checks are additional native work. The final sweep
checks parents before children and prunes descendants when an identity, position,
sample or edge changed during multi-frame discovery. A deficient finished
attempt retries at the existing 0.5-second cadence, within the same deadline.

The shared placement helper is `CompleteConnectedPath(Vector3 sample)` in the
`ArenaAreaRuntime` partial class. Spawn admission, player replacement preflight,
teleports and the root-owned team arrival lease call this helper. A known sampled
marker takes its one stored tree chain; every vertex and both directions of each
leg are checked again. A stale chain rejects the candidate. Dynamic body/teleport
positions first try a direct bidirectional route and then fresh attachments to
the verified tree. Nothing caches a successful placement certificate across calls.

Known-marker placement costs at most twice the tree depth in directed path
queries (56 at the current 28-marker worst-case depth; direct/root-connected
markers are normally two). Dynamic attachment can examine all reached markers,
with a finite worst case of `2 + N*(2 + 2*N)` directed probes (1,626 at N=28;
33,026 at the hard cap N=128). Actual costs should be much lower with shallow
trees and the first successful attachment; they are not GPU-measured here.
This potential cost is disclosed rather than hidden behind a stale cache.

Graph reachability is **not movement permission**. `InsideCorners`, `AllowPath`
and the native accepted-path guards still inspect the actual route given to the
bot and reject every outside segment. The correction does not make native AI
automatically follow the graph. GPU follow-up must therefore check both full
assembly and subsequent movement/rejected-path stalls.

## Native contracts checked

- `_decompiled/BotPathFinderCorePoints.cs`, `CalculatePathFull` (74–215), already
  composes routes through cover/core legs. `GoToPosition` (513–545) supplies the
  actual resulting corners through `BotPathController.GoToByWay`.
- `_decompiled/BotPathController.cs`, `GoToByWay` (254 onward), creates the actual
  `VectorTargetBotPath`; existing Arena patches check the supplied/accepted path.
- `_decompiled/EFT/BotSpawner.cs`, `method_7` (898 onward), passes exact point
  positions and core IDs to native creation. The correction retains all original
  bot points and this dispatch path.
- Reversing an existing corner array is not used as native reverse-link proof.
  The reverse direction receives its own native calculation.

## Diagnostics

Each completed preparation attempt emits an anchor position, sample/reached/
fresh-marker counts, recovered-via-waypoint count and probe count. Failed sampled
markers report their identity/position and distinguish no nearby NavMesh sample,
wrong floor height and outside inset. Unreached/pruned markers report last tested
source, forward/reverse failure (calculation, partial/invalid, outside corner or
segment, stale marker), and current/fresh-chain state. Timeout always invokes the
root-owned team roster diagnostics, even if that attempt was already summarized.
No per-frame log spam is introduced.

## Offline verification and remaining GPU work

`tools/AreaConnectivityChecks` links both new production classes unchanged into
a pure executable. Its native surface doubles control sample/path responses;
they do not recreate the graph, adapter or placement policy. The checks exercise
directional asymmetry, one-way cycles, disconnected components, split budgets,
waypoint recovery, near-but-distinct targets across blocked links, final-sweep
pruning after a preparation-time edge change, stale forward/reverse edges,
marker identity/position/object replacement, changed floors, reset/raid-token
ownership, legacy isolation and timeout diagnostics. Ten source integration
contracts verify helper call sites, retained tolerances, timeout and actual
movement corner checks. Named source mutants must fail their targeted behavioral
assertions and compile successfully first.

Leaf result: 630 production-linked assertions, ten source integration contracts,
and thirteen named negative controls passed. Root owns final full-mod builds,
existing regressions, independent integration review, commit and deployment.
Independent reviewer reviewed the graph/adapter and requested the final sweep
and identity/replacement fixtures; no GPU execution has occurred in this leaf.

GPU retest: cold-start game/server with the final installed hashes; repeat 2v2
and 5v5 on the new team-start contour, both player factions when available, full
initial rosters, movement between sectors, repeated deaths/refills and protected
end restoration. Observe waypoint summaries and movement-watch diagnostics.
Confirm that legacy contour behavior remains unchanged. A passing offline graph
test or completed build does not count as this GPU acceptance.
