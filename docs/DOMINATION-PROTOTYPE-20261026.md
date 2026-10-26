# Domination prototype — 2026-10-26

Status: implemented and deployed as a development prototype; native acceptance
and its limits are recorded in [the GPU report](GPU-DOMINATION-20261026.md).
The same-day [feedback follow-up](DOMINATION-FOLLOWUP-20261026.md) supersedes
the initial presentation behavior and deployed client identities below.
This replaces the unimplemented
September single-control-point proposal in `CONTROL-POINT-REVIEW.md`.

## Playable contract

`DOMINATION (WIP)` uses independent USEC/BEAR teams, the existing side and even
participant selectors, Arena kits and continuous respawns. V1 allows full Factory
day/night only. PitFireTeam squads, Customs areas and ported maps are excluded.
BigBrain 1.5.0 or newer and the matching Arena companion must successfully register
the objective layer; otherwise only this mode is unavailable. The current
forward-version API checks remain in force; this is not a global new dependency.

Three points A/B/C take five seconds to capture. An enemy point first needs five
seconds to neutralize, then five to capture. Additional bodies do not accelerate
capture. Both factions inside freeze progress and income; hostile occupancy alone
stops the owner's income immediately. Empty points keep their owner/progress.
Each owned, unopposed point pays one point per second. The first team to 200 wins;
after ten simulation minutes the higher score wins, with an equal score a draw.
Limits are frozen per match. A simultaneous threshold is evaluated symmetrically.

Kills, deaths, assists and personal combat points stay on the existing scoreboard.
They cannot win the objective match. Objective lead/time announcements work;
"kills remaining" cues are excluded. V1 does not submit reward receipts: the
existing server reward proofs are kill-based. The map menu explicitly displays
that limitation, without changing the profile's reward preference. Full profile
restoration remains mandatory and unchanged.

## Geometry and presentation

Initial reviewed Factory seeds are A (-5.89, 0.08, -27.46), B (-6.77, 0.17, 10.01),
C (-20.756, 0.166, 22.769). These are prototype placements, not a claim of final
balance. The live scene must prove floor, clearance and both travel directions
against Factory's main navigation network before the match can start. Bounded
nearby placement repair follows the existing pickup survey rules.

The volume is a four-metre horizontal radius and a one-metre foot-height tolerance.
A line from centre +0.8m to the actor +0.8m must be unobstructed, preventing capture
through walls or floors. Only the exact living current local body and owned active
bot wrappers count. Ground rings and labels use normal scene depth; the HUD shows
A/B/C ownership, progress and contesting in faction colours. World labels show
only the point letter. The ground ring explicitly uses neutral grey, USEC blue,
BEAR red or contested yellow. The pickup effect/notice row sits below the objective
row with a shared layout calculation, including live score-panel offsets.

Pickups retain their tall beacons. Initial, repaired and rotated floors, including
both sides of a position exchange, must stay more than six horizontal metres from
each point when their height difference is at most two metres. This reserves the
capture volume plus clearance; independent floors remain usable. Built-in
Domination Factory layouts supplement the eight authored seeds with bounded,
filtered native bot-spawn candidates. The original seeds still determine the
navigation anchor. Custom layouts are not expanded or rewritten, but must pass
the same spacing check. See the follow-up for native placement evidence.
Native stop and match end dispose all objective presentation and authority.

## Bot authority

The existing independent-team roster and respawn protections are reused as a
constructor variant of `TeamDeathmatchMode`; the original kill-shaped match policy
is unchanged. `DominationPolicy` and `DominationMatchPolicy` own capture and terminal
decisions separately. Long unsampled intervals over 30 seconds award no guessed
occupancy income; the match deadline still applies.

The coordinate objective bridge assigns bodies across points, retains a valid
assignment for at least eight seconds and plans at most once per second. Bots get
distinct destinations inside their assigned point and hold after arrival. Enemy
shooting visibility, incoming fire, immediate hazards, medical need and empty
magazines yield to native/SAIN actions. Movement uses the existing SAIN handoff,
real complete paths, bounded retries and exact owned-route cleanup, never teleports.
The Domination layer has priority 75, below Arena fire escape 79. PFT's separate
post-death pursuit authority is not widened. TDM's omniscient nearest-enemy search
is skipped only for Domination; faction friendship and ordinary perception remain.

## Verification

The seven-stage production-linked verification passed: 3,213 unit tests (one
known APBS-version-specific skip), objective policy, admission, bot movement,
original TDM lifecycle, give-up and BigBrain compatibility. Objective policy
checks run 15,808 assertions plus 25 rejected source mutations; the objective bot
checks run 86 assertions plus 12 rejected mutations. Original TDM runtime runs
439 assertions, including 29 new Domination lifecycle cases. The real client,
companion and server were built with deployment disabled before explicit install.

Independent source reviews found and resolved the exact-deadline sampling gap
and a stale BigBrain action on objective reassignment. Menu, participant, session,
reward exclusion and announcement regressions are documented in the local
`regression-report.md`; native rendering and navigation are separate evidence.
The final pickup-view correction passed 40 assertions and seven rejected source
mutations against the real view with only Unity/mode-query boundaries doubled.

The first native Factory-day match reached BEAR 200 : USEC 107 through objective
income, with an independently different kill count. It exercised bot captures,
recaptures, contested points, local death/respawn, terminal flow and cleanup.
The observed UI overlap and disabled reward-button background were corrected
before the final night/control runs. Factory night confirmed the corrections,
rejected capture from the tunnel below B, and captured it from the correct floor.
Original TDM retained kill scoring without objective objects; an ordinary PMC
control had no Arena runtime/HUD and extracted through the native exit. Night
Domination and TDM inventory comparisons were exact; test settings are restored
and both game/server stopped. See the GPU report for evidence and remaining
limits; placement balance is still provisional.

Initial deployed SHA-256 identities (historical; superseded by the feedback
follow-up on 2026-10-26; no public version bump):

| Assembly | SHA-256 |
|---|---|
| Client | `612025302F213FFB37E8FCDE04FA1856A1C872E4C44DA3ECB02157F27A99DA4E` |
| BigBrain companion | `537E1C0B2C25A2ACC1362F869E9B3BE70C01E106D2E1F857119B29F72D6A7FDC` |
| Server | `12127A1B6F61801F939E26EBAE37D39149772B00AE3FE62EA45532349D9C7A8F` |

Raw local evidence belongs under `artifacts/domination-20261026/`; it is not a
substitute for the observed game run and may contain private test-profile data.
