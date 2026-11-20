# Arena logging

Policy introduced 2026-10-15. This affects ArenaMode and its optional BigBrain
companion only; it does not filter Unity, SPT or other mods' output.

| Level | Purpose |
|---|---|
| Info | Loaded build, available integrations, the armed mode and its frozen raid settings, loadout protection, profile restoration and player-visible limitations or recovery. |
| Debug | Successful hook/asset setup, spawn requests, placement, lifecycle/identity details, audio queue policy, normal cancellation and gameplay: match, round and wave starts, results, scores, money and per-spawn kit details. |
| Developer | Detailed movement, audio, renderer, AI and corpse probes, including individual hits/jumps. Opt-in, with existing rate limits. |
| Warning / Error | Real failures, degraded behavior, rejected protection/identity contracts, failed restore, missing assets and unexpected native exceptions. |

Gameplay is not an Info topic (user decision 2026-10-18). Match, round and wave
lines, results with scores, Tactical money and kit details such as skipped
optional grenades or spare magazines that did not fit are Debug. A no-contest
round and a kit without any reserve stay Warning: there the round has no valid
result or the player fights on the loaded magazine alone.

The Gun Game weapon-pool generator is the same kind of case: when weapon presets of the item database
do not fit it (a weapon mod changed a weapon's slots) it logs ONE Warning per server start with the count,
the first three presets and their reasons, details at Debug, and nothing when nothing was left out. Left out
silently, a lost weapon would be as invisible as the old `base-invalid` Debug line.

A start whose rewards are paused is not gameplay either: it is one Warning per start that
names the reason code ("Arena rewards are paused for this start: ...", see
[Rewards paused at start](REWARDS-RUNTIME.md#rewards-paused-at-start)), never Debug. A
withheld reward once stayed hidden behind a Debug line for 14 days.

The difficulty model follows this rule: the server's gear-band line (`Bot gear band on ...`,
mode, step and level) and the client's per-spawn level lines are Debug. The step is not an
Info topic; the only Info line is the one-time `Arena difficulty step set to Reduced from the
old difficulty settings.` after the settings migration, which is a player-visible change.

Translocator supply, launch, recall, confirmed relocation and expected target
refusals are Debug. Missing device assets, incompatible templates and unexpected
native failures use bounded warnings; no per-frame physics logging is emitted.

In F12, tick **Advanced settings** and enable **Diagnostics → Developer Mode**
(.cfg key `Diagnostics/DeveloperMode`) to collect detailed probes. It
defaults to **false**, is not a gameplay mutator and can be changed at runtime.
Enabled probes appear as **`[dev]` Info** so no global BepInEx Debug change is
needed. Ordinary Debug messages still use the logger's native Debug setting.
Turning DeveloperMode off stops these probes again. Existing saved settings are
not reset. Zombie melee observation only attaches to bots arriving while enabled;
enable it before the raid for full coverage of that diagnostic counter.

Zombie route planning reports cumulative approach/melee jobs, deferrals, the
two-job frame limit and queue age at most once every five seconds in Developer
Mode. These count complete native planning jobs, not nested NavMesh calls or CPU
time. Individual stuck replacements are ordinary Debug lines with no combat
credit; a failed native recovery or budget hook warns once per lifecycle.

DeveloperMode also opts each new Arena respawn request into Core 1.0.8's detailed
pipeline timing. With it off, state/reflection counters and following-frame
measurements are not collected. The standalone Redeploy driver has its own
`Diagnostics/EnablePipelineTiming` switch, also off by default. A measurement
already started finishes normally if the switch changes mid-respawn.
The per-raid timing sequence counts measured pipelines; respawns while disabled
do not consume a diagnostic sequence number.
Core timing uses its own ordinary Info lines; Arena's other detailed probes
retain the `[dev]` prefix.

Native model faults receive bounded object/body context only in an admitted
Arena world. See [the exact hooks and limits](reviews/NATIVE-MODEL-DIAGNOSTICS-20261018.md).
Original native errors remain unchanged. The server independently omits only
successful exact settings GET request lines; failures and other routes keep
their diagnostics ([scope and checks](reviews/SETTINGS-POLL-LOGGING-20261018.md)).

A BepInEx installation configured with disk/console `LogLevels = All` still
records Debug messages. This cleanup does not change that global setting or
Harmony's independent trace channels.

**LogDamageExceptions remains independent and defaults on.** It records the
original failure and body state without suppressing the exception or changing
damage. Do not disable this safety alarm merely to get a clean log.

For developers:

- Use `LogDebug` / server `Debug` for routine technical information.
- Use `ArenaMode.Diagnostics.ArenaLog.Developer` for detailed probes. Guard
  expensive reflection, scene scans and payload collection with
  `ArenaLog.DeveloperEnabled` before doing the work.
- Never put gameplay decisions, cleanup, lease release, validation or safety
  warnings behind a logging switch. Diagnostic-only counters may be partial.
- Where one helper reports both normal policy and failures, route by the known
  normal reason; unknown future reasons retain Warning. The ladder generator
  supplies separate detail and warning callbacks for this reason.
- Keep the loaded client **build/MVID** in the startup Info line. It identifies
  the actual module even if a DLL on disk was replaced during a running game.

The standalone session helper keeps lifecycle summaries, retries and failures in
`helper.log`. Per-link filesystem cleanup is Debug and off by default. To collect
it for a helper run, launch with `ARENAMODE_SESSION_DEBUG=1` in its environment;
the watchdog inherits that setting. This does not change copy/delete safety,
profile handling or the game's separate DeveloperMode setting.

Every dialog of the helper has the same shape: what happened in plain words;
whether the profile was changed, claimed ("Your profile was not changed.") only for
a refusal the normal server said it can continue from or a failure before that
server was asked to stop; what the player can do next; where the details are
(`Details:` is the full `helper.log` path, `SPT server log folder:` is
`<SPT folder>\user\logs\spt` where the server refused); and last a `Code:` line for
reports. The code is `stage/reason` for a declined entry (the same text as the
`Arena entry declined:` log line, e.g. `preflight-save/save-not-confirmed`),
otherwise a fixed word such as `entry-paused` or `start-failed`. Dialog texts come
from `HelperRunner.EnterFailureNotice`, `HelperOfflineSession.WaitingDialogText` and
`HelperRewardReturn.*DialogText`; log lines are unchanged.

Two dialogs belong to a normal server that started with Arena reward recovery paused
(server Warning containing "rewards are paused for this start"). An entry on top of the
unsettled session is refused (the helper's own check before it stops anything, or the
server's `offline-session-already-active` / `rewards-paused-recovery-pending`
decline) with code `session-unsettled` (`entry-preflight/session-unsettled` when the
server declined). A return whose normal server answers but left the session
unsettled opens the game against that server (never stopping or restarting it) and
shows one dialog afterwards, "Arena ended and SPT is running again, but it could not
settle the Arena session when it started", with the server's reason code in the text
and `Code: return-rewards-paused`. The helper accepts that only when
`user\arena-rewards\rewards-paused.json` names the very server process it started
or found (pid and start time); otherwise the old failure dialog and its log line stay.
The dialog makes no claim about the profile, because the helper cannot prove it.

## Session save refusals

An authenticated entry/return rejected by the drain/save sequence emits one
Warning per attempt, without enabling Debug or DeveloperMode. Successful checks
stay silent. The original `stage/reason` remains searchable; the warning adds:

- `operation=enter|return` and, for `return-profile-save-not-confirmed`, an eight-character
  hash alias of the failing loaded profile plus `selected|other-loaded`.
- `ramDiskMatch`: the first RAM snapshot matches the saved file (when another mod
  transforms the profile write, see the pipeline tokens below: this stays the plain
  RAM-against-file result); and
  `ramStableDuringReadback`: RAM is unchanged between the two snapshots. **Both RAM
  snapshots are after the native save.** Either or both comparisons can fail.
- `pipeline`, `ramInputMatch`, `outputDiskMatch`: printed directly after `diagnostics=`
  and only when `pipeline` is not `none`, so a setup without a profile-writing mod keeps
  the lines it always had. `pipeline` is one of `transformed` (another mod, for example
  Pack A Punch, changes the text on its way to the disk and Arena captured it),
  `opaque` (no usable capture: a direct byte writer, a skipped write, a limit),
  `unsafe` (the capture is not a pure removal or touches Arena's reward/identity data),
  `unavailable` (the capture hook is not active), or `none`.
  `ramInputMatch`: RAM equals the text handed to the write; `outputDiskMatch`: the bytes
  the write produced equal the file. Both are `true|false|unknown|n/a`. Mod names,
  assembly names, paths and counts never appear. See
  [PACK-A-PUNCH-COMPAT.md](PACK-A-PUNCH-COMPAT.md).
- `fieldSample`: at most three fixed schema section labels, prefixed `disk:` or
  `ram:`; with a transformed write the prefixes are `input:` (RAM against the write's
  input), `output:` (the write's output against the file) and `ram:`, and `disk:` is
  left out because it would only list the other mod's expected edit. This is a sample, not a complete diff. Unknown extension fields stay
  `unclassified-section` or their nearest known parent; values, dynamic dictionary
  keys, array items, full profile IDs and paths are never included.
- `updateStopStarted`: native update stopping has begun, not necessarily completed.
  `canResumeNormal` is the listener's final permission to reopen the normal game,
  including the request/update holds and host shutdown state. On the legacy return
  route it remains false by protocol; false alone does not mean an unusable server.
  If that final decision could not be reached, it is `unconfirmed`.

Diagnostics use only the already captured strings, only after a failed comparison.
Each snapshot is limited to 4 Mi characters and JSON depth 64; larger snapshots
report `size-limit`, and unavailable comparisons say `unknown`. Sampling failures
cannot alter the original refusal. A held process retains the retry/do-not-close
guidance in the same warning. Logging failures cannot change the reply or holds.

These observations do **not** prove profile corruption or identify the writer.
For a report, request the surrounding server log and matching `helper.log` section.
The save/readback rule is the exact comparison, extended for a transforming mod by
the captured write pipeline: RAM must equal the write's input and the write's output
must equal the file, the output must only remove data and leave Arena's reward and
identity blocks alone (`docs/reviews/PROFILE-WRITE-PIPELINE-20261120.md`). Without such a
mod nothing changes. Helper replies and lease formats are unchanged.

The raid end and the failed-start restoration have no session switch to refuse. When their
readback is not confirmed, one Warning is written once per process:
`Restored profile not confirmed after saving: operation=raid-end|failed-start; profile=<alias> (<role>);
ramDiskMatch=...; ramStableDuringReadback=...; fieldSample=[...]; diagnostics=...;` plus the pipeline
tokens above when they apply. Other operation words print as `unknown`.

Other lines of the profile write capture: Warning once per start if the capture hook cannot be
installed ("Profile write proof unavailable; Arena keeps the strict save comparison"), Warning once per
process if the Pack A Punch side file time cannot be kept ("Could not keep the Pack A Punch side file ...").
Debug: the hook enabled (number of foreign prefixes on the two write methods, no names); one fact per process
that a profile write is transformed (profile alias only); one line per process when a captured write had no
output; the side file kept fresh (alias only); an unsupported Pack A Punch version. None of them is Info.
`SessionSaveDiagnosticsTests` exercises the production comparison and sequence,
including privacy sentinels, limits, retries, held failures and a throwing logger;
run it as part of `tools/Run-ArenaVerification.ps1` without deploying.

## Logging cleanup evidence

The cleanup reviewed all client, server and companion log emitters. Historical
Info call-site counts are not a promise about total lines per raid: other mods,
Unity and server request middleware retain their own logging policies.

Implementation and evidence:

- [Client inventory and exceptions](reports/2026-10/LOGGING-CLIENT-AUDIT-20261015.md)
- [Server/companion inventory and warning routing](reports/2026-10/LOGGING-SERVER-AUDIT-20261015.md)
- [Existing log errors and attribution](reports/2026-10/LOGGING-ERROR-AUDIT-20261015.md)
- [Harness integration](reports/2026-10/LOGGING-HARNESS-AUDIT-20261015.md)
- [Fresh GPU2 test](reports/2026-10/GPU-LOGGING-TEST-20261015.md)

Reproduce the dedicated routing check with
`pwsh -File tools/LoggingChecks/Verify.ps1`. It uses the real installed BepInEx
logger plus production `ArenaLog` source and rejects broken opt-in guards. The
server unit suite also tests routine ladder output separately from malformed
draws, prohibited-ammunition fallback and attachment failures.

## Crowd reactions (Debug)

The spectator crowd presentation (docs/reviews/CROWD-REACTIONS-20261102.md) logs at Debug only
and nothing per frame: `Arena crowd attached: mode=…, excitement=…, reactions=…` or `Arena crowd
off: …` once per match (the key was off, Zombies, ArenaMapPort absent, an older or incompatible
contract); `Arena crowd reactions claimed: handle=…, kinds=0x…, attempt=…` or, after the 30 s
window, `Arena crowd reactions not claimed: …`; one `Arena crowd reaction: kind=…, strength=…,
source=…` per sent reaction (kinds Approve, Disapprove, Start, Finish and Lull, the last with
`source=no kill for 60 s`; ", announcer speaking" when the interlock halved it); `Arena crowd
reaction dropped: kind=…, strength=…, source=…, …` when the one-mood rule or the 1 s mood window
of ArenaMapPort's roar layer drops the weaker of an Approve and a Disapprove (the rest of the
line names the other mood); `Arena crowd
reactions released: handle=…, reason=…, sent=…` and `Arena crowd detached: reason=…,
reactions=…, excitementPeak=…`.

Warnings, by prefix. Once per process and reason (the installed contract does not change between
matches): `ArenaMapPort SpectatorCrowd has no int ContractVersion property`, `ArenaMapPort
SpectatorCrowd contract version … is not supported`, `ArenaMapPort SpectatorCrowd
SetExcitement/IsAvailable have another signature`, `ArenaMapPort SpectatorCrowd reaction members
have another signature`, `ArenaMapPort crowd contract unavailable:` (the bind threw). At most
once per match each: `Arena crowd presentation unavailable:` (Attach threw), `Arena crowd
presentation stopped:` (the tick threw; everything is released), `Arena crowd murmur intensity
stopped:` (`SetExcitement` or `IsAvailable` threw; reactions keep running), `Arena crowd match
snapshot unavailable, murmur intensity and lull stopped:` (the ArenaMode snapshot read threw; the
other reactions keep running), `Arena crowd murmur intensity reset failed:`, `ArenaMapPort crowd
reactions unavailable:` (`ReactionKinds` or `ClaimReactions` threw; no further attempts),
`ArenaMapPort crowd reactions stopped:` (`React` threw; the claim is released), `ArenaMapPort
crowd reactions release failed:`, and `Arena crowd reaction event failed:` (the first exception
in a crowd event handler; it is contained and never reaches the match).

## Tactical opening rounds (solo)

The round report and the squad hold (`TacticalOpeningBandGate`, `ArenaTacticalRoundClient`) log
at Debug: the server refusing or not applying the feature, a failed report attempt, a held squad
request with its seconds and release reason. Warnings: `Tactical opening rounds: the server did
not answer the round report (...)`, at most once per raid, when a report never got through
within its four sends; `Tactical opening rounds failed while <doing> (...)`, at most once per
raid, when the gate itself threw (the squad is then not held); and `Tactical opening rounds could
not start; ...` when the match-start wiring threw. Nothing is written at Info. The SPT HTTP
client logs at Error on its own for each failed attempt (up to 16 lines for the first report
against a server that lacks the route); that comes from SPT, not from this feature.

The pool purge (`ArenaTacticalFreshGearPatch`) logs at Debug: `Arena Tactical: dropped N pooled
<role>/<difficulty> profile(s), levels a to b, gear level now c; the next one is generated fresh.`
per purge, and `Arena Tactical: N pooled <role>/<difficulty> profile(s) are outside gear level c but
2 purges for this level are spent; they are served as they are.` once per pair and level. Warnings,
each at most once per raid: `Arena Tactical: the server's gear band does not match the round; bot
profiles of the pool are served as they are.` (the purge budget ran out: the bot system's range
adapter is off, or a round report is behind), `Arena Tactical fresh gear skipped; native generation
retained: ...` (an exception while reading the pool) and, at plugin load, `Arena Tactical fresh gear
unavailable; ...`. The server's `Tactical round N reported (seq S); gear level L ...` and the
`round=N` suffix of the `Bot gear band` line are Debug too. The buy-screen line is not logged.

## Map-screen refresh probe (DeveloperMode)

While `Diagnostics/DeveloperMode` is on, the Arena map selection screen logs every five
seconds at Info `[dev] Arena map menu refresh: N ticks in 5 s, max M ms; avg/max per
section (ms): ported, modeEligibility, modes, eligibility, rows, prepare, areas, info,
preview; compat …; pft …`: the sections of one `ArenaLocationSelectionPatch` refresh tick
and the call timers of `ArenaMenuSelection.CallTiming` (the optional-mod admission and the
PitFireTeam roster decision). Nothing is measured while the mode is off. Added 31.10.2026
to attribute the screen's frame-time spikes (see HANDOFF, "Arena map screen frame drops").
