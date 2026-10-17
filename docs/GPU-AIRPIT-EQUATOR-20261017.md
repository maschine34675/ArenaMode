# AirPit admission and Equator gates — delivery and native checks

2026-10-17. Source commits `feb8567` (AirPit), `983b774` (Equator gates) and `ab3506b` (native ordering correction).
Feature contracts: [AirPit](AIRPIT-INTEGRATION-20261017.md) and
[Equator gates](EQUATOR-SPAWN-GATES-20261017.md).

## Offline evidence

- Server suite: 1,017 passed, one opt-in APBS check skipped.
- AirPit: 120 filtered server tests; menu 711; real BepInEx migration 275;
  preview/presentation 4,446; PFT startup 260; coverless combat 177;
  two compiled omission controls rejected.
- Spawn gates: 88 checks and 16 compiled faults; original Equator formations:
  100 checks and 11 compiled faults.
- Full TDM runtime: 340 checks, 29 faults. Extracted burst integration:
  93 checks, 17 faults. Give Up: 626 checks, 16 faults.
- Root independently reran both feature gates against the integrated source.
  A separate adversarial reviewer found no remaining concrete issue after the
  captured-owner, committed-Stop and booth-extraction corrections.
- A pre-existing burst-harness logger double lacked `LogDebug`; the double now
  matches the already-existing production logger use. No logging behavior in
  the game was changed for this harness repair.

Logs: ignored `artifacts/airpit-gates-20261017/` and the feature harnesses'
`artifacts/` directories. Tests use deployment overrides and do not substitute
for real Unity animation, navigation or bot behavior.

## Build and delivery

Initial staged build used clean source `983b774c7c3c58960d33329162830acd2f6ff4b7`:
`artifacts/area-map-delivery/e201937f18074c5a915a1cdb2927872d/manifest.json`.
Client/server and package verification passed; 89 installed files remained
unchanged during staging. Existing server nullable/MudBlazor warnings remain.

| Artifact | SHA256 |
|---|---|
| ArenaMode.Client | `6ADDB9957B7F9CD9F1B54DB6E1A8C2679989B1F74722896C901ADCE8D93BA75E` |
| ArenaMode.Server | `EFBB3C2A99AF02B5F9A9FA59D31CFBBC74A92BCE40247781E17F7C425018ADBB` |

Only these two DLLs were copied to the local installation. The host comparison
confirmed both identical on SPT-GPU-2. The initially requested restart recipe
correctly refused because EFT had already been shut down to unlock the files;
the subsequent ordinary `gpu-run start` is recorded separately. No forced
takeover, MapPort change, bundle replacement or foreign-mod deployment occurred.

## Native verification

Completed on SPT-GPU-2 with corrected client `ab3506b` and the server from
`983b774`: two Equator 3v3 raids and one AirPit Deathmatch. Gate hold/opening,
both teams leaving their booths, local respawn, extraction deferral and protected
exit passed. AirPit filled seven AI slots and respawned the player. The first
failed build and its recovery are retained below, separately from acceptance.

The host was free and idle before acquisition. The authoritative before-test
snapshot is `artifacts/airpit-gates-20261017/native-owned-baseline/`; the earlier
`native-baseline/` predates the end of the other session and is not used to
attribute profile/configuration changes to this run. The baseline records
137 local/host DLLs, the test profile, configuration, and twelve PFT files.
DeveloperMode is temporarily pinned by a restoration ledger.

## Coverage limits

The scope adds generic AirPit placement, not its original northern team box,
new pickup locations or new map bundles. Equator Tactical and PFT Zombies
remain excluded. The offline admission matrix does not certify every map/mode,
PFT roster size, long match or hardware configuration in native play.

### First native run: lifecycle defect, not accepted

