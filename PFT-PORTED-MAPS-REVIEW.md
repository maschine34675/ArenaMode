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

## Four passes and evidence

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
