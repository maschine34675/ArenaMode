# Reward startup order: position instead of priority range, named refusals, the reason in the session lease

Date: 2026-11-05. Branch `fix/reward-startup-order` from `master` `65007e74` (0.4.1 plus the
unreleased Iceberg and map-screen work). Trigger:
[maschine34675/ArenaMode#1](https://github.com/maschine34675/ArenaMode/issues/1), reported by
iRoxor against 0.4.1 with BetterFlea 2.5.0 (also 2.4.1): no profile ever enrolled, every
normal start logged `Startup rewards unavailable (earlier-startup-callback)`, every Arena
session ran with `"authority": null`, and the Arena session's reward card said "Activation
pending — return to the normal session, then re-enter Arena" forever. The issue's diagnosis
and its three suggestions were verified against the installed SPT 4.1.6 assemblies and
adopted together.

## What SPT 4.1.6 does (verified in the installed IL, not the reference source)

Decompiled with ilspycmd 11 from `D:\SPT41\SPT_Runtime` (`SPT.Server.dll`, `SPTarkov.DI.dll`,
`SPTarkov.Server.Core.dll`):

- `ProgramExtensions.RunPreSptLoadCallbacks` resolves `IReadOnlyList<DependencyInjectionContainer>`,
  keeps the containers with `Type == typeof(IOnLoad)` and `0 <= TypePriority < 200000`
  (`OnLoadOrder.GameCallbacks`), and awaits them in that list's order. No further sort. A
  negative priority never runs. `SPTStartupHostedService` runs the ones at `>= 200000` later,
  after the profiles are loaded.
- `DependencyInjectionHandler.InjectAll` builds that list with a LINQ `orderby
  TypePriority`, which is a stable sort, over `_injectedTypeNames.Values`, a dictionary
  filled in registration order. Each type contributes its own container first, then one per
  interface, then one per base type; the `IOnLoad` container is what the runner picks up.
- `ProgramHelpers.RegisterSptServicesAsync` registers, in this order: `SPT.Server`
  (`Program`), `SPTarkov.Reflection` (`PatchManager`), `SPTarkov.Server.Web` (`SPTWeb`), then
  every loaded mod's assemblies, then `SPTarkov.Server.Core` (`SPTStartupHostedService`). The
  loaded mods are sorted by `ModGuid`, case-insensitive (`ModLoader`). So at one priority a
  mod with a GUID before `com.maschine.ArenaMode` runs before Arena's step, one after it
  runs after, and SPT's own `Watermark` (priority 0, in Core) runs after every mod at 0.
- SPT's own pre-load callbacks: `Watermark` 0, `DialogueCallbacks` 1000, `HideoutCallbacks`
  2000, `InsuranceCallbacks` 3000, `BtrDeliveryCallbacks` 4000, `HealthCallbacks` 5000,
  `ImageRouteImporter` 100000. Nothing of SPT between 0 and 1000.

The `Injectable` attribute's constructor takes `(InjectionType, int typePriority = int.MaxValue)`;
`Watermark` is `[Injectable(InjectionType.Transient, int.MaxValue, TypePriority = 0)]`.

## The behaviour from 0.3.0 up to and including 0.4.1

`ArenaRewardStartup` sat at `Watermark + 1` since commit `87997ae2` (first released in
0.3.0 on 2026-10-25). `GetAdmissionFailure` returned
`earlier-startup-callback` for any other `IOnLoad` container with a priority in `0..1`,
before or after Arena's step. `Watermark + 1` is the natural "run before other mods" value.
The refusal switched the whole start's reward step off: no enrollment, no recording, no
payment, with one Warning that named no callback. The normal session's card said "Rewards
are temporarily unavailable." (`UnavailableDetail`), the private session's card derived
"Activation pending — return to the normal session, then re-enter Arena." from
`Enabled && !Enrolled`, and the lease carried nothing that could have said otherwise.

## The contract now

1. **Priority `OnLoadOrder.Watermark` (0).** The earliest slot SPT runs. A mod at
   `Watermark + 1` always runs after Arena's step. Only a mod that also chooses 0 and whose
   GUID sorts before `com.maschine.ArenaMode` can precede it.
2. **Position, not priority range.** `ArenaRewardStartup.PreLoadCallbacks` mirrors SPT's
   filter (`IOnLoad`, `0 <= priority < GameCallbacks`) in list order;
   `EarlierCallbacks` returns the entries before Arena's own, without `Watermark`.
   `GetAdmissionFailure` returns `startup-callback-unlisted` when Arena's own entry is
   missing from the list (the order is unknown), `earlier-startup-callback` when
   `EarlierCallbacks` is non-empty, and admits a callback at the same priority registered
   later, at a later priority, with a negative priority or under another contract. The
   refusal stays: it is the documented regular native startup boundary
   ([REWARDS-SERVER.md](../REWARDS-SERVER.md), "Admission and concurrency"), not a blanket
   foreign-mod block; after change 1 it can only hit a mod that really runs first.
3. **Named and recurring.** The Warning reads
   `Startup rewards unavailable (earlier-startup-callback: <Type.FullName> in <Assembly>.dll (priority N), …); profiles will load normally.`
   followed, for every refusal that recurs while the installation stays as it is, by one
   sentence saying so. `RewardUnavailableText` classifies the reason codes:
   `earlier-startup-callback`, `startup-callback-unlisted` → "Rewards are off: another
   mod's startup step runs before Arena's reward step. See the server log.";
   `unreviewed-native-startup-version`, `not-native-server-startup`, `runtime-root-mismatch`
   → "Rewards are off: this SPT server version or start is not reviewed for rewards. …";
   `native-profiles-already-loaded` → "Rewards are off: another mod loads the profiles
   before Arena's reward step. …"; the three `reward-storage-*` codes → the existing
   reward-folder sentence; `another-server-or-unknown-process` and an unexpected exception
   type name stay "Rewards are temporarily unavailable." The normal session's
   `UnavailableDetail` uses it (a profile's own `authority-lost-*` failure comes first,
   then the reward-folder flag, then the start's reason).
4. **The reason next to the lease.** For a lease without authority the normal server
   writes `user/arena-rewards/offline-sessions/<sessionId>/authority-unavailable.json`
   (`RewardOfflineAuthorityUnavailable`: schema 1, the session id, the reason) right after
   `lease.json`: `entry:<code>` when the entry's own read of the profile's reward records
   failed (wipe, moved install, damaged enrollment: that names what is wrong with THIS
   profile and comes first), else `startup:<code>` when the normal server's start recorded
   a refusal (`RewardRuntimeCoordinator.StartupUnavailable`, passed by
   `ArenaRewardReturnListener` into `PreflightReady` and `CreateReady`), else no file (a
   profile that never enrolled under an admitted start: the genuine "activation pending",
   rewards switched on inside a session are enrolled at the next normal start). The lease
   itself keeps its 17 members, bytes and hash: the first draft put the reason into the
   lease as an optional member, and the first native entry on GPU-2 paused in the session
   helper, which parses the lease with a fixed member count
   (`HelperOfflineSession.ParseLease`, `Parse(bytes, 17)`) and showed "Arena is waiting
   for SPT to finish saving"; a file of its own keeps every helper release working. The
   code is cut to `[A-Za-z0-9._-]`, at most 120 characters, before it is written
   (`RewardUnavailableText.LeaseCode`: the leading run of such characters, `unknown` when
   there is none), and the reader (`RewardOfflineSession.ReadAuthorityUnavailable`, from
   the original root's session directory, where the private server reads the lease and the
   active pointer anyway) accepts only schema 1, this session's id and that shape, never
   throws, and treats anything else as no reason; a file next to an authority changes
   nothing. The private session's `RewardOfflinePrivate.Status` reads it once and turns a
   reason into state `withheld` with the sentence above, where "See the server log."
   becomes "See the normal server's log." (the private server's own log holds nothing
   about it); an `entry:` code is `RecordsMismatch` except `IOException` and
   `UnauthorizedAccessException`, which may be a scanner holding the file for a moment
   after the stop and stay `Temporary`. `CanChange` stays true, and the client's card
   already shows `Detail` for `withheld`; switched off inside the session the card says
   "off", switched on again the reason returns. An identical entry retry (lost reply) finds
   the published lease and record, whether or not the retry still knows the reason. The
   label above the detail still reads "PROFILE REWARDS: ON" (the setting is on, payment is
   off), as the normal session's withheld card has always shown it.

Not changed: the client, the whitelist, the enrollment, the payment path, the lease hash
of a lease without a reason. Known gap, documented in REWARDS-RUNTIME.md: a healthy
enrollment of an earlier admitted start is still delegated when the current start was
refused, so that session earns and its card says "credited when returning", while the
receipts are paid only by the next start whose reward step runs; for a recurring refusal
that is after the installation changes, and only the normal session's card says so.
Carrying the reason next to an authority was not done: the receipts are not lost, and a
lease with authority must keep its shape. "another-server-or-unknown-process" stays
"temporarily unavailable" although FindConflictingServer also reports it for a server
image it does not recognise or a peer it cannot identify, which recur; the log names the
code either way.

## Verification

- `ArenaMode.Tests`: `RewardStartupTests` (`NativeAdmissionRejectsContractDrift` with the
  `early-callback` case now inserted before the step and a new `unlisted` case;
  `AdmissionFollowsTheRealCallbackOrderNotAPriorityRange`;
  `AnOrderRefusalNamesEveryEarlierCallbackWithItsAssemblyAndPriority`;
  `TheCardSaysWhichRefusalsRecurInsteadOfTemporarily`), `RewardRuntimeTests`
  (`ANormalSessionShowsARecurringStartRefusalAsOffNotTemporary`;
  `UnavailableNativeStartupIsVisibleWithoutEnrollingOrEditingTheProfile` now expects the
  unreviewed-server sentence), `RewardStartupRefusalTests.cs` (partial
  `RewardOfflineSessionTests`: the record and the private status for a refused start, no
  record without a refusal and nothing of it in the lease, records mismatch first, healthy
  authority despite a refusal, a record that is not this session's ignored, the code's
  shape, the scoping).
- Native run on GPU-2, first attempt (2026-11-05 20:55, build `c5618dfe` with the reason
  as a lease member): the server start with the probe at `Watermark + 1` ran the reward
  step and the probe after it, as intended, but the Arena entry paused in the session
  helper ("Arena is waiting for SPT to finish saving"): `HelperOfflineSession.ParseLease`
  refuses a lease with more than 17 members. The reason moved into its own file (change
  4); the review had not read the helper's parser.
- `tools/OfflineStartupChecks`: Focused stage runs these classes; `Mutations.ps1` gained
  `callback-position-ignored` (every pre-load callback counts as earlier: the 0.4.1 bug) and
  `lease-reason-dropped` (the lease forgets the reason), each rejected by its named test.
- `tools/RewardMenuStateChecks`: a private "withheld" status with the earlier-callback
  sentence is shown as that sentence, not as "Activation pending", with the switch kept.
  This pins the client's existing `withheld` branch (unchanged here), not the server's
  status; the server side is pinned by `RewardStartupRefusalTests.cs`.
- Adversarial review (2026-11-05, four lenses, three refuters per finding, 49 agents):
  found the wrapped-reason lease invalidity above (fixed), the missing attribute test
  and mutant for the priority itself (added: `TheRewardStepRunsAtTheEarliestSlot`,
  `priority-back-at-watermark-plus-one`, plus `pre-load-upper-bound-inclusive` and
  `unlisted-step-admitted`), the entry-side file errors shown as a mismatch (fixed), the
  release that introduced the check (0.3.0, not 0.4.0; corrected everywhere), the
  circular report references (fixed) and the two documented gaps above. Not covered by
  a test: `ArenaRewardReturnListener` passing `RewardRuntimeCoordinator.StartupUnavailable`
  into `PreflightReady`/`CreateReady` (an HTTP listener with a host lifetime; the native
  run exercises the same call for the `entry:` reason).
- Native run on GPU-2: planned as
  [GPU-REWARD-STARTUP-ORDER-20261105.md](../reports/2026-11/GPU-REWARD-STARTUP-ORDER-20261105.md),
  outstanding until that file exists.

## Replying to the issue

Suggested, for the user to post: the three suggestions were adopted as proposed, with the
order read from the same list SPT runs, the Warning naming type, assembly and priority, the
"Rewards are off" sentence in both sessions and the reason carried in the lease; thanks for
the IL analysis, which matched the installed assemblies. Ships with the next release.
