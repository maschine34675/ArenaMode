# Arena session: answer to the external review (2026-10-23)

Status: written on branch `feature/arena-session-review-20261023` (worktree `ArenaMode-sr3`), 2026-10-23, merged into
master the same day. The binding specification is [ARENA-SESSION-20261021.md](ARENA-SESSION-20261021.md); every
change below is also recorded in its section 11, and the GPU run of this pass in its section 10 (fifth run). The
"Evidence and limits" section at the end says what ran and what did not.

## What was reviewed

A second model (Codex) reviewed the Arena session at `c021c0d` (the watchdog build of 2026-10-23) with two independent
sub-reviews (helper file system and process lifecycle; preloader gate and policy) and a root that re-verified both
against the sources and reproduced the two code findings with unchanged production sources. The report is
`.unlazy/session-review-20261023/review.md` in the main checkout (`D:\SPT41\Development\ArenaMode\.unlazy\...`, an
ignored folder; `helper.md`, `policy.md`, `backup-repro/`, `repro/` beside it), not in the repository. It made no
change and ran no game. Its verdict: the new session design can replace the separate launcher prototype; the three
findings below need no return to that architecture. The prototype (`8c34f54` on `feature/arena-start-prototype`) was
archived and deleted by the user afterwards.

The review's own checks at `c021c0d`: the whole unit suite (2 289 passed, 1 known skip), the client session harness
(679 assertions), the gate harness under Unity's Mono (17 scenarios, 617 checks), the net472 helper baseline (7 checks),
`tools/Run-ArenaVerification.ps1` with `passed=2 failed=0` and the installed files unchanged. It did not run the mutant
sets and did not start the game.

## The three findings, all corrected

### 1. The private server kept the normal server's backup destination (P2)

*Finding.* `HelperServerTree.Build` copied `SPT_Data\configs\backup.json` unchanged and rewrote only `http.json`. SPT's
`BackupService` writes the copies of the server's profiles to `<directory>/<date>` resolved against the working
directory, deletes the oldest folders there beyond `maxBackups` and restores a profile from the newest one. With an
absolute `directory`, or a relative one that climbs out of the tree, the arena server would have written the
disposable profile's copies into the normal server's pool, where that cleanup deletes the normal server's backups and a
later restore may pick an arena copy. Reproduced by the review with the unchanged `HelperServerTree.Build` on a
synthetic tree (absolute and `../..` escape, the relative default stays inside). The installed file uses the shipped
value `./user/profiles/backups`, so this installation was not affected.

*Agreed.* The logger paths had the same hole closed on 2026-10-22; the backup directory was missed.

*Changed.* `HelperJsonText.TryConfineBackupDirectory` (SessionLogic.cs): the top-level `directory` of the tree's copy is
replaced by `./user/profiles/backups` unless it is a relative path below `user` by the rule of the logger paths
(`IsLogPathUnderUser`: `.` and `..` resolved on the text, any case, either separator; a drive, UNC or root-relative
path, a colon, a wildcard, a `..` that climbs out of `user`, a path into any other folder of the tree such as the
`SPT_Data` junctions, and a segment Windows would rewrite are all moved), and the member is **added** when it is missing.
That last rule came out of this pass's own review: the first draft left a missing member alone, on the premise that
SPT's default is the private folder. It is not. `./user/profiles/backups` is only the value the shipped `backup.json`
carries; SPT's `BackupConfig.Directory` defaults to the empty string, which the service resolves to the root of the
working directory's drive, so a missing member is as unsafe as an absolute path. A `directory` that is not a string is
replaced as well (fail closed); a member of that name deeper in the document is left alone. The confinement runs on
every file SPT's loader reads as the backup config (`HelperLayout.IsBackupConfigFile`: extension `.json` or `.jsonc`,
name without extension a substring of `spt-backup`, compared exactly as the loader does; the loader keeps the last file
it reads, so every match is confined), also a finding of this pass's review: the first draft confined the literal
`backup.json` only. A runtime without any such file is left as it is; the helper invents no config file.
`HelperServerTree.Build` applies it right after the `http.json` patch and records every change in
`HelperTreeResult.BackupDirectoryRewrites` (`<file>: 'old' -> 'new'`, `<file>: (no directory member) -> 'new'`); the
runner logs one Warning for them (`backup config: N backup directory value(s) did not lie below user ...`). The
runtime's own files are never touched; a copy that needs no change is byte-identical. Spec section 6 (the tree bullet
list) describes it; the README's session paragraph says that the session's backups stay inside the private copy.

