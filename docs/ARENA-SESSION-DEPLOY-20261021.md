# Arena session: solution, deployment and packaging (2026-10-21)

Integration notes for the five parts of [the session specification](ARENA-SESSION-20261021.md)
(section 9). Offline evidence only: nothing here was run in the game, and nothing was deployed to
an installation.

## What is installed where

| File | Built by | Installed to |
|---|---|---|
| `maschine-ArenaMode.Gate.dll` (preloader patcher) | `ArenaMode.Gate` | `BepInEx/patchers/maschine-ArenaMode/` |
| `maschine-ArenaMode.Session.exe` (helper) | `ArenaMode.Session` | `BepInEx/plugins/maschine-ArenaMode/` |
| `arena-whitelist.json` (shipped default list) | `assets/arena-whitelist.json` | `BepInEx/plugins/maschine-ArenaMode/` |

The client looks for the helper and the default list in that plugin folder
(`ArenaSessionContract.PluginFolderName`, `HelperFileName`, `DefaultPolicyFileName`), the gate
reads the list from the same place. A user's own list, `BepInEx/config/com.maschine.ArenaMode.whitelist.json`,
is never shipped, deployed or overwritten.

## Build and deploy targets

| Target | Project | Runs | Condition |
|---|---|---|---|
| `DeployArenaGate` | `ArenaMode.Gate` | `tools/Deploy-ArenaSession.ps1` after the build | `SPTModType == Patcher` |
| `DeployArenaSessionHelper` | `ArenaMode.Session` | `tools/Deploy-ArenaSession.ps1` after the build | `SPTModType == Helper` |
| `DeployArenaAudioAssets` | `ArenaMode.Client` | `tools/Write-ArenaAssets.ps1` (now also writes `arena-whitelist.json`) | after `SPTClientDeploy` |

`Deploy-ArenaSession.ps1` follows `Deploy-ArenaClient.ps1`: it accepts only the two named files with
a matching assembly identity, writes nowhere outside the selected installation, refuses a link or
junction anywhere on the path, stops before any write when a target is locked (a loaded gate, a running
helper), copies and hash-checks a staged file before it replaces the old one, skips byte-identical files
and, in a Release build, moves a stale Debug `.pdb` to `BepInEx/backups/maschine-ArenaMode-layout/`.
There is no layout to migrate: both files are new in every installation.

Every one of these names is part of the no-deploy contract:

- `tools/AirPitChecks/NoDeploy.targets` now empties `DeployArenaGate` and `DeployArenaSessionHelper`
  next to the three existing names. The packager writes the same five into its own no-deploy file.
- The repository's other switch, `-p:SPTModType=NoDeploy`, also suppresses both new targets (their
  conditions) as it does `SPTClientDeploy`.
- `ArenaMode.Session` does not import `Shared.props` (see its project file): it derives `ArenaRepoRoot`
  and `SPTPath` itself, and its `<Version>` is repeated by hand. The packager checks the version of the
  built exe against `Shared.props`.

`ArenaMode.sln` builds both new projects in Debug and Release (all platform rows map to Any CPU).
`dotnet build ArenaMode.sln` therefore deploys the gate and the helper like the other parts; offline
builds pass the NoDeploy properties as before.

## Default list

`Write-ArenaAssets.ps1` names `assets/arena-whitelist.json` explicitly (like `LICENSE` and the README,
without the tracked-file filter) and refuses to continue when it is not a schema 1 policy, has no entries or
has an entry without exactly one of `guid` and `patcher`: a broken default list makes every Arena session
unavailable, so build, deployment and the release ZIP stop instead. Since 2026-10-22 the same check covers the
optional `deny` member (an array of non-blank strings; the loader fails the whole default file otherwise),
requires it to name the five refused mods (a missing, empty or differently spelled `deny` would silently turn
them into plain unlisted mods) and refuses a unit that `mods` lists and `deny` names (the loader would deny it
and report it at every start).

## Release package

