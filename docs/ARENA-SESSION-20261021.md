# Arena session: whitelist by not loading (spec, 2026-10-21)

Status: implementation spec for branch `feature/arena-whitelist-session`. This file is the
contract between the five parts below. If an implementation detail has to deviate, change this
file in the same commit and say why.

## 1. Goal and user decisions

Many third-party mods break Arena raids. Per-mod adapters are no longer an option. Arena now
runs only with **whitelisted** mods:

- Mods that are not on the whitelist are **not loaded at all** in an *Arena session*: a separate
  EFT process (plugins filtered by a preloader patcher) talking to a separate SPT server process
  (a private runtime tree with only whitelisted server mods).
- Switching between a normal session and an Arena session is a controlled restart of the game
  (about 1.5 to 2 minutes). Accepted by the user (2026-10-21).
- The Arena session uses a **disposable copy** of the profile and offers **only Arena raids**.
  Accepted by the user.
- Default whitelist besides ArenaMode: **SAIN, APBS, ABPS, BigBrain, Waypoints**.
- A whitelisted mod whose installed version differs from the reviewed version only produces a
  **warning**, never a block.
- The user may turn the whitelist into a blacklist (`"unlisted": "allow"` in the user file, decision
  2026-10-21, section 3): everything the user did not deny is then allowed. The shipped default is
  always a whitelist.
- A normal session whose loaded mods are already all whitelisted may play Arena directly, without
  a restart (it is equivalent to an Arena session except for the disposable profile).
- No code specific to one foreign mod. Existing adapters under `ArenaMode.Client/Compat` stay as
  they are; in an Arena session they are dormant because their mods are absent.
- Fika is never filtered. With Fika present Arena stays unavailable as today and no session is
  offered.

Non-goals for v1: pausing mods inside a running process, removing existing adapters, Arena with
Fika, checking enum consistency of denied preloader patchers beyond neutralising them.

## 2. Units and the default list

A unit is identified by its GUID: the BepInEx plugin GUID on the client and the SPT `ModGuid` on
the server. All units below use the same GUID on both sides.

| Tier | GUID | Client | Server | Reviewed version |
|---|---|---|---|---|
| platform (code) | `com.SPT.core`, `com.SPT.custom`, `com.SPT.debugging`, `com.SPT.singleplayer` | yes | - | 4.1.6 |
| platform (code) | `com.bepis.bepinex.configurationmanager` | yes | - | any |
| platform (code) | every GUID starting with `com.fika.` | yes | - | never filtered |
| arena (code) | `com.maschine.ArenaMode` | yes | yes | this build |
| mod (file) | `me.sol.sain` SAIN | yes | yes | 4.5.1 |
| mod (file) | `com.acidphantasm.progressivebotsystem` APBS | - | yes | 2.4.1 |
| mod (file) | `com.acidphantasm.botplacementsystem` ABPS | yes | yes | 2.1.0 |
| mod (file) | `xyz.drakia.bigbrain` BigBrain | yes | - | 1.5.0 |
| mod (file) | `xyz.drakia.waypoints` Waypoints | yes | - | 1.9.0 |
| tool (file) | `com.maschine.UiBridge`, `com.maschine.ModProfiler`, `com.maschine.LogTools` | yes | - | any |

Preloader patchers are matched by file name. Platform patchers (code):
`FixPluginTypesSerialization.dll`, `spt-prepatch.dll`, `maschine-ArenaMode.Gate.dll`.
Tool patcher (file): `maschine-LogTools.dll`. Every other patcher is neutralised in an Arena session.

Libraries without a `BepInPlugin` (for example `maschine-Redeploy.Core.dll`,
`maschine-ArenaMode.BigBrainCompat.dll`, `spt-common.dll`) are not plugins and are never filtered;
they load only when an allowed plugin uses them.

Tools are listed because the GPU test hosts need them; the user can deny them in the user file.

## 3. Policy files

- Shipped default: `BepInEx/plugins/maschine-ArenaMode/arena-whitelist.json` (overwritten by updates).
  Source in the repo: `assets/arena-whitelist.json`.
- User file (optional, never shipped): `BepInEx/config/com.maschine.ArenaMode.whitelist.json`.

Default file:

```json
{
  "schema": 1,
  "mods": [
    { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "clientVersion": "4.5.1", "serverVersion": "4.5.1" },
    { "patcher": "maschine-LogTools.dll", "name": "LogTools patcher", "kind": "tool" }
  ]
}
```