*Tests.* `ArenaSessionHelperTests`: eight escaping values are moved (absolute with either separator, `../..`, a `..`
through `user`, a path into `SPT_Data`, another tree folder, a root-relative path, the empty string), four values below
`user` are kept byte for byte, a missing member is added in the file's own style (indented, compact, empty object) and a
second pass changes nothing, a nested member is left alone, a non-string member is replaced, malformed JSON is an
error; a built tree from a fixture runtime with an absolute destination carries the private folder while the fixture's
file is unchanged; a runtime without the file or with the shipped value needs nothing; `backup.jsonc` and
`spt-backup.json` are confined too (the loader's rule is pinned for eight names) and a malformed file fails the build
with its name. `ArenaSessionHelperProcessTests`: an enter logs exactly one Warning naming the move, the tree's copy is
confined, the runtime's file untouched. The fixed names (`backup.json`, `spt-backup`, `./user/profiles/backups`) are
pinned with the other contract names. Negative control of the first draft: with the `IsLogPathUnderUser` test removed
from the production code the eight "moved" cases and the tree test fail.

### 2. A patcher denied after the start did not refuse the Arena session (P2)

*Finding.* `ArenaWhitelistAdmission.ComputeArenaSession` judged the patchers that ran before the gate
(`patchersRanBeforeGate`), the plugins this game loaded and the server's mods with the policy as it reads now, and
compared the two start hashes (gate and server). A preloader patcher the policy allowed when the game started, and that a
user file denies afterwards, was in none of these groups: the session stayed admissible although it still contained that
patcher's work, while the same policy refused the patcher at a direct start from a normal session. Reproduced by the
review with the unchanged production sources in the client harness's world (`WTT-ContentBackportPatcher.dll` allowed at
the start, denied by the user file, screen reopened: admitted; controls: the normal session refused it, a new deny of
SAIN refused the Arena session).

*Agreed.* The policy-stamp work of 2026-10-22 made the change visible; the evaluation then had nothing to judge the
allowed patchers against, because the report never named them.