Layout identifier `spt-root-folders-v6` (a new `BepInEx/patchers` folder). `Package-TestRelease.ps1` builds
the gate and the helper from the frozen snapshot, checks their assembly version and embedded source
revision like the other binaries, adds them to the payload and describes the session in `INSTALL.txt`. A
selected revision without the two projects and the default list is rejected before any build.
`Verify-TestRelease.py` now requires the gate in the exact DLL set, the helper as the only executable and the
default list as a schema 1 policy that still names every mod of the 2026-10-22 decision (SAIN, APBS, ABPS,
BigBrain, Waypoints and all the other installed mods) and their two file patchers (`WTT-ContentBackportPatcher.dll`,
`maschine-LogTools.dll`), keeps the five denies (ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special Forces,
Aishi) in a well-formed `deny` array, and does not allow what it denies.

## Offline evidence (integration run, 2026-10-21)

Everything below was built and run with the no-deploy properties; the installation under `D:\SPT41`
(plugin folder, patchers, server mod folder, Core) was compared before and after and did not change.

| Check | Result |
|---|---|
| `dotnet build ArenaMode.sln -c Release` | 0 errors, 27 warnings (none from the session files); the gate and the helper build in Debug and Release for every solution platform row |
| `dotnet test ArenaMode.Tests` | 1761 passed, 2 skipped (both need the installed game), 0 failed; the helper's `SessionLogic.cs` and `SessionFileSystem.cs` are linked into the test project |
| `tools/DeployLayoutChecks/Verify.ps1` | 126 checks: the real MSBuild targets against scratch roots, now also for `DeployArenaGate` and `DeployArenaSessionHelper` (fresh, repeated, replaced, Debug/Release symbols, both NoDeploy switches, wrong or renamed assembly, locked file, junctions at three places, directory in the way) and for the default list (installed, overwritten by an update, user list untouched) |
| `Write-ArenaAssets.ps1` into a scratch folder | 64 files including `arena-whitelist.json` (byte-identical, repeat run rewrites nothing); six broken lists (schema 2, no or empty `mods`, entry with both or neither key, invalid JSON) are refused before anything is written |
| `Package-TestRelease.ps1` on a scratch snapshot of the tree (committed in a scratch repository, because the real tree stays uncommitted) | `PACKAGE_READY`, 74 files, layout v6; gate, helper and list are in the ZIP; `Verify-TestRelease.py` accepts it |
| `Verify-TestRelease.py` negative controls (scratch) | 17 of 17 defects rejected for the intended reason (missing or foreign gate, helper or list, second executable, stray patcher DLL, symbols, bad schema, entry shape, lost default mod, JSON error, layout id, changed byte) |
| `Deploy-ArenaSession.ps1` fault controls (scratch) | 8 of 8 mutants of the script fail a scenario (identity check, lock check, link check, rewrite of identical files, symbol retirement and copy, folder choice x2) |
| `tools/ArenaGateChecks/Verify.ps1` | 11 scenarios, 265 checks, 8 mutants rejected |
| `tools/ArenaAvailabilityChecks/Verify.ps1` (NativeMenuChecks, OptionalModAdmissionChecks, session ping, `ArenaSessionClientChecks`) | `ARENA_AVAILABILITY_VERIFIED` |
| other client harnesses of the parts' reports | `Verify-QuietSettingsTransport`, `Verify-SessionModeCaption` (35 mutants), AreaTeamMenu, ParticipantCount, EquatorTeamSpawn, ArenaCharacterPreview, ArenaUiInteraction, PitFireTeamChecks (370), NativeSideView (29), ParticipantServerChecks, SpawnGateChecks, WeaponVariantChecks: all pass |
| harnesses that link a changed file (`ArenaServerClient.cs`) | AiLimit, ClassicMovement, ContinuousLoadAmmo, Deminvincibility, DurationDifficulty, Effects, LootingBots, Molotov, NativeModelDiagnostics, Pause, PftTacticalKit, RecoilLegacy, RemoveTheDead, SkillsExtended, StatTrack, ArenaLabyrinthAccess pass (the foreign-mod ones with the published DLLs of the main checkout's `artifacts/`, as their `Verify.ps1` expects) |

A throwaway cross-check (not part of the repository) compiled the real `ArenaSessionSwitch.cs` next to the
helper's `SessionLogic.cs`: `HelperRequest.TryParse` accepts the request files the client writes for `enter` and
`leave`, so the two sides agree on the request format.

Limits: no game, no GPU host. The gate's patcher order and `Finish()` timing, the helper against the real
`SPT.Server.exe`, and the client UI are acceptance items of the specification (section 10). A solution build
without the no-deploy properties installs the gate and the helper, like it installs everything else.
`Package-TestRelease.ps1` reads the embedded version with `FileVersionInfo`, which returns nothing for a path
over 260 characters: give it a short `-OutputDirectory` (the failure message then shows an empty version).

## Review fixes (second pass, 2026-10-21)

An adversarial review of the three parts found these; each was checked against the code first and then fixed. Offline
evidence only, nothing was deployed or run in the game.

| Finding | Change | Evidence |
|---|---|---|
| Neutralised preloader patchers were named at Debug, which the preloader's log listener drops | One Info line at the end of `Initialize()` names them; the Error line of a later gate failure says that they stay off | `ArenaGateChecks`: 11 scenarios, 275 checks, 10 mutants rejected (two new) |
| Spec sections 5 to 8, README, `DEPENDENCIES.md`, `AGENTS.md` and the docs index did not describe the implementation | Sections rewritten, change list added (section 11), whitelist section and install tree in the README, release boundaries, overridable deploy targets, index links | Read against the code; no harness pins these texts |
| `stop-server` compared the process image as text: a junction or SUBST spelling of the install made it answer "not ours" and leave the arena server running | The image is compared as a file (`GetFinalPathNameByHandle`) | `ArenaSessionHelperProcessTests` (12 tests; the stop and leave tests were mutation-checked) and a scratch end-to-end run of enter, leave and stop against stand-in server and game executables (100 checks, five fault controls) |
| The session folder, mutex and pid file are shared by all installations of a Windows user: a second installation stopped and replaced the first one's arena server | `StopArenaServer` takes the caller's runtime and leaves a server of another installation alone (`OtherInstallation`); `enter` fails and restores the normal session, `leave` carries on | same |
| `leave` and the recovery after a failed enter ended when `server.pid` could not be read or deleted | The cleanup is guarded; the game is started anyway | same |
| A listener that was not the started server (or a server that had exited) could be taken for it | `exited` is checked first; the listening process is looked up (`GetExtendedTcpTable`) and must be the started one | same |
| The client quit the game before the helper had read the request; a request it refused (profile id that is not 24 hex, server mods outside `<game>\SPT_Runtime\user\mods`, no server mods) left the player with no game | The helper's own parser (`SessionLogic.cs`, now linked into the client) runs on the request text before anything is written; the profile id rule is the helper's; the session is not offered for a server the helper cannot switch | `ArenaSessionClientChecks`: 62 mutants rejected (three new, one replaced) |
| Comment claimed server mod copies confine everything the mods write | Comment and spec say only the helper's own operations are confined | n/a |

Limits that remain: a server that fails at startup stays alive behind "Press any key to exit..." (SPT's
`Program.WaitForExit`), so such an `enter` still takes the 180 s wait before the normal session returns; a request
that the helper rejects for a reason the client's copy of the parser does not know (a client and a helper from
different builds) still ends without a restart; the end-to-end run used stand-ins, not the real `SPT.Server.exe`.

