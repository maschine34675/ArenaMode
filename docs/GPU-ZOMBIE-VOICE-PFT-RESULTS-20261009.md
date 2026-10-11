# Zombie voice budget and PFT fast path — 2026-10-09

## Build and scope

- Final production client: `4220c0501c69d362dd868e2bec73a3b388bfe01f`,
  SHA256 `96E93E67E1CAD501D6B7F6C335C9912143C0C626FECBB3A3F94CB2A111800D19`.
  Deployed to development installation and SPT-GPU-2; restarted process
  `6f169a26d4d74f27a2b6a8677f0b8f3a` verifies 21/21 compared plugins and Core
  matching. GPU profile is SPT-GPU-2, Factory day.
- Staged build `artifacts/double-jump-build/cf55bc6b6dcc4cf995a384cab793bf60/`
  passed with zero errors and four existing CS8632 warnings. The initial build
  caught the `EFT.Version`/`System.Version` ambiguity; `daa1dd0` qualified it.
- Server remains `0.1.0+9c67ed74ea032c239656599536ee645b53e9fc7a`, SHA256
  `C0871C34B4F1AFAAE990777E39827EDCC1BAF7A6C74401A1A0A47AD73DE49307`.
  Core SHA256 `673D9A106F747E5EE0A70EDE21E9DE9E5AB48E5DDE12E0B2922B2139ECDFE4DD`.
  No server, Core, PFT, SAIN or other third-party DLL change.
- UiBridge remains `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`, SHA256
  `306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E`.
- Two default-on F12 comparison/fallback switches under Zombie:
  `PftFastPathEnabled` and `VoiceBudgetEnabled`. Both apply immediately.

## Source and offline acceptance

The independent installed-IL/source reviews found no blocking defect, including
the subsequent duplicate-list correction. Parent reruns passed the actual-source
PFT harness (190 assertions, 19 compiled fault variants rejected), voice harness
(325 assertions including 16 native contracts, 19 compiled faults rejected),
and unit suite (913 passed, two existing skips). Compilation errors are not
accepted as successful mutation failures. Tests cover ordinary/wrong/stale raid
and bodies, custom brains/layers, infected bosses and followers, wake fallbacks,
clip lifetime, native playback refusal, settings changes and cleanup.

The final PFT parent manifest is
`tools/ZombiePftFastPathChecks/evidence/f0a56a30fd6a4cfbaa36c0a4f59b9cf4/manifest.json`.
Production PFT source hash is
`729829569422C0A59724D7DEFDEF65D8E752765620033C8132F422BCDE9E0F36`;
voice hash is `EEA3BFB278F3CFE6B7AC1096CDAE80D71A5A96DCC364B00CFECF607999F11BC9`,
policy hash `E979B7ACABABDC5D8BE6959A2450E4A2658CAB1E6435BB81D257496E8BD96703`.

## Native discovery and voice evidence

The initial native client `daa1dd0` proved both detours actually execute under
Mono: PFT calls/skips were nonzero, and voice postfixes observed matching clips
playing on Unity AudioSources. The seven-entry PFT guard then stopped accepting
older melee brains. Read-only inspection found lists of 15, 15, 21 and 22 entries,
mostly repeated references to the same native `InfectedWaitLayer`. Installed IL
confirmed native `SetCanAttack(false)` repeatedly inserts it without deduplication.
The final correction removes only our entry-count ceiling; every entry is still
checked after list mutation, and the native list is never changed. Initial-build
late cost figures are therefore not acceptance of the corrected optimization.

The voice code is unchanged between the initial and final client. Initial native
observations include:

- At 16:09:07, the enabled approach window had 49 repetitive candidates,
  24 confirmed playing-source observations and 25 suppressed requests; peak four.
- At 16:14:04, an enabled 30-second window had 448 candidates, 204 suppressed,
  63 confirmed playing observations, 67 exempt phrases and peak six.
