# Native logging check — 15 October 2026

## Result and limits

**PASS for the tested logging contract and native combat paths.** A fresh SPT-GPU-2 process ran Factory Team Deathmatch and solo Zombies on the delivered logging build. Developer details switched OFF → ON → OFF during the same TDM player body, without stopping combat or reload. Normal match start/readiness/result information remained visible with DeveloperMode OFF. No client or server Error was recorded in the fresh test window.

The first two-raid build exposed one misleading Arena Warning: the expected solo-Zombie death/exit invokes `ArenaRespawnDriver.OnExternalStop()` after its protected exit already stood down. Root corrected only that logging classification, preserving the unexpected-abort Warning. **A third, exit-only native follow-up on the final DLL passed: expected exit at Debug, zero Arena Warning/Error, protected profile restored.** Both build identities are kept separate below.

The older native `BotMemory.GoalTargetBeingHitAction` exception **did not recur**. This is non-reproduction in two short Factory raids, not proof of a fix. The runs deliberately exercised native shots, hostile melee, corpse release and replacement profiles. No FPS/capacity conclusion is drawn. Godmode/infinite ammo/aim tracking and safety-checked teleport were test aids, not performance conditions.

## Final exit-only follow-up — production commit `6dc2e59`

Root changed only the log branch in `OnExternalStop`: when `StandDown` is already true, emit Debug; otherwise preserve the existing Warning. Both branches retain the unconditional `StandDown = true` assignment. Gameplay state is unchanged. The final DLL was built immediately before the commit, so hash/MVID identify the tested binary more precisely than its embedded source label.

- Final Client SHA-256 **`4D8E897F0E7C4A4C660AED51D373CD168CA9012FE0A0301D776B16E80DA026A8`**, MVID **`563da9c1-363a-49eb-b83c-f390dd4e6817`**. Server/companion unchanged from the table below.
- `gpu-run deploy-wait --game` verified the new client hash on the host and restarted EFT. Fresh instance **`ae44a453`**, UiBridge started 18:41:21; startup file line 7682 confirms the final MVID. All **48 loaded identities** match VM and host.
- Client log `LogOutput-SPT-GPU-2-2026-10-15_18-41-13.log`. Solo Factory Zombies used **unchanged user settings**: 32 max alive, wave size 50, growth 20, own gear, Developer OFF. No test hook, config change, teleport, aim tracking or synthetic death was used in this follow-up.
- Native raid ran from 18:43:10 to ordinary hostile death at 18:43:29 (19 seconds; 0 kills). Thirty live infected were observed during startup; 32 native animator warnings at teardown correspond to the full native group, not to an Error.
- File line **15673**, 18:43:29.423: **Debug** `Arena native stop observed after respawn already stood down.` Then protected result commit at line 15675 and ordinary Info result at line 15722. **Zero Arena Warning/Error**, no old unexpected-end Warning.
- Complete fresh process: **41 Warning, 0 Error** = four optional Fika exclusions, four other-mod Harmony target warnings, one Factory transit BoxCollider warning, **32 native Animator.GotoState synchronization warnings** during teardown. The animator family remains diagnostically open; it was not hidden or downgraded by Arena. Four server profile-preview fallback warnings before raid, zero server Errors in the follow-up window (16:41 UTC onwards).
- Returned via all native result NEXT pages to **MainMenu, no raid, no hooks active, control free, no other session**. All 216 config values independently equal the original baseline again. All 401 inventory items, quests, skills and experience unchanged; only normal health-update and hideout-update timestamps changed. No profile overwrite.
- Evidence: `followup-status.json`, `followup-exit.json`, `followup-warnings.json`, `followup-analysis.json`, `followup-remote-client.json`, `followup-config.json`, `followup-profile-verification.json`, `followup-server-analysis.json`. Profile check is reproducible with `node artifacts/logging-20261015/native/analyze-followup.cjs`.

## Exact build and environment

This section and the two-raid coverage below describe the **first logging build**, before the final five-line exit-log correction above.