Fresh process `b6f5925a`, log `LogOutput-SPT-GPU-2-2026-10-17_12-33-26.log`:
Air Pit is visible in gold at its native menu position. Equator Team DM at six
participants acquired the hold at 12:35:47; MapPort installed 11 carvers and
reported all nine evaluated doorways sealed. At 12:36:01 Arena incorrectly
released the gates as "start not admitted" immediately after native Run
completed, before GameWorld.OnGameStarted confirms Arena. The release log is
emitted after the one-second slide. Capacity therefore saw IsHeld=false with
carvers still present and rejected all 10 USEC starts. The error is lifecycle
ordering, not evidence that booth geometry should be relaxed.

The scheduled native abort reached Stopping but did not complete its results
transition. Pause-menu Disconnect also did not complete. This failed start is
recorded explicitly; no successful Equator match is claimed for build 983b774.
The correction must retain the hold across Run completion and clean up rejected
admission only after the real start event. Its regression must use that sequence.

Baseline caveat: the prior MapPort test owner changed the live developer hold
from true to false and its seconds from 60 to 30 at 11:31:54/55 (previous-process
UiBridge action log). Those pre-existing live values were saved only after our
12:30 file snapshot. Preserve false/30; restoring the stale file snapshot would
re-enable the other test's hold. No Arena test changed those MapPort settings.

### Corrected source and protected recovery

Commit `ab3506b` removes success cleanup from the Run observer and subscribes a
later exact-owner RaidStarted rejection observer. Root and independent review
found no open correction defect; the regression explicitly detects the old
Run-before-event failure. Spawn-gate harness: 100 checks / 19 negative controls;
formation harness: 100 / 11. The staged client manifest is
`artifacts/double-jump-build/b867a4cedc6641dbbd788d0fba96200b/manifest.json`, SHA256
`8520E6264BC69A5EE65EFA3E4AE5A75448AFFF8DC94F0301DA421C63E6C50985`.
Tracked source was clean; only this untracked report was present. Server build
remains the preceding verified hash; no server restart for this client fix.

The failed game's status was confirmed Stopping. Client-only graceful quit was
confirmed at 12:44; the server stayed alive because the protected snapshot is
in memory. The next process must reconcile its abandoned exact raid before any
new arm. No raw profile was restored or overwritten by the test tools.

Independent profile audit found 314 inventory IDs before/after, no additions
or deletions. The apparent stack deltas are native ProfileFixer normalization
(missing count to 1 on the dogtag and bayonet), logged at 12:33:46 before Arena's
12:35:45 snapshot. Health 100/100 likewise predates the snapshot. The native
backup `backups/2026-10-17_10-35-45/` differs from the inspected profile only in
three session skill fatigue counters reset by native StartLocalRaid and the
hideout update timestamp; skill progress is unchanged.

Separate follow-up, not corrected or certified here: the startup ShowAction's
FadeBlackScreen may replace Stop's pending black-screen coroutine and cancel
its GameEnd callback. This source-supported hypothesis matches the failed
start's Stopping state but still needs its own reproduction and scoped fix.

Recovery verified before the next arm: server at **12:48:38.952** logged
`Profile snapshot restored`; at **12:48:38.977** protected end completed and the
snapshot was persisted, skipping ordinary post-raid processing. Only afterward,
at **12:48:38.994–995**, the next Equator arm reserved its boundary and captured
its new snapshot. Corrected process `0071a95e` (started12:47:22) reports all19
maschine plugins identical across local build, host file and loaded process.

### Corrected Equator run 1 — start, extraction deferral and protected exit

In `0071a95e`, the second Equator 3v3 started successfully:

- 12:48:53.654: USEC **10/10**, BEAR **10/10** safe connected booth positions.
- 12:49:06.219: test player moved into the original north extraction trigger.
  Screenshot `equator-gates-closed.png` shows closed booth doors, assembling
  teams, zero match timer and disabled damage/scoring.
- 12:49:12.764: Assembling → Countdown, with the full roster present.
- 12:49:16.765: release finished its one-second slide; 15 parts off, 11 carvers
  removed. At 12:49:17.265 all nine evaluated doorway paths were complete.
