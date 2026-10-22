# Arena session gate: build notes and offline evidence (2026-10-21)

Implementation notes for `ArenaMode.Gate` (`maschine-ArenaMode.Gate.dll`, spec section 5 of
[ARENA-SESSION-20261021.md](ARENA-SESSION-20261021.md)) and for `tools/ArenaGateChecks`, the offline
checks that run the gate against the game's real BepInEx under the game's Mono.

## Why the filter is installed in `Finish()`, not in `Initialize()`

The first draft of spec section 5 installed the Harmony postfix in `Initialize()` (the spec now says
`Finish()`). That cannot work. Measured under the
Mono of Unity 2022.3.43 with the game's `BepInEx.dll`:

- `Assembly.GetType("BepInEx.PluginInfo")` and `MakeGenericMethod` load nothing from Unity.
- `Harmony.Patch(TypeLoader.FindPluginTypes<PluginInfo>)` loads `UnityEngine` and
  `UnityEngine.CoreModule` from disk. (Patching an unrelated method loads neither.) `PluginInfo`
  has a `BaseUnityPlugin` member, which derives from `MonoBehaviour`; the typed-reference rule in
  the spec is not enough, the patch itself pulls the type in.
- During `Initialize()` the preloader has not yet patched and loaded `UnityEngine.CoreModule`. A
  disk copy loaded then is the unpatched one, BepInEx's entry-point patch lands in a second copy
  that Unity never uses, the chainloader never starts, and no plugin loads, ArenaMode included.
- `Finish()` runs after `PatchAndLoad` has loaded the patched modules (`Assembly.Load(bytes)`) and
  before Unity starts the chainloader. Harmony then only adds the `UnityEngine` facade and
  `netstandard`; the module is not loaded twice.

So `Initialize()` loads the policy and neutralises patchers (both must precede the preloader's
reading of the target lists), and `Finish()` installs the postfix. `Arm()` also refuses to install
when `UnityEngine.CoreModule` is not loaded yet (report: inactive, reason named): the gate must
never be the code that loads that assembly.

## Behaviour beyond the spec

- The neutralised patchers are named in one Info line written at the end of `Initialize()`
  (`Arena session: N preloader patchers switched off: a.dll, b.dll`). Debug would not reach the log: the
  preloader's log listener keeps only the levels of `Logging.Console.LogLevels` (default Fatal to Info) and
  replays them into `LogOutput.log` later. A failure after `Initialize()` leaves those patchers off (the
  preloader has consumed their lists), and the Error line says so.
- The report is published twice. After `Initialize()` it is a provisional **inactive** report
  ("the plugin list has not been filtered yet"), so a postfix that never runs cannot look like an
  active gate. The final report replaces it after the chainloader's scan.
- `PluginInfo.Location` is not set yet when the postfix runs (the chainloader sets it afterwards);
  the report's `file` is the DLL path from the dictionary key, the same path `Location` gets.
- Report warnings are, in this order: the decision's warnings, the policy diagnostics (an ignored
  user file or entry), the patcher warnings. The Info line counts all of them.
- Patchers that sort before the gate have run their initializer already. They are still
  neutralised (target list, patch, finalizer) and listed in `patchersNeutralised`; the report gets
  one warning per such patcher, and (since 2026-10-22) their file names go into `patchersRanBeforeGate`: a
  member every report carries (an empty array when none), a subset of `patchersNeutralised`, kept by the
  provisional and the inactive reports, and counted in the Info summary as ` (N patcher(s) ran before the gate)`.
  An allowed patcher is never listed, wherever it sorts. BepInEx's own entry-point patcher and the gate are
  recognised by the assembly their delegates come from and are never touched.
- The postfix never throws. The plan (decision, report JSON) is built before any list changes;
  the removal restores the lists if it fails. A failure turns the gate Failed for good: later scans
  pass everything, the report is inactive with the reason, an Error line is logged.
- Debug lines also carry the time Harmony needed (74 ms in the harness) and the time one filter
  run needed (6 ms).

## Observed and known