Entry fields: `guid` or `patcher` (exactly one), `name`, `kind` (`mod` or `tool`), optional
`clientVersion` / `serverVersion` (reviewed versions; absent = any version without warning).

User file: `{ "schema": 1, "unlisted": "deny" | "allow", "allow": [ <entries as above> ], "deny": [ "<guid or patcher file>" ] }`.
`unlisted`, `allow` and `deny` are each optional; schema stays 1.

The policy is a combined allow/deny list (user decision 2026-10-21). For one unit, in this order:

1. **Platform and arena units** are always allowed. `deny` cannot remove them.
2. A unit in the user `deny` list is **excluded** (`denied-by-user`), except as a hard dependency of
   an allowed unit: the closure of section 4 still pulls it in, with a warning.
3. A unit that a default entry or a user `allow` entry names is **allowed** (`whitelist` or `tool`). A
   reviewed version that differs from the installed one is a warning; this is the only source of
   version warnings, so it exists only for entries that carry a reviewed version.
4. Every other unit follows `unlisted`. `"deny"` (the default) **excludes** it (`not-on-whitelist`):
   the whitelist behaviour. `"allow"` **allows** it (`unlisted-allowed`, no warning of its own): the
   blacklist behaviour, everything the user did not deny is allowed.

The same rules apply to client plugins and server mods (by GUID) and to preloader patchers (by file
name). Because `deny` is checked before `unlisted`, a blacklist is just `"unlisted": "allow"` plus a
`deny` list; `allow` entries are then only needed to attach reviewed versions.

A user file with an unknown schema, an unknown `unlisted` value (anything but the strings `deny` and
`allow`, compared ignoring case and surrounding white space; the empty string, numbers, booleans,
arrays and objects included; JSON `null` counts as absent) or a parse error is ignored with a
warning, as a whole: guessing a mode would silently invert what the user wrote. In blacklist mode
(`"unlisted": "allow"`) the same goes for a `deny` member that cannot be read in full, that is, one that is
not an array or has an entry that is not a non-blank string: there the deny list is the whole filter, and
applying part of it would load a mod the user excluded, so the file is ignored as a whole and the shipped
whitelist stays in force. A mistyped `allow` member or entry, and under a whitelist a mistyped `deny` member
or entry, are skipped individually with a warning (a lost `allow` entry only leaves a mod unlisted, and under
a whitelist nothing unlisted loads). A `unlisted` member in
the shipped default file is not read: the default list is always a whitelist. A missing or unreadable
default file makes the Arena session unavailable (the gate stays inactive and reports the error).

The policy hash covers the entries after `deny`. In blacklist mode it also covers the mode and the
denied names, because there they are the whole filter; a whitelist policy hashes exactly as before
the mode existed.

GUID comparison is ordinal and case-insensitive. Versions compare as `System.Version` after
dropping a leading `v` and any suffix after `-` or `+`; an unparsable version counts as different.

## 4. Shared logic (`ArenaMode.Shared/Session/`, linked source)

Linked into the gate (netstandard2.1, preloader), the helper (net472), the client
(netstandard2.1), the server (net10.0) and the tests (net10.0). Therefore: no NuGet dependency, no
Newtonsoft, no System.Text.Json, no Unity, no BepInEx types, C# features that compile for all four
targets.

- `ArenaMiniJson`: minimal JSON reader (object, array, string with escapes, number, bool, null)
  returning plain `Dictionary<string, object>` / `List<object>` / `string` / `double` / `bool` /
  `null`, plus a small writer for the report. Rejects trailing garbage. Never throws out of
  `TryParse`.
- `ArenaWhitelistPolicy`: `TryLoad(string defaultJson, string userJsonOrNull, out policy, out
  List<string> diagnostics)`; queries `IsAllowedPlugin(guid)`, `IsAllowedPatcher(fileName)`,
  `IsAllowedServerMod(guid)` (a named entry, or blacklist mode and not denied), `ReviewedVersion(guid,
  side)`, `UnlistedMode` / `AllowsUnlisted`, `IsUnlistedAllowed(id)`, `Hash` (stable hash of the
  effective policy for logs and the marker).
