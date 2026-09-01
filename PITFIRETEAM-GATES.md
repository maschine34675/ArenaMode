# Gates: PitFireTeam compatibility

Scope: Allow PitFireTeam companions to join one Arena Tactical elimination round so four
selected companions produce a real five-player team against five Arena opponents.
PitFireTeam 0.10.0 exposes no safe leader-rebind or round-squad-reset contract, therefore
the compatible path deliberately keeps the original PMC body and equipment and ends the
raid after that one round. Preserve the existing multi-round solo Tactical flow, ordinary
raids, and every profile-protection guarantee. PitFireTeam remains an optional external
mod: inspect its runtime surface for compatibility, but do not copy its source or bundle
its DLL.

Safety boundary: Team Arena must fail closed when the PitFireTeam identity, supported
runtime contract, selected roster, or player-body rebind cannot be proven. Ordinary
PMC/Scav/PitFireTeam raids must always fail open to their original behavior. No gate may
be marked complete from a successful build alone.

## Offline and build gates

- [x] P1: Arena's side-selection gate accepts either the unchanged solo case or exactly
  four current PitFireTeam companions whose account IDs are present in PitFireTeam's own
  selected-member set and whose profile IDs identify four ordinary recruitable teammates.
  It rejects Fika/Coop, more than four companions, duplicate account/profile IDs, the three
  fixed Goon profiles, unknown local-group entries, stale selections, and an unsupported
  PitFireTeam runtime contract. Merely installing PitFireTeam does not change solo Arena
  eligibility.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED
  REQUIRED CASES: absent plugin; installed but empty selection; one and three authenticated
  companions rejected; exactly four authenticated companions; mixed Pit/non-Pit group;
  duplicate account/profile IDs, Goon, stale and oversized groups; Fika/Coop;
  unsupported PitFireTeam version/layout; selection changed between display and raid start.

- [x] P2: PitFireTeam's selected members are loaded before the final Arena claim is
  validated. The pre-raid shortcut must not skip the PitFireTeam callback/screen needed
  to establish that roster. Cancel, Back, PMC and Scav clear only Arena's transient claim;
  Arena never mutates or clears PitFireTeam's selected group as a side effect. Arena's
  native third toggle remains hidden while PitFireTeam reuses the scene for "My Squad",
  using PFT's own `SquadModeActive` marker rather than the vanilla Random toggle's default
  inactive state. A changed PFT layout that loses that marker hides Arena fail-closed; it
  must not hide Arena on the ordinary screen of the supported 0.10.0 layout.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED
  REQUIRED CASES: accept-screen path available with PitFireTeam; unchanged shortcut without
  PitFireTeam installed; with PitFireTeam installed the confirmation screen may remain visible
  because its server-backed auto-join roster is only loaded there; cancel/back cleanup; final
  validation uses the live roster.

- [x] P3: Tactical is the only Arena mode that accepts companions in the first compatible
  release. A companion roster selected for Endless Deathmatch, Rounds, or Gun Game is
  rejected with a clear reason before the server arms Arena. Solo behavior in all four
  modes remains unchanged.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED

- [x] P4: Team Arena never replaces the local EFT player body. It skips the initial Arena
  kit swap, keeps the PMC equipment that entered the raid, holds a dead local player until
  the team round resolves, and ends the raid instead of attempting a respawn or second
  round. A local death during the result delay remains held until the already-decided native
  result stop. Solo Arena keeps every existing kit swap and respawn. A code path that would
  start Redeploy while a Pit team is active aborts team Arena safely. Team Arena also skips
  background kit preloading; every preload is tied to a raid epoch so a canceled coroutine
  cannot mark bundles warm or leak work into the next ordinary or solo Arena raid. Gun Game
  promotion and its weapon replacement carry that same raid lease plus an operation generation,
  so a late completion cannot mutate a later player, clear newer flags, or invoke an old callback.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED
  REQUIRED CASES: team raid start; living team player; dead team player; attempted second
  round; absent/inactive PitFireTeam; solo raid-start swap and solo death respawn unchanged.

