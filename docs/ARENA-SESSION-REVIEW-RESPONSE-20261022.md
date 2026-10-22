# Arena session: answer to the external review (2026-10-22)

Status: written on branch `feature/arena-session-review-fixes`, 2026-10-22, committed there (`baf396e`; the policy stamp
the GPU run exposed in `380f696`, its review fixes after it) and merged into master the same day. Checked by the unit
tests and the harnesses under `tools/`; SPT-GPU-2 ran enter, leave, the helper handshake and the mid-session policy
change (spec section 10, third run); items 10 and 11 of that section keep offline checks only. The binding
specification is [ARENA-SESSION-20261021.md](ARENA-SESSION-20261021.md); every change below is also recorded in its
section 11.

## What was reviewed

A second model (Codex) reviewed the alternative Arena session at `1713f5f` against the published `v0.1.3`: the report
is `artifacts/session-review-20261022/root/report.md` (German) with three sub-reports (`gate-policy`, `compat`,
`isolation`); the files are in the main checkout's ignored `artifacts` folder
(`D:\SPT41\Development\ArenaMode\artifacts\session-review-20261022\`), not in this branch. It asked for an assessment, made no change and ran no game or server. Its verdict on the approach:
keep it (a deliberately limited mod selection before the plugins start and a separate server with a profile copy
need fewer foreign-mod patches and protect the normal server's files better than a profile reset alone), but it is
no complete sandbox and no compatibility statement for the mods that are allowed. Its remark that the twelve removed
adapters concern no mod of the default list means their removal proves no regression in the intended default
operation; that stands, and the allowed-mod integrations (PFT, SVM, Recoil Legacy, DynamicMaps) stay.

The review also said that the default list and the adapter cleanup were "only checked offline" according to
`docs/HANDOFF.md`. That was true when it was written. The GPU run of the same day is recorded now (HANDOFF and
section 10 of the spec); it ran the build *before* the changes described here: HEAD `1713f5f`. The run's report names no
hash; the commit follows from the policy-override note the run shows, which first exists in that commit, with nothing
after it. No file with the run's observations was found (a search of the main checkout for the probe's name in text
files finds nothing), so the table in section 10 is the only record.

## The five findings to correct before a release

### 1. The server mods that are loaded after the restart were not checked

*Finding.* `ArenaWhitelistAdmission` computed the exclusions of the server's mods in an Arena session but used only
the warnings. The same inventory with a denied mod was refused in a normal session and admitted, without even a
warning, in an Arena session. A policy file changed or synchronised during the restart could differ from the
request the server tree was built from.

*Agreed.* The hash of the policy was written into the marker and the gate report and never compared.

*Changed.*

- Server: the Arena instance's ping carries `policyHash`, the string from the tree's marker (read once at startup, JSON
  `null` when the marker has none, and one startup Warning then). A normal server's ping is unchanged (no such member).
  `ArenaServerInstance`, `ArenaModeRouter.BuildPing`.
- Client: `ArenaServerClient.PolicyHash` reads it exactly as written (absent, null, blank or not a string is unknown).
  In an Arena session `ArenaWhitelistAdmission` now refuses Arena when the ping's `serverMods` contain a unit the
  policy as read now excludes (any exclusion reason, `requires-denied` included: `The Arena session server runs
  <names>, which the current policy excludes: the policy changed during the switch. ...`) and when the ping's hash and
  the gate report's hash are both known and differ (`... was built for a different policy than this game applies ...`).
  Each refusal is logged once at Warning (the hash one with both hashes) and leaves the RETURN TO NORMAL SESSION action.

*Chosen.* Both sides now state what they apply (server: the marker hash through the ping; game: the gate's report) and the
client compares; an unknown hash never counts as a difference, so an older server or a marker without a hash does not
lock a player out. The comparison is exact (no case folding).

*Left.* The client's check reads the policy files itself, so a user file that is locked at that moment (Syncthing)
makes the client's policy differ from the gate's and can produce a false "policy changed" refusal. It fails closed and
the way out (return to the normal session, start again) is offered. Nothing here proves a real raid is affected today; the
review said the same. A user file above 256 KB was a second, permanent way to the same refusal (the client ignored it,
the gate applied it); closed in the second pass: both read the files through one function.

### 2. Preloader patchers were not part of the admission decision

*Finding.* The direct start in a normal session inventoried only `Chainloader.PluginInfos`; patchers are a separate
set in BepInEx. And a not-allowed patcher can have run its initializer before the gate (it sorts earlier), which the
gate only reported as a free-text warning, so the session counted as fully filtered.

*Agreed.*

*Changed.*

- Gate: the report has `patchersRanBeforeGate` (file names, a subset of `patchersNeutralised`, always present, kept by the
  provisional and every inactive report) and the Info summary gets ` (N patcher(s) ran before the gate)`.
- Client, Arena session: a non-empty `patchersRanBeforeGate` refuses Arena (`The preloader patcher <files> ran before the
  Arena gate could stop it. Allow it in the policy file or remove it, then start the Arena session again.`).
- Client, normal session: every `*.dll` below `BepInEx\patchers` (any depth, by file name, once per name) is judged with
  `IsAllowedPatcher`/`ExclusionReason`, exactly as the gate does. An excluded patcher refuses a direct start, is named under
  "Blocked for Arena" or "Not on the whitelist", and the START ARENA SESSION action stays available (the session
  neutralises it).

*Left (closed in the second pass).* A patcher whose origin assembly or file name the gate cannot determine was still only
warned about. When the gate cannot find its own position in the patcher list the new list stayed empty and the existing
"position unknown" warning was the only trace. A library DLL in the patchers folder counted as a patcher in the client (the
gate leaves it alone); the installed set (five files) has no such library.

### 3. The hand-over to the restart helper was not confirmed

*Finding.* The game quit after `Process.Start` and one immediate `HasExited`; the helper could still fail (unusable request,
busy mutex) and the player would be at the desktop with the reason only in `helper.log`.

*Agreed.* Source-path finding (no live reproduction), plausible from the code.

*Changed.*

- Helper: after it has read and validated the request and taken its mutex, and before it waits for the game, it writes
  `<request>.accepted` (`{"schema":1,"helperPid":N,"acceptedUtc":...}`) atomically (temporary file, then move). It deletes
  a stale file first, before it reads the request; if it cannot write the acknowledgement it ends without touching the
  game. The order lives in `HelperHost`, split out of `Program.Main` so that it is tested against the real classes.
- Client: `ArenaSessionSwitch.Request` deletes a stale acknowledgement, writes the request, starts the helper and returns;
  a coroutine on the plugin looks every 250 ms for at most 15 s. The game quits only when the file parses, names the
  pid of the helper that was started and that helper is still running. If the helper exits first or the time runs out
  the game keeps running, one Warning is logged and the screen shows the reason with the action again. The notice
  while waiting offers no second click. The two-click flow is unchanged.

*Left (closed in the second pass).* On a timeout the client deletes the request file, so a helper that is merely slow and
has not read it yet (a busy mutex, a slow start) fails to read it and ends. A helper that had already taken the request over
could not be stopped by the client: it kept waiting for the game for up to 120 s, and if the player closed the game by hand in
that window the switch still happened. Now the helper watches the request file during that wait.

### 4. Generic server prepatches of allowed mods were not carried over

*Finding.* The private tree took `user/mods` but not `user/patchers/<GUID>`, which SPT 4.1.6 expects for `HasPrepatcher`
mods; an allowed mod with a server prepatcher (Skills Extended was the example) could not load.

*Agreed.* Reproduced by the reviewer in an isolated copy.

*Changed.* The helper copies `<runtime>\user\patchers\<guid>` into the tree for every server mod of the request whose folder
exists, through the sandbox, and nothing else from `user\patchers` (SPT applies every folder it finds, so a policy-excluded
mod's prepatch must not be there). A GUID listed twice is copied once; a GUID that is not a plain folder name is not looked up.
The "server tree ready" line of `helper.log` gives count and names.

*Left.* Nothing in the default list needs it today; it matters for user-allowed mods.

### 5. A started server that could not be registered stayed unknown

*Finding.* `server.pid` was written after the process started; if that write failed, no later cleanup knew the running
server.

*Agreed.*

*Changed.* `server.pid` is the very next step after `Process.Start` (the sandbox that writes it is built before the start). If the
write fails after its retries the helper ends the process it just started with the handle at hand, removes a partial pid
file and fails the enter with a clear Error line; the existing recovery then restores the normal session. The normal server
that `leave` and the recovery start writes no pid file (it is the user's own).

*Left (closed in the second pass).* A helper killed in the instant between the start and the write left an unregistered
server that `stop-server` could not find, and a marker without a pid was not treated as an unresolved state before a rebuild
(the review's second half). Both were documented limits (spec sections 6 and 11).

## The deliberate trade-offs

| Review remark | Decision |
|---|---|
| Deny is not final for hard dependencies: the decision pulled a denied dependency back in and warned. | **Changed.** With the code-level refusals of ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special Forces and Aishi gone, the default deny list is the only guard, and one allowed mod that lists such a mod as a dependency defeated it. A denied unit is never allowed now, not even as a dependency. A unit that needs one, directly or through other units, is excluded with `requires-denied:<guid>` and one warning, on the client and for server mods through `ModDependencies`. Platform and Arena units are the only exception. The normal-session refusal lists such dependants under "Blocked for Arena". A user who wants such a mod has to allow the library. Unlisted libraries are still pulled in. |
| A normal session whose mods are all allowed plays Arena directly, without the private server copy; a user-allowed statistics mod then writes real server data. | **Kept.** User decision (2026-10-21): no restart when nothing needs filtering. It is stated in the README now: the direct start uses the normal server, and only Arena's own profile protection applies; the disposable server is one click away. An always-private session is a trade against the restart cost and was not chosen. |
| Client data stays shared (same game executable and folder; a fully allowed Skills Extended keeps writing its ledger under `BepInEx/config`). | **Inherent, documented.** The session starts the same game from the same folder; a server copy cannot undo a client-side write. The default list has no such mod. |
| Shared server data and bundles are junctions, so writes pass through. | **Inherent, documented.** Junctions avoid copying gigabytes at every enter; the profile, mod-local configuration and sidecars are real copies. The spec (section 6) says which is which. |
| One session folder per Windows user makes parallel installations harder. | **Known.** The helper never stops or replaces another installation's server (runtime check), and the mixed-helper limit of the 0.1.3 builds is documented; every installation of one user has to receive the new helper together. |
| Restart, a second server and the failure handling cost time and resources. | **Accepted** (user, 2026-10-21). The GPU run of 2026-10-22 measured the arena server from freshly copied binaries at 35 s on its first start and 6 s on the second; the run of 2026-10-21 restarted into the session in 38 s. |

## Second pass: the fixes reviewed again

The fixes above were reviewed once more (semantics, documentation, tests) and every finding was checked against the code
before anything was changed. Eleven findings; all hold in substance, two details of the reviewer's reasoning did not and are
noted in 3 and 9. The contract is in the spec (section 11, last entry).

| # | Finding | Decision and change |
|---|---|---|
| 1 | The policy hash ignored a user deny in whitelist mode, although deny wins over hard dependencies made such a deny decisive: the default list names M, M needs the unlisted library L, a user deny of L removes both, and the hash was the same with and without the deny. | **Confirmed, fixed.** The user's denied names are hashed in a whitelist too (one canonical line per name; denies of platform and Arena units are skipped and not hashed). A policy without a user deny keeps its hash. The comment in `ComputeHash`, spec sections 3 and 11 and the tests say so; two new pinned values, cross-checked with an independent FNV-1a. |
| 2 | A helper that acknowledged late stayed armed and switched the session when the player closed the game by hand; the `StartCoroutine` failure path did not even withdraw the request. | **Confirmed, fixed.** The client deletes the request file on every failure path (before: only after a timeout). The helper stamps the file (last-write time and length) before it reads it and looks at the stamp in every round of its wait for the game, and once more after the game is gone: gone or replaced ends the run with one Warning, nothing changed, game untouched. A rewrite with the same text counts as replaced, so a new attempt ends the old helper within half a second. |
| 3 | A patcher whose origin or file name the gate cannot determine stayed active and out of the report; with an unknown gate position `patchersRanBeforeGate` stayed empty. | **Confirmed, fixed (fail closed).** Such a patcher is neutralised as `(unidentified) <type name>` with its own warning and listed in `patchersRanBeforeGate` when it has an initializer and sorts before the gate; with an unknown gate position every neutralised patcher that has an initializer is listed. Reach stays very low (BepInEx loads patchers from files). One premise of the finding was checked and did not hold: a patcher made of dynamic methods is not "no assembly", on the game's Mono its delegates report an assembly of the runtime, so the gate already switched it off, under the name `mscorlib.dll`; the unplaceable ones are delegates of an assembly without a location and a patcher without delegates. Harness: the scenarios `unidentified-patcher` and `gate-unlocated`. |
| 4 | A helper killed between `Process.Start` and the `server.pid` write leaves a server nobody knows; the next enter fails or half-builds. | **Confirmed, fixed.** When no registered server was stopped, a running process whose image is the tree's own `SPT.Server.exe` is stopped under the ownership rule of the registered server: the marker must be there, written by this helper, and name the caller's installation. Without a marker the process is left alone, `enter` refuses and `helper.log` names the pid; the server of another installation is left alone. The installation's own server is another file and is never touched. |
| 5 | A support library in `BepInEx\patchers` counted as a patcher in the normal session and blocked every direct start. | **Confirmed, fixed, differently from the suggestion.** Not by asking BepInEx for its patcher list (the client runs long after the preloader phase), but by what BepInEx requires of a patcher: a public static `TargetDLLs` member, so the name is in the metadata of the DLL. A DLL whose bytes do not contain it is no unit; a file that cannot be read counts as a patcher; an allowed name is not read. All five installed patchers contain the name. |
| 6 | `docs/DEPENDENCIES.md` and the "third pass" row of the deploy document still said a denied mod that an allowed mod needs is pulled in with a warning. | **Confirmed, fixed.** Both rewritten; the deploy document states that the row is superseded for denied libraries. |
| 7 | HANDOFF's open list omitted the two gate limits. | **Confirmed.** Both are closed by 3; the list now names the limits that remain. |
| 8 | HANDOFF's GPU paragraph referred to itself, named no build, and cited a file that is not in the branch. | **Confirmed in part.** (a) The circular wording is gone. (b) The build is `1713f5f`, by the run's own observation (the policy-override note); it is written down in spec section 10, HANDOFF and above, together with the fact that the report names no hash. (c) The report of the run is not kept as a file anywhere that was found, and the review's files are in the main checkout's ignored `artifacts` folder; both are said where the paths are cited. |
| 9 | No test pinned which denied dependency a cut-off unit names; a "last wins" mutant survived all tests. | **Confirmed, fixed.** The reviewer's own suggestion (two different denied leaves) would not have killed that mutant: across different GUIDs the loop stops at the first denied one anyway, and the order only varies for a GUID installed twice. Both are tested now (the dependency order across GUIDs, in both orders; the first installed copy of a duplicated GUID, in both installed orders), and the tie-break is written down in spec section 4. |
| 10 | Nothing pinned the removal of a stale `server.pid` after a failed registration, or the "could NOT be ended" outcome. | **Confirmed, fixed.** A seam for the wait on a killed server (`HelperRunner.ServerExitWaiter`), a read-only stale pid file, two tests. |
| 11 | The client capped policy files at 256 KB and the gate did not: the client ignored a larger user file and the gate applied it, so the new hash check refused on every attempt. | **Confirmed, fixed.** One reader, `ArenaPolicyFiles.TryRead`, for both (limit, decoding, sharing mode; one byte more than the limit is read so that growth cannot get past it). Unit tests, a source pin that keeps a private reader from coming back, the gate harness world `user-oversize`. |

## Third pass: the sub-reports' remaining findings, and the review of that pass

The third pass answered what the two sub-reports `gate-policy` (appendix) and `isolation` (finding 3 and its hardening notes)
still held after the second pass. Three changes, each with its entry in spec section 11:

- **Client (spec sections 2, 3, 7, 10).** An Arena session whose policy cannot be loaded refuses (`Return to the normal
  session.`): before, it judged the server's mods only if the policy had loaded and otherwise admitted on the gate's word. The
  plugins the game loaded are judged with the policy as it reads now (check 4, `This game loaded <names>, which the current
  policy excludes: the policy changed during the switch.`); checks 2, 3 and 4 need no ping and come first. Section 7 no longer
  says that a missing default list makes the whitelist stand aside in a normal session (it never fails open).
- **Default file and policy files (spec sections 2, 3, 9).** A `mods` entry of the shipped default file that names a platform or
  Arena unit, or any `com.fika.` GUID, is skipped with a `default whitelist:` diagnostic; `Write-ArenaAssets.ps1` and
  `Verify-TestRelease.py` reject such a list at build and package time. JSON `null` for the user file's `allow`, `deny` and
  `unlisted` is a missing member.
- **Helper (spec section 6).** The wait for the arena server no longer takes a missing owner for the server: an empty table keeps
  waiting, other owners keep it waiting, an unreadable table is accepted only if the helper's own probe found the port free right
  before the start. The lookup is bound to the address the connection reached. Every file the helper creates or truncates is
  opened without following a link, and `WriteAllText` (so `server.pid`) removes a link at the destination first. An absolute or
  out-of-tree `filePath` of the copied `sptLogger.json` is moved to `./user/logs/<last segment>/`.

A review of these three changes (semantics, documentation, tests) found five minor points. Each was checked against the code
and, where it was about the file system or sockets, against the real thing before anything was changed.

| # | Finding | Decision and change |
|---|---|---|
| 1 | No test pinned the IPv6 table read for an IPv4 connection or any real wildcard listener (`0.0.0.0`, dual-stack `[::]`); removing the read left every helper test green. | **Confirmed in part, fixed in part.** The gap holds in a wider form: the only wildcard coverage was the pure `ListenerServes` table, so even a mutant that no longer used it for the rows of the system table survived. Two tests now bind a real `0.0.0.0` listener and a real dual-stack `[::]` listener and look the owner up for the address a `CanConnect` probe reached. The premise did not hold: the finding expected a healthy server on `[::]` to be listed in the IPv6 table only, so that dropping the read would fail the wait. On Windows Server 2025 the IPv4 table lists a dual-stack socket as `0.0.0.0` as well (`netstat -ano -p tcp` shows it; a V6-only socket appears only in the IPv6 table), so the removed read is invisible with a dual-stack listener and only stops counting a V6-only `[::]` listener, the false accept the spec names. The read is kept (an older system may list the socket once) and has no mutant; the spec says so. |
| 2 | `OpenFileForWriting` was documented to refuse a link, a junction and a folder with a `HelperException`; only a file link does. | **Confirmed, fixed (documentation and dead code, not behaviour).** A junction and a folder fail in `CreateFile` with "access denied" (no backup semantics), so they surface as an `UnauthorizedAccessException`; nothing is written in any case. The folder branch of the attribute check could not be reached and was removed; the XML summary and spec section 6 say what happens; the test pins each exception type. Chosen: not to open with `FILE_FLAG_BACKUP_SEMANTICS`, which exists for opening folders and would let a write open hold a folder handle at all. |
| 3 | On the shipping runtime (net472) `new TcpClient()` is IPv4-only, so `CanConnect` cannot reach an IPv6 literal such as `::1`; the unit tests pass only because net10.0 sockets are dual-stack. | **Confirmed, fixed.** Checked in Windows PowerShell 5.1 against the exe built before the fix: the IPv4 and wildcard checks passed, the `::1` probe returned false. An IP literal is now probed with a socket of its own address family, a host name as before (not a dual-mode socket, which needs IPv6 to be enabled on the machine even for an IPv4 host). The user's installed `http.json` has `127.0.0.1`, so nothing is known to be affected. New `tools/ArenaSessionHelperNet472Checks`: seven checks on .NET Framework, three mutants rejected on their named check. |
| 4 | Spec section 6 said that a parent folder that is a link ends the `helper.log` write; it does so only if the log's own name is a link as well. | **Confirmed, fixed.** Reworded; a plain log in a folder that is a link is written through (only the log's own name is judged). A new test pins the write-through; the old test, which sets up both conditions, got a name that says so. |
| 5 | HANDOFF and this document gave the second pass's check numbers as the latest and had no entry for the third pass. | **Confirmed, fixed.** This section and the HANDOFF entry. |

Offline results after the third pass and its review (no-deploy properties throughout, run through PowerShell, nothing under
`D:\SPT41` written; the measured numbers of the review were 2185 / 667 / 156): `dotnet build ArenaMode.sln` 0 errors; `dotnet test
ArenaMode.Tests` 2188 passed, 2 skipped (both need the installed game), 0 failed; `ArenaSessionClientChecks/Verify.ps1` 667
assertions and 156 mutants rejected; `ArenaGateChecks/Verify.ps1` 17 scenarios, 617 checks, 35 mutants rejected;
`ArenaSessionHelperNet472Checks/Verify.ps1` 7 checks, 3 of 3 mutants rejected. The new unit tests were also run against three
hand-made mutants of the production source (the wildcard filter by equality, the open with backup semantics, a log that refuses a
linked folder); each failed exactly the tests named for it. `Verify-SessionPing.ps1`, `OptionalModAdmissionChecks` and
`ArenaAvailabilityChecks` were not rerun in this pass (their sources and the sources they compile are untouched by it).

## Evidence and limits

- **Offline, for the changes above.** Unit tests (`ArenaMode.Tests`) cover the policy, the report, the server ping, the helper
  and its acknowledgement; the harnesses `tools/ArenaGateChecks`, `tools/ArenaSessionClientChecks` and
  `tools/NativeMenuChecks/Verify-SessionPing.ps1` run production sources against stand-ins and reject their mutants on the
  named assertion (counts in `docs/HANDOFF.md`). They cannot prove the Unity screens or a real helper hand-over. After the
  second pass: build 0 errors, 2074 unit tests passed (2 skipped, they need the installed game), client harness 634
  assertions and 141 mutants, gate harness 17 scenarios, 617 checks and 35 mutants. The unit tests of the second pass were
  also run against 17 hand-made mutants of the production code, each killed by the test named for it (hash of a user deny in a
  whitelist, both tie-break variants, the reader's size limit and sharing mode, the request watch in four places, the stray scan
  in six, the stale pid file, the "could NOT be ended" message).
- **On a GPU host (SPT-GPU-2, 2026-10-22, the build before these changes).** A normal session with every installed mod
  on the default list started an Arena deathmatch directly (no refusal, no warning). A throwaway probe plugin that is not on the
  list refused Arena with START ARENA SESSION and the session excluded it (40 allowed, 1 excluded). With the one-line override
  the normal session allowed Arena with the note `policy override: 1 unlisted mod(s) allowed by your policy file:
  ArenaWhitelistProbe`, and the session loaded the probe (41 allowed, 0 excluded, with the gate suffix). The arena server from
  freshly copied binaries needed 35 s on its first start and 6 s on the second. Recorded from the run's report.
- **Still to run on a GPU host** (spec section 10, items 6 to 8, plus the review's list): the build with these changes, normal to
  Arena to normal with profile and client data checked, respawns, PFT, a policy file changed during the switch, a helper made to
  fail during the hand-over, and a throwaway patcher in `BepInEx\patchers`.

## After the review: what the GPU run added

The run of the fixes on SPT-GPU-2 (spec section 10, third run) found one thing the review did not: the admission
outcome was cached per ping generation and plugin count, so a policy file that changed while the game ran stayed
invisible until the next ping, and in an Arena session the refusals for a changed policy never fired for a file that
arrived after the first screen. The side screen and the START ARENA SESSION click now compare the files' length and
last-write time with the cached outcome's stamps and evaluate again when they differ (spec section 7 and section 11,
last entry). A change that only widens the policy is noticed the same way and refuses nothing; the session keeps
running without the mods it did not load, and nothing tells the player so: a known gap, not a safety one.