## Server binaries in the tree (2026-10-21)

The helper no longer starts the installation's `SPT.Server.exe` for the arena server. `enter` copies the
server binaries (every top-level file of `SPT_Runtime` except `sptLogger.json`, `SPT.Launcher*`, anything with
`Linux` in its name and `*.so`; 44 files, 23 MB for 4.1.6) into the tree root and starts `<root>\server\SPT.Server.exe`,
so a server mod that resolves its paths from `AppContext.BaseDirectory` writes into the disposable tree and
not into the installation. Nothing else is installed or deployed differently: the helper exe is the same file
in the same place. Specification: section 6 and the last entry of section 11 of
[ARENA-SESSION-20261021.md](ARENA-SESSION-20261021.md).

| Check | Result |
|---|---|
| `dotnet test ArenaMode.Tests --filter ArenaSessionHelper` | 384 passed, 0 skipped (symbolic-link tests included); 14 mutants of the new code checked, each rejected by at least one test (incremental skip, length/time rule, exclusion rules, stale binaries, link replacement, marker order, tree-versus-installation identity, older-marker acceptance, folder warning, missing exe, marker member) |
| Helper `Enter`/`Leave`/`stop-server` against a stand-in server with the real layout (scratch, not in the repository) | 49 checks: the server's image and `AppContext.BaseDirectory` are the tree, the runtime is unchanged, a second `enter` copies 0 of 6 binaries, an older helper's session (marker without `serverExe`) is stopped by `stop-server` and by `enter`, `leave` starts the normal server from the runtime as before |
| Real `SPT.Server.exe` 4.1.6 from a tree built out of a scratch copy of the installed runtime (scratch, not in the repository) | `enter` 18 s including the server start; 44 files copied; four server mods loaded; `/health` answers; the Arena instance is recognised; the copied runtime was not written to; `stop-server` ends it |

