# First bounded Customs area

2026-09-07. Continuation of the user-confirmed Factory boundary/spawn prototype
(`0c54b77`; acceptance record `011beb5`). The user owns actual raid testing.
Customs implementation `a690ce2` subsequently received a successful user playtest:
overall trouble-free operation, with manual boundary refinement still desired.
The exact scope of that report and remaining checks are recorded below.

## Scope and activation

Restart server and game after deployment. Before loading, set F12 -> Team
Deathmatch -> `ExperimentalArea` to `customs-factory-center-v1`. Select Arena,
Team Deathmatch and the native Customs tile. This preset fixes the match at
**2v2**, including the human PMC; PitFireTeam is not used. Customs becomes
selectable only for this exact independent TDM preset. There is no full-map
Customs Arena, Customs Tactical/PFT, editor, area picker or CTF implementation.

For existing full-map matches set `ExperimentalArea = full-map`; the existing
Factory South preset remains `factory-south-v1` on Factory daytime. Its geometry
is unchanged. Other modes ignore the saved TDM area and retain their old maps.

## Candidate geometry and limits

Shared versioned definition `ArenaAreaCatalog.CustomsFactoryCenter`:

| Property | Value |
| --- | --- |
| Map | `bigmap` (Customs) |
| Native bot zone | `ZoneFactoryCenter` |
| Bounds minimum XYZ | `(305, 0, -135)` |
| Bounds maximum XYZ | `(420, 12, -65)` |
| Spawn foot-height band | `0.5 .. 3.0` |
| Size | `115 x 70 m`, fixed 2v2 |

Installed `locations/bigmap/base.json` provides 15 distinct PMC positions usable
by either faction and seven distinct bot positions within these bounds. For
each PMC start there is another PMC start at least 40m away and at least three
bot positions at least 25m away. Some starts have exactly three such AI choices;
there is little spare capacity if live connectivity rejects a key point.

Spawn heights are observed at about Y=1.12..2.18. The enclosing floor and ceiling
are design choices, not measured building limits. The northern face can cut the
switch-building footprint; stairs, lower floors, doorways and paths around that
edge need particular attention. This is an experimental candidate, not a claim
that the box follows every building wall. Old Gas alone was rejected because
its true PMC starts form one cluster without a 40m respawn alternative.

No new spawns are invented. Geometry filtering on the per-raid server clone is
followed by the existing live scene filter and connected NavMesh/path check.
Insufficient connected/spaced points abort preparation rather than falling back
outside the area. Physical bodies, navigation destinations, paths, direct route
setters, teleports and respawns retain the Factory prototype's scoped guards.
The previously documented limitations (sampled carving probes, projectiles,
grenades and AI sight not promised blocked) still apply.

## Integration and isolation

- `TryResolve` rejects Customs plus omitted/blank/`full-map` area before snapshot
  recovery, preparation or arm replacement. Exact area/map/mode and independent
  team status remain required on client, server, response and runtime ownership.
- The new server map default uses its real zone. Existing disabled/custom map
  values survive config merging. The client upgrades only the two exact former
  standard allowlists; edited lists remain user-owned.
- A Customs-only solo configuration can still enter the mode screen even when
  a different mode was saved. A PFT group cannot use that capability as its only
  map. Native tile construction uses configured availability; selection/start
  use the strict current mode/area eligibility.
- Only the existing Customs row is shown/hidden as mode or F12 values change;
  invalid selection is cleared through the native setter. No repeated native
  `ShowInternal`, shared location mutation or global event subscription is used.
  The 0.2s screen-owned updater uses cached configuration, not HTTP. Closing or
  changing the exact controller disposes it and restores its native row.
- Customs has Savage-only markers bearing the Player category. The local PMC
  now requires the native side mask to include its actual USEC/BEAR faction.
  Missing, Scav and unsupported profile sides fail closed. Loading, readiness
  and respawn all use this policy; the profile is available before MainPlayer.
- Ready labels and minimum AI capacity derive from the captured definition.
  No optional mod source or binary is copied or bundled.

Native contracts checked locally: `EFT/BaseLocalGame` creation/profile/Run,
`EFT.Game.Spawning/ISpawnPoint`, `EPlayerSideMask`,
`EFT.UI/LocationButton.Show` (toggle false does not select; `Select(false)` does),
and `MatchMakerSelectionLocationScreen` ShowInternal/SelectedLocation/Close.

## Automated verification and review

Run `tools/CustomsAreaChecks/Verify.ps1` with stages Rules, Integration and Build;
pass `-SptRoot C:\SPT41` on the GPU host. Builds deploy automatically, so serialize
these runs and stop game/server first. The spawn policy's native-source check
also expects `Development/_decompiled` under that installation.

Final serial verification on 2026-09-07 passed:

