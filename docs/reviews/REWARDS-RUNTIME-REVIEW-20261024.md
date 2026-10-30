# Reward runtime integration: independent review

Review date: 2026-10-24. This reviews the runtime changes following `843eb6f`.
It is a source review, not a claim that every mode was played. Native acceptance
and loaded build identities belong in the separate GPU report.

## Findings and corrections

1. **Unlimited Team Deathmatch give-up rejected valid prior earnings.** The client
   reports a confirmed local give-up as `LocalForfeit`, including unlimited TDM,
   whose reward rule is `KillsOnly`. The validator originally accepted that event
   only under `TeamScore`, rejecting the whole final packet. The correction now
   requires the `teamdeathmatch` mode, the local victim and either of those two
   rules; only `TeamScore` receives the virtual opposing-team point. The reviewer
   re-read the production correction and the new runtime payout/negative validator
   cases. Executing those tests is recorded by the parent verification run.
   Sources: `ArenaRewardCapture.LocalForfeit`,
   `TeamDeathmatchMode.CompleteLocalGiveUp`, `RewardResultValidator.Evaluate`,
   `RewardRuntimeTests.UnlimitedTeamDeathmatchForfeitPreservesEarnedKillsWithoutWinBonus`.

2. **Tactical timer arithmetic could change the frozen reward limit.** Deriving
   duration from `(absolute float deadline - absolute float start)` can differ
   from the original configured duration. For example, at uptime `16384f`,
   `180.001f` seconds becomes `180.001953125f` after that subtraction: rounded
   contract limits differ by one millisecond. The read-only adapter now retains
   the configured duration directly, which removes this false rule-change signal.
   A related expiry endpoint needs the actual native deadline as evidence:
   at uptime `1000f`, `240.1f` seconds can produce `240.099975585938f` elapsed,
   which truncates to 240099 ms against a 240100 ms limit. A legitimately closed
   timeout must not consequently appear unfinished. The correction now sets an
   expiry flag only in the real solo/team timeout branches, resets it at combat
   start and exposes an integer minimum elapsed duration to the collector. The
   collector applies that floor without a float round-trip. The reviewer traced
   both setters, the reset and the capture consumer: early elimination does not
   assert a timeout. Integer default durations do not exhibit these examples.
   Source: `TacticalMode.BeginRewardCombat` / `ReadRewardState` in
   `ArenaRewardModeState.cs`, and `ArenaRewardCapture.Milliseconds`.

3. **Normal solo Tactical cleanup looked like an unexpected combat retirement.**
   `EndRound` originally retired surviving opponents while its phase remained
   `Live`. The new native-death collector therefore correctly recognized those
   deaths as administrative, but incorrectly withheld the whole match for a
   normal round loss or timeout. `EndRound` now changes to `Aftermath` before
   retiring the old squad. The reviewer verified that source order. Unexpected
   administrative retirements during actual live combat still invalidate rewards.
   Sources: `TacticalMode.EndRound`, `ArenaRewardCapture.DeathPatch` / `Observe`.

4. **Startup refusals were missing from the player status.** A fresh runtime
   coordinator could report ready/pending after the startup processor refused an
   unresolved durable guard, because startup only logged that outcome. Startup
   now forwards per-profile quarantine/rejection and storage failure, plus global
   admission refusal, into separate coordinator status fields. `GetStatus`
   reports the refusal after ordinary ready/pending calculation. The reviewer
   traced this path and read the synthetic restarted-coordinator and unsupported
   version tests; they assert the reason remains visible without profile edits.
   This changes diagnostics, not the payout admission decision.
   Sources: `ArenaRewardStartup.OnLoadAsync`,
   `RewardRuntimeCoordinator.ObserveStartupOutcome` / `ObserveStartupUnavailable`
   / `GetStatus`.

## Native idle-profile representation follow-up

The GPU run exposed an additional adapter assumption: a valid SPT 4.1.6 profile
can contain `"inraid": {}`. Independent decompilation of the installed
`SPTarkov.Server.Core.dll` confirms:

- `CreateProfileService.CreateProfile` assigns `InraidData = new Inraid()` before
  its save/load/save sequence. Both `Inraid.Location` and `Inraid.Character` are
  nullable strings with no non-null initializer.
- `JsonUtil` uses `WhenWritingNull`, so this native object serializes as `{}`.
- `InraidSaveLoadRouter.HandleLoadInternal` supplies `none`/`none` only when the
  whole object is null. An existing empty object is retained.