Limits: no GPU-host run of the whole switch with this change yet (the acceptance in section 10 predates
it), and a runtime with top-level folders besides `SPT_Data` and `user` gets a warning in `helper.log`
because the tree does not contain them.

## Review fixes (third pass, 2026-10-21)

Three findings of a policy and tree review, each checked against the code and the decompiled server first. Offline
evidence only, nothing was deployed or run in the game.

| Finding | Change | Evidence |
|---|---|---|
| Server mods were evaluated without dependency data, so a `deny` (or, under a whitelist, an omission) of a library that an allowed server mod lists in `ModDependencies` was honoured; SPT's `ModValidator` then logs `modloader-no_mods_loaded` and loads no mod at all, ArenaMode's server mod included, and the helper only waits for the port | The ping's `serverMods` entries carry `dependencies` (the GUIDs of `ModDependencies`, sorted, always an array); the client reads them (absent, malformed or non-string entries = none) and passes them as hard dependencies, so the existing closure keeps the library in the tree with the usual warning. The closure stays per side: a client plugin pulled in as a dependency does not pull in a server mod that merely shares its GUID | `ArenaServerInstanceTests` (ping member set, sorted GUIDs, empty array), `ArenaWhitelistTests` (server-side closure under blacklist and whitelist), `ArenaSessionClientChecks` (scenarios plus the mutant `admission-server-dependencies-dropped`), `Verify-SessionPing.ps1` (two new mutants on the ping parser) |
| A blacklist applied a `deny` list in part: a `deny` that is not an array, or an entry that is not a non-blank string, was skipped with a warning while the mode stayed `allow`, so the mod the user meant to exclude loaded | In blacklist mode only, such a `deny` invalidates the whole user file like an unknown `unlisted` value (diagnostic `"deny" must be an array when "unlisted" is "allow", the file is ignored`, shipped whitelist in force). A mistyped `allow`, and under a whitelist a mistyped `deny`, stay per-entry: nothing unlisted loads there | `ArenaWhitelistTests` (eleven malformed shapes, readable lists, whitelist control; mutation-checked: with the check off 11 of them fail) and one `ArenaSessionClientChecks` scenario |
| An older helper (the 0.1.3 build before the server binaries moved into the tree) treats the new marker's tree server as "not ours", deletes `server.pid` and leaves the server running | Not fixable from the new helper, and the proposed helper-only version bump is not applied: `Package-TestRelease.ps1` requires the helper's assembly version to equal `Shared.props`, and the next release bump is the release's decision. Documented instead (specification, section 6): every installation of one Windows user must receive the new helper together; the marker's `serverExe` member and `status` tell the builds apart; an orphan left by an older helper has lost its pid file, so `stop-server` cannot find it and it is ended by hand | text only |

Limits: the closure for server mods depends on the ping carrying `dependencies`; a server mod build without them (a
client newer than its server mod, which a release never ships) behaves as before this pass.

Offline results after this pass (no-deploy properties throughout; nothing under `D:\SPT41` outside the worktree was
written, checked by modification time): `dotnet build ArenaMode.sln -c Release` 0 errors; `dotnet test ArenaMode.Tests`
1894 passed, 2 skipped (both need the installed game), 0 failed; `Verify-SessionPing.ps1` 777 assertions and 9 mutants
rejected (two new); `ArenaSessionClientChecks/Verify.ps1` 194 assertions and 66 mutants rejected (one new);
`ArenaGateChecks/Verify.ps1` 12 scenarios, 321 checks, 11 mutants rejected; `DeployLayoutChecks/Verify.ps1` 126 checks.
