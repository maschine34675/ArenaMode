# Tarkova Cola compatibility: native test record — 2026-10-25

Status: bounded native acceptance completed. Zombie research/achievement capture, DM exclusion, physical respawn binding, restoration, ordinary PMC pass-through, coordinated startup payout, duplicate suppression and mod-absent normal startup passed. The temporary mod files/settings were archived and removed. A harmless empty directory and dangling bundle junction remain in the private cache; see the exact cleanup boundary below.

## Scope and preservation

The run used the existing SPT-GPU-2 test profile and Tarkova Cola 2.2.1.
Only its client and server folders were installed temporarily; the packaged
optional VisitAPI server is excluded. No fabricated receipts, manual profile or
progress edits, or whole-profile rollback form part of the test.

The release ZIP SHA256 is
`9d5116b79e21301655135a157d680ee209b66aa1450b449b1a5da64cbc981607`.
Before installation, all three canonical profiles on each of the development
installation and GPU2 were archived with matching source-before, copied and
source-after hashes. Both complete BepInEx configuration directories were also
archived. Neither installation contained a Tarkova Cola client/server folder.
The ignored archive is `artifacts/tarkova-cola/native/before-20261025-135527`.

Initial UiBridge observation: unchanged game instance `76f12c8a`, started
13:00:16, normal main menu, no input since startup, no other connected session,
control free, normal backend on port 6969. This establishes availability only;
the running game does not yet contain the compatibility build or test mod.

## Planned native cases

1. Verify actual loaded Arena/Core/Cola identities and successful client/server
   contract installation after the root agent's build/deployment signal.
2. Enter the private Arena session through its normal UI. Run protected solo
   Zombies on Factory, consume an actual Speed Cola and kill actual zombies with
   the normal firearm path. Inspect the live optional state and native logs.
3. Leave through the native pause menu while alive. Compare the restored private
   profile and ordinary Cola baseline, and inspect the genuine signed result.
4. Return through **RETURN TO NORMAL SESSION**. Check automatic normal-server
   restart, one startup progression/achievement payment and corresponding mail.
   A further return/startup must not duplicate it.
5. Check an ordinary PMC raid passes through the native Cola lifecycle, and a
   non-Zombie Arena run cannot export Cola progression. Exercise rewards OFF and
   replacement-body behavior if the native run permits them within its scope.
6. Restore changed settings, retain generated foreign state outside mod discovery,
   remove only the temporary payload, inspect custom-item/achievement residue and
   verify normal startup without restoring an old whole profile.

The desired first achievement is Speed Cola's first kill, whose native reward is
25,000 RUB. Native inventory polling may also recognize the test bottle as found;
the report must record actual achievements and any custom bottle prize rather
than assuming this remains a RUB-only test.

## Results

Build/deployment evidence is separate from the native observations below. The earlier return blocker was resolved by user-coordinated shutdown of both hosts; the save/readback protection was never relaxed. Startup payment and a subsequent empty private-session return are separate proofs.

### First native pass and findings

- Exact optional payload installed on VM and GPU2: 149 files, excluding VisitAPI;
  every source/VM/GPU hash matched (`native/release-payload-manifest.json`).
  Client runtime contracts installed without a failure. The normal server logged
  protected progression enabled with bottle/grenade refunds excluded.
- A temporary, archived user policy denied only Freecam, so the real **START
  ARENA SESSION** flow could be exercised. It must be removed after the final
  normal return; no helper or gate was bypassed.
- The first attempt safely refused arm: the lower-camel `colaBaseline` field
  was not bound by native case-sensitive JsonUtil. Root fixed the annotation,
  added the native-wire regression and redeployed the server. The corrected
  private server SHA256 is
  `11de31f6a913a09b1f652a79bffa2b3feee04c3710891e85abc558b77e383108`.
  Failed-start logs are preserved under `native/arm-refused`.
- Corrected instance `2275446b` (14:20:18 local) started Factory Zombies normally.
  Test settings: 4 maximum alive, 2 at wave start, no growth/shooters, arena kit,
  no companions, 60-second intermission. Godmode was used. One real Speed Cola
  was given via the test inventory hook and consumed with native **UseAll**;
  live `SpeedColaActive` became true. Twenty-three real AK-103 shots produced
  one actual zombie kill. No kill/progression/receipt fabrication was used.
- The live foreign achievement state earned `a_found` and `a_first`.
  `a_found` legitimately includes a bottle prize; it is not a bottle refund.
  Adapter failure stayed null. After native disconnect the lease became null,
  and the foreign disk state remained its exact 57-byte pre-Arena baseline.
- The genuine signed result is a completed Zombie report with one kill,
  `AllowIncomplete=true`, and a verified restoration receipt whose expected
  and persisted profile revisions match. Its typed Cola delta contains only
  the two achievement keys. Evidence: `native/after-zombie-1/receipt-summary.json`.
