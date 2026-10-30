# Protected PFT startup on ported maps and bounded Customs

2026-10-09. This change remains inside ArenaMode; it does not replace or copy
PitFireTeam or ArenaMapPort code. Installed PFT 0.10.3 SHA256 is
`0F875F824215F68A47FFD9183FB0BDD92CBA72762E1702F458A0EE614CC6DBB9`.

## Observed failure

`LogOutput-MASCHINE11-2026-10-09_17-53-05.log` authenticates four companions for
Tactical Bay5 at 17:54:56.793, requests follower details at 17:55:08.859, and ends
no contest with 0/4 at 17:55:58.789. No physical follower activation is observed.
This log contains Bay5 Tactical, not separate evidence of Equator/Zombie failure.

The installed PFT DLL decompilation, under
`artifacts/zombie-voice-pft-20261009/sain-analysis/pft-installed`, shows its
`BotsControllerPatch.GetClosestCorePoint` obtaining a core through
`CoversData.GetClosest(position).CorePointInGame`. EFT `AICoversData.GetClosest`
returns null for an empty cover list. ArenaMapPort's `AICorePointRepairs.cs`
explicitly describes no baked GroupPoints on its ports, while creating the real
core-point holder. PFT therefore faults after profile preparation, before its
activation loop. Its initial `Task.WhenAll(...).ContinueWith` only clears a cache
and does not surface the antecedent exception, explaining the uninformative
timeout. This source-supported diagnosis still requires native confirmation.

## Ported-map repair

The existing exact-version adapter installs one narrow Harmony prefix on PFT's
core-point resolver. Only a protected tactical/zombie roster, the exact current
LocalGame/GameWorld/controller/local PMC, and Bay5 or Equator qualify. Loading
requires the acknowledged same-map arm and native Running status; active rounds
require the exact confirmed raid and Started status. Finalization, other modes,
ordinary raids and a nonempty cover list retain native PFT behavior.

When the cover list is empty, the adapter returns the nearest finite existing
core whose ID resolves to that exact object in the same holder. It authors no
cover, point, path or spawn position and does not loosen map admission. A missing
or malformed holder still fails normally. In particular, Equator Tactical's
separate insufficient-opponent-start limitation is unchanged by this repair.

## Bounded Customs integration

PFT's first `SpawnFollowers()` is a void loading event, not an awaited dependency
of map loading. Defer that native dispatch until enclosure/navigation readiness,
retaining the exact game, world, controller, leader, PFT boss wrapper and arm
epoch. Repeated loading events coalesce; reset, boss replacement, another world
or finalization discard the pending dispatch. The native `squadSpawned` guard
continues owning duplicate native dispatch prevention.

For initial canonical companions and the existing lifecycle's exactly leased
runtime Profile objects, PFT's `ActivateBotAtPosition` receives distinct real
connected markers from the area runtime, with a 120-second placement reservation.
The existing runtime activation prefix runs first; an earlier Harmony rejection
cannot obtain a placement. Authentication remains in the PFT adapter. Physical
arrival must retain the exact body Profile, world, leader and placement lease
before its native callback can publish/adopt it. Native task completion alone
never releases the reservation because EFT detaches the actual CreateBot work.

Rejected initial calls settle only their captured spawner charge. Rejected
runtime calls reuse the lifecycle's existing settlement and retirement, avoiding
double retirement. The callback compares the native counter before/after success
or exception, and fails closed if PFT did not settle it. Exact rejected bodies use
the existing unpublished-body cleanup; unrelated Profile objects are untouched.
An initial-profile weak ledger also guards `BotCreatorClient.CreateBot` after
EFT's `Task.Yield`, before it reads the global GameWorld argument. The native
LocalGame creator uses its own GameWorld rather than that argument; the guard
prevents deferred creation into the ended/disposed old game. It blocks stale
creation even after reset and ignores another creator; existing runtime raw-player leases
remain authoritative. No cancellation token is changed by this adapter.

The native 91db119 Customs retest exposed one additional ordering seam: the
per-frame PFT discovery runs before area readiness and deferred match creation.
At 19:06:03.823 it latched a missing leader before the mode could bind him;
area READY followed at .922, match creation at 19:06:04.014 and all four real
follower activations at 19:06:04.561--19:06:05.548. The old latched error then
ended the otherwise assembled team. Initial roster discovery now defers only
while an exact current bounded area exists, no match object exists, and no
leader reference, leader profile ID or follower body has ever been bound.
The guard lives after the existing error/version/live checks: it never clears
a latched error or bypasses authentication. Once the mode starts or any binding
exists, the original physical roster checks remain mandatory. ReferenceEquals
distinguishes an initially absent leader from a destroyed Unity leader object.
The source-linked PFT harness tests those boundaries, including a ready area
before match creation and destroyed/lost previously bound leaders.

### Consecutive protected raids

The e55d6a7 native retest reached a live 5v5 on Factory Center with all four
companions and five opponents, then returned through protected Stop(Left).
The next raid in the same process (Yard/Zombies) reached area READY with eight
player and 22 bot markers but rejected all four companions at 19:22:22. The
old message incorrectly called every admission rejection a missing marker.
The real repeated-raid defect is the process-wide initial-profile ID set:
protected end retains finalization state, and the next successful roster freeze
reset only the round lifecycle, leaving those IDs already admitted. That guard
runs before the area reservation API. The profile-only weak raw-creation table
could likewise reject a reused Profile object from a different raid.

