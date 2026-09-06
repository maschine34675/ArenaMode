# Arena SAIN personality exclusions

The optional client adapter supports the installed SAIN **4.5.1**. Arena replaces
only **Rat, Timmy and Coward** assignments with the current preset's **Normal**
personality. Other choices, including Wreckless, SnappingTurtle, GigaChad, Chad
and custom personalities, stay unchanged. This is a conservative first balancing
step: the excluded share becomes Normal, not a reroll that increases the number
of aggressive personalities. Personality settings can affect difficulty as well
as behavior; this is not just a movement change.

`F12 -> ArenaMode -> Bots -> ExcludePassiveSainPersonalities` defaults to true.
It applies to new assignments in every Arena mode. Start a fresh raid when
comparing on/off; existing bots are not reassigned by the setting. SAIN does not
need edited files, presets or a custom build. Without SAIN, Arena works normally.
Unreviewed SAIN versions or incompatible signatures disable this optional filter
with one warning rather than blocking the raid.

## Implementation and scope

- A postfix on `SAINBotInfoClass.GetPersonality(out settings)` substitutes the
  personality and corresponding settings together. SAIN calls it during bot
  construction and `ConfigureBot`, before calculating difficulty/search/hold
  values. The last selection boundary also covers forced/nickname personalities.
- A prefix on the public `SetPersonality` replaces an explicitly requested
  forbidden value. SAIN's setter still installs both values itself. Existing
  allowed assignments are left untouched.
- Reflection closes generic hooks over SAIN's actual enum/settings types. There
  is no hard SAIN assembly reference or bundled SAIN binary. The plugin declares
  a soft load-order dependency and removes its own hooks on shutdown/partial
  installation failure.
- Every change requires an armed or exact active Arena raid, matching actual
  map, AI ownership and the current body in `GameWorld`. Stale sessions, replaced
  bodies, humans and normal raids retain their native assignments.
- The current preset dictionary is read on every changed assignment. No shared
  settings, assignment flags or preset files are modified. Missing Normal data
  preserves the native result instead of mixing an enum with different settings.
- Diagnostics show at most the first three replacements per world, without bot
  names/profile IDs. There is no frame-by-frame polling or repeated reassignment.

## PitFireTeam boundary

Installed PFT **0.10.0**, without its separate SAIN addon, disables SAIN combat
layers for followers and controls their combat itself. SAIN personalities still
exist on their components, but filtering them does **not** make allied combat
behave like SAIN-controlled opponents. PFT also sets `ForgetEnemyTime`; this
adapter never recalculates timers or changes Arena's existing post-death pursuit.

The newer reference addon has its own allowed personality assignments, but it is
not installed and its hook signatures differ from installed PFT. It is not
runtime proof for this setup. No SAIN or PFT source/assets are copied or bundled.

## Checks

Run from the repository on the local SPT host:

```powershell
pwsh -NoProfile -File tools/SainPersonalityChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/SainPersonalityChecks/Verify-Regression.ps1
pwsh -NoProfile -File tools/SainPersonalityChecks/Verify-Deployment.ps1
```

The production-linked harness currently exercises 176 assertions: both typed
hooks, all enum choices in armed/active contexts, matching settings references,
unchanged preset objects, explicit assignments, partial-install cleanup,
ordinary/foreign/stale contexts, current-body checks, preset reloads, missing
Normal data, bounded diagnostics and shutdown. Five temporary source mutants
must fail named behavior assertions before a final unchanged baseline passes.
The test executable deliberately has SAIN's assembly identity; it is a stand-in,
never deployed and never a replacement for SAIN.

Installed Cecil checks confirm version, signature/parameter names, enum members,
public owner access, preset layout and SAIN initialization order. An independent
review also traced native `Player.Init -> GameWorld.RegisterPlayer` before
`BotOwner.PreActivate -> SAIN component construction`, confirming that the
current-body check is available during initial assignment.

The deployment check runs existing Arena spawn/PFT/expansion checks, builds and
deploys the client in Release, checks live/client/Core hashes, optional dependency
metadata and verifies that all installed SAIN client/server files and the existing
`me.sol.sain.cfg` stayed unchanged.
The server mod and profile protection are not changed by this feature.

Limits: stand-ins invoke the registered production hook methods; they do not
execute Unity or Harmony detours. A separate experimental .NET Framework probe
could not load the installed Harmony DLL because Windows' assembly-origin trust
check rejected it; no trust setting or DLL flag was changed to bypass that check.
No successful real-Harmony runtime test is claimed. The host has no GPU, so a
fresh Arena raid, a later bot spawn/preset reload and a following normal raid
remain required user tests. Optional-adapter failures should appear once in the
BepInEx log; successful initialization and the first replacements are logged.

Source/version cross-check: [official SAIN 4.5.1 release](https://github.com/ArchangelWTF/SAIN/releases/tag/v4.5.1).
Actual behavior claims above were checked against the installed assemblies and
local decompilation, not inferred from newer reference code.
