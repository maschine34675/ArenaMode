# Solo Zombie survival and Factory courtyard — 2026-10-09

## Changes and delivery

- `e4ba7c3`: exclude the complete five-marker courtyard group from Arena bot
  starts on Factory day/night; protected Tactical/PFT and Zombie/PFT leaders use
  the same filter because companions spawn beside them. Ordinary and solo player
  starts remain unchanged. Existing five underground exclusions remain.
- `92f3f4b`: solo Zombie has one life, with either equipment choice. Death freezes
  score/waves/refills, retains interception through any selected killcam and ends
  via the exact native owner. Late infected requests/arrivals cannot reopen play.
  The final death still appears once in the killfeed. PFT wave restoration remains
  separate; the menu says `RESPAWN: None` for solo and `Next wave` for a squad.
- `2db0f4f`: native testing found the result panel obscuring the killcam. Defer only
  its automatic opening until replay ownership is released, keeping all terminal
  guards immediate. No GUI-time camera polling.
- Final client ProductVersion `0.1.0+2db0f4fe087509de475ae6416db95a3eddadf733`,
  SHA256 `AD4AC28B9F29FD5E2268D183E09988AB463D71F2857EF876EE5C342FBE46D883`.
- Server ProductVersion `0.1.0+e4ba7c3cbf8adb7fe36d60bee18094d6b92ff09f`,
  SHA256 `FEF1AD1DDBB40DCD68F3389E0B796053472134EB0934F0CC361839A49B8AB93A`.
- Both installed locally and on GPU2; server restarted gracefully. Final client
  restart reports all 21 compared plugins identical on development disk, GPU disk
  and in the running process. Core/BigBrain/PFT/assets unchanged. UiBridge remains
  `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`.
- Staged client `artifacts/double-jump-build/0e918f0b0d6348c687530f53557e4754/`:
  zero errors, four existing CS8632 warnings. Server zero errors, existing CS8620
  and MUD0002 warnings. No push.

## Factory marker evidence

The user's outlined pocket beside Courtyard Gate / Woods transit contains five
native player-marker records, not only the previously excluded one. Installed
day/night databases contain identical IDs/positions. A GPU2 scene inventory on
the preceding build found exactly those five records in X 7..36 / Z 43..67;
Woods transit was at `(23.7, 0.8, 61.6)`. See `FACTORY-SPAWNS-REVIEW.md` for exact
IDs, names and coordinates. The four additions lie within about 6.6 m of the
user's approximate X 13.10 / Z 59.34. No radius exclusion or scene mutation was
added. All other 135 of 145 day/night records remain available.

This scene inventory confirms marker identity, not native acceptance of a newly
spawned protected PFT squad in the final build. The final filter and day/night
isolation are covered by the installed-database/native-hook harness below.

## Native solo runs on SPT-GPU-2

Dedicated profile `SPT-GPU-2`, no invited followers. Temporarily pinned wave size
and max alive to four, shooter share zero. Times below are local UTC+02:00.

1. Client `92f3f4b`, Factory day, own gear, replay off: match began 11:30:34.180.
   UiBridge's kill hook killed the local PMC at 11:30:47.080. One casualty,
   `solo survival ended; no respawn`, native protected `Killed` exit committed
   11:30:47.347. No replacement or subsequent wave; normal results returned to
   MainMenu without purchasing healing.
2. Same client, Factory day, provided Enforcer kit and three-second replay:
   raid-start kit swap completed 11:32:01.455. Health was reduced to one HP per
   part for a bounded real infected melee death. Death at 11:32:26.093; KillcamReplay
   captured 58 snapshots and Arena held the dead body. Native exit committed
   11:32:35.542 after replay ownership released. One death, no respawn. Screenshot
   exposed the large automatic result panel covering replay, fixed by `2db0f4f`.
3. Final client `2db0f4f`, Factory day, provided Vanguard kit and three-second
   replay: kit handover completed 11:38:09.327. With the same bounded-health
   setup, an infected killed the PMC at 11:38:43.580; KillcamReplay recorded 55
   snapshots. Screenshot `artifacts/solo-zombie-factory-20261009/final-replay.png`
   shows the unobscured native replay, with only the normal compact score HUD;
   no automatic result window. Native protected `Killed` exit committed
   11:38:53.050, again with one death and no replacement. Normal result navigation
   returned to MainMenu without healing. No new Arena error was logged.

The shared respawn driver logs `External raid end during arena respawn — standing
down` during the intentional terminal Stop. Source inspection shows this is its
existing unconditional warning for any intercepted-death exit, including an
already authorized StandDown; it does not indicate an attempted replacement.
Server logs confirm deferred raid-end notification followed by the protected
snapshot restore and skipped ordinary post-raid processing.
Other final-run warnings were the existing deliberately throwing
`RespawnApiProbe` subscriber, stale PerfectCulling sampler cleanup, Factory Woods
transit's negative-scale BoxCollider, and native animator teardown warnings.

## Verification and limitations

Parent baselines: 885 unit tests passed, two existing skips; Factory 1105 checks
and 14 compiled mutations; Solo 85 checks; PFT Zombies 59; killcam 170; native mode
menu 157; rendered menu 1182; TDM native exit 227 plus 15 faults; Give up 626;
announcer combat 194; actual extracted async burst methods 34 checks. Additional
leaf runs passed Solo 13, PFT 11, catalog five and burst nine compiled mutations.
Independent adversarial review resolved final-death presentation and terminal
replay-owner findings; no remaining actionable P0-P2 findings.

Full profile comparison after all three solo runs changed only `characters.pmc.Health.UpdateTime` and
`characters.pmc.Hideout.sptUpdateLastRunTimestamp` in the full profile comparison.
All twelve stored PFT teammate files were byte-identical. Six temporary test
setting operations were restored using their ledgers; after graceful EFT quit,
the original config bytes were restored too (only locale-formatted comments
differed). GPU2 EFT is closed; its SPT server remains running. No profile was
restored manually. No final-build native PFT wave transition, long endurance run, all-map
coverage or Factory-night raid is claimed here.

Ignored evidence and deployment backups: `artifacts/solo-zombie-factory-20261009/`.
