# ArenaMode — working notes for agents

Read this file completely. Then, for every area you touch, read its section in
[docs/AREA-RULES.md](docs/AREA-RULES.md) and the review documents that section names;
[docs/TRAPS.md](docs/TRAPS.md) lists the pitfalls that recur, and
[docs/HANDOFF.md](docs/HANDOFF.md) is the dated working state (versions, installed state,
open work). Most of it was learned the expensive way. Keep this file short: area rules go to
AREA-RULES.md, traps to TRAPS.md, state to HANDOFF.md, new documents to `docs/` (see
[docs/README.md](docs/README.md)). It stays at the repository root under this name: agent
clients discover it there, and the harnesses find the repository by walking up to it.

ArenaMode is a total-conversion mod for SPT 4.1 that turns Tarkov into an
Unreal-Tournament-style arena: instant respawn, kits instead of loot, and full restoration
of the entry profile. Optional rewards pay startup RUB mail plus an atomic PMC XP delta. Six
modes: Deathmatch, Gun Game, Team Deathmatch, Domination (a Team Deathmatch variant),
Tactical and Zombies, the last three marked WIP in the menu. Arena is solo by default; the
optional PitFireTeam path adds protected squads to Tactical and Zombies. Arena runs only
with whitelisted mods: a normal session with other mods offers START ARENA · RESTART (two
clicks; the second reads CONFIRM RESTART · Ns), which restarts the game with only the
allowed plugins (preloader gate `ArenaMode.Gate`) against a second server with a private
tree (helper `ArenaMode.Session`). No Fika dependency. Shared body and camera lifetimes live
in the sibling Redeploy.Core.

## Layout

