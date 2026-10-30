# Compatibility acceptance on SPT-GPU-2 — 2026-10-20

## Result and exact scope

Current completion status: bounded native feature checks, reviewed removal
recovery, clean startup and final post-stop cleanup verification passed.
Both programs are stopped.

The bounded native tests passed for Pause Continued, the Effects Manager body
handoff, Recoil's physical-body ownership and Skills Extended's XP subscriptions.
ISB's real client package visibly refused Arena while ordinary PMC map selection
remained available. This is not certification of every feature of those mods.
The limitations below are part of the result.

Frozen Arena source: `0caab4e0ac126507911bf90c7f48520f0a4944b3`, version 0.1.2.
The root build/deployment manifest is
`artifacts/compat-second-20261020/integration/frozen-build.json`.

| Artifact | SHA-256 |
|---|---|
| Client | `A01660CC2C03AA06A39E607573121F87C8EDD20551BB27665C992F5C9FCD9BED` |
| BigBrain library | `5CB55F42CA1839698BD209CCE4C60036D8D3ECA42FAF2DFC166B4E584668C22F` |
| Server | `DD7C2784BB5F469375291ABF55B16CE89D1A66D3B13660F8EDC39C14317CFC71` |

Host: SPT-GPU-2, EFT 0.16.9.5-40743, Unity 2022.3.43f1,
UiBridge 0.9.4+42bbfb38. Native actions used `gpu-run`, native Windows input and
UiBridge's documented testing hooks; inspection was read-only. Gameplay setup
did not edit or overwrite raw profiles. The later, narrowly reviewed cleanup
exception is documented below. The ordinary raids ended through real Gate 3 extraction,
not Give Up. Controlled Arena deaths and infinite-ammo/godmode hooks are clearly
separate from the actual firing observations.

| Temporary component | Exact version / scope |
|---|---|
| Pause Continued | 1.5.0 |
| EFT Effects Manager | 1.1.0 library, no plugin GUID |
| Broken Limbs Don't Heal | 1.1.0 plugin |
| Skills Extended | 3.1.4, complete package |
| Recoil Rework Legacy | 2.0.0 client; unrelated server package not installed |
| ISB Special Forces | 2.0.1 complete client subset, including assets/config/prepatch |
| MoreBotsAPI | 2.1.1 full release, only for the ISB client phase |

The working-adapter process (`7ab667e0`, 20:01:29 local start) loaded 45 plugins
and nine libraries. Every reported VM/host/loaded MVID matched. The ISB phase
(`1a0fe3e3`, 20:25:04) loaded 49 plugins/nine libraries, likewise matching.
The fresh Pause visual probe (`5ec1afb8`, 20:35:58) again had 45/nine after ISB
and MoreBots removal. Build, deployment and native evidence remain distinct.

## Observed native cases

| Case | Observed result |
|---|---|
| Ordinary Factory PMC before Arena | F10 paused (`IsPaused=true`, `timeScale=0`) and resumed (`false`, `1`). Effects, lifecycle controller/movement context, both Recoil owners and firearm cache belonged to the current body. Five actual MP5 shots. Native extraction. |
| Labs Deathmatch initial kit | Effects/Recoil references pointed to the replacement body. One actual ADAR shot. Pause attempt remained unpaused, timeScale 1. |
| Deathmatch first controlled death | Distinct live successor, matching Effects/Recoil physical references; three actual Mk17 shots after replacement. |
| Deathmatch second controlled death | Another distinct live successor and correct references; one actual MP-153 shot after replacement. |
| PFT Zombies, Factory, one companion | Friendly companion spawned. Two living-PMC wave refills retained correct Effects/Recoil ownership. Five actual MP5 shots after each refill. Five further native shots killed an infected in wave 2. |
| PFT dead-PMC continuation | Native controlled death entered spectator. The existing 100-second no-progress watchdog retired wave 3; this was **not** a companion combat victory. Wave 4 restored the dead PMC with correct references; five actual MP5 shots afterward. |
| Ordinary Factory PMC afterward, same process | Ordinary pause/resume worked again. Correct Effects/Recoil owners and five actual MP5 shots. Native Gate 3 extraction. |
| Fresh Arena Pause visual check | First F10 with an empty notice queue produced a visible native notice: “Pause Continued is unavailable during Arena matches. Ordinary raids can still be paused.” Eight closely spaced screenshot/inspection probes retained it; 30 scenario steps passed. |

Actual shot receipts contain sequences 1–35 and their game frames, transcribed
in `gpu/native-tool-observations.md`; the owner JSON reports alone do not prove
shots. Their matching object handles do prove physical reference identity within
the observed process. `pft-live-refill1-owners` had one stale inspector Type handle
at its final unrelated step; the full retry passed. The transient PFT retired-body
marker also triggered UiBridge's existing other-human safety gate once; it was
not bypassed and subsequent settled-body shots succeeded.

### Skills and Effects evidence

