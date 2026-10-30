# Direct start: native host1 acceptance, 2026-10-28

Host: SPT-GPU-1 / `192.168.178.114`, `C:\SPT41`. Test profile: SPT-GPU-1. All times below are UTC. Evidence root: `artifacts/direct-start-native-20261028/host1/` (private raw profiles and authority records remain ignored artifacts).

**Final state, 17:57 UTC:** host1 is stopped and SPT41 synchronization intentionally remains paused after proven cross-host interference. The current profile has XP 610, level 1, zero payments/reward messages and 973,000 inventory RUB. The previously earned 2,200 XP / 22,000 RUB were lost from the live profile during later synchronization; they were not manually restored or repaid. The earned copies remain preserved. After narrowly archiving conflicting metadata and one duplicate server DLL, two normal server starts completed automatic closure without payout; the repeat left the complete profile unchanged. Current evidence is in `rollback-1725/safe-cleanup/`; earlier successful captures remain intact.

**Native result:** six real Factory rounds across five directly started Arena sessions completed without a round-end hang. All five sessions returned to normal SPT and have activation, closure and completion proofs. A reward-startup defect reproduced on the initial server candidate; the corrected server subsequently paid the two eligible Deathmatch runs exactly once. A further normal-server startup left the entire profile unchanged. Those results remain valid, but later synchronization did not preserve the earned progression.

## Native scenarios

Each entry used the installed `maschine-ArenaMode.Session.exe start --profile <test-profile>`, with EFT closed. The helper saved/stopped normal SPT and started the private server/game. `gpu-run start` was not substituted for direct entry. Each return used the native **RETURN TO SPT / CONFIRM RESTART** button and reached a new normal MainMenu instance.

| Scenario | Native completion | Gameplay evidence | Return / reward result |
|---|---|---|---|
| DM ON, extra timer case | 16:17:17; 120-second timer | No local kill; not counted as positive payout coverage | Completed and restored; zero-value result |
| DM ON | 16:23:07; one-kill target | Snofielf killed with AKS-74; 83 observed shots including earlier misses; 1 kill, 0 deaths | Initial return withheld payment with `invalid-startup-state`; later legitimately recovered once |
| DM OFF | 16:32:13; one-kill target | MP-153, 4 observed shots; 1 kill, 0 deaths | Zero payment; normal return |
| Domination, underlying consent ON | 16:41:51; point limit | AKS-74 native kill observed; occupied A; BEAR 26 : 100 USEC defeat | Zero payment by current Domination policy; normal return |
| Domination, underlying consent OFF | 16:52:28; point limit | MP-153, 7 observed shots, WillDaPope killed; occupied A; BEAR 100 : 0 USEC victory at 1:57 | Zero payment; normal return |
| DM ON, reward fix | 17:01:29; one-kill target | SR-25, 4 observed shots; Tyrian killed; 1 kill, 0 deaths | Fresh run and earlier eligible DM run each paid 11,000 RUB + 1,100 XP |

Deathmatch used one round, one kill and a 300-second fallback (the extra first case used 120 seconds). Domination used 100 points / 180 seconds. These are supported configuration ranges, applied in memory. The normal native score/timer logic ended each match; no quit, arbitrary method invocation, fabricated kill, receipt or payout ended a scenario.

Test assistance was explicit: UiBridge godmode, infinite ammo with condition preservation, validated teleport near bots/control points, aim tracking and native trigger presses. Shots and deaths were observed by UiBridge. These runs establish native lifecycle/profile/reward behavior, not unaided gameplay balance or complete bot-navigation quality.

Domination deliberately exposes `N/A` and “Domination prototype: no rewards.” Its signed session states prove underlying consent ON/revision 2 and OFF/revision 3, respectively. Positive Domination payout is not supported by the current policy. Factory points varied between the two sessions: A/B changed; C was shared. Ported Arena maps were unavailable because this host has no detected Arena installation, so coverage is Factory only.

## Full profile evidence

For every session, the normal profile was captured after the normal writer saved/stopped and before gameplay. Raw native start/end backups and all available reward checkpoints are retained under each scenario's `end/` directory. Comparisons inspect the whole JSON before classifying differences.

| Comparison | Result |
|---|---|
| DM ON native end vs persisted checkpoint | Exact full-profile fingerprint `d073fcf2b0f1d3108829f6586c3a61da4e7ba01bfcd6093c9392a6919008817f` |
| Fixed DM ON native end vs persisted checkpoint | Exact full-profile fingerprint `d3ae2aa9e2ab4780f7512131d12c40d32f5820a75d1f4bff9fafe73669b2dd37` |
| DM OFF and Domination ON native start/end backups | Only `characters.pmc.Hideout.sptUpdateLastRunTimestamp` differs; the native start backup precedes snapshot preprocessing. The protected-end runtime also passed its full expected/disk/live restoration readback before logging completion |
| Domination OFF native start/end backups | Exactly equal, zero differences |
| Initial DM ON normal before/after return | Two normal timestamps; existing dogtag and scabbard items each gained the default `upd.StackObjectsCount = 1`. No item added/removed. These normalization changes are retained explicitly, not filtered out |
| DM OFF and both Domination normal returns | Only health and hideout timestamps differ |
| Fixed DM ON normal return | XP, two timestamps, new reward dialogue, two permanent payment entries; no other profile differences |
| First corrected-server startup, before the fixed DM baseline | Only the internal `com.maschine.ArenaMode.rewards.recoveries` marker was added; no gameplay/profile progression changed |
| Subsequent normal startup, final server | Whole profile exactly equal, zero differences |
| Initial check after Syncthing restoration, 17:12:48 | Whole profile still exactly equal to the verified post-restart profile at that time; later replaced by external synchronization at 17:23:52 |

