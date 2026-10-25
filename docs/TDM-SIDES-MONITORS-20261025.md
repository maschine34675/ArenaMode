# Team DM sides and ArenaMapPort score monitors

Date: 2026-10-25. Post-0.3.0 change; native verification is recorded below when complete.

## Behavior

Team DM offers USEC and BEAR buttons on the map selection screen. A new installation
initially follows the real PMC faction (`Team Deathmatch / Side = Profile`); clicking
either button stores that explicit choice. The selected team is frozen before the
asynchronous native load. It controls friendly/enemy relations, team population,
initial team starts and local respawns, including the ported maps' team boxes and
Customs area starts. A red or blue selection marker makes the current choice clear.

The real profile faction, appearance, voice and equipment remain unchanged. The
virtual team is bound to the profile ID, native faction, arm token/epoch and exact
game/world pair. Foreign bodies, stale raids and missing identity are refused. It
is cleared on cancellation, ordinary raid entry and Arena teardown. Gear selection
may use this frozen choice in a later feature; no such behavior is included here.

USEC is blue and BEAR is red. The shared presentation palette covers tracers,
mounted/akimbo lasers, Arena lighting, friendly diamonds, killfeed names and score
accents. It changes neither friendly-fire rules nor saved custom colors. Health,
armor and powerup indicators retain their own meanings. Other modes retain their
configured friendly/opponent colors.

## World monitors

The optional `com.maschine.ArenaMapPort` reflection API uses the exact primitive
signatures of `ArenaMapPort.Client.ScoreMonitors`, contract version 1. No hard
dependency or foreign assembly reference is introduced. The MapPort contract is
documented in `D:/ArenaMapPort/docs/progress/score-monitors.md`.

BEAR is always on the left in red (`FF6B6B`); USEC is always on the right in blue
(`7FA7FF`). Scores and living participant counts are mapped from the selected
team, rather than assuming the local player is USEC. Forfeit points follow the
same scoreboard. The clock shows readiness/countdown, remaining match time or
elapsed time for unlimited matches. The result score/clock stays frozen during
the existing 15-second result display.

Each match gets a unique claim shorter than MapPort's 40-character owner limit.
Failed setters, stale handles, match end, failure, protected abort and native stop
release that exact claim. Missing/incompatible monitors cannot fail the match.
Score totals are cached by scoreboard version; unchanged display groups are not
rebuilt. The cheap clock setter also validates ownership on identical frames.

## Extraction isolation

The native BEAR respawn test exposed a pre-existing overlap between Equator's
south team box and its eight-second exit. This is **not** faction eligibility:
native extraction matches the raid's EntryPoint, and both Equator exits accept
`ArenaEquator`. The new side choice made the overlapping location easier to reach.

The user subsequently requested all extractions disabled throughout Arena.
This rule belongs to ArenaMode, not ArenaMapPort: it applies to every Arena mode
on vanilla and ported maps while normal PMC/Scav extractions retain their behavior.
`ArenaExtractionIsolation` guards standard and secret `Proceed` calls plus the
extraction-only native `IGame.StopSession` callback, which covers shared timers.
It requires the exact current local body, game/world and registered exit; loading
additionally requires the acknowledged arm token in the native request. There is
no global exit-status mutation and no patch to general `LocalGame.Stop`.

The obsolete team-box-only extraction deferral/replay was removed. Opening
gates now never starts a deferred extraction timer. Match results and manual
Arena leave still own their normal stop path.

## Verification

The production client and BigBrain companion build successfully. The client
reports four existing CS8632 nullable-context warnings and no errors. The full
unit suite passed **3,197 tests**, with one known APBS-environment skip. The
integration run passed all 18 selected harnesses; the separately rechecked PFT
contract harness passed **381 assertions**. Initial failures were old test
fixtures or exact mutation anchors; those were updated and fully rerun.

Selected evidence:

| Check | Result |
|---|---|
| Frozen team choice / invalid and stale identity | 329 assertions |
| Central palette and isolation | 128 assertions, 3 rejected mutations |
| Optional monitor API, projection and release | 183 assertions, 10 rejected mutations |
| Team DM lifecycle, both opposite selected/native sides | 410 assertions |
| Native menu options and side buttons | 2,544 assertions |
| Ported-map box placement and replacement bodies | 472 assertions |
| Customs area team runtime, including both opposite sides | 473 assertions, 25 rejected mutations |
| Native spawn-gate integration after extraction guard centralization | 275 assertions, 25 rejected mutations |

The additional give-up, spawn-shield scope, team-menu, tracer, laser, lighting,
friendly-marker, UI-asset, native-mode and killfeed checks passed. The three
dedicated harnesses compile the production selection lease, palette and monitor
binding/mapping. NativeMenu fixtures link the same selection lease. The parent
independently reran the leaves and repaired integration checks.

Local evidence is under `artifacts/tdm-sides-20261025/` and
`artifacts/run-arena-verification/`; the former preserves the full-suite TRX,
build logs, integration retest and deployed hashes. Offline builds suppressed
all deployment targets. Both client DLLs were subsequently deployed together,
and `gpu-run deploy-wait --host host2` verified identical host copies.

