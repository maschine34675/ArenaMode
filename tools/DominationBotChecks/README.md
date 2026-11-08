# Domination bot objective checks

Run `./tools/DominationBotChecks/Verify.ps1` from the repository root. This is a
standalone .NET source-linked harness: it compiles the real objective bridge and
the three BigBrain objective action/layer/runtime files, with external game,
physics, navigation, session and physical roster doubles. It does not reference
or build the production client/companion projects. The wrapper also passes the
shared NoDeploy targets and a non-installing server destination.

Since 2026-11-08 the bridge asks the running mode through the production
`IArenaObjectiveHost` (`ArenaMode.Client/Modes/IArenaObjectiveHost.cs`, linked) instead of
`TeamDeathmatchMode` directly; Team DM's exact answer
(`TeamDeathmatchMode.ObjectiveHost.cs`, linked) sits beside the doubled Domination partial,
and a single-team host double models Tactical's enemy squad: scav bodies lease as team A on
one point, a disowned body loses its lease, a host that is not live revokes every lease.
The mutant `faction-not-host` (the bridge reading the faction again) must fail the single-team
check; `host-not-live` (the bridge ignoring the host's live flag) is killed earlier, at the Team DM
case `ordinary TDM excluded`, which mutates the same `ObjectivesLive` term the single-team
`not live` assertion relies on.

The original movement, combat/medical yielding, exact body ownership, faction,
navigation and cleanup checks remain. The variable objective-count fixture
models the frozen mode's published `ObjectiveCount` at that external boundary.
It verifies:

- One, two and three objectives distribute each team over the available points
  without replacing stable leases every planning tick.
- A publisher/count mismatch or stale shorter snapshot revokes old movement
  authority; a subsequent matching publication can resume assignments.
- Sixteen points are accepted; zero or more than sixteen are rejected.
- Null, duplicate/empty identity and nonfinite-coordinate publications revoke
  existing leases instead of leaving bots controlled by stale objective data.

The wrapper compiles and executes 21 isolated source-copy mutants. Every mutant
must compile and fail at its named behavioral assertion; a build error does not
count. The added count mutations restore a fixed-three restriction, remove the
frozen publisher-count check, or remove the upper bound. Before/after source
hashes ensure the wrapper did not edit the real implementation. Evidence lives
under `artifacts/domination-bot-checks/<run-id>/`.

This is not proof of Unity movement, live NavMesh geometry, original map capture
positions, native BigBrain scheduling or GPU performance. Those require the
separate fixed-layout/runtime and in-game tests.
