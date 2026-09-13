# Native area selection: real BepInEx configuration regression

```powershell
pwsh -NoProfile -File tools/AreaConfigChecks/Verify.ps1
```

Use `-SptRoot C:\SPT41` for the GPU installation. This standalone net10 harness
compiles the **entire unchanged production `Config/Settings.cs`**, the real menu
proposal and shared catalogs. It uses the installed `BepInEx.dll` for
`ConfigFile`, `ConfigEntry`, `AcceptableValueList`, keyboard-shortcut conversion
and actual file persistence/reloading. The installed Unity managed assemblies
provide enums and value types; no game, native engine or rendering API is called.
There are no configuration doubles or extracted/reimplemented Settings methods.

The only seams replace unrelated game/session flags, the plugin logger and the
HTTP settings report; the existing Gun Game preload constant fixture is reused.
All builds, copied dependency DLLs and real `.cfg` files stay in this harness's
ignored `artifacts/<run-id>/` directory. There are no mod project references,
SPT SDK targets, deployments or writes to the installed BepInEx configuration.

The regression checks:

- Enumerate real area-menu options across supported maps/modes and compare their
  four IDs with the actual BepInEx constraint: whole map and three surveyed
  Customs areas, including the stable Factory East ID now labelled Factory Side.
- Select every offered ID with automatic saving both enabled and disabled.
  The real `Settings.TrySelectArea` must persist the choice without a test-issued
  save, retain it through a fresh `ConfigFile` and complete `Settings.Init`, and
  avoid rewriting/reporting an already selected value.
- Reject unknown, nonexact and null menu IDs without changing the current value,
  file or report count. Real `ConfigFile.Reload` retains valid external changes;
  invalid persisted values follow BepInEx's normal first-acceptable-value clamp.
- Retired Factory South/Center rectangle IDs must be rejected by menu writes
  and the shared arm protocol. Fresh binding and live Reload clamp their old
  saved values to whole map; Customs then proposes the supported fixed contour.
- Compile the complete historical `Settings.cs` from GPU report commit
  `588090fe2c7cf6f391ee02e720d57a8bf62cfa43` independently. Its missing Factory East
  entry must fail the binding probe at `ACCEPTABLE-AREA`, and its separate old
  whitelist must fail selection at `SELECT-AREA`. The fixed source must pass both
  identical probes. Compile errors do not count as reproduced defects.

The successful result is `AREA CONFIG CHECKS PASSED` (141 production assertions,
two reproduced historical failures, focused fixed-source probes and a final
clean rerun). `verification.log` and `receipt.json` record the source/runtime
identities. The script rejects a Settings source change during verification.

Reviewed runtime: BepInEx 5.4.23.5, SHA256
`8255B28902886085C578B9E427D3073C97002DB85176D2090CDEDA90EF14CE70`.
This proves the configuration handoff and disk behavior; it does not certify
Unity button interaction, menu rendering, loading or raid navigation.
