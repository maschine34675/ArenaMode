# Logging follow-up: GPU2 native verification, 18 October 2026

**N1–N3 passed for the bounded scenarios below.** The intentional PFT exit no
longer produced the old missing-roster/no-contest alarm. Real DM deaths still
played their replays and respawned; opt-in timing and native-object context
appeared only after enabling DeveloperMode. This is not clean-log acceptance:
native model/animation errors remain, with better attribution where captured.

## Candidate and deployment

- Arena runtime candidate `1ffe0b5`, Redeploy `aca6e7a` (Core 1.0.8), KillcamReplay
  `00939e5`. The later Arena `f081c66` changes tests only, not these binaries.
- GPU2 profile SPT-GPU-2 only; EFT 0.16.9.5-40743, Unity 2022.3.43f1,
  UiBridge 0.9.4+42bbfb38; fresh process `4a1afffb`, UiBridge start **14:59:28**.
  Server restarted at **14:59:06.553**, BepInEx log begins **14:59:20**.
- Fresh private backups preceded deployment. The client and server were stopped;
  the paired Arena client used `tools/Deploy-ArenaClient.ps1`; the other four
  binaries replaced only their exact existing installed paths. `gpu-run
  deploy-wait --host host2` checked the six hashes before a fresh `start`.
- Startup/final status confirmed VM, host disk and loaded client/library identity.
  All seven `com.maschine.ArenaMode.nativemodeldiagnostics` Harmony hooks were
  present, including both utility transpilers and the WeaponPrefab finalizer.
  No diagnostics-unavailable/fallback warning occurred.
- BepInEx disk/console `LogLevels=All` and global Harmony logging were unchanged.
  Debug messages therefore **still appear on disk**; severity changes do not
  promise lower disk volume with this configuration.

| Binary | Verified host SHA256 |
| --- | --- |
| ArenaMode.Client | `C5A71C5E644E049CDBA2AC69A9EAF7E499DDBE5118A0CDD22E660B0A9322244C` |
| ArenaMode.BigBrainCompat | `F9BF9215C1CD178B4FE3085E0D93477A64BA20A4FA579D02F0DFAFFCA291B5A0` |
| ArenaMode.Server | `34DD4512A5647EF9D860CC61290466E4F564D311E64792BCD2595E7C5FBB36C4` |
| Redeploy.Core | `94918EDB34DD73C2E5A4B18D62DFD3DFD82E8BD6A57615FE3FCFE141B840A6F1` |
| Redeploy.Client | `9C7DA50A936CFD9ED2BA54EA1F9FEA54B8CFC3FF753DA4A564D5F21520412252` |
| KillcamReplay | `B4AED95EC72F0748D5469F13AB26D13735E2807011A9B433E85EDB95EA55EEBD` |

Evidence is private/ignored under `artifacts/log-followup/`: `deployment.json`,
`start-status.json`, `after/status.json`, `native-diagnostic-hooks.json`,
`verification.json`, `analysis.json` and the paginated merged exports. Backups,
profiles and full logs are not release assets and must not be published wholesale.

## Native scenarios

All times below are host local, UTC+02:00. Raw merged JSON `time` uses UTC.

1. **Factory Deathmatch, eight total participants**, FFA, endless, no kill/time
   limit, killcam enabled. Godmode was explicitly off; infinite ammo was never
   enabled. Native sprint-to-enemy and real trigger actions exercised combat.
   The first ADAR trigger reported **one observed shot**. A later Mosin trigger
   was interrupted by death and reported no observed shot; it is not counted.
2. DeveloperMode initially **false**, Core `EnablePipelineTiming=false` throughout:
   MedDeatH killed the player at **15:02:49.988**. Playback began **15:02:50.786**,
   span **2.94 s**, ended **15:02:57.920**; the replacement arrived **15:02:58.130**.
   Initial kit replacement and this respawn emitted no pipeline timing or model
   context. Normal sampler eviction and recorder/fade detail were Debug.
