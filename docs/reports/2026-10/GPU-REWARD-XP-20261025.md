# XP rewards: GPU-2 native acceptance — 2026-10-25

Tested on the existing GPU-2 test profile, installed SPT 4.1.6 / EFT 0.16.9.5,
at 1920×1080. No result, kill, receipt, payout or XP value was injected.
Before deployment all 50 profile JSON files across development and GPU-2 were
backed up to ignored evidence. The pre-existing test profile had three real
`arena-v1` payments, 33,000 RUB in recorded rewards, 3,761 PMC XP and level 2.

## Loaded build

Release server/client were built, deployed locally and synchronized to GPU-2.
SPT and EFT restarted before testing. Final UiBridge status confirms all 16
maschine plugins and both libraries match build, host disk and loaded assemblies.

| DLL | SHA-256 |
| --- | --- |
| `maschine-ArenaMode.Client.dll` | `7E2D9A58830A09816042C7D182CDE76F1BD12F2F34D698DD9956BD5A68C48BCF` |
| `maschine-ArenaMode.Server.dll` | `41A67F786B31005B46251422B48FE26DB8CC3C8D74439475600FAD7A774DBAE7` |

The helper, gate and shared respawn libraries were unchanged. This is a development
build after 0.2.0, not a published release.

## Private match and transfer

The normal installation refuses Arena because of its existing unlisted
Manimal-LoadAmmoAnim plugin. The normal START ARENA SESSION flow opened a filtered
private server on port 6970; no whitelist or foreign mod was edited for this test.
The map card was ON, with the requested RUB/XP rates and caps fully visible in
three lines without clipping. An old paid receipt correctly displayed zero XP.

Five temporary in-memory match settings selected Deathmatch, all bots against
the player, one round and one kill to win. Factory day had four participants.
UiBridge godmode, infinite ammo, safe teleport and head tracking supported a
bounded combat test. The AKM fired 13 observed native shots, producing one
headshot kill and a victory. The server-frozen receipt had four actors, one death,
one local kill, one LocalTarget victory and 39,376 ms of observed round time.

Independent receipt inspection verified the HMAC, the `arena-v2` policy
(100 / 1,000 / 20,000 XP), and identical expected/persisted restoration revisions.
The normal profile retained its original XP, inventory money and three payments
through private combat; only a native Hideout update timestamp changed.
RETURN TO NORMAL SESSION stopped the private server and transferred the signed
receipt. Returning alone did not pay it while the normal server kept running.

## Startup, level and mail

After a clean normal-server restart, a read-only file watcher captured the payout
before the game login. The same profile replacement contained exactly one new
payment, one new mail and 1,100 additional PMC XP. At that point Level was still 2;
Arena had not rewritten it. Native login then displayed level **03**, and native
saving persisted level 3. Skills, quests and the scav profile were unchanged.

The System mail displayed:

> Arena reward: 11000 RUB. 1100 XP already credited to your PMC. Collect the RUB below.

Only that new mail was collected through GET → RECEIVE ALL → ACCEPT. Live
inventory rose by exactly 11,000 RUB. A second complete server/game restart after
collection reported `AlreadyPaid (reward-inbox-already-imported)`.

| Field | Before | After payout, before login | After collection and second restart |
| --- | ---: | ---: | ---: |
| PMC XP | 3,761 | 4,861 | 4,861 |
| PMC level | 2 | 2 | 3 |
| Permanent payment entries | 3 | 4 | 4 |
| Total recorded RUB | 33,000 | 44,000 | 44,000 |
| Total recorded XP | 0 | 1,100 | 1,100 |
| Inventory RUB | 486,221 | 486,221 | 497,221 |
| Reward mail entries | 3 | 4 | 4 |
| Uncollected reward RUB | 11,000 | 22,000 | 11,000 |

The three pre-update payment records remained exactly unchanged. Their original
signed receipts, allocations and decisions were accepted on each new startup,
without repricing, fingerprint drift or retroactive XP. The payment log appeared
once: `Arena reward: 11000 RUB delivered by mail; 1100 XP credited.`

## Cleanup and limits

- Normal MainMenu remains open against port 6969; no private server, raid or active
  test hook remains. No temporary test mods were installed.
- Arena configuration values match the pre-test snapshot; all five temporary
  settings disappeared at restart.
- Of the 48 unrelated profile-file copies, 44 remained byte-identical. Four
  copies (two profiles on both synchronized installations) received only an empty
  reward enrollment extension from the previously implemented default-on startup.
  Their existing profile fields were exactly unchanged.
- No ArenaMode warning/error appeared in the private combat session. The final
  normal-server run had no errors. No profile, journal or receipt was manually
  restored, edited or deleted to clean up the test.
- The first menu script clicked past the refused Arena choice into ordinary map
  selection; it was backed out without starting a raid. The initial eight-second
  bot check ran before asynchronous spawning completed; all three opponents then
  became active. Both runner misses are retained in evidence, not counted as passes.

This proves one private Deathmatch victory, legacy history, startup payout,
native level advancement, mail collection and repeated startup. Other modes and
resolutions rely on their unchanged capture path and synthetic tests. Modified XP
curves were source-reviewed, not exercised with a temporary progression mod.

Ignored evidence: `artifacts/reward-xp/gpu/`, including `private-receipt.json`,
`startup-before-login.*`, `after-replay-startup.*`, `acceptance-result.json`,
`native-level.png`, `mail-xp.png`, `final-loaded.json` and scenario reports.