- `ArenaWhitelistDecision`: input = installed units `(guid, name, version, file, hardDependencies)`
  for one side; output = `Allowed` (reason: `platform`, `arena`, `whitelist`, `tool`,
  `unlisted-allowed`, `dependency-of:<guid>`), `Excluded` (reason: `not-on-whitelist`,
  `denied-by-user`) and `Warnings` (`version <installed> differs from reviewed <reviewed>`, `pulled in
  as hard dependency of <name>`, with ` (overrides the user's deny)` appended for a denied unit).
  `unlisted-allowed` occurs only in blacklist mode and carries no warning. Closure: a hard dependency
  of an allowed unit (a listed one or an `unlisted-allowed` one) that is installed but not allowed (a
  denied unit; in whitelist mode any unlisted one) is allowed with a warning, transitively. Same
  function for the server side, whose hard dependencies are the mod's `ModDependencies`, sent in the ping
  (section 8): SPT's mod validator refuses to load EVERY server mod when one listed dependency is missing,
  so a denied or unlisted library of an allowed server mod has to stay in the tree. The closure is per
  side: a client plugin pulled in as a dependency does not pull in a server mod that merely shares its GUID,
  nor the other way round. A ping without `dependencies` (an older server) means none.
- `ArenaSessionContract`: constants and the report format.
  - command-line flag `-arena-session`
  - AppDomain data key `com.maschine.ArenaMode.Gate.Report`
  - runtime root `%LOCALAPPDATA%\maschine-ArenaMode\session`, server tree `<root>\server`,
    request file `<root>\request.json`, helper log `<root>\helper.log`
  - marker file name `arena-session.json` (in the server tree root = server working directory)
  - default arena port 6970 (fallback 6971 to 6979 if 6970 is taken by a foreign process)
  - helper file name `maschine-ArenaMode.Session.exe`
  - gate report JSON: `{ "schema":1, "active":bool, "error":string|null, "policyHash":string,
    "allowed":[{guid,name,version,file,reason}], "excluded":[...], "warnings":[string],
    "patchersNeutralised":[file] }`

## 5. Gate: preloader patcher (`ArenaMode.Gate`, `maschine-ArenaMode.Gate.dll`)

Deployed to `BepInEx/patchers/maschine-ArenaMode/`. Same patcher contract as
`D:\SPT41\Development\LogTools` (`TargetDLLs` empty, `Initialize`, `Patch` never called, `Finish`).
The work is split over `Initialize` and `Finish`, in the order BepInEx runs them; the measurements
behind the split are in [ARENA-GATE-20261021.md](ARENA-GATE-20261021.md).

- Both callbacks: if `Environment.GetCommandLineArgs()` has no `-arena-session` (case-insensitive),
  return on the first line: no Harmony, no logging, no file access.
- With the flag, `Initialize()`:
  1. Load the policy (paths from `BepInEx.Paths`). On failure: report `active:false` + error, do
     not filter anything, return.
  2. Neutralise every patcher that is not allowed, through the public members of
     `PatcherPlugin`: its target list is replaced by an empty one and its patch, initializer and
     finalizer delegates by no-ops. The gate itself and BepInEx's own entry-point patcher are
     recognised by the assembly their delegates come from and are never touched. Initializers
     that already ran (patchers that sort before the gate) cannot be undone; each gets a warning
     in the report. Never touch `FixPluginTypesSerialization` lists or any file on disk.
  3. Publish a provisional `active:false` report ("the plugin list has not been filtered yet"),
     so that a postfix that never runs cannot look like an active gate.
- With the flag, `Finish()`, which runs after the preloader has loaded its patched assemblies:
  4. Install a Harmony postfix on the closed generic
     `BepInEx.Bootstrap.TypeLoader.FindPluginTypes<BepInEx.PluginInfo>(...)`, resolved and closed
     by reflection. This cannot happen in `Initialize`: patching that method makes the runtime
     load `PluginInfo` and with it `UnityEngine.CoreModule`, and during `Initialize` the
     preloader has not yet patched and loaded that assembly, so the unpatched file would be
     loaded from disk, BepInEx's entry-point patch would land in a copy Unity never uses, the
     chainloader would never start and no plugin would load, ArenaMode included. For the same
     reason `Arm()` refuses to install when `UnityEngine.CoreModule` is not loaded yet (report
     `active:false` with that reason). The postfix takes `object __result` (never a typed
     BepInEx.Bootstrap signature) and reads every `PluginInfo` member by reflection, so the gate's
     own types stay free of the dependency. It acts only when the runtime type is the dictionary
     of plugin infos for the chainloader plugin directory, collects every `PluginInfo` of every
     DLL (GUID, name, version, hard dependencies), runs `ArenaWhitelistDecision` once and removes
     excluded `PluginInfo` items from their lists. The type cache file stays complete because the
     postfix runs after caching. `PluginInfo.Location` is not set yet at that point, so the
     report's `file` is the DLL path from the dictionary key (the path `Location` gets later).
  5. Publish the final report with `AppDomain.CurrentDomain.SetData(key, json)` and log through a
     BepInEx log source named `maschine-ArenaMode.Gate`: one Info line
     (`Arena session: N plugins allowed, M excluded, K warnings`), one Info line from
     `Initialize` that names the neutralised patchers when there are any (Debug would not reach
     the log: the preloader keeps only the levels of `Logging.Console.LogLevels`, default Fatal
     to Info), and one Debug line per excluded plugin.
