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

User file: `{ "schema": 1, "allow": [ <entries as above> ], "deny": [ "<guid or patcher file>" ] }`.

Effective policy = platform + arena + default entries + user `allow` - user `deny`. `deny` cannot
remove platform or arena units. A user file with an unknown schema or a parse error is ignored with
a warning. A missing or unreadable default file makes the Arena session unavailable (the gate stays
inactive and reports the error).

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
  `IsAllowedServerMod(guid)`, `ReviewedVersion(guid, side)`, `Hash` (stable hash of the effective
  policy for logs and the marker).
- `ArenaWhitelistDecision`: input = installed units `(guid, name, version, file, hardDependencies)`
  for one side; output = `Allowed` (reason: `platform`, `arena`, `whitelist`, `tool`,
  `dependency-of:<guid>`), `Excluded` (reason: `not-on-whitelist`, `denied-by-user`) and `Warnings`
  (`version <installed> differs from reviewed <reviewed>`, `pulled in as hard dependency of
  <name>`). Closure: a hard dependency of an allowed unit that is installed but not allowed is
  allowed with a warning, transitively. Same function for the server side (no dependencies there).
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
installation (see `stop-server`).

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
   - `sptLogger.json`: copy of `<sptRuntimeDir>\sptLogger.json` with every `filePattern` value
     prefixed by `arena-`.
   - `SPT_Data\`: a directory junction for every subdirectory of `<sptRuntimeDir>\SPT_Data`
     except `configs`; files directly in `SPT_Data` copied; `configs\` copied, then `http.json`
     patched: `port` and `backendPort` set to the arena port, everything else unchanged.
   - `user\mods\`: emptied, then a copy of every folder in `serverMods` (copies, not junctions,
     so what those mods write beside their own files stays in the disposable tree). Only the
     helper's own operations are confined this way: the server is started from the real
     runtime's exe, so a user-allowed server mod that resolves its paths from
     `AppContext.BaseDirectory` still reads and writes the real runtime.
   - `user\profiles\<profileId>.json`: fresh copy; `user\profileData\<profileId>\` copied if it
     exists; `user\certs\` and `user\credentials\` copied if they exist.
   - `arena-session.json` marker: `{schema, createdUtc, profileId, port, sourceRuntime,
     policyHash, helperVersion}`.
4. Start `<sptRuntimeDir>\SPT.Server.exe` with `WorkingDirectory = <root>\server` (the server
   resolves data and mods relative to the working directory and its DLLs relative to the exe),
   its own console window, an environment without `DOORSTOP_*` variables; write `server.pid`.
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

`stop-server`: if `server.pid` names a running process whose image is the same file as
`SPT.Server.exe` of the recorded `sourceRuntime` (compared as files, not as text: a junction or a
SUBST drive gives one file several spellings) and whose start time agrees with `server.pid`,
kill it (the tree is disposable) and delete `server.pid`. A pid that fails these checks is not
the server and is left alone (a reused pid also loses its `server.pid`). `enter` and `leave`
additionally leave a server alone whose `sourceRuntime` is not their own `sptRuntimeDir`.

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
    and show the action **START ARENA SESSION**. When no session can be offered (older server, no
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
  (`[{guid,name,version,folder}]`); absent fields mean an older server: no session offer, normal
  admission continues with the client-side check only. When a screen opens, the ping is
  refreshed with a bounded wait (250 ms) if the answer depends on it; a ping that does not
  arrive in time leaves the fields unknown and the explicit Arena click asks again.

## 8. Server (`ArenaMode.Server`)

- Ping adds `arenaInstance` (`arena-session.json` exists in the working directory) and
  `serverMods` (every loaded mod: GUID, name, version, absolute folder).
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
  only what the helper does itself, not mods that use `AppContext.BaseDirectory`.
- Section 7: the profile id is the `-token=` argument first; the ping is refreshed with a 250 ms bound when a
  screen opens; the refusal text differs when no session can be offered; the whitelist fails open in a normal
  session and closed in an Arena session.
- Section 8: the refusal of an ordinary start is an SPT error body (`err` 1000), not an exception.
