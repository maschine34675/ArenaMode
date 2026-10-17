# Equator original Team DM spawn gates — 2026-10-17

Independent Equator Team Deathmatch now asks the optional ArenaMapPort spawn-gate
API to keep the original booth doors closed during loading, team assembly and
the three-second match countdown. The gates open once; player respawns and bot
replacements leave them open for the rest of the match. Tactical and PFT Zombies
on Equator remain excluded. Other modes and maps do not request a hold.

## Contract and lifecycle

`D:/ArenaMapPort/docs/progress/spawn-gates.md` and the actual public
`ArenaMapPort.Client.SpawnGates` implementation are authoritative for the map
side. ArenaMode validates `bool Hold(string)`, `void Release(string)`, the public
static `bool IsHeld` getter and `int ClosedParts` getter on the registered
MapPort plugin's own assembly. Missing, disabled and incompatible contracts keep
the existing open-box behavior; no hard reference or new setting is added.
Ambiguous overloads are also a silent mismatch. API exceptions are actual
degradation warnings, rather than successful-integration Info chatter.

The first request occurs only in the exact armed Equator TDM native collection
postfix, after MapPort has offered its door geometry and Arena has validated and
restored all twenty private formation records. No menu-time or future-scene hold
exists. The owner captures LocalGame, GameWorld, arm epoch and the confirmed
native start/end token. The API's part count proves only that this scene has
registered parts, not that the doors are physically closed or open.

An already-held API is not borrowed: it could be a developer test or another
caller. Arena neither grants its path exception nor releases that foreign hold.
An accepted owned hold is requested only once across repeated collections.
The map API itself is not tokenized; these guards therefore bind Arena's own
request and callbacks, rather than claiming MapPort provides per-caller leases.

Cleanup releases only the captured owner after a native `LocalGame.Stop` for
the exact local profile actually committed `Stopping`/`Stopped`, on match
failure, match end, failed native loading, canceled arming, session reset and
plugin shutdown. The successful native Run observer also releases a consumed
arm that the raid-start callback rejected (including rejected server
confirmation), even though it is no longer `ArmedForNextRaid`. Old mode teardown
supplies its captured game/world/epoch; it cannot release a new match's gates.
Stale world/token/epoch leases never call the global API into a successor raid;
MapPort's owned-scene unload disposes that old scene's gate state.
The Stop observer is a postfix: Redeploy's suppressed life-only Give Up and
native Stop calls for other extracted actors must not open the local team's
gates early.

## Navigation and combat release

There is no change to native path results and no global acceptance of Partial
paths. All existing physics, immutable marker/header, actual-body, reservation
and occupancy validation stays in place.

The map NavMesh is installed after initial player placement, so a pre-hold
atrium proof is unavailable at this loading seam. During the owned hold only,
a `PathPartial` to the atrium may instead use a **complete** path inside its own
booth. This requires a current canonical formation marker (or the existing
0.75 m actual-arrival tolerance), a surveyed floor height and the own booth's
inset interior, a physically clear sampled booth anchor, and every local path
corner within the same interior. Invalid paths, missing samples, disconnected
anchors, off-formation positions and paths leaving the box all fail. North and
south anchors are original formation points 03, not invented NavMesh islands.
The separated rear south compartment is outside the accepted interior.

At countdown expiry Arena requests release but holds the match in Countdown.
MapPort marks `IsHeld=false` immediately, while its slide can take 0–5 seconds
before carvers disappear. The exception therefore stops immediately at release.
All twenty canonical candidates must then pass their real complete atrium paths
and physics checks before Live, scoring, damage, the match clock and the round
start cue are enabled. A ten-second opening watchdog fails closed if navigation
does not reconnect. This watchdog uses the same simulation clock as MapPort's
slide, so a paused game cannot expire an animation that is also paused. The
notice reads `OPENING SPAWN GATES` during that wait.
Pending spawn reservations are never cleared by opening the gates.

## Booth extraction overlap

