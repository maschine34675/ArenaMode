# Independent Team Deathmatch

## Behavior and selection

Select Arena on the native Scav/PMC screen, then **TEAM DM** in the existing
mode selector above the map controls. F12 also exposes `teamdeathmatch`.
`Team Deathmatch / PlayersPerTeam` defaults to 5, accepts 2–12, and is captured
at match start. The human's unchanged PMC faction determines their side.
At the default there are four friendly bots plus the human against five bots.
Death reserves the human's place until their normal instant Arena respawn.

This is continuous USEC-versus-BEAR combat using existing Arena kits and bot
progression, without a score limit, intermission, buy phase or PFT followers.
Both factions receive periodic knowledge of the nearest live opponent, even
while the human is dead; native AI or installed SAIN owns movement and combat.
The ordinary HuntPlayer/PMC-floor controls do not govern this mode.

The HUD shows faction kill totals. The Arena scoreboard separates both teams,
including all earlier lives in match totals. New generated bots start a new
personal run; they do not impersonate a previous bot. Existing team-colored
tracers classify admitted friendly/enemy identities and preserve the color of
posthumous projectiles. Same-team kills do not earn points or announcements.

Generated Team DM bots now receive fixed native faction armbands: **USEC blue,
BEAR red**. Arena applies them after APBS inventory generation and before native
profile bundle loading, with no APBS configuration changes. Human equipment and
protected PFT kits are unchanged. Armbands use faction colors independently of
the configurable friendly/opponent tracer palette.

`Effects / TeamLasers` defaults to enabled for Team DM. Visible mounted weapon
lasers and their dots use `FriendlyTracerColor` / `OpponentTracerColor`, including
the current human body and admitted active bots. Infrared lasers, flashlight
colors, activation and native intensity settings are unchanged. Unknown owners
and shaders remain native; pooled state is restored. See `tools/TeamLaserChecks`.

A successful **Give up** in Team DM adds one kill and the normal PMC base score
(currently 200 points) to the opponent team. It awards no individual bot a kill,
headshot, spree or announcer event. Normal local death/respawn accounting remains
responsible for the player's death. Failed, duplicate or stale actions do not
earn team points, and a stopped mode still permits the existing Give up escape
action. Leaving the raid is not treated as Give up. See `tools/TeamGiveUpChecks`.

Same-faction attributed bullets/explosions are blocked before health and armor
mutations. Self-damage, environmental effects and unknown attribution retain
their native behavior. Friendly grenade camera shake/contusion can remain:
the separate native contusion callback has no attacker identity, and no broad
invulnerability/visual suppression was added.

PFT is optional and is not used to form these teams. A selected full PFT roster
continues to force protected Tactical; its ownership and persistence gates are
unchanged. To play TEAM DM with PFT installed, enter the native selection alone.
No SAIN/PFT/BigBrain binary or code was bundled. Installed SAIN 4.5.1 was checked
locally; normal native AI is the fallback when SAIN is absent.

## Lifetime and safety

The server edits only its exact live per-raid location clone. PMC role rules
explicitly make USEC friendly to USEC and BEAR friendly to BEAR; opposite roles
are enemies. Copies of touched hostility records/lists prevent changes to SPT's
shared configuration. The quiet map disables all ordinary waves and bosses.

The client requests one explicit-faction bot at a time per faction, keeps the
human slot reserved, and rejects unowned direct dispatch before native counters
are charged. Each request has an exact raid lease and separate reservation.
An expired profile load cannot start a parallel load for that faction. Detached
native activations retain uncertainty, with at most three outstanding requests
per faction; late/duplicate/wrong-faction bodies cannot be admitted as replacements.
No request is cancelled with StopSpawn after native dispatch.

PreActive bodies immediately occupy capacity, but must become Active within
45 seconds. Failed or expired activations are retired before a replacement is
requested. Three consecutive activation failures stop the mode with a diagnostic.
Administrative retirement verifies the body is no longer alive; ambiguous or
suppressed retirement stops refills instead of silently creating more bots.
When a mode stops for an invariant failure, use the existing Arena Give up action.

