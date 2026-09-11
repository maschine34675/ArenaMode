# ArenaMode working handoff

Snapshot: 2026-09-11. Read `AGENTS.md` first. This is a compact handoff, not the
conversation transcript or a live synchronization channel. Verify current files,
commit IDs, installed artifacts and logs before treating a recorded result as
current. User-facing communication is normally German.

## Baseline and responsibilities

- **Killfeed readability and quickslot clearance (2026-09-11):** each pooled
  killfeed row now has a dark 88%-opacity background, padded 18px bold text and
  brighter death/other-kill colors. Entries stay fully visible for 4.5 seconds,
  then fade over 1.5 seconds; the six-row pool and six-second lifetime remain.
  Alpha is reset before reuse, and TMP is still constructed while active. Text
  stays within the existing 460px feed width (ellipsis for unusually long lines)
  to contain long lines at the 1920x1080 reference layout. No input targets added. ScorePanel
  moves from 18 to 110px below the top at the 1920x1080 reference resolution;
  the assembly notice follows below it at 190px. The r2 gameplay screenshot has
  quickslots ending around 96px, but other resolutions/modded positions still
  need visual confirmation. Native-menu hiding and extraction suppression from
  `d35d878` remain unchanged. Next GPU check: bright/dark scenery, full and reused
  killfeed, long names, quickslots plus assembly notice, and inventory return.
  Independent review of source `e37c3b6`: no P0-P2. Actual client build passed
  with zero errors and the four existing CS8632 warnings; all 29 embedded icons
  verified. Delivered client SHA256
  `857206A4BF295607130B6F54A2A9CDECDB3F5A92B53784F9C644F7FC5199F1BB`;
  previous client backed up and all 39 protected server/dependency/config/asset
  files unchanged. Receipt:
  `artifacts/hud-readability-delivery/69ce41e9c20d427fb0dfa6245e44a1c3/receipt.json`.
  Game restart required, server unchanged. GPU rendering and mirror not verified.
  Other aspect ratios may narrow the gap to the centered score; check those
  separately. No new synthetic tests for these cosmetic changes. The existing
  r3 panel instruction stays pinned to its original build; do not silently use
  a newer DLL as evidence for that exact-hash test job.

- **r2 GPU acceptance and remaining UI polish (2026-09-11):** report `2a57e7d`
  confirms visible panels, direct-rig grenade counts/two successive G throws,
  armored-rig magazine reserves, F12 adjustments and fresh post-respawn data.
  Named exit lists stay hidden in two Arena starts and return in normal PMC.
  Independently read synchronized final r2 log: 2 panel markers, 2 list markers,
  0 CanvasGroup spam and 0 panel/extraction exceptions. Overall report remains
  bounded: 5 passed, 1 failed, 6 blocked, 2 not run. Screenshots confirm status
  and top-score overlap in inventory and the separate residual green header.
  New source hides Arena canvas drawing on non-BattleUI screens, without
  deactivating its GameObject or stopping ticks. Panels also hide on the early
  native inventory flag and refresh immediately on return. Existing positions
  remain unchanged. Header target independently resolved from installed level46:
  `_mainDescription` contains Image/Icon/State, numeric Clock is a sibling;
  runtime hierarchy guards preserve that separation. Only header activeSelf is
  borrowed/restored, existing successful list masking retained. The original
  two GPU reports stay unchanged. Separate BotWeaponSelector/SAIN/async-upload
  errors are recorded in r2; the available stacks do not establish UI causation,
  and no speculative gameplay fix is part of this UI change. Source `d35d878`
  passed independent native/context reviews without P0-P2 findings. Final checks:
  269 panel assertions / 18 rejected source faults; 44 extraction assertions /
  5 rejected source faults. Actual client build: zero errors, four existing
  CS8632 warnings, all 29 icons verified. Client-only delivery from that clean
  commit, SHA256
  `218F176B15D007DB451893368EC754DDA5669F45C8E96B4F0EE1E67E9BA58C7C`.
  Previous `ED74F678...` client backed up; all 39 protected server/dependency/
  config/asset files unchanged. Receipt:
  `artifacts/panel-r2-followup-delivery/cf79028df6284c57b4e199856d17a666/receipt.json`.
  `GPU-PANELS-TEST-INSTRUCTIONS.md` now pins this build for the focused
  `arena-panels-native-ui-20260911-r3` job and a separate r3 report. Neither
  GPU mirror nor native visual/input behavior of this follow-up is verified here.
  Game restart required; no server update/restart. The new screen-visibility
  harness covers the helper/context, not actual ArenaHud.Tick execution or native
  rendering; continued event ticks were source-reviewed separately.

- **GPU panel/native UI failures corrected (2026-09-11):** test report
  `734af45` and the user's follow-up log reject the original UI delivery.
  Independent native diagnosis found two separate regressions: inactive-parent
  TMP construction in ArenaInfoPanels, and rejected duplicate CanvasGroup
  additions on PointsPanel (1,826 lines in the captured user log). Build the
  panels synchronously while active, hide before returning; borrow and restore
  the existing list group's alpha/enabled values instead of adding a component.
  Missing/failing native groups now fail open and are quarantined per list/raid;
  once-only full exceptions and construction/masking markers aid diagnosis.
  Both regression harnesses now model the previously missed Unity contracts.
  Panel tests still substitute TMP views and do not certify rendering.
  Current source includes the completed `3f34f86` grenade-picker fix unchanged.
  Independent reviews found no P0-P2. Source commit `998cef5` on
  `fix/arena-panel-native-ui`; final checks 35 extraction assertions / 3 rejected
  source faults, 248 panel assertions / 15 rejected source faults. Actual client
  build: zero errors, four existing CS8632 warnings, all 29 icons verified.
  Delivered from that clean commit, client SHA256
  `ED74F678CB303D775035FA6A4192387C167214C32B3A6FE6DD4CEE251D5CBEE1`.
  Old `D927D5B9…` client backed up; all 39 protected server/dependency/config/asset
  files unchanged. Receipt:
  `artifacts/panel-native-ui-fix-delivery/07f8e2b9a54f4fe0a308780659959878/receipt.json`.
  Revised `GPU-PANELS-TEST-INSTRUCTIONS.md` pins that build as job
  `arena-panels-native-ui-20260911-r2` and requests a separate retest report.
  Previous GPU failure report unchanged. No new GPU execution or mirror
  verification here; client only, game restart required, server unchanged.

