# Offline startup recovery: installation and native verification

Source: `967b675f`. Server and shipping .NET Framework helper were rebuilt from
that commit, archived and installed together locally and on GPU-2 on 2026-10-29.
Both report `0.3.0+967b675f12d947c255a9f62c57decc5c0d014549`.

| Component | SHA-256 |
|---|---|
| Server | `6882FA22A83AA26C5DA153A7DABBDD8BB3CCB1E1137D9C4F953B839FB0E9245D` |
| Session helper | `00A745032CCCE966799861183C1D07F51D0F212A43073CED7F76E746E930CEC9` |

The server rebuild had three existing compiler/analyzer warnings and no errors;
the helper built without warnings/errors. No client payload was rebuilt or
replaced. The earlier automated verification remains recorded in
[the recovery implementation note](OFFLINE-STARTUP-RECOVERY-20261028.md).

## Native coverage

GPU-2 used the independent `C:\ArenaMode-DirectNative-20261028` installation and
its dedicated test profile. The ordinary installation's helper state was moved
aside only after its previous closure and process absence were verified.

- **Missing entire private directory:** entered Arena through the shipping
  helper, exited EFT normally and waited for the watchdog to stop the private
  server. Archived the private directory, leaving valid original activation and
  ownership intact. The real normal SPT server started, emitted the short discard
  notice once and served port 6969. The entire ordinary profile was byte-identical.
- **Repeated normal startup:** stopped that server gracefully, started it again
  and verified the listener and complete profile again. No second discard notice
  or profile change occurred. The helper then rebuilt the private tree and opened
  a fresh Arena main menu.
- **Missing private marker:** exited that session and waited for private shutdown,
  then archived only `arena-session.json`. Direct helper startup recovered through
  ordinary SPT, discarded the old session and reached another Arena main menu.
  The entire ordinary profile was still byte-identical.
- **Healthy subsequent round and return:** completed a two-minute Factory
  Deathmatch, one round with two participants. Native result processing restored
  the protected profile and reached the result screens without hanging. RETURN
  TO SPT stopped the private server and restarted normal SPT/EFT; a new game
  process reached MainMenu. The Arena strip and
  return button were absent there. The healthy session has genuine closure and
  completion records, with its active pointer retired.

Both discarded sessions have only lease, activation, discard and discard-completion
records: no fabricated closure/completion. Their hashes were independently checked.
Actual consent reads OFF at revision 2 and no reward handoff exists. Both native
fault cases began with rewards OFF; native ON-to-OFF transition and late payout
replay remain covered by the automated tests, not these two native runs.

All seven complete profile captures across both fault cases and repeated startup
share one hash. After the final normal EFT login, the full comparison found only
`characters.pmc.Health.UpdateTime` and
`characters.pmc.Hideout.sptUpdateLastRunTimestamp` changed. Inventory, progression
and every other field were unchanged. The client logged a refused reward capture
(`reward-snapshot-not-observed`) with rewards OFF; server evidence separately
confirms the protected snapshot and its native restoration. This run is not a
new reward-payout acceptance claim.

The existing gpu-run menu recipe did not recognize the dynamic objectives
"Endless combat"/"Survive timer". Its guarded attempts stopped at map selection;
observed NEXT/READY controls completed the correctly configured round. No product
code or unrelated automation tooling was changed for this limitation.

## Cleanup and limits

Game, helper and test servers were stopped; normal server shutdown used Ctrl+C.
All 719 backed-up profile/reward/config files of GPU-2's ordinary installation
were unchanged. Its original helper state was restored and all 878 files compared.
Local profiles/config were unchanged while isolated. Syncthing's prior running
state was restored locally and on GPU-2. The completed independent fixture and
fault archives remain outside the synchronized installation for inspection.

GPU-1 was initially idle but another session started it during this work. Its
locked server assembly rejected the first exclusive-open check, before any copy;
this task did not stop it or deploy directly there. Subsequent normal file
synchronization delivered both candidate files to GPU-1, hash-verified at
07:18 UTC. This is not evidence of a process restart or loaded-version acceptance.
Local installation is disk-verified; native evidence is from GPU-2's isolated copy.
Server identity is bound by candidate hash, launch path/time, listener owner and
observed startup behavior, not an independently sampled managed-server MVID.

Ignored evidence and reproducible assertions:
`artifacts/offline-startup-native-20261029/`. `Verify-Evidence.ps1` has Deployment,
Recovery, Lifecycle and Cleanup stages. No ordinary profile was manually repaired
or replaced. This does not establish distributed ownership for synchronized hosts,
remote-server support or arbitrary power-loss recovery.