3. DeveloperMode changed to **true at 15:03:09.395**. Randek killed the player at
   **15:03:31.495**. Playback began **15:03:32.298**, span **2.97 s**, ended
   **15:03:39.446**. Full-health replacement with MCX followed; the Core pipeline
   timing appeared at **15:03:39.518**, its following-frame line at **15:03:39.556**.
   The timing numbers describe this one replacement, not general performance.
   Native DISCONNECT returned normally to MainMenu.
4. **Factory PFT Tactical, four existing invited companions, supplied PMC kit**.
   RoundSeconds temporarily 60; companions kept their gear. Round 1 was live at
   **15:05:55.178**. UiBridge's deterministic native **local kill hook at
   15:06:28.047** exercised spectating and leader rebuild; this is not another
   enemy-caused death. Round 2 became live **15:07:13.365**, four fresh companions
   plus five opponents. The unattended timer then also produced a successful
   round 3 at **15:08:23.943**. This longer-than-planned wait is recorded rather
   than described as a two-round-only test.
5. The AKM kit after rebuilding matched round 1's 19 displayed equipment entries
   and state apart from regenerated IDs (scabbard/secure container retained).
   At **15:09:09.694**, firing into the floor produced **two observed shots**,
   magazine+chamber 31→29: rebuilt hands were actually usable. Normal DISCONNECT
   during live round 3 committed **native Stop(Left) at 15:09:16.393**; normal
   match-end and profile restoration followed. No missing-roster error,
   spurious no-contest or activation failure appeared after that stop.

No new AirPit/TDM run was needed for these logging changes; earlier coverage is
documented separately in [the previous audit](GPU-LOG-AUDIT-20261018.md).

## Successful settings polling

At **14:59:09.817** the server logged the adapter's successful-install Debug
message. The complete capture contains **zero exact `/arenamode/client-settings`
Info requests**, **three `/push` Info requests**, and **176 ordinary `/client/`
Info requests**. Global middleware logging remained active.

Read-only runtime inspection established a functional response, not just silence:
`SettingsSync._appliedRevision=1`, `_reportedOnce=true`; `_nextPollAt` advanced
from **335.819855** to **735.240845** across the test. Applied revision starts at
-1 and only changes after a successful nonempty GET/apply path. No fetch warning
occurred. A VM-to-host direct HTTPS probe timed out; it was abandoned without
network changes and is not claimed as HTTP success. Query/lookalike/failure cases
remain covered by the separately reviewed actual-detour harness, not manufactured
live failed requests. See [server adapter checks](SETTINGS-POLL-LOGGING-20261018.md).

## Remaining warnings/errors and new context

The final merged capture contains **10,675 unique original records**, bounded
**14:59:06.553–15:11:31.445**. Twenty-two structured pages end with no continuation,
`hasMore=false`; all three sources were available on every page. Original files
are archived under `raw-logs/`. No Warning/Error text was cut. One EFT **Info**
Control-settings dump reaches the 20k tool text cap; its full text is in the raw
EFT file. `cutOff=0` alone would not prove that Info text complete.

| Stream | Warning | Error | Attribution |
| --- | ---: | ---: | --- |
| BepInEx | 23 | 0 | Four missing-Fika optional serialization exclusions, four named Harmony warnings, fifteen Unity warnings. |
| Server | 19 | 0 | Ten Fence plate-slot warnings, eight PFT companion profile-preview fallbacks, one trader refresh fallback. |
| EFT traces | 0 | 73 | Native families below; repeated records are not 73 independent gameplay failures. |

There were no Warning/Error emitters from ArenaMode, Redeploy.Core or
KillcamReplay in this window. The 73 EFT records group into: JetMaterial 25,
spawn reconciliation 16, UtilityOperationEvent 8, Benelli foldable 6,
undisposed PlayerBody 4, animation hash-zero 3, custom clipping type 3,
WeaponPrefab.Parent NRE 3, Transit 2, cover path 2, and `error null take to hands` 1.
The old source attribution remains relevant; a mod-free comparison was not run.

The new opt-in context naturally captured **21 records in four families**:

- **Jet material (6):** three MCX pool instances at the second DM respawn and
  three Remington870 pool instances during PFT startup; all captured subjects had
  `activeHierarchy=False`, under `POOLS/.../InactiveGameObject`.
