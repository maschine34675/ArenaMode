# Extraction hardening after the MapPort pocket exits (2026-11-13)

ArenaMapPort moved the extraction points of five ported maps into the team start
pockets (its record: `D:\ArenaMapPort\docs\progress\spawn-rows-and-pocket-exits.md`).
In Team Deathmatch and Domination the formation points now lie inside, or within about
1 m of, an 8-second exit box. A read-only survey found no defect in the normal path but
two hardening gaps. This document is the contract for both; the extraction isolation
itself stays described in [TDM-SIDES-MONITORS-20261025.md](TDM-SIDES-MONITORS-20261025.md).

## 1. Arena refuses to arm without its extraction isolation

**Before.** `ArenaExtractionIsolation.Enable()` (once, from `Plugin.InitializeRuntime`)
catches a Harmony failure, logs one error and calls `Shutdown()`. Its prefixes then
return `true` (native behaviour) and Arena still armed and started. With the exits at
the spawn points, a team-mode player would have extracted natively about 8 s after
spawning.

**Now.** `ArenaExtractionIsolation.AdmissionFailure` is null exactly while all three
hooks are installed (`ExfiltrationPoint.Proceed`, the `SecretExfiltrationPoint`
override, and the extraction-only `EndByExitTrigerScenario.IGame.StopSession`
implementation); otherwise it is the player-visible reason. `ArenaOptionalMods`
returns it right after the whitelist and ahead of the server's admission failure and
the adapter contracts. That chain is the process-lifetime admission `Plugin`
registers with `ArenaMenuSelection`, so one gate covers:

- the menu: the ARENA choice and the map screen show the reason and stay disabled;
- the final validation (`TryValidateAndFreezeRequest`) before a roster is frozen;
- the raid start: `ArenaLocalRaidStartedPatch` reads `CompatibilityRejection` before
  `PostRaidStart` (the server arm) and before `ArenaSession.MarkArmed`, so the start is
  cancelled with "Arena start cancelled to protect your profile." plus the reason.

`NativeMenuChecks` (OptionalCompatibilityChecks) already proves that any admission
reason blocks all three without arming the server, and that ordinary PMC/Scav starts
never consult the chain.

Rules:

- The flag is `_enabled`, set only after the last hook is installed and cleared by
  `Shutdown()`, which the failure path calls. Never set it earlier or keep it after a
  failed install.
- The hooks keep failing open (`return true`) when they cannot prove ownership.
  Normal raids stay bit-identical; only Arena admission is closed.
- The whitelist stays the first provider (its refusal carries the START ARENA ·
  RESTART offer). An isolation failure is reported before the server's reason
  because no server update repairs it.

Verification: `tools/ArenaExtractionChecks` (admission refused before `Enable`, after
`Shutdown`, after a failure at each of the three hooks, admitted after a later
successful `Enable`; mutants `admission-ignores-hooks`, `failed-enable-claims-ready`)
and `tools/OptionalModAdmissionChecks` (reason propagated unchanged, adapters
short-circuited, whitelist ahead, server behind; mutants `skip-extraction-isolation`,
`extraction-isolation-ahead-of-whitelist`, `extraction-isolation-behind-server`).
Both new checks were red before the change (admission never consulted the flag).
