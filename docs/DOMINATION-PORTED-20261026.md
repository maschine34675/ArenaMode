# Domination on the four ported maps

Domination remains independent USEC versus BEAR with continuous respawns, fixed
five-second capture/neutralization, one point per held uncontested objective per
second, 200 points to win and a ten-minute limit. Allied headcount does not
accelerate capture. Combat kills remain personal statistics, with no objective
income or persistent reward grant. PFT admission is unchanged.

## Original objective anchors

Each port uses its individual original `CapturePointEntity` transforms from the
Arena source scenes. The accepted imported scenes retain those same roots.
Factory day/night retains the existing A/B/C layout.

| Map | Label | Original world X / Y / Z | Original level / entity object |
|---|---|---|---|
| Equator | X | 1.28 / 0.25 / 4.22 | level57 / 2771 |
| Bay5 | X | 91.26 / -4.366 / 29.44 | level88 / 3348 |
| AirPit | A | 18.96 / -0.1 / -1.24 | level28 / 2484 |
| AirPit | B | -45.38 / 0 / 3.82 | level28 / 2507 |
| Bowl | A | -20.51 / 0 / -37.12 | level48 / 2580 |
| Bowl | B | 16.99 / -0.1 / -30.72 | level48 / 2603 |

AirPit's points are on opposite sides of the airliner; Bowl's are separate points
beside the helicopter. The legacy MapPort `*.modes.json` derived `capture_point`
is the average of two light roots on those maps and must not be used as a single
objective. An audit of all 28 original scenes found exactly these six entities.
The original serialized capture-volume fields are not authenticated, so ArenaMode
retains its tested four-metre radius, one-metre foot-height tolerance and visibility
check instead of guessing the meaning of unknown floats.

## Runtime boundaries

`DominationMapLayout` freezes the map's labels and count. Both match policies and
the objective bridge consume that same count. Unsupported maps, bounded areas,
PFT squads and missing objective support remain rejected at final admission.

Preparation proves every point's floor, standing clearance and complete paths
to/from the established main-court anchor before creating any marker. Ported
points permit only the existing nearby NavMesh/floor projection, with no two-metre
alternative-location search. Factory retains its existing bounded survey behavior.
Missing or blocked original points abort the protected match instead of silently
substituting a convenient location.

Ported Domination uses the existing independent TDM team-box planner, selected
faction starts, respawns, finite bot spawn shields and owned gate release. After
the gate completion contract and all native box-exit checks pass, each objective
must also have complete paths in both directions from both booth anchors before
the live clock starts. Rendering and navigation are still checked in the loaded
scene; the source audit alone is not gameplay acceptance.

Bots spread across the available objective set while retaining the same body,
match, medical, combat and path ownership safeguards. Pickup clearance applies
to every prepared objective through initial placement and rotation; pickup
beacons stay enabled. MapPort monitors now read objective scores directly,
including income without a kill-scoreboard version change, selected-faction
colors and the frozen terminal result. Ordinary TDM keeps kill scoring and its
existing cache. Native raid stop releases both monitor and objective ownership.
The [monitor review](DOMINATION-MONITOR-REVIEW-20261026.md) records the projection
checks and their native-test boundaries.

## Verification and deployment

Raw local evidence: `artifacts/domination-ported-20261026/`. The original-data
audit is `map-contract.md`; native tests are documented separately in
[the GPU report](GPU-DOMINATION-PORTED-20261026.md). Builds use the NoDeploy
targets and absent server destination before explicit installation.

The parent verification passed 3,213 unit tests with the existing APBS-specific
skip. Focused checks passed: policy 21,596 assertions / 31 mutants; admission
156 / nine compiled mutants plus four wiring controls; native menu 894;
original-box runtime 1,584 / 12 mutants; objective bots 145 / 15; objective
runtime 311 / ten; shared match lifecycle 479; monitor projection 275 / 15;
pickup spacing 37 / 12; and announcement, gate and spawn-shield regressions.
The old gate/shield harnesses needed their fixtures updated to the existing
Domination partial and the changed gate-call block; compiler or stale mutation
anchor failures were not counted as successful negative tests.

These checks link production code with explicit external doubles. They do not
replace the native floor/collider, navigation, rendering and bot observations.

Deployed SHA-256 identities:

| Assembly | SHA-256 |
|---|---|
| Client | `4E4EAC24D76513510B78F6D80E0030884E812C80CA0EB169C57A7F9EB74AC256` |
| BigBrain companion | `F43F074CADD68A8E8EEF78CF6148FA6261A00F10B0D7091A418F7F9E2705D8F1` |
| Server | `9CB6025157617A9B8314979A851AC1441D925EB449EA9974FBD34C90DB61F274` |
