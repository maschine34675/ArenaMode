# Unified Deathmatch rules and runtime

2026-09-12. Endless and Rounds share one canonical Deathmatch implementation.
`RoundsMode` remains the concrete type for existing round-event, scoreboard and
lighting consumers. It accepts an immutable `DeathmatchOptions` captured by the
arming/menu integration; the mode never rereads mutable configuration in a raid.
Normal raid, server profile protection and authenticated Tactical identities are
outside this rules adapter and retain their existing ownership boundaries.

## Round and result contract

- Round count zero means unlimited rounds. A positive value plays exactly that
  many scoring brackets. Finite rounds without a kill or time limit are rejected.
- The kill limit and optional time limit apply to each round. With both limits
  zero play is continuous, without intermissions or an automatic result.
- In free-for-all, the first individual to reach the kill target wins the round.
  At time expiry the highest individual round kills win; a tie or scoreless
  bracket draws. The deadline is checked before accepting an event, so a kill at
  the deadline cannot change the already expired standings.
- In all-vs-player, only the player's kills count toward the target. Reaching it
  wins the round; timing out with an unmet positive target loses. Timer-only
  brackets complete neutrally. Bot kills never form an opposing team score.
- Finite FFA series compare individual round wins, drawing if the lead is tied.
  Finite all-vs-player target challenges compare player round wins and misses;
  timer-only series complete neutrally. A manual raid exit awards no result.

The round's winner ID/name, reason, local kills/deaths/points and draw flag are
available beside the existing `RoundState` fields. End events carry an independent
finished snapshot, so a subscriber retaining one does not see it turn into the
next round's zeroed state. Match totals remain on the existing scoreboard.

Generated bot profile IDs are different contestants, even if their display names
match. No synthetic persistent bot slots or aggregated FFA opponent are created.
Round winner calculations use their own counters because the main scoreboard
compacts ordinary bot history. Up to 4096 distinct scoring contestants are kept
per limited round and up to 4096 distinct winners per finite series; exceeding
either capacity ends explicitly as NO CONTEST instead of silently dropping a
potential winner. Unlimited series keep no match-long winner table, and continuous
play keeps no opponent counter table. Counters contain scalar IDs/names only.

## Body, protection and exit ownership

The first round starts from a later Tick, after the initial kit-swap lease and
the actual native pipeline have settled, the current local body is alive, and
LocalGame and GameWorld reference that exact body. Initial loading does not
consume round time. The exact staging body is protected by the existing damage
prefix; scoring remains closed and a preparation notice explains the wait.

Round breaks retain the existing minimum three-second interval, optional live kit
replacement, full-heal fallback and current-protection query. Input can freeze
only while protection actually exists. Replacement requests retain the exact
captured game/world/match, and each break also has a generation: a delayed old
preload or completion cannot affect a later round or another raid. The existing
30-second missing-callback grace invalidates that old request before proceeding;
an active native pipeline is never bypassed by the grace period. Synchronous and
throwing native request callbacks do not strand the intermission latch.

A terminal result does not start a break respawn, heal or add protection to the
native damage coefficient. It holds death respawns, suppresses Arena's ambient
refill requests and protects the exact current local body through the existing
damage-prefix integration. Late kills, deaths and respawn/roster publication are
excluded by the common match adapter. Local-victim death is recorded once before
the kill can finish the match, regardless of native event order.

The result displays for 15 unscaled seconds after any entered replacement pipeline
settles, with a user exit request able to shorten the display. It delegates exit
to the existing `TdmMatchExit` coordinator generalized by root integration. The
coordinator still verifies the exact captured raid, immutable raid profile ID,
current local/native owner, running simulation and absence of a native pipeline
before calling Stop. Its precommit-failure interception and postcommit no-retry
rules are retained. A native owner that never recovers remains an explicit safe
exit problem; this mode never invents a repaired owner or profile.

Native review anchor: `../_decompiled/EFT/BaseLocalGame.cs` sets Stopping before
dereferencing `_playerOwner.Player` and schedules GameEnd after the black fade.
The adapter's `OnMatchEnd` invalidates pending round callbacks and releases scalar
tables without emitting a round win or scheduling native body/protection work.
The initial live kit swap also retains root's terminal admission predicate as a
defensive backstop; a pending initial preload cannot admit a post-result swap.

## Verification boundary

Run `pwsh -NoProfile -File tools/DeathmatchChecks/Verify.ps1`. The standalone project
links the actual production rules, RoundsMode and mode interface. Deterministic
native I/O doubles test result/body guards, event snapshots, intermission races,
protection fallback and captured exit arguments. Deliberately compiled source
faults must fail their intended assertion; compiler errors do not count as a
successful negative control. Evidence and source hashes are written beneath the
ignored `tools/DeathmatchChecks/evidence/` directory.

The leaf does not build/deploy the game projects or certify a native GPU raid.
Root integration separately verifies scoring admission, damage hooks, the native
exit coordinator, initial kit-swap admission, menu freezing, hostility, presentation
and delivered assemblies. Required GPU acceptance includes short FFA and
all-vs-player targets, timer-only rounds, continuous play, a final local death,
deadline during preload/body replacement, ordinary exit and a normal PMC raid.