- Semantic before/after profile comparison found only native maintenance fields:
  ordinary profile `Hideout.sptUpdateLastRunTimestamp`; private profile that
  timestamp and `Health.UpdateTime`. Inventory, money, skills, progression and
  quest trees were restored. Whole-file equality is not claimed.
- Native research XP exposed an ordering defect: `FinishCapture` ran before
  `EndStatisticsSession`, therefore observed session XP=0 despite the kill.
  Root corrected the exact statistics-end capture seam before the repeat below.
- **RETURN TO NORMAL SESSION** transferred the signed result, then safely
  deferred normal-server restart because another loaded profile (SPT-GPU-1,
  diagnostic fingerprint `38834474`) failed native save/readback equality.
  The visible queued-rewards notice was acknowledged. No server force-kill,
  save-hash reset or profile rollback was attempted. Logs are retained under
  `native/return-1`; payout was pending at this first stopping point.

The queued-rewards notice was the reason the normal restart initially appeared
stalled. Windows-MCP observed and acknowledged that existing **OK** dialog; the
helper then started normal instance `32efeddd` (14:33:00), backend 6969, MainMenu.
The server remained running throughout (pid 1748). The save refusal did not hold
or overwrite profiles, and did not discard the returned result. The affected
profile is not the active test profile. No profile repair was attempted.

### Return conflict diagnosis

GPU1 was independently running its own test profile while its profile files were
being mirrored onto GPU2. A read-only status found GPU1 instance `79cbac9d`,
started 14:34:47, with recent input. No GPU1 control/action was taken. Its exact
profile is the one the GPU2 save guard refused. The ignored file observations
show the foreign profile continued changing on both mirrored installations.

A native read-only `/client/game/profile/list` request on GPU2 (no login, select,
profile save or active-profile switch) then captured the loaded character clones.
After excluding native clone-only trader loyalty sanitation, RAM-versus-disk
differences were precisely `PMC.Health.UpdateTime` and
`PMC.Hideout.sptUpdateLastRunTimestamp`; Scav was identical. RAM held older values
than the externally synchronized file. Case-distinct production fields were
already present before testing and survived native roundtrip, so are not blamed.
The server guard remains unchanged. Evidence:
`native/return-1/memory-disk-diff-paths.json` and `readback-differences.json`.
A first diagnostic request omitted SPT's request-compression header and failed
before route dispatch; the resulting 13:44:41UTC compression error is test-tool
error, not a mod gameplay failure. The corrected GET succeeded, and the temporary
remote query script/output files were removed after archiving.

The research-capture correction was built separately. Root reported 3092 passing
unit tests, one existing skip, and client SHA256
`fd3e8712454d2452bc7b0643416b51669669f242ed6efaf81115a09330ecccc0`.
The next native pass restarted only the game; the normal server was left running
while GPU1's synchronized profile remained in use. Startup payout was not forced.

### Corrected research capture: native success

Private instance `70c3853c` (14:48:40) loaded the corrected Arena client. The real
second Zombie match had two MCX .300 BLK headshot kills, completed its first wave,
and reapplied the kit. Speed Cola remained active across that living wave change;
compatibility failure remained null. One test trigger request timed out when the
second kill initiated the kit change, but actual shot/death/frame data confirmed
the shot and target death; it was not retried blindly.

Native end at 14:51:16 reported 88 earned session XP and awarded 28 Speed Cola XP
through upstream 25% Arena scaling plus its existing bonus. The genuine signed
run `ef243202a1774ed2a44e7257173d4a4f` contains `xp.speedcola=28`, the same two earned
achievement keys, completed report state 0 and successful restoration state 1 with
matching profile revisions. The foreign disk stayed at its ordinary baseline;
after cleanup `_lease` and `_failure` were null. Evidence is under
`native/after-zombie-2`, with live identity in `zombie-2-live-status.json`.

During this run a parallel development session updated the VM's Redeploy.Core
from 1.0.11 to 1.0.12. GPU2 still held and ran 1.0.11+2e8cb76 throughout this proof;
we did not replace the library or claim this run validated the new build.

### Non-Zombie mode and physical respawn: native success

In the same private process, a two-participant Deathmatch used an actual Speed
Cola and one real SKS headshot kill. The lease observed 391 earned XP. A controlled
native local death caused a real replacement body: player/lease/foreign `Me`
all changed from inspector object 19 to 20, compatibility failure stayed null,
`Failed=false`, `ReplacementStatisticsPending=false`, and earned XP carried 397.
The old perk cleared on death, as upstream intends. A second real bottle on the
new body activated normally, proving the new body was usable rather than merely
referred to.

