# Mixed native release checks — 29 October 2026

ArenaMode 0.3.0, Redeploy 1.0.2 / Core 1.0.17 and ArenaMapPort 0.4.0 were
exercised concurrently on SPT-GPU-1 and SPT-GPU-2. Both hosts were free before
testing; synchronization was paused and original configuration/profile files
were backed up. These are representative checks with the installed mod stacks,
not exhaustive mode or third-party compatibility certification.

## Native coverage

| Host | Scenario | Observed result |
|---|---|---|
| GPU-1 | Direct Session.exe entry and return | Private server/menu and normal-session return reached. |
| GPU-1 | Factory day TDM | 5v5, 120-second result, death/respawn, movement, native results/menu. |
| GPU-1 | Factory night Domination | Three randomized points, bot captures, integrated HUD, death/respawn, 120-second result. |
| GPU-1 | GunGame | Actual shot/kill changed tier 1 to 2 and weapon; death retained tier with the default setting. Controlled exit afterward. |
| GPU-1 | Deathmatch | Finite 60-second round ended naturally. |
| GPU-1 | Tactical | Buy/ready, death into round 2, Vanguard purchase and correct remaining balance/loadout. Controlled exit afterward. |
| GPU-1 | Zombies | Actual Mosin kill, natural wave 1 to 2/refill, then death/end. A separate own-gear run exercised the stuck-wave watchdog; that advance was not counted as a combat victory. |
| GPU-2 | Equator day/night | TDM, gates, movement, fighting bots, distinct lighting, 120-second results and menu returns. |
| GPU-2 | AirPit/Bowl | Original two-point Domination layouts, traversable routes, autonomous bot captures, natural 120-second results. |
| GPU-2 | Bay5 | Deathmatch, navigation and bots, natural 120-second end. Raw selection/consent show rewards ON; this is not the OFF regression proof. |
| GPU-2 | Explicit rewards OFF | UI and saved consent verified OFF, then Equator TDM with a fresh capture request, no refusal warning and natural 60-second end. |
| GPU-2 | Standalone Redeploy | Arena inactive; naked PMC respawn, return to corpse, original M4 recovered and fired, second death/give-up/native menu. |
| GPU-2 | PMC to own Scav | Native extraction/transfer, SELL ALL blocked without inventory/currency/mail mutation, partial transfer and deliberate remaining-loot forfeiture, committed clean journal and no duplicate item IDs. |

Test deaths used native damage/death paths, including the test host's controlled
death action; observed kills used actual shots. No reward/score completion was
fabricated. Full GunGame ladder wins, complete Tactical matches and long Zombie
survival were outside this sample.

## Corrections

- **Rewards off:** reward capture incorrectly reported `reward-snapshot-not-observed`
  although opting out intentionally creates no reward snapshot. Missing-snapshot
  handling now resolves authorization first. Authorized runs still reject missing
  evidence; accepted run retries remain idempotent after consent changes.
- **Helper logs:** per-link cleanup details require `ARENAMODE_SESSION_DEBUG=1`.
  Startup/return summaries, retries, failures and link-safety warnings stay visible.
  In the two dated direct-entry windows, Info lines fell from 152 to 17; all 135
  routine link-removal lines disappeared while lifecycle completion remained.
- **Redeploy logs:** nine successful UI/render/spawn/cooldown/guard details now use
  Debug. Protection, item/inventory decisions and failures retain their levels.
- **MapPort diagnostics:** spectator output readback runs only in DeveloperMode.
  Confirmed voices increased with `LastOutputPeak=0` in player mode; zero is no
  longer a measurement of audible output in that mode.
- **MapPort end callback:** a late Bowl physics collision requested an already
  unloaded `weaponsounds.bundle`. Remaining objects in owned scenes now skip only
  that late sound callback after the Scripts scene disappears, preserving the
  native collision timer. Foreign scenes and live missing-bank warnings remain.

## Remaining findings

