# Deminvincibility: scoped Arena immortality isolation

Implemented 2026-10-19/20. Offline evidence is separate from native acceptance.
No foreign binaries, source, configuration or server component are shipped.

## Scope

Deminvincibility remains optional (`com.hazelify.deminvincibility`). Its
**1 HP Mode**, **Second Chance** and the associated limb-destruction protection
do not prevent an exact Arena local body from dying or giving up. Ordinary
PMC/Scav raids retain the original behavior and configuration. Bots, menu models,
unrelated worlds and merely selected/unconfirmed Arena raids are not authorized.

This deliberately does **not** disable the entire mod: its damage-percentage
slider, removal of negative health effects and fracture preferences remain
active. They therefore remain player-selected balance inputs in Arena. With
effect removal enabled, bleeding can still be removed by the foreign mod. These
preferences are not an immortality gate and are not silently rewritten.

## Verified upstream and native contracts

Primary references:

- [Published Deminvincibility 2.2.0](https://github.com/minihazel/Deminvincibility/releases/tag/2.2.0),
  source release `fec0550c40eef2de563fe0fa250da90ebe59fc53`.
- [Surveyed source](https://github.com/minihazel/Deminvincibility/tree/2d347f6629dbd3ccc49be21cadaa92228bbba310),
  additionally checked against the actual published DLL rather than assuming HEAD
  and release equivalence.
- [Kill prefix](https://github.com/minihazel/Deminvincibility/blob/2d347f6629dbd3ccc49be21cadaa92228bbba310/Patches/Kill.cs)
  and [damage prefix](https://github.com/minihazel/Deminvincibility/blob/2d347f6629dbd3ccc49be21cadaa92228bbba310/Patches/ApplyDamage.cs).

Published DLL SHA256:
`F34E19DF4257F6D35C202B39599F613C76458FAE8D27912042F37B18CED07C4A`.
The plugin advertises 2.2.0 while its assembly remains 1.0.0.0. The correctly
spelled assembly is `Deminvincibility`; the actual plugin type is the upstream
spelling `Deminvincibility.DeminvicibilityPlugin`.

The published startup installs four prefixes on `ActiveHealthController`:
`Kill`, `DestroyBodyPart`, `ApplyDamage` and `DoFracture`. `DoBleed` and
`ChangeHealth` exist in source but are **not enabled**, and must not be cited as
existing runtime conflicts. Admission verifies actual Harmony receipts, the
plugin/assembly metadata, precise parameter/result types and the whole foreign
hook inventory, including HarmonyX IL manipulators. New versions at or above 2.2.0 may pass those contracts with one
warning. Unknown or missing contracts reject only Arena; no global BepInEx
incompatibility or foreign unpatching is used.

Installed EFT 0.16.9.5 source and assembly were inspected. Native
`ActiveHealthController.ApplyDamage` calls `DestroyBodyPart`, distributes lethal
damage and eventually calls `Kill`. `Kill` sets `IsAlive=false`, then raises
`DiedEvent`. These are the death signals used by Arena/Core and give-up's
`Kill(Undefined)`. Arena must allow the **native** operations; it never fabricates
death events or substitutes a success return for a skipped native death.

## Runtime boundaries

- Prefix the foreign `Kill.Prefix` and `DestroyBodyPartPatch.Prefix`. For a leased
  Arena body, skip that foreign prefix and return `true` to its native caller.
  Native damage protection, death, cleanup and other mods' native patches remain.
- Transpile only the unique, verified `Keep1Health` config-value read inside the
  foreign `ApplyDamage.Prefix`. It reads `false` for a leased Arena body and the
  unchanged original value otherwise. Damage scaling and effect removal still run.
  Adjacent call shape, branch/exception entries and unknown in-assembly helpers or
  direct config-state reads are rejected instead of guessing at new clamp code.
- Do not consume or reset upstream `HasSecondChance`/`IsDebouncing`. A pending
  ordinary-raid debounce may complete naturally; its global state cannot delay an
  Arena death. There are no foreign config writes, dictionary clears or async
  cancellations.
- Weak leases bind the exact **health controller, physical body and GameWorld**.
  Both `controller.Player` and `body.ActiveHealthController` must agree. Initial
  registration captures an unhurt entry body before a kit replacement, while
  direct calls may acquire only the current local body. Registered respawns get
  independent leases. Retired-body callbacks keep their existing lease; a reused
  controller/body/world or a new ordinary body with the same profile does not.
- Loading admission requires a matching native LocalGame/GameWorld, PMC map,
  server-confirmed Arena end identity and pending arm. The sole pre-IBotGame
  exception is initial local construction with the exact native Profile object
  and no assigned LocalGame.LocalPlayer. A mere outgoing start marker is rejected.
- Shutdown removes only Arena's adapter patches and weak leases. Logging is Debug
  for setup, opt-in developer diagnostics for the first guarded path, and Warning
  for real contract failures/newer versions.

## Offline evidence and remaining native test

Run `pwsh -NoProfile -File tools/DeminvincibilityCompatChecks/Verify.ps1`.
The harness links all three production source files. It executes transformed
damage-prefix IL with independent health boundaries, checks published DLL metadata
without loading it, verifies the installed native API/death event, and runs
compiled fault variants. Coverage includes normal PMC/Scav controls; lethal
bullet/bleeding/fall/barbed/explosion/undefined death events; Give Up's undefined
path; initial-kit retirement; respawn and following ordinary world; reused body,
controller and profile identities; wrong/loading/start-only contexts; retained
foreign preferences and Second Chance state; forward-version and missing/extra
patch rejection; partial-install cleanup and shutdown.

The client also compiles against the real installed EFT and Harmony assemblies
with `tools/AirPitChecks/NoDeploy.targets`. Four existing nullable-annotation
warnings in `ClientSettingsModels.cs` remain unrelated.

Final offline run: **121 assertions and 27 compiled negative controls passed**;
the unchanged production baseline passed before and after all fault variants.
Evidence: `artifacts/deminvincibility-checks/01d85d5cde98486ebb6211aceb3d9ff5/`.
Final client build log:
`artifacts/ammo-raid-health-compat-20261019/demi-final-build.log`.

Four closing checks covered (1) published/native method and IL contracts,
(2) exact ownership across loading/retirement/reuse,
(3) executed transformed IL, ordinary controls and negative variants, and
(4) native-reference compilation and final source review. A separate read-only
review of the frozen production files, harness and upstream damage/death code
found no remaining blocker. No GPU deployment or host modification was performed.

Frozen production SHA256:

| File | SHA256 |
|---|---|
| `ArenaDeminvincibilityCompat.cs` | `97FDB25D8617CA1A9E4240AB390EC757F2F39954E57A7638EE7C8A706F0B55CA` |
| `ArenaDeminvincibilityContract.cs` | `CCE9E4374850EFAE8B8D3009BF246358F298020F94C94A1B7D7137AFFDA57430` |
| `ArenaDeminvincibilityScope.cs` | `C1FE5533F1FB74083F0777E759781E29B08A2FEEC955F790E2C9405A3FC03F3B` |

Native gameplay acceptance is **pending**, not implied by these checks. On an
exclusive GPU host, use published 2.2.0 with 1 HP/Second Chance enabled; prove an
ordinary control still survives, then real lethal Arena damage and Give Up lead
to their normal death/respawn or solo-Zombie result, repeat after a respawn, and
finish with another ordinary control. Keep unrelated damage/effect preferences
observable, verify saved foreign config unchanged, then remove only temporary
test files. Do not interrupt another host session or weaken protected raid-end
identity to complete this test.