In the ordinary control after Arena, actual invocation-list entries included
Skills Extended `ApplyMedicalXp`, `ApplyNatoRifleXp` and `ApplyEasternRifleXp`.
A subsequent fresh Labs Arena world had 12 health-effect and two mastery callback
entries; all declaring types were inspected, and none belonged to Skills
Extended. Native EFT and Effects Manager callbacks remained. This demonstrates
selective Arena subscription removal plus ordinary restoration, not a delivered
XP reward.

The real Labs Signals runtime was enabled, made its native raid request and
reported **“Signal cache unavailable: No signal placements configured for this
map.”** Its state was null. A temporary protected-Arena PDA and receiver input
did not create an actual cache/reward. The PDA did not persist after leaving.
No foreign journal was changed to manufacture a reward. Actual Signals reward
suppression therefore remains untested.

Effects Manager retained one real registration, explicitly inspected as
`BrokenLimbsDontHeal.Effects.HealedFracture` (dictionary key plus runtime Type
FullName saved separately). Current player/controller/movement ownership was
checked on initial kits, two death replacements, living PFT refills and the
PFT dead-player continuation. A real fracture followed by native treatment was
not reached in the bounded tests. A blacked limb would not count as a fracture;
registration/ownership evidence does not certify healed-fracture behavior.

### ISB admission and deliberate server limit

The full published client subset comprised 721 files, approximately 973 MB,
not a dummy or DLL-only substitute. MoreBotsAPI was the real 2.1.1 release.
At character selection Arena was disabled and the golden diagnostic explained
that ISB progression is not protected, ISB must be removed/restarted for Arena,
and ordinary PMC/Scav raids remain available. Clicking the Arena model retained
PMC selection; NEXT opened ordinary map selection.

`ArenaMode.Session.ArenaSession.IsActive` and `ArmedForNextRaid` were false.
The successful arm query was `ArmedForNextRaid` in process `1a0fe3e3`, around
20:26:50 local. An earlier `IsArmed` query returned 404 and is not a successful
arm-state read. ISB Aishi and a full ordinary ISB raid were not exercised.

The ISB server phase was intentionally not installed: inspection of the exact
published DLL found profile-load stat migration in `ISBZofiaProfileRouter` and
quest-list changes in `ISBAmnestyQuestPatch.AddVisibleQuests`, outside raid
protection. The source-only `EventQuestProfileMigration` is absent from that
published DLL and is **not** the release finding. See the separate
`review/isb-server-profile-review.md`. Server-only refusal remains offline proof.
Expected ISB missing-server-route errors in the client-only phase are not Arena
adapter failures and do not establish ordinary ISB gameplay compatibility.

## Profile preservation and cleanup

Protected Inventory, Health, Skills, Quests and Encyclopedia hashes matched
before Arena versus after Deathmatch and after PFT. The root independently
compared every PMC/Scav/root field over the same interval: the only difference
was `Hideout.sptUpdateLastRunTimestamp`. The additional PDA probe also retained
all five protected fields. Ordinary controls legitimately advanced raid-related
state and are not covered by an unchanged-whole-session claim. PFT's separate
external persistence files were not independently snapshotted for this leaf.

The existing MP5 was returned through a native drag to its original stash row 20,
column 7, horizontal orientation, with 19/20 plus a chambered round and 100/100
condition. All 315 item identities remained. Native ordinary processing added
`StackObjectsCount=1` to the preexisting dogtag and bayonet; no profile overwrite
was used to erase that normal serialization difference. The temporary invited
PFT group was emptied, Labs participants restored to 16, original Gun Game mode
and Zombie defaults (32/50/20, own gear) restored, and Pause's original F9 restored.

Installation receipts claimed only absent targets. Before each destructive
cleanup, EFT/server were stopped and bidirectional Syncthing index quiescence
was established. Reviewed helpers archived exact files, rechecked paths/reparse
points and hashes before mutation, preserved unknown files, and removed only
empty created directories non-recursively. No sync service or config was changed.

ISB/MoreBots removal: 1,446/10 files across the two roots. The post-drain ended
18:34:47Z (stable sequences 3311578/2009281), followed by exact absence checks.
SAIN baseline/restore proposals remained `changes: []`; no SAIN restore was needed.
Final working-adapter removals: Pause 8, Effects 2, Broken Limbs 6, Recoil 4,
Skills 192 files. Final post-cleanup drain ended 18:45:34Z, with two stable
zero-queue samples, sequences 3314419/2011708. Subsequent checks found **zero**
remaining receipt-owned payload/generated paths on either installation.
All six deployed Arena DLL paths still matched the frozen hashes.

Of 465 preexisting config files across both roots, 463 were byte-identical;
only the two Arena configs changed serialization comments/version header, with
original functional values checked separately. Non-loadable Syncthing-owned
`~syncthing~*.tmp` files were preserved; complete directory equality is not
claimed. The drain observes local incoming indexes plus remote completion;
remote local-error state was not independently queried, so exact UNC absence
and hash checks are an essential additional proof.