Both ledgers now belong to an exact BotCreatorClient weak key. Native
`LocalGame.vmethod_1` constructs a new creator per raid (`LocalGame.cs:171`);
the same canonical companion ID or Profile can therefore start in a later raid,
while duplicates on the current creator remain blocked. Old-creator tombstones
survive Reset so a suspended native raw creation cannot enter an ended game.
Successful roster reauthentication resets pending area placements immediately
before the existing round reset, outside the compatibility state lock. Failed
reauthentication and CancelFinalizingAfterFailedStop are unchanged. Each
admission failure now reports its actual guard separately from missing markers.

The production-linked harness covers eight combinations of explicit/no reset,
new/reused Profile, and pending/completed old activation. It verifies the new
creator's successful reservation/callback, old creator's raw veto and isolated
charge, same-creator duplicate IDs even through another Profile object, retained
duplicate protection after reset, and late old callbacks receiving either the
old world or an unrelated new-world body. No native spawn collection, contour,
zone, category, occupancy or path filter was loosened for this correction.

Correction review completed four passes: native lifecycle evidence; exact-key
implementation and pending cleanup; stale/duplicate/counter adversarial cases;
independent source review and compiled negative controls. The final linked
harness has 234 assertions (109 bounded-hook), and all 24 deliberate compiled
faults are rejected. Replacing only the adapter source with e55d6a7 compiles but
fails the new same-companion/new-creator regression, proving the check detects
the observed defect. Evidence lives under
`artifacts/pft-ported-customs-20261009/repeated-raid-faults` and
`repeated-raid-baseline.log`. Native acceptance of the corrected second raid
remains separate from these offline checks.

## Four passes and evidence

### Failed-start integration follow-up (native 54d9f7e candidate)

Customs Factory Center Tactical exposed a distinct startup failure after PFT
changed the requested initial spawn category to Coop. That correction belongs to
the area runtime. Its fail-closed path then called native Stop synchronously from
OnGameStarted; inspection found the exact game already Stopping but its results
transition hung, while the cleared area and missing match allowed generic burst
requests. Deferring Stop is a mitigation for this observed callback ordering,
not a claim that the native fade implementation itself has been proven faulty.

`ArenaMatchService.Abort.cs` keeps the existing service partial and binds the
abort to the exact started game/world and its canonical LocalGame profile. The
schedule immediately latches damage/respawn/PFT guards and invalidates detached
burst requests; it never stops the game on that callback stack. The controller
can attempt native Stop only on a later frame. An already Stopping/Stopped game
is accepted once; a no-op or exception before that state retries at most once
per second only while Started. Logs are paced; completion is never inferred
from a normal Stop return or from unrelated statuses. A synchronous GameEnd
cannot re-latch the cleared abort. A different game/world discards it rather
than applying old guards or stopping the new raid.

The pending-or-committed latch suppresses generic/solo burst Tick, new/detached
burst leases and optional kit warmup even after the area is reset and Current
is null. Existing raid-end and new-raid recovery clear the latch. This also
covers the exact server-rejected start before IsActive becomes true, without
authorizing ordinary raid exits from LastRaidWasArena alone.

Managed Deathmatch/Gun Game has its own population generation, deliberately
unaffected by ambient Burst.Reset during player respawns. Its IsLeaseCurrent
therefore checks the abort latch directly as well. The solo runtime harness
verifies both modes with delayed profile preparation and detached native arrival
after an abort, while that generation remains unchanged: no new native dispatch,
no published late body and no replacement top-up. The `failed-start-lease`
negative control removes exactly that independent guard.

`tools/FailedStartAbortChecks` compiles that production partial against native
boundary doubles and exercises same-frame deferral, duplicate scheduling,
canonical/missing/changed profile identity, no-op and throwing Stop, committed
teardown, paced retries/logs, synchronous end, ordinary/stale worlds and the
integration gates. Its mutation runner deliberately removes these protections.
Native results-screen acceptance of a forced failure remains separate from
these offline state-machine checks and the normal Customs startup retest.

1. Installed-contract pass: followed native LocalGame loading through PFT
   SpawnFollowers, profile preparation, core resolution and detached activation.
2. Implementation pass: minimum port fallback; separate bounded startup and
   reservation adapter, with ordinary/native paths preserved.
3. Negative-control pass: source-linked harness exercises zero/nonzero covers,
   maps, mode/version/scope mismatches, destroyed/invalid/duplicate cores,
   deferred readiness, stale epochs/worlds/bosses, missing markers, early task
   settlement, callback failure before/after decrement, reset and late arrival.
4. Adversarial pass: callback counter and duplicate runtime retirement findings
   fixed; initial raw-creation continuation guarded after native yield. Parent
   independent review and GPU proof are recorded in the delivery report.

`dotnet run --project tools/PftPortedMapChecks -c Release` executes both production
adapter files with native-shaped doubles. `Verify.ps1` compiles deliberate faulty
versions and requires behavioral rejection. These are lifecycle/contract checks,
not proof of navigation, native PFT playback or physical in-game acceptance.

The parent integrates `PitFireTeamAreaSpawns.Tick` after the area tick and reset
on plugin shutdown. The normal compatibility clear already resets this adapter
before resetting the round lifecycle. Builds, deployment identity, native map
smokes and full profile/PFT-store preservation belong to the parent delivery.