- Any exception: report `active:false` + error, leave the plugin list unfiltered (all plugins
  load), never exit the game. ArenaMode then refuses Arena in that process. Patchers that were
  already neutralised stay off (the preloader has consumed their lists); the Error line and the
  report's `patchersNeutralised` say so.

## 6. Helper (`ArenaMode.Session`, `maschine-ArenaMode.Session.exe`)

.NET Framework 4.7.2 `WinExe` without NuGet packages (no console window), deployed to
`BepInEx/plugins/maschine-ArenaMode/`. `enter`, `leave` and `stop-server` run one at a time (named
mutex `Local\maschine-ArenaMode.Session`); `status` only reads and takes no mutex. Every run appends
to `helper.log` (rotated at 1 MB) and exits 0 on success, 1 on failure, never shows UI. The
manifest declares `longPathAware`: the server tree sits deeper than the install.

The runtime root, the mutex, `server.pid` and the tree are shared by every SPT installation of one
Windows user. A helper therefore never stops, replaces or deletes the arena server of another
installation (see `stop-server`). That protects only between helpers that know the rule. An older helper
(the 0.1.3 builds from before the server binaries moved into the tree) does not understand a marker with
`serverExe`: it takes the tree's server for "not ours", deletes `server.pid` and leaves the server running,
and the new helper can no longer find it through the pid file. The helper's version is the repository's
(the packager requires the built exe to match `Shared.props`, and the next release bump is the release's
decision), so until then only the marker's `serverExe` member and `status` tell the two builds apart. Every
installation of one Windows user therefore has to receive the new helper together; a server an older helper
left behind (`SPT.Server.exe` running from `<root>\server`) is ended by hand.

Commands: `enter --request <file>`, `leave --request <file>`, `stop-server`, `status`.

Request file (written by the client):

```json
{ "schema":1, "direction":"enter", "gamePid":1234,
  "gameExe":"C:\\SPT41\\EscapeFromTarkov.exe", "gameDir":"C:\\SPT41",
  "gameArgs":["-force-gfx-jobs","native","-token=<id>","-config={...}"],
  "profileId":"<id>", "sptRuntimeDir":"C:\\SPT41\\SPT_Runtime",
  "serverMods":[{"guid":"me.sol.sain","folder":"C:\\SPT41\\SPT_Runtime\\user\\mods\\Solarint-SAIN-ServerMod"}],
  "policyHash":"..." }
```

The helper refuses a request it cannot read (exit 1, nothing changed), so the client checks the
same rules before it quits the game (section 7). The rules, in `HelperRequest.TryParse`: schema 1;
`gamePid` positive; `gameExe`, `gameDir` and `sptRuntimeDir` absolute drive paths without `..`
(no UNC or device paths), `gameExe` an `.exe` strictly inside `gameDir`; `gameArgs` strings; for
`enter` a `profileId` of exactly 24 hexadecimal characters (optional for `leave`, the same rule
when given) and a non-empty `serverMods` whose folders are each a direct child of
`<sptRuntimeDir>\user\mods` with a plain, unique folder name.

`enter`:
1. Wait until `gamePid` has exited (max 120 s; on timeout log and exit 1, never kill the game).
2. Stop a previous arena server of this tree (see `stop-server`). If it belongs to another
   installation the enter fails (step 6 applies).
