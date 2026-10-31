# ArenaMode working handoff

Snapshot: 2026-10-30. Read [AGENTS.md](../AGENTS.md) first, then the
[area rules](AREA-RULES.md) for what you touch. This is a compact handoff, not a transcript
and not a live synchronization channel: verify files, commit IDs, installed artifacts and
logs before treating a recorded result as current; a new session has no access to the
earlier conversation or the agent's memory. The complete earlier handoff (5,917 lines,
2026-09 to 2026-10-29) and the earlier `AGENTS.md` are archived in
[handoff-archive/HANDOFF-2026-10-29.md](handoff-archive/HANDOFF-2026-10-29.md) and
[handoff-archive/AGENTS-2026-10-29.md](handoff-archive/AGENTS-2026-10-29.md), text and line
numbering unchanged, only their links and document paths re-rooted for the new layout.

## Released and installed

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
  the release binaries of ArenaMode 0.4.1 and Redeploy 1.1.1. Recoil Rework Legacy,
  installed temporarily for the 1.1.1 native check, was removed again on all hosts including
  its generated `com.pein.camerarecoilmod.cfg`. Any development build replaces these with
  differently stamped binaries; say so in the next handoff.
- Profile `SPT-GPU-2` (`6ab7db1c…`) was wiped by the user in the SPT launcher on
  2026-10-29 while it carried Tarkova Cola reward history. Since 0.4.1 SPT starts with one
  Warning and that profile's rewards stay off ([REWARDS-RUNTIME.md](REWARDS-RUNTIME.md),
  "Lost reward authority"); they return only after the documented reward-folder rename at a
  stopped server.
- Repository hygiene 2026-10-30: `docs/` reorganised (reviews/, reports/, evidence/,
  handoff-archive/), `test-evidence/` moved to `docs/evidence/`, `assets/archive/` deleted
  (its thumbnail iterations stay in git history before this change), `AGENTS.md` cut to
  the global rules, this handoff rewritten. The local branch `codex/ux-menu-20261029`
  (merged) was deleted. On the development machine the gitignored `artifacts/` folders
  older than 2026-10-28 and all `bin/`/`obj/` output were deleted; raw evidence those
  folders held (for example `artifacts/ux-followup-20261027/private-errors.txt`) is gone,
  and the reports that linked it keep only their summaries.

## Open work

**Iceberg (2026-10-31, branch `feature/iceberg`, not merged, not pushed):** `arena_iceberg` is
integrated like Bowl in four commits (admission with Tactical refused, CheckPoint team boxes,
Domination A/B/C, docs); see [ICEBERG-INTEGRATION-20261031.md](reviews/ICEBERG-INTEGRATION-20261031.md).
Offline checks only: the **native GPU test is pending** (pockets 4v4/10v10, gate hold and
release, Bear house (12) clearance, Domination A in the pool and B, PFT Zombies, pickups).
It needs an ArenaMapPort build that ships `arena_iceberg`: today only the unreleased MapPort
`feature/iceberg` build `0.4.0+32fda33` installed here. The published ArenaMapPort 0.4.0 has
none and carries the same version string, so ArenaMode detects the map by presence (live
location, plugin identity), never by version; the whitelist row stays 0.4.0. Do not announce
Iceberg in an ArenaMode release before MapPort publishes it. Nothing was deployed: the
installed binaries are still the 0.4.1 release.

- **Installed MapPort data is a harness dependency.** `tools/ArenaPickupChecks` reads every
  installed ported map, Iceberg included, and fails loudly without
  `user/mods/ArenaMapPort/db/arena_iceberg/base.json`; so do `IcebergChecks`, and through
  ArenaPickupChecks also `BowlChecks`, `BowlTeamBoxChecks`, `IcebergTeamBoxChecks` and any
  full harness run. They need an ArenaMapPort build that ships `arena_iceberg` (the feature
  build `0.4.0+32fda33` or a later release); the published 0.4.0 is not enough.
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
  minimum 1.0.18); `tools/NativeMenuChecks/StandIns.cs` counts `LogDebug` as Info, so its
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