- Full server/data suite: **330 passed, zero skipped**, including 61 new Customs
  cases. The targeted Rules and integration selections passed 74 and 95 cases.
- Native menu negotiation/owned-row checks: 195 assertions, including the actual
  PFT policy's strict-versus-picker eligibility path. Mode selection: 130 checks.
  Existing native map, bot spawning, TDM runtime/give-up and bounded respawn
  harnesses also pass; Factory assertions remain present.
- The production-linked PMC side/category policy passes its matrix and named
  regressions. Four isolated spawn-policy mutants, two Customs admission/menu
  mutants and the three prior boundary/respawn mutants fail at their intended
  assertions; restored positive controls pass.
- Release solution and final explicit Release companion/client/Redeploy build
  succeeded. Four deployed DLLs match their build outputs by SHA256.
- Independent adversarial review closed both parts of a PFT/F12 synchronization
  defect: comparison must include the actual saved mode, and presentation needs
  a separate live roster decision that tolerates a pending mode change. Final
  selection, roster freezing and start stay strict. No open P0-P2 findings.

Standalone client checks substitute host dependencies; they do not execute
Harmony, Unity rendering, physics or an actual raid. Negative controls mutate
ignored copies only; test projects have no deployment targets. Evidence is in
ignored `artifacts/customs-checks` and `artifacts/area-checks` in the isolated
development clone. Initial fixture compilation/JSON-converter issues and a
Windows PowerShell error-output wrapping issue were corrected before the passing
final run; no tests were skipped to obtain the pass.

Final Release/local deployment identities:

| DLL | SHA256 |
| --- | --- |
| ArenaMode.Client | `FFF0CFBF64BA483BC8ACC89374A669D7D21C060428B67BCA663CF6E64815CA69` |
| ArenaMode.BigBrainCompat | `0BDFF0277811A9AE57393569C315A577C592E3EA0E71593A19486B2ECB91AF57` |
| ArenaMode.Server | `E359258EA507E4A05E44FB0382AE7CECC18620B014280115B386A967AD3DEE12` |
| Redeploy.Core | `47505BB1687E9565F79C54894C5AA16C23BAED08247D9D51609C35E5CC788F94` |

These identify the installation on D:\SPT41. The read-only local Syncthing query
at delivery reported **SPT-GPU disconnected, not paused**. The subsequent user
playtest confirms the Customs preset ran on the GPU host; it does not establish
current synchronization status or a fresh match of remote hashes. No second
transfer path or remote deployment was started.

## User playtest and remaining acceptance

User follow-up on 2026-09-07 after delivery of `a690ce2`: Customs also works,
overall without problems; the boundaries should probably be refined manually
later. This is a positive practical test of the Customs prototype. It is the
user's report, not an agent raid, log inspection or new automated test run.

Boundary placement remains provisional: refine the final contour in the actual
scene around walls, doorways and stairs rather than treating the current box as
a finished layout. No particular replacement coordinates are established by the
report. Any later geometry change needs a new preset version and renewed
spawn-capacity/connectivity checks; do not weaken the existing safety checks.

The report does not specify match count/duration or confirm every scenario
individually. Detailed edge/vault coverage, repeated respawns, Escape/give-up,
re-entry, subsequent full-map/ordinary raids and F12/native-menu transitions
remain explicitly unconfirmed. The combined acceptance checklist therefore
remains partly satisfied rather than being marked fully passed.

Retained checklist (basic operation is now user-confirmed):

1. Select the new preset and independent TDM on Customs. Expect a visible grid,
   `Arena area READY: customs-factory-center-v1` and 2v2. If preparation stops,
   retain the `Arena area stopped:` reason and surrounding log lines.
2. Inspect reachable edges, corners and especially the northern building cut.
   Sprint/jump/vault at barriers; check stairs and lower floors. No body should
   cross or become trapped in a disconnected room. A safely rejected candidate
   should be adjusted and versioned, never bypass its readiness checks.
3. Fight and respawn several times, including near a boundary; watch both teams
   move, use cover and refill. Note long stalls or repeated rejected paths.
4. Verify Escape/give-up during preparation and combat. Re-enter bounded Customs,
   then Factory South, full-map TDM and an ordinary PMC/Scav raid. Boundaries,
   input/navigation restrictions and profile effects must not leak afterward.
5. In the map menu, change TDM to another mode and change the F12 area away from
   Customs. Its tile/selection must disappear without duplicate views. Returning
   to the matching preset/TDM must make it selectable again. Normal PMC/Scav
   map selection must remain unchanged.

No GPU raid was performed by the agent. The Factory and Customs user reports
are separate evidence and do not imply that all teardown cases passed. This
acceptance update changes documentation only; geometry, code, DLLs and settings
remain unchanged.
