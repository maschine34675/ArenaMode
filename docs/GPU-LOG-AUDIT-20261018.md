# Disabled mode previews and representative GPU2 log audit

**2026-10-18 — native gates N1–N3 passed.** The complete CTF/Domination button
surfaces are visibly disabled and inert. Three short representative Arena samples
completed, including real enemy-caused death/killcam/respawn in Deathmatch and
AirPit Team DM, then a deterministic death/round rebuild in PFT Tactical. This
does not mean the logs are clean: the known PFT exit-order diagnostic reproduced,
and other warning/error families remain explicitly recorded below and in the
[source attribution audit](LOG-SOURCE-AUDIT-20261018.md).

## Candidate, installation and evidence boundary

- UI-only source candidate **`53fa560`**, paired Release Client/BigBrainCompat
  deployment through `tools/Deploy-ArenaClient.ps1`. Synchronization completed
  before a fresh `gpu-run start`; no Server/Core or unrelated mod was deployed.
- GPU2 test profile **SPT-GPU-2** only. EFT 0.16.9.5-40743, Unity 2022.3.43f1,
  UiBridge 0.9.4+42bbfb38; process **`14e1597f`**, started **13:56:53** local.
- Final native status: **41 plugins and six libraries** match across VM build,
  host disk and loaded process. The narrower maschine listing contains 17 plugins
  and two libraries. CraftQueue is absent in this installation; the tester did
  not remove/disable it. Earlier CraftQueue failures are not part of this run.
- Diagnostics/DeveloperMode stayed **false**. Existing BepInEx Disk and Console
  `LogLevels=All`, Harmony `LogChannels=Info, Warn, Error, Debug` were unchanged.
  No safety alarm was disabled to improve the output.

| Binary | SHA256 |
| --- | --- |
| ArenaMode.Client | `AD92778F7761D008AAEE6CED737A45F0C016FC5AE59C82F807B4A84DBC7D47F4` |
| ArenaMode.BigBrainCompat | `936FCFCC9FD56AC66C6F4B7ED086C2B1D47B66BAF72E66DDC1646FE9DB779B63` |
| ArenaMode.Server, unchanged | `BA468562C633445CC668D709748861456102F1DAFEF5DA61457B732C3348845A` |
| Redeploy.Core, unchanged | `6319028DF7A828999EE2B4CCC8B4ECD8213CE22529EA07A34A1ABA61F4148577` |

Private raw evidence is ignored under
`D:/SPT41/Development/ArenaMode-ui-log-audit/artifacts/ui-log-audit/`.
Do not publish this directory wholesale: it contains profile/store/config backups
and raw logs. `start-status.json`, `final-status.json`, `verification.json` and
`final-logs/summary.json` are the primary verification records.

## Native UI and preview comparison

`menu/disabled-mode-surfaces.png` was visually reviewed at 1920×1080. Both future
mode rows have entire dark neutral-gray backgrounds, distinct from the playable
blue-gray rows. UiBridge marks both controls disabled. Actual clicks on each row
kept SelectLocation open and left all **224 configuration entries unchanged**.
Clicking Deathmatch afterwards changed the rule controls normally. Evidence:
`menu-check.json`, the two `after-*-click-config.json` captures and the initial
menu scenario report.

The solo Deathmatch Confirmation and Deploying screens both displayed a complete
PMC with Arena equipment (`dm-entry/dm-confirmation.png`, `dm-deploying.png`).

A separate control invited the four existing PFT companions through MY SQUAD,
opened the ordinary **PMC** Factory Confirmation screen, and returned without
pressing READY. Its full five-person preview displayed the unarmed test PMC and
four armed companions. The same party then opened Arena Tactical with **Arena
kits**: all five models remained complete and the local PMC showed the Arena
equipment preview. See `pmc-four-companion-confirmation.png`,
`pft-preview-final/pft-confirmation.png` and `pft-start/pft-deploying.png`.
The four `PlayerModelLoader/CreateWeapon` NREs from the earlier test did **not**
reproduce in either control. This is one warmed-process comparison, not proof
that the historical model problem is resolved or that its owner is known.

The recipe refused two BACK steps during ordinary PMC-menu navigation; fresh
UiBridge coordinates and native clicks completed those returns. No normal raid
was started and no insurance transaction was submitted. Those automation failures
are retained under `pft-preview/` and `pft-preview-resume/`, not counted as game
start failures.

## Representative raid samples

All times below are GPU2 local UTC+02:00. Gunfire uses the real native trigger;
there are no fake kill events or inventory grants.

| Sample | Observed behavior | Boundaries |
| --- | --- | --- |
| Factory Deathmatch, 14:01:36–14:09:25 | Seven bots actively fought and were replaced. A native sprint route and safe teleports exercised different positions; 19 real SVDS/AKM shots, one own headshot kill, two enemy-caused deaths. | Godmode was used during initial setup/combat and explicitly disabled before death testing. FFA, eight total participants, one match. |
| AirPit Team DM, 14:11:04–14:12:46 | 4v4 formed. The PMC ran from the north box into combat, sustained real bot damage and died; the replacement appeared in the north team box with a new ADAR kit and fired one native shot. | One death/respawn. No godmode used in this raid; no sustained performance benchmark. |
| Factory PFT Tactical, 14:19:05–14:20:51 | Four existing companions plus PMC entered with PMC-only supplied VPO-209 kit. Two real shots after moving near an opponent; native kill hook triggered death/spectating. Round 2 rebuilt the leader, companions and kit and reached live 5v5. | Kill hook is deterministic lifecycle coverage, **not an enemy-caused Tactical death**. Round safety timer temporarily 60 seconds. |