The map's native north exit overlaps north formation point 07; the south exit
can touch the body at south point 05. Leaving these individual eight-second
exits active while their door is held would unexpectedly end a match during
assembly. `ExfiltrationPoint.Proceed(Player,bool)` is therefore deferred only
for the exact registered live local body, the owned Equator hold/slide, and the
currently registered `Equator North Gate` / `Equator South Gate` trigger which
actually contains that body. No native exit fields or triggers are changed.

Once both boxes have real complete paths, deferred native Proceed calls resume
once for a still-entered, still-live exact body on the same current trigger.
Its native extraction timer starts freshly after opening. A body which left,
died, was replaced, or belongs to a different world is never replayed; cleanup
discards deferred entries without extracting anyone. Ordinary raids, other
modes, other players, other exits and foreign gate holds retain native behavior.
The source chain is `ExfiltrationPoint.OnTriggerEnter -> Entered.Add -> Proceed
-> OnStartExtraction -> EndByExitTrigerScenario.StartExtraction -> Update`.

## Four implementation passes

1. **Source and ownership:** inspected native `BaseLocalGame.Run` collection,
   initial-player and bot/NavMesh ordering; `LocalGame.Stop`; existing exact
   Equator formation/arm/reservation ownership; and actual MapPort hold, slide,
   carve, release and scene-unload implementation.
2. **Implementation and contract checks:** optional reflected lease; loading
   hold; own-booth-only local navigation; full-path opening barrier; explicit
   load rejection, native Stop, match failure/end and reset cleanup. Independent
   review caught possible ambiguous overload resolution outside the guard; this
   was corrected and covered with a deliberately incompatible API fixture.
3. **Adversarial lifecycle review:** identified that parameterless mode cleanup
   could release a successor's static gate owner. Mode cleanup now supplies its
   frozen game/world/epoch, with old-mode and reused-object negative cases.
   Reservations are exercised across sealed arrival and full-path reopening.
   The independent reviewer also identified native Stop's wrong-profile and
   suppressed-Give-Up cases; the postfix now observes committed shutdown only.
   Source review exposed the real booth/exfil overlap, so exact local deferral
   and native restart after opening were added with separate negative checks.
4. **Verification:** the standalone production-linked spawn-gate harness runs
   the actual adapter/runtime and the unchanged production countdown method
   with the real match policy. It covers absent/disabled/foreign/invalid API,
   exact scope, local-path constraints, ownership, teardown, delayed opening,
   full-path timeout, pause, countdown restart, remaining open after respawn and
   scoped native-extraction deferral/restart. The
   existing Equator formation harness remains a separate regression check.

Commands:

```powershell
pwsh -NoProfile -File tools/SpawnGateChecks/Verify.ps1
pwsh -NoProfile -File tools/EquatorTeamSpawnChecks/Verify.ps1
```

The first check's success marker is `EQUATOR_SPAWN_GATE_CHECKS_PASSED`; both
harnesses also compile isolated source mutants and require their named negative
checks to fail. Generated sources and evidence stay in ignored `artifacts`
subfolders. Final worker results: **88 spawn-gate assertions and 16 negative
controls**, **100 formation assertions and 11 negative controls**, with both
restored baselines green. The formation harness's arrival mutation now anchors
its exact lease context, because the new extraction guard legitimately also
validates a registered local body. They do not deploy or control the game. The
parent integration run records compiled artifact identities and native
acceptance separately; these engine doubles do not prove physical sliding or
native bot movement.

## Native acceptance still required

On the exact new client and current MapPort: Equator Team DM must show the
closed gates while bots assemble, slide once after countdown, then allow both
teams to leave and fight. The first local and bot replacements must remain in
their original faction boxes without re-closing. Also exercise protected early
exit and a subsequent ordinary/nonteam selection. Stand inside an original
booth exit beyond eight seconds during assembly to confirm no extraction,
then test a fresh native timer after the doors have opened. No worker native
acceptance is claimed here; deployment and GPU control belong to the parent.