- Disabled windows suppressed zero requests. The corrected final client's first
  disabled window reached 17 observed repetitive sources and 250 `wouldSkip`.
- The final client later reached peak 20 with the limit disabled. After restoring
  the limit and moving away from the melee crowd, its 16:24:53 window confirmed
  46 playing observations, 208 suppressed requests, peak four and 14 exempt
  phrases; the following partial exit window reached peak six without overflow.
- No observation overflow or voice-admission warning. Critical phrases and nearby
  attack calls bypass the budget; `exempt` combines those categories and does not
  prove separate audibility of every critical phrase. A playing source is not an
  audio-device or subjective hearing result.
- The initial solo run recorded 11,068 native melee attempts and 1,892 landed hits
  with player godmode. Deliberate local death held respawn and ended protected;
  the voice service reported its boundary and the native result flow reached
  MainMenu without a new local life.
- The corrected final solo run recorded 17,791 native melee attempts and 3,107
  landed hits under godmode. All 56 bots remained alive during the comparison.
  Its final PFT aggregate was 1,023,141 eligible-context calls, 1,022,918 skips,
  223 original standby fallbacks, 1,393 list validations, zero custom-shape
  fallbacks and zero errors. The count difference is exactly the retained wake
  path. Protected Disconnect returned to MainMenu and reset the services.

The voice limit applies to six repetitive voices, with at most four at/above
20 m; attack calls within 8 m and all other phrase types retain native playback.
Native Say/SAIN hearing, speaker busy/priority/chain events, and other subscribers
are preserved. This is not a cap on every audible sound or all occlusion work.

## Measurement design

The solo comparisons use one living human, 56 living melee infected, own gear,
zero shooter share, no stuck-wave timeout, and player godmode, with no kills or
corpse workload. All capture tests use the same Factory point near
`(72.1, 0.3, -73.9)` and native navigation/room checks. No bot state is frozen.
ModProfiler headless exports every 30 seconds, GC remains as configured.
Voice suppression is disabled during PFT comparisons. The PFT switch is toggled
within the same raid; mixed transition intervals and partial tails are excluded.

All CPU values use CSV delta intervals. The PFT timer includes Arena's nested
prefix; do not add it to the nested Arena timer or to parent AI timers. The old
baseline's six full windows measured 0.20782–0.36844 ms/frame for PFT's prefix at
exactly 56 calls/frame. Fight state and native list growth change across time;
these are workload observations, not an FPS benchmark or a population-cap claim.

The final run switched PFT off at 16:21:47.152 and back on at 16:23:14.458
(CEST). Voice admission was disabled for the entire measured span. Full,
unmixed windows show:

| Window end | PFT switch | PFT prefix inclusive ms/frame | Nested Arena guard ms/frame |
|---|---|---:|---:|
| 16:20:34 | On | 0.04487 | 0.03370 |
| 16:21:04 | On | 0.04589 | 0.03460 |
| 16:21:34 | On | 0.04723 | 0.03589 |
| 16:22:35 | Off | 0.33815 | 0.00834 |
| 16:23:05 | Off | 0.37805 | 0.00847 |
| 16:24:05 | On again | 0.04413 | 0.03292 |

All rows have exactly 56 PFT calls per frame. The return-on window differs from
the two off windows by **0.29402–0.33392 ms/frame**, including the guard's cost
already. This is the observed reduction of this specific inclusive path in one
raid; it is not a statistically controlled FPS result. Native AI, melee, SAIN
and other mods continue working. Duplicated wait layers grow naturally over the
test, making the return-on result particularly relevant. The zero-frame start,
16:22:04/16:23:35 mixed-toggle windows and 6.4-second final tail are excluded.
The early on windows still contain approach; the late off/return-on windows
have comparable melee activity (53.13–53.48 versus 53.14 calls/frame) and melee
inclusive cost (0.34850–0.35438 versus 0.35373 ms/frame). PFT's finalizer also
remains approximately 0.0078 ms/frame. This supports the narrower path comparison.
There is only one full return-on window, not randomized repeated trials.
Profiler timers stayed intact, with zero patch failures, zero GC collections,
no resets/rescans and 1,787 instrumented methods. Relevant rows have no warning
flags. The measured inner instrumentation floor is 0.139 microseconds/call;
values are reported raw rather than subtracting an estimated overhead.