3. Materialise `<root>\server` (always under the runtime root; refuse any other path; never
   recurse into a reparse point when deleting; delete junctions as links):
   - Server binaries, so that the arena server runs from the tree and `AppContext.BaseDirectory`
     (the folder of the exe, which several server mods resolve their paths from) is the tree: every
     top-level **file** of `<sptRuntimeDir>` except `sptLogger.json` (generated below), `SPT.Launcher*`,
     anything with `Linux` in its name (any case) and `*.so`, copied to the tree root. For SPT 4.1.6 that
     is 44 files, 23 MB: `SPT.Server.exe`, `.dll`, `.runtimeconfig.json`, `.deps.json` and the flat
     libraries of the `deps.json`. Nothing else is needed beside the exe (checked against the server
     host: the working directory decides `IsRunFromInstallationFolder`, `sptLogger.json`, `SPT_Data` and
     `user\mods`; the base directory is used for the satellite assemblies in `SPT_Data\dotnet`, which is a
     junction below; the executing assembly's folder is used for `SPTarkov.Server.Core.dll`, which is
     copied). The copy is incremental: the binaries of the previous tree stay in place and a file is
     copied again only when its length or last-write time (UTC, to the tick) differs from the runtime's
     file, because a copy keeps the source's timestamp; a binary the runtime no longer has is removed.
     Locked files are retried like every other copy. Top-level **folders** of the runtime other than
     `SPT_Data` and `user` are not part of the tree; the helper logs a warning naming them (none exist in
     4.1.6), because a `deps.json` that pointed into one would not find it beside the copied exe. A
     runtime without `SPT.Server.exe` fails the build.
   - `sptLogger.json`: copy of `<sptRuntimeDir>\sptLogger.json` with every `filePattern` value
     prefixed by `arena-`.
   - `SPT_Data\`: a directory junction for every subdirectory of `<sptRuntimeDir>\SPT_Data`
     except `configs`; files directly in `SPT_Data` copied; `configs\` copied, then `http.json`
     patched: `port` and `backendPort` set to the arena port, everything else unchanged.
   - `user\mods\`: emptied, then a copy of every folder in `serverMods` (copies, not junctions,
     so what those mods write beside their own files stays in the disposable tree). A user-allowed
     server mod that resolves its paths from `AppContext.BaseDirectory`, the working directory or its
     assembly location therefore reads and writes the tree, not the real runtime. The exception is
     the `SPT_Data` folders that are junctions: they are the installation's own. Inside a mod, a
     folder that holds only `*.bundle` files (at any depth, at least one) is a junction too: SPT
     serves asset bundles read-only and nothing writes them, while a copy would duplicate
     gigabytes at every enter (WTT-ContentBackport ships about 8 GB of bundles).
   - `user\cache\bundleHashCache.json`: copied if it exists (its keys are relative paths and the
     linked bundles are the installation's files), so the server does not hash every bundle again.
   - `user\profiles\<profileId>.json`: fresh copy; `user\profileData\<profileId>\` copied if it
     exists; `user\certs\` and `user\credentials\` copied if they exist.
   - `arena-session.json` marker: `{schema, createdUtc, profileId, port, sourceRuntime, serverExe,
     policyHash, helperVersion}`. `serverExe` is the tree's `SPT.Server.exe`; a marker of an older
     helper has no such member (its server ran the installation's exe).
   Before anything is built, the tree root is cleaned: the marker first (a half-cleaned tree must not
   look finished), then everything except the plain files that are server binaries of the runtime being
   copied (folders and links, a stale binary, last session's leftovers).
4. Start `<root>\server\SPT.Server.exe` (the tree's own copy, never the installation's) with
   `WorkingDirectory = <root>\server` (the server resolves data and mods relative to the working
   directory and its DLLs relative to the exe), its own console window, an environment without
   `DOORSTOP_*` variables; write `server.pid`.
   Wait until `127.0.0.1:<port>` accepts TCP and the listener is the process that was started
   (max 180 s). A server that has exited ends the wait at once; a listener owned by another
   process is not taken for the server.
5. Start `gameExe` from `gameDir` with `gameArgs` rewritten: the port inside the `BackendUrl` of
   the `-config=` argument replaced by the arena port, `-arena-session` appended once; environment
   without `DOORSTOP_*` variables (an inherited `DOORSTOP_DISABLE`/`DOORSTOP_INITIALIZED` would
   start the game without BepInEx).
6. If a step after 1 fails and the arena game was not started, restore the normal session as
   `leave` does (the old game is gone; the player must not be left with nothing). Never start a
   second game.

`leave`: wait for `gamePid` to exit; stop the arena server (a failure to do so is logged and
does not keep the game from starting); read the normal port from
`<sptRuntimeDir>\SPT_Data\configs\http.json`; if nothing listens there, start
`<sptRuntimeDir>\SPT.Server.exe` with `WorkingDirectory = <sptRuntimeDir>` and wait for the port;
start the game with `-arena-session` removed and the `BackendUrl` port set back to the normal port.
The arena tree is kept; the next `enter` replaces it.

`stop-server`: if `server.pid` names a running process whose image is the same file as the exe the
marker says the server was started from and whose start time agrees with `server.pid`, kill it (the
tree is disposable) and delete `server.pid`. That exe is `<root>\server\SPT.Server.exe`, recomputed
from the helper's own session root (the marker's `serverExe` only says that the server was started
from the tree; a kill decision does not rest on a path read from a file), for a marker that has
`serverExe`; and `SPT.Server.exe` of the recorded `sourceRuntime` for a marker without it, a server
started by an older helper, so that a session which outlives a helper update is still stopped once.
The two are never both accepted: a marker with `serverExe` does not accept the installation's exe,
which is also the image of the normal server. Images are compared as files, not as text (a junction or
a SUBST drive gives one file several spellings). A pid that fails these checks is not the server and
is left alone (a reused pid also loses its `server.pid`). `enter` and `leave` additionally leave a
server alone whose `sourceRuntime` is not their own `sptRuntimeDir`.

`status`: print a JSON line with tree, marker, pid and port state.

Pure functions (argument rewrite, environment cleaning, `http.json` and `sptLogger.json`
patching, path safety, request parsing) live in their own file and are covered by
`ArenaMode.Tests`; the client links the request parser.

## 7. Client (`ArenaMode.Client`)

- `ArenaSessionMode`: `IsArenaSession` (command-line flag), `GateReport` (parsed from the
  AppDomain data), `GateActive`.
- Whitelist admission, evaluated first inside `Compat/ArenaOptionalMods.GetRejectionReason()`:
  - Arena session: refuse with a reason if the gate is not active or the server ping does not say
    `arenaInstance:true`. An internal failure refuses (a process that cannot prove it is
    filtered must not run Arena).
  - Normal session: evaluate `Chainloader.PluginInfos` and the server's `serverMods` with the same
    policy. If anything is excluded, refuse: `Arena runs only with whitelisted mods. Not on the
    whitelist: <names>. Start an Arena session to play Arena without them; the game restarts.`
    and show the action **START ARENA SESSION**. In blacklist mode only mods the user denied are
    excluded, so the first two sentences read `Arena does not run with mods denied in your Arena
    policy file: <names>.` and the rest is unchanged. When no session can be offered (older server, no
    helper, no local SPT server, a remote backend, the server already is an Arena instance, or a
    server the helper cannot switch: no allowed server mod, or one whose folder is not a direct
    child of `<game>\SPT_Runtime\user\mods`) the refusal ends instead with `Remove them and
    restart the game to play Arena.` and shows no action. A missing or unreadable default
    whitelist makes the whitelist stand aside in a normal session (the existing adapters still
    apply); an internal failure does the same there.
  - Version warnings never refuse; log them once (Info) and show them as a second line.
  - Fika keeps its existing refusal first and gets no session action.
- Arena session menu: ARENA is preselected, PMC and Scav are not selectable, and a notice reads
  `ARENA SESSION: only Arena is available. Profile changes in this session are discarded.` with the
  action **RETURN TO NORMAL SESSION**.
- `ArenaAvailabilityNotice` gets an optional action (label + callback). The action needs two clicks:
  the first changes the label to `CLICK AGAIN TO RESTART THE GAME`, a second click within five
  seconds triggers it.
- `ArenaSessionSwitch.Request(direction)`: writes the request file (game exe and root from
  `BepInEx.Paths`, arguments from `Environment.GetCommandLineArgs()` without argv[0], profile id
  from the `-token=` argument, else from the logged-in profile, and only if it is the 24
  hexadecimal characters the helper accepts, `sptRuntimeDir = <gameDir>\SPT_Runtime`, the allowed
  `serverMods` with folders from the last ping), starts the helper and, only if the helper process
  started, quits the game with `Application.Quit()`. Before anything is written it runs the
  helper's own request parser (`HelperRequest.TryParse`, linked source) on the request text; a
  request the helper would refuse leaves the game running and shows the reason. One Info line per
  request.
- Ping parsing: new optional fields `arenaInstance` (bool) and `serverMods`
  (`[{guid,name,version,folder,dependencies}]`, `dependencies` an optional array of GUID strings, absent
  or malformed = none); absent fields mean an older server: no session offer, normal
  admission continues with the client-side check only. When a screen opens, the ping is
  refreshed with a bounded wait (250 ms) if the answer depends on it; a ping that does not
  arrive in time leaves the fields unknown and the explicit Arena click asks again.

## 8. Server (`ArenaMode.Server`)

- Ping adds `arenaInstance` (`arena-session.json` exists in the working directory) and
  `serverMods` (every loaded mod: GUID, name, version, absolute folder, and the GUIDs of its
  `ModDependencies`, sorted).
- Arena instance: refuse ordinary (non-Arena) local raid starts with a clear error; log one Info
  line at startup: `Arena session server: N mods loaded (<names>)`. The refusal is a normal SPT
  response body `{err: 1000, errmsg, data: null}` (`UnknownScriptExecutionError`), not an
  exception or an HTTP error: the EFT client retries every failure it classifies as default for
  minutes, while it treats this code as expected, aborts the start at once and returns to the
  main menu.
- Nothing changes for a normal server apart from the two ping fields.

## 9. Build, deploy, packaging

- New projects in `ArenaMode.sln`: `ArenaMode.Gate`, `ArenaMode.Session`.
- Deploy targets: the gate to `<SPT>\BepInEx\patchers\maschine-ArenaMode\`, the helper exe and
  `assets/arena-whitelist.json` to `<SPT>\BepInEx\plugins\maschine-ArenaMode\`. Every new deploy
  target must be overridable the same way the existing ones are (`tools/AirPitChecks/NoDeploy.targets`).
- `tools/Write-ArenaAssets.ps1` and the release packaging include the three new files.

## 10. Acceptance on SPT-GPU-2

1. Normal start without the flag: the gate is inert; plugin list and Harmony counts equal the
   previous build.
2. Normal session with extra mods: ARENA is disabled with the whitelist reason and the START ARENA
   SESSION action.
3. Enter: the game quits; the arena server on 6970 loads only ArenaMode, SAIN, APBS and ABPS; the
   game starts with only allowed plugins; the ARENA SESSION notice shows; PMC and Scav are not
   selectable; a deathmatch on Factory runs to the end.
4. Leave: the normal server and the full plugin set are back.
5. Killing the game inside a session leaves the normal launch path unaffected.

Result on SPT-GPU-2, 2026-10-21 (build `db99587`, deployed to `D:\SPT41`), all five passed:

| Step | Observed |
|---|---|
| 1 | Normal start: 41 plugins loaded, 1198 patched methods; the gate is listed as a loaded patcher and writes no line of its own. Patcher order in the player: BepInEx.Preloader, FixPluginTypesSerialization, LogTools, spt-prepatch, gate, WTT-ContentBackportPatcher. |
| 2 | ARENA disabled with the whitelist reason and START ARENA SESSION; the two-click action works. |
| 3 | Restart to the main menu in 38 s. Gate: WTT patcher switched off, 13 plugins allowed, 28 excluded, 0 warnings; 545 patched methods. Arena server on 6970 ready 4 s after start with exactly ArenaMode, SAIN, APBS and ABPS; the profile copy only lost the PitFireTeam trader. ARENA preselected, PMC/Scav locked. Deathmatch on Factory: 7 bots active on SAIN and Arena layers, no ArenaMode warning; left and returned normally. |
| 4 | RETURN TO NORMAL SESSION: restart in 39 s, arena server stopped, normal server kept running, 41 plugins and 1198 patched methods again. |
| 5 | Second enter, then `taskkill /F` on the game and a normal `gpu-run start`: normal session with 1198 patched methods; `stop-server` then ended the left-over arena server. |

Follow-up fixed after the run: the refusal named a mod with differently named client and server parts
twice ("DynamicMaps, Dynamic Maps"); the list is now de-duplicated by GUID.

## 11. Changes since the first draft

Sections 5 to 8 above describe the implementation, not the first draft. What changed, and why:

- Section 5: the filter is installed in `Finish()`, not `Initialize()` (installing it earlier loads the
  unpatched `UnityEngine.CoreModule` and no plugin starts; measured, see
  [ARENA-GATE-20261021.md](ARENA-GATE-20261021.md)). The report is provisional until the first scan.
  Neutralised patchers are named in one Info line because Debug never reaches the log during the
  preloader stage, and a later failure says that they stay off.
- Section 6: `status` takes no mutex. A failed `enter` restores the normal session after the old game has
  exited (a player must not be left with no game). `stop-server` compares the server image as a file
  (junction and SUBST spellings differ from the path the game was started from), and `enter`/`leave` leave
  the arena server of another installation alone because the runtime root is shared. The wait for a server
  checks that the process it started holds the port. The request rules are stricter than the first draft
  (24-hex profile id, `serverMods` folders directly below `<sptRuntimeDir>\user\mods`) and the client runs
  the same parser before it quits the game. The tree is kept after `leave`. The server mod copies confine
  what the helper itself and the mods write beside their own files; mods that resolve paths from
  `AppContext.BaseDirectory` were not confined until the exe moved into the tree (last entry below).
- Section 7: the profile id is the `-token=` argument first; the ping is refreshed with a 250 ms bound when a
  screen opens; the refusal text differs when no session can be offered; the whitelist fails open in a normal
  session and closed in an Arena session.
- Section 8: the refusal of an ordinary start is an SPT error body (`err` 1000), not an exception.
- Sections 3, 4 and 7 (2026-10-21, user decision): the user file may carry `"unlisted": "allow"`. Per unit the
  order is platform/arena, then the user `deny`, then the named entries, then `unlisted`. New reason
  `unlisted-allowed`; an unknown `unlisted` value invalidates the whole user file; the policy hash
  covers the mode and the denied names in blacklist mode only (whitelist hashes are unchanged); the
  client refusal and the gate's patcher warning name the denied mods instead of a whitelist.
- Section 6 (2026-10-21, after the GPU run): the arena server runs from its own copy of the server binaries
  in the tree, not from the installation's `SPT.Server.exe`. Why: several server mods resolve paths from
  `AppContext.BaseDirectory`, the folder of the exe, so a server started from the real runtime let them
  write into the real installation although the helper had copied their folders. What changed: `enter`
  copies the top-level files of the runtime (minus `sptLogger.json`, `SPT.Launcher*`, `*Linux*`, `*.so`)
  incrementally into the tree root and starts `<root>\server\SPT.Server.exe`; the marker names it as
  `serverExe`; the identity check of `stop-server`/`enter`/`leave` expects the tree's exe for such a marker
  and the installation's exe only for a marker without `serverExe` (an older helper's session is stopped
  once, a marker of this helper never takes the installation's exe for the arena server); the tree root
  is cleaned except for the binaries that are still current. Evidence (offline, scratch folders only, the
  installation was only read): the helper's own `Enter`, `Leave` and `stop-server` against a stand-in
  server with the same layout (apphost exe, dll, `runtimeconfig.json`, `deps.json`) showed the stand-in's
  `AppContext.BaseDirectory` and image path in the tree, nothing written to the runtime, a second `enter`
  copying no binary and an older helper's session (marker without `serverExe`, server running the
  runtime's exe, which wrote into the runtime as the control) being stopped by `stop-server` and by `enter`;
  the real `SPT.Server.exe` of 4.1.6 booted from a tree built from a scratch copy of the installed
  runtime (44 files, 23 MB copied, four server mods loaded, `/health` healthy, the server log shows the
  Arena instance, the copied runtime unchanged afterwards). Not covered: a GPU-host run of the whole switch
  with this change (section 10 records the run before it).
- Sections 3, 4, 6, 7 and 8 (2026-10-21, review pass): the closure covers server mods. The ping's `serverMods`
  entries carry `dependencies` (the mod's `ModDependencies`) and the client passes them as hard dependencies;
  before, a `deny` (or an omission under a whitelist) of a library that an allowed server mod needs made SPT's
  mod validator drop every mod in the tree, ArenaMode's server mod included. In blacklist mode a `deny` list
  that is not an array of non-blank strings invalidates the user file like an unknown `unlisted` value (a lost
  deny entry would load a mod the user excluded); everything else in the user file stays lenient. Section 6
  states the mixed-helper limit of installations that share one Windows user.
