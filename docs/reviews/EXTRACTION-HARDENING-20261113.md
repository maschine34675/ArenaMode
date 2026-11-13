# Extraction hardening after the MapPort pocket exits (2026-11-13)

ArenaMapPort moved the extraction points of five ported maps into the team start
pockets (its record: `D:\ArenaMapPort\docs\progress\spawn-rows-and-pocket-exits.md`).
In Team Deathmatch and Domination the formation points now lie inside, or within about
1 m of, an 8-second exit box. A read-only survey found no defect in the normal path but
two hardening gaps. This document is the contract for both fixes; the extraction isolation
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

## 2. No bot leaves an Arena raid through SAIN's extraction

**SAIN's path (installed 4.5.1, checked with Cecil, not inferred from newer source).**
`ExtractLayer.IsActive` requires `allowedToExtract()`, which reads
`Info.FileSettings.Mind.EnableExtracts`, then the preset's `SAIN_EXTRACT_TOGGLE`, and only
then asks the static `BotExtractManager.IsBotAllowedToExfil(BotComponent)` (PMC or Scav
profile). The active layer selects `ExtractAction`, which calls
`exfil.OnItemTransferred(bot)` and, after the exit timer, `LeaveData.RemoveFromMap()`.
That is SAIN's only code that touches an exit or removes a bot, and `ExtractLayer` is the
only selector of `ExtractAction`. With the toggle on, SAIN also removes BSG's own
"Exfiltration" layer from PMC brains.

**Installed state.** The only preset, `SPT_Runtime\user\mods\Solarint-SAIN-ServerMod\Presets\Default [Modified].json`,
has `SAIN_EXTRACT_TOGGLE: false`. The private Arena session tree copies each allowed server
mod folder, presets included, from the ordinary installation at every entry
(`SessionFileSystem`, "user\mods: a real copy"), so it inherits that value; no kept private
tree existed on the development host on 2026-11-13.

**Decision: a guard, not only documentation.** It is one postfix on a public static,
side-effect-free method, and it is verifiable offline. `ArenaSainExtractGuard`
(SAIN 4.5.1+, soft dependency, bound like `ArenaSainPersonalities`) sets the result to false
during an armed Arena raid on the world's map or the exact active Arena raid. Rules:

- Every bot of that raid, PitFireTeam companions included: Arena ends by its match rules.
- SAIN's own method still runs; its refusal stays a refusal; no preset or setting is written.
- Priority last, so another mod's postfix cannot reopen the exit.
- SAIN only asks while the toggle is on, so with the default preset the postfix never runs.
- Normal raids, a stale `IsActive`, another map, or a missing world/game keep SAIN's answer.
- Binding failures (version below 4.5.1, missing or changed method, Harmony failure) leave
  SAIN native with one warning; Arena is not refused for that, since the toggle is off by
  default and a refusal would block every user over an optional setting.

Verification: `tools/SainExtractGuardChecks` (33 assertions with a stand-in assembly carrying
SAIN's identity; mutants `guard-ignores-arena` (the unguarded behaviour, red),
`stale-active-raid`, `map-not-checked`, `foreign-postfix-can-reopen`, `version-unchecked`)
and its `Verify-Contracts.ps1`, which reads the installed `SAIN.dll` and fails if the
admission method's shape, the layer's toggle/admission order, or the "only ExtractAction
exits, only ExtractLayer selects it" facts change.

**Not covered offline; what a native test needs.** On a spare installation (or with the
preset backed up and restored byte-identically afterwards): set `SAIN_EXTRACT_TOGGLE` to
true, start one Team Deathmatch on a MapPort map with pocket exits (Bay5 or Equator), and
watch the BepInEx log for SAIN's "Extracted at" line and the bot count for a bot vanishing
without a death; the expected result is none. With SAIN's Debug logging on, the extract
layer should never report active for an Arena bot. A following normal raid with the toggle
on should still show SAIN bots extracting.

**Residual, not guarded.** BSG's own PMC "Exfiltration" layer (priority 79 in the
pmcBEAR/pmcUSEC brains) stays in place while the toggle is off. It needs the raid clock past
the bot's `_timeToExfiltration` (1200 s, or the location's `BotLocationModifier`
exfiltration times), no enemy in memory and AI exfiltration points in the bot's patrol data,
and it removes the bot with `RemoveFromMap` without touching an `ExfiltrationPoint`. It
existed before the MapPort change and has not been observed in Arena; whether the MapPort
maps carry AI exfiltration points and whether Arena bots use PMC brains was not checked.