The first clean start failed: Skills Extended had persisted the foreign enum
entries `Hacking` and `SignalsIntelligence` in three profiles. Without its
prepatcher, the server rejected those profiles; EFT then failed before MainMenu
with PrepareGameJob/CustomizationLoadOperation errors. Both programs were stopped.
This was an uninstall side effect, not a native Arena replacement failure.

After independent source/binary and recovery review, the root archived immutable
original copies and removed only the two foreign, zero-progress tail rows from
each of the three profiles (six rows total). Every other byte was preserved;
there was no whole-profile rollback. The six native-but-inactive skills added
by Skills Extended to the test profile were retained. Root evidence lives in
`integration/skills-residue-recovery/`: dry-run/review, applied result, sync
checks and both-root physical hashes. This is the explicit cleanup exception to
the no-raw-profile-edit rule used during gameplay setup.
The archived originals are pre-repair copies, not complete pre-install baselines.
One non-test profile had no direct pre-install snapshot; its provenance relied
on the successful earlier native profile load and the exact extension contract.
The review records that evidence limit. An initial post-drain hash check had
not yet matched both roots; no repair was repeated. A fresh drain followed by
two complete physical checks ten seconds apart verified all six resulting files.

The final server start at 19:05Z completed its profile-load phase with no
unknown-enum/profile-rejection errors. Its log enumerated 19 JSON files and
reported `Saved 3 profiles`; those counters do not independently prove 19 loaded
profiles or three physical writes. EFT
instance `3a97954c` (21:06:40 local) reached MainMenu with exactly the original
41 plugins and six libraries, all VM/host/loaded MVIDs identical. Arena was
available again, and native selection reached its map screen without a raid.
The native server settings report at 21:07:37 restored Labs participants 16,
Gun Game, and Zombies 32/50/20 with own gear. Both Arena configs had all 232
original functional values and no added/changed key; only comments/header
serialization differed. The clean-start client log contains no Error entries.
Both programs were then stopped gracefully. After the final synchronization
drain, the root ran the independently reviewed installation verifier twice,
ten seconds apart. Both runs confirmed zero receipt-owned files, 463 unchanged
config files plus two Arena configs with identical values, all six frozen DLL
hashes and synchronized canonical profiles with no foreign skill entries.
Evidence: `integration/final-post-stop-sync.json` and
`integration/final-install-first.json` / `final-install-second.json`.

## Log review and explicit limits

No Pause/Effects/Recoil/Arena adapter exception was observed in the working
cases. Native/asset messages also appeared in ordinary Factory controls:
spawn-marker repair, missing JetMaterial/WeatherObstacle and the Factory
`TRANSITS/Road_to_woods` negative-size BoxCollider warning. A PlayerBody disposal
warning near initial PFT preparation requires its separate lifecycle review;
this leaf does not attribute it to an adapter without evidence. Expected ISB
client-only route errors are separated above.

Not established: actual Signals reward delivery/suppression, native
healed-fracture treatment, server-only ISB gameplay/admission, Aishi native
installation, Tactical-PFT transitions, deliberate failed-replacement recovery,
or performance/general compatibility across maps and future versions.

## Local evidence index

All paths below are relative to ignored
`artifacts/compat-second-20261020/gpu/`; raw profiles and downloaded third-party
binaries must not be committed or shipped.

- `ordinary-before-owners`, `arena-initial-owners`, `arena-respawn1-owners`,
  `arena-respawn2-owners`, `pft-initial-owners`, `pft-live-refill1-retry`,
  `pft-live-refill2-owners`, `pft-death-refill-owners`, `ordinary-after-owners`:
  read-only ownership scenario reports.
- `native-tool-observations.md`: explicitly transcribed direct MCP observations
  for shots and invocation lists, rather than fabricated raw scenario output.
- `pause-notice-native/report.json` and `pause-notice-4.png`: immediate native
  F10/screenshot sequence and readable notice.
- `broken-native-registration*.json`: actual dictionary key and Type identity.
- `isb-client-startup.json`, `isb-client-side-screen.json`,
  `isb-client-arena-refused.png`, `isb-ordinary-map-access.json`,
  `isb-client-not-armed.json`: real client admission and ordinary menu controls.
- `core-test-logs/`: working-adapter, ISB, Pause UI, failed cleanup-start and successful final clean-start logs.
- `clean-start-status.json`, `clean-arena-available.json`, `final-client-settings-cache.json` and `final-config-values.json`: final loaded baseline and restored native settings.
- `before-arena`, `after-arena`, `after-pft`, `before-ui-arena`, `after-ui-arena`:
  local protected-field baselines; raw profiles stay ignored.
- `installed-*.json`, `cleanup-*.json`, archived removed-files manifests,
  SAIN baseline/proposals and `post-cleanup-inventory.json`: exact cleanup proof.
