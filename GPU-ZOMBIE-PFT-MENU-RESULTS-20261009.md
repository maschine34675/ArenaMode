# Zombie PFT map selection and wave smoke test — 2026-10-09

## Build and scope

- GPU host: SPT-GPU-2, dedicated profile SPT-GPU-2. Four user-created companions
  test1/test2/test3/test4 invited through My Squad's native context menus.
- Fix: `ecc22d532124fd20b5dbd8c769dc7716f95428ac`.
- Client ProductVersion `0.1.0+ecc22d532124fd20b5dbd8c769dc7716f95428ac`, SHA256
  `C2A295B57BB5A9DF86732B1F03FCD02015A23EB72E0F6C5FF42599D5A4B83CA1`.
- Server unchanged from `4ece234`, SHA256
  `1DEBA5FC55E382DDB5F0BD987B1C8C4FD57BB7F40D28E9B1F5DF19ECA45EC048`.
- Client deployed locally and to GPU2. Restart verified 21/21 compared plugins
  identical on development disk, GPU disk and loaded process; Redeploy Core also
  identical. UiBridge `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a` unchanged.
- Staged build: `artifacts/double-jump-build/1c55914a6f944943bcd0dd18e3ec8802/`:
  zero errors, four existing CS8632 warnings. No server rebuild needed.

## Observed in the actual game

1. Before the update, four invited companions plus Zombies reproduced the defect:
   clicking Factory checked its tile but Ready, Next and the time controls were
   absent. The runtime was the previous deployed `4ece234` client.
2. After restart with `ecc22d5`, the same native group could select Zombies and
   Factory. Ready/Next and both time toggles were visible. Day -> night -> day
   changed the actual checked toggle and preserved Next. Ready on this grouped
   map screen remained natively disabled; Next reached confirmation, where Ready
   was enabled and successfully started the raid. The displayed area summary was
   `5-player squad`, equipment `PMC entry gear`, respawn `Next wave`.
3. Factory day loaded with the authenticated four-companion roster. At 10:58:58.833
   local time the log confirmed `exact five-player team ready`; all four followers
   were Active, ordinary enemy PMCs absent, infected spawned and attacked.
4. At 10:59:11 the UiBridge kill hook killed only the local PMC. Arena held respawn,
   retained the squad and started the ally spectator. The screenshot showed
   companion health, team marker, ongoing combat and companion kills. Escape
   opened Disconnect/Resume; Resume returned to observation.
5. One follower died in wave 1. The remaining three cleared all 56 infected.
   At 11:01:12.836 wave 1 ended; the local PMC respawned with full health and entry
   gear. All four followers were replaced, including survivors; their new runtime
   IDs differed from wave 1. At 11:01:15.321 the full squad was ready, and wave 2
   spawned infected normally. No immediate local respawn or team no-contest.
6. At 11:01:52 Disconnect ended the test normally in wave 2. The native result
   flow returned to MainMenu; the XP page was skipped. No healing was purchased.

Times above are GPU local time, Europe/Berlin (UTC+02:00).

## Persistence, cleanup and limitations

After the delayed-finalization window, all twelve files in this test profile's
PFT teammate directory were byte-identical to the pretest copies. The complete
player JSON differed only at `characters.pmc.Health.UpdateTime` and
`characters.pmc.Hideout.sptUpdateLastRunTimestamp`; inventory and progression were
unchanged. Server log confirmed the protected post-raid snapshot restore and
suppression of PFT death/escape writes. No profile or teammate files were restored
manually. Arena config values did not change; the original bytes were restored
after closing EFT. GPU2 EFT is closed, its SPT server remains running.

The run was not completely log-clean:

- One infectedAssault request returned no profiles during wave 2; other arrivals
  continued. No associated match abort was observed.
- A deliberate RespawnApiProbe subscriber throws during replacement; Redeploy
  logged it and continued to the successful new PMC and squad. This is an existing
  test plugin, not a Zombie failure.
- The normal Disconnect produced one `Arena rejected the physical PitFireTeam
  roster` error for a disappearing follower. Source review confirms an existing
  shared Tactical/PFT teardown false alarm: native Stop sets Stopping and PFT
  clears its wrappers before Arena's general observer stops. Only its diagnostic
  latch changes; persistence suppression remains active. A separate follow-up
  should skip physical roster observation for Stopping/Stopped of the exact
  bound game, without relaxing live identity or persistence checks.
- Existing Harmony startup warnings and the Factory Woods-transit negative-scale
  BoxCollider warning were also present.

This certifies Factory menu/time selection, initial protected squad activation,
combat after PMC death, spectator/Escape, one dead-PMC wave transition and protected
exit. It does not certify team wipe, a surviving-PMC wave transition, repeated
rebuilds, every map, killcam ownership or ordinary/Tactical raid regressions in
the engine. Offline native-hook tests cover map and mode guard regressions.

Ignored evidence: `artifacts/zombie-pft-menu-20261009/` contains pre/post profile,
companion hashes, original config, profile-difference paths and the complete
UiBridge log export (`native-log.json`, 3619 lines, one pretruncated long line).

Parent offline reruns: native area hook 169, area view 29, real BepInEx area config
221, mode selection 157 assertions; independent adversarial review found no new
P0-P2 issue. The new native-hook case was demonstrated failing before the fix.