- [x] P5: Arena keeps one authoritative team roster for the complete protected team raid.
  PitFireTeam allies are excluded from Arena enemy/population counts and from
  teammate corpse trimming or gear stripping. Each Arena opponent and each live team
  member are mutually hostile, while friendly-fire/ally relationships are not rewritten
  as enemy relationships. Every selected profile must resolve to exactly one active physical
  follower body; an active unselected, missing-identity, duplicate-account or duplicate-profile
  body, or a replacement body for an already-seen teammate, ends the one round as no-contest
  rather than being silently deduplicated or treated as a respawn. All four followers must also
  resolve through one exact PitFireTeam leader wrapper whose readonly real-player reference is
  the original frozen local PMC; a stale, split or substituted wrapper fails closed.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED

- [x] P6: Tactical derives both requested teams from the authenticated roster. Four
  companions request and require exactly five friendlies and five opponents. Combat damage
  is held until every expected live member is registered and the complete bidirectional
  hostility matrix exists; a partial or timed-out team is a no-contest and ends the raid,
  never a smaller silently accepted round. The round ends on team elimination rather than
  merely on the local player's death. After the result delay, the protected raid ends once;
  no second round, follower reset or player-body replacement is attempted. During staging and
  the immutable-result aftermath, shots, Player damage side effects, explosion armor damage,
  health damage and contusion are all stopped at their earliest verified mutation surfaces.
  A guard exception remains fail-closed without scoring a second result. If the hostility matrix
  becomes unverifiable after live play begins, the result is no-contest rather than a guessed
  win or loss.
  The direct squad planner must choose one allowed zone that can hold all five distinct
  points at least 25 m from the player. It consumes the preferred 25-70 m band first and
  then fills from farther points; a short profile or point plan dispatches no partial team.
  A server-side location-mutator failure is reported to the client and ends the protected
  raid as no-contest before play; team Arena never continues with vanilla waves plus its five
  requested opponents. Any protected match-initialization failure blocks damage, preserves
  the frozen leader body and retries only a pre-commit native stop.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED
  NOTE: Offline checks may prove the policy and state transitions; live EFT behavior remains
  subject to the manual gates below.

- [x] P7: An Arena session suppresses every PitFireTeam persistence path for the protected
  session, including delayed/asynchronous follower progress, death/escape summaries,
  teammate-file writes/deletes and main-profile/mail effects. The guard is restricted to
  the exact Arena session and known PitFireTeam teammate path. It remains active through a
  bounded post-raid finalization window, then clears on completion, cancellation, failure,
  shutdown, and at the next ordinary-raid cutover only after older PFT operation readers have
  drained. Protection starts only for an explicit team Arena request; solo Arena never activates
  it. User-facing PFT management entry points are blocked as whole operations while Armed/InRaid
  so money, main-profile data and teammate files cannot diverge. During Idle finalization, an
  explicit management request may run only as one exact-session scope that also contains nested
  recovery, settings, notification and file work;
  the scope is invalidated on every return or exception and cannot authorize another session.
  CHECK: dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo
  EXPECT: Test Run Successful.
  REQUIRED CASES: allowed path outside Arena; blocked matching text/byte/async write and
  delete during Arena; different session/path allowed; path traversal/case normalization;
  delayed write blocked during finalization; whole management operation blocked while active;
  exact-session postraid management transaction allowed without partial state; other session
  remains blocked; expiry/cancel/next normal raid restores writes; solo Arena never suppresses.

- [x] P8: The existing Arena main-profile snapshot/restore remains intact, and all new
  compatibility patches return immediately when Arena is inactive. With PitFireTeam absent,
  incompatible, disabled, or throwing during discovery, ordinary raids and solo Arena retain
  their prior behavior, solo postraid does not start PFT finalization, and no optional
  PitFireTeam type is loaded eagerly. Protected native start and end own the complete async
  SPT transactions: identical HTTP retries share the same internal task, caller cancellation
  only stops that caller's wait, and an ordinary start is serialized against Arena mutation.
  The server binds the arm token to the exact SPT-generated native server ID and rejects every
  malformed, stale or foreign start/end marker. Failed-start restore waits until the native
  mutation task has settled; finalizing or crashed end work remains retryable from retained
  client state without permitting a second restore or vanilla post-raid fallthrough. When the
  Arena server mod is known present, an ordinary raid likewise owns one process/request-bound
  native start/end transaction for its complete lifetime; exact retries share work, failed ends
  remain retryable, the owning process cannot discard its confirmed live raid, and a replacement
  process can recover an orphan left by a crash. An active generation takes precedence over an
  older success tombstone if SPT reuses its seconds-resolution native server ID. A proven next
  ordinary GameWorld clears every stale client Arena match, abort, respawn and end latch before
  combat. Both server-side ordinary start paths recheck state/snapshot and clear the prior PFT
  finalization guard under one synchronous exclusive operation boundary; that writer is released
  before native asynchronous start work. With no confirmed Arena server mod, the ordinary request
  and server ID remain byte-identical to the original game.
  CHECK: dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo
  EXPECT: Test Run Successful.

