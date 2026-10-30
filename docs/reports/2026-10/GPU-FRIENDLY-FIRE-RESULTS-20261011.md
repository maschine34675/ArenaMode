# Friendly Fire — GPU2 native verification, 2026-10-11

Result: deployment, menu/default/preference checks, raid freezing and the OFF local
damage-hook veto passed. The ON raid admitted real attributed native knife hits,
but the requested sampled HP decrease was **not established**. Do not describe
this as complete OFF/ON HP acceptance. PFT/native damage rules were not changed.

All times below are Europe/Berlin (UTC+2). Raw JSON/server logs use UTC.
Evidence root: `artifacts/friendly-fire-native-20261011/`.

## Build, baseline and deployment

- No rebuild. Both staged DLLs came from
  `100c00362c0c2d47992868c4e66bad0dfe0f00f3`, using the existing
  `artifacts/area-map-delivery/11909aa24ade491d83a283972ee40a0f/manifest.json`.
  Staged SHA256 values matched the manifest. Committed `100c003..f059152`
  changes were documentation only; the parent separately owned later Equator work.
- Fresh backup completed at 14:21:58, before installation/config/profile actions:
  full current profile, all twelve current PFT files, all host BepInEx configs,
  installed client/server backups on both machines, and 137 local/host DLL hashes.
  The October 9 baseline was not used for change attribution.
- Initial host state: MainMenu, no raid, control free; server PID 14668 and game
  PID 15936 running. The parent explicitly released GPU2. EFT quit gracefully,
  then the host helper stopped the server through Ctrl+C. Only the two staged
  Arena DLLs were copied locally and to GPU2, at 14:22:36–37.
- Both ProductVersions: `0.1.0+100c00362c0c2d47992868c4e66bad0dfe0f00f3`.
  Client SHA256 `854B538C4277BC7D3D293D805ED11C1F1C1D9CC391CE4EAC3BCF90F14A367F26`.
  Server SHA256 `DEBDE6F54D874062BF8CEE27ED67B63AEEF3D94AA9970F13B758FF1BC4D6972E`.
- Restarted profile `SPT-GPU-2` / `LOCAL_TEST_PROFILE_2`; process instance
  `97352cd7ce264c26bc7483cac046a664`, started 14:23:26. UiBridge verified all
  43 plugins and five libraries identical between VM disk, GPU disk and loaded
  code. Final process IDs: server 8240, game 18800; launcher 11016 unchanged.
- UiBridge `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`, SHA256
  `306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E`.
  Core, PFT, SAIN, UiBridge and other DLLs were neither rebuilt nor changed.

Receipts: `deployment.json`, `committed-source-diff.txt`, `baseline/`,
`restarted-status.txt`, `artifacts-before.json`.

## Native menus

- Team DM displayed Friendly Fire ON. Solo Zombies displayed disabled N/A and
  offered EQUATOR. Screenshots were visually inspected at 1920×1080.
- The four existing stored companions test1–test4 were invited through the native
  group UI. No new companion or persistent inventory item was created in menus.
- PFT Zombies displayed Friendly Fire OFF and omitted EQUATOR. PFT Tactical
  displayed ON and also omitted EQUATOR.
- Native toggles were changed and modes revisited: Zombie ON persisted while
  Tactical OFF persisted independently. Both were restored to their defaults.
- All seventeen mutators fit in the native nine/eight-row panel, including the
  final Friendly Fire and No Fall Damage rows, without clipping/overlap at this
  resolution. Other resolutions were not tested.

Evidence: `menu-tdm/`, `menu-zombie-solo/`, `menu-pft/report.md` and its three PNGs.

## Native raid controls and outcomes

Temporary values were WaveStartSize 30, MaxAlive **4**, SpawnIntervalSeconds 10,
WaveGrowth 0, ShooterPercent 0. The documented MaxAlive 1 is outside the installed
setting's accepted 4–56 range, so the permitted minimum 4 was used in every raid.
Original values 56/56/0.2/10/20 were recorded and restored.