OFF/prototype modes do not publish reward checkpoints. Their backup comparisons and protected-end readback logs are the evidence; a nonexistent independent persisted checkpoint is not claimed.

The paid receipt keys were `e1c6fa98ad8d4a868315a949dfb79122:9` and `f3b2ad365d5b467fb02dd02eb9ef74bd:10`, matching the two actual completed eligible DM run IDs. Each paid 11,000 RUB and 1,100 XP. XP rose from 610 to 2,810; the subsequent native login recalculated level 1 to 2. At the verified capture at 17:12:48, inventory RUB remained 973,000 and 22,000 RUB were attached to two unclaimed reward messages. Those payments/messages and earned XP were subsequently replaced by external synchronization. `payment-reconciliation.json` and `final-restart/diff.json` preserve the historical checks. Other original profiles were byte-identical at that initial cleanup.

The initial failure was preserved, reported and investigated against captured ordinary metadata; no original profile, ledger, authority or receipt was manually repaired or replaced. The reward fix recovered valid later results while retaining the old closed decisions. See the root investigation/report for the source change.

## Candidate boundaries and cleanup

| Component / coverage | SHA-256 |
|---|---|
| Helper used for all host1 native direct entries/returns | `2D8C7321367A4464A2DF08833A8080F53037EAFADE6214E2738E6B7B12EA9C09` |
| Initial server, four-combination matrix | `19FDE5869D95CEDFD300A090748613C05F3B61CC53873B7E50801680E679052A` |
| Reward-fix server, positive DM + return | `CA15D61426CF11D3FB17F33AB12DCC8CFAB05523E94ED9FC0A4524354BA56918` |
| Final combined server, subsequent ordinary startup / no duplicate | `2262F02036AA20F93FC87CF9222BE1C27A6007E4E7C0BF5C9B7ED59AE185363B` |
| Final helper installed after host1 testing; host1 disk-only coverage | `D2CF68D3E54957A5E827426CFFB1B15B96B927B1991C32C191409A6601A2A5B5` |

The helper/server replacements were hash-verified and backed up with owned processes stopped. Client runtime status reported 16/16 matching machine plugins; server candidates were verified before cold start and, for the reward retest, in the copied private tree. Host1 does not claim a loaded-server MVID measurement. The final helper's native rights-transition coverage belongs to host2, not this matrix.

Root source commits: `6f154cd` (reward startup) and `c6db9c3` (process identity / mixed privilege robustness). `final-hashes-after-sync.json` confirms the final installed binaries still match after synchronization resumed.

Original profiles, reward metadata and complete configuration were backed up before tests. SPT41 Syncthing folder `m3opk-xr6am` was paused at 16:09:37 and initially restored to its original `paused=false` state at 17:10:57; other folders were untouched. Initial cleanup restored original Reward ON consent (revision 4), verified ArenaMode config byte-identical to the backup, and checked all 220 backed-up configuration files recursively. Remaining byte differences in other configs were generated comments/order only; actual setting values matched. Earned progression remained intact at 17:12:48, but the later synchronization replaced it. Synchronization was paused again at 17:26:44 and remains paused under the user's authorization and revised acceptance contract C5.

All five sessions have lease/activation/closed/completion records. `cleanup-proof.json`, `final-installed.json`, `sync-restored.json`, `other-profiles-unchanged.json` and `final-process-status.txt` record the initial cleanup. The subsequent follow-up below has separate final process, profile and synchronization evidence.

## Review passes

1. Completion: all four requested combinations, real normal native ends and returns; extra timer run and fixed positive retest retained separately.
2. Data: strict full-profile ON checkpoints, unmasked OFF/prototype backup differences, exact receipt-to-run reconciliation and an unchanged full-profile repeat startup.
3. Safety/cleanup: no profile overwrite or reward injection; initial settings/consent restoration retained, owned processes stopped, synchronization deliberately paused after interference, final binary boundaries explicit. Only the 22 recognized metadata copies and one exact duplicate DLL were archived outside the installation with canonical-file guards.
4. Reporting: initial reward failure, failed mod-loading startup and external progression rollback retained; Domination no-reward policy, assistance, Factory-only scope and final-helper disk-only host1 evidence are explicit. Raw private evidence stays outside committed documentation.

## Reopened synchronization follow-up

