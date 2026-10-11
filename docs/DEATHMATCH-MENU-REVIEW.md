# Unified Deathmatch menu and lifecycle integration (2026-09-12)

Implementation review; GPU acceptance remains pending. Related evidence and
boundaries: DEATHMATCH-MODE-REVIEW.md, DEATHMATCH-HOSTILITY-REVIEW.md,
DEATHMATCH-GROUPS-REVIEW.md and ARENA-PREVIEW-REVIEW.md.

## User-confirmed rules

The native menu has four entries: Deathmatch, Gun Game, Tactical and Team DM.
Deathmatch replaces the separate Endless/Rounds entries. Round count is 1, 3, 5,
10 or Endless in the menu; custom 1-100 values remain possible through F12.
Per-round kills are Off/10/20/50/100, time Off/2/5/10/20 minutes, opponents FFA or
All vs player. Custom F12 limits are displayed exactly without guessed markers.
Both limits off means continuous play and requires endless rounds. Finite round
buttons stay unavailable until a kill/time limit is selected. Invalid F12
combinations are rejected before a server snapshot, with an actionable error.

FFA counts each original profile ID independently, including bot-vs-bot kills.
At a time limit the highest individual kill count wins; ties draw. A finite
series compares individual round wins. Newly generated bots are new contestants.
All vs player uses the local kill target; missing it by the deadline loses that
round. A time-only all-vs-player series completes neutrally. Intermissions remain
scoring brackets in the same raid, with the proven kit/protection path, rather
than map reloads or population resets.

Legacy mode ids still parse, but are saved as deathmatch. One-time migration
copies selected Rounds' kill/time limits; selected Endless stays continuous.
The new opponent selector defaults to FFA and overrides the server default only
for canonical Deathmatch. Legacy aliases sent by older clients, Gun Game,
Tactical and Team DM retain their prior server policies.

## Scope and invariants

All options are frozen after settings sync, before /raid-start; the server must
acknowledge exactly the same hostility string and arm token. Missing/mismatched
acknowledgements retain uncertain-arm cancellation rather than allowing a
misconfigured raid. Per-arm session storage and exact cloned-location identity
keep global bot settings and ordinary raids unchanged. The first round waits
for the initial kit swap and exact local body before its clock starts.

Scoring admission runs before scoreboard/awards mutate; the final local casualty
is counted once before the winner freezes. Terminal roster updates and respawns
remain held, native damage blocked for the exact result body, and native exit
waits for a valid owner and settled replacement pipeline. A shared terminal
facade preserves the existing independent Team DM path. Profile snapshots and
the exact native end bypass are unchanged.

Tactical shows actual solo 1vN (default 1v5), or 5v5 (PFT) with four accepted
companions; a visible note names PitFireTeam's requirement. No new simulated
PFT roster or team-size switch was introduced.

Menu panels share their footprint with the play-area panel: Deathmatch ends at
reference Y=722 and area starts at734, all other modes area starts at668.
Changing modes with unchanged area rows still updates placement. Mode and rule
buttons reject callbacks from previous or hidden views and active/armed raids.

## Independent findings corrected

- Initial kit preload could outlive a short result: round one now waits for
  actual readiness; initial swap has a terminal cancellation predicate.
- Native FFA role grouping and stale groups: exact-spawner independent group
  lifetime and reciprocal active opponent admission are separately reviewed
  and tested in DEATHMATCH-GROUPS-REVIEW.md.
- Area panel overlapped the fourth Deathmatch rule row: one shared height policy
  plus mode-sensitive area layout resolves it.
- A retained old mode-row delegate could change a new panel: exact root/button
  ownership guard, with a failing defect control, resolves it.

## Verification

Run tools/DeathmatchMenuChecks/Verify.ps1. It executes the actual selection,
config/view, transport/native-start, give-up/scoreboard, area view and native
result-exit sources against scoped external I/O doubles, plus compiled deliberate
fault controls. It does not emulate EFT rendering or AI. The runtime, preview,
server hostility and group test scripts are independent checks.

Run tools/DeathmatchMenuChecks/Verify-Build.ps1 for both actual projects using
installed references with isolated output/deployment paths. Installed client,
server, Redeploy, Arena assets and configs must remain unchanged until explicit
DLL delivery. No audio or dependency copy is part of delivery.

Native AI acceptance, ordinary PMC return, repeated navigation and downstream
model continuity still require the focused GPU test instruction delivered with
exact client/server SHA256 identities. Never transfer r6 acceptance to this DLL.

## GPU r1 follow-up (2026-09-12)

Report `05e88d0` stopped at the stale side-selection description "Five modes".
The native screenshot and all four committed image hashes agree with the report.
This was a cosmetic failure under the overly broad test stop rule, not a match
crash: no map-selection tests or raids ran. The replacement copy is count-free,
"Solo and team arena combat.", with the existing second profile-safety line;
PitFireTeam's two-line description is unchanged. No new literal-only test was
added for this low-impact text correction; independent source/image review and
the actual client build cover it, with native text confirmation in GPU r2.

The user separately confirmed that the character no longer jitters. Preserve
this as user-observed acceptance of the visible improvement. r1 itself has no
video or consecutive-frame evidence and does not certify model continuity on
subsequent screens, nor the exact numerical cause. No further preview changes
are justified by this report. r2 prioritizes the untested menu and raid cases;
cosmetic findings are recorded without aborting independent cases. Recording is
attempted only if jitter recurs and tooling is available; otherwise that subcase
stays open. Safety, exceptions and build drift still stop
the relevant run. Original r1 instructions/results remain historical evidence.

### Canonical Deathmatch laser brightness follow-up

The bounded audit of old mode-id consumers found one functional omission:
ArenaTeamLaserColors.TryOwner admitted only the old solo ids, so canonical
Deathmatch silently skipped both point/intensity and beam-only boosts. Add the
exact shared DeathmatchSelection.ModeId; do not use NormalizeId here because it
also maps unknown ids to Deathmatch. All native raid/body/weapon/AI ownership
checks and the intentional absence of solo team tint remain unchanged.

The production-linked replay now covers canonical Deathmatch and four retained
solo ids for both local and active AI bodies, point and beam-only brightness,
unchanged native dot/intensity/tint, stale bodies and unknown-mode refusal. It
failed the exact new Deathmatch assertion before the fix and passes 1293
assertions afterward. The existing regression runner now rejects 14 compiled
faults including removal of canonical admission, then reruns the passing actual
source. Independent review found no remaining established defect. This is an
offline rendering-contract check, not measured native laser visibility; r2 may
check it opportunistically without requiring a kit modification or extra raid.