Deathmatch killcam starts were **14:07:22.241** (theStraxus) and
**14:09:07.589** (RainbowPC). Arena held respawn until camera release at
**14:07:30.438** and **14:09:15.518**, respectively (7.96/7.93 seconds).
AirPit killcam began **14:12:15.314** (mighty_condor), released after **7.95s** at
**14:12:23.266**, and the north-box replacement followed at **14:12:23.286**.
The new PMC bodies had full health and functional firearms. This combines
real enemy-death evidence with the PFT deterministic transition sample.

PFT round 1 became live at **14:19:15.312** and ended at **14:20:15.438**, a
timer victory with three allies and one opponent alive. All nine old bot bodies
left the registries, the entry kit was prepared before held PMC respawn release,
the new leader/four followers were adopted, and round 2 became live at
**14:20:32.241**. The 20 displayed equipped/usable items matched their original
state after excluding newly generated IDs; knife and secure-container IDs were
retained. That displayed count is not a recursive attachment count.
`pft-equipment-round1.json`, `pft-equipment-round2.json`, `pft-spectating.png`
and `pft-round2.png` preserve the evidence.

Each sample used the normal native DISCONNECT/result-page flow back to MainMenu.
No hanging load, failed respawn or protected restoration failure was observed.

## Complete time-bounded logs

`uibridge_log stream=all` was read forward from
**2026-10-18T13:56:45+02:00**, preserving returned cursors and their arrival marks.
The combined capture includes startup and all samples, not just the newest lines.
After the final exit, configuration restoration and menu settling, the last poll
added nine ordinary Info/Debug records through **14:22:41.063**. Earlier server
sessions are excluded by that initial time boundary.

The result contains **11,436 unique entries**, from **23 full structured pages
plus the final poll**. All three streams were available, the final pages have no
continuation, and deduplication keys retain stream/file/line provenance. One
large EFT Info control-settings dump reaches the tool's 20,000-character limit;
no Warning/Error stack is truncated. The full original files are also archived
under `raw-logs/`. Text-only tool previews sometimes elide page content; the
counts use complete `structuredContent.lines`, not those previews.

| Stream | Warning | Error | Info | Debug | Message |
| --- | ---: | ---: | ---: | ---: | ---: |
| Client/BepInEx | 51 | 1 | 7,963 | 1,301 | 6 |
| Server | 27 | 0 | 485 | 879 | 0 |
| EFT trace | 0 | 120 | 521 | 82 | 0 |

These are emitted records, **not 199 independent defects**. Lifecycle repetitions,
different streams and native error-severity diagnostics require interpretation.
The separate source audit supplies the direct/indirect/unknown attribution.
Important native observations are:

- Arena: three bounded native-awareness rejection warnings for one follower
  after PMC death; the director tried other targets and the match still reached
  round 2. The real safety diagnostics were not muted.
- Arena: at **14:20:49.978**, immediately after intentional `Stop(Left)` at
  **14:20:49.976**, the PFT roster observer reported a disappeared living follower,
  then emitted one no-contest Warning. This is the known exit-order false alarm;
  it did not interrupt live round 2 or prevent profile restoration.
- Redeploy.Core: six stale PerfectCulling sampler cleanup warnings, associated
  with successful native body replacements, not six failed respawns.
- EFT: 56 JetMaterial errors, native Factory spawn-marker repairs, AirPit
  missing-component/weather diagnostics, model/weapon animation diagnostics and
  four initial PFT PlayerBody disposal errors remain in the capture. Their exact
  families/times are available to the source auditor; a timestamp alone is not
  ownership evidence. No preview NRE reproduced in this process.
- Server: profile-display fallbacks, two startup profile-fixer messages and
  repeated Fence armor-slot warnings; no Arena local-start rejection in this run.

With DeveloperMode off, direct client emitters produced Arena **24 Info / 695
Debug / 4 Warning / 1 Error**, Redeploy.Core **44 Info / 6 Debug / 6 Warning**,
and KillcamReplay **64 Info** records. Arena's own successful routine details are
already largely Debug; the effective global `All` setting still writes them.
Server middleware's recurring `/arenamode/client-settings` Info records are
counted separately from Arena's logger. Do not infer a client RequestHandler
polling regression from that server emitter. Technical noise recommendations
and safety-preserving changes belong to the source audit rather than this test.

## Restoration and limits

`verification.json` is green for **each** raid's exact server-entry/exit pair:
Inventory including fast-panel bindings, Skills, Stats, Quests,
TaskConditionCounters, Achievements, InsuredItems and Encyclopedia compare equal.
Health values compare equal after excluding native update timestamps. Each pair
contains **314/314 inventory items**. The prelaunch backup also preserves the
state before SPT's ordinary startup profile fixer; that startup performed two
StackObjectsCount repairs before any Arena raid, so per-raid comparisons use the
actual server-entry snapshots instead of attributing those fixes to Arena.

All **12 PFT store files** and the KillcamReplay/PFT config files are byte-equal
to their backups. All **224 Arena settings** match their baseline, including
Deathmatch ReplaySeconds=0, GameMode=teamdeathmatch, Tactical RoundSeconds=240
and TeamOwnGear=true. Server/Core hashes remain unchanged.

Final status confirms MainMenu, no raid, no active godmode/infinite-ammo/aim/walk
hook, no control lease, and matching loaded code. GPU2 has been released. No
personal profile, new companion, external mod disable, server-data edit or
additional host was used.

This is representative functional and logging coverage, not exhaustive all-map,
all-kit or long-duration testing. It does not certify a fixed minimum frame rate,
prove that unreproduced historical preview errors are gone, or assert that the
remaining native warnings are harmless merely because these samples completed.
