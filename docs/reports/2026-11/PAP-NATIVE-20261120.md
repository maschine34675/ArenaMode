# Pack A Punch: native checks (2026-11-20)

Source: branch `feature/pap-save-pipeline-v070` at `eafe1530` (build stamp `0.6.0+eafe1530`), on top of master
`0d596980`. Contract: [profile write pipeline](../../reviews/PROFILE-WRITE-PIPELINE-20261120.md). User guide:
[Pack A Punch compatibility](../../PACK-A-PUNCH-COMPAT.md).

## Setup

- Host: GPU-2. Its install `C:\SPT41` is synced from `D:\SPT41`. No other session used the GPU hosts during the test; the
  session that had used GPU-2 before had finished and confirmed it.
- Pack A Punch Hideout Module 2.2.1 (Forge mod 3223, GitHub release `v2.2.1`, SHA-256 `a1802df7…`, the file of the Forge's
  VirusTotal link) and its hard dependency Hideout Uncensored 1.3.0 (Forge mod 3167, SHA-256 `3ae6900d…` as stated on the
  Forge), client and server parts. PaP's first-launch question was answered ON (normal gameplay).
- Profile SPT-GPU-2 (`6ab7db1c…`, hash alias `da72bda3`). Backup before the test:
  `D:\SPT41-Backups\pap-v070\20261120-113558` (profiles, reward folders, ArenaMode config, release files).
- At its first start PaP gave all three loaded profiles the area 115 ("3 profile(s) given the area") and offered
  "115 Riddles" to spt41dev (`6a6f5c04…`). The GPU-2 profile held the area only.

Raw output: `artifacts/pap-native-20261120/` (gitignored).

## 1. Reproduction on release 0.6.0 (GPU-2)

PaP and Hideout Uncensored are not on the Arena whitelist, so the side screen showed MOD CONFLICT and START ARENA ·
RESTART went through the helper:

- helper: `Arena entry declined: preflight-save/save-not-confirmed; active requests=0; active updates=0; normal resume=True`;
- server: `Session switch refused: preflight-save/return-profile-save-not-confirmed; operation=enter; profile=da72bda3
  (selected); ramDiskMatch=false; ramStableDuringReadback=true; fieldSample=[disk:characters.pmc.Hideout.Areas]; ...`

These are the helper lines of Forge issue 3. Its server line also names `Quests`, because that player's profile holds the
quest.

## 2. Scenario A: private session with the fix (GPU-2)

- Installed: client, helper, BigBrainCompat and server `0.6.0+eafe1530`. Redeploy Core stayed 1.0.24.
- Server start:
  - Debug line "Profile write proof enabled; foreign prefixes: text write 1, byte write 0; foreign postfixes on the profile
    comparison: 0".
  - PaP restored the area of all three profiles from its side files.
- START ARENA · RESTART:
  - The helper saved and stopped normal SPT, built the server tree (13 server mods) and started the arena server, which
    accepted connections on 6970 after 7.1 s. The game came up in `-arena-session`.
  - No refusal. Debug: "A profile write is transformed before it reaches the disk by another mod (profile 26dc25d5); the
    save comparison follows the captured write", and at the entry save three "Captured profile write ... differs from its
    input in 1 top-level sections".
- The match: Deathmatch on Factory, 2 participants, one round of 2 minutes. One bot was killed (UiBridge aim tracking,
  godmode). The round was won at the time limit (1 kill, 250 points) and the match ended.
- RETURN TO SPT · RESTART:
  - PaP restored all three profiles: "restored 0 fragment(s), 0 in mail, quest, Pack-a-Punch area" for spt41dev and the
    area for the two others.
  - No `.stale` file. "0 profile(s) given the area".
- No reward was paid: "Reward skipped for profile 6ab7db1c… (invalid-result-history)".
  - That state was already there: the GPU-2 server log has it at every start since at least 2026-11-17, with release 0.6.0
    and before PaP was installed.
  - So this return made no startup profile write. Part 2 is shown in section 4 instead.
- No ArenaMode or PaP Warning or Error in the server log from the fix install to the end of the test.

## 3. Scenario B: normal session with PaP loaded (GPU-2)

- A temporary user file `BepInEx/config/com.maschine.ArenaMode.whitelist.json` = `{ "schema": 1, "unlisted": "allow" }`
  admitted every installed mod. The ARENA toggle then showed no MOD CONFLICT. The file was removed afterwards.