- [x] P9: The Release solution and all offline compatibility checks pass against the
  installed SPT 4.1 assemblies without launching EFT. When PFT is installed, the offline
  harness reads the actual DLL metadata and proves the exact 0.10.0 selected-roster fields
  and My Squad marker instead of relying only on source-shaped regex checks.
  CHECK: dotnet build ArenaMode.sln -c Release --nologo
  EXPECT: Build succeeded.
  CHECK: dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release --nologo
  EXPECT: Test Run Successful.
  CHECK: dotnet run --project tools/PitFireTeamChecks/PitFireTeamChecks.csproj -c Release
  EXPECT: PITFIRETEAM_COMPAT_CHECKS_PASSED
  CHECK: dotnet run --project tools/NativeMenuChecks/NativeMenuChecks.csproj -c Release
  EXPECT: ARENA_MENU_CHECKS_PASSED
  CHECK: dotnet run --project tools/NativeMapViewChecks/NativeMapViewChecks.csproj -c Release
  EXPECT: PASS: 40 native map-view regression checks
  CHECK: dotnet run --project tools/NativeSideViewChecks/NativeSideViewChecks.csproj -c Release
  EXPECT: PASS: 29 native side-view regression checks
  CHECK: dotnet run --project tools/LabsBotSpawnChecks/LabsBotSpawnChecks.csproj -c Release
  EXPECT: LABS_BOT_SPAWN_CHECKS_PASSED
  CHECK: dotnet run --project tools/AnnouncerChecks/AnnouncerChecks.csproj -c Release
  EXPECT: ARENA_ANNOUNCER_CHECKS_PASSED
  VERIFIED 2026-09-01 after the M2 opponent-allocation fix: Release build 0 errors; unit 92/92;
  PitFireTeam 199/199; native menu 48/48; map view 40/40; side view 29/29;
  Labs bot spawns 75/75;
  announcer 78/78.

- [x] P10: The deployed Arena client and server DLLs are byte-for-byte identical to the
  verified Release outputs. The verification prints both SHA-256 pairs and confirms that
  neither PitFireTeam's DLL nor any PitFireTeam source/assets were added to ArenaMode's
  build output, deployment tree, or tracked files.
  CHECK: powershell -NoProfile -File tools/Verify-PitFireTeamDeployment.ps1
  EXPECT: PITFIRETEAM_DEPLOYMENT_VERIFIED
  VERIFIED 2026-09-01 after the M2 opponent-allocation fix: client build/deployed
  `7DC1679E3B0C47890CC311F38107D4A74C9E780EFBF36BD4A3E59E23EABCDA85`; server
  build/deployed `2528835212D286D90D84184C932EFB2129E86C91B012F407ED51E2DD35DB1B8A`.

- [x] P11: An independent adversarial review finds no path that can arm team Arena with an
  unauthenticated/stale group, strand followers on an old player body, leak Arena hostility
  or persistence suppression into a normal raid, modify PitFireTeam teammate storage, or
  weaken the existing main-profile protection. All material findings are fixed and the
  affected gates rerun before commit.
  REVIEW 2026-09-01: final full working-tree reread found no remaining High/Medium issue;
  independent client/Tactical, ordinary lifecycle/package, and server PFT callgraph audits clean.
  M2 FOLLOW-UP REVIEW 2026-09-01: the exact post-`Apply` repair, Harmony binding,
  reference identity, Back/cancel cleanup and ordinary/solo isolation were independently
  rechecked with no remaining High/Medium finding.
  M2 OPPONENT FOLLOW-UP REVIEW 2026-09-01: two independent final rereads found no
  remaining High/Medium issue after physical-point deduplication and post-profile-load
  zone replanning. Exact rejection occurs before native spawn accounting; solo, ambient
  and ordinary paths retain their prior best-effort behavior.