*Changed.* The gate records the patchers it let run (`ArenaGatePatchers.Outcome.Allowed`, each file once; the gate itself
and BepInEx's own patcher are not judged and not listed, platform patchers are) and publishes them as `patchersAllowed` in
every report: provisional, final and inactive (`ArenaGateRuntime.AllowedPatchers`, cleared with the rollback;
`ArenaGateReport.PatchersAllowed`, written as an empty array when none, read as empty from a report of an older gate).
The client judges them with `IsAllowedPatcher`, the gate's own rule, right after the `patchersRanBeforeGate` check and
before the loaded-plugin check: `This game ran the preloader patcher <file>, which the current policy excludes: the
policy changed during the switch. Return to the normal session and start the Arena session again.` (several files in the
plural; names sorted, once each, twelve at most). A patcher the gate neutralised is not in the list and cannot refuse,
whatever the policy says now; a policy that allows one of them now is a widening and refuses nothing, like everywhere
else; the refusal needs no ping. Spec: section 4 (the member), section 5 step 2, section 7 (new check 4; the old checks
4 to 7 are 5 to 8, the two section 11 entries and the answer of 2026-10-22 that cited the old numbers say so),
`ARENA-GATE-20261021.md`.

*Tests.* `ArenaGateReportTests` and `ArenaWhitelistTests`: the member travels through the JSON as the last key, is copied
by `FromDecision`, is present and empty where nothing was let run, and reads as empty from an older report (only file
names are read). `tools/ArenaSessionClientChecks` (`CheckArenaSessionPatchersAllowed`, 19 new assertions): the review's
scenario refuses with the exact text and is logged once (a new ping does not log it again); the refusal needs no ping;
several files in the plural, sorted, once each; twelve names at most, the rest counted; a file listed twice is one file,
in the singular; a deny or an allow of a neutralised patcher changes nothing; blacklist mode admits an unlisted patcher
the gate let run and refuses a user deny of it; an older report has nothing to judge; a patcher that ran before the gate
is refused first, the new check second, a loaded plugin third; the normal session still names the denied patcher. Eight
new mutants (the check dropped, judged by listing instead of `IsAllowedPatcher`, singular only, the names uncapped,
duplicates counted, the log line dropped, swapped with the `patchersRanBeforeGate` check, swapped with the loaded-plugin
check). The check runs after the older session checks of the harness, so that every older mutant still fails its own
assertion first (this pass's review found six that no longer did when the new check ran ahead of them, and one swap
mutant whose span had swallowed the new block; both corrected). `tools/ArenaGateChecks`: every scenario that reads a
report now also checks `patchersAllowed` from the raw JSON (present, file names only, exactly the patchers the world's
policy allows, none of them neutralised), the real-install scenario the four installed patchers; five new mutants (not
collected, the neutralised listed instead, missing from the final report, missing from the inactive reports, not
published); the three existing mutants anchored on the changed lines were re-anchored.

### 3. The helper mutex was one per Windows session, the files one per user (P2, rare)

*Finding.* `ArenaSessionContract.HelperMutexName` was `Local\maschine-ArenaMode.Session`; `Local\` kernel objects are
scoped to the Windows session (the Terminal Services session a logon lands in), while the runtime root, `server.pid`,
`watch.json` and the tree live in the user's local application data. Two concurrent sessions of the same account (a
console and a Remote Desktop session on a server) each had a mutex of their own and could build, stop or clean the same
tree at the same time. Not a defect of two helpers inside one session.

*Agreed.* The review suggested an exclusive lock file or an appropriately secured, user-bound global mutex; the mutex
keeps the existing wait and abandoned-mutex semantics and the tests' structure, so that is what changed.

*Changed.* `HelperMutex` (HelperHost.cs): the name is `Global\maschine-ArenaMode.Session.<SID of the user>`
(`ArenaSessionContract.HelperMutexNamePrefix` + `WindowsIdentity.GetCurrent().User`), one object for every session of
that user, machine-wide; the SID keeps the accounts of one machine apart in the shared namespace. The shipping helper
creates it with an access list of exactly that user and SYSTEM (FullControl). The default list of a `Global\` object
comes from the creating token: measured on this machine for an elevated administrator it names SYSTEM, the
Administrators and the creating logon session, no user SID, so whether another session of the same user could open such
an object would depend on that token; the explicit list makes it independent of elevation and of the session (the
first draft's comments stated the measured list as a universal property; this pass's review corrected the wording).
Once the helper holds the mutex no other account can acquire it; an account that creates the name first only denies
service: the open fails, a `HelperException` is logged, nothing is touched. A helper that cannot determine the user's
SID ends before it does anything (`Program.Main`). The unit tests run the same code on net10.0, where
`System.Threading.AccessControl` is not part of the runtime, and create the mutex without the list (`#if NETFRAMEWORK`);
the shipping build's list is read by the net472 harness. Spec section 6 (the paragraph after the helper's description,
and the shared-root paragraph), section 9 (the harness).

