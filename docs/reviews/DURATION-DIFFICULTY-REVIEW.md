# Match-duration difficulty — 2026-09-09

Finite **independent Team Deathmatch** now uses its frozen match duration and
released playing clock for both existing time-dependent difficulty curves:

`time progress = clamp(playing seconds / (0.8 * time limit), 0, 1)`

Both curves reach their configured peak at 80% so the final fifth can contain
new peak-tier opponents. Assembly, loading and countdown do not advance the
curve. A 5-minute match reaches full time pressure/equipment at 4 minutes; a
10-minute match at 8. A kill-limit victory can still end earlier; future kills
cannot be predicted by this time-based curve.

The existing killstreak surge remains additive (default maximum +35% at eight
kills). Difficulty levels are discrete: with no streak, PMCs switch from hard
to impossible at **64% of duration**, subject to the existing cap. Full pressure
occurs at 80%. Bot equipment still uses the configured map start/end level and
native random range, not a forced exact equipment set. Existing living bots
are not changed retroactively.

Unlimited TDM uses its live clock for the legacy pressure ramp; its equipment
retains the legacy server raid clock (including setup). Other modes retain
their previous curves. PFT Tactical still uses native player-relative equipment.
Respawn, search, treatment, rounds and other gameplay timers are not difficulty
curves and were not rescaled. Disabling pressure or map tier scaling retains
the corresponding opt-out.

## Native request and cache boundary

The game backend/session can outlive one raid. `ArenaBotDifficultyClockPatch`
therefore captures an exact current provider/creator/game/world and arm token
inside `BotProfileClient.CreateProfile`, before its first await. A synchronous
thread-local scope authorizes only that nested final backend request. Postfix
and idempotent finalizer restore the previous scope when the Task is returned;
it never flows to asynchronous continuations. Ineligible nested demands shadow
the outer scope. All three hooks remain inert until their complete installation
succeeds; a partial failure attempts rollback and retains native generation
even if hook removal fails. No timer route, polling, server ambient clock or profile
metadata is added.

The final base-typed condition is cloned as `TimedWave` with `arenaMatchClock`
extension data and a one-profile demand. Role/difficulty are retained; unknown
derived DTOs and invalid/foreign requests retain the native request. A probe
against installed native `JsonExtensions.ToJson`, its converters and
`BotGenerateRequestParams<List<CountTypeBotWave>>` confirmed a derived public
clock property survives serialization. Server `GenerateCondition.ExtensionData`
retains it through `BotController.GenerateBotWaves` and `GenerateBotWave`.
The server validates protocol, finite numbers/duration, positive counts and
exact live session/map/mode/arm before reducing the per-request reserve and
applying the same curve. Missing metadata retains legacy-client behavior;
present invalid metadata leaves native details unchanged. Existing tier logs
now identify `clock=match` or `clock=legacy`, duration and progress without tokens.

Native source verification exposed two otherwise hidden caches:

- A demand miss asks for three profiles; native server PMC batch configuration
  may expand that to fifteen. Remaining profiles would retain early gear.
- `BotProfileClient.CreateProfile` queues backups after misses. Startup also
  preloads types from disabled boss waves regardless of their spawn chance.

Finite TDM skips Arena's six-per-type target prewarm. Each owned demand removes
only matching **unused** profile references from that provider before native
selection and requests one fresh profile; assigned bodies/inventories are not
modified or disposed. Exact current PMC backup additions are suppressed.
Normal raids, Scavs, unlimited modes, PFT and other providers retain preloading.
One backend request per new PMC is the deliberate tradeoff for timely gear;
the existing bounded per-faction preparation queues still govern concurrency.
Native bundle loading and cancellation remain unchanged. This is not a promise
to override an arbitrary third-party cache refill arriving during the await.

Native anchors: `../_decompiled/BotProfileClient.cs` constructor, LoadProfiles
and CreateProfile; `ABotProfileCreator._simpleProfiles`; `BotProfileBackuploader`
constructor and AddProfileForBackup; `BotCreationData`, `BotCreatorClient`,
`EFT/CountTypeBotWave.cs`, `EFT/ClientBackendSession.cs` LoadBots. No third-party
implementation was copied or bundled.

## Latest preceding movement retest

Frozen client log: `artifacts/duration-balance/current/LogOutput.log`, SHA256
`423E9CF3657346F68E2D962D892E83469242ED9EEEC2F77F938EB9113C82152B`.
This tested the previous search change, not this new balance build.
TDM finished normally at 21:00:16, USEC **35:17 BEAR**, 600 seconds. Five local
respawns completed in 0.75–0.83 seconds. No activation failure/timeout or in-raid
error appeared. Protected end completed in the UTC server log at 19:00:32.

Compared with the preceding run's endpoint samples, intervals with at most 1m
displacement fell from 21/156 (13.5%) to 7/147 (4.8%); median displacement rose
from 8.75m to 12.5m. These are endpoint samples, not continuous paths or a
controlled experiment. There were 183 successful 5s search caps and no adapter
errors. This is consistent with the user's positive impression.

One bot again reached Customs `(464.91, 1.05, -43.64)` with invalid navigation
and two samples 30.7s apart; last displacement was zero. Its search delay was
already 0.1s. Keep this separate known geometry/path problem open; this request
does not justify teleporting bots or removing a spawn. The later menu GUIClip
error (21:02:55) and two animator synchronization warnings lack identifying
stacks and remain unattributed. Airborne mantle remains a separate open issue
from the earlier test, not certified by this run's broad positive feedback.

## Verification and GPU acceptance

`tools/DurationDifficultyChecks/Verify.ps1` links actual curve, pressure and
request-scope code. It covers cache references, DTO copying, serialization,
stale/foreign scopes, configuration opt-outs and an actual suspended Task's
scope boundary, including partial hook installation/rollback failures. Nine
source faults must fail the intended checks.
`tools/TeamDeathmatchRuntimeChecks/Verify-Regression.ps1` exercises the actual
TDM lifecycle including frozen clock, long assembly and terminal/replaced raids.
Real server tests exercise deserialized extension data, exact session isolation,
invalid metadata and batch preservation/capping. Staged client/server builds
compile against installed APIs and verify installed artifacts remain unchanged.

The GPU acceptance still belongs to the user: short finite matches with bot
replacement before/after 80%, no accelerated loading/countdown, normal and
unlimited raids, PFT retained gear, no growing preparation latency. Compare
server `clock=match` tier lines to client live elapsed time. Both client and
server binaries need restarting after delivery. No native GPU raid is claimed.
