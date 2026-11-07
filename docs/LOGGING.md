# Arena logging

Policy introduced 2026-10-15. This affects ArenaMode and its optional BigBrain
companion only; it does not filter Unity, SPT or other mods' output.

| Level | Purpose |
|---|---|
| Info | Loaded build, available integrations, the armed mode and its frozen raid settings, loadout protection, profile restoration and player-visible limitations or recovery. |
| Debug | Successful hook/asset setup, spawn requests, placement, lifecycle/identity details, audio queue policy, normal cancellation and gameplay: match, round and wave starts, results, scores, money and per-spawn kit details. |
| Developer | Detailed movement, audio, renderer, AI and corpse probes, including individual hits/jumps. Opt-in, with existing rate limits. |
| Warning / Error | Real failures, degraded behavior, rejected protection/identity contracts, failed restore, missing assets and unexpected native exceptions. |

Gameplay is not an Info topic (user decision 2026-10-18). Match, round and wave
lines, results with scores, Tactical money and kit details such as skipped
optional grenades or spare magazines that did not fit are Debug. A no-contest
round and a kit without any reserve stay Warning: there the round has no valid
result or the player fights on the loaded magazine alone.

In F12, tick **Advanced settings** and enable **Diagnostics → Developer Mode**
(.cfg key `Diagnostics/DeveloperMode`) to collect detailed probes. It
defaults to **false**, is not a gameplay mutator and can be changed at runtime.
Enabled probes appear as **`[dev]` Info** so no global BepInEx Debug change is
needed. Ordinary Debug messages still use the logger's native Debug setting.
Turning DeveloperMode off stops these probes again. Existing saved settings are
not reset. Zombie melee observation only attaches to bots arriving while enabled;
enable it before the raid for full coverage of that diagnostic counter.

DeveloperMode also opts each new Arena respawn request into Core 1.0.8's detailed
pipeline timing. With it off, state/reflection counters and following-frame
measurements are not collected. The standalone Redeploy driver has its own
`Diagnostics/EnablePipelineTiming` switch, also off by default. A measurement
already started finishes normally if the switch changes mid-respawn.
The per-raid timing sequence counts measured pipelines; respawns while disabled
do not consume a diagnostic sequence number.
Core timing uses its own ordinary Info lines; Arena's other detailed probes
retain the `[dev]` prefix.

Native model faults receive bounded object/body context only in an admitted
Arena world. See [the exact hooks and limits](reviews/NATIVE-MODEL-DIAGNOSTICS-20261018.md).
Original native errors remain unchanged. The server independently omits only
successful exact settings GET request lines; failures and other routes keep
their diagnostics ([scope and checks](reviews/SETTINGS-POLL-LOGGING-20261018.md)).

A BepInEx installation configured with disk/console `LogLevels = All` still
records Debug messages. This cleanup does not change that global setting or
Harmony's independent trace channels.

**LogDamageExceptions remains independent and defaults on.** It records the
original failure and body state without suppressing the exception or changing
damage. Do not disable this safety alarm merely to get a clean log.

For developers:

- Use `LogDebug` / server `Debug` for routine technical information.
- Use `ArenaMode.Diagnostics.ArenaLog.Developer` for detailed probes. Guard
  expensive reflection, scene scans and payload collection with
  `ArenaLog.DeveloperEnabled` before doing the work.
- Never put gameplay decisions, cleanup, lease release, validation or safety
  warnings behind a logging switch. Diagnostic-only counters may be partial.
- Where one helper reports both normal policy and failures, route by the known
  normal reason; unknown future reasons retain Warning. The ladder generator
  supplies separate detail and warning callbacks for this reason.
- Keep the loaded client **build/MVID** in the startup Info line. It identifies
  the actual module even if a DLL on disk was replaced during a running game.

The standalone session helper keeps lifecycle summaries, retries and failures in
`helper.log`. Per-link filesystem cleanup is Debug and off by default. To collect
it for a helper run, launch with `ARENAMODE_SESSION_DEBUG=1` in its environment;
the watchdog inherits that setting. This does not change copy/delete safety,
profile handling or the game's separate DeveloperMode setting.