## Four-companion smoke

The final client was also tested with the existing test1–test4 companions,
invited through the native My Squad context menus. Factory confirmed the
five-player squad, PMC entry gear and next-wave restoration. At 16:28:09.111,
the log confirmed the exact five-player team ready; all four companions were
active on follower layers, with no replacement enemy PMC team.

Infected spawned, approached and landed native melee hits. The unarmed local
PMC, with godmode off for this separate smoke, died naturally at 16:28:32.
Arena retained the squad and switched to protected spectator instead of giving
an immediate solo respawn. A screenshot showed test1 aiming/shooting at nearby
infected, HP 410/410 and a test1 SR-25 zombie kill at 7 m. Thus follower combat
and enemy acquisition continued with both changes enabled.

The squad's 120-second PFT aggregate was 257,377 calls, 257,371 skipped, six
standby fallbacks, 346 validations and zero shape/errors. Its first voice window
had 288 candidates, 114 admitted, 83 playing observations, 146 suppressed,
92 exempt and no overflow. After local death, the voice budget deliberately
passes native audio rather than measuring distance from the dead PMC's body.
This is an initial combat/spectator smoke, not a new wave-rebind or wipe test,
and no squad performance gain is asserted.

Protected Disconnect reached MainMenu. The final squad PFT counts were 318,022
calls / 318,016 skips, six standby fallbacks, 398 validations and zero errors
inside the fast path. The native KillList independently recorded companion
infected kills. One already-known Arena diagnostic occurred one millisecond
after `Stop(Left)`: the physical PFT roster lost an account while PFT removed
its wrappers. This is the same shutdown-observer false alarm documented in
`GPU-ZOMBIE-PFT-MENU-RESULTS-20261009.md`, not a clean error-free exit log.
There was no preceding live-team error, abort or persistence loss. The observer
teardown race is not changed by this performance work.

## Preservation and remaining limits

After protected exits and restoring the exact test-setting ledgers, the full
profile differs only in `Health.UpdateTime` and `Hideout.sptUpdateLastRunTimestamp`.
All twelve saved PFT companion files remain byte-identical. No profile or
companion backup was written back to produce that result. All previous Arena
and ModProfiler setting values match their backups; only the two intentional
new default-on switches were added. Temporary profiler capture and godmode are
off. The old retreat probe remains absent. Unrelated Molotov assets are left
untracked and unchanged. No remote push was requested or performed.
GPU2 EFT is closed; its SPT server remains running. Parent preservation evidence
is `artifacts/zombie-voice-pft-20261009/preservation.json`; the independent squad
record is `pft-smoke/SMOKE.md` and `pft-smoke/native-evidence.json` there.

The native acceptance covers solo approach/melee, off/on comparison, voice
admission and cleanup, plus initial four-companion combat, natural local death,
spectator continuation and protected exit. It does not certify arbitrary custom
brains, repeated wave rebinds, team wipe, long-session tuning or subjective voice
balance; their untouched/guarded paths and prior evidence remain separate.

SAIN remains unchanged. The installed producer/consumer investigation is in
`ZOMBIE-SAIN-GLOBAL-REVIEW.md`; no supported Arena-owned sparse-pair boundary was
found. Infected-to-companion and companion-to-infected data remain necessary.

Ignored raw evidence is under `artifacts/zombie-voice-pft-20261009/`, including
baseline/initial/final captures, source-bound build output, logs, settings ledgers
and full profile/PFT preservation comparisons.
