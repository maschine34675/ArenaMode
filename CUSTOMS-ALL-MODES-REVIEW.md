# Customs: bounded areas in every Arena mode (2026-10-09)

The three surveyed contours are the only Customs choices for Deathmatch, Gun
Game, independent Team Deathmatch, Tactical (solo/PFT), and Zombies (solo/PFT).
Their IDs, vertices, ground anchors and faction-start records are unchanged.
Ordinary PMC Customs still uses its unmodified full map.

## Pass 1: request and menu ownership

`ArenaAreaCatalog.TryResolve` refuses full Customs, including null/blank IDs,
before arm preparation. It admits the three current contours for all five
current modes; legacy/unknown modes, retired contours and wrong maps still fail.
The native menu may propose the first valid area when the old preference is a
whole map or an obsolete ID. Only a successful settings write accepts that
proposal. Final start, server arm/replay and final location validation never
repair an invalid selection.

The existing persisted `TeamDeathmatchArea` key is retained for compatibility,
but now applies to every mode. Only independent TDM has its 2..5 size row.
Tactical owns its existing five opponents; PFT owns exactly four authenticated
companions; Zombies owns its wave roster. Those modes cannot submit a TDM size.
Deathmatch/Gun Game retain the participant stepper and exact owned population
inside a contour. For their taller menu, the stepper is beside its label so the
three area choices end above native READY (synthetic layout bottom 958).
Equator's pre-existing Tactical capacity rejection is deliberately unchanged.

## Pass 2: exact raid and initialization

`ArenaAreaRuntime.Arm` binds the area, native token and frozen mode. Every loading
and active test compares that mode in addition to its existing exact game/world,
native identity and lifecycle tests. No ordinary/unarmed scene acquires filters,
walls, bot destinations or teleports. All modes use the same per-raid server
spawn filtering and the same scene collection filter. Yard still creates its
twenty new records on the clone only, and final validation never invents them.

The initial PMC uses its surveyed faction start. Native placement, all area
enclosures, carving, directed waypoint connectivity and fresh-chain validation
run before match start. Parent integration defers match initialization and kit
handover until readiness, so countdown/wave/deathmatch timers do not consume
time while preparing the contour. The static five-per-sector proof in non-TDM
is a spawn-corpus requirement, not an injected team size.

## Pass 3: movement, populations and handover

Containment now covers every exact living AI body, including native infected,
bosses and protected PFT companions. An outside destination, path or teleport
still fails; widening the roles does not broaden the exact active raid check.
Directed paths must stay inside the polygon; height remains unlimited.

Only independent TDM uses its faction respawn ranking and initial faction bot
reservations. Other directors use their existing planners with the shared
connected-point filter. Player respawn selection in every bounded mode rejects
occupied positions and reserves the selected position before irreversible body
handover. The prepared selector rechecks that same reservation afterward;
common respawn completion releases it. In-place kit replacement retains its
existing position/geometry validation and does not reserve a different marker.

`TryReserveCompanionSpawn` admits only an exact ready protected Tactical/Zombie
area with a frozen PFT roster. It chooses a real, connected, unoccupied native
bot marker nearest the current PMC and leases it before asynchronous creation.
Four pending companions therefore cannot reuse one point. The PFT adapter owns
profile authentication and release callbacks; the area owns geometry,
navigation and occupancy. Companion leases permit mixed stored factions, last
at most 120 seconds and remain tied to the exact token and immutable marker
identity/position. Independent TDM leases remain 30 seconds. No foreign mod or
source spawn record is modified.

## Pass 4: adversarial and reproducible evidence

- `CustomsAllModesChecks`: 392 assertions against linked production catalog,
  menu state and participant state; three compiled negative controls reject a
  whole-Customs bypass, reintroduced TDM-only admission and PFT in wrong modes.
- `AreaTeamRuntimeChecks`: 423 assertions against actual placement/reservation
  methods plus controlled native lifecycle/navigation seams, with 19 compiled
  negative controls. Added non-TDM late-occupancy tests and four-companion
  reservation, mixed-side, displaced-arrival, timeout and next-token cases.
- `ParticipantMenuChecks`: 3,731 assertions, including actual view construction
  and every map/mode/total layout combination. The larger Customs Deathmatch
  panel initially failed this check; compact stepper placement corrected it.
- `AreaTeamMenuChecks`: 29 assertions; three Customs rows plus four TDM sizes,
  retired callback ownership and protected-squad labels remain covered.
- Customs refresh: 4,495 assertions and four compiled faults; Factory Side:
  2,535 assertions and ten negative controls. Both retain survey fixtures,
  native marker corpus, exact contours, separation and Yard donor checks.
  Mode expectations were updated rather than removing their rejection tests.

Raw offline evidence: `artifacts/customs-all-modes-checks/c16301954ba349c09be35d4f822cccb5`,
`tools/AreaTeamRuntimeChecks/artifacts/35f686e1e6aa43b5ac7137ea2381a3b3`,
`artifacts/customs-refresh-checks/1ddf6ddf229e4f09988915012736fa0a` and
`tools/CustomsEastAreaChecks/artifacts/96b5c69aaaea4699a9128417e846e92e`.

The above are offline checks, not proof of native carving, PFT activation or
match completion. Native source-bound GPU evidence and full server-suite results
belong to the parent integration report. Independent review must inspect the
shared route overload and delayed PFT first-spawn/round-rebind seams in addition
to this leaf's changes. Review already caught the old route overload and the
non-TDM player reservation gap; both are corrected in the integrated sources.