Native group and memory enemy admission, goal selection and optional SAIN enemy
admission all apply the same faction rule. Reconciliation removes stale bodies
before new relations are added, because SAIN removes some entries by profile ID.
Weak affiliation records preserve dead-body attribution without retaining
corpses. No global faction/profile, cached difficulty or ammo template is changed.
Both sides retain Arena's configured corpse-looting restriction.

## Initial Team DM verification

The following results describe the original Team DM implementation. The later
identification and Give up changes are verified separately below.

The implementation has production-linked population/dispatch/score/award/tracer
tests, modeled mode lifecycle scenarios, server clone and native picker tests,
mutation controls, native-source review and an independent adversarial review.
The detailed acceptance ledger is `tools/TeamDeathmatchChecks/GATES.md`.
Final offline results: 157 server/profile/scoreboard tests; 14,931 population,
dispatch, scoring and award assertions; 143 actual mode/relationship checks with
modeled engine seams; 76 picker checks; 144 tracer checks; 84 Labs spawn checks.
The PFT (231), team/SAIN (767), Solo Tactical (47), announcer (142), cleanup (44)
and existing side/map/menu checks also pass. Controlled regression runs rejected
five setup, thirteen policy, three runtime, eight tracer and two Labs mutations.

Independent cross-review found and resolved failed-activation slot retention
and unverified administrative retirement. The integration also keeps friendly
TDM bots under the corpse-looting rule and waits for complete local respawn
before adopting a replacement human body. No unresolved P0–P2 review findings.

Client and server Release builds succeeded. Four existing client nullable-context
warnings remain; server build reported none. The explicit client Release build
was last, to leave Redeploy.Core in Release. Built/deployed SHA256 pairs match:

| Artifact | SHA256 |
|---|---|
| Arena client | `9F9B89662D4E1174232A5C487AFFC39375607CFBFA17B4F714E8056F44FA5773` |
| Arena server | `A979C4497B1965634DBB7DDD3573C874C7AD7BD6688912C60E73E25E09E7AE8F` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