| Path | What |
|---|---|
| `ArenaMode.Client/` | BepInEx plugin `com.maschine.ArenaMode` (assembly `maschine-ArenaMode.Client`, config `com.maschine.ArenaMode.cfg`; netstandard2.1, Harmony). The bulk of the mod. |
| `ArenaMode.BigBrainCompat/` | Plain optional BigBrain 1.5.0+ library `maschine-ArenaMode.BigBrainCompat` (netstandard2.1) for pursuit, pickups, Domination objectives and fire avoidance; no plugin GUID. Owned by Arena's Start/Update/OnDestroy; update both client DLLs together. |
| `ArenaMode.Server/` | SPT server mod `maschine-ArenaMode.Server` (net10.0) + the Blazor config page; `defaults/kits.json` and `defaults/ladder.json`. |
| `ArenaMode.Gate/` | BepInEx preloader patcher `maschine-ArenaMode.Gate` (netstandard2.1), deploys to `BepInEx\patchers\maschine-ArenaMode\`. |
| `ArenaMode.Session/` | The session helper `maschine-ArenaMode.Session.exe` (net472 WinExe) that switches between the normal and the private server tree; deploys next to the client DLL. |
| `ArenaMode.Shared/` | Linked source, not a project: protocol, DTOs, policies (`ArenaBotSpawnPolicy`, `ArenaPopulationPolicy`), map/area/team-box catalogues. Client and Server link all of it; Gate and Session link only `Shared/Session/`. |
| `ArenaMode.Tests/` | xunit (net10.0); references the server project (which compiles the shared sources, so the tests see them through that reference) and links selected client and session-helper sources. |
| `tools/` | The offline verification: about 195 harness folders. Most are a console program that links production sources; about 120 of them carry a `Verify.ps1` that runs the baseline and then hand-made mutants of the production code, some are script-only, some program-only (the runner falls back to `dotnet run`). Shared helpers (`Invoke-ArenaSourceChecks.ps1`, `Assert-ArenaTestRun.ps1`, `Read-ArenaInstalledSnapshot.ps1`, `AirPitChecks/NoDeploy.targets`), the entry point `Run-ArenaVerification.ps1` with its input model `ArenaVerificationInputs.ps1`, its worker checkouts `ArenaVerificationWorkers.ps1` and its register `verification-steps.txt`, the build/deploy/release scripts, and the kit and ladder generators live here too. |
| `docs/` | Living references at the top, feature contracts in `reviews/`, dated records in `reports/<YYYY-MM>/`, screenshots in `evidence/`, the earlier handoff and AGENTS.md in `handoff-archive/`. |
| `assets/` | Thumbnail artwork and the shipped default whitelist `arena-whitelist.json`. |

External, outside the repo:

| Path | What |
|---|---|
| `../_decompiled/` | The complete decompiled game source. **Verify every claim about game internals here.** |
| `../_reference/` | Other people's mods, for technique. Licence rules below (non-negotiable 3). |
| `../Redeploy/` | Sibling repository; `ArenaMode.Client` references `../Redeploy/Redeploy.Core` and stamps its `CoreVersion.props`. |
| `../UiBridge/` | The test bridge for GPU runs and `tools/gpu-run` (below). |
| `D:\SPT41\` | The game. Arena client DLLs/assets deploy to `BepInEx\plugins\maschine-ArenaMode\`; shared Core stays flat in `BepInEx\plugins\`. Syncthing mirrors the whole tree to the GPU hosts (`C:\SPT41`) and MASCHINE11. |
| `D:\SPT41\SPT_Runtime\` | The server. Server DLL deploys to `user\mods\ArenaMode\`. |

## Where the knowledge is

- [docs/AREA-RULES.md](docs/AREA-RULES.md): the per-area constraints, each naming the review documents to read first. Its "Overview" section holds the complete list of loading-time exceptions to non-negotiable 2.
- [docs/TRAPS.md](docs/TRAPS.md): the recurring pitfalls (engine, Harmony, SPT, Unity object identity).
- [docs/HANDOFF.md](docs/HANDOFF.md): versions, installed state, open work, how to continue on the GPU host.
- [docs/FEATURES.md](docs/FEATURES.md) (source-checked feature inventory), [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md), [docs/LOGGING.md](docs/LOGGING.md), [docs/MAP-MODE-COMPATIBILITY.md](docs/MAP-MODE-COMPATIBILITY.md).
- `docs/reviews/`: one contract per feature; `docs/reports/`: dated evidence, never proof about the current build. When a dated review or handoff entry disagrees with the current source, the source wins; fix the document.
- **Commit messages** explain *why*, including the mistakes; `git log` over a file you are about to change is the fastest way in.
- **Doc comments**: every non-obvious decision is argued at the code. If a comment says a rule is deliberate, it is; a comment that disagrees with the code is a bug too.

**UiBridge is allowed in every GPU run (user decision, 2026-09-18).**
`BepInEx\plugins\maschine-UiBridge.dll` (`com.maschine.UiBridge`, repo `../UiBridge`) is a
permanent part of the GPU host installation: a test bridge that lets agent sessions read the
UI, wait for game states and, with `[Actions] AllowActions`/`AllowTestHooks` switched on
(deliberately, keep them), inject ECommands, run item actions and set the look direction. GPU
test instructions must not treat it as an unexpected or unrelated plugin, must not remove or
disable it, and must not count its Harmony patches as contamination: they add nothing unless
a session sends a command or enables a test hook, and every action it takes is in the BepInEx
log as `action …`. Record its ProductVersion/SHA256 with the other unchanged mods; it is not
part of the Arena binding, so its hash is not an Arena hash change. A run may use it instead
of fixed click coordinates and sleeps (`../UiBridge/README.md`). Before the first game test
of a session call `uibridge_help topic=overview`: its playbooks hold the sequences and
failure pictures; this repository keeps only what is Arena-specific.

## Build and deploy

```bash
dotnet build ArenaMode.sln -c Release   # builds AND deploys both halves into D:\SPT41
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -Harness <folder>[,<folder>:<Stage>...]   # xunit once without deploying, then the named harnesses
```

All mod projects copy themselves into place on build, and so does a bare
`dotnet test ArenaMode.Tests`: the test project builds the server, whose deploy target
writes the live `user\mods` unless `tools/AirPitChecks/NoDeploy.targets` and a
`SPTUserMods` sentinel are passed (the runner does). Every build also rebuilds the sibling
Redeploy.Core through the project reference and deploys it flat into `BepInEx\plugins`
unless suppressed the same way. `ProductVersion` carries the commit, so a development build
replaces the installed release binaries with differently stamped ones; the GPU tests are
bound to installed hashes. Keep the deployment target names overridable: `SPTClientDeploy`,
`DeployArenaAudioAssets`, `DeployArenaGate`, `DeployArenaSessionHelper` and the server's
`DeployToSpt` (`NoDeploy.targets` empties all five, Core's sibling target included).
`StageArenaAssets`/`StageArenaPublishAssets` write only build/publish output.

Build/deploy and release packaging share `tools/Write-ArenaAssets.ps1` and
`tools/Arena-NoticeLayout.ps1`: notices and embedded map provenance go under the payload's
`docs/licenses`; do not reintroduce direct copies of source notice JSON. Preserve unknown
user files during migration. `tools/Deploy-ArenaClient.ps1` migrates the old
`plugins/ArenaMode` folder and flat owned DLL/PDBs into `plugins/maschine-ArenaMode`,
preserving user data and archiving replaced files outside plugin discovery; it is a
recoverable migration, not a transaction over the tree, and promises no byte-identical
rollback (docs/reviews/UNIFIED-DEPLOY-20261017.md has the limits and the no-deploy checks).
Release packaging: `tools/Package-TestRelease.ps1 -Release` with the Core from the matching
Redeploy release; the Redeploy checkout must sit at the Core's build commit while packaging,
and release identity comes from the tagged commit and `docs/RELEASE-<version>.md`, never
from a rebuilt hash or from a pre-release sweep of other binaries.

Core admission: both clients fail closed before runtime initialization when the loaded Core
assembly is missing, unknown or older than `RedeployCoreBuiltAgainst` (stamped from
`../Redeploy/Redeploy.Core/CoreVersion.props` at build time). Keep `Plugin.Awake` free of
Core types and `InitializeRuntime` NoInlining; Arena `Start`/`Update`/`OnDestroy` stay
guarded, including cleanup of rejected plugins. Arena needs Arena.Server for profile
protection; Redeploy.Core itself has no server dependency.

- **Client changes** need the game restarted, nothing else.
- **Hand edits to `config.json`, `kits.json` or `ladder.json`** take effect with the config
  page's "Reload from disk" (or a server restart); edits made in the page apply on Save. The
  client re-fetches the kit catalogue every raid.
- **Config page**: `https://127.0.0.1:6969/arenamode`, **https** with a self-signed certificate; `curl` and most browsers fail silently on `http://`.