The verified post-restart profile had XP 2,810, level 2, two payments totaling 22,000 RUB / 2,200 XP and two reward messages. `final-restart/after-sync.summary.json` captured it at **17:12:48.258**, file SHA-256 `66A35B959FD2254C8CB3AFF0FC207724304040D048704280EBDE9C29FD3A0B77`; `after-sync-diff.json` established exact equality. The check at 17:16:13 concerned installed binary hashes, not another profile capture.

Root's subsequent cross-host audit found all three live copies replaced. Host1 independently captured the replacement at 17:27:03: file SHA-256 `FC7AB5911D663670FCD16C4C04A880B86C78C43E1B356B0DD8C4CA625A9DFA4C`, 172,183 bytes, last-write timestamp `2026-10-28T17:12:28.1961109Z`, XP 610, level 1, zero payments and reward messages. The modification timestamp is not the time it arrived on host1 and does not by itself identify the writer. Root correlates the host2 writer and cross-host synchronization; host1's read-only daemon/database/event capture is retained for source validation.

Host1's Syncthing event stream establishes the actual replacement: `ItemStarted` at **17:23:52.6321697**, `ItemFinished` at **17:23:52.6631344**, exact live profile path, action `update`, no error. Its disk stream records the remote modification at **17:23:53.6334321** with `modifiedBy=LJEZTQP`. Host1's configured remote peer is SPT-VM (`6CUZOLD`); the originating device ID therefore needs the cross-host mapping, not inference from the immediate connection. The paused folder's file-database endpoint returned HTTP 500; it was not resumed for diagnosis.

The earned profile survives in three Syncthing conflict files ending `182348-LJEZTQP`, `182349-LJEZTQP` and `182352-LJEZTQP`, each byte-identical to the verified `66A35B...` profile. All 279 conflict files found in profiles/reward metadata were inventoried and copied into `rollback-1725/conflicts/`. `selected-profile-conflicts.json` identifies the three exact matching earned copies. They have not been copied over the live profile.

## Safe cleanup and final ordinary-start verification

SPT41 synchronization remained paused throughout cleanup. The reviewed archive script first previewed then moved exactly 22 recognized reward-metadata conflict copies to `C:\Temp\ArenaMode-sync-conflicts-20261028-host1` at 17:47:56. Hash guards proved all 608 canonical metadata files and three canonical profiles unchanged. Profile conflict backups were excluded. `safe-cleanup/archive-preview.json`, `archive-applied.json` and `archive-copy/` preserve the manifest and readback. The owned script only adapted path handling for host1's Windows PowerShell 5; the reviewed selection and guards were unchanged.

The pre-start live profile was byte-identical to the frozen rollback capture: file SHA-256 `FC7AB591...`; canonical-JSON SHA-256 `d2f8120e...`. These are different hash representations of the same file, not different profile versions (`pre-start-raw-hashes.json`, `pre-start-diff.json`).

The first normal startup at 17:48 failed to load ArenaMode: ModLoader reported `Assembly with same name is already loaded`. This is explicitly **not** successful candidate-loaded coverage. The profile stayed exactly unchanged. The cause was `user/mods/ArenaMode/maschine-ArenaMode.Server.sync-conflict-20261028-181231-W7YIU3M.dll`, an exact old CA15 candidate alongside canonical 2262. After graceful server stop, fresh paused/process guards and root authorization, only that file was archived at 17:51:42 to the archive's `binary-conflicts/` directory. Canonical DLL and profile hashes were unchanged (`binary-archive-applied.json`). No broad deletion or profile restoration occurred.

The retry loaded ArenaMode at 17:52:05 and logged two `ClosedWithoutPayment` decisions at 17:52:09. The complete profile comparison has exactly one added path, `/com.maschine.ArenaMode.rewards/recoveries/1`; no XP, payment, mail or inventory change occurred. The server was stopped gracefully. A second normal startup loaded ArenaMode at 17:53:09 and repeated the safe closure decisions at 17:53:13. Its full profile was byte-identical to the retry profile, proving no repeated mutation or payout. Evidence: `retry-diff.json`, `repeat-diff.json`, `after-retry.profile.json`, `after-repeat.profile.json`, both startup logs and `*-reward-lines.txt`. EFT was not started during this cleanup.

The repeated server was stopped with Ctrl-C. `safe-cleanup/final-state.json`, captured at **17:57:15**, confirms no EFT, launcher, server or helper process, SPT41 `paused=true`, canonical server 2262 and helper D2CF, no conflict DLL anywhere under BepInEx or `user/mods`, and all three earned profile conflict backups still byte-identical to `66A35B...`. The live profile after stop has file SHA-256 `508028538E30AA888FF62B6D8195B306701FBD829247C2A1DB5E553CBEF772DF`, unchanged from the repeat capture. `final-metadata/` and `server-after-stop.log` preserve the resulting ordinary state.

G2/G3 are complete for the revised acceptance contract: all differences and the external loss are accounted for, cleanup is idempotent, and the installation is safely stopped with synchronization explicitly paused. This does **not** claim that earned progression was restored or that simultaneous profile synchronization is safe. No live profile was overwritten and no old reward was repaid.