One native `BotFirstAid.CG_ApplyToSelf.method_1` exception occurred 41 ms after
TDM Stop was committed. The source permits a medication callback after bot weapon
state disposal; the exact live null reference is unproven. No owned premature
disposal was identified. The raid still returned normally. This remains an open
teardown finding, not a suppressed or proven harmless error.

Two Equator shoe props explicitly use Arena's `Books` sound type, whose SPT impact
bank is absent. Their sound-only fallback warns once and retains physics. This
missing sound remains. Unity missing-script/LOD/animator/attachment warnings are
preserved and attributed only as far as the logs allow. No global logger filter
was changed. This sweep does not resolve the previously reported intermittent
audio noise or establish subjective audio quality.

## Builds and evidence

Final offline checks: **3,398 ArenaMode tests passed, 3 known environment skips;
469 Redeploy tests passed; 2,545 MapPort tests passed**, including 21 new actual
prefix/scene-lifetime cases. The logging harness passed. A first reward-check
ordering attempt failed the existing replay tests and was corrected before any
deployment. Redeploy's test logger stub was extended for Debug before its final
pass. Independent code review found no actionable regression in the final diffs.

Final candidate hashes (full values and matching symbols in the manifest):

| Artifact | SHA-256 prefix |
|---|---|
| Arena server | `8699E1993EB0FD73` |
| Session helper | `46A4D1E56E7885B5` |
| Redeploy client | `913B17649034D08E` |
| Redeploy Core | `8E650CF1E621F967` |
| MapPort client R2 | `D847AB2F3B1914A5` |

Raw native logs, actions, screenshots, loaded-module identities, TRX files,
deployment receipts and verification scripts are under the ignored local
`artifacts/release-sweep-20261029/`. `candidate-r2.json` is the final manifest;
the initial MapPort `F3AECA69…` candidate is retained as historical evidence.
Arena client/BigBrain are unchanged from the preceding UX candidate.

Final R2 rechecks passed on both hosts: GPU-1 direct entry, actual 5v5 TDM at
60 seconds with death/respawn, protected end and normal-session return; GPU-2
Bowl at 90 seconds, native menu and zero retained spectator resources, without
another soundbundle exception. A separate Equator developer run recorded 29
confirmed voices and peak 0.002342, confirming diagnostics still work when opted
in; player-mode Bowl recorded 46 confirmed voices with the probe off.

## Restoration

GPU-1 restored all 220 original configs byte-for-byte and retained all 75 original
profile/backup files. Native startup rotation had removed 36 original backup
files; only those missing files were restored under `profiles/backups`.
The played profile changed only health/hideout timestamps. Another profile's
single 60,738 RUB mail attachment expired at 10:22:58 UTC, before the test began
at 10:39:33 UTC; the exact native expiry was preserved. All remaining fields were
structurally identical. No active profile was manually repaired or rolled back.

GPU-2 restored 250 original config files. The disposable profile, its 15 backups,
completed Scav journal and only its own newly created reward artifacts were
hash-archived and removed from the active setup. The original isolated fixture
remains unchanged. Seventy-two historical backup files removed by native rotation
were restored only when missing. An independent full comparison of all three
ordinary profiles found only the same already-expired mail and the played
profile's two native timestamps; both PMC and Scav inventories were unchanged.

Both hosts returned to their original synchronization state after games, servers
and helpers were closed and the private-session active pointer was absent.

Synchronization catch-up created eight new conflict copies: two profile variants,
four caches and two ProgressiveBots logs. Each was hash-archived before removal;
historical conflicts were left intact. Both profile copies contain only the
GPU-2 test's later native time fields; inventories and other fields agree with
the active winner. No active profile was replaced. A fresh independent comparison
of the three ordinary profiles on all three installations, all 470 original host
configs and all eight conflict paths passed after propagation. Candidate hashes
also still match on the local installation and both hosts.
Final host probes recorded active, idle synchronization with no pending files or
pull errors: GPU-2 at 11:44:48 UTC and GPU-1 at 11:47:39 UTC. Games, servers and
helpers remained stopped.