Logs are per host: the client log is `BepInEx\LogOutput-<HOST>-<start>.log` since LogTools
1.7 (local time; `BepInEx\logs\current-<HOST>.txt` names the current file, and since
LogTools 1.8 `BepInEx\LogOutput.log` is a symlink that exists only on the machine that ran
the game), the server log is `SPT_Runtime\user\logs\spt\spt-<HOST>-YYYYMMDD.log` through
each host's `SPT_Runtime\sptLogger.json` file pattern (**UTC**; the two disagree by your
offset, which has cost hours before). `gpu-run log` reads both. Read
[docs/LOGGING.md](docs/LOGGING.md) before adding runtime logging: Info is for
player-relevant events, routine technical detail is Debug, expensive probes require
Diagnostics/DeveloperMode; never gate gameplay cleanup, protection warnings or the
`LogDamageExceptions` safety alarm.

## Tests

`ArenaMode.Tests/` is xunit and needs no game (checks that need the item database skip
themselves; point `SPT_DATABASE` at the `SPT_Runtime/SPT_Data/database` directory to run
them). About 3,500 tests in about 90 s plus the build. What the suite cannot hold are the
Harmony patches against Unity and EFT types, so **for anything client-side, verification is
still a raid**; the harnesses under `tools/<Feature>Checks/` cover the seams: each links
production sources into a console program and its `Verify.ps1` runs the baseline, then
hand-made mutants of the production code, each of which must fail its named assertion. A
mutant is anchored on a literal of the production source: a changed production line shows
up as "anchor missing" (`Test-ArenaSourceFaults` compares with LF line endings; the
harnesses that use `Invoke-Mutant` also insist on exactly one site).

