# ArenaMode working handoff

Snapshot: 2026-11-07. Read [AGENTS.md](../AGENTS.md) first, then the
[area rules](AREA-RULES.md) for what you touch. This is a compact handoff, not a transcript
and not a live synchronization channel: verify files, commit IDs, installed artifacts and
logs before treating a recorded result as current; a new session has no access to the
earlier conversation or the agent's memory. The complete earlier handoff (5,917 lines,
2026-09 to 2026-10-29) and the earlier `AGENTS.md` are archived in
[handoff-archive/HANDOFF-2026-10-29.md](handoff-archive/HANDOFF-2026-10-29.md) and
[handoff-archive/AGENTS-2026-10-29.md](handoff-archive/AGENTS-2026-10-29.md), text and line
numbering unchanged, only their links and document paths re-rooted for the new layout.

## Released and installed

- **0.5.0 published (2026-11-07 evening; tag `v0.5.0` = `063a7684`,
  [GitHub release](https://github.com/maschine34675/ArenaMode/releases/tag/v0.5.0) with
  `ArenaMode-0.5.0.zip` and its checksum, latest, not a prerelease; `master` pushed to `dev`
  with the record commit `eaa46712` and this note):** version 0.5.0 in the five version sites, shared **Core 1.0.20** from Redeploy's
  release commit `3b68193` (Redeploy 1.1.2, prepared the same evening, its package
  `Redeploy-1.1.2.zip` built from that commit), the whitelist at ArenaMapPort 0.5.0 (published
  2026-11-07), Redeploy 1.1.2 and PitFireTeam 1.1.1, the PitFireTeam 1.1.1 adaptation
  ([review](reviews/PITFIRETEAM-111-20261107.md), see Open work), Domination without the WIP
  mark and with a 300-point default, [RELEASE-0.5.0.md](RELEASE-0.5.0.md) with its native-check
  and package sections still pending. Verified offline on the release state: xunit 3,700
  passed / 3 expected skips, PitFireTeamChecks 407, DominationPolicyChecks 21,596,
  DominationAdmissionChecks, PftTacticalKitChecks, ArenaSessionClientChecks (174 mutants),
  DeployLayoutChecks, ArenaGateChecks. Packaged from the build commit `063a7684` (the release
  commit plus a System.Version fix, the package snapshot's exclusion of the server's
  client-settings.json and the PitFireTeam reference-tree lookup): `ArenaMode-0.5.0.zip` SHA-256
  `524ab749…836a14`, 3,892,127 bytes, Core `1.0.20+3b68193` byte-identical to Redeploy 1.1.2,
  VirusTotal in the release notes. Installed on D:\SPT41 and rolled out to GPU-2 at 21:49; the
  native cycle is recorded in [GPU-RELEASE-050-20261107.md](reports/2026-11/GPU-RELEASE-050-20261107.md)
  (Redeploy respawn, Team DM Iceberg, Domination Bowl with the crowd, PitFireTeam Tactical over
  three rounds with the served preflight). The Forge (sp-mod.com) upload is manual and open;
  [FORGE-DESCRIPTION.md](FORGE-DESCRIPTION.md) is the 0.5.0 draft. On 2026-11-08 the 0.5.0
  changelog, [RELEASE-0.5.0.md](RELEASE-0.5.0.md) (GitHub release body re-published from it) and
  the Forge draft gained the DynamicMaps Domination objectives of `feature/domination-map-markers`
  (merged 2026-11-01), which the release texts had left out; the tagged `CHANGELOG.md` on GitHub
  shows it only after the next push of `master`.
- **Branches (2026-11-07 evening):** `master` holds everything that was open: the crowd
  reactions (`feature/crowd-reactions`, merge `28b92a40`), the reward startup order fix, the
  session save diagnostics (`codex/session-save-diagnostics-20261105`, fast-forwarded), the
  Iceberg and map-screen work; those branches and the `ArenaMode-crowd` worktree were deleted
  after their merges. `master` was pushed to `dev` with the 0.5.0 release on 2026-11-07. The
  only open branch is `feature/fika-admission` in the worktree `D:\SPT41\Development\ArenaMode-fika-m1`
  (another session's work, left alone). After the merge: no-deploy solution build, xunit and the
  crowd baseline on the merged `master` (see the merge commit's successor for the record).

- **Session save diagnostics (2026-11-05, source only):** rejected authenticated
  session switches now carry RAM/disk versus RAM-during-readback results, a hashed
  profile alias/selection role, at most three fixed section labels and the final
  resume permission in one Warning. Capture is bounded and failure-only; save
  proof, hold/retry behavior and helper/lease contracts are unchanged. See
  [logging meanings and limits](LOGGING.md#session-save-refusals).
  Offline verification: **3,694 passed, 3 expected fixture-gated skips**, including
  23 new diagnostic cases exercising the production comparison/sequence. Listener
  response/error placement was independently source-reviewed; no native game claim.
  Installed hashes stayed unchanged. No deployment or release.

- **ArenaMode 0.4.1** (2026-10-29, hotfix for 0.4.0) is the latest
  [GitHub release](https://github.com/maschine34675/ArenaMode/releases/tag/v0.4.1); tag
  `v0.4.1` = `2a4438c9`. It bundles shared **Redeploy.Core 1.0.18**, byte-identical to the
  Core in **Redeploy 1.1.1** (tag `232af19`); the whitelist records Redeploy 1.1.1 and
  ArenaMapPort 0.4.0. Release notes: [RELEASE-0.4.1.md](RELEASE-0.4.1.md); the 0.4.0 and
  Redeploy 1.1.0 release pages name their successors. Package evidence (hashes, tests,
  VirusTotal, the native GPU-2 cycle) is in the release notes; the two-host pre-release
  sweep [RELEASE-SWEEP-20261029.md](reports/2026-10/RELEASE-SWEEP-20261029.md) ran on
  earlier binaries and proves nothing about the rebuilt release hashes.
- **Forge** (sp-mod.com) upload is manual and still open for 0.4.1;
  [FORGE-DESCRIPTION.md](FORGE-DESCRIPTION.md) is the eight-tab draft; its ArenaMapPort
  wording says 0.4.0 is published (corrected on `feature/iceberg`, 2026-10-31).
- **ArenaMapPort 0.4.0** was published by the user on 2026-10-30 (tag `v0.4.0` in
  `D:\ArenaMapPort` on commit `8f23988`, "0.4.0 published: Pixeldrain links in the release
  note"). The `CHANGELOG.md` 0.4.0 and 0.4.1 entries describe the wording at release time
  ("not yet published") and stay.
- **Installed everywhere** (D:\SPT41 on this machine, mirrored by Syncthing to SPT-GPU-1
  `192.168.178.114` and SPT-GPU-2 `192.168.178.109`, both at `C:\SPT41`, and MASCHINE11):
  since 2026-11-07 21:49 the package binaries of ArenaMode 0.5.0 (`063a7684`) and Redeploy 1.1.2
  (`3b68193`, Core 1.0.20), ArenaMapPort 0.5.0 since 18:18 and PitFireTeam 1.1.1 since 2026-11-05;
  before that the release binaries of ArenaMode 0.4.1 and Redeploy 1.1.1. Recoil Rework Legacy,
  installed temporarily for the 1.1.1 native check, was removed again on all hosts including
  its generated `com.pein.camerarecoilmod.cfg`. Any development build replaces these with
  differently stamped binaries; say so in the next handoff. Since 2026-11-08 the ArenaMode
  client and server DLLs on every host are the development build `0.5.0+fd4543d9` (Cola
  archive; before it `b3dd3983`, Domination rewards); BigBrainCompat, Gate, the Session helper
  and Core 1.0.20 are still the release files.
- Profile `SPT-GPU-2` (`6ab7db1c…`) was wiped by the user in the SPT launcher on
  2026-10-29 while it carried Tarkova Cola reward history; from 0.4.1 to the Domination
  build SPT started with one Warning and that profile's rewards stayed off
  ([REWARDS-RUNTIME.md](REWARDS-RUNTIME.md), "Lost reward authority"). On 2026-11-08 the Cola
  archive change (Open work) archived its records at the first server start with that build
  (`user/arena-rewards/6ab7db1c….archived-20261108T102701Z-4d0d46d8`, consent OFF carried),
  rewards were switched ON in the game and the next start enrolled it afresh (epoch
  `e34fafb2…`): [GPU-COLA-ARCHIVE-20261108.md](reports/2026-11/GPU-COLA-ARCHIVE-20261108.md).
  Its rewards are ON now; the manual rename remains only for an incomplete history.
- Repository hygiene 2026-10-30: `docs/` reorganised (reviews/, reports/, evidence/,
  handoff-archive/), `test-evidence/` moved to `docs/evidence/`, `assets/archive/` deleted
  (its thumbnail iterations stay in git history before this change), `AGENTS.md` cut to
  the global rules, this handoff rewritten. The local branch `codex/ux-menu-20261029`
  (merged) was deleted. On the development machine the gitignored `artifacts/` folders
  older than 2026-10-28 and all `bin/`/`obj/` output were deleted; raw evidence those
  folders held (for example `artifacts/ux-followup-20261027/private-errors.txt`) is gone,
  and the reports that linked it keep only their summaries.

## Open work

**Lost reward authority with Cola history (2026-11-08, master, not released):** user decision: the
automatic archive of `RewardAuthorityRecovery.Reconcile` also covers a complete Tarkova Cola history. Before,
`UnsettledCore()` refused unconditionally on a `cola-plans` folder or Cola progression in a journal payment, so
a player who ever earned a Cola reward and then wiped the profile kept rewards off for good (Warning at every
start; only the manual rename helped). Now a complete companion side (`RewardColaJournal.CompanionGap` returns
null) is settled and archived by rename with the consent carried over and a fresh enrollment in the same pass; an
incomplete or unreadable history keeps the quarantine and an unfinished Cola payment still stops startup
(`RequireCompleteCompanionSide`). The automatic archive needs what `Reconcile` always needed: a runtime-managed
enrollment, a `consent.json` and the profile file present; a legacy enrollment or a missing consent file keeps the
old notice (which now names that) and the manual remedy. The Cola progression lives in the Tarkova Cola mod's own
files, never touched.
Files: `ArenaMode.Server/Rewards/RewardAuthorityRecovery.cs`, `RewardColaJournal.cs`, `RewardStartupProcessor.cs`;
docs `docs/REWARDS-RUNTIME.md` ("Lost reward authority"), `CHANGELOG.md`. Verified offline: xunit 3,717 passed
(3 skips; new cases for the archived wiped and moved profiles, the named gaps, the kept notice and the recovered
line with a detail), OfflineStartupChecks 481, no-deploy solution build 0 errors. Native 2026-11-08 on GPU-2:
profile SPT-GPU-2 (two completed Cola plans with their `.done.json`, consent OFF, runtime-managed enrollment) was
archived with its Cola history at the first server start with the build and enrolled afresh at the next one after
rewards were switched ON ([GPU-COLA-ARCHIVE-20261108.md](reports/2026-11/GPU-COLA-ARCHIVE-20261108.md)). Not
run natively: a moved install, an incomplete history (both xunit), a payout into the fresh enrollment.

**Domination rewards (2026-11-08, master, not released):** Domination joined the reward protocol
under the `KillsOnly` rule like Gun Game and Zombies: eligible local hostile final kills pay (1,000 RUB
+ 100 XP under the server policy, capped per match), no match-win bonus because objective ownership has
no server-verifiable proof (see `docs/REWARDS-RESULTS.md`, "Win derivation"). Touches the client capture
(`ArenaRewardCapture`), `ArenaRewardModeState`, the server `RewardResultValidator` and the consent card
text; the Domination reward clock is a monotonic high-water mark of the policy's clock with no time limit
(a score-limit finish stamps the analytic crossing time, which can lie before the last observed frame
and would void the run). Verified offline: xunit 3,708 passed (new validator cases: KillsOnly pays one
kill and no win, TeamScore and LocalTarget rejected with mode controls, the local forfeit accepted
without a score), DominationAdmissionChecks (reward pins and 11 compiled mutants, three of them on the
reward rule and the clock), TdmMatchMenuChecks, TeamDeathmatchRuntimeChecks, TdmMonitorChecks,
AnnouncementIntegrationChecks, SpawnShieldScopeChecks, TeamGiveUpChecks, RewardMenuStateChecks,
DominationPolicyChecks; no-deploy solution build 0 errors. Native run 2026-11-08 on GPU-2 with profile
SPT-GPU-1: one kill in a Domination match, 1,000 RUB + 100 XP paid at the next normal server start
([GPU-DOMINATION-REWARDS-20261108.md](reports/2026-11/GPU-DOMINATION-REWARDS-20261108.md)); a score-limit
finish, a time-limit finish and a give-up with rewards were not run.

**PitFireTeam 1.1.1 (2026-11-07, in the 0.5.0 release commit):** every member the adapter
reflects on is unchanged, but 1.1.1's `SpawnGroupBots` awaits a thread-pool preflight
(`LoadSavedSpawnSettings`, `/client/game/bot/followerdetails`) before `BotCreationData.Create`,
so Arena's leased follower respawn of rounds 2+ (Tactical, Zombies) returned an incomplete task
before `Create` and failed the lease. Arena now remembers the completed natural round-1 preflight
task (gated on the report guard's arming predicate, the first completed result of a raid stays)
and serves it inside its leased call so the state machine reaches `Create` synchronously; the
kill-report guard also patches the six-argument `SendKillReport`, the stop-cleanup fallback
mirrors `CompanionRaidPreparation.Reset` and `FollowerRaidHealthRetention.Clear`, and 1.1.1 is a
reviewed baseline. All of it is gated on an installed assembly of 1.1.0 or newer; builds below
bind byte for byte as before. `PitFireTeamChecks` pins the source (gate, prefix conditions,
postfix assignments, mutants) and the installed metadata (skipped below 1.1.0); the served
preflight itself is proven only natively (GPU-2 Tactical, three rounds, in the 0.5.0 cycle).
Known limit: a companion wounded in a normal raid is refused by 1.1.1 at the Arena start; the
player heals it in PFT's menu. `PitFireTeam100Checks` (1.0.0 release folder) and
`PitFireTeamRoundLifecycleChecks` (0.10.0 reference checkout) need folders this machine lacks.
Contract and findings: [PITFIRETEAM-111-20261107.md](reviews/PITFIRETEAM-111-20261107.md).

**Crowd reactions, ArenaMode side (2026-11-02, branch `feature/crowd-reactions` from
`65007e74`, merged into `master` on 2026-11-07 evening and the branch deleted; its build
`0.4.1+67bbc215` is installed on all Syncthing hosts since 2026-11-07 17:16, client DLL only,
the server stays `3b0dc30e`; the client sources of `master` are identical to that build):** phase B of
`D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md`. **2026-11-07: ArenaMapPort's roar layer
(`e3e4290`: each played reaction roars Arena's Cheer bed for Approve, Start and Finish or an
Angry bed for Disapprove from every stand, the other kind cut in 0.5 s; the murmur swell is
gone; contract unchanged) adopted in `67bbc215`:** one mood per flush
(`Frame.KeepOneMood`, the stronger of Approve and Disapprove across the going and the waiting
frame) and a 1 s mood window across flushes (the opposite kind within a second only when
stronger; a weaker same-kind mood leaves the memory alone; Start, Finish and Lull end it), one
Debug line per drop; `CrowdReactionChecks` 1,340 assertions, 61 mutants, the real surface reads
MapPort's roar kind table and amplitude law when present. Native GPU-2 run of that build
([GPU-CROWD-ROAR-20261107.md](reports/2026-11/GPU-CROWD-ROAR-20261107.md)): 25 reactions in
Team DM (Bowl) and Deathmatch (Equator, 3 rounds), every one played with the right roar from
every stand, no drop, no ArenaMode warning; the trade case did not occur in the game (offline
only). Open for the ear: the roar level (MapPort's -32 dBFS start value), the 1 s window, and
that a lost lead boos from all stands on every lead change (six times in eight minutes of 4v4).
Earlier state: phase B of
`D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md`. Murmur intensity through ArenaMapPort 0.4.0's
`SetExcitement` and dormant crowd reactions for the phase A contract; F12 Audio
`CrowdReactions`. Offline only: `tools/CrowdReactionChecks` (51 mutants, plus a real-surface
stage against the installed ArenaMapPort client) and a compile check
of the complete client against the game DLLs at `11682885`; the later changes (only the three
crowd files) are compiled by that harness alone, against stand-ins. ArenaMapPort phase A (the
reaction members) is implemented in MapPort `d70acd2` on its `feature/iceberg` (2026-11-02, not
released, planned with its next release; mask 62, Lull 6 and PurifiedTeam 7 reserved, owners up
to 64 characters) and host-run there with MapPort's developer demo only, without ArenaMode. The
harness's real-surface stage binds the installed client's `SpectatorCrowd` and runs the service
against its real queue: kinds, owner, strengths, claim, release and reset agree with what
ArenaMode sends and expects (the plan's 40-character owner truncation is 64 in phase A, well
above ArenaMode's 40-character owner); kinds withdrawn mid-match by MapPort's developer key end
ArenaMode's reactions for that match (accepted, developer-key only). Native GPU-2 run on 2026-11-02 evening with MapPort phase A
([GPU-CROWD-REACTIONS-20261102.md](reports/2026-11/GPU-CROWD-REACTIONS-20261102.md)): every kind
sent once and played by MapPort in Team DM and Deathmatch, Finish once per result, excitement
0.6/0.85, release on every exit seen, Iceberg no claim, Zombies off, no ArenaMode warning. Pending:
the user's listening (two tuning questions recorded there: the announcer interlock halves nearly
every announced event, and Start at 1.0 exhausts MapPort's Start pool), Gun Game, Tactical,
Domination and the spectator camera with the crowd, then the merge. Three independent reviews on 2026-11-02 found one major
(a teammate's kill reacted, against D5) and minor issues; all are fixed on the branch
(`83d4aee3`..`4d4a9444`). Applied afterwards from the plan as revised in ArenaMapPort
`08964c1` (the user's automatic subtitles of every crowd clip): a lost lead disapproves at 0.3
(laments only, below MapPort's 0.4 band; an own death keeps 0.5), and the Lull of the user's
decision D14 ("wie empfohlen"): ArenaMode sends Lull 0.4 after 60 s of live combat without a
kill, at most one per 90 s, dormant until a MapPort after phase A ships the Lull bank and
advertises kind 6; a kill or a local death withdraws a Lull still waiting to go out (review of
`484b095a`). User decision D1 (MapPort side): phase A exports only the 70 "keep" clips
for now; the 19 "listen" clips wait for the user's listening, which is still pending. Contract
and decisions (D1, D5, D7, D10, D12, D14, announcer interlock):
[CROWD-REACTIONS-20261102.md](reviews/CROWD-REACTIONS-20261102.md).

**Reward startup order (2026-11-05, merged into `master` from `fix/reward-startup-order`
the same evening, for the next release, not released, not pushed):** fixes
[maschine34675/ArenaMode#1](https://github.com/maschine34675/ArenaMode/issues/1): with
another server mod's `IOnLoad` at `Watermark + 1` (BetterFlea) the reward step refused
every start, enrolled nothing, and the Arena session's card said "Activation pending"
forever. The step now runs at `Watermark` (0), refuses only a callback that really sits
before it in SPT's run order, names each one in the Warning, says "Rewards are off: …"
for every refusal that recurs, and the normal server records the reason in a file next
to the session lease (`authority-unavailable.json`; never in the lease, whose 17
members the session helper parses with a fixed count) so the private card shows it.
Server-only; the client and the helper are unchanged. Contract: [REWARD-STARTUP-ORDER-20261105.md](reviews/REWARD-STARTUP-ORDER-20261105.md).
Verified offline (xunit, `OfflineStartupChecks` mutants, `RewardMenuStateChecks`, an
adversarial review of 49 agents whose findings are fixed or documented there) and natively
on GPU-2 with a probe server mod at `Watermark + 1` and at 0
([GPU-REWARD-STARTUP-ORDER-20261105.md](reports/2026-11/GPU-REWARD-STARTUP-ORDER-20261105.md)):
the first attempt put the reason into the lease and paused in the session helper, whose
parser counts the lease's 17 members; the reason is a file of its own since `3b0dc30e`.
**Installed state since 2026-11-05 evening:** the server DLL `0.4.1+3b0dc30e` of this
branch is on every Syncthing host (built from the server project alone; client
`0.4.1+67bbc215` of `feature/crowd-reactions` since 2026-11-07 17:16, helper and Core 1.0.19 unchanged); the probe
was removed from all hosts after the run. The issue has no reply yet; the review document
holds a suggested one.

**Domination objectives on DynamicMaps (2026-11-01, merged into `master` from
`feature/domination-map-markers`, not released):** ArenaMode owns live
objective diamonds, using each round's verified IDs/positions and faction or
contested colours. They appear only in the active Domination round and are removed
at its end; map assets and other modes are unchanged. Latest installed client and
BigBrain companion source `73e0c773` enlarges the diamonds and letters by 50% at
the user's request; GPU-2 loaded that exact build and verified the larger A/B/C,
capture colour and disconnect cleanup on Iceberg. Core 1.0.19 and the server stay
unchanged. Earlier build `01f5bb04` has Factory capture/reopen/end/disconnect and
Iceberg simultaneous A/B/C/ownership/time-limit acceptance on both GPU hosts.
GPU-1 lacks EFT Arena, so the ported-map checks use GPU-2. The latest run preserved
all 240 setting values and 49 companion files; its profile control passed after
accounting separately for startup PitFireTeam registration. Contract and evidence:
[DOMINATION-MAP-MARKERS-20261101.md](reviews/DOMINATION-MAP-MARKERS-20261101.md).

**Arena map screen frame drops (2026-10-31, fixed, measured on GPU-2, merged into
ArenaMode `master` and Redeploy `main` on 31.10. evening, not released):** on the Arena SelectLocation screen GPU-2 dropped from 60 to 48 FPS
with one 45-130 ms frame every 0.2 s (the user's PC far worse). Measured with
`uibridge_status perfSeconds`, a ModProfiler headless capture and the DeveloperMode timing
probe (`ArenaLocationSelectionPatch`, `[dev] Arena map menu refresh: …` every 5 s):
`ArenaMenuSelection.CompatibilityRejection` ran four times per refresh tick at about 12 ms
each, because every adapter of an optional mod that is NOT installed called
`Harmony.UnpatchSelf()` on every check (HarmonyX walks all ~1,270 patched methods, ~2 ms and
~10 MB per walk). Fix, built and deployed to all Syncthing hosts on 31.10. evening (first for the
measurement as ArenaMode `0.4.1+60795208` with Core `1.0.19+51ac95c`, then from the merged
heads; see the Iceberg entry):
Redeploy `fix/absent-adapter-unpatch` `51ac95c` (**Core 1.0.19**: Quick Cancel, PiP
Disabler, Quest Tracker, Audio Indicators and Healing Autocancel unpatch only while they
hold patches, pinned by `Redeploy.Tests/AdapterUnpatchGuardTests`) and ArenaMode
`fix/map-screen-admission` `af444148` (Fast Grenade Throw and PiP preload guards) plus
`0e85df4d` (one admission verdict per display pass, opened only around
`ScreenState.Refresh`; clicks, setters, claim, freeze and start stay fresh;
DominationMenuPerformanceChecks pins one check per idle refresh, four before). An ArenaMode
built from that branch stamps and refuses anything older than Core 1.0.19: merge, build and
ship both branches together. Three reviews found no blocker; their minor findings (contract
wording, Show-time read counts, a throwing-refresh and nesting check, a tighter Redeploy
oracle, PiP preload and second-`Patch` failure checks, handoff wording) were applied on both
branches after the deployed commits. Measured after the fix on GPU-2: 60.0 FPS with P95 16.67 ms and maximum
16.7 ms with and without a selected map, refresh tick at most 0.6 ms, exactly one `compat`
evaluation per tick at 0.1 ms (three reads answered by the pass), clicks 0.1-0.8 ms per
evaluation, a Domination raid on Iceberg started and left normally; before: 48 FPS, P95
45 ms, maximum 76-130 ms. Report with both measurements:
[MAP-MENU-PERFORMANCE-20261031.md](reports/2026-10/MAP-MENU-PERFORMANCE-20261031.md);
contract and test results: [MAP-MENU-ADMISSION-20261031.md](reviews/MAP-MENU-ADMISSION-20261031.md);
cause evidence: `artifacts/iceberg-20261031/gpu/menu-perf/`. Release note: the next
ArenaMode release must ship with a Redeploy release that carries Core 1.0.19.

**Iceberg (2026-10-31, merged into `master` from `feature/iceberg`, not released):** `arena_iceberg` is
integrated like Bowl in four commits (admission with Tactical refused, CheckPoint team boxes,
Domination A/B/C, docs) plus the review-fix commit `2709476d`; see [ICEBERG-INTEGRATION-20261031.md](reviews/ICEBERG-INTEGRATION-20261031.md).
Native GPU-2 cycle on 31.10. passed for Domination 4v4, Team DM 4v4 and 10v10, Deathmatch 8
and an ordinary PMC raid; Tactical is refused in the menu
([GPU-ICEBERG-20261031.md](reports/2026-10/GPU-ICEBERG-20261031.md)). The first Domination
start refused point A (light root in the pool basin); the heights now follow the measured
NavMesh floors (`a6ed8d93`). Not run: PFT Zombies, the monitors, daytime.
MapPort follow-up: Iceberg's overview marker sits under the Arena mode panel in the Arena
flow (MapPort's position integration overrides ArenaMode's anchors, so only MapPort can move
it east). It needs an ArenaMapPort build that ships `arena_iceberg`: today only the unreleased MapPort
`feature/iceberg` build `0.4.0+32fda33` installed here. The published ArenaMapPort 0.4.0 has
none and carries the same version string, so ArenaMode detects the map by presence (live
location, plugin identity), never by version; the whitelist row stays 0.4.0. Do not announce
Iceberg in an ArenaMode release before MapPort publishes it. **Historical 31 October
measurement build (current ArenaMode installation is recorded above):** all Syncthing
hosts had the orchestrator's build of 31.10. evening for the native map-screen
measurement: ArenaMode `0.4.1+60795208` (`fix/map-screen-admission`) with Redeploy.Core
`1.0.19+51ac95c` (`fix/absent-adapter-unpatch`), neither the 0.4.1 release nor `master`
(before that: the development build of `master`, `a6ed8d93` plus the timing probe, with the
released Core). Builds of `master` before the merge stamp Core 1.0.18 and keep the released
`1.0.18+232af19` bytes: the rebuild writes different bytes under the same version, so the
release Core from `Redeploy/artifacts/publish-111/.../stage` is copied back after every such
build. Builds of `fix/map-screen-admission` (and of `master` once it is merged) stamp 1.0.19 and
refuse Core 1.0.18: they ship with the freshly built Core 1.0.19 from
`fix/absent-adapter-unpatch`, never with the copied-back release bytes. The ArenaMode cfg
migrated to revision 5 with `arena_iceberg` in `General/Maps`.

- **Installed MapPort data is a harness dependency.** `tools/ArenaPickupChecks` reads every
  installed ported map, Iceberg included, and fails loudly without
  `user/mods/ArenaMapPort/db/arena_iceberg/base.json`; so do `IcebergChecks`, and through
  ArenaPickupChecks also `BowlChecks`, `BowlTeamBoxChecks`, `IcebergTeamBoxChecks` and any
  full harness run. They need an ArenaMapPort build that ships `arena_iceberg` (the feature
  build `0.4.0+32fda33` or a later release); the published 0.4.0 is not enough.
- **The installed ArenaMapPort client is a harness dependency too** (branch
  `feature/crowd-reactions`): the real-surface stage of `tools/CrowdReactionChecks` loads
  `BepInEx/plugins/maschine-ArenaMapPort/maschine-ArenaMapPort.Client.dll` read-only and fails
  loudly when it is missing or lacks the eight `SpectatorCrowd` members of crowd reactions phase
  A (MapPort `d70acd2` or later); the published 0.4.0 is not enough. Installed here on
  2026-11-02: ProductVersion `0.4.0+08964c1` (SHA256 `B6DF4A1D…AE8521`), MapPort's development
  build of that work from before the commit. `-SptRoot` of `Run-ArenaVerification.ps1` (forwarded
  to every harness script that declares it) or of `Verify.ps1` names another installation.
- **FORGE-DESCRIPTION.md deliberately carries no Iceberg** (it is the 0.4.1 upload draft).
  Add the Iceberg lines (mode list, 2v2–10v10 pockets, spawn shield, Domination A/B/C) when
  an ArenaMapPort release ships `arena_iceberg`.
- **PortedCoverChecks** reads the installed PFT decompile from the gitignored
  `artifacts/pft-installed/`; after a cleanup or a PitFireTeam update recreate it with
  `pwsh -NoProfile -File tools/Decompile-PitFireTeam.ps1` (read-only on the install; it refuses
  a non-empty output folder, so remove the old one first). `ARENA_PFT_INSTALLED_SOURCE`, when
  set, names another decompile root and is tried first.
- Harness rot found on the way and repaired on that branch: AreaConfigChecks,
  ArenaLightingChecks, PortedCoverChecks, ParticipantMenuChecks' Domination oracle,
  AreaTeamMenuChecks' layout literals and `tools/ArenaLabyrinthAccessChecks` (did not compile:
  `ArenaMenuSelection.cs` needs the Domination bridge stand-in). Still broken and not
  repaired: `tools/AnnouncementIntegrationChecks` (12 compile errors from `e7d208ad`: its
  `Vector3` double has no `x`/`z`, and `TeamDeathmatchMode.Domination.cs` needs `Config`).

Known and small (from the 0.4.1/1.1.1 release review and the 2026-10-30 audit):

- Three `ModulePatch` subclasses break the naming scheme: `StopPatch`, `GameEndPatch`,
  `DeathPatch` in `ArenaMode.Client/Session/ArenaRewardCapture.cs` (added in `74c2c3b6`).
  Their bare names are the Harmony ids. Rename per AGENTS.md; no test enforces the prefix.
- Redeploy's `NotifyRefused` still says "PMC respawn choices are still open" when the
  respawn item has vanished, and a non-cost `InvalidOperationException` from the Scav
  preparation reaches the player as raw text.
- A corrupt but provably unpaid Tarkova Cola plan file after a profile wipe, and a duplicate
  JSON key in a wiped profile, still stop SPT at startup (0.4.1 tolerates only inert
  leftovers and a complete history).
- Document drift found by the 2026-10-30 audit (source wins): BOT-MOVEMENT-REVIEW.md says
  walk/sprint 1.25/1.30, `Settings.cs` defaults both to 1.5 since `0312f78b`;
  AIR-CONTROL-REVIEW.md says default 60°/s, `Settings.cs` binds 80; POLYGON-AREA-REVIEW.md
  (lines 69 and 71) names `plugins\ArenaMode\AreaDrafts`, the export goes to
  `plugins/maschine-ArenaMode/AreaDrafts`; MODE-SIDEBAR-REVIEW.md says five modes, the
  catalogue has six; SHARED-BODY-COMPAT-20261023.md says "requires Core 1.0.10" (current
  source minimum 1.0.19, release 0.4.1 1.0.18); `tools/NativeMenuChecks/StandIns.cs` counts `LogDebug` as Info, so its
  "logs Info, not a warning" assertion cannot tell the two apart.
  [MAP-MODE-COMPATIBILITY.md](MAP-MODE-COMPATIBILITY.md) gained Bowl, Iceberg and the
  Domination column on 2026-10-31; its prose is still mostly the 2026-10-17 state.

Unresolved native observations:

- `SmokeGrenade.RemoveRigidbody` → `CG_TimerBehaviourCoroutine.MoveNext` NullReferenceException
  during protected-exit teardown, cause unattributed ([UX-FOLLOWUP-20261027.md](reports/2026-10/UX-FOLLOWUP-20261027.md);
  the full stack it linked under `artifacts/` was deleted in the 2026-10-30 cleanup).
- `BotMemory.GoalTargetBeingHitAction` NullReferenceException on a melee hit of a dying or
  stopping player (`BotMemory.Dispose` leaves `BeingHitAction` subscribed), seen 244 ms
  after a protected Stop and as the "rare native Zombie BotMemory damage NRE"
  ([GPU-MODE-COPY-MAP-ACCESS-RESULTS-20261009.md](reports/2026-10/GPU-MODE-COPY-MAP-ACCESS-RESULTS-20261009.md),
  "Open native observation"; [ZOMBIE-MODE-REVIEW.md](reviews/ZOMBIE-MODE-REVIEW.md)). Add
  no suppression or speculative damage patch.
- A direct-start entry was refused twice by an older normal process; cause unproven, the
  Debug reason codes now cover that boundary ([DIRECT-START-20261028.md](reviews/DIRECT-START-20261028.md)).
- Zombie melee path searches are synchronous: when the player becomes unreachable all 50
  infected recompute paths in the same frames (10 frames of 130-180 ms). Candidate fix: cap
  recomputations per frame ([ZOMBIE-BODY-CULLING-20261017.md](reviews/ZOMBIE-BODY-CULLING-20261017.md), "Nebenbefunde").
- Post-match item-preview contamination: read-only probe only, no causal fix
  ([PREVIEW-DIAGNOSTICS-REVIEW.md](reports/2026-09/PREVIEW-DIAGNOSTICS-REVIEW.md)). The
  user produces the affected state in a manual raid; the agent then inspects the item in
  the menu or Hideout, reopens the preview after ten seconds if throttled, compares clean
  and affected, and destroys no guessed scene object.
- Zombie teardown `Animator.GotoState` warnings have no proven owner.

Native coverage still missing (offline or harness coverage only):

- Equator Team DM at 10v10 and PitFireTeam rosters on AirPit
  ([GPU-AIRPIT-EQUATOR-20261017.md](reports/2026-10/GPU-AIRPIT-EQUATOR-20261017.md)); the
  crowded-box failure ([GPU-EQUATOR-TEAM-SPAWNS-RESULTS-20261011.md](reports/2026-10/GPU-EQUATOR-TEAM-SPAWNS-RESULTS-20261011.md));
  Tactical with one to three companions ([GPU-PFT-SQUAD-TEST-INSTRUCTIONS-20261013.md](reports/2026-10/GPU-PFT-SQUAD-TEST-INSTRUCTIONS-20261013.md)).
- Molotov cold-pool own gear, full-rig supply and all-map/all-kit coverage
  ([GPU-FOLLOWUPS-20261015.md](reports/2026-10/GPU-FOLLOWUPS-20261015.md)); Collateral
  without a native double-penetration kill; the PFT quick-slot ledger (clear a bound medical
  key after a round rebuild, use a medkit right before dying).
- Arena session spec section 10: item 6's helper-failure path (its handshake ran in the
  third run, line 1107) and items 7-11 ([ARENA-SESSION-20261021.md](ARENA-SESSION-20261021.md),
  lines 1310 and 1327); the 300 s preparation and 60 s native-arrival limits of Zombie
  waves (solo and PFT, `ZombieSpawnService`).
- Menu: the disabled PFT row and tooltip, a true 16:9 geometry
  ([MODE-SIDEBAR-REVIEW.md](reviews/MODE-SIDEBAR-REVIEW.md)); a genuine BEAR profile in
  the character preview ([CHARACTER-PREVIEW-REVIEW.md](reviews/CHARACTER-PREVIEW-REVIEW.md));
  exit waymarkers overlapping the status panel (status unknown).

Longer-standing: [FUTURE-WORK.md](FUTURE-WORK.md) (difficulty controls, further bounded
areas, objective modes such as Capture the Flag; its area section records prototype progress
and links the evidence). Multiplayer with Fika is a concept only
([MULTIPLAYER-FIKA-CONCEPT-20261026.md](reports/2026-10/MULTIPLAYER-FIKA-CONCEPT-20261026.md)).

## Responsibilities and machines

- `D:\SPT41\Development\ArenaMode` (branch `master`, remote `dev`) and
  `D:\SPT41\Development\Redeploy` (branch `main`) on this machine are the only writers.
  Claude Code and Codex sessions alternate on the same checkouts: finish and commit before
  handing over, keep `master`/`main` clean, never open a second writable clone or worktree
  that builds into the same installation, and never fix the same code in two sessions.
- The GPU hosts only run the game from the Syncthing mirror; nothing is edited or built
  there. Tests are driven from this machine through UiBridge (`uibridge_help` →
  `uibridge_status` → snapshots, waits, clicks) and `../UiBridge/tools/gpu-run`
  (`start`, `raid`, `leave`, `log`, `deploy-wait --host host2 --server --files …`,
  `server stop --force`). SPT-GPU-2 is preferred, SPT-GPU-1 is the fallback.
- Do not deploy to, or take control of, a GPU host on which another session is running;
  verify there read-only (disk SHA = loaded, via UiBridge) and draw no gameplay acceptance
  from such a check. Leave foreign worktrees, branches and a running second GPU session
  alone, and never compete with the user's mouse and keyboard during a run.
- Syncthing spreads **everything** under `D:\SPT41`: profiles, reward folders, DLLs,
  configs. Commits travel only through Git; create no second or competing sync mechanism,
  and change the existing one only after inspecting it. A profile broken on one host is
  broken on all; never write the same profile from two hosts at once (host 2 once overwrote
  host 1's earned state), back profiles up before any test that writes them (routine menu
  checks need no backup), and archive every canonical profile on both installations before
  a temporary server-profile/enum extension.
- Before a test, compare the mod versions and settings of the two hosts and keep test
  configuration changes explicit, so a mod difference is not mistaken for an Arena
  regression.
- Delivery into the mirrored installation needs no "are game and server stopped" question
  (user decision, 2026-09-08); verify local hashes, report remote sync separately, and
  restart the changed component before testing, because a copied DLL proves nothing about a
  running process.

## Continue on the GPU host

1. `uibridge_status` first; no answer usually means EFT is not running, not a dead host:
   `gpu-run start`. Start and restart EFT only through `gpu-run`.
2. Deploy with `gpu-run deploy-wait` and let it verify the hashes; a stuck old server needs
   `gpu-run server stop --force` before a redeploy. Record the ProductVersion/SHA-256 of the
   loaded DLLs with every result; hash equality on disk is not proof of what is loaded.
3. START ARENA · RESTART appears only when the mod set blocks direct play
   (`gpu-run click "START ARENA" --confirm "CONFIRM RESTART" --wait-restart`); test the
   session path with a Session.exe direct start (Windows-MCP `App` with
   `mode: "launch_executable"`, picker "Start Arena"). The two-click return is
   `RETURN TO SPT` then `CONFIRM RESTART`.
4. Leave raids via Esc/Backspace and `uibridge_wait inRaid:false`; before `EXIT`, wait with
   `afterRestart=true`. Keep waits under the client timeout; `continue=true` resumes.
5. Report per run: host, source commit, loaded hashes, mod versions, mode/map/kit, local
   time and zone, steps, expected/observed, pass/fail/blocked, and the log/screenshot paths
   under the gitignored `artifacts/`. Commit only a small, redacted report under
   `docs/reports/`; never profiles, tokens or unrelated logs.

## Parallel development without collisions

- One writer per repository. Exchange reviewed commits through Git, never by mirroring two
  writable working directories or `.git` folders; do not change the synchronization
  configuration without inspecting it and agreeing its scope.
- Worktrees isolate feature edits but share Git metadata and do not isolate the deployment
  directories: never build from two checkouts into the same installation or shared
  Redeploy output, and do not run concurrent `dotnet` builds in one checkout (parallel
  harness builds collide).
- Agree the starting commit, scope and touched areas before parallel work; integrate selected
  commits, rerun the affected checks, build once, then test that exact build. When both
  sessions stay active, one integrates and updates this handoff; each test run may have its
  own report.

## Build and dependency traps

- Keep the layout `<SPT>/Development/ArenaMode` with siblings `Redeploy`, `_decompiled`,
  `_reference` and `UiBridge`; `Shared.props` resolves the game root two directories above
  the repo and the server under `SPT_Runtime`. Another layout needs an untracked
  `Shared.props.user` with `SPTPath` and `SPTUserPath` (Redeploy has its own).
- `ArenaMode.Client` references `../Redeploy/Redeploy.Core` and stamps its
  `CoreVersion.props`; a Core DLL alone does not satisfy the reference, and every Arena
  build rebuilds and deploys Core unless `tools/AirPitChecks/NoDeploy.targets` plus the
  `SPTUserMods` sentinel suppress all deployment targets (`Run-ArenaVerification.ps1` does).
- Builds auto-deploy, including the server build triggered by a bare `dotnet test`; there is
  no generic `DeployToSpt=false` switch. Prefer Release; rebuild the client last when another
  check rebuilt the shared dependency in Debug.
- `SPT_DATABASE` must point at the `SPT_Runtime/SPT_Data/database` **directory** for the
  data-bound tests; a harness's `-Database` parameter (`templates/items.json`) is separate.
  Require the expected tests to run, not silently skip (`tools/Assert-ArenaTestRun.ps1`
  tolerates only the known environment skips).
- The full solution needs the installed BigBrain reference DLL; optional runtime
  BigBrain/PitFireTeam/SAIN/APBS versions must match the compatibility checks in
  `ArenaMode.Shared` rather than old notes.
- Licensed audio and attribution travel with the client assets through
  `tools/Write-ArenaAssets.ps1`; never deploy a guessed DLL set.
- Packaging a release needs the Redeploy checkout at the Core's build commit
  (`Package-TestRelease.ps1 -Release -CoreAssemblyPath … -CoreAssemblySha256 …` checks the
  Core's `ProductVersion`); make Redeploy's docs commits after the Arena package exists.

## Where to look next

- [AREA-RULES.md](AREA-RULES.md) and [TRAPS.md](TRAPS.md), then the review a rule names.
- [README.md](README.md): the documentation index (living references, `reviews/`,
  `reports/<YYYY-MM>/`, `evidence/`).
- [FEATURES.md](FEATURES.md) for what exists, [DEPENDENCIES.md](DEPENDENCIES.md) for the
  optional-mod contracts, [LOGGING.md](LOGGING.md) before adding a log line.
- Logs: client `<SPT>/BepInEx/LogOutput-<HOST>-<start>.log` (local time; see
  `BepInEx/logs/current-<HOST>.txt`), server `<SPT>/SPT_Runtime/user/logs/spt/spt-<HOST>-YYYYMMDD.log`
  (UTC). Kits and config: `<SPT>/SPT_Runtime/user/mods/ArenaMode/config/`.

Maintain this file as a concise, dated snapshot of decisions, open evidence gaps and the
next owner's scope. Append nothing historical here; dated records go to `reports/`.