Native anchors checked: `EFT/BotSpawner.cs` activation callback/counters,
`EFT/BotOwner.cs` PreActive/ActiveFail transition, `BotsGroup.cs` enemy/ally APIs,
`EFT/BotMemory.cs` admission/removal/goal, and `EFT/Player.cs` shot/armor order.
Installed SAIN 4.5.1 SHA256:
`C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.
Its `EnemyListController.tryAddEnemy(IPlayer)`, inherited BotOwner and exact
EnemyPlayer/EnemyInfo/last-seen APIs were verified against tagged source and DLL
metadata. Existing protected-team contract checks also pass against this DLL.

There is no GPU on this host. **Restart both game and server**, then test:

1. TEAM DM with the default 5v5: four friendly PMCs and five opposing PMCs;
   friendly bots must not target the human or their own faction.
2. Kill an opponent and a friendly bot externally if needed: each death fills
   only its own faction slot. Shoot a teammate and check health/armor protection;
   enemy and self/environmental damage must still work.
3. Die and respawn repeatedly; verify team affiliation, opponent activity,
   tracer colors and durable team points. Repeat with the other PMC faction
   using a separate test profile, without editing the main profile's faction.
4. Enter an ordinary raid afterward, and separately test protected PFT Tactical
   with a selected full roster. Neither must inherit TDM spawning or friendship.

Actual navigation, combat and mod interactions remain unverified until that
playtest. Offline passes are not a claim that a live Unity raid was run.

## Identification and Give up verification, 2026-09-05

The user confirmed the underlying Team DM works in game before these additions.
Armbands, laser coloring and Give up team credit now have independent source and
native-contract review plus fresh parent-run checks. Review found and corrected
one regression: a failed Team DM must still allow the existing Give up action
even when it cannot award team points. No unresolved P0-P2 findings remain.

- All 187 server/profile/scoreboard tests passed, none skipped; 30 new armband
  cases and four deliberately incorrect policy/session variants were checked.
- Give up: 373 production-linked assertions and six rejected behavioral mutants,
  including exact current-body affiliation for the laser service.
- Lasers: 79 production-linked assertions and six rejected mutants; installed
  native rendering fields and six visible/six IR material definitions verified.
  Installed APBS and BrightLasers contracts were inspected without editing them.
- Existing Team DM population/scoring (14,931), runtime (143), tracer (144),
  PFT (231), team/SAIN (767), Solo Tactical (47), announcer (142), cleanup (44)
  and native mode-selection (76) checks all passed again.

Final server and client Release builds passed; the client was explicitly built
last to retain Redeploy.Core Release. Server reported one MUD0002 warning in the
unchanged config page; client reported four existing CS8632 warnings. Installed
files match the built files by SHA256:

| Artifact | SHA256 |
|---|---|
| Arena client | `67EADDCEDEFBBD7C15CACDEF1F84B04029BE38F0930037AEF04E192F878993A2` |
| Arena server | `913BB606AD02A037CA70C1196881C050F55F08F3A6636B35C4C38D771CE8803A` |
| Redeploy.Core | `3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C` |

Restart **both server and game** for the new patch and client service. GPU checks
remain a handoff: inspect both factions' freshly spawned armbands with APBS,
visible laser beam/dot colors with weapon swaps and respawns, unchanged IR and
flashlight modes, and one opponent team kill/+200 points for each successful
Give up. Then check ordinary raids and protected Tactical remain unchanged.
No GPU raid was started on this host.

## Native exit window, 2026-09-06

Native `BaseLocalGame.Stop` commits `GameStatus.Stopping` before
`BotsController.StopGettingInfo` disposes bot owners; `GameEnd` is deferred to
the black-screen callback. Arena's active raid latch can still be set while
healthy wrappers become Disposed/NonActive. The old mode counted them as failed
activations, then showed a false three-failures error while leaving the raid.

TDM now requires its captured exact game to remain Started. On committed native
Stopping/Stopped it cancels its own pending work once and leaves disposal to the
native shutdown. Tick, detached predicates, observer admission, refill and
retirement/failure paths respect that boundary, including synchronous Stop during
a death callback. No failure toast or readiness cue is generated by this normal
exit. An informational shutdown line remains. Shared burst-service late-arrival
cleanup is unchanged; an expired arrival in a still-Started raid is still retired.

Life-only Give up uses native health.Kill and Redeploy intercepts the killed Stop,
leaving the game Started. Its opposing-team point and respawn remain enabled.
Real activation failures in Started still retire the failed body and retain the
three-consecutive-failures limit and error notification. Native/profile patches,
PFT/SAIN configuration and ordinary raids are unchanged.

The production-linked runtime harness covers the shutdown window and live-fault
controls, with deliberate regressions for both stop gating and the reentrant
failure/ready-cue paths. The installed metadata oracle also verifies native Stop
ordering. A GPU test still requires a game restart and remains user-side: leave
TDM from the native menu, then separately test life-only Give up and respawn.

## Bounded TDM diagnostics, 2026-09-06

Failure, timeout and native shutdown logs now include game status, whether a
local respawn is running, and both factions' occupied/target, Active, PreActive
and other living body counts. Pending work lists only process-local request
numbers, phase (`loading` or dispatched to `native`), age and expiry. The queue
is captured before failure/shutdown clears it; inspection never settles tickets.
Snapshots tolerate disposed properties without raising a new mode failure.

Existing admission lines include the arrival state and request duration. An
activation retirement records its state before Kill, faction, time waiting and
failure number out of three. A previously Active bot reports that fact instead
of an invented activation age. A successful replacement records and resets the
previous consecutive-failure count.

Unavailable native roster, readable-again, request timeout and expired-work
settled are transition-only messages, with no per-frame/periodic heartbeat.
Settled work is deliberately not called combat-ready: readiness still requires
all team slots filled by Active bodies. Detailed snapshots stay in the log;
player error toasts retain their short reason. No profile IDs are logged by
these diagnostics. Shared burst cleanup and all spawn/scoring policies remain
unchanged.

Verification for the exit and diagnostic changes: 189 production-linked mode
checks passed, with eleven deliberate regressions rejected for their intended
assertions; 373 Give up checks and 14,931 existing population/dispatch/scoreboard
checks passed. Installed native/SAIN contract checks, existing bot/PFT/SAIN
regressions and the final client Release deployment passed. Built/live client
and Redeploy.Core hashes match; 31 external mod/configuration files are unchanged.
Independent adversarial review has no open P0-P2 findings. Five acceptance gates
are met, zero are unmet, and the GPU raid gate remains a user-side handoff.
These client-only changes require a game restart, not a server restart.
