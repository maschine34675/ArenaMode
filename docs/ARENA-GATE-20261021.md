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
  one warning per such patcher. BepInEx's own entry-point patcher and the gate are recognised by
  the assembly their delegates come from and are never touched.
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
- The default list has no entry for ArenaMapPort, PitFireTeam, DynamicMaps or Killcam, so in an
  Arena session they are excluded (see the shared agent's open issue). The installed game
  excludes 29 of its 42 plugins and allows 13 with no warning.

## Offline checks

`pwsh -File tools/ArenaGateChecks/Verify.ps1` (about 80 seconds). It builds the gate with
`dotnet build` (no deploy), compiles a driver and a dummy patcher template with the .NET SDK's
Roslyn against the game's own `mscorlib`, and runs one scenario per process under the editor Mono
(`MONO_PATH` = the game's `Managed`). The driver calls the real `AssemblyPatcher.AddPatchersFromDirectory`
and `PatchAndLoad` (with BepInEx's real entry-point patcher patching a copy of the game's
`UnityEngine.CoreModule`) and then the same `FindPluginTypes<PluginInfo>` call that
`Chainloader.Start()` makes. Dummy plugins are generated with Cecil and carry real `BepInPlugin`
and `BepInDependency` metadata. 11 scenarios, 275 checks:

| Scenario | Proves |
|---|---|
| inert | no flag: no Harmony patch, no log source, no report, every patcher and plugin untouched, even with an unreadable policy |
| full | shipped policy: decisions per plugin, hard and soft dependencies, version warnings, the Info summary, the Info line that names the neutralised patchers and one Debug line per exclusion, the type cache still complete, cache-warm rescan identical, other directory untouched, patcher neutralisation (including the order cases), built-in patcher intact, no Unity assembly before the patched CoreModule |
| user-file / user-garbage | user allow and deny, platform units undeniable, unparsable user file ignored with a warning |
| bad-default / missing-default | policy unusable: inactive report with the reason, no patch, no patcher touched, Error line |
| no-core-module | the gate refuses to install and loads no Unity assembly itself |
| postfix-failure | malformed plugin info: no exception, lists unchanged, report inactive, later scans unfiltered, the Error line says that the neutralised patchers stay off |
| control-early-install | negative control: patching during `Initialize` is detected as an early Unity load |
| baseline-real / real-install | the 42 plugins of the installed game, decided by the policy code from units read in a separate gate-free process |

Ten mutants of the gate (install in `Initialize`, no flag check, nothing removed, patchers not
neutralised, entry-point patcher neutralised, every directory filtered, soft dependency pulled in,
postfix may throw, patchers named at Debug, "stay off" not said) are compiled and must each fail a scenario; all do, for the intended reason.

Not covered, because it needs the game: the player Mono instead of the editor Mono (same version,
different build), the real chainloader and Unity start, `FixPluginTypesSerialization` and the other
real patchers, and anything ArenaMode does with the report.

## What the GPU test has to show

1. A normal start (no flag) matches the previous build: same patcher and plugin list, the same
   number of `Enabled patch` lines, no `maschine-ArenaMode.Gate` log source.
2. An Arena session start: BepInEx starts at all (`Chainloader started`), no
   "loaded too early ... UnityEngine.CoreModule" warning, the Info line
   `Arena session: N preloader patchers switched off: ...` (WTT-ContentBackportPatcher and any other foreign
   patcher) among the preloader's lines, the Info line
   `Arena session: N plugins allowed, M excluded, K warnings` after `Chainloader started`, a Debug
   line per excluded plugin (Debug needs the disk log level; the host has `All`), and
   `Chainloader.PluginInfos` equal to the allowed list.
3. The patcher order in the log matches the one above, and no "initializer ran before the gate"
   warning appears.
4. `maschine-LogTools` and `UiBridge` still work in the session.