*Tests.* `ArenaSessionHelperTests`: the name for the current user and for a given SID, and a second `Open` of the same
name from another thread finds it taken; the contract pin holds the `Global\` prefix; the source pins of `Program` and
`HelperHost` follow `HelperMutex.Open(_mutexName)` (the host opens the mutex exactly twice, through `HelperMutex`, and
`Program` names it through `HelperMutex.Name()`). `tools/ArenaSessionHelperNet472Checks` (Windows PowerShell 5.1 on the
built exe): the name is `Global\maschine-ArenaMode.Session.<SID>`, and the access list of a mutex the build creates under
a fresh name is exactly the user and SYSTEM (a fresh name, because an object of the real name that already exists, a
helper or a watch of this user, would be opened as it is; this pass's review found the first draft opening the real
name); three new mutants (a `Local\` prefix, the user rule dropped, the default list) fail their named checks; the mutant
table can now name the file it edits. Not proved offline: two real sessions of one account; the cross-session property
follows from the `Global\` namespace and the explicit list.

## What the review listed as bounded, not corrected

- **Late patcher registration** (`AssemblyPatcher.AddPatcher` from an allowed patcher's `Initialize`): the gate judges
  one snapshot; a patcher registered afterwards is never evaluated and is not in `patchersAllowed` either. No installed
  mod uses that API (the review's own timing counter-review); documented in `policy.md` as a hardening gap, not a
  current conflict. Unchanged.
- **Mono generic sharing of `FindPluginTypes<T>`**: already documented in `ARENA-GATE-20261021.md`. Unchanged.
- **Config-argument prevalidation** of the helper: hardening information, no supported argument shape that fails.
  Unchanged.
- The accepted trade-offs of the previous answer (shared client files, junctions, one session folder per user, the
  disposable profile copy) stand.

## The review of this pass

An adversarial review of the change itself (six lenses: backup, gate, client, mutex, evidence, docs; three skeptics per
finding; 75 agents) confirmed 21 findings, grouped: the missing `directory` member and the loader's file rule (finding 1
above), the six older client-harness mutants and the swallowed swap span (finding 2), the real-name access-list check
and the universal wording of the default list (finding 3), and stale numbers, check numbers and refusal lists in the
docs and comments (`ARENA-GATE-20261021.md`, the answer of 2026-10-22, section 11 of the spec, `AGENTS.md`, the READMEs,
`SessionPolicyChecks.cs`). All of them are corrected in this pass; two findings were refuted (late `AddPatcher`
registration, already bounded above; the foreign-DACL branch having no check, a shipping-only path with no way to create
a foreign object in the harness).

## Evidence and limits

- Build without deploy (`NoDeploy.targets`, `SPTUserMods` sentinel); the xunit families touched by the change
  (`ArenaGateReportTests`, `ArenaSessionHelperTests`, `ArenaWhitelistTests`, `ArenaSessionHelperProcessTests`: 909
  passed); the whole suite once at the end of the pass through `tools/Run-ArenaVerification.ps1` together with the
  three harnesses' complete mutant sets (section 11 of the spec carries the numbers of that run).
- Negative control of the first draft: with three planted faults (the `IsLogPathUnderUser` test removed from
  `TryConfineBackupDirectory`, `patchersAllowed` written as an empty array, a `Local\` prefix) exactly the eleven new
  xunit cases fail, the sources restored byte for byte afterwards.
- `tools/ArenaSessionClientChecks`: baseline 698 assertions (679 before); `tools/ArenaGateChecks`: 17 scenarios, 705
  checks (617 before); `tools/ArenaSessionHelperNet472Checks`: 9 checks, 6 of 6 mutants rejected.
- SPT-GPU-2, the same day (spec section 10, fifth run, build `015ff7b`): an enter through the `Global\` mutex (no
  Warning, the tree's `backup.json` byte-identical to the installed one), a user-file deny of
  `WTT-ContentBackportPatcher.dll` arriving in the running session and refusing at the next side screen with the new
  text, its removal admitting again, RETURN TO NORMAL SESSION with the watch waiting for the leave's mutex and logging
  `NothingToStop` 9 ms after `leave finished`; the normal session afterwards with 16 of 16 files identical and no second
  server.
- Not proved: SPT's backup service running against a confined copy on a real installation (the installed file carries
  the shipped value; the native call path is read, the copy is rewritten and checked offline); two Windows sessions
  of one account (see finding 3).
