# Forge whitelist audit — 2026-11-21

All **68 default policy entries** were reconciled against the current public Forge
API: 63 mod entries (including the Content Backport patcher) and five diagnostic
tool entries (including the LogTools patcher). The existing five explicit denials
were not changed. This is a publication, release-metadata and source-diff audit,
not a new in-game compatibility test.

Seven mod records change, affecting eight client/server version fields. Versions
are the actual BepInPlugin/SPT ModMetadata values, not necessarily the download
label or assembly version. Current applicable SPT 4.1 releases take precedence over
later-published SPT 4.0 backports; publication-time anomalies are retained below.
No game files, foreign mods, profiles or user settings were installed or changed.

## Updated runtime reviews

| Mod | Previous | New | Scope of reviewed changes |
|---|---|---|---|
| [StashFilter](https://sp-mod.com/mod/2907/stash-filter) | client 1.0.0 | 1.1.0 | Read-only stash price/weight sorting and grouped-item corrections; unique Harmony IDs. |
| [UIFixes](https://sp-mod.com/mod/1342/ui-fixes) | client/server 6.0.3 | 6.0.4 | Edit Build weapon-body acceptance and assembled tooltip; reload/body hooks unchanged. |
| [give-ui](https://sp-mod.com/mod/1617/give-ui) | server 5.0.0 | 5.0.1 | Server metadata update; external GUI decompression/response disposal fixes. |
| [Better BepInEx](https://sp-mod.com/mod/3207/better-bepinex) | client 1.0.0 | 1.0.4 | Menu input ownership, Escape/click suppression and setting presentation. No retained physical player reference found. |
| [HUD Breakout](https://sp-mod.com/mod/3148/hud-breakout) | client 1.2.0 | 1.4.1 | Effect duration, scale/ultrawide placement, equipment display and optional Fika extraction visibility. Resolves the current main player each update. |
| [WebOverlay](https://sp-mod.com/mod/2879/weboverlay) | client 1.11.0 | 1.12.0 | Additive page-driven move/resize and bounds fixes within overlay lifecycle. |
| [ArmorClassIcon](https://sp-mod.com/mod/2934/armor-class-icon) | client 1.0.0 | 1.1.1 | Trader/flea/accessory badges, presets, examined/search-state visibility and requested settings refresh. Weak UI caches, no retained body. |

Actual official release DLL metadata was inspected for every changed side. No
new body adapter or profile-protection change was identified in these seven
deltas. Their earlier native results apply only to the previously tested versions;
this audit does not establish new runtime acceptance. In particular Better
BepInEx's input handling and HUD Breakout's rendering changes have not been
retested during respawn here. Fika admission is unchanged.

## Newer releases deliberately not marked reviewed

### Tarkova Cola 4.0.0 — retain 3.0.1

[Forge](https://sp-mod.com/mod/3088/tarkova-cola) published 4.0.0 on November 21
for SPT 4.1.6. The [release source](https://github.com/Yendaxddd/TarkovaCola/tree/7163afb46ed8732d83dc26f3415e252bb5bf6c91)
adds six perks, a changed research/achievement catalog, a downed state and a third
weapon slot. Arena's existing exact catalog fingerprints reject this version
before activation; raising the whitelist number would not repair that contract.

There is also a new delayed Tombstone offering refund: `PerkService._pendingOffering`
is flushed after returning to a server-safe menu through `/tarkovacola/offering`,
then `OfferingStore.Send` mails items. Existing Arena capture/restore covers the
older refund/native/claim queues, and the server patch covers `RefundStore`, not
this new mail path. Simply accepting the new catalog would therefore leave a
profile-affecting gap. Required follow-up: catalog/reward semantics, offering
queue and mail protection, new perk/body-state cleanup, then protected native
entry/respawn/exit and whole-profile verification. No bypass was added.

### Fontaine FOV Fix 5.0.1 — retain 4.1.0

[Forge](https://sp-mod.com/mod/701/fontaines-fov-fix) now requires
[SPT Realism Common Lib](https://sp-mod.com/mod/3135/spt-realism-common-lib),
currently 0.1.0. The official FOV DLL confirms the hard dependency
`com.fontaine.spt-realism-common-lib`, absent from the default whitelist.
The [4.1.0-to-5.0.1 diff](https://github.com/space-commits/SPT-FOV-Fix/compare/v4.1.0...v5.0.1)
retains the FOV controller and the hooks Core currently reflects; this audit does
**not** demonstrate a FOV contract rejection.

The new dependency creates nine body-scoped controllers from local `Player.Init`.
Several unsubscribe from static events only on `OnDestroy`, so retained outgoing
bodies can keep callbacks after the globals point to a successor. `GearState`
unsubscribes through the current global weapon-state publisher rather than the
original one; old `ReloadState` instances read the new global player's animator.
These are source-level lifecycle risks, not reproduced native failures. Required
follow-up belongs in Redeploy.Core: instance ownership, retired/cancelled-body
fences, creation rollback, and real death plus retained-live-body tests with
ADS/zoom/reloading. Arena-specific movement/reload effects also need review before
marking the dependency chain reviewed. **Keeping 4.1.0 in the policy does not block
FOV 5.0.1:** version differences are advisory, and the existing dependency closure
pulls an installed unlisted hard dependency into the session with a warning
(`ArenaWhitelistDecision`, dependency closure). Thus an installed FOV 5.0.1/Common
Lib combination can currently load despite the missing explicit Common Lib entry.
This review records the lifecycle risk; it does not introduce a new deny rule or
change the user-approved advisory/dependency policy.

## Package and publication exceptions

- WTT Armory: package **3.0.0**, actual client/server **2.0.5**; preserve the
  [separately tested runtime review](WTT-ARMORY-20261121.md).
- Healing Autocancel: package **5.0.1**, actual client/assembly **1.1.0**, confirmed
  from the current official DLL; preserve 1.1.0.
- LowAmmoCue: current SPT 4.1 release **1.0.0**. Later-published **0.9.0** is an
  SPT 4.0 backport. ArmorClassIcon's later **0.4.0** is likewise a legacy backport;
  its applicable version is **1.1.1**.
- Content Backport: **2.0.7** is the current hotfix, although Forge gives 2.0.6 a
  timestamp one minute later. API order, semantic version and hotfix description
  agree on 2.0.7. Its patcher is admitted by filename, without a version field.
- SurroundAudio: current exact-GUID and partial-name public searches, including
  legacy entries, found no match. Retain local 1.0.0; public currency is unverified.
- Diagnostic tools stay intentionally unversioned. ModProfiler has a public 1.4.0
  release; UiBridge, LogTools and ConsoleToMonitor returned no public match. The
  LogTools filename-only patcher shares that internal tool exemption.
- Forge GUID case/aliases are not substituted for actual runtime identities.

## Complete inventory

`Client / Server` is the resulting runtime policy. `Forge` is the current package
for this SPT generation, not a claim that all its feature paths were tested.

| # | Policy entry | Forge | Client / Server | Result |
|---|---|---|---|---|
| 1 | [Tarkova Cola](https://sp-mod.com/mod/3088/tarkova-cola) | 4.0.0 | 3.0.1 / 3.0.1 | Retain 3.0.1; protection work required |
| 2 | [SAIN](https://sp-mod.com/mod/791/sain-solarints-ai-modifications-full-ai-combat-system-replacement) | 4.5.1 | 4.5.1 / 4.5.1 | Current publication |
| 3 | [APBS](https://sp-mod.com/mod/1594/apbs-acids-progressive-bot-system) | 2.4.1 | - / 2.4.1 | Current publication |
| 4 | [ABPS](https://sp-mod.com/mod/2097/abps-acids-bot-placement-system) | 2.1.0 | 2.1.0 / 2.1.0 | Current publication |
| 5 | [BigBrain](https://sp-mod.com/mod/902/bigbrain) | 1.5.0 | 1.5.0 / - | Current publication |
| 6 | [Waypoints](https://sp-mod.com/mod/827/waypoints-expanded-navmesh) | 1.9.0 | 1.9.0 / - | Current publication |
| 7 | [ArenaMapPort](https://sp-mod.com/mod/3166/arena-map-port) | 0.6.0 | 0.6.0 / 0.6.0 | Current publication |
| 8 | [Killcam](https://sp-mod.com/mod/3174/killcam) | 1.0.1 | 1.0.1 / - | Current publication |
| 9 | [Redeploy](https://sp-mod.com/mod/3173/redeploy) | 1.1.3 | 1.1.3 / 1.1.3 | Current publication |
| 10 | [AutoCorpseSearch](https://sp-mod.com/mod/2730/auto-corpse-search) | 2.2.0 | 2.2.0 / - | Current publication |
| 11 | [DefaultAutoFireMode](https://sp-mod.com/mod/2740/default-auto-fire-mode) | 2.0.0 | 2.0.0 / - | Current publication |
| 12 | [DualSideDoorBreach](https://sp-mod.com/mod/2752/dual-side-door-breach) | 2.0.2 | 2.0.2 / - | Current publication |
| 13 | [LinkedSearchInStash](https://sp-mod.com/mod/2731/linked-search-in-stash) | 2.0.1 | 2.0.1 / - | Current publication |
| 14 | [LowAmmoCue](https://sp-mod.com/mod/3046/low-ammo-cue) | 1.0.0 | 1.0.0 / - | Current SPT 4.1; publication-order exception |
| 15 | [RaidMenuCleanupFix](https://sp-mod.com/mod/2733/raid-menu-cleanup-fix) | 2.0.0 | 2.0.0 / - | Current publication |
| 16 | [StashFilter](https://sp-mod.com/mod/2907/stash-filter) | 1.1.0 | 1.1.0 / - | Updated; source + release metadata |
| 17 | SurroundAudio | - | 1.0.0 / - | No public Forge match found |
| 18 | [UnloadAllMagazines](https://sp-mod.com/mod/2739/unload-all-magazines) | 2.2.1 | 2.2.1 / - | Current publication |
| 19 | [PitFireTeam](https://sp-mod.com/mod/2676/pit-fireteam) | 1.1.2 | 1.1.2 / 1.1.2 | Current publication |
| 20 | [DynamicMaps](https://sp-mod.com/mod/1431/dynamic-maps) | 1.3.0 | 1.3.0 / 1.3.0 | Current publication |
| 21 | [UIFixes](https://sp-mod.com/mod/1342/ui-fixes) | 6.0.4 | 6.0.4 / 6.0.4 | Updated; source + release metadata |
| 22 | [WTT-CommonLib](https://sp-mod.com/mod/2310/wtt-commonlib) | 3.0.8 | 3.0.8 / 3.0.8 | Current publication |
| 23 | [WTT-ContentBackport](https://sp-mod.com/mod/2512/wtt-content-backport) | 2.0.7 | 2.0.7 / 2.0.7 | Current SPT 4.1; publication-order exception |
| 24 | [WTT-Armory](https://sp-mod.com/mod/2246/wtt-armory) | 3.0.0 | 2.0.5 / 2.0.5 | Package/runtime versions differ |
| 25 | [WTT-ContentBackport patcher](https://sp-mod.com/mod/2512/wtt-content-backport) | 2.0.7 | - / - | Filename-only patcher; no version field |
| 26 | [SVM](https://sp-mod.com/mod/236/server-value-modifier-svm) | 2.2.3 | - / 2.2.3 | Current publication |
| 27 | [give-ui](https://sp-mod.com/mod/1617/give-ui) | 5.0.1 | - / 5.0.1 | Updated; source + release metadata |
| 28 | [ContinuousHealing](https://sp-mod.com/mod/1884/continuous-healing) | 1.6.3 | 1.6.3 / - | Current publication |
| 29 | [Continuous Load Ammo](https://sp-mod.com/mod/2112/continuous-load-ammo) | 1.2.0 | 1.2.0 / - | Current publication |
| 30 | [Recoil Rework Legacy](https://sp-mod.com/mod/2190/recoil-rework-legacy) | 2.0.0 | 2.0.0 / - | Current publication |
| 31 | [Fontaine FOV Fix](https://sp-mod.com/mod/701/fontaines-fov-fix) | 5.0.1 | 4.1.0 / - | Retain 4.1.0; new dependency review required |
| 32 | [Manimal Ammo Loading Animations](https://sp-mod.com/mod/2681/manimals-ammo-loading-animations) | 2.0.0 | 2.0.0 / 2.0.0 | Current publication |
| 33 | [Increase Look Direction](https://sp-mod.com/mod/2121/increase-look-direction) | 1.3.0 | 1.3.0 / - | Current publication |
| 34 | [Auto Shoulder Swap](https://sp-mod.com/mod/2639/auto-shoulder-swap) | 1.1.1 | 1.1.1 / - | Current publication |
| 35 | [Dynamic External Resolution Patch](https://sp-mod.com/mod/2200/dynamic-external-resolution-patch-derp) | 1.1.2 | 1.1.2 / - | Current publication |
| 36 | [Player Encumbrance Bar](https://sp-mod.com/mod/1374/player-encumbrance-bar) | 1.3.0 | 1.3.0 / - | Current publication |
| 37 | [Bot Callsigns Reloaded](https://sp-mod.com/mod/1873/bot-callsigns-reloaded) | 2.0.6 | - / 2.0.6 | Current publication |
| 38 | [Magazine Check Interrupt](https://sp-mod.com/mod/2643/magazine-check-interrupt) | 1.1.1 | 1.1.1 / - | Current publication |
| 39 | [Accurate Circular Radar](https://sp-mod.com/mod/1100/accurate-circular-radar) | 1.3.4 | 1.3.4 / - | Current publication |
| 40 | [Simple Crosshair](https://sp-mod.com/mod/1387/simple-crosshair) | 1.4.0 | 1.4.0 / - | Current publication |
| 41 | [Better BepInEx](https://sp-mod.com/mod/3207/better-bepinex) | 1.0.4 | 1.0.4 / - | Updated; source + release metadata |
| 42 | [Use Items Anywhere](https://sp-mod.com/mod/2386/use-items-anywhere) | 2.1.6 | 2.1.6 / - | Current publication |
| 43 | [ImpactCue-UI-MOD](https://sp-mod.com/mod/3029/impactcue-ui-mod) | 2.0.3 | 2.0.3 / - | Current publication |
| 44 | [ImpactCue Web Config](https://sp-mod.com/mod/3029/impactcue-ui-mod) | 2.0.3 | - / 2.0.3 | Current publication |
| 45 | [Amand's Graphics](https://sp-mod.com/mod/592/amandss-graphics) | 1.8.0 | 1.8.0 / - | Current publication |
| 46 | [Enemy Markers](https://sp-mod.com/mod/1858/enemy-markers) | 1.4.0 | 1.4.0 / - | Current publication |
| 47 | [Fast Grenade Throw](https://sp-mod.com/mod/2783/fast-grenade-throw) | 2.0.0 | 2.0.0 / - | Current publication |
| 48 | [VisualAssist](https://sp-mod.com/mod/2213/jankys-visual-assist) | 1.2.0 | 1.2.0 / - | Current publication |
| 49 | [Manimal's Quick Cancel](https://sp-mod.com/mod/2969/manimals-quick-cancel) | 2.0.0 | 2.0.0 / - | Current publication |
| 50 | [HUD Breakout](https://sp-mod.com/mod/3148/hud-breakout) | 1.4.1 | 1.4.1 / - | Updated; source + release metadata |
| 51 | [Picture in Picture Disabler](https://sp-mod.com/mod/2667/picture-in-picture-disabler) | 2.1.0 | 2.1.0 / - | Current publication |
| 52 | [Freecam](https://sp-mod.com/mod/164/freecam) | 1.4.8 | 1.4.8 / - | Current publication |
| 53 | [DoorDash](https://sp-mod.com/mod/2214/doordash) | 1.2.0 | 1.2.0 / - | Current publication |
| 54 | [Anvil-WebOverlay](https://sp-mod.com/mod/2879/weboverlay) | 1.12.0 | 1.12.0 / - | Updated; source + release metadata |
| 55 | [ArmorClassIcon](https://sp-mod.com/mod/2934/armor-class-icon) | 1.1.1 | 1.1.1 / - | Updated; source + release metadata |
| 56 | [Caliber Under Ammo Name](https://sp-mod.com/mod/2586/caliber-under-ammo-name) | 1.2.0 | 1.2.0 / - | Current publication |
| 57 | [Compatibility Highlighter](https://sp-mod.com/mod/2859/compatibility-highlighter) | 1.3.2 | 1.3.2 / - | Current publication |
| 58 | [Trader Modding and Improved Weapon Building](https://sp-mod.com/mod/1283/trader-modding-and-improved-weapon-building) | 2.1.2 | 2.1.2 / 2.1.2 | Current publication |
| 59 | [BetterAmmoLoadingList](https://sp-mod.com/mod/2221/ball-better-ammo-loading-list) | 1.1.1 | 1.1.1 / - | Current publication |
| 60 | [BrighterInteriors](https://sp-mod.com/mod/2613/brighter-interiors) | 1.1.1 | 1.1.1 / - | Current publication |
| 61 | [DevBalaclavaFix](https://sp-mod.com/mod/1937/dev-balaclava-fix) | 1.2.0 | 1.2.0 / - | Current publication |
| 62 | [HealingAutoCancel](https://sp-mod.com/mod/1274/healing-autocancel) | 5.0.1 | 1.1.0 / - | Package/runtime versions differ |
| 63 | [SmajlecLights](https://sp-mod.com/mod/2601/smajlecs-lights) | 1.3.0 | 1.3.0 / - | Current publication |
| 64 | UiBridge | - | - / - | Intentionally unversioned tool |
| 65 | [ModProfiler](https://sp-mod.com/mod/2952/mod-profiler) | 1.4.0 | - / - | Intentionally unversioned tool |
| 66 | LogTools | - | - / - | Intentionally unversioned tool |
| 67 | ConsoleToMonitor | - | - / - | Intentionally unversioned tool |
| 68 | LogTools patcher | - | - / - | Filename-only patcher; no version field |

## Evidence and verification

Raw public API responses, release archives, hashes, decompiled metadata and source
diffs are retained locally under `artifacts/whitelist-forge-20261121/part1` to
`part3`; each `results.json` maps exact input rows to its evidence and source URLs.
The parent reconciled all 68 identities and challenged the timestamp-only selection,
which exposed the LowAmmoCue and Backport exceptions above. No foreign code/assets
are included in the repository change.

Offline verification completed on **2026-11-22**:

- `tools/Run-ArenaVerification.ps1 -ChangedSince 6b3ba6a4` selected 51 harnesses
  plus the unit suite. The corrected full run reported **51 passed / one failed**.
  Unit results: **4,422 passed / three known environment-gated skips**.
- The first run exposed a stale UIFixes 6.0.3 current-install fixture expecting
  no advisory; it was stopped, both current-install fixtures were updated to
  6.0.4, and the complete affected selection was restarted. No assertion was
  removed or weakened; the version-drift and denial cases remain.
- The remaining failure was `VerificationRunnerChecks`: its full baseline and
  54 of 55 intentional-fault checks passed, but `redeploy-exclude-dropped`
  encountered Git's `failed to read .git/worktrees/.../commondir` while creating
  concurrent temporary worktrees. This prevented its required compiler symptom
  from appearing. The unchanged runner's isolated `parallel-redeploy` baseline
  then passed, and the isolated fault was rejected by its exact declared
  assertion and expected `CS0579` symptom. Runner production/test sources were
  not changed. This is a passing targeted retry, not a claim that the broad run
  had zero failures.
- Aggregate coverage is **52/52 selected checks with passing evidence**, including
  that isolated retry. Raw receipts are `verification-run.log`,
  `runner-redeploy-baseline.log`, `runner-redeploy-mutant-confirmed.log` and
  `runner-retry-receipt.json` under the audit artifact directory.
- Exact row coverage, accepted version edits and policy invariants passed the
  separate audit verifier. Independent review checked all changed release DLL
  metadata, source changes, report rows and unchanged deny/version semantics.
  It caught and resolved a temporary text-encoding issue; parent review also
  corrected the publication-order and hard-dependency conclusions above.
- All **80 installed DLL/PDB/default-policy files** retained their hashes and
  membership. No deployment, new native test, push or release was performed.

Source/binary review and passing policy tests do not turn the seven new foreign
versions into newly exercised in-game compatibility results.