Inside a fix loop run only the affected harness; the whole suite and the whole mutant sets
run once at the end of a pass, before the commit (`tools/Run-ArenaVerification.ps1`, which
also hashes the live install before and after and fails any step that deployed). The runner
keeps a result cache: a step (the suite included) whose inputs are unchanged since its last
PASS is not run again, and its verdict reads `PASS <step> 0s cached: inputs unchanged since
<time> (ran <n>s)`; a step that runs again names what changed (`ran: changed since last PASS:
shared, externals (install|D:\SPT41|BepInEx\plugins)`). The inputs are the files of the
checkout the step reaches, the installed game/BepInEx/SPT state, the sibling checkouts, the
tool versions, the relevant environment and, for steps that call git, HEAD; only a PASS is
kept, and only if the inputs did not move while the step ran. So a rerun after a docs-only
commit or a reviewer's identical pass costs seconds. `-NoCache` runs everything; **a release
gate runs with `-NoCache`**. The rules and their argued exceptions are in
`tools/ArenaVerificationInputs.ps1` and docs/TRAPS.md ("Verification runner"); a change to
them needs a case and a fault in `tools/VerificationRunnerChecks`. Do not compute the affected
set by hand: `-ChangedSince <ref>` adds every step registered in `tools/verification-steps.txt`
that compiles, names (mutant targets) or contains a file changed since `<ref>` (committed,
staged, unstaged, untracked), one `SELECT` line with the reasons each, and reports changed
harness folders that are not registered; `-ListOnly` only prints, `-All` takes the whole
register. Register every new offline harness there. Steps run four at a time (`-Parallel`,
1 to 8; 1 is the old sequential run in this checkout), each in a worker checkout
`D:\SPT41\Development\ArenaMode-vw<k>`: a persistent sibling worktree the runner makes
byte-identical to this checkout (HEAD plus every uncommitted, untracked and deleted file,
checked file by file) before it starts; never edit or commit in a worker, and remove one
only with `git worktree remove`. The mutants of `Test-ArenaSourceFaults` build side by side
too (`ARENA_MUTANT_PARALLEL`); every mutant must still fail its named assertion. Several
older wrappers run the whole suite themselves (`git grep -n 'dotnet test' tools`). When
adding a test, check that it fails against the bug it claims to catch (the zone test below
was verified that way: reintroduce the comma and three tests go red). The xunit tests exist
because these things broke repeatedly and quietly: kit catalogue invariants (the client
REFUSES a broken kit, so the symptom is a naked respawn mid-raid), the ladder draw (every
ammunition site on a weapon holding the same cartridge), and location mutation (every
generated wave names exactly ONE real zone; a comma-separated list matches nothing and the
wave is dropped in silence, which once emptied the Lab).

A native acceptance run counts only when the before/after whole-profile comparison differs
in nothing but the Health/Hideout bookkeeping timestamps, every PitFireTeam companion file
stays byte-identical, every BepInEx config value the test changed is restored
byte-identically, and the loaded Arena plugins match the deployed disk hashes, checked
loaded (UiBridge), not only on disk. Before temporary testing of any server profile or enum
extension, archive every canonical profile on both synchronized installations, not only the
selected test profile: a foreign enum extension leaves persisted entries (Skills Extended's
Hacking/SignalsIntelligence rows), removing its prepatcher can make those profiles
unloadable, and SPT's automatic backups rotate, so they are no substitute. Never restore
whole profiles merely to remove a test mod; review the exact residue and preserve unrelated
progress.

## Non-negotiable

1. **The profile is sacred.** An arena raid must cost and gain nothing through its
   raid-result merge. Default-ON RUB mail and PMC XP (the user may switch them off),
   journaled and paid at a later normal server start, are a separate, documented exception
   ([docs/REWARDS-RUNTIME.md](docs/REWARDS-RUNTIME.md)). Two layers protect it: an exact-ID
   `EndLocalRaidAsync` prefix that replaces the complete native post-raid pipeline, and a
   snapshot/restore around the raid. Never weaken either. Reward work does not relax the
   full-profile restoration, the full native restore readback is mandatory even with rewards
   OFF, and nothing of the private session tree is ever written back to the real runtime or
   profile.