Only after protected InRaid was confirmed, the bridge granted one ER Bayonet
(`54491bb74bdc2d09088b4567`) per raid. It arrived unsearched in Pockets; native
SEARCH revealed it, and native Equip/SelectKnife put it in hand. Native
ToggleShooting/EndShooting pairs performed attacks. The final ON contact used
the game's alternate knife attack, ToggleAlternativeShooting/EndAlternativeShooting.
No companion HP setter, forced kill, gear edit, or foreign configuration change
was used. Walking/aim tracking and one guarded teleport only positioned the PMC.

### Initial OFF attempt — not a passing comparison

Wave 1 became ready at 14:28:50.789. The unarmed local PMC died while resolving
the inventory search step. No usable FF contact evidence came from this attempt.
Protected native exit completed; server snapshot restore logged at 14:30:10.346.

### OFF retry — local damage protection passed

- Wave 1 exact team ready 14:32:17.659; runtime TeamPhase was Live.
- `FriendlyFireEnabledSelection` was false. Changing the F12 preference to true
  for the following raid left this frozen value false.
- Source was PMC `LOCAL_TEST_PROFILE_2`; exact victim test3 was
  `6ac8a818172cc3431cfcb43c`.
- Native attack at 14:35:21.175 produced log #3736 at 14:35:21.381:
  `Arena ZombieMode: friendly fire blocked (local damage hook, source=LOCAL_TEST_PROFILE_2, victim=6ac8a818172cc3431cfcb43c; frozen mutator OFF).`
  This is the local damage-hook diagnostic, not an effect-filter candidate.
- Sampled head/chest/stomach/left arm/right arm/left leg/right leg values were
  35/85/70/60/60/65/65 before and after (440 HP). LastAggressor remained null;
  TeamPhase remained Live. An earlier swing without a veto was treated as a miss.
- Godmode was switched off at 14:35:54.573; protected exit at 14:36:03.753;
  server snapshot restored at 14:36:04.864.

Evidence: `off-proof.json`, `off-live/`, native log #3442/#3735–3738.

### ON control — real native hits, HP acceptance incomplete

- Exact team ready 14:38:09.281. Runtime TeamPhase Live and frozen FF true were
  verified. Resetting the preference to false did not change the current true
  value, independently proving the opposite freeze direction.
- First-wave contacts named the exact local PMC as LastAggressor and reported
  native Melee armor damage (including 32.5 on HeadCommon). These were not HP passes.
- Do not infer an HP delta from retained first-wave health handles: a later fresh
  traversal differed, an infected actor became LastAggressor, and the natural
  wave transition rebuilt all four PFT bodies at 14:43:28.936.
- A fresh wave-2 test3 body, `6acb847f054477aec100a4ed`, was resolved through the
  live team roster. Fresh traversal before contact: 440 HP, LastAggressor null.
  An early swing missed. Native alternate knife input at 14:44:51.093 subsequently
  yielded LastAggressor equal to the local PMC, Melee, Damage 21.06,
  DidBodyDamage 21.0599976, DidArmorDamage 14.039999, HeadCommon. Freshly traversed
  seven-part HP still sampled as 440 at 14:45:08.649.
- Both wave-2 HP samples used fresh traversal (before at 14:44:09, after at
  14:45:08), not retained handles or `Profile.Health`: `ArenaMatchService.Current`
  -> ZombieMode `_team.LiveTeamMembers` -> exact EFT.LocalPlayer test3 body
  -> `EFT.Player::HealthController` (PlayerHealthController)
  -> `BaseHealthController<ActiveHealthController.Effect>::BodyState`
  -> each of the seven `BodyPartState::Health` entries
  -> `EFT.HealthSystem.HealthValue::Current`. The wave-2 bot handle was
  `9aac0c69:89`; the profile identity above was rechecked.