- **Grenade pick after widened reach (2026-09-11):** fixes the pre-existing
  defect handed over by the grenade-row review. A grenade bound and drawn from
  outside the tracked rig/pocket top level (the backpack without the installed
  UseItemsAnywhere mod; nested pouches with it) stayed
  `Equipment.TopPriorityGrenade` after it was thrown, so every tap of G fell back
  to the weapon until hold-G. `ArenaReachPatches.cs` adds a postfix on
  `FastAccessGrenadeItemView.OnItemRemoved` (the pick, or a container holding it,
  left and is no longer reachable: run the view's own `SetNewTopPriorityGrenade`)
  and a void prefix on `PlayerInputTranslator.vmethod_1` (clear an undrawable pick
  so the native no-pick branch runs). Both gate on `ArenaSession.IsActive` alone
  (argued at the code), are enabled through a contained helper, and log once per
  raid. Ordinary raids untouched. New `tools/ArenaGrenadePickChecks`: 123
  assertions, 14 rejected source faults, installed contracts plus 8 rejected
  contract faults. Three-lens adversarial review: no P0/P1; both P2s and the
  P3s fixed or documented. Implementation commit `3f34f86`; staged from it,
  clean (zero errors, the four existing CS8632 warnings), client SHA256
  `FF07FE124952C9D81A958284C8DEA8E83AB303B4243A0D6BCCA336E88A171EA0`, manifest
  `artifacts/double-jump-build/2307d55300984d3886b2406aecd9c2d3/manifest.json`
  (it also contains `18267c8`'s native UI work). **Initially staged only:** the
  installed `D927D5B9…` remained pinned during the first GPU panel job. That job
  has finished; the picker fix is now delivered in the combined `998cef5` client
  documented above. Do not deploy the older staged `FF07FE12…` build over it.
  Chain, residuals and exact GPU steps: GRENADE-PICK-REVIEW.md. Client-only;
  game restart.

- **Native Arena UI polish (initial delivery, 2026-09-11):** **GPU rejected this
  implementation; superseded by the correction above.** It built on grenade-row
  commits `2b86e3a` / `f178906`, which remain intact. The character-selection
  description now uses two short, non-wrapping lines for solo and PFT choices;
  PMC/Scav still replace the text natively. ArenaExtractionList blocks only
  exit-list requests in the exact active game/world/UI and adds its own
  transparent list-only CanvasGroup after synchronous Arena confirmation.
  This catches the startup animation which native SetTime starts before
  OnGameStarted. It does not depend on HudEnabled, touch Armed state, cancel
  native coroutines or suppress the separate extraction countdown/single-O
  clock. Mask removed at end, changed world/UI and shutdown; disabled or failed
  masking leaves native requests available. Independent review findings on
  failed unpatch cleanup and fail-open hooks were fixed and covered by the
  production-linked NativeArenaHudChecks (28 assertions and one rejected
  compiled fault). Existing panel/grenade checks: 247 assertions and 14
  rejected faults; actual client build and all 29 icon resources verified.
  Delivered client-only from clean commit `18267c8`, built in the separate
  `ArenaMode-NativeHud` worktree to exclude the concurrent uncommitted
  ArenaReachPatches/grenade-picker work in the main checkout. Installed SHA256:
  `D927D5B98D654BE63DDB25BD41727408FC86C1B488165C7666D283898F512ABE`.
  Previous client `ACE467B4…` backed up; all 39 protected server/dependency,
  configuration and asset files unchanged. Receipt:
  `artifacts/native-hud-delivery/df831be51ae4485893f69851ef384d2a/receipt.json`.
  **GPU job:** [GPU-PANELS-TEST-INSTRUCTIONS.md](GPU-PANELS-TEST-INSTRUCTIONS.md)
  specifies this exact build, a separate test profile, P01–P14 with screenshot/
  log evidence, and ordinary-raid restoration. Job prepared, not dispatched;
  GPU mirror and native visual/input acceptance remain unverified. Game restart
  required, no server update. Recheck installed hash if parallel delivery occurs.

- **Grenade row in the weapon panel (2026-09-11):** frag/flash/smoke counts
  split into "ready" (the native tap-G candidate list, which the installed
  UseItemsAnywhere widens to the backpack top level) and dimmed "+N" stored
  (nested pouches etc.). Stays visible with a grenade in hand; hidden without
  grenades. Three new icons with their own hash record `ARENA-ICONS.json`;
  `SOURCE-IMPORT.json` untouched, never rerun the imported generators. Offline
  suite 247 assertions / 14 rejected faults; staged build clean; adversarial
  review found no P0-P2, its P3s fixed. A pre-existing stale-TopPriorityGrenade
  defect in the reach patches (tap-G blocked after throwing a backpack grenade
  via a number key) was handed to a separate task (since fixed, see the
  grenade-pick entry above). GPU acceptance pending, see
  the grenade section of RAID-INFO-PANELS-REVIEW.md. Client-only; game restart.
  Delivered from clean commit `2b86e3a`: installed client SHA256
  `ACE467B448B942420A6A4AF00E95B0055FAE050D7E17DBB5FCBA86E5793CD6A5`
  (previous `6C660F3B…` backed up), server and 38 other protected files
  unchanged. Receipt:
  `artifacts/raid-panel-delivery/c97b2993a80b4e86a87d664bb94d4a9a/receipt.json`.
- **RaidInfoPanels absorbed (2026-09-11):** the main checkout now uses
  `feature/arena-raid-info-panels`, based on `0f9f1e9` and therefore includes
  the hitmarker implementation and completed parallel Solo Tactical changes.
  Both weapon/reserve and health/armor/status panels are now match-owned
  modules on Arena's canvas, using its font and outline style. Their complete
  source, 26 unchanged generated icons and four generators were imported from
  the user's RaidInfoPanels source `55ee600`. No standalone plugin/reference
  or additional gameplay patch. F12 settings live under `HUD Panels - ...`;
  all 17 current values migrate as defaults from the old CFG, with existing
  Arena settings taking precedence. **Keep the legacy CFG in place until at
  least the first new game start.** Exact local-body gates hide these panels
  in ordinary raids, on death/respawn and during spectating; match teardown
  releases their caches. Weight warnings use the body's actual Arena limits.
  Armored rigs already count through `TacticalVest`; the old "Armor-Vest-Grids"
  note referred only to the separate `ArmorVest` slot. Clarified and covered
  by the production-counter regression. Parent verification: 180 assertions,
  seven controlled source faults; real client build/resource checks pass.
  See RAID-INFO-PANELS-REVIEW.md and tools/RaidInfoPanelChecks.
  Verified complete source/Git/installed-artifact backup:
  `D:\SPT41\Development\_archive\RaidInfoPanels-2026-09-11-f8c969a4dffa4335987a02ade57e66fe`.
  Only the user-named local project and installed standalone DLL are retired;
  GitHub remote stays unchanged. **Delivered and retired:** clean source
  `1e6d350`, client SHA256
  `6C660F3B3F8B39ED6B54F3E9960605D4A437FA63CE277A29DC0D7DC7FB190B72`.
  Build: `artifacts/double-jump-build/b7ea9d9beaaa47ee9c22873495191521/manifest.json`.
  Delivery/previous-client backup:
  `artifacts/raid-panel-delivery/9b26d7afe2414856bc9d2e5c62b24440/receipt.json`.
  The local RaidInfoPanels checkout and old installed DLL are gone; retained
  legacy/Arena CFGs, server/dependencies and existing audio/drafts are unchanged.
  **Game restart only.** GPU layout/migration/playtest and
  mirror completion are not claimed; test both panels over kit changes,
  death/respawn, a second raid and an ordinary raid.

- **Example hitmarker audio (2026-09-11):** implemented in isolated worktree
  `D:\SPT41\Development\ArenaMode-HitmarkerAudio`, branch
  `feature/hitmarker-audio`, now based on `b7678ef` so the completed parallel
  Solo Tactical fix below is preserved. This feature is also included in the
  current main checkout through the RaidInfoPanels branch described above.
  Three adapted Kenney CC0 hit/headshot/kill WAVs, unchanged original pack
  notices and source/hash/conversion manifest are bundled. F12 Audio:
  `HitmarkerSounds=true`, `HitmarkerVolume=0.5`, independent of Interface,
  announcer and visual HUD. Exact local hostile direct damage only, 40 ms
  coalescing, kill priority, no queued stale audio or paused tails. Shared Master
  adapter has separate announcer/impact instances; existing announcer tests
  and fault controls pass. See HITMARKER-AUDIO-REVIEW.md and
  tools/HitmarkerAudioChecks for tests and pending user listening/GPU checks.
  **Delivered:** clean source commit `d9e7598`, client SHA256
  `0E7AD442F924314B47565AA5755C6766C74FD900ABAAC8AC0FD50ACFF08EDAD6`.
  Build manifest: `artifacts/double-jump-build/f63c503ccbe546b295e6e4c412c858e5/manifest.json`.
  Receipt/backups: `artifacts/hitmarker-delivery/992cd14563c8416e9404be81c958707d/`.
  Seven audio/license/source-record files plus client verified at destination;
  ten protected dependencies and all pre-existing audio/draft files unchanged.
  Offline: 54 hitmarker assertions, 142 existing announcer assertions and its
  17 controlled source faults; three WAVs and original notice hashes also
  verified from committed Git blobs. Client build: 0 errors, 4 existing CS8632
  warnings. Independent reviews complete. **Restart game only**; GPU listening
  and mirror completion unverified. Do not rebuild an older checkout lacking
  these changes over the current integrated client.

- **Solo Tactical: the buy phase waits for a body swap in flight (2026-09-11, 09:58):**
  this commit on `feature/arena-mantle-reload`, on top of the entry below.
  The round-1 buy phase (5 s minimum, or Ready) could end before the
  raid-start kit swap had landed; StartRound then charged the purchase and
  started a second swap on the same body, the base-kit swap landed first and
  the purchase stood down silently — paid, not received, one round late.
  `Tick` now asks the pure `SoloTacticalRoundStartPolicy` once the buy clock
  has run out: it holds while `LiveKitRespawn.IsInitialSwapPending` or (for a
  death in the buy phase, which Tactical never counts as down) while
  `ArenaRespawnDriver.IsRespawnInProgress` with `_playerDown` false, then
  starts; after 60 s it gives up with a warning and never waits again that
  match, so a swap that never exits cannot deadlock the mode. Toast "Round 1
  — waiting for your kit", HUD "BUY  waiting for your kit", buy panel
  "starting when your kit has landed". Five xunit tests on the policy (four
  go red against a policy that never holds, checked by mutation), a
  source-text gate assertion plus a fourth and fifth mutant in
  `tools/SoloTacticalSpawnChecks` (53 assertions, five mutants rejected),
  PitFireTeam / TeamArenaExpansion / RoundLifecycle / Tracer checkers green,
  two independent reviews without a new fault. Installed client SHA256:
  `A79D4F6F7B4E7FA747BF55EA71E8F521EB08498F74F1126CC00D75166B6A8638`.
  **Restart game only.** Playtest: solo Tactical, BuySeconds 5 and
  KitPreloadInLoadingScreen Off (cold kit), press Ready right after the raid
  starts: expect the waiting toast/HUD, then the base kit landing, then the
  round; the log shows "waiting for the body swap in flight" and "settled
  after N s". Then the same with a kit bought before Ready: it must land at
  round start and be charged once. Known and left alone: a non-buying
  round-1 survivor gets the in-place base-kit swap and, a tick later, the
  usual round-break re-issue at a spawn point with protection — the same
  sequence every warm-kit round 1 already had.

- **Solo Tactical: the raid-start kit now counts as worn (2026-09-11, 07:28):**
  this commit on `feature/arena-mantle-reload`. Pre-existing defect found by
  the adversarial review of `af3647d`, not caused by it: the solo kit provider
  fell through to `GetBaseKit` for the raid-start swap, but nothing recorded
  that kit in `_worn`, so a round-one survivor who bought nothing was handed a
  NEW random draw at every round break (RandomKit on; `PickRandomKit` never
  repeats the last draw, so a different kit was certain) under the "you keep
  your gear" toast. A configured kit hid it, which is why the first rounds
  played never showed it. The provider now records the base-kit draw as worn
  at hand-out time; the dead `_bought = Affordable()` store at match start is
  gone (EnterBuy cleared it before anything read it), so Tactical still opens
  on the base kit as `KitPreloadPlan.OpeningKit` and its test require.
  Clean Release build, tests 531 green / 2 skipped, two independent reviews
  without a new fault. Installed client SHA256:
  `DD27FDC4F58C4A8788A03811B93321CC8F8C8489C97E2D4E843E8CC03D66BA26`.
  **Restart game only.** Playtest: solo Tactical, RandomKit on, survive round
  1 without buying; round 2 must re-issue the same kit, and the client log
  must show one `Arena kit drawn:` line per match, not one per round.
  The round-1 buy-phase race both reviews named is fixed by the entry above.

- **Airborne mantle user acceptance (2026-09-10, 20:37 retest):** user confirms
  the tested lower climbs now work; higher places no longer trigger. Five logged
  native entries at 0.676-1.302m remaining height all have nativeError=false,
  restored 1.6m capsules and positive post-exit height. Four samples are grounded;
  the first is already jumping again. Late higher requests at 1.617-1.703m are
  rejected by the 1.31m cap. No Error-level lines in the current snapshot.
  Preserve the working `9a550a5` implementation and 0.5m actual approach gate;
  greater remaining height would be a separate physical/animation extension,
  not an unverified limit increase. See AIR-MOVEMENT-REVIEW.md for frozen log
  path/hash and scope of acceptance. This does not certify every ledge or the
  still-unrun normal-raid/respawn regressions. Documentation only: no new build,
  deployment or gameplay changes. Installed client hash remains the one below.

- **Actual mantle approach correction (2026-09-10, 20:13 retest):** user reports
  unchanged drops despite `b0a8664`. Nine native entries have valid remaining
  heights. Seven apply their requested motion in full; several start 0.83-1.47m
  from the selected surface while the clip travels only ~0.55m forward. The
  native velocity-shifted grid accounts for the difference. Near 0.4m attempts
  3/7 remain above entry; attempt 4 is also above after further movement, so
  distance is a supported mechanism, not a guaranteed outcome classifier.
  Snapshot has no Error-level lines. Frozen log/table/hash and limitations are
  in AIR-MOVEMENT-REVIEW.md.

  Implementation `9a550a5` on `feature/arena-mantle-reload` additionally requires
  actual planar body-to-selected-surface distance <=0.5m before extra airborne
  Climb. Held input retries, including final dispatch refresh; height remains
  separate. Native Vault preference, standing reach, physics/global grid and
  reload handling stay untouched. The selected sample need not be the nearest
  edge: fast approaches may wait for wall contact and a slower refreshed scan.
  No forced positioning or physical success claim.

  Full **374 assertions / 51 source faults**, independent review without P0-P2.
  Evidence: `artifacts/air-vault-checks/d0e7fd88bda647e78e6cf04ae4148f81/`.
  Clean build from `9a550a5`: zero errors/four existing nullable warnings;
  manifest `artifacts/double-jump-build/048d51fd92ca47f786239a08e5c33693/manifest.json`.
  Verified local client SHA256:
  `4018D0A24643C187B6DA713C2C20C9CCE452AAE4C62650FA72E56B42B957F7E8`.
  Receipt/verified old-client backup:
  `artifacts/mantle-distance/delivery-9a550a5-7299471308264575bc06d8ae5c60b59c/`.
  Same ten protected files as below stayed unchanged. **Restart game only** after
  existing mirroring; GPU mirror/result unverified, no push/merge or GPU launch.
  Test the same ledges with held vault during jump, slow/fast approach and a
  normal standing climb; inspect motion plus physical-approach rejection logs.

- **Mantle physical follow-up / reload accepted (2026-09-10, 19:40 retest):**
  implementation commit `b0a8664` on `feature/arena-mantle-reload`. User confirms
  reload speed is now correct and understands the UIFixes in-place swap pause;
  reload code stays unchanged. The log proves eight Unity Climb entries, but the
  user usually falls back down. Native clips top out at 1.30m vertical travel;
  Arena had admitted up to 1.70m. Additional airborne Climb now waits for at most
  1.31m remaining height, so held input can retry as the jump rises. Standing
  reach and Vault preference remain unchanged. This corrects a proven unsupported
  range, not a proven explanation for all eight drops. See AIR-MOVEMENT-REVIEW.md
  for native asset/grid-coordinate evidence and the frozen log hash.

  New read-only `Arena mantle motion #...` diagnostics capture accepted geometry,
  requested versus actual movement, capsule/rise and exit/0.75s post-exit state.
  Twelve attempts per world, eight-second observation timeout; no position,
  collider, gravity or native-state correction. Future logs can distinguish a
  blocked displacement from an over-distant ledge or post-animation drop. The
  separate optic-icon and SAIN unload NREs do not establish a mantle connection.

  Full checks passed **363 assertions / 47 source-fault controls**; independent
  source/production review found no P0-P2. Evidence:
  `artifacts/air-vault-checks/a17fde8e2deb4c0580e27b3ea4918584/`.
  Final clean staged build from `b0a8664`: zero errors/four existing nullable
  warnings, manifest `artifacts/double-jump-build/f203e355b8774806b8142ffa687514e8/manifest.json`.
  Verified client-only local delivery SHA256:
  `51DFE284308A34DD65877CB0FA40AC0E812455CA5086597DDE7DC9AAECC84827`.
  Receipt and verified previous-client backup:
  `artifacts/mantle-physical/delivery-b0a8664-7e2c5618056e45118e8d5b6c0e201b9a/`.
  Ten protected files unchanged: Arena server/BigBrain, three Redeploy DLLs,
  APBS DLL/config, both UIFixes DLLs/config. **Restart the game only** after
  existing mirroring. GPU mirror completion and actual physical mantle success
  remain unverified here; no game launch, push or merge. User test: the same ledge
  with held vault during a jump, plus lower/higher ledges; if it drops, inspect
  entry/exit/after-exit motion records. Ordinary-raid/respawn isolation remains
  a live regression check, not something offline collision doubles can prove.

- **19:09 retest fixes: in-place reload and stale falling flag (2026-09-10):**
  still on `feature/arena-mantle-reload`, with separate implementation commits
  `d968bda` (mantle) and `d71fa13` (native magazine swap handling).
  The new log contains 78 reload diagnostic lines for 13 regular reloads with
  verified Unity multipliers, below the 96-line budget. User confirmed normal R.
  Installed UIFixes routes R through native PullOutMag/InsertMag operations if
  there is no free magazine slot in the rig. Those two exact operation types were
  excluded from both speed and diagnostics; they are now included. Native swap
  inventory/callback behavior remains, with owned speed restored before all five
  completion/reset boundaries. No UIFixes dependency or setting change. This also
  accelerates the same explicit inventory magazine actions inside Arena. Direct
  chamber/cartridge work, unknown types, malfunctions, AI and normal raids remain
  excluded. Additional native swap transitions can still make it slower overall.

  All 47 detailed mantle dispatch refusals showed a valid Jump_Move/Sprint owner,
  no transition, IsJumping=true and Landing=false, but FallingDown=true. The native
  MovePlayerState can leave this bool set during a genuine jump; only FallDown.Exit
  clears it. Removed only that unreliable bool guard; exact managed Jump/animator
  ownership, landing, geometry, stamina and hands guards remain. Real FallDown
  stays excluded and the bool is not modified. Alternating queue/refusal reasons
  now have separate log throttles instead of consuming all 100 lines immediately.
  See both movement/reload review documents for native sources and evidence.

  Frozen client log:
  `artifacts/reload-mantle-retest/58b6d72db3324285838ec897c2104dfe/client.log`,
  SHA256 `17D6D61FC0BE42CB95C1ACF509ABCBE84C41AAD41B82323A76E0C56DB1E1355C`.
  The remaining SAIN OnDisable NRE was during world unloading at 19:09:32.723;
  a link to these features is not established. Server snapshot restore/persistence
  had already completed at 19:09:32.503/.541 local, resolving the earlier deferred
  client warning. No additional error-driven match stop was observed.

  Full offline checks: mantle **339 assertions / 41 source faults**, reload
  **779 / 11**, handling **161 / 12**, plus installed native IL contracts. Both
  independent source reviews found no P0-P2. Clean final build from `d71fa13`
  passed with zero errors and four existing shared nullable warnings. Verified
  client-only local delivery SHA256:
  `A15D2BDE379153D5C6C35A92D69EAE641AA0F1C733A444AAB998D223F99714ED`.
  Receipt and verified old-client backup:
  `artifacts/reload-inplace-mantle-falling/delivery-d71fa13-6430d21d42f146e5b6761efd546076cd/`.
  The old client was `97A8F509EB2D574406B669CBA8C753AD0131ACC5ADA5397798D1A05ECAF53B71`.
  Ten protected files stayed byte-identical: Arena server/BigBrain, three Redeploy
  DLLs, APBS DLL/config, both UIFixes DLLs and its config. **Restart the game only**
  after existing mirroring. GPU mirror completion and live acceptance are not
  verified here; no GPU launch, push or merge. Pending user test: R reload with
  a full rig (e.g. an affected kit), factor changes, empty/tactical reload,
  interruption/death and native inventory integrity; mantle at valid airborne
  ledges, real falling/landing refusal, respawn and ordinary-raid isolation.

- **Mantle retest correction and weapon reload diagnostics (2026-09-10, 18:08):**
  on `feature/arena-mantle-reload`. Implementation commits `0244048` and `5f35bc8`
  retain the concurrently added preload correction `ea1a979`. Unity mantle now
  recognizes the actual Move/Sprint jump leaves through each leaf's own initialized
  native movement container. Landing/transition/geometry/hands checks remain;
  bounded diagnostics identify exact rejection stage and native state flags.
  The failed 17:18 archive confirms hooks loaded, 16 misleading unsupported-body
  refusals, five native refusals and two queued requests without dispatch proof.
  Unlogged source hashes mean individual failed attempts remain unclassified.
  See `AIR-MOVEMENT-REVIEW.md` for native asset evidence and the required GPU test.

  The user subsequently confirmed reload acceleration works. Their fast/slow
  weapon list compared different kits at one fixed setting, not matching reloads
  at 1 versus 3. Weight does not appear in the inspected native reload-speed path;
  weapon/magazine/mastery/skill and reload variant preserve native differences.
  Both slow/fast groups bind SpeedReload. No speculative timing replacement was
  made. New bounded `Arena reload diagnostic` lines record weapon template,
  factor, original/written/readback values, real Unity hands speed multiplier,
  refusals and completion. F12 is live; web config is not polled in active Arena.
  Compare the SAME weapon/magazine/reload type with F12 1 and 3 before comparing
  different kit weights. See `RELOAD-HANDLING-REVIEW.md`.

  Offline checks passed: mantle **320 assertions / 39 source faults**, reload
  **724 / 8**, handling **161 / 12**, including installed native IL contracts.
  Independent reviews found no remaining P0-P2. These are host contract checks,
  not Unity physics/clip playback or live transaction acceptance. Native GPU
  mantle and matched weapon timing tests remain pending with the user.

  Clean build from `5f35bc8`: zero errors, four existing shared nullable warnings.
  Client-only local delivery, verified SHA256
  `97A8F509EB2D574406B669CBA8C753AD0131ACC5ADA5397798D1A05ECAF53B71`.
  Verified previous-client backup and full protected-file receipt:
  `artifacts/mantle-reload-retest/delivery-5f35bc8-533972a6d72842a0a35481f2c3675404/`.
  The pre-delivery client was `48B5DB990F22F092FCB12F315DDC80B42AF50EF5BEE8667601817A03F9271D8D`,
  not the older delivery below. Arena server, BigBrain companion, all three
  Redeploy DLLs and APBS DLL/root config remained unchanged during this delivery.
  **Restart the game only.** Mirroring configuration was left alone; completion
  on the GPU machine is unverified. No game launch, push or merge performed.

- **Unity airborne mantle and faster reload delivery (2026-09-10):** on
  `feature/arena-mantle-reload`, based on `5b058ac`. Separate implementation
  commits: `1339f00` Unity airborne mantle, `3e26b57` firearm reload speed.
  Native Unity body support now issues one guarded CrossFade from real jump
  states into the initialized native Climb/Vault state. It retains native hands,
  geometry, stamina, animation events, root motion and collider cleanup. Dispatch
  commits the move; later key release/settings disable cannot strand native entry
  cleanup. Delayed entry retains its observer, while old world/body/controller
  identities cannot take over a new body. FastAnimator remains supported.
  See `AIR-MOVEMENT-REVIEW.md`; actual GPU mantle acceptance is still pending.
  Existing `AirVaultEnabled=true` applies, no new binding. With jump-on-release,
  press/hold vault again while airborne and approaching a climbable obstacle.

  F12 Movement `WeaponReloadSpeedMultiplier` defaults to **1.5**, range 1-3.
  All six reviewed regular reload families (including quick reload, internal
  magazines and revolvers) use separate native arms/body reload baselines with
  skill/injury refresh preserved. Owned values restore on completion, interruption,
  death, controller change and raid end. Ordinary raids, AI, firing, inventory
  round loading, malfunction clearing and underbarrel launchers remain unchanged.
  UBGL has an additional animator and needs separate synchronization validation.
  See `RELOAD-HANDLING-REVIEW.md` for limits and the GPU test checklist.

  Re-executed offline verification on the final implementation: mantle **227
  assertions / 35 source-fault controls**, reload **707 / 6**, existing handling
  **161 / 12**, air fire **92 / 10**, double jump **144 / 8**. Installed native IL
  contracts passed. Independent adversarial reviews: no remaining P0-P2. These
  are contract/ownership checks against native-extracted methods and host doubles;
  they do not prove actual Unity animation/physics or live inventory transactions.
  Final evidence: `artifacts/air-vault-checks/f9bd6fb12acb44cc951892184df9c563/`,
  `artifacts/arena-reload-checks/09cd793bfce940668910f2eb8e0c32d9/`, plus current
  handling/air-fire/double-jump evidence under their usual ignored artifact roots.

  Built from clean `3e26b57`, zero errors and four existing shared nullable
  annotation warnings. Only the client DLL was installed locally, with verified
  previous-client backup and receipt:
  `artifacts/mantle-reload/delivery-3e26b57-6c1dbf29096e4b05a2f7f159db852579/`.
  Client SHA256 `0CF4F9936CF3805471310104B88D582B1016078E06A7939419F579AAF69567A2`;
  previous client `4099C20E4D284650CA62A582801E2D8325F37B533B40EE41F70FEDE01B8C1F06`.
  Arena server, BigBrain companion, all three Redeploy DLLs, APBS DLL and root
  APBS config stayed byte-identical. **Restart the game only; no server restart.**
  Existing mirroring is left in place; GPU mirror completion is not independently
  verified here. No GPU game launch, push or merge performed. Pending user tests:
  airborne ledge climb/vault, double jump away from obstacles, pre/post-commit
  release, weapon reload/interrupt/death/respawn and a normal PMC raid afterward.

- **APBS user TDM acceptance (2026-09-10, 10:53–11:06 local):** user reports
  good behavior; archived `LogOutput-2026-09-10_11-06-50.log` and associated
  server/APBS records confirm successful 2.4.1 migration and adapter activation.
  Customs 5v5 ended normally after 600 playing seconds, USEC 35:22 BEAR;
  player 17 kills/six deaths. Applied range reaches 61–71; APBS's own generated
  bots reach Tier 7 (levels 65/69/71). One initial BEAR ActiveFail recovered
  automatically; no live-play error. Early raid-end deferred warning resolved:
  snapshot restored 11:06:11.928 and protected end persisted at .960 local.
  A single SAIN `BotComponent.OnDisable` NRE follows during pool unloading at
  11:06:12.097; possible indirect Arena involvement is not established.
  Five airborne-mantle attempts still reject UnityAnimatorWrapper. The current
  FastAnimator-only guard agrees with that log; earlier handoff claims about
  Unity mantle support are not current runtime acceptance. Nine of 146 movement
  endpoint samples are below 2m, mostly SeekCover; no proof of widespread stalls.
  See `APBS-COMPATIBILITY-REVIEW.md`, including frozen evidence hashes/location.
  This test predates `5cae5e7` and does not validate its preload optimization.
  Solo legacy modes, protected PFT transitions and ordinary-raid isolation remain
  pending live checks. Diagnosis changed documentation only; no DLL deployed.

- **APBS adapter delivery (2026-09-10):** built and delivered only the server
  DLL from clean implementation commit `503540e`. Receipt, build log and verified
  previous-server backup:
  `artifacts/apbs-compat/delivery-503540e-31dd0bbe0d1746ba94a2bd48d02eeae3/`.
  Server SHA256 `F573C6123444C2764FDEEA0908DF1C0FD7F51EB2EC57228CF25728163777E01A`;
  previous server `2258ADF8BC342ACDC435588D04ED5870C094CF43BF17EFE0912526544F9F1FA4`.
  Clean rebuild: zero errors, one existing MudTabs `PanelClass` analyzer warning.
  Current client retained unchanged:
  `D57398D64766E34FF76673CBCC7B4A22019CA427824F5D45642FBDC220CF341B`.
  BigBrain companion, both Redeploy DLLs and APBS DLL/config also stayed
  byte-identical; complete before/after hashes are in `delivery.json`.
  All three runnable compatibility checks were re-executed successfully before
  commit; independent final review has no open P0-P2. **Restart SPT server.**
  GPU mirror completion and native raid acceptance remain unverified. No push
  or merge; work remains on `feature/apbs-2.4.1-compat`.

- **APBS 2.4.1 compatibility (2026-09-10):** user installed the complete update.
  Work is on `feature/apbs-2.4.1-compat` from `ff18eab`, preserving the newer
  client load-boost work. Only the Arena server changes. The optional exact
  metadata-version adapter restores Arena's request-owned bot level range after
  APBS's new global raid-level/Weighted-or-Delta selection. Native deferred
  parallel waves and per-bot clones retain exact session-entry/arm ownership;
  normal raids, protected PFT, saved companions and APBS config stay untouched.
  See `APBS-COMPATIBILITY-REVIEW.md` and `tools/ApbsCompatibilityChecks/`.
  Full server suite: **516 passed, one pre-existing installed-Factory geometry
  skip**. The installed APBS test runs real native wave/clone/level processing;
  full inventory/cache bodies are stubbed. Actual USEC/BEAR/Scav and enabled
  prestige paths, all selection switches, deferred rearm and exception cleanup
  passed. Source-fault positive control: 13 passed; missing session validation
  then causes three assertion failures including same-token rearm. Installed IL
  and armband checks passed. Final independent review: no open P0-P2 after two
  exception-path fixes. No GPU raid, mirror verification, push or merge.
  APBS root config was still pre-migration and is left for APBS's next startup.
  Delivery source/hash evidence is recorded above. **Restart SPT
  server after delivery; client DLL is unchanged.** Pending user acceptance:
  short finite TDM, one solo legacy mode, saved-companion PFT round transition,
  ordinary PMC raid after Arena, and APBS startup/config migration log check.

- **Balance delivery (2026-09-09, 19:48 UTC):** bleeding code `dc642ec`, duration
  code `6327247`; both binaries were built from clean `6327247` and delivered
  locally with verified previous-DLL backups under
  `artifacts/duration-balance/delivery-6327247/`. Combined clean-build manifest:
  `artifacts/duration-balance/build/1b67304a8c774341b05c8022fb468e26/manifest.json`.
  Client SHA256 `B40F0B08757B1218EB7E5980556E3B4259C7CE39008A253347BD9F3C0930149E`;
  server SHA256 `3933A49BB3B2E5F5228A28619259886E5E18154DAA286EE76C57260ABA3D1440`.
  Redeploy stayed byte-identical (`7FAFDD65...`, full hash in delivery record).
  Final builds: client zero errors/four existing nullable warnings; server
  zero errors/warnings. All declared host/server/regression checks passed,
  including nine difficulty and six bleeding source faults; independent final
  review has no open P0-P2. **Restart game and SPT server.** GPU mirror completion
  and native playtest remain unverified. No push or branch merge performed.

- **Duration-scaled TDM and latest retest (2026-09-09, evening):**
  `DURATION-DIFFICULTY-REVIEW.md` records the 600s USEC 35:17 BEAR match and
  frozen log hash. Low-displacement endpoint samples declined; one bot still
  revisited the known Customs invalid-nav position. Keep that geometry issue
  and the earlier UnityAnimatorWrapper airborne-mantle issue open. No new
  in-raid error or activation failure; protected end completed in server log.
  Finite independent TDM now linearly scales existing pressure and equipment
  against frozen live-play duration, reaching full curve at 80% (5min -> 4min).
  Assembly/countdown are excluded; streak surge/caps remain. PMC impossible
  starts at 64% absent streak. Existing bots are not retroactively re-equipped.
  Native provider-scoped requests carry the exact arm clock without polling;
  one-profile demands, unused cache-reference exclusion and exact PMC backup
  suppression prevent early equipment stockpiles. Partial hook installation
  leaves generation native. Normal raids/unlimited modes/PFT preserve their
  documented boundaries (unlimited pressure live time, gear server raid time).
  Branch `feature/arena-duration-balance` inherits `ea26880` and its prior
  protected-loadout work. Host checks cover the actual clock/pressure/request
  patches plus nine source faults; actual TDM mode has 325 checks and its
  existing 28 source faults/burst regression. Server: 468 passed, one skipped
  installed-Factory geometry fixture. Staged builds succeeded; independent
  final review has no unresolved P0-P2. Delivery/source hashes are recorded in
  a following entry, not inferred from these pre-commit builds. No GPU raid
  run, push or branch merge is included in this change.

- **Arena bleeding balance (2026-09-09):** `ARENA-BLEEDING-REVIEW.md` documents
  native light/heavy bleed HP loss at default x3, configurable under F12
  Character / BleedingDamageMultiplier (1-10). Exact live Arena bodies only;
  bots remain affected while the local player respawns. Existing protection
  vetoes run first. No effect-duration, treatment, profile or ordinary-raid
  changes. Host checks: 130 assertions and six rejected source faults; native
  call-chain review found no recursive amplification and no open P0-P2.
  User GPU acceptance and delivery are recorded separately below/above; a
  successful host check does not certify Unity raid behavior.

- **Latest retest and bot search follow-up (2026-09-09, 19:19–19:29):**
  see TDM-SEARCH-TEMPO-REVIEW.md for the frozen log hash and exact evidence.
  TDM ended normally, USEC 23:13 BEAR. Repeated Freeze/DontWantTo samples have
  roughly 61–62s delays. The new optional exact-SAIN-4.5.1 adapter caps only the
  two native search predicate arguments to 5s for adopted live TDM opponents;
  native priorities and shared Info/personality values remain unchanged.
  Passive actual-mover detail is bounded to one snapshot per bot/raid after
  two low-displacement samples. No teleport or path rewrite. Normal raids/PFT
  remain outside this adapter. This is a new behavior requiring user GPU testing.
  **Airborne mantle still fails:** this run uses UnityAnimatorWrapper, so the
  previous FastAnimator-only fix rejected every logged attempt before queuing.
  The earlier acceptance below is superseded for mantle; no Unity repair is
  included in this bot change. Source branch: `feature/arena-bot-search-followup`,
  based on `5c9d00b` and retaining the later protected-loadout work.
  Offline checks passed: tempo 104 assertions/7 source faults, awareness 620/33,
  actual TDM mode 318 checks with 28 source faults plus burst-preparation
  regression. Independent final review: no unresolved P0–P2. Client staging:
  zero errors, four existing nullable warnings. **Delivered locally from clean
  source commit `acad49e`**, client SHA256
  `302986ADCD914B4140744FE66F185D12A335E424961867DD30548F05777B9861`.
  Clean staging: `artifacts/double-jump-build/e3286d8e79f240eeb9007dee89b0ac91/`.
  Verified previous-client backup and delivery record:
  `artifacts/bot-movement-improvements/delivery-acad49e/`; prior client SHA256
  `52F98D2116CD212E943C022C861A71CF7A3D483D253892DC0F80E4B1A0CF2220`.
  Server (`851409A1...`) and Redeploy (`7FAFDD65...`) stayed byte-identical; full
  hashes are in the delivery record. **Restart only the game.** New GPU test and
  mirror completion are not verified. No push or branch merge was performed.

- **Movement correction and bot-log diagnosis (2026-09-09, later follow-up):**
  the user explicitly reports airborne mantle still fails, including a second
  Space press held after jumping. This supersedes the earlier broad acceptance
  below: absence of errors never proved an actual Jump-to-Climb transition.
  The installed animator graph has no Jump-to-Climb/Vault edges. A client-only
  correction adds a one-shot local native transition with fresh geometry,
  stamina and hands checks; native callbacks retain movement/capsule ownership.
  It supports the actual behaviorless Jump_Loop via its verified Jump_Idle
  owner, consumes Jump only after confirmed target entry, and retains ordinary
  native transitions and exception cleanup. No fake grounding or global graph
  edits. See AIR-MOVEMENT-REVIEW.md and tools/ArenaAirVaultChecks/README.md.
  **Delivered locally from clean commit `8bb4547`.** The client SHA256 is
  `6093E5BB570C879D614EC84D57A29DAA941C91181CF3E60D35AAC9F817046E0D`.
  Clean staging: `artifacts/double-jump-build/daeb40bbc1034da4bb7964776b4e83a7/`;
  verified prior-client backup and delivery record:
  `artifacts/movement-followup/delivery-8bb4547/`. The previous DLL hash was
  `4826B7805D4182C0BC4D14BB4BB34C896BD44D1F9EE145043F7F28E8EF7ADF4C`.
  Final client build: zero errors, four existing nullable warnings. Mantle
  checks: 120 assertions/25 source faults; hip-fire/shared scope 92/10,
  double jump 144/8 and native TDM menu 80/8 passed. Final independent review
  has no unresolved P0-P2 findings. Server and Redeploy stayed byte-identical
  (`2E394A32...` / `6FCE5366...`); full hashes are in the staging manifest.
  **Restart only the game.** GPU mirror completion and native playtest are not
  newly verified. No push or branch merge. New GPU acceptance remains pending;
  test varied obstacle heights and prolonged/double jumps, failed attempts,
  no extra jump after mantle, native standing vault and normal PMC behavior.
  The latest frozen log is `artifacts/movement-followup/20260909-1540/LogOutput.log`
  (SHA256 `E2BFA57FCA2E850D7CF5C0D7661C3EB4B76E23917CDDB67D6061B56C594D0F05`).
  The diagnostic report there finds two bots repeatedly stranded at
  `(464.91,1.05,-43.64)` for about 182 seconds, a source also present in the prior
  test. Cached SAIN OnNavMesh is misleading at that spot; a sampled hit is
  about 0.94m away. It is 24.4m inside the contour and not a spawn, so do not
  remove a spawn or shift the border on this evidence. Wider low movement
  correlates with search delays up to 78.8s; one active-search stall still needs
  actual SAIN ActivePath/corner diagnostics. No AI behavior mutation is included.

- Historical broad movement playtest (2026-09-09, client log 12:28:36–12:43:28):
  user initially reported the requested changes worked. Later explicit mantle
  retests supersede that initial impression. All eleven handling/vault/fire patches enabled successfully;
  no movement fallback, request-error or mantle-timeout diagnostics appear.
  This remains user-observed acceptance for the other three changes with a
  log review, not an independently replayed test of every edge case. The historical delivery entry below records
  the pre-test state. The logged client has the 11:06 build timestamp and later
  quick-slot work; its exact DLL hash is not present in the log. Current checkout
  also includes `35abdeb`, `8cf0391` and `edb2418`; the movement sources themselves
  in that logged snapshot were unchanged since `f53bffa`. Do not treat the currently installed DLL as proof
  of the historical test binary or claim this run tested the later `edb2418`.
  Customs contour TDM reached 5v5 ready at 12:32:05.920, then ended normally at
  its 600-second limit (12:42:08.427), USEC 15:14 BEAR. Local player: 11 kills,
  three deaths; replacement pipelines completed in 0.78/0.76/0.80 seconds.
  One initial BEAR activation became native `ActiveFail` at 12:31:44.172;
  replacement recovered at 12:31:48.848, with no further activation failures.
  Native `BotOwner.Activate` swallows its exception when setting that state, so
  the exact activation cause is not exposed by this log.
  The client's deferred raid-end warning at 12:42:17.148 is resolved by the
  matching UTC server log: native end arrived 10:42:17.211, snapshot restored
  at .230 and protected end/persistence completed at .256. No stuck restore
  is indicated. The sole error is a SAIN `BotComponent.OnDisable` NRE during
  native pool/world unloading at 12:42:17.339, after profile persistence; no
  movement hook is in that stack. Its precise null/cause remains unattributed.
  Retain the isolated activation and unload findings for follow-up, without
  speculative patches. Evidence snapshots and summary are ignored under
  `artifacts/movement-playtest/20260909-1228/`; client log SHA256
  `850047ADE5075C69176A3B14212D38DFF560C811FA15FA8A34575625C5EFA41C`.
  This follow-up changes documentation only; no build, deployment or push.
- Arena movement expansion delivered (2026-09-09, code `f53bffa`, branch
  `feature/arena-air-movement`, inheriting the previous independent TDM flow):
  hard-landing speed-limit duration defaults to 0.5 of native; firearm draw and
  holster animations each default to 1.5 times native speed. Both use scoped
  local-body changes, without altering damage, reload/fire/global animator speed.
  Deliberate native vault input may request a mantle during a genuine jump;
  native geometry/stamina/hands restrictions and animation entry remain in use.
  Conventional firearms may hip-fire during that jump without unlocking aiming.
  A pending mantle takes precedence over double jump and the hip-fire exception;
  stale geometry or replaced raid/body/animator cancels ownership safely.
  All five settings are under F12 Movement and its existing master switch.
  No new key, forced animation, teleport, server/profile/AI/Redeploy change.
  See `AIR-MOVEMENT-REVIEW.md` for native anchors, limitations and GPU checklist.
  Offline checks passed: handling 161 assertions/12 deliberate source faults,
  air vault 70/13, hip fire/shared scope 92/10, double jump 144/8 and native TDM
  menu/settings 80/8, including final original-production reruns. Parent reran
  both delegated suites independently. Four-pass adversarial review has no
  unresolved P0-P2. All eight scoped implementation/delivery gates are met,
  zero unmet/abandoned; native GPU acceptance is explicitly outside those gates.
  Client rebuilt from clean `f53bffa` with zero errors/four existing nullable
  warnings and delivered locally, SHA256
  `375D7AE61F58A5AFE168EEAE4AEA3FB0CC6ADBB52B8F5EB2917FEA9338AA456F`.
  Clean staged manifest:
  `artifacts/double-jump-build/1087c40163954f3fb0779ec9a1d5fe5c/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/air-movement-delivery/e3b1314bf10d4551baf1b7416cf67528/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No GitHub
  push, merge to master, GPU mirror-completion verification or native playtest.
  Compare the client hash in `C:\SPT41` before testing. Airborne mantle graph
  acceptance/timing is unverified; its pending geometry tolerances need a real
  scene test. Walk-off falling and underbarrel launcher fire remain native.
  A potentially inlined native mid-operation speed refresh safely relinquishes
  acceleration for that operation; do not replace this with global speed hooks.
- Independent TDM match flow delivered (2026-09-08, code `94a8c38`,
  branch `feature/tdm-match-flow`): implements
  a complete-roster/initial-kit readiness gate, fresh three-second countdown,
  frozen client kill/time limits (default 50/600s, each zero disables), and an
  immutable victory/defeat/draw result. Native map-screen presets fit inside the
  current mode panel; other modes and PFT retain their controls. Damage/scoring
  are held during preparation/countdown and after result; actors may still move.
  The final scoreboard opens once the native replacement pipeline settles,
  uses existing win/loss audio (draw is neutral text), and offers Return to menu
  or automatic protected exit after 15 seconds. New bot requests/respawn entry
  stop immediately; an already-started body replacement may safely finish.
  Native exit requires the exact game/world/profile and a valid current owner;
  terminal factory failures never route through the generic unsafe-owner abort.
  No global AI pause, SAIN/path repair, server, Redeploy or profile-rule changes.
  See `TDM-MATCH-FLOW-REVIEW.md` and `tools/TdmMatchChecks/GATES.md` for verification
  and the native GPU acceptance matrix. No GPU playtest is implied by compilation.
  The latest user diagnostic run before this feature had 103 awareness samples
  with no invalid-path reproduction; the bounded position diagnostics remain.
  Offline verification passed: policy/native exit/extracted respawn guards
  227 assertions + 15 deliberate source faults; actual TDM runtime 313 + 26,
  preserved extracted burst checks 21 + 8; actual match/give-up 618 + 16;
  actual native-menu rules/views/settings 80 + 8. Each fault must fail its
  intended assertion, and the original production inputs are rerun afterwards.
  Parent independently reran both delegated suites; shared banner (36), area
  menu (397), warmup, native map/access, announcer, PFT/round expansion and the
  server/profile suite passed. Existing Labyrinth fixture was repaired in
  `7e4cf77` by supplying the already-existing area team-size setting; no
  production Labyrinth behavior or assertions changed. Independent adversarial
  review has no unresolved P0-P2. Staged client build passed with zero errors
  and four existing nullable warnings. All eleven scoped gates are met, zero
  unmet/abandoned; these gates explicitly do not certify native GPU behavior.
  Client rebuilt from clean `94a8c38` and delivered locally, SHA256
  `436C770A289480AAB9BC7407CAC5EA4D59A2702EEE97503F660076696969C1D7`.
  Clean staged manifest:
  `artifacts/double-jump-build/df64595a0ce642f7b6fd4a76864555ea/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/tdm-match-delivery/6c5be9363f7e448a87ac64a4e874ed30/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No GitHub
  push, native GPU playtest or GPU mirror-completion verification. Check the
  delivered client hash in `C:\SPT41` before running the acceptance matrix.
- TDM invalid-path positions delivered (2026-09-08, code `f209955` on `master`):
  the user's next log has eight
  sampled invalid paths, six for the same actor; another actor also cannot path
  to that actor. `Arena TDM path detail:` adds one bounded position snapshot per
  physical bot/raid after two qualified invalid-path samples at the existing
  30-second cadence. It reads actual body positions, cached SAIN NavData and last
  hits, signed XYZ offsets, the selected goal's last-known destination and cached
  path status/corner count/length. No sampling, path calculation, cache repair,
  teleport, loading, AI behavior, profile, server or Redeploy changes.
  SAIN's `IsOnNavMesh` and last-on time are cached heuristics, not fresh hit proof;
  last-on age is explicitly not hit age. See `TDM-AWARENESS-REVIEW.md`.
  Installed native getters checked; independent adversarial review has no P0-P2.
  Full staged client build passed with zero errors/four existing nullable warnings,
  without changing installed artifacts. The production-linked verifier passed
  596 assertions, 28 named negative controls and its final production rerun;
  evidence: `artifacts/tdm-awareness-checks/804fdf3544e848378a96f6b8f4023533/`.
  Client rebuilt from clean `f209955` and delivered locally, SHA256
  `DCD43FC0E60049C4F1D86FD22FA2E398C0CC43E5852B2B1AC1FFDC13A6ECB677`.
  Staged manifest: `artifacts/double-jump-build/2b969a3756c2434aa73feac8a0993a35/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/tdm-path-positions/delivery-f9cc8380573d4486ae02bc9bf14de670/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No new native
  GPU test, mirror-completion verification or push. Next test: allow a repeatedly
  failing bot to reach two qualified 30-second samples, then inspect its
  `Arena TDM path detail:` line with the adjoining awareness records.
- TDM search diagnostics delivered (2026-09-08, code `f619b7d` on `master`):
  the user's two Customs team-start
  USEC 5v5 logs confirm completed rosters after 104.141/37.059 seconds, preserved
  slow preparations and working hearing/search in both runs. Remaining sampled
  path failures need better attribution. `TdmSearchAwareness` now adds the selected
  SAIN goal with its own contact/distance fields and the exact cached
  `PathCalcFailReason`. Nullable `DebugShallSearch`, no-goal and `None`/`AvoidGrenade`
  checks prevent reporting retained reasons as a current evaluation. Cue target
  and EFT native goal stay separate. No movement, hearing, loading, profile,
  server, Redeploy or LootOutline changes. User removed LootOutline for now;
  its interaction with hidden/trimmed corpses remains a separate diagnostic lead.
  See the follow-up in `TDM-AWARENESS-REVIEW.md` and ignored log comparison
  `artifacts/log-comparison/20260908-201507/` (both matching server ends confirm
  snapshot restore/persistence; not a new independent profile-diff test).
  Verification: 577 production-linked assertions, 24 named negative controls and
  final production rerun passed, evidence
  `artifacts/tdm-awareness-checks/f607bc3c1aaa44fea1663f71e3b91ee6/`.
  Installed SAIN 4.5.1 contracts independently inspected; adversarial review has
  no remaining P0-P2 findings. Full staged client build: zero errors/four existing
  nullable warnings, installed artifacts unchanged by that build.
  Client rebuilt from clean `f619b7d` and delivered locally, SHA256
  `9C174F3DCEAE8988853B3F6CD0D8E807B6FD9823CA3EA096586D7EBB7C2A1A60`.
  Staged manifest: `artifacts/double-jump-build/d77c04233bd04412a194ca513f7e756e/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/tdm-search-diagnostics/delivery-8f92b6fbfdf048bcaa2f3dc899f9c5d7/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No new native
  GPU test, mirror-completion verification or push. Next test: capture the same
  `Arena TDM awareness:` lines during a stalled search; inspect `sainGoal`,
  goal contact, `shallSearch` and `pathReason` together with movement samples.
- GPU retest follow-up delivered (2026-09-08): `55164e4` was reviewed against
  its archived logs and installed native/SAIN contracts. Three reviewed fixes
  are now on `master`: `8fb502c` supplies a guarded SAIN hearing search cue per
  native contact episode and bounded decision/movement diagnostics; `8ee1b05`
  separates slow bot preparation from the 45-second native-arrival deadline;
  `4a979cf` defers optional kit-catalog loading until initial TDM teams and current
  native kit loading/respawn work settle. The 300-second preparation watchdog
  uses the existing mode-failure path, without retrying unresolved work, claiming
  native task cancellation or adding an automatic raid exit. Actual path/area
  guards, faction capacity, profile/end protection and normal raids are unchanged.
  See `TDM-RETEST-FOLLOWUP.md`, `TDM-PREPARATION-REVIEW.md` and
  `TDM-AWARENESS-REVIEW.md` for evidence and remaining uncertainty.
  Offline verification: all seven runnable gates passed, plus three reviewed
  manual gates (10/10 total, zero unmet/abandoned). Policy 14972 assertions,
  runtime 268, extracted actual async Burst methods 21, awareness 565,
  warmup/native-pool ownership 37, actual optic guards 67; 84 named source-fault
  controls across these suites. All 444 server tests passed without skips,
  existing menu/team/give-up/polygon integration passed, and supplies packing
  passed 1605 checks. Independent adversarial reviews have no unresolved P0-P2.
  Full clean client build has zero errors and four existing nullable warnings.
  **Client delivered locally** from clean code commit `4a979cf`, SHA256:
  `04D47F6DD8B7C9C252C564604819405F89E0ED9F3BD621999032571B8A349C9D`.
  Staged manifest: `artifacts/double-jump-build/907b9b5c983344b78b993bb40a13a63e/manifest.json`.
  Verified previous client and delivery record:
  `artifacts/tdm-retest/delivery-5f45d5094fe54298a490ea46e1f81d9c/`.
  Server and Redeploy remain byte-identical to the retest baseline below.
  **Restart only the game.** GPU mirror completion and new native raid behavior
  are not newly verified; check the above client hash in `C:\SPT41` before testing.
  No GitHub push. The ordinary authorized mirroring workflow needs no new stop
  confirmation. First retest quiet USEC/BEAR 2v2 and cold BEAR 5v5 without player
  shots: observe actual combat, roster time, refills and boundary containment;
  compare first casualties against initial full-roster READY. Early combat can
  delay that latch, but required kits/refills never wait for optional warmup.
  Preserve the profile checks. The 20-second first cold Vanguard load is not a
  proven timeout defect; the two Unity archive-close errors remain unattributed
  and were not suppressed or patched speculatively.
- Native GPU retest of `e2859e8` completed (2026-09-08, `C:\SPT41`): installed
  client `DEFC7DA2...`, server `D7879AEF...` and Redeploy `7D950A47...` match
  the delivery. Both separate USEC and BEAR profiles reached actual full new-preset
  rosters at all sizes 2v2–5v5; the previous navigation abort did not recur. BEAR
  5v5 took 7:03 to fill and BEAR 4v4 3:48, with recovered timed-out requests.
  Bot movement/combat remain inconsistent: both original USEC 2v2 samples, the
  original BEAR 2v2 and BEAR 5v5 stayed quiet in their observation windows. The
  later user-requested BEAR 2v2 firing trial showed movement, a kill and a refill
  BEFORE three own shots; it does not establish that shots awaken stalled bots.
  USEC 5v5, BEAR 3v3/4v4 and legacy contour show combat/refills. Legacy fixed 2v2
  ran 8:06 with three own respawns (1.44/1.49/1.48s), both-side bot refills and
  visible sights. All eleven Arena ends restored and persisted protected profile
  fields unchanged. Human faction labels, sampled replacement rendering, menu
  transitions, PFT Escape/settings and bounded double-jump cases passed. Precise
  movement/geometry and actual PFT group/buy/round/spectator coverage remain
  incomplete; no complete GPU approval. Normal PMC Customs/USP-1 ADS and F9
  separation passed the observed cases; its ordinary LEFT 2:13 gear loss and
  progression changes are documented separately from Arena protection. Two Unity
  archive-close errors before BEAR 3v3 arming are unattributed; no new raid
  camera exception observed. RDP interruptions and restored input bindings are
  documented. Final client setting and Controls comparisons are zero; server
  configuration unchanged. Final RDP display is 1920×1080 (initial 1920×945),
  with DLSS Balanced and other rendering options unchanged. Native game exit
  and owned test-server stop completed; both absent at 18:36:23 MESZ.
  See [GPU-RETEST-2026-09-08.md](GPU-RETEST-2026-09-08.md) and ignored evidence
  `artifacts/gpu-acceptance/2026-09-08_125900-retest/`. Only documentation changed;
  no code/build/deploy, manual profile repair or push during this acceptance.
- GPU report follow-up (2026-09-08): the user authorized implementation from
  report `2327f63`, with double jump merged into the main branch first. `master`
  includes that merge as `227fb10`; all three reviewed fixes are now integrated
  into `master` through code commit `e2859e8` (the source of the delivered DLL).
  `853a2ce` fixes the human PMC scoreboard role; `77469cc` guards the replacement
  grass-camera ordering and optional PFT inactive-menu warmup. Team-start
  readiness now requires the actual 1 PMC + N-1 allies versus N opponents and
  verifies contained bidirectional waypoint connections between existing native
  markers. Fresh final and placement checks reject stale paths; actual bot path
  corners are still checked independently. Legacy areas retain direct validation.
  See `TEAM-START-ACCEPTANCE-REVIEW.md`, `AREA-CONNECTIVITY-REVIEW.md`,
  `ROLE-LABEL-REVIEW.md` and `GPU-ACCEPTANCE-COMPAT-REVIEW.md`.
  Final verification: all seven executable gates passed, plus independent review
  and verified delivery (9/9 gates). Targeted checks: team runtime 369, connectivity
  630, role 174, double jump 143, render/PFT 47 assertions and 54 named source
  controls; ten connectivity source contracts. All 444 server tests passed with
  zero skips and the existing menu/team/polygon regressions passed. Full client
  build: zero errors, four existing nullable warnings.
  **Client delivered locally** in `D:\SPT41` from clean `e2859e8`, SHA256:
  `DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`.
  Server and Redeploy hashes remain identical to the GPU baseline below. Staging:
  `artifacts/double-jump-build/c96304a3078740c78c60610cf5757cca/manifest.json`;
  previous client and verified `delivery.json`:
  `artifacts/team-start-acceptance/delivery-267f829d91bf4dd98c88287380fef267/`.
  GPU mirror completion is not newly verified; check the delivered hash there.
  No GitHub push or native GPU success is claimed. Restart only the game.
  Retest new team starts on both factions at sizes 2-5, initial roster and refills,
  actual movement/boundaries, F9 role, upscaled rendering after respawn and PFT
  Escape/settings menu. Double jump also needs its first GPU acceptance. Only
  the client changes; no server/Redeploy rebuild is required for this follow-up.
- Native GPU acceptance (2026-09-08, `C:\SPT41`): **new team-start preset fails**.
  Expected DLL hashes matched before/after testing at documentation HEAD `62c7b70`.
  All four sizes are selectable in the native 1920x945 menu; 2v2 (including its
  sole retry), 5v5, 3v3 and 4v4 abort after 13 seconds during navigation validation:
  `BEAR starts: PMC 0`. No active new-preset teams, sector/boundary or refill pass.
  Legacy contour 2v2 played for 3:45 with three player respawns, friendly refills
  and working observed optics; preload guard diagnostic occurred, no camera NRE.
  All six Arena ends restored and persisted protected profile fields unchanged.
  Normal PMC/PSO test passed the observed start/ADS/hotkey separation; its ordinary
  LEFT gear loss is documented separately. PFT group and other-faction tests remain
  unexecuted. At that point the Unity render/menu errors were unattributed; the
  follow-up above records source-supported repairs and remaining uncertainty. See
  [GPU-ACCEPTANCE-2026-09-08.md](GPU-ACCEPTANCE-2026-09-08.md) for exact limits/times.
  At the end of this test run, game/server were stopped and only documentation
  had changed; no fixes/build/deploy/push occurred during the acceptance itself.
  Raw evidence and private test-profile backups are ignored under
  `artifacts/gpu-acceptance/2026-09-08_102800/`.
- Integrated into `master` before the navigation fix (2026-09-08):
  `feature/double-jump` / `fcfcd71` adds an optional extra
  jump after a normal local Arena takeoff, using the configured jump binding.
  F12 Movement: `DoubleJumpEnabled` (default true), `DoubleJumpHeightMult`
  (default 1, range 0.25-1.5). Native collision, lateral motion and stamina remain
  in charge; bots/ordinary raids and ledge-fall rescue are excluded. See
  `DOUBLE-JUMP-REVIEW.md` and `tools/DoubleJumpChecks/GATES.md`.
  At feature preparation: 143 production-linked assertions and seven deliberate source faults,
  full staged client build (four existing nullable warnings), independent review
  without unresolved P0-P2. The live test baseline stayed unchanged at that time.
  **Now delivered with `e2859e8`; GPU acceptance still pending.** The user
  authorized integration before correcting the report's navigation failures.
  See the current delivery entry above; server unchanged by the double jump.
- Latest implementation (2026-09-08): `2407351` scopes Arena's cold optic-pool
  callbacks; `6843e1e` adds the separate **Customs contour (team starts)** preset
  with native **2v2–5v5** selection. Choose Arena / Team Deathmatch / Customs /
  that new preset explicitly. USEC starts west and BEAR east; after initial
  assembly, respawns use connected in-area points ranked against live enemies.
  Position/identity reservations protect asynchronous bot and player handover.
  Legacy contours stay fixed 2v2; full-map/PFT/ordinary raid paths are unchanged.
  See `AREA-TEAM-SETUP-REVIEW.md`, its linked scope reports and
  `OPTIC-PRELOAD-REVIEW.md` for limitations and user test steps.
  The final implementation passed 444/444 server tests (zero skips), focused
  production-linked runtime/menu/optic checks and deliberate fault controls,
  full legacy area/polygon regressions and independent adversarial reviews.
  No native GPU raid or actual menu rendering is claimed for this update.
  Both DLLs were rebuilt from clean code commit `6843e1e` into isolated staging
  and installed locally in `D:\SPT41`, with matching SHA256 hashes:
  client `49762EB638B244F3E2D45ADB3BC040CA6A42FBAF895B7A2A3C80B1CC006839E9`;
  server `D7879AEFD593B8CA2915B8F8FAF7106C9F007A6E6BE2D02425D3892AF253AAF2`.
  Previous DLLs, prepared outputs and `delivery.json` are in ignored
  `artifacts/area-team-setup/prepared` in the shared repository.
  **Restart game and SPT server** before testing 2v2 and 5v5, initial team sides,
  several deaths/refills and optics during background kit preloading. Distance
  scoring is not a cover/visibility guarantee, and the broad start sectors still
  need a gameplay fairness check. GPU mirror completion is not newly verified.
  Both code commits are integrated in the shared checkout; continue there, not
  in the temporary `Documents/Codex/AreaTeamSetup` author clone. No GitHub push
  was performed for this follow-up. Normal mirrored delivery needs no repeated
  stop-confirmation prompt; the existing user-authorized workflow is unchanged.
- Latest user acceptance (2026-09-08): `714b80b` now loads and plays successfully.
  The 07:26-07:36 local log confirms contour READY with 17 player/11 bot spawn
  candidates, 2v2 readiness, 9.6 minutes of play, 8 kills/3 deaths, no reported
  boundary or activation failure, and completed server snapshot restoration.
  See `CUSTOMS-CONTOUR-REVIEW.md` for exact observations and the two isolated
  optic-camera/menu errors still worth tracking. This is one successful user
  raid, not exhaustive geometric or long-duration acceptance. No new DLL needed.
- Previous implementation: `fix/contour-corner-carvers`, `714b80b`, based on `922d656`. The user's
  07:04:53 local attempt on 2026-09-08 identifies the exact cause: corner 0's
  navigation capsule reports height 27.7499981 instead of assigned 55.4999962.
  The previous small-rounding hypothesis was insufficient. Physical capsules
  and edge-box checks pass. Corner navigation now uses full-height box obstacles
  via the same size API as edges; physical limits, grid, area/spawn rules and
  server remain unchanged. The boxes carve slightly more conservatively at
  diagonals (up to 0.125 m extra); all navigation readiness checks stay strict.
  See `CUSTOMS-CONTOUR-REVIEW.md` for implementation and subsequent user success.
  The delivered change required only a game restart, no server restart.
  Offline checks: 69,023 enclosure assertions, 13 deliberate fault controls,
  staged Release client build and independent review without open P0-P2.
  Locally installed client SHA256:
  `20D76AA252C3B2939DC17B6FC4E7A2A3B21C614F324D8EBC7512C7B640714167`.
  Prepared DLL/backup/manifest and failure excerpts are in ignored
  `artifacts/corner-carvers/prepared`. Continue in the integrated shared checkout,
  not the old isolated author clones; GPU synchronization is not newly verified.
- Previous follow-up: `fix/contour-integrity`, implementation `922d656`, based on `63affca`. The first user
  polygon raid failed at 06:45:16 local on 2026-09-08: all 28 markers were
  accepted, but the aggregate enclosure-integrity guard failed before navigation
  readiness. Exact capsule dimension comparisons are a suspected cause only;
  native getter values were absent from that log. The client now allows bounded
  0.1-mm scalar roundoff against planned capsule dimensions and propagates precise
  component/property diagnostics through preparation and active-match failure.
  Safety guards, server, contour and spawn policy are unchanged. See the dated
  follow-up in `CUSTOMS-CONTOUR-REVIEW.md`; the next GPU raid remains unverified.
  Follow-up verification passed 67,002 enclosure assertions, 11 fault controls
  and a staged Release client build; independent review has no open P0-P2.
  The client is installed locally, hash `326405FD7021EC2E9A7E60075EE70198851443370307549DA1FBA6D7754C891E`;
  backup/prepared DLL/manifest are in ignored `artifacts/contour-integrity/prepared`.
  Restart the game for this update; no server restart is needed. Continue from
  the integrated shared checkout, not the temporary isolated author clone.
- Previous follow-up: `feature/customs-contour`, implementation `4a1d0f4`, based
  on clean `235b285`. The
  user's 15-point Customs survey is registered as a NEW selectable
  `customs-factory-center-polygon-v1` / **Factory Center (contour)**, independent
  TDM 2v2 only. Choose it explicitly; legacy area IDs/defaults remain unchanged.
  The new preset has only lateral containment: no actor/path/spawn height band,
  physical floor/ceiling or vertical cap carving. Scene-derived side walls cover
  overlapping NavMesh triangles and spawn anchors with padding. The elevated
  sniper marker is excluded by exact identity on both client and server.
  See `CUSTOMS-CONTOUR-REVIEW.md` for scope, staging checks and user GPU tests.
  The implementation is integrated in the shared checkout; continue there,
  not in the old isolated author clone. Client AND server need a restart after deployment.
  Offline verification passed 386/386 tests (zero skips), focused real-data and
  native-menu checks, enclosure/fault-control checks and staged Release builds.
  Review has no open P0–P2. On 2026-09-08, both reviewed DLLs were installed in
  D:\SPT41 and their SHA256 hashes matched the prepared build. Retained client
  and server DLLs plus manifest are in ignored `artifacts/customs-contour/prepared`
  in the shared checkout. See the contour review for hashes; local installation
  does not by itself prove GPU synchronization or in-raid acceptance.
- Current follow-up: `fix/pmc-area-capture`, based on clean `83a26cf`. The user
  found Arena-only capture impractical because they cannot walk outside the old
  contour. F12 `Development - Area outline` now offers `AllowInNormalPmc`
  (default false), in addition to `Enabled`, for measuring in a normal local PMC
  raid. This is a read/draw/explicit-export exception only; normal raid/profile
  consequences remain and Arena borders stay closed. No server/geometry change.
  The production-linked capture checks pass 292 assertions plus nine deliberate
  fault controls; independent review has no open P0-P2. Release client compilation
  uses isolated deployment staging, with live DLL hashes unchanged during tests.
  That capture follow-up needs a game restart, not a server restart; its
  historical offline checks are distinct from user GPU testing.
- Current work: polygon boundaries and an opt-in scene capture/export helper on
  `feature/polygon-arena-areas`, based on clean `e61b6e3`. See
  `POLYGON-AREA-REVIEW.md` for controls, validation and pending user GPU checks.
  The user has no Customs vertices yet and explicitly chose capture help first;
  both existing presets stay unchanged. No draft is loaded into a live match.
  Work was prepared in an isolated checkout outside Syncthing; continue from the
  safely integrated shared checkout after delivery, not temporary author clones.
- Previous work: native area selection on `feature/native-area-menu`, based on
  the clean `ec11aa8` handoff snapshot. See `NATIVE-AREA-MENU-REVIEW.md` for the
  selection rules, verification and pending GPU menu checks. The source was
  prepared outside Syncthing in an isolated checkout; use the integrated shared
  checkout for subsequent work, not that temporary clone.
- Previous gameplay baseline: bounded Customs `a690ce2` on
  `feature/customs-arena-area`, based on `011beb5`. The subsequent user playtest
  reports overall trouble-free operation, with boundary refinement still needed.
  See `CUSTOMS-AREA-REVIEW.md`; this acceptance update changes documentation only.
  Prior Factory prototype `0c54b7745534798a09662511281978ffbb20acc4` includes the
  earlier kits/GPU menu redesign; its core behavior is user-confirmed below.
- Development host: `D:\SPT41\Development\ArenaMode`; no GPU raid execution there.
- User-confirmed GPU host installation: `C:\SPT41`, a mirror of this host's
  `D:\SPT41`. If Development is included, the corresponding repository is
  `C:\SPT41\Development\ArenaMode`. Check that checkout before editing. Mirror
  inspection on the GPU host confirmed bidirectional Syncthing of all `C:\SPT41`
  without exclusions, matching installed hashes, and working native capture/input.
- **The user performs actual in-raid gameplay tests.** Do not assign autonomous
  raids to the GPU session. Its next intended work is UI redesign and automated
  visual/menu checks, with local development/build/review here as useful.
- Use the existing mirrored installation for UI checks; no second full game copy
  is needed solely for testing. Before editing source on both machines, establish
  separate writable checkouts/branches outside any conflicting mirror scope.
- For simultaneous implementation, assign a separate feature/fix and branch to
  each session. Agree who integrates changes and who owns each test deployment.
- Commit every completed change with tests/documentation and
  `Co-authored-by: Codex <codex@openai.com>`. Do not push without authorization.
- Preserve normal raids and profiles. Protected PFT Tactical uses the PMC entry
  loadout and must not receive standard Arena kits or grenade additions.

### User clarification on the GPU host (2026-09-07)

- **Artifact delivery update (2026-09-08):** do not ask again whether the game
  and SPT server are stopped before routine delivery into the existing mirrored
  installation. The user states that their synchronization defers files while
  they are in use and explicitly removed the stop-confirmation prerequisite.
  An offline GPU host is not a reason to hold back local deployment. Keep local
  hash verification and report remote synchronization separately; source/build
  ownership rules below still apply. Restart changed components before testing
  the new version; copying a DLL is not proof a running process has loaded it.
- EFT/SPT is never run on both PCs simultaneously. The user accepts immediate
  mirroring/overwriting of logs and saves; these profiles are disposable test data
  and even their loss is acceptable. Routine menu checks do not require pausing
  Syncthing or restoring all runtime changes for test-data protection. The local
  `SPT41` folder was resumed at 11:03:21 MESZ after the initial menu inspection.
- This test-data policy does not change Arena's profile-isolation behavior or
  authorize autonomous raids. Concurrent source edits/builds still need the
  coordination described below; single-host game execution does not imply that
  two source checkouts cannot be edited concurrently.
- The baseline contains 22 screenshots covering menus, maps/times, all five modes
  and return to normal PMC selection; no raid was started. The initial report's
  claim that BACK was incomplete was incorrect and has been withdrawn. Do not
  treat missing BACK text or other unverified glyph gaps as an established bug.

## Current behavior and recent work

The surveyed Customs contour is now a separate selectable area alongside the
legacy rectangle. It is the first height-unlimited preset; existing box/polygon
presets and capture-export defaults keep their previous semantics. Source draft
JSON remains review data and is never autoloaded. Installed database checks give
17 PMC/11 bot candidate points, not yet a proof of loaded scene connectivity.

Polygon support now accepts simple concave XZ outlines plus enclosing heights.
Walls/grid, floor/ceiling caps, spawn containment and full path-segment checks
share that contour. F12 `Development - Area outline` optionally captures ordered
scene points (Ctrl+F6 aim, Ctrl+F10 feet, Ctrl+F7 undo, Ctrl+F8 export). JSONs go to
`BepInEx/plugins/ArenaMode/AreaDrafts`; capture is scoped to the exact local raid
and does not change settings/geometry for it. Normal PMC capture additionally
requires `AllowInNormalPmc`; Scav/Hideout and ending/invalid Arena identities are
rejected. Ordinary raids retain ordinary equipment/profile consequences; this
helper grants no Arena protection. Export before leaving/disabling.
The user subsequently supplied the Customs draft, now integrated as described
above. Waypoints remains the spawn viewer. CTF, team-size changes and autonomous
raids remain out of scope. See the polygon and contour reviews for test evidence
and what still needs actual Unity/GPU confirmation.

Historical polygon-stage verification at `83a26cf`: 363 complete database-backed tests (no skips),
focused geometry/capture/enclosure and native/TDM regressions, plus 14 deliberate
fault controls. Independent review closed the body/world ownership and visual
layer findings. The user confirmed server and game stopped before deployment.
Final Release outputs are hash-compared with the local installation; this is not
a fresh GPU host hash comparison or an in-raid test.

The previous native picker received the user's "ja passt" acceptance. This is
broad functional acceptance, not proof of every individual PFT/menu transition.

Native area selection now appears below the five Arena mode rows on the map
screen. Choose Arena -> Team Deathmatch -> Customs: the map is directly offered
and selects Factory Center automatically, with fixed 2v2 displayed. No F12
preselection is required. Factory daytime offers Whole map / Factory South;
South remains fixed 2v2. Factory nighttime, Labs and Labyrinth offer Whole map.
Area selection shares the existing F12 `ExperimentalArea` setting. Map/time/mode
changes preserve compatible choices and normalize incompatible ones during menu
selection; leaving independent TDM hides Customs and clears its selected map.
There is no whole-map Customs Arena or PFT area support. Final client/server
arm validation remains strict and never repairs settings. Configured native rows
are constructed before the mode view's PFT repair, avoiding an empty picker from
a stale F12 mode. No repeated native ShowInternal, geometry or gameplay changes.
Edited allowlists and explicit server disables remain authoritative. Restart the
game after this client change; server source and area protocol are unchanged.

This first Customs candidate spans X305..420 / Z-135..-65, with 15 PMC and seven
bot positions in installed data. These are coordinate checks, not live NavMesh
evidence. The northern face can cut a building footprint. In the subsequent
2026-09-07 playtest the user reports that Customs also works without overall
problems, but the boundaries should eventually be refined by hand. This confirms
basic practical operation, not every detailed test case below. Boundary polish,
explicit repeated-respawn/edge checks, Escape/re-entry, menu setting changes and
teardown into ordinary raids remain to be confirmed individually. Its live
readiness gate must reject an unusable component rather than spawn outside.
Factory South geometry remains unchanged; loading/readiness/respawn now share an explicit
PMC-side check so Savage-only Player markers cannot become human spawn choices.
`CUSTOMS-AREA-REVIEW.md` records checks, deployment evidence and the test list.
Historical Customs-stage local verification: 330 server/data tests passed without skips; targeted
client regressions and nine negative controls passed, independent review has no
open P0-P2 findings. Release outputs match all four DLLs installed on D:\SPT41.
At delivery the local Syncthing query reported SPT-GPU disconnected (not paused).
The later user playtest confirms the Customs preset ran there; it is not a fresh
remote hash comparison or a current synchronization-status check. No code,
boundary coordinates, DLLs or installed settings changed in this acceptance update.

Gameplay prototype `0c54b77` on `feature/bounded-arena-prototype`, based on `7298980`
including the GPU menu redesign: opt-in Factory South, Factory daytime,
independent TDM fixed 2v2. See [BOUNDED-AREA-REVIEW.md](BOUNDED-AREA-REVIEW.md)
for the exact area, server/client contract, verification and required user raid
checks. Enable before the raid via F12 -> Team Deathmatch -> `ExperimentalArea`
= `factory-south-v1`; default/disable is `full-map`. Other modes are unchanged.
Server and game need a restart after a gameplay deployment. User GPU playtest
on 2026-09-07 confirms: visible impassable grid, player and bot spawns always
inside the zone, and bots staying inside without observed stalls. This is the
user's report, not a new agent raid or log inspection. Remaining explicit checks:
Escape/give-up, another bounded match, full-map TDM and a subsequent ordinary
PMC/Scav raid with clean teardown/profile behavior. No gameplay code changed
in this acceptance update.
The Customs candidate above follows this successful core small-map test;
CTF, an editor and PFT region support remain unimplemented. The native area picker
is now implemented; its current verification is in `NATIVE-AREA-MENU-REVIEW.md`.

The user confirmed game and development work on the GPU host were stopped and
new DLLs may be installed. This change was prepared in an independent local
clone outside the Syncthing tree, then integrated into the shared repository;
do not resume that old isolated clone as a second source writer. Deployment
hash checks on D:\SPT41 do not by themselves prove completed GPU synchronization.
The local Syncthing query reported SPT-GPU disconnected at initial delivery.
The later user playtest confirms the prototype ran there; no fresh remote hash
or current synchronization-status verification is implied by that report.

Five modes: Endless Deathmatch, Rounds, Gun Game, Tactical, Team Deathmatch.
Team Deathmatch is independent of PFT; protected Tactical 5v5 is a separate path.
Maps: Factory day/night, Labs, Labyrinth; Customs only with its bounded independent
TDM preset. Factory night uses the native time selection on the Factory tile,
not a separate night tile.

GPU menu redesign on `feature/arena-native-menu-options`: Arena's five modes now
appear as five permanently visible native button rows in a left-side panel on
the map screen. Three short rules/respawn/equipment facts replace the mode
paragraph. The selected row keeps a marker after hover ends; native map points,
right-hand location information and navigation stay in place. Factory time
selection is back at its original native position. Player count/difficulty remain
future implemented options, with no placeholders or gameplay changes.

`MODE-SIDEBAR-REVIEW.md` records native row ownership, corrected mode copy, 123
passing selection-state checks and the GPU pass: all five modes, Factory day/night,
Labs/Labyrinth, clean Back/normal PMC maps and exactly one sidebar on Arena reentry.
The disabled PFT row/tooltip presentation and true 16:9 geometry were not tested
on the GPU. Actual captures are 1920 x 945 at native UI scale 0.875. Private
comparison artifacts are in the GPU task's `outputs/Arena-Modusauswahl` and logs
in `work/arena-mode-sidebar`; the final runtime log has no error entries or Arena
warnings. The earlier upward-opening dropdown is historical and documented in
`NATIVE-MENU-REVIEW.md`; its source/licensing research remains relevant.

The earlier character-selection polish at `328665d` aligned the Arena toggle with
SCAV/PMC and added a native-style `SEPARATE MATCH` subheading. At that historical
stage only Arena's wrapper moved; `CHARACTER-SELECTION-REVIEW.md` records its
contracts and evidence.

The character preview at `70a65c0` adds the current PMC's native posed appearance above
ARENA, with a separate fixed M4 USEC / AK-74M BEAR example kit. It moves the complete
native PMC/SCAV columns outward together, restoring them on Close/Show and before
PFT squad management. Model, camera, lights, render texture and bundle retention
have their own owner; no profile inventory or gameplay kit is used or changed.
Preview setup/load failure preserves the working Arena choice. See
`CHARACTER-PREVIEW-REVIEW.md` for native contracts, independent review, production
and fixture build identities, logs and exact GPU coverage. The 29 side/53 flow
checks still pass but do not execute the new Unity/async owner.

On `WIN-Q470OL2A14A`, the production menu run covered USEC/M4 rendering, label
selection, Arena and normal SCAV map/Back routes, repeated reopening, populated
MY SQUAD without Arena, and clean ordinary M60 inspection afterward. A temporary
fixture also rendered the BEAR kit on an unchanged USEC character and verified
closing during delayed bundle loading; the final production source/DLL contains
neither fixture override nor delay. Real BEAR profile appearance remains untested.
Final production standard-M4 smoke passed: all three figures, ARENA label and
description, then clean Back to the main menu. Its saved client log contains no
error-level entries, Arena warnings or fixture markers; game and owned server
were stopped. No raid was started. Captures are 1920 x 945, native UI scale 0.875; private
screenshots/manifest are in the GPU task's `outputs/Arena-Charaktervorschau`.

| Commit | Change | Verification still needed (raid work belongs to user) |
| --- | --- | --- |
| `20462c0` | Standard kits request two RGD-5s, no automatic Zarya. Explicit rig/pocket placement bypasses UseItemsAnywhere's backpack-first `GrenadeThrowingSlots` patch. Custom authored supplies and protected PFT gear are retained. | In-game placement and grenade-key use, including respawn/Gun Game. |
| `1119b56` | Labyrinth Way Up (`labir_exit`) has a 10-second extraction countdown on the Arena raid clone; availability timers remain zero. | Walking past the doorway must not immediately end a match. |
| `f17ae7b` | Read-only diagnostics for stationary foreign helmets/equipment in the item/weapon preview after Arena, in menu/Hideout. **No causal rendering fix yet.** | Reproduce affected preview and collect `Arena preview probe:` plus screenshot. |
| `50d2a89`, `fae3a43` | Restored Tactical preset descriptions and excluded generated reflex sights on empty scope rings. | UI appearance and generated weapon samples. |

The last two investigated Solo Tactical endings were native Labyrinth extractions,
not opponent-spawn failures; the user confirmed walking past the exit door.
Do not reopen that spawn diagnosis without new evidence.

Latest grenade verification: 222 server tests passed, zero skips; 1,605
production-linked supply assertions; both old-placement negative controls failed
at their intended assertions. Default and installed 20-kit catalogs each packed
40 RGD-5s in rig/pockets without displacing original supplies in the offline
grid model. Full grids still skip optional grenades, never spill into backpack.
Independent review found no open issues. These are not GPU test results.

GPU Release/installed hashes after the mode-sidebar client build
(2026-09-07; menu pass completed at the recorded window geometry).
The server binary remains the prior gameplay build; Redeploy.Core was rebuilt
from unchanged source `58abf5876629bb51be808d78ee2cd7a5d34af77a`:

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client | `67B55E0A532BFACE875AF556D6760B1AD454698AB9FE4B4125A8A99350390986` |
| ArenaMode.Server | `FCE6C394DF9184E81FE6D43B0D236380DE71E3BB110708497E2471D395B2DBE8` |
| Redeploy.Core | `F923B0D709908F75969B3101E9F8290CD2BB06DF457F64B1FFF8876A7BBFEA00` |

Hashes identify these built files. A fresh build on another machine may differ;
always record that build's hashes and compare them with its installed copies.

## Continue on the GPU host

1. Open a new session in `C:\SPT41\Development\ArenaMode` if present. Read this
   file and `AGENTS.md`, inspect branch/commit/uncommitted files, and verify that
   references/deployment resolve to `C:\SPT41` and `C:\SPT41\SPT_Runtime`.
   Confirm the latest handoff/commits arrived through the existing mirror or an
   agreed Git transfer; do not create a competing sync mechanism. A new session
   must not assume access to the original conversation or live agent memory.
2. For UI checks only, use a fixed mirrored build with its commit/hash record.
   A complete development toolchain is unnecessary for simply running the game.
   For development, transfer/clone the repositories and dependencies listed below.
3. Use the existing disposable test profile and compare relevant mod versions/settings
   between hosts. The user permits live bidirectional mirroring of this test data
   under the single-host execution policy above; routine menu checks need no
   mandatory profile backup/restore. Keep test configuration changes explicit so
   a difference in mods is not mistaken for an Arena regression.
4. Keep one deployment owner and deliver one coherent build through the existing
   mirror without an additional game/server stop question. Verify installed
   hashes locally and synchronization separately. The user manages restart and
   test timing; identify the actually loaded build when recording test results.
5. Begin with the UI workflow below, not the pending raid tests. For the existing
   preview-contamination investigation, the user supplies the post-match state
   after their manual raid; the agent can then inspect the affected item in
   menu/Hideout and collect the probe. Reopen after ten seconds if throttled.
   Compare clean/affected previews; do not destroy guessed scene objects.
6. Return a report per test run: hostname, source commit, actual DLL hashes,
   mod versions/config changes, mode/map/kit, local time and time zone, steps,
   expected/observed behavior, pass/fail/blocked, and exact log/screenshot paths.
   Save logs before the next launch only when needed as diagnostic evidence;
   routine menu checks do not require log backups. Raw logs/screenshots belong
   in a private artifact location (local `artifacts/` is ignored); commit a small
   redacted report if useful. Never commit profiles, tokens or unrelated logs.

## Next work: UI redesign with automated visual checks

- Run the UI-focused session directly on the GPU host. First inspect available
  tools; if native Computer Use is available, verify that it can capture EFT's
  window and perform one harmless menu interaction. Browser-only tools cannot validate the native Unity
  UI. Native game capture/input were verified on the GPU host on 2026-09-07.
  RDP-disconnect behavior remains untested; do not infer it from the presence of
  a GPU or a mirrored installation.
- Native Computer Use on Windows requires the active unlocked desktop, a visible
  target window and app permission. Do not compete with the user's mouse/keyboard
  during a run. See [official Computer Use documentation](https://learn.chatgpt.com/docs/computer-use).
- Start with reachable menu flows: PMC/Scav/Arena selection, map/time/mode
  selection, back navigation, and overlap/readability checks at recorded
  resolution/UI scale. Stop before starting a raid. Save before/after screenshots
  and associate them with the tested build.
- Raid HUD, scoreboard, Tactical buy panel and spectator UI depend on live match
  state. For their redesign, prefer a development-only preview using the same
  rendering code with fixed sample data (long names, empty/full lists, different
  scores), without spawning bots or simulating a real match. This preview is a
  proposed implementation, not an existing tool or completed feature. Keep it
  opt-in and isolated from profiles, normal raids and production gameplay gates.
  Do not fake `ArenaSession.IsActive` or call match-start hooks just to show a
  panel. Preview buy/ready controls must not purchase, start or end anything;
  preview teardown must not stop a real spectator service. The existing
  `ArenaPreviewDiagnostics` is unrelated item-preview diagnostics, not this UI
  preview facility.
- Distinguish visual results (layout, clipping, fonts, hover/click, scaling) from
  gameplay integration. A preview pass does not prove scoring, respawn, purchase
  transactions or raid input ownership; the user verifies those in actual raids.
- Iterate one agreed screen at a time: capture baseline, implement, review,
  build/deploy between runs, reopen and compare. No autonomous raid playtest or
  scheduled/unattended loop was configured by this handoff update.

## Parallel development without collisions

- Use a separate clone on each machine; exchange reviewed commits using Git
  through the agreed remote or a Git bundle. Do not live-mirror two writable
  working directories or `.git` folders. If the existing SPT mirror includes
  Development, exclude/separate the GPU working checkout before parallel edits;
  otherwise the next mirror pass may overwrite local UI changes. Do not change
  synchronization configuration without inspecting it and agreeing its scope.
- Within one machine, worktrees can isolate feature edits, but they share Git
  metadata and do not isolate Arena's deployment directories or external projects.
- Agree the starting commit, scope and touched areas before parallel work. Avoid
  having both sessions independently fix the same code. Integrate selected commits,
  rerun affected checks, build once, then test that exact integration build.
- When moving a whole task, finish/commit local work and hand over the current
  state. When keeping both sessions active, one handles integration and updates
  this shared summary; each test run can have its own report to avoid conflicts.

## Build and dependency traps

- Keep the normal layout when practical: `<SPT>/Development/ArenaMode`, sibling
  `Redeploy`, `_decompiled`, and the needed private `_reference` sources.
  ArenaMode does not contain those other repositories or game DLLs.
- The client references `../Redeploy/Redeploy.Core` relative to the Arena repo.
  Verified Redeploy checkout at handoff: `58abf5876629bb51be808d78ee2cd7a5d34af77a`,
  clean. Transfer compatible source separately if building; do not assume a DLL
  alone satisfies the project reference.
- `Shared.props` resolves the game root two directories above the repo and the
  server root under `SPT_Runtime`. In a different layout, verify/override both
  `SPTPath` and `SPTUserPath`. Local `Shared.props.user` is imported and ignored by
  Git, so it must be configured separately on a new machine. Redeploy has its own
  props/deployment target. Check its effective paths too. With both repos under
  `C:\SPT41\Development`, relative defaults already resolve to C:; only override
  where necessary, and inspect mirrored `.user` files for stale D: paths.
- Builds auto-deploy, including the server build triggered by `dotnet test`.
  A generic `DeployToSpt=false` switch is not honored by the current Arena targets.
  Do not build from an arbitrary worktree until paths, references and deployment
  ownership are settled. Never run concurrent builds into the same installation
  or shared Redeploy output. Prefer Release; rebuild the client last if another
  check rebuilt the shared dependency in Debug.
- Usual commands from the verified repo layout:
  `dotnet build ArenaMode.sln -c Release` and
  `dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release`.
  `tools/KitPolishChecks/Verify-Supplies.ps1` covers the latest grenade change.
  Many harnesses use local `_decompiled` sources, native DLLs or D: defaults;
  inspect their parameters before running elsewhere. Set `SPT_DATABASE` to the
  actual `SPT_Runtime/SPT_Data/database` **directory** for server tests. This is
  separate from a harness's `-Database` parameter pointing at `templates/items.json`;
  the nested server tests do not automatically inherit that parameter. Require
  the expected tests to run, not silently skip. On the GPU host the environment
  variable is `C:\SPT41\SPT_Runtime\SPT_Data\database`.
- Keep licensed audio and attribution together with client assets; use the
  build's asset deployment or a complete verified package, not a guessed DLL set.
  The full solution also needs the installed BigBrain reference DLL. Optional
  runtime BigBrain/PFT/SAIN dependencies must match the actual compatibility
  checks; re-read source and installed versions rather than trusting old notes.

## Where to look next

Client log: `<SPT>/BepInEx/LogOutput.log` (local time).
Server log: `<SPT>/SPT_Runtime/user/logs/spt/sptYYYYMMDD.log` (UTC).
Kits/config: `<SPT>/SPT_Runtime/user/mods/ArenaMode/config/`.

- `KIT-POLISH-REVIEW.md`: grenade conflict, placement rules, evidence.
- `FUTURE-WORK.md`: deferred difficulty controls, further bounded map areas and
  objective-based modes such as Capture the Flag; the area section also records
  current prototype progress and links its review/test evidence.
- `PREVIEW-DIAGNOSTICS-REVIEW.md`: unresolved post-match preview issue.
- `CHARACTER-PREVIEW-REVIEW.md`: separate third menu figure, ownership, GPU/fixture
  evidence and missing genuine-BEAR-profile coverage.
- `MODE-SIDEBAR-REVIEW.md`: five visible map-screen modes, compact facts and
  GPU coverage; PFT presentation and true 16:9 appearance remain untested.
- `tools/ArenaLabyrinthAccessChecks/README.md`: Way Up and Arena-only map access.
- `PRESET-DESCRIPTION-REVIEW.md`, `OPTIC-MOUNT-REVIEW.md`: latest kit/UI fixes.
- `TEAM-DEATHMATCH-REVIEW.md`, `PITFIRETEAM-MULTIROUND-GATES.md`: team modes.
- `ANNOUNCER-REVIEW.md`, `ANNOUNCER-AWARDS-REVIEW.md`: audio behavior/licensing.
- `CORPSE-CLEANUP-REVIEW.md`, `PFT-GOAL-HANDOFF-REVIEW.md`: bot lifecycle/movement.

Read the relevant topic document and recent commits when starting a change,
rather than loading every historical review. Maintain this file as a concise,
dated snapshot of decisions, open evidence gaps and the next owner's scope.