2. **Normal raids must be bit-identical.** Every patch starts with an
   `ArenaSession.IsActive` gate and fails open to vanilla. A setting that leaks into a
   non-arena raid is a bug even if it is harmless. The loading-time and static exceptions
   are argued at their call sites and listed in full in AREA-RULES.md, "Overview": the one
   PFT Tactical kit before the first body, `ArenaProfilePreloadPatch`, `KitBundlePreloader`
   with `KitOpticPreloadGuard`, the Deathmatch group/relationship hooks,
   `ZombieNativeEventBridge`, `ArenaPostRaidScreens`, the Molotov item registration, and
   the user-authorized survey helpers (area outline and pickup capture, both off by
   default). Add none without the same argument.
3. **Licences.** ABPS, APBS, SAIN, RaidOverhaul, MoreBotsAPI, SVM, PitFireTeam, BigBrain:
   technique only; read them, never copy code, never bundle their DLLs
   (`tools/Verify-PitFireTeamDeployment.ps1` enforces the PitFireTeam part at packaging
   time). MIT/Apache sources (ORBIT, DragonDen, FieldKit, traumacore, bullettime) may be
   adapted with attribution; `THIRD-PARTY-NOTICES.md` is the attribution record and its
   distribution checklist binds every package. LacywayPvETweaks and LetMeRightClick are
   proprietary: do not touch.
4. **Adversarial review after every implementation.** This has caught a genuine critical
   bug in nearly every milestone: a hands softlock, a permanent-invulnerability latch, a
   deadlocked game mode. Do not skip it because a change looks small. The tests are a floor,
   not a substitute: none of those three would have been caught by one.

Three repository-wide prohibitions live in TRAPS.md with their reasons: never mutate
`GlobalConfiguration` (two argued exceptions, see there), keep exactly one `Profile.Clone()`
in the repository, and never call `GC.Collect`/`GC.GetTotalMemory(true)` from Arena code.

## How to work here

- Create a commit after every completed change, including its tests and documentation; do
  not leave finished work uncommitted or combine unrelated changes. Small, self-contained
  commits with a message that explains the reasoning. Do not push without authorization;
  `master` tracks `dev/master` on github.com/maschine34675/ArenaMode, so a push publishes.
- Verify game internals against `../_decompiled/` rather than assuming; it has contradicted
  confident guesses many times. When a playtest disagrees with your explanation, the
  explanation is wrong; measure before you theorise again.
- Every `ModulePatch` subclass must be named `Arena…` (SPT uses the bare class name as the
  Harmony id): `Arena` + outer type without its leading `Arena` and trailing
  `Patches`/`Patch` + the class name, unless the name already starts with that stem
  (`ArenaSessionModeCaptionPatch.LocationPatch` → `ArenaSessionModeCaptionLocationPatch`);
  a top-level class is `Arena` + name, server patches likewise (`ArenaServerBotCapPatch`); a
  primary-constructor patch class counts too. Three classes in
  `ArenaMode.Client/Session/ArenaRewardCapture.cs` still break it (HANDOFF.md, "Open work").
- A native GPU cycle with the exact package binaries comes before a release tag; a startup
  fatal on a test host is a release blocker.
- Documentation: new feature contracts go to `docs/reviews/`, dated records to
  `docs/reports/<YYYY-MM>/`, each with an index line in `docs/README.md`. Screenshots and
  logs stay under the gitignored `artifacts/`; a report quotes what matters, redacted.
  `CHANGELOG.md` stays at the repository root by explicit request, with its links rooted at
  `docs/`. Update `docs/HANDOFF.md` when handing work over or changing the agreed
  responsibilities; it is a snapshot, not a live record.
- User-facing communication is normally German; code, commits and documents are English.