## Manual GPU gates

- [x] M1 MANUAL: With PitFireTeam installed but no companions selected, Arena starts as a
  solo raid exactly as before. PMC, Scav and an ordinary PitFireTeam raid also start and
  complete normally. No Arena-only compatibility log/action appears after Arena is inactive.
  VERIFIED 2026-09-01: User live test passed with unchanged behavior.

- [ ] M2 MANUAL: Select exactly four PitFireTeam companions, choose Arena -> Tactical, and
  start a supported map. The raid reaches live play with exactly the local player plus four
  controllable followers against exactly five Arena opponents. All opponents fight every
  surviving teammate; followers do not attack one another or consume the enemy quota.
  RETEST REQUIRED 2026-09-01: First live attempt reached the Labs scene load but was rejected
  before server arming. PitFireTeam had kept and Arena had re-authenticated all four companions;
  its local-ready branch caused vanilla to rewrite the final non-transit value from `Common` to
  `None`. Arena now restores only that proven `Common`-to-`None` change immediately after
  vanilla applies the clone; the clone itself stays untouched, the final request gate remains
  strict, and Back/cancel cannot leak the repair into an ordinary raid.
  RETEST REQUIRED 2026-09-01 after second live attempt: Labs loaded and all four followers
  arrived friendly, proving the local-ready repair. Arena requested five cached hard USEC
  profiles; source, cache and direct-spawn evidence traced the three-bot dispatch with high
  confidence to its point planner, although the old log did not yet print the pre-trim profile
  and point counts. The planner could select only the three points in the preferred 25-70 m
  band while discarding valid farther points. Exactly three opponents arrived and the strict
  3/5 readiness check correctly ended the raid as no-contest. The planner now fills the
  preferred band with distinct farther points and skips an allowed zone that cannot hold
  all five, replans after asynchronous profile loading, and deduplicates physical positions.
  Exact team requests dispatch no partial profile or point batch. Regardless of callback/log
  order, the next log must contain `requested 5`, `profiles 5`, a safe-zone plan with
  `selected 5`, `dispatched 5x`, exactly five arrivals, and `Tactical team Arena live`;
  no readiness timeout is acceptable.

- [ ] M3 MANUAL: No initial kit/body swap occurs in team Arena; the player enters with the
  PMC equipment shown on the confirmation screen and followers still accept commands. The
  local player's death does not dismiss living teammates or end the round while that side
  remains alive. Team elimination ends the round and then the protected raid once, with no
  respawn, second round, duplicate followers, stale enemies, or teammate corpse cleanup.

- [ ] M4 MANUAL: Hash/copy the main SPT profile and the complete PitFireTeam teammate
  directory immediately before team Arena. After extract/death/cancel, wait at least 35 seconds
  documented finalization window and compare again: both trees are byte-identical and Arena
  created no mail, rewards, insurance, progress, inventory, death/escape, recruit or follower
  persistence. Repeat with a deliberately delayed PitFireTeam outcome if the runtime exposes
  one.

- [ ] M5 MANUAL: During the result/finalization window, attempt one visible PitFireTeam
  management action, then repeat it after 35 seconds. It must either be rejected before any
  money/item/file change or complete coherently; verify that no partial main-profile or teammate
  state remains. Then run an ordinary non-Arena PitFireTeam raid that earns a visible teammate/
  progress change and confirm it persists. Finally run one ordinary PMC or Scav raid and one
  solo Arena raid to confirm both remain usable and solo Arena does not suppress PFT state.

- [ ] M6 MANUAL: Negative live cases (five or more companions, a mixed/unknown group member,
  unsupported PitFireTeam runtime, non-Tactical Arena mode, incomplete companion spawn and
  any attempted Arena body swap) refuse or cancel team Arena with a clear log/message. None continues as
  an unprotected raid, damages either profile store, or prevents the next ordinary raid.

ENVIRONMENT LIMIT: This server has no GPU. Do not launch EFT here. P1-P11 may be completed
with source inspection, offline harnesses, tests, build, deployment hashes and independent
review; M1-M6 remain explicitly open until the user tests the deployed build on a gaming PC.