- 12:49:17.772: all twenty formation-to-atrium paths revalidated, then Live.
- The player remained in the exit for **over eleven seconds before Live**,
  longer than the native eight-second timer, without extracting during the hold.
  The fresh native timer after opening completed normally; raid ended12:49:26.876.
- Server restored/persisted the snapshot at12:49:26.952–975. No ordinary post-raid
  processing ran. This is a successful protected extraction, not a combat test.

### Corrected Equator run 2 — departure and respawn

Same fresh process, another 3v3. Both formations prepared and the gate sequence
repeated correctly; Live started **12:51:12.512**. Real bot snapshots then showed
USEC outside the north box at (5.7,0.1,37.3) and BEAR outside the south box at
(-2.8,0.1,-29.5)/(5.4,0.1,-30). A later snapshot placed both USEC in the central
map around z0–4 and BEAR around z-2 to-23: both teams left their booths.

A controlled local death exercised killcam and actual respawn. At12:51:36.147–220
the player was assigned and verified at `arena-equator-tdm-v1-north-04`,
(-2.93,0.08,48.59). Match remained Live, death count1, gates visibly still open
(`equator-gates-open.png`). No second hold or release occurred on that respawn.
Godmode was briefly used for observation and disabled before the controlled
kill. This is functional acceptance, not a performance measurement.

### Air Pit — native Deathmatch smoke test

The current gpu-run raid recipe's separate catalog does not know AIR PIT yet and
refused before starting a raid. No foreign tool code was edited; the test used
UiBridge-observed native menu buttons instead. Gold AIR PIT, Deathmatch, eight
participants, and the normal NEXT/READY sequence were visible and usable.

At12:53:51 the client admitted `arena_airpit` Deathmatch. Seven real active bots
filled the seven reserved AI slots; observations showed movement on the shipped
navigation and combat layers. Later native replacement arrivals refilled deaths
while the alive count remained seven. The local test death at12:54:23.832 took
0.27 seconds to respawn, arriving at(-16.9,0,-71.7) with a valid AK-103 kit and
death count1. No gate hold was acquired on AirPit. Screenshots: `airpit-menu.png`
and `airpit-raid.png` in the ignored evidence directory.

No new pickup positions or Arena lighting overrides were added; their existing
unsupported-map paths correctly report no layout/director for AirPit. This
smoke test covers Deathmatch admission/population/respawn; it does not certify
PFT squad behavior or every other mode. Server/offline checks cover those menu
and exact-raid adapter paths separately.

## Final cleanup and acceptance boundary

AirPit protected end restored/persisted the snapshot at12:54:51.494–525.
Subsequent ordinary PMC SelectLocation had no Arena mutators/mode controls and
restored the native MapPort labels (`ARENA: AIR PIT`, `ARENA: EQUATOR`,
`ARENA: BAY 5`). No ordinary raid was started. The scenario evidence is
`scenario-ordinary-menu-restoration-20261017-125527-host2` in gpu-run's temp tree.

Final corrected-process buffer: zero Error/Fatal entries, all43 plugins loaded,
19/19 maschine DLLs matching build/host/loaded identities. The normal test exits
worked; this does not resolve the separately documented earliest-start abort.

Restored DeveloperMode=false, GameMode=gungame and the prior TDM Customs area.
Godmode is off. EFT quit gracefully and was confirmed unreachable; SPT server
remains running. Final semantic config differences are only intended AirPit
revision3/default-map/participant entries, plus the already-explained prior
MapPort owner's saved developer values. None of those foreign values was reset.

Across **137** local/host DLLs, only the two Arena DLLs at their two destinations
changed. All **12** PFT files are unchanged. Final profile compared to the native
pre-raid backup has **314/314 items** and exactly **one differing field**: the
normal hideout update timestamp. Inventory, health, skills, quests and other
profile content match; `verify-final-profile.mjs` rejects any other difference.
No raw profile write, MapPort/source/bundle replacement, publication or push.

Remaining native coverage: all other AirPit modes/PFT rosters, Equator10v10,
long replacement cycles and the south booth's extraction trigger. The current
implementation/short acceptance does not claim those stress cases.