The cleanup reviewed all client, server and companion log emitters. Historical
Info call-site counts are not a promise about total lines per raid: other mods,
Unity and server request middleware retain their own logging policies.

Implementation and evidence:

- [Client inventory and exceptions](reports/2026-10/LOGGING-CLIENT-AUDIT-20261015.md)
- [Server/companion inventory and warning routing](reports/2026-10/LOGGING-SERVER-AUDIT-20261015.md)
- [Existing log errors and attribution](reports/2026-10/LOGGING-ERROR-AUDIT-20261015.md)
- [Harness integration](reports/2026-10/LOGGING-HARNESS-AUDIT-20261015.md)
- [Fresh GPU2 test](reports/2026-10/GPU-LOGGING-TEST-20261015.md)

Reproduce the dedicated routing check with
`pwsh -File tools/LoggingChecks/Verify.ps1`. It uses the real installed BepInEx
logger plus production `ArenaLog` source and rejects broken opt-in guards. The
server unit suite also tests routine ladder output separately from malformed
draws, prohibited-ammunition fallback and attachment failures.

## Crowd reactions (Debug)

The spectator crowd presentation (docs/reviews/CROWD-REACTIONS-20261102.md) logs at Debug only
and nothing per frame: `Arena crowd attached: mode=…, excitement=…, reactions=…` or `Arena crowd
off: …` once per match (the key was off, Zombies, ArenaMapPort absent, an older or incompatible
contract); `Arena crowd reactions claimed: handle=…, kinds=0x…, attempt=…` or, after the 30 s
window, `Arena crowd reactions not claimed: …`; one `Arena crowd reaction: kind=…, strength=…,
source=…` per sent reaction (kinds Approve, Disapprove, Start, Finish and Lull, the last with
`source=no kill for 60 s`; ", announcer speaking" when the interlock halved it); `Arena crowd
reaction dropped: kind=…, strength=…, source=…, …` when the one-mood rule or the 1 s mood window
of ArenaMapPort's roar layer drops the weaker of an Approve and a Disapprove (the rest of the
line names the other mood); `Arena crowd
reactions released: handle=…, reason=…, sent=…` and `Arena crowd detached: reason=…,
reactions=…, excitementPeak=…`.

Warnings, by prefix. Once per process and reason (the installed contract does not change between
matches): `ArenaMapPort SpectatorCrowd has no int ContractVersion property`, `ArenaMapPort
SpectatorCrowd contract version … is not supported`, `ArenaMapPort SpectatorCrowd
SetExcitement/IsAvailable have another signature`, `ArenaMapPort SpectatorCrowd reaction members
have another signature`, `ArenaMapPort crowd contract unavailable:` (the bind threw). At most
once per match each: `Arena crowd presentation unavailable:` (Attach threw), `Arena crowd
presentation stopped:` (the tick threw; everything is released), `Arena crowd murmur intensity
stopped:` (`SetExcitement` or `IsAvailable` threw; reactions keep running), `Arena crowd match
snapshot unavailable, murmur intensity and lull stopped:` (the ArenaMode snapshot read threw; the
other reactions keep running), `Arena crowd murmur intensity reset failed:`, `ArenaMapPort crowd
reactions unavailable:` (`ReactionKinds` or `ClaimReactions` threw; no further attempts),
`ArenaMapPort crowd reactions stopped:` (`React` threw; the claim is released), `ArenaMapPort
crowd reactions release failed:`, and `Arena crowd reaction event failed:` (the first exception
in a crowd event handler; it is contained and never reaches the match).

## Map-screen refresh probe (DeveloperMode)

While `Diagnostics/DeveloperMode` is on, the Arena map selection screen logs every five
seconds at Info `[dev] Arena map menu refresh: N ticks in 5 s, max M ms; avg/max per
section (ms): ported, modeEligibility, modes, eligibility, rows, prepare, areas, info,
preview; compat …; pft …`: the sections of one `ArenaLocationSelectionPatch` refresh tick
and the call timers of `ArenaMenuSelection.CallTiming` (the optional-mod admission and the
PitFireTeam roster decision). Nothing is measured while the mode is off. Added 31.10.2026
to attribute the screen's frame-time spikes (see HANDOFF, "Arena map screen frame drops").