- Source: parent manifest `artifacts/logging-20261015/build-manifest.json`, base commit `9f29f49d8195d823633a437ebe8bb352d87b066d` **plus the manifest's uncommitted logging changes/source fingerprint**.
- Host/profile: **SPT-GPU-2**, hosts.json default test profile only. EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`; UiBridge `0.9.3+e1af7e24f65904fb73b68e43cb92ae185e51cb13`.
- Fresh process: UiBridge instance `c0e64226`, started at 18:20:35 Europe/Berlin. Client log `LogOutput-SPT-GPU-2-2026-10-15_18-20-27.log`.
- Client startup line 7682 confirms **MVID `9024a306-e382-4b89-aa86-881a76f56e9e`**.
- Final UiBridge comparison: all **43 plugins + 5 libraries** identical across VM, host disk and loaded process. Remote file hashes independently rechecked after the raids.

| Artifact | Remote SHA-256 |
| --- | --- |
| Client | `AB3BC1C102C885412EEB36979566158C4C909F9771D4DDC552E76E1A652207E0` |
| BigBrain companion | `5701DFF516540CD3DAA7B8BC3E42132E96E3184391EE1676928AEB918818B0F9` |
| Server | `C1A37197A746958C6A82EE2D41B1A7E61E9497F9A486AE92A871F33DC81BEA3B` |

Server log timestamps are UTC: **client 18:32 corresponds to server 16:32**. The fresh server window begins 16:19 UTC, includes its startup and both raids, and ends at the captured 16:36:34 tail. No other mod/logger settings were changed. The live client log is locked against ordinary file hashing; its exact metadata and the complete warning/Info query exports are preserved instead of claiming an unavailable raw-log hash.

## Native coverage

### Factory Team Deathmatch

- Armed at 18:25:52; started at 18:26:07; native 5v5 readiness at 18:26:35. Local USEC with four friendly and five opposing slots.
- Actual SKS head kills while DeveloperMode OFF and ON; subsequent actual RSASS kill after respawn. Final **3 kills / 2 deaths, score 700, best streak 2, 3.4 minutes**.
- One deterministic UiBridge local-death trigger explicitly tested native respawn. One subsequent Backspace give-up produced the second death and another successful respawn; Backspace is **not** match exit in this mode. Ended through Escape → Disconnect, then advanced native result pages with NEXT.
- At least one corpse release and replacement bots confirmed during the ON window. `Performance/MaxCorpses=2` deliberately made cleanup observable in a short raid.
- Reload was exercised on the **same SKS body** before and after the second OFF toggle. ON emitted the native reload completion detail; OFF did not. Actual kills and score progression confirm that detail guards did not gate the gameplay actions.

| Developer window (client local time) | `[dev]` Info entries | Ordinary Arena Info |
| --- | ---: | ---: |
| OFF: arm to 18:27:20.295 | 0 | 5 |
| ON: 18:27:20.295–18:27:45.217 | 50 | 0 |
| OFF: 18:27:45.217 through result 18:29:29.557 | 0 | 1 |

The ON window contains native reload completion, hitmarker audio play, TDM awareness, search tempo, pickup objectives and corpse diagnostics. Counts are file-query events, not frame counts. Ordinary Debug is a separate diagnostic channel: it was visible to UiBridge/the host's already-enabled debug capture without DeveloperMode. No global BepInEx filtering was changed.

### Factory solo Zombies

- DeveloperMode enabled at **18:30:55**, before loading/spawning. Temporary test values: MaxAlive 16, WaveStartSize 20, WaveGrowth 4, Arena kit instead of own gear. Existing crowd, shooter share 20% and 0.2 s spawn interval were retained.
- Started 18:32:03; **16 simultaneous native infected** observed and **20 distinct spawn-arrival events** over the wave. Four arrivals after the initial sixteen demonstrate refill behavior.
- Native infected swings and landed hits started at 18:32:14. Close contact continued while real weapon fire killed targets. **7 own kills**, with **9 corpse releases** before the exit; bot crossfire/other deaths need not equal own kills.
- Test hooks were switched off before the final contact. The local player then died through ordinary hostile damage within ten seconds and the solo match ended normally: **7 kills / 1 death, score 1150, best streak 7, 2.1 minutes**.
- Developer summary: 4519 recorded native swing callbacks and 259 landed-hit events. These are implementation counters, **not independently verified unique swings**. Developer was enabled before spawning; the summary's late-toggle/partial-observation caveat was not exercised in this raid.
- Zombie ON interval produced 25 developer Info events and five ordinary Arena Info events, plus the one exit Warning described below.

## Exact warning/error inventory

Complete client process: **30 Warning, 0 Error/Fatal**. All thirty are exported, including startup and teardown; none were filtered out to achieve the result.

| Signature | Count | Evidence and causality |
| --- | ---: | --- |
| Optional Fika-dependent DLL serialization exclusions | 4 | Startup, lines 16–19. Fika absent; MedicRound/UIFixes/WTT companion assemblies. Unrelated to Arena combat. |
| HarmonyX inherited-method patch targets | 4 | DynamicMaps `BattleUIScreenShowPatch`, UIFixes `DialogWindowPatch` and `WeaponModdingScreenCursorPatch`, WTT `IsConditionDone`. Existing source attribution in `LOGGING-ERROR-AUDIT-20261015.md`; not Arena patches. |
| `RespawnApiProbe.ProbeRuntime.AlwaysThrow` subscriber | 4 | Lines 15619, 15949, 16065, 16254. Explicit installed test-fixture exception at initial body replacement and respawns. Not a spontaneous production failure; subscriber delivery and match continued. Probe was left installed and unchanged. |
| Redeploy `Destroying stale PerfectCulling sampler 'FPS Camera'` | 4 | Paired with those four body replacements. Arena invokes the body-replacement path, Redeploy owns the cleanup warning. Indirect Arena-triggered recovery, not an Arena exception. |
| Factory `TRANSITS/Road_to_woods` negative BoxCollider | 2 | Once per raid, 18:26:37 and 18:32:33. Native hierarchy warning, no stack proving Arena causation. Existing scene/transit data family. |
| Arena `External raid end during arena respawn` | 1 | File line 16462 at 18:34:10.768, between normal Zombie terminal-death Info and protected native result commit. Direct Arena diagnostic in `ArenaRespawnDriver.OnExternalStop`; normal terminal death hold in this observed case. Unexpected external aborts must remain distinguishable. |
| Unity `Calling Animator.GotoState on Synchronize layer` | 11 | Lines 16470–16480, 18:34:11.826–.834 during Zombie teardown. Native animator warnings, no managed stack. Temporal link to Arena-invoked end is real; the owning animator/caller is **unknown**, so not labelled harmless or conclusively external. No stuck exit observed. |

No native `BotMemory` hit exception, StaticDeferredDecal NRE or `Animator.SetBoltCatch` destruction exception appeared in this process. Those older findings remain documented separately.

Fresh server window: **13 Warning, 0 Error/Fatal**: one PFT courier trader fallback during startup; four profile-preview fallbacks before either raid; eight Fence missing plate-slot warnings during its assortment generation. These are the already-attributed families in the earlier audit, with no new evidence of Arena corruption.

There are also **63 Information entries from `SPTarkov.Server.Middleware.SptLoggerMiddleware`** for `/arenamode/client-settings` in the captured window, typically every ten seconds after polling is active. The route is Arena-related, but the logging source is SPT middleware, not the Arena logger. This was reported to root without changing unrelated logger settings.

## Restoration after the initial two raids

- Saved all **216/216 live Arena config values**, raw Arena config and the default test profile before any test edit.
- Re-read all 216 after both tests; restored the seven values that actually differed through BepInEx's live config API, then independently read all 216 again: **zero differences**. DeveloperMode is OFF; corpse limit 10; TDM limit 600 s; Zombie values 32/50/20 and own gear true are restored.
- Raw config hashes differ only because BepInEx rewrote eleven **comment-only numeric range descriptions** from decimal commas to decimal points. No setting value differs; no blind file overwrite was used.
- Protected test-profile inventory is **byte-equivalent as parsed JSON**, including all 401 items. Quests, skills, experience (100) and every other profile field are unchanged except normal `characters/pmc/Hideout/sptUpdateLastRunTimestamp`. The profile was compared, never overwritten.
- Final status: **MainMenu, no raid, godmode OFF, infinite ammo OFF, no tracking, control free, no other session**. EFT/server remain running for the parent; no further action taken.

## Reproducibility and evidence

Private evidence directory: `artifacts/logging-20261015/native/` (ignored; do not publish raw profiles/logs).

- `client-warnings.json`: complete 30-event file query with exact source line/time/stack.
- `client-arena-info.json`, `developer-switches.json`, `client-combat-lifecycle.json`: complete matching pages for Info contract, toggles, 20 infected arrivals and corpse releases.
- `tdm-start.json`, `tdm-off-kill.json`, `tdm-on-dev.json`, `tdm-respawn.json`, `zombie-contact.json`, `zombie-combat.json`, `zombie-death.json`: native state/action outcomes.
- `restored-config.json`, `restore-applied.json`, `before-*`, `after-*`, `analysis.json`: restoration and protected-profile comparison. Recompute with `node artifacts/logging-20261015/native/analyze-native.cjs`.
- `remote-file-manifest.json`, parent build manifest and `final-status.json`: hash, timestamp and loaded-build evidence.
- `tdm-prepare/report.json`: generic automation's outdated-label failure. It waited for `TEAM DEATHMATCH`, whereas the real menu says `TEAM DM`; continued with fresh observed UiBridge controls. `zombie-prepare.json` is the prepared scenario, not an executed recipe; Zombie configuration and native menu steps were performed directly and recorded.

Two additional tool-layer failures were separate from the game: an initially mistyped server-DLL relative filename was rejected before mutation; then deploy-wait verified all three correct hashes but its restart helper refused because EFT was already stopped. A regular `gpu-run start --host host2` correctly started the fresh process. None is counted as an Arena runtime error.

## Independent review before deployment

Reviewed early Developer guards in load boosting, TDM search tempo, corpse cleanup, friendly fire, Molotov touches, movement/vault observations, friendly markers, input-blocker traces and audio. Gameplay ownership/application was outside the guards; only diagnostic work was conditional. The Zombie melee observer subscribes only to bots arriving while Developer is ON, so late enabling yields partial observation. Parent made that caveat explicit before this tested build. Warning/error emission remained independent of DeveloperMode.