- The same Deathmatch, now in the normal session, with one bot kill.
- At the end the server logged:
  - "Profile snapshot restored";
  - "Captured profile write (profile da72bda3) differs from its input in 1 top-level sections";
  - "Protected Arena end completed for session …".
- Under PaP this end failed on 0.6.0. That was Sankese's report and the reason for ArenaMode-WeaponPoolFix 1.3.0.
- No 0.6.0 control was run for this scenario. Its readback is the same strict comparison that section 1 shows failing.

## 4. Part 2 on a copy of the install (VM)

- What was copied: `D:\SPT41\SPT_Runtime` (10.3 GB, without `user\logs`) after the host test, so it held PaP, Hideout
  Uncensored, PaP's side files and the fix server DLL. The copy went to `D:\SPT41-Backups\nativetest-pap-20261120`, a
  folder Syncthing does not share.
- Between the runs the copy's profiles, reward folders and PaP folder were restored from a saved state. Only the server ran.
- The copy is another folder. ArenaMode's startup therefore archives spt41dev's reward records and enrolls the profile
  again, writing the profile file twice (archive, enrollment) before SPT loads it.
- The side files were 11 minutes older than these writes.

| Run | Server DLL | Result |
|---|---|---|
| Control | `eafe1530` compiled without the one carry line in `RewardStartupFiles.WriteAtomic` (SHA-256 `C71F1BF9…`) | See the list below. |
| Fix | `eafe1530` (SHA-256 `CA36F11C…`) | See the list below. |

Control run:
- PaP logged "Profile 6a6f5c04…: restored nothing (side file older than the profile, set aside as .stale)" and left
  `6a6f5c04….json.stale`.
- PaP then gave the area anew ("1 profile(s) given the area") and offered "115 Riddles" anew. The player's PaP state would be
  lost.

Fix run:
- "Pack A Punch side file kept fresh for profile 26dc25d5" twice.
- PaP logged "restored 0 fragment(s), 0 in mail, quest, Pack-a-Punch area from the mod's side file" for spt41dev and the
  area for the two others.
- No `.stale` file, and "0 profile(s) given the area".
- The side files ended with the profile's write time (11:01:55).

The copy was removed afterwards.

## Restore

- EFT and the server on GPU-2 stopped; both were off before the test.
- PaP and Hideout Uncensored removed from `D:\SPT41`: client and server folders and their two BepInEx configs. PaP's
  runtime files were kept as evidence.
- Release 0.6.0 files restored: Client and Server `0.6.0+72273354`, Session and BigBrainCompat `0.6.0+39dfcf77`.
- `com.maschine.ArenaMode.cfg` (`F9AF829F…`) and `client-settings.json` (`1F29D2C9…`) restored.
- Identical on GPU-2; nothing of the test left on GPU-1.
- No profile file holds PaP data (no area 115, no fragment template).
- Profile SPT-GPU-2 against the backup differs in three places:
  - `Health/UpdateTime` and `Hideout/sptUpdateLastRunTimestamp`;
  - SPT's own mod history `spt.mods`, which gained two entries (PaPHideoutModule 2.2.1, HideoutUncensored 1.3.0).
- The other two profiles are unchanged.

## Not covered natively

- A payout write at the return, because of the test profile's own `invalid-result-history`. Section 4 exercises the same
  `WriteAtomic` path through the archive and enrollment writes; offline it is pinned by
  `PapSideFileGuardTests.PayoutStartupWriteKeepsSideFresh`.
- Fragments in the inventory or in mail, and quest progress counters. The GPU-2 profile had the area only; spt41dev had the
  offered quest only.
- A private Arena server with PaP admitted.

## Offline verification

- `tools/Run-ArenaVerification.ps1 -ChangedSince 0d596980` on `1002c4e3`: 52 of 53 steps passed.
  - xunit once: 4,393 tests, 4,390 passed, 3 environment-gated skips.
  - The failing step, EquatorMapChecks, runs the whole xunit suite again. A new assertion there matched "124" inside a hex
    fingerprint; this was fixed in `d8723c1d` and its class re-run (43 passed).
- `tools/OfflineStartupChecks/Mutations.ps1` on `eafe1530`: 19 of 19 mutants rejected, baseline 34 passed. Two anchors were
  re-anchored in `eafe1530` after the review fix.
