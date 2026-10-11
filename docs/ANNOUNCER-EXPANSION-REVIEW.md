# Additional announcer cues (2026-09-14)

This change activates 19 of the already packaged Creator recordings. It changes
presentation only: no score, mode result, timer, kit, respawn or profile rule is
altered. Development is isolated on `feature/additional-announcer-lines`; the
parallel respawn work and installed DLLs are not overwritten.

## Trigger contract

| Phrase | Trigger |
| --- | --- |
| Prepare to Fight | Once per preparation epoch: DM assembly/intermission, TDM assembly, Tactical buy/staging, Gun Game initial kit preparation |
| Three / Two / One | Only the current second of the existing TDM readiness countdown; no synthetic countdown for Tactical's buy timer |
| Taken / Lost / Tied for the Lead | Changes in FFA round kills or TDM team kills; silent at 0:0; three-second cooldown coalesces the latest net transition |
| One Minute / Thirty Seconds / Ten Seconds Remaining | Crossings of the actual live DM, TDM or Tactical time limit; no warnings during Tactical opponent assembly |
| Five Kills / One Kill Remaining | Remaining kills until the actual DM/TDM target; in FFA this follows the leading contestant, in all-vs-player the local player, in TDM the leading team |
| Match Point | Live entry in protected Tactical 5v5 when either side is one win from the first-to target |
| Final Round | Actual last finite DM round, or deciding protected 5v5 round, including first-to-one; replaces Match Point when both apply |
| Final Weapon | Gun Game's final tier is equipped on the living player; not merely earned while a swap/respawn is pending; once per ladder cycle |
| Ladder Complete | Gun Game's completed-ladder counter advances; still fires when the equipped-tier field clears during the wrap |
| Draw | Actual DM round/match or TDM match draw; never inferred from give-up, failure or no-contest |
| Shutdown | Local eligible enemy kill ends a proven physical-life series of at least five kills |
| Clutch | Protected 5v5: the living player was last ally with opponents remaining, then is still alive when validated opponents reach zero |

Time/kill thresholds are once per epoch. An initial observation below a threshold
does not replay its warning. If a frame skips multiple thresholds, only the most
urgent crossed threshold is emitted. Continuous DM without a kill or time limit
does not invent limits or lead announcements. Team presentation reads raw kills,
not score points. Final Round and Match Point occur on actual combat entry.

The runtime director reads immutable snapshots after the current mode's tick. It
requires the exact active Arena raid and current match; it cancels status before
the audio queue drains. Detach resets the presentation policy. Temporary native
owner/world transitions cancel stale status without resetting already consumed
milestones. No native player/bot reference is retained by the policy.

## Award selection and audio priority

The old match selected `StreakLabel(...)` before `MultiKillLabel(...)`, and the
audio queue also ranked sprees above multi-kills. Thus Killing Spree could replace
Triple Kill at both stages. Per-kill selection is now:

1. First Blood
2. Payback
3. Shutdown
4. Multi-kill (Double / Triple / Multi / Monster)
5. AIRBORNE (airborne hip fire), then NO SCOPE (bolt-action hip fire)
6. Kill streak (Killing Spree through Godlike)
7. Single-hit Headshot

The later shot-time extension is specified in `SHOT-AWARDS-REVIEW.md`. Since the
2026-09-17 supplement, AIRBORNE replaces the former TRICKSHOT name with unchanged
conditions; both special labels use their own `airborne.ogg` / `no_scope.ogg`
recordings. Godlike remains only a missing/invalid-clip fallback.

Scores and the HUD's existing four-kill label MULTI KILL remain unchanged; that
file's recording says ULTRA KILL. This is one selected award per kill, not a queue
of every coincident award. Triple Kill can still be displaced by a higher-ranked
award or superseded by a later multi-kill while speech is busy.

For the one pending speech slot, Last Man Standing ranks above First Blood,
Payback, Shutdown, multi-kills, Airborne, No Scope, sprees, Headshot, ladder milestones, series
milestones, remaining-limit warnings, lead changes and preparation, in that order.
Equal priority keeps the latest event. Ordinary awards expire after four seconds;
status expires after two. Numbers are never queued, so a busy or unavailable
audio channel skips them instead of speaking an obsolete countdown. Digit clips
reserve their full duration without the extra combat-callout gap.

Results have a separate slot and wait for an active combat award to finish.
Results cancel transient status; the next real Round Start clears old queued
speech. Clutch coalesces only with the immediately following same-frame win. The
original RoundWon/MatchWon event still reaches lighting and other subscribers;
if optional Clutch audio is unavailable, the original win recording is used.

Shutdown uses a separate presentation-only physical-life counter. Existing
scoreboard streak/score formulas and stable team account identities are unchanged.
The victim's current roster epoch, profile and body ID must match before its death
resets the counter. Old PFT body aliases, retired opponents and administrative or
friendly deaths do not award Shutdown. Clutch is invalidated by local death,
respawn, changed survivors, settled result and the next award round; a spectator
win after local death is not a Clutch.

## Assets, volume and limits

All 46 binary OGGs and their provenance are unchanged. The original manifest's
18 active / 28 reserve intake classification remains historical; `runtimeEventKey`
adds the 19 explicit optional bindings. The loader reads root clips plus that
whitelist, never recursively. Existing root custom overrides take precedence.
This prevents the reserve `round_won`/`round_lost` alternatives from replacing the
current recordings. Four alternate results and five CTF phrases stay unused.

The default independent Master output, F12 volume, managed PCM cache and native
audio-reset recovery remain. Mute, readiness loss and lifecycle cancellation stop
active transient speech on that owned output. The optional legacy Interface/
GUISounds route shares a source with unrelated UI, so it cannot safely stop a
phrase already submitted; pending speech is still canceled and numbers never
queue. A game restart is required for a new client/pack. No server change is needed.

## Verification boundary

Production-linked checks cover routing/recovery, pure progress rules, the real
mode snapshot adapters/director, full match/award/scoreboard behavior, and asset
selection. Historical faulty behavior and compiled fault controls must fail at
their intended assertions. Existing Tactical, PFT, TDM, give-up and unit checks
remain required, followed by a staged build against installed EFT/Core assemblies.
The staged build disables deployment and checks installed DLL hashes unchanged.

Final parent verification on 2026-09-14: audio 203 assertions and 29 source faults;
progress 139 assertions and 22 source faults; combat 151 assertions, the historical
spree-first baseline and nine source faults; actual mode integration 34 assertions
and four source faults. All 46 assets decode/hash-check, with seven additional
metadata fault controls. Existing checks pass: 621 unit tests (two skipped),
PFT 248, Team Arena expansion 830, give-up 618 with 16 faults, Tactical polish 74
with historical/fault controls, and the TDM match/exit regression suite. An
independent adversarial review found and closed the first-to-one/deciding-round
announcement edge case. No native speech or raid acceptance is claimed.

Offline tests cannot prove audibility, speech pacing or native raid callbacks.
After merging alongside the respawn work, test real TDM countdown/readiness loss,
simultaneous Triple Kill/Spree, live time and kill thresholds, FFA/TDM lead
changes, Gun Game final weapon/wrap, actual draw, and protected living Clutch versus
dead-player team victory. Check independent volume and a second match after raid
teardown. Do not use the old staged DLL after newer respawn changes without merging
and rebuilding the combined source first. See HANDOFF.md for the final build binding.