- Patcher order under the editor Mono of the game's Unity version (case-insensitive, culture
  style; the player's Mono has to confirm it): `BepInEx.Preloader`,
  `FixPluginTypesSerialization`, **`maschine-ArenaMode.Gate`**, `maschine-LogTools`,
  `spt-prepatch`, `WTT-ContentBackportPatcher`. With the patchers installed today only
  `FixPluginTypesSerialization` (platform, allowed) runs before the gate, so nothing needs the
  "initializer ran before the gate" warning. The GPU log must confirm this order.
- Harmony on Mono detours the shared code of every reference-type instantiation of
  `FindPluginTypes<T>`. After the patch `FindPluginTypes<PatcherPlugin>` would return a dictionary
  of `PluginInfo` lists. Nothing calls it after `Finish()` (only BepInEx's own patcher scan, long
  done; no installed mod or patcher references `FindPluginTypes`), and the postfix ignores anything
  but the chainloader's plugin directory. It is a hazard only for a future caller.
- The default list of 2026-10-21 had no entry for ArenaMapPort, PitFireTeam, DynamicMaps or Killcam, so in an
  Arena session they were excluded (29 of 42 installed plugins excluded, 13 allowed with no warning). Since
  2026-10-22 the default list allows every installed mod and denies five (ORBIT, Questing Bots, Adaptive Bot
  Culling, ISB Special Forces, Aishi); the real-install scenario then allows all 41 plugins of the host, with no
  warning. A unit the default file denies is excluded with the reason `denied` (the user's deny: `denied-by-user`,
  absent from the list: `not-on-whitelist`); the gate's patcher warning names the list too.

## Offline checks