The final native end logged 397 earned XP but granted no Cola research in DM.
Signed result `e192d253aa4a40ea879d6f038a346716` explicitly contains no Cola delta
and has matching restoration revisions. The foreign disk remained unchanged.
Evidence: `native/after-dm` and `dm-respawn-status.json`.

The next normal return transferred both new signed results. The existing queued
notice appeared again because the unrelated synchronized profile conflict still
prevented a safe normal-server restart. It was acknowledged; no restart was
forced. Normal instance `5f5cec3b` (14:56:43) now loads the independently updated
Redeploy.Core 1.0.12, so the forthcoming ordinary-raid countercheck has that distinct
library identity; the preceding Arena body proof ran 1.0.11.

### Ordinary PMC countercheck and first stopping point

Normal instance `5f5cec3b` started Factory through the native PMC path, with AI
set to None and no companions. The optional adapter had no lease/failure. Native
Cola state/get and state/save ran, its ordinary guard/post startup path executed,
and `EndStatisticsSession` reported 100 native XP with no active perks. No bottle,
claim or synthetic progress was added in this ordinary raid.

Gate3's normal extraction trigger completed a 38-second Run Through. This was a
successful live extraction, not disconnect/death. The active profile retained
all 314 inventory items, gained the native 100 PMC XP (7061→7161), had zero Cola
items/native Cola achievements, and its three mail dialogues were unchanged.
Afterward `ArenaNow=false`, `InArena=false`, `ServerSafe=true` and compatibility
lease/failure null. Evidence: `native/after-pmc` and
`normal-after-pmc-status.json`. The ordinary Cola file contains no earned XP,
challenge progress or achievements; only its native initialization defaults.

The game was gracefully closed after returning to MainMenu. At this first stopping point, host-helper
status: GPU2 normal server pid 1748, started 13:18:37UTC, still running; no EFT or
private server; the ordinary launcher remains open. No other running server was
stopped. All three Arena settings changed by the menu recipes were restored to
the archive (FreeForAll=true, factory day solo participants 4, GameMode=zombie).
The effective Arena config comparison is zero. The temporary Freecam-only policy
was removed from both mirrored installations, where no user policy existed
before this test. Volatile test-only options/godmode ended with the game.

The corrected Zombie and DM restored-profile comparisons differ only in native
Hideout maintenance timestamp; no inventory/progression tree changed. The first
run additionally differed in native Health.UpdateTime. Do not describe these as
byte-identical files. Each signed restoration receipt separately confirms the
protected expected/persisted revision equality.

### Coordinated normal startup: genuine pending rewards paid once

The user subsequently confirmed both hosts free and SPT stopped. Both helper
status probes confirmed no server, game or launcher, and no second private server.
Before starting anything, all six canonical profile files, both reward trees,
foreign state and generated config were archived again with verified hashes in
`native/resume-before-20261025-153313` (164 files). No profile rollback or manual
progress edit was used. Arena client/server hashes remained the corrected values
above; this resumed stage used Redeploy.Core 1.0.12.

The first normal server start (pid 15460) paid the three retained, genuine signed
results. They were earned by the earlier combat tests, not created for this stage:

| Signed run | Actual kills | Normal reward | Cola payment |
| --- | ---: | --- | --- |
| `2b2f528472f5467e81d657dd3f439d0a` | 1 | 1,000 RUB + 100 PMC XP | `a_first` and `a_found`: 40,000 RUB + one Speed Cola |
| `ef243202a1774ed2a44e7257173d4a4f` | 2 | 2,000 RUB + 200 PMC XP | 28 Speed Cola research XP; the same achievements granted no second prize |
| `e192d253aa4a40ea879d6f038a346716` | 1 | 1,000 RUB + 100 PMC XP | None (Deathmatch) |

The verified test subset totals **44,000 RUB, 400 PMC XP, 28 research XP and one
Speed Cola achievement prize**, with no refund payment. All three are kill-only
results with incomplete final-round allowance, not round wins. PMC XP changed
7,161 to 7,561; the ledger changed six to nine payments. Two native achievement
keys were added once (`6a1c00000000000000000a01` and
`6a1c00000000000000000a04`). The foreign state records `xp.speedcola=28` and both
earned achievement flags. Two Cola plans have completed `.done` records.

The actual SYSTEM mail text displays the already-credited PMC XP, says two new
achievements for the first Zombie result and zero for the second, and contains
the exact catalog prizes. The complete existing unrelated mail and six earlier
ledger payments were preserved. Evidence: `native/payout-first-start`, including
the native mail UI capture and original persisted mail items.

A second **normal server restart**, using graceful Ctrl-C and no force, started
pid 4748. The whole profile was semantically unchanged; mail, achievements,
ledger and foreign state were unchanged, as were both plan/done file hashes.
Evidence: `native/payout-second-start/idempotence-summary.json`. The independent
parent audit is `artifacts/tarkova-cola/final-payout-review.json`.