| Deployed component | SHA-256 |
|---|---|
| Final Arena client | `69C25C74F383EBAB256C63D4FE5050CD9F069A2768BC00434EAE64EDE6F985F4` |
| Final BigBrain companion | `5C32B9FC3DFB29AC9200FFB3C857933DB495C45BAF18F7DCAB028E6F9968B1CA` |
| ArenaMapPort API provider | `0A7A5EAACA2F52885C5274CE6529DA73E89F84B149629047DF964EB100E43F43` |

The server, map bundles and Core were not changed by this feature. These are
post-0.3.0 development DLLs, not a new published release.

After the extraction scope extension, the parent reran the full unit suite
(again 3,197 passed, one known skip) and the final extraction harness:
**127 assertions, 12 rejected mutations**. The updated spawn-gate harness passed
**275 assertions and 25 mutations** after its old temporary extraction checks
were replaced by the central extraction harness. Its negative cases include
ordinary/stale/foreign contexts, death and absent replacement bodies, timer
grace (`SoftStopping`), manual/non-extraction exit statuses and hook rollback.
The client/companion final build passed without deployment before both were
explicitly installed. Final evidence: `final-extraction-gates.log`,
`spawn-gates-extraction.log`, `final-deployed-hashes.json`.

## Native verification

GPU-2 first candidate: EFT 0.16.9.5-40743, SPT 4.1.6, UiBridge 0.9.6 and
ArenaMapPort 0.3.0. The original test PMC is USEC. The actual menu buttons were
clicked, not replaced by configuration-only setup. Native evidence is under
`artifacts/tdm-sides-20261025/gpu/`, including `summary.md` and screenshots.

- Equator USEC 2v2 started in the north box `(-1.7, 0.1, 45.5)`, with one USEC
  ally/two BEAR enemies. Nine monitors were claimed and rendered scores, the
  countdown and alive markers. Parent inspected the screenshot independently.
- Equator BEAR 2v2 started in the south box `(-4.3, 0.1, -39.1)` despite the
  unchanged native USEC profile. Runtime affiliation was BEAR/red. USEC enemy
  dictionaries included the player; the BEAR ally's dictionary excluded them.
  A real VSS headshot killed a USEC bot and scored for BEAR. After godmode was
  disabled, a USEC enemy killed the player and the replacement spawned at
  south-05 `(6.51, 0.08, -41.99)` with native USEC profile and BEAR affiliation.
- That first respawn exposed the eight-second native extraction overlap;
  the raid exited RunThrough and monitor release correctly followed native
  shutdown. This led to the central extraction change above. It was not a
  monitor rebind failure or proof of faction-dependent native exfil eligibility.
- Customs Factory Side BEAR 2v2 started at `(570.45, 1.48, -111.76)` and reached
  Live with one BEAR ally and two USEC opponents. No MapPort monitors were
  claimed on this vanilla map. Parent inspected the menu screenshot: side
  choice, area and team-size rows remain separate and readable.
- After these raids the original faction was still USEC; frozen choice and
  monitor claim were cleared. All 227 Arena config values matched their saved
  baseline. Test-only godmode/ammunition hooks were reset.

No ArenaMode/Redeploy/Killcam warning or error was found in the merged first-run
client/server/EFT export. This does not describe the whole log as clean: native
material/weather/audio diagnostics and Unity LOD/missing-script/collider warnings
remain, plus a MapPort missing Books-impact sound. Their causality is not
established by this side-selection test; details are in the local GPU summary.

The final build was loaded in process `39c0cb84`; client MVID
`68ad0206-354c-4cc8-a04b-921e331d3ee5`, companion MVID
`a3a295fb-8e3f-4da2-9c7c-b3de9b58f970`. Disk and loaded builds match the final
hashes above. The extraction correction passed the native retest:

- A real RSASS headshot killed a USEC enemy; a subsequent real enemy kill of
  the player respawned them in the BEAR south box. The original profile stayed
  USEC. Standing at south-05 for over 15 seconds no longer ended the match.
- After the replacement body, the monitor claim remained active. The final
  screenshot and read-only runtime frame agree: **BEAR 2 / USEC 3**, both teams
  with two alive, countdown **03:17**. The BEAR HUD accent is red. Parent
  independently inspected this screenshot and the runtime values.
- Positive control: a normal PMC Equator raid with no bots, inactive Arena
  session and no frozen choice, extracted normally from the identical south-05
  position after approximately eight seconds. The native result was
  RunThrough / EARLY EXTRACTION at 00:00:23, without a leave/kill hook.
- Arena could still be left through the normal menu, with monitor and side
  selection cleanup. Test settings and hooks are restored after the runs.

Parent compared all 227 saved Arena configuration entries after the final
control: no differences. The final merged warning/error export contains no
ArenaMode/Redeploy/Killcam source warnings or errors. Native material, weather
and Unity diagnostics remain subject to the limits above. The PMC result screen
showed a BEAR emblem, but a subsequent direct profile read still reported USEC;
the cosmetic result graphic is not evidence of a changed profile faction.

Evidence includes `retest-loaded-status.json`, `retest-after-respawn.json`,
`retest-south05-still-in-raid.png`, `bear-retest-monitors-live-after-respawn.png`
and `pmc-positive-extraction.png`. Native coverage does not claim every port
map, every tracer/laser variant, secret extraction or Customs respawn; those
broader paths have the stated production-linked offline coverage.