- Subsequent read-only source review gives DidBodyDamage stronger meaning than
  nominal attack damage: `_decompiled/EFT/Player.cs:30037` assigns the return of
  `ActiveHealthController.ApplyDamage` to it; that method reads Common HP before
  damage and returns before minus after (`_decompiled/EFT.HealthSystem/ActiveHealthController.cs:3463-3464`).
  Thus 21.0599976 records a synchronous native HP delta. It conflicts with the
  later fresh seven-part sample of 440; no immediate paired HP snapshot was taken.
  This is recorded as native damage evidence, without promoting the requested
  sampled HP comparison to PASS. The reason for
  the mismatch was not isolated. Neither Arena regression nor PFT healing/native
  protection is asserted as the cause. No further raid was run after the parent
  asked to bound attempts and preserve this limitation.
- Godmode off 14:46:06.144; protected exit 14:46:16.435; server snapshot restored
  14:46:17.526 and protected end completed 14:46:17.554.

Evidence: `on-proof-incomplete.json`, `on-start/`, `actions-errors.json`, full logs.

### Source follow-up on the ON observation

An independent read-only check established that the positive native
`DidBodyDamage` is the synchronous **actual Common-HP loss**, not a predicted
damage value: `ActiveHealthController.ApplyDamage` returns the before/after
health difference, and `Player.ApplyDamageInfo` stores that return. No subsequent
writer was found in the examined Arena, PFT 0.10.3 and SAIN sources. Thus the
recorded contact supports an immediate 21.06 HP hit; the later snapshot cannot
establish that no damage occurred.

The same check found PFT's `FollowerMedical.ForceHeal`: after a post-combat delay
of at least twelve seconds it restores every body part to its maximum. The
wave-2 HP observation was about seventeen seconds after the knife action.
Automatic healing is therefore a concrete explanation, but its execution was
not captured in this test. The original limitation of the sampled before/after
comparison remains; no extra raid or change to PFT's healing was made.

### Scope of local protection helper

The helper was used only during the OFF retry and ON protected raid, to prevent
another setup death. Source inspection independently confirms local-only scope:
`../UiBridge/UiBridge.Client/Game/TestHooks.cs` resolves
`PlayerStateReader.MainPlayer()` and passes that player to SetGodmode;
`Game/TestHookRuntime.cs` captures that player's ActiveHealthController and only
sets/restores that exact controller's coefficient. The ON victim's coefficient
was read as 1.0. This does not establish the cause of the ON HP observation.

## Errors, restoration and handover

- No Error/Fatal log entry occurred in the accepted live windows. Three existing
  PFT physical-roster diagnostics followed committed native Stop, at sequence
  3263 after 3262 (14:30:09.258), 3774 after 3773 (14:36:03.753), and 4467 after
  4466 (14:46:16.437 vs .435). The first two share the same timestamp resolution,
  so their ordering is established by sequence. These are teardown observations.
- Full native log export contains 4520 lines, #1–4520; four already-truncated
  entries are explicitly flagged. Full host BepInEx and server logs were copied.
- At the 14:47:35 preservation audit, the complete profile differed only at
  Health/UpdateTime and Hideout/sptUpdateLastRunTimestamp. No profile JSON was
  overwritten by the tester. All twelve PFT files matched byte hashes exactly.
- Every pre-existing config assignment was restored. Only the three expected
  newly introduced default FF keys remained (Tactical true, Team DM true,
  Zombie false). All 137 baseline DLL paths were checked; only the two intended
  Arena DLLs on each installation changed. Foreign changes: zero.
- Walking, tracking and godmode were stopped; all raids left through protected
  native Disconnect and the native result screens. Server was left running as
  before. Per parent handover instruction, EFT stays at MainMenu for subsequent
  Equator tests rather than being closed. No commit or HANDOFF edit by this worker.

Evidence: `preservation.json`, `profile-after.json`, `final-status.txt`,
`host-release-status.txt` (14:50:53, MainMenu/no raid/control free),
`LogOutput-SPT-GPU-2-2026-10-11_14-23-18.log`, `server-log-20261011.log`.
This test does not certify grenade visuals, every weapon, all modes or long matches.