- `InRaidController.AddPlayer` has an empty body; the inspected `InRaidHelper`
  handles inventory/FIR and does not set/reset this legacy profile object. The
  reference source agrees with these installed methods.

The implemented correction was independently re-read in
`RewardStartupFiles.RequireCleanProfile`: it requires an actual `inraid` object
and accepts each known field only if absent, null or the exact string `none`.
Active location/character names, empty strings, whitespace, wrong value types and
a missing/non-object `inraid` remain rejected. It does not rewrite the profile or
treat the legacy object as positive restoration evidence. Exact
snapshot/restore/durable-readback matching and pending run/snapshot guards remain
mandatory. Native test execution is recorded by the parent verification.

## Native server identity follow-up

A subsequent real final-report attempt exposed an overly strict native identity
pattern. Independent decompilation of installed SPT 4.1.6
`LocationLifecycleService` confirms that native `ServerId` is generated as
`$"{request.Location}.{request.PlayerSide} {timeUtil.GetTimeStamp()}"`. Its single
space before the decimal timestamp is part of SPT's identifier. The old reward
pattern rejected it despite matching the exact server-confirmed session.

The correction was independently traced through `RewardResultValidator` and
`RewardRuntimeCoordinator.Begin`. It retains the exact Arena arm-token suffix,
the overall 192-character bound and the 128-character prefix bound. The prefix
allows the existing identifier alphabet with an optional single ASCII space
followed by 1–19 decimal digits. Tabs, newlines, repeated spaces, textual suffixes
and a nested Arena separator remain refused. Begin now applies this same format
check before admitting a run, rather than discovering the mismatch only at its
final report. Request and native-session identities still require exact equality.

The reviewer read the eight positive/negative format cases and the runtime
fixture's change to the actual `factory4_day.Pmc <timestamp>` shape. Their
execution is part of the parent test run, not a separate reviewer test claim.

## Integrity boundaries checked

- Consent is an explicit authenticated menu action. The route requires an idle
  normal profile and uses the existing raid-start gate. It writes reward metadata;
  first enrollment and actual RUB mail remain in the pre-load startup window.
- Begin/capture must match the server's confirmed arm, native-end identity,
  profile and mode. The wire objects carry no amount, profile path, payout
  authority or restoration declaration. Revisions and completed packets cannot
  be changed by a retry.
- The profile observer receives the actual detached snapshot and successful
  native profile-store swap. A separate bounded disk read compares the complete
  restored JSON document. Publication runs before the exact native session is
  released. Exceptions in reward observers cannot cancel profile restoration.
- Durable snapshot and run guards cover the gap before the first client report.
  A missing RAM snapshot is not recovery evidence. Failed starts can retire their
  guard only after the positive restore/read-back path and receive no reward.
- The helper copies one enabled delegation into the private runtime and returns
  only immutable signed result metadata. It verifies the original installation,
  profile generation and delegation, refuses linked paths, verifies destination
  bytes before deleting the source, and retains conflicting/undrained results.
  Transfer requires positive absence of the private server writer.
- Startup authenticates each source with normal-server-owned delegation data.
  New allocations bind current clean ordinary progress; existing allocations
  keep their original revision. The original decision, journal, profile ledger
  and confirmation-only recovery checks remain in the settlement path.
- Replay after mail collection does not create another payment: completion is
  backed by permanent ledger/journal records, not the current presence of mail.
  Lost history is refused rather than recreated as an empty generation.
- Ordinary PMC/Scav raids are not admitted to reward capture or profile rollback
  by these routes. The changes add observers around existing Arena protection;
  the only new normal-profile writes are explicit startup enrollment and the
  startup mail/ledger transaction.

## Diagnostic and native limits

All four findings above were corrected and independently re-read in the working
tree. Build/test execution and loaded native code identity remain separate
verification evidence; this review does not substitute for those checks.

SPT has no independent server combat simulation. Roster and fatal-event data are
client observations; this is consistency validation and profile-safe payment,
not anti-cheat against the machine owner. Arbitrary external profile writers and
joint rollback of every authority/profile file remain outside the cooperative
writer contract. The existing exact SPT startup-version admission is preserved.

This reviewer did not run builds, mutate installations or operate a GPU host.
Native callback order, live UI consent, private-session restart/return, actual
mail display and every mode's rare ending transitions require the separately
recorded native checks. No claim of blanket PFT or all-mode acceptance is made.