### Empty private-session switch after payment: no new payout

This is an additional return-path test, not the event that paid the earlier
results. Starting in normal instance `7697b184`, the existing **START ARENA
SESSION** UI entered private instance `8859c15f`. No raid was started and no new
result was produced. **RETURN TO NORMAL SESSION** returned to normal instance
`82ca5d3a` on backend 6969 in 37 seconds, with no queued-rewards notice or error.
The helper retained the already-running normal server because no payment restart
was needed. XP, ledger, mail and foreign state remained unchanged.

The temporary Freecam-only user policy was used solely to expose the genuine
private-session UI, then removed from both installations after normal return.
Neither installation had such a policy before the test. Evidence:
`native/no-result-private-status.json` and `native/no-result-return`.

This proves the separate empty return is idempotent. It does not replace the
startup payment proof or claim a fresh pending-result return automatically
restarted the normal server during this resumed stage.

### Test cleanup, preservation and final mod-absent startup

Only the single Speed Cola prize was claimed from its known SYSTEM mail via the
native GET transfer screen. The native stash DISCARD action and its explicit
confirmation destroyed that one test bottle. No RUB was claimed or deleted;
all **44,000 test RUB** remain in the three reward mails. No unrelated items or
mail were touched. After graceful game and server shutdown, the saved profile
contains zero Cola items in inventory or mail, while both achievement keys and
7,561 PMC XP remain. Evidence: `native/cleanup/bottle-discard-summary.json` and
`post-native-bottle-discard-profile.json`.

All temporary release files were identified by the original 149-file manifest.
Before the first removal, every file was copied outside mod discovery and its
archive SHA256 was compared with the source. The resulting 569 archived files
include the two normal payloads, 149 VM Syncthing temporary copies whose hashes
also exactly match the release, and the private cache's 114 copied release files.
Generated native `loot.json`, foreign profile state and upstream config were
archived and removed only at their exact known test paths. The private helper's
bundle directory was a junction rather than a second bundle copy. No canonical
profile, reward ledger, signed outbox or payment journal was removed.

Absent on **both** `D:\SPT41` and GPU2 `C:\SPT41` after removal:

- `BepInEx\plugins\TarkovaCola`;
- `SPT_Runtime\user\mods\TarkovaCola`;
- `BepInEx\config\com.tarkovacola.server.cfg`.

Exact archives and removal inventory are under
`native/cleanup/removed-payload/manifest.json`. They retain the earned research
state outside discovery; restoring the test mod does not require reconstructing
or modifying a player profile.

There are **no remaining mod files** in the private cache. Its residual path on
GPU2 is
`C:\Users\Service\AppData\Local\maschine-ArenaMode\session\server\user\mods\TarkovaCola`:
it contains only an empty `images` directory and a dangling `bundles` junction
whose removed target was `C:\SPT41\SPT_Runtime\user\mods\TarkovaCola\bundles`.
The final attempt to remove this empty structure was rejected by automatic
approval review with `blocked by policy`; it was not retried or bypassed.
`native/cleanup/private-residual-structure.json` records that precise boundary.

With the mod absent, the normal server started successfully (pid 17544), then
EFT reached MainMenu in 55 seconds, instance `5738591e`, normal backend 6969.
The loader reported 41 plugins without failures/dependency errors; the saved
default status verifies the 16 listed plugins and two listed libraries match
the installed and VM files (`loadedMatchesDisk=true`, `compareStatus=identical`). Tarkova
Cola was not loaded. There were no client Error/Fatal entries or server errors
in this startup segment; known unrelated loader/Harmony/Fence warnings remain.
The final saved profile preserves the same ledger, mail and achievements as
after the deliberate bottle discard, with 7,561 PMC XP and no Cola items.
A final read-only residue scan found no Cola item in inventory or mail in any
of the six canonical profile files across the two installations.
Evidence: `native/mod-absent-start/status.json`, `final-preservation-summary.json`
and `final-mail-summary.json`.

Finally, the game quit gracefully and the normal server stopped via Ctrl-C.
Both host helpers confirmed no EFT, launcher, ordinary or private server running.
The original Arena settings remain restored; no new combat tests were performed
in this resumed payout/cleanup stage.

Native coverage is intentionally bounded: solo Zombie living wave transition,
DM physical death/respawn, and ordinary PMC. PFT squad replacement, rewards OFF,
crash/interruption recovery and other maps were not exercised natively here;
the associated offline policy/journal tests are distinct evidence. Corrected
native runs had no client errors; existing loader/Harmony warnings, one ordinary
Unity BoxCollider negative-scale warning, and Arena-end Animator.GotoState
warnings remain recorded without an unsupported causal attribution.