`pwsh -File tools/ArenaGateChecks/Verify.ps1` (about one minute for the scenarios, about five with the mutants;
`-SkipMutants` leaves them out). It builds the gate with
`dotnet build` (no deploy), compiles a driver and a dummy patcher template with the .NET SDK's
Roslyn against the game's own `mscorlib`, and runs one scenario per process under the editor Mono
(`MONO_PATH` = the game's `Managed`). The driver calls the real `AssemblyPatcher.AddPatchersFromDirectory`
and `PatchAndLoad` (with BepInEx's real entry-point patcher patching a copy of the game's
`UnityEngine.CoreModule`) and then the same `FindPluginTypes<PluginInfo>` call that
`Chainloader.Start()` makes. Dummy plugins are generated with Cecil and carry real `BepInPlugin`
and `BepInDependency` metadata. 17 scenarios, 617 checks (since 2026-10-22; `default-deny`, `no-early-patcher`, `user-oversize`,
`unidentified-patcher` and `gate-unlocated` are new):

| Scenario | Proves |
|---|---|
| inert | no flag: no Harmony patch, no log source, no report, every patcher and plugin untouched, even with an unreadable policy |
| full | shipped policy: decisions per plugin, hard and soft dependencies, version warnings, the Info summary, the Info line that names the neutralised patchers and one Debug line per exclusion, the type cache still complete, cache-warm rescan identical, other directory untouched, patcher neutralisation (including the order cases), `patchersRanBeforeGate` (read from the raw JSON of the provisional and the final report: exactly the denied patchers that sort before the gate, not the allowed ones that sort there nor the denied ones that sort after it), the ` (1 patcher(s) ran before the gate)` suffix of the summary, built-in patcher intact, no Unity assembly before the patched CoreModule |
| no-early-patcher | the same world without the denied patcher that sorts before the gate: `patchersRanBeforeGate` is present and empty in the provisional and the final report, the summary line has no suffix, the denied patchers that sort after the gate are neutralised without a warning |
| user-file / user-garbage | user allow and deny, platform units undeniable, unparsable user file ignored with a warning; a user allow lifts a default deny (ORBIT); the patcher warning names the user's deny |
| default-deny | the shipped file plus extra default denies (a plugin, an allowed patcher, a patcher that sorts before the gate): reason `denied`, a listed-and-denied patcher is denied and reported, the patcher warning says `is denied by the default policy list` |
| user-blacklist | `"unlisted": "allow"` with a deny list: everything not denied is allowed (`unlisted-allowed`, no warning of its own), denied plugins are excluded (`denied-by-user`; the shipped default's deny of ORBIT still holds, reason `denied`), a hard dependency the user denied is not pulled in and the unlisted plugin that needs it is excluded (`requires-denied:<guid>`, with its own warning; deny wins over hard dependencies), every preloader patcher runs except the denied ones |
| bad-default / missing-default | policy unusable: inactive report with the reason, no patch, no patcher touched, Error line, `patchersRanBeforeGate` present and empty |
| no-core-module | the gate refuses to install and loads no Unity assembly itself; the inactive report still lists the patchers that ran before the gate |
| postfix-failure | malformed plugin info: no exception, lists unchanged, report inactive, later scans unfiltered, the Error line says that the neutralised patchers stay off, the inactive report keeps `patchersRanBeforeGate` |
| user-oversize | a valid user file one byte above 256 KB that would deny SAIN is ignored as a whole with a warning (`user whitelist: cannot read ..., the file is ignored: larger than 256 KB`), none of its deny list applies and the report's policy hash is the one of the default list alone; the client reads the same files through the same function (`ArenaPolicyFiles`) and ignores it too |
| unidentified-patcher | four patchers the gate cannot place, one early and one late of each kind (delegates from a dynamic assembly, which has no file; a patcher with only a patch method, so no assembly to find): all four are neutralised under `(unidentified) <type name>` with their own warning, the early one with an initializer is listed in `patchersRanBeforeGate` and its initializer ran, none of the late ones ran anything, and the preloader gets to its end (BepInEx would fail on a patcher without a target list) |
| gate-unlocated | the gate is called through a wrapper patcher and is not itself in the patcher list, so its position is unknown: every neutralised patcher is listed in `patchersRanBeforeGate` (including ones whose initializer has not run) and one warning says why |
| control-early-install | negative control: patching during `Initialize` is detected as an early Unity load |
| baseline-real / real-install | the 41 plugins of the installed game, decided by the policy code from units read in a separate gate-free process; the WTT patcher is on the default list now and runs untouched; no patcher is neutralised, so `patchersRanBeforeGate` is empty whatever sorts before the gate |

Thirty-five mutants of the gate and of the shared session sources it links (install in `Initialize`, no flag
check, nothing removed, patchers not neutralised, unlisted patchers neutralised in blacklist mode, entry-point
patcher neutralised, every directory filtered, soft dependency pulled in, postfix may throw, patchers named at
Debug, "stay off" not said, patcher warning does not name the default deny, patcher warning does not name the
user's deny, Debug exclusion line without the reason, the six around the policy-override suffix, flag and
warning, and nine for `patchersRanBeforeGate`: never collected, every neutralised patcher listed, allowed early
patchers listed, missing in the final report, missing in the inactive reports, not published, omitted when empty,
summary suffix missing, suffix written for an empty list, and six of the second pass: the size limit of the shared
policy reader off, a private reader for the user file, a patcher without a file left active, not reported as having run
early, without its warning, and an unknown gate position listing nothing) are compiled and must each fail a scenario; all
do, for the intended reason.

Not covered, because it needs the game: the player Mono instead of the editor Mono (same version,
different build), the real chainloader and Unity start, `FixPluginTypesSerialization` and the other
real patchers, and anything ArenaMode does with the report.

## What the GPU test has to show

1. A normal start (no flag) matches the previous build: same patcher and plugin list, the same
   number of `Enabled patch` lines, no `maschine-ArenaMode.Gate` log source.
2. An Arena session start: BepInEx starts at all (`Chainloader started`), no
   "loaded too early ... UnityEngine.CoreModule" warning, the Info line
   `Arena session: N preloader patchers switched off: ...` among the preloader's lines, written only when a
   patcher outside the default list is installed (with the default list of 2026-10-22 a host that has just
   FixPluginTypesSerialization, maschine-LogTools, spt-prepatch and WTT-ContentBackportPatcher switches none off:
   the line is not written, and the WTT patcher has to run), the Info line
   `Arena session: N plugins allowed, M excluded, K warnings` after `Chainloader started`, a Debug
   line per excluded plugin (none when every installed plugin is allowed, as with the default list of
   2026-10-22 on the development hosts; Debug needs the disk log level, the host has `All`), and
   `Chainloader.PluginInfos` equal to the allowed list.
3. The patcher order in the log matches the one above, and no "initializer ran before the gate"
   warning appears.
4. `maschine-LogTools` and `UiBridge` still work in the session.