- **Custom clipping (3):** M32 headset LOD1, customType0. The first DM and initial
  PFT samples were inactive pool objects. The later **15:07:13.383** sample was
  active on Bot19. These are observations at capture time; do not generalize the
  inactive samples into an assertion that the family is harmless.
- **Body disposal (4):** **15:05:44.359–.363**, inactive `MenuPlayer` descendants
  of MatchMaker AcceptScreen/GroupPreview/Player2–Player5. The bounded stack
  includes `TarkovApplication.LocalGameCreate → DestroyImmediate → OnDestroy`.
  This identifies confirmation preview bodies rather than a live killcam ghost;
  it does not establish which mod created or mishandled the preview lifecycle.
- **Utility timeout (8):** PFT live-round AI `CheckMagazine` operations timed out
  around 2.50 s, active hierarchy, pool `inPool=False`. Logs report bodyProfile
  none/currentWorldBody unregistered even with an AI owner. Treat that as the
  diagnostic lookup result, not proof that an active bot was actually missing
  from the game registry. No responsible mod is assigned from this evidence.

The three identical **15:05:20.823 WeaponPrefab.Parent NRE records** arose in
`PlayerModelLoader.CreateWeapon → PlayerModelView.Show`, before the later armed
loading phase. The Parent hook was installed, but no Arena context accompanied
these menu events; do not claim that all native errors are now fully attributed.
The native exception remains visible. Likewise, the single null-take-to-hands and
cover-path diagnostics remain unresolved; successful later shots do not explain
their earlier cause. No invalid prefab or animation was deliberately created.

Most Info volume is still external startup output: **6,081 HarmonyX plus 1,171
ModulePatch records**. Killcam retained concise replay lifecycle Info and moved
technical puppet/recorder/fade detail to Debug. DeveloperMode intentionally adds
diagnostic Info while enabled; it was restored false after testing. No wider
Unity log suppression was introduced or exercised as a new fix.

## Restoration and reproducible evidence checks

`node artifacts/log-followup/verify.cjs` exits 0 and records:

- Both DM/PFT per-raid profiles: Inventory (314 items), Skills, Stats, Quests,
  TaskConditionCounters, Achievements, InsuredItems and Encyclopedia identical;
  health values identical excluding the ordinary UpdateTime timestamp.
- All twelve PFT store files byte-identical. No profile/store backup was written
  over the post-test files to manufacture this result.
- All **224 Arena settings** and **eight current Redeploy settings** restored;
  new Core timing setting remains false. Killcam/PFT/Bep configs byte-identical.
  The old seven-key Redeploy raw config was not reapplied over the new setting.
- All **46 unrelated plugin DLLs** unchanged, no extra DLLs, six candidate host
  hashes still exact. Existing assets and other configs were not deployed.
- Native post-raid party UI showed only the local occupied group slot; four
  companion portraits remain, with the checked portrait offering INVITE TO GROUP.
  Final status: MainMenu, no raid, no active godmode/ammo/tracking/walk or control
  lease. GPU2 is free; server and game remain running in the menu.

Useful private reproduction commands:

```powershell
node artifacts/log-followup/api.mjs restore artifacts/log-followup/settings-restoration.json artifacts/log-followup/before/config-224.json
node artifacts/log-followup/export-logs.mjs artifacts/log-followup/final-logs debug
node artifacts/log-followup/analyze.cjs
node artifacts/log-followup/verify.cjs
```

Four-pass self-review: (1) candidate/hash and exact deployment scope;
(2) native event chronology and off/on controls; (3) merged-source completeness,
unresolved errors and conservative ownership claims; (4) protected state,
configuration and menu restoration. Parent independent review remains separate.

The integrating agent subsequently checked the loaded five-module identities
and six-file manifest, independently recounted the complete merged capture and
reviewed its stop/restore positive controls, absence of the former roster alarm,
native-error groups and oversized Info payload. Separate comparisons and a fresh
rerun of `verify.cjs` confirm the protected profile sections, health values,
PFT byte hashes, settings and unrelated files. The saved final status confirms
MainMenu and inactive test hooks. No additional gameplay coverage is implied by
this independent evidence review.
