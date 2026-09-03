using ArenaMode.Modes;
using ArenaMode.Spectator;
using ArenaMode.Bots;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckFrozenRosterPolicy();
            CheckStartPolicy();
            CheckEscapePolicy();
            CheckTargetCycling();
            CheckRoundContinuation();
            CheckPursuitPolicy();
            CheckEnemyIdentityPolicy();
            CheckPursuitOwnerPolicy();
            CheckSainAssemblyPolicy();
            CheckSainSourcePolicy();
            CheckSainRebindTransaction();
            CheckSourceContract();
            CheckInstalledPitFireTeamContract();
            CheckInstalledSainContract();
            Console.WriteLine($"TEAM_ARENA_EXPANSION_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"TEAM_ARENA_EXPANSION_CHECKS_FAILED after {_assertions} assertions: {exception}");
            return 1;
        }
    }

    private static void CheckFrozenRosterPolicy()
    {
        string[] nine = Enumerable.Range(1, 9).Select(index => $"profile-{index}").ToArray();
        Assert(SpectatorPolicy.TryFreezeAllowedProfileIds(nine, out string[] frozen, out string problem),
            "exactly nine distinct non-local actors form a valid frozen spectator roster");
        Assert(problem == null && frozen.SequenceEqual(nine),
            "freezing keeps the exact identity order and reports no failure");

        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(nine.Take(8), out _, out _),
            "an incomplete 5v5 roster is rejected");
        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(nine.Append("profile-10"), out _, out _),
            "an oversized roster is rejected");
        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(
                nine.Take(8).Append("profile-1"), out _, out problem)
               && problem.Contains("duplicate", StringComparison.OrdinalIgnoreCase),
            "duplicate profile identities are rejected explicitly");
        Assert(!SpectatorPolicy.TryFreezeAllowedProfileIds(
                nine.Take(8).Append(" "), out _, out problem)
               && problem.Contains("empty", StringComparison.OrdinalIgnoreCase),
            "empty profile identities are rejected explicitly");
    }

    private static void CheckStartPolicy()
    {
        SpectatorStartGate ready = Gate();
        Assert(SpectatorPolicy.DecideStart(ready) == SpectatorStartDecision.Start,
            "an eligible dead player with a living target and no camera owner starts spectating");
        Assert(SpectatorPolicy.DecideStart(Gate(killcam: OptionalCameraOwnerState.Idle))
               == SpectatorStartDecision.Start,
            "an installed idle Killcam permits the hand-off");

        Assert(SpectatorPolicy.DecideStart(Gate(killcam: OptionalCameraOwnerState.Busy))
               == SpectatorStartDecision.Wait,
            "an active Killcam retains camera ownership");
        Assert(SpectatorPolicy.DecideStart(Gate(freecam: OptionalCameraOwnerState.Busy))
               == SpectatorStartDecision.Wait,
            "an active external freecam retains camera ownership");
        Assert(SpectatorPolicy.DecideStart(Gate(targets: 0)) == SpectatorStartDecision.Wait,
            "a valid lease waits for its first living target");

        Assert(SpectatorPolicy.DecideStart(Gate(arena: false)) == SpectatorStartDecision.Cancel,
            "spectating fails closed outside Arena");
        Assert(SpectatorPolicy.DecideStart(Gate(team: false)) == SpectatorStartDecision.Cancel,
            "spectating fails closed outside protected team Arena");
        Assert(SpectatorPolicy.DecideStart(Gate(eligible: false)) == SpectatorStartDecision.Cancel,
            "a revoked round lease cancels spectating");
        Assert(SpectatorPolicy.DecideStart(Gate(epoch: false)) == SpectatorStartDecision.Cancel,
            "an old round epoch cancels spectating");
        Assert(SpectatorPolicy.DecideStart(Gate(dead: false)) == SpectatorStartDecision.Cancel,
            "a living local player cannot enter the death spectator");
        Assert(SpectatorPolicy.DecideStart(Gate(killcam: OptionalCameraOwnerState.Incompatible))
               == SpectatorStartDecision.Cancel,
            "an installed but unreadable Killcam contract fails closed");
        Assert(SpectatorPolicy.DecideStart(Gate(freecam: OptionalCameraOwnerState.Incompatible))
               == SpectatorStartDecision.Cancel,
            "an installed but unreadable Freecam contract fails closed");
    }

    private static void CheckEscapePolicy()
    {
        var body = new object();
        SpectatorEscapeGate Gate(
            bool escape = true,
            bool arena = true,
            bool team = true,
            bool owns = true,
            bool battleUi = true,
            bool eligible = true,
            bool epoch = true,
            bool local = true,
            bool dead = true,
            object owner = null)
            => new(
                escape,
                arena,
                team,
                owns,
                battleUi,
                eligible,
                epoch,
                local,
                dead,
                body,
                owner ?? body);

        Assert(SpectatorPolicy.ShouldTranslateEscape(Gate()),
            "Escape reaches the native raid menu for the exact dead body while Arena owns the camera");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(Gate(escape: false)),
            "non-Escape commands stay on the native dead-player path");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(Gate(arena: false))
               && !SpectatorPolicy.ShouldTranslateEscape(Gate(team: false)),
            "ordinary and non-team raids cannot use the spectator Escape exception");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(Gate(owns: false))
               && !SpectatorPolicy.ShouldTranslateEscape(Gate(battleUi: false)),
            "pending Killcam and an already-open menu cannot trigger the exception");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(Gate(eligible: false))
               && !SpectatorPolicy.ShouldTranslateEscape(Gate(epoch: false)),
            "a revoked or old spectator lease cannot open a raid menu");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(Gate(local: false))
               && !SpectatorPolicy.ShouldTranslateEscape(Gate(dead: false)),
            "a foreign or respawned body cannot use the dead-player exception");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(Gate(owner: new object())),
            "a different owner body cannot be joined by profile-like equality");
        Assert(!SpectatorPolicy.ShouldTranslateEscape(new SpectatorEscapeGate(
                true, true, true, true, true, true, true, true, true, null, null)),
            "a missing expected body fails closed");
    }

    private static void CheckTargetCycling()
    {
        Assert(SpectatorPolicy.CycleIndex(4, 0, 1) == 1,
            "forward cycling advances one participant");
        Assert(SpectatorPolicy.CycleIndex(4, 3, 1) == 0,
            "forward cycling wraps to the first participant");
        Assert(SpectatorPolicy.CycleIndex(4, 0, -1) == 3,
            "backward cycling wraps to the last participant");
        Assert(SpectatorPolicy.CycleIndex(4, -1, 1) == 0,
            "a missing current target recovers at the first living participant");
        Assert(SpectatorPolicy.CycleIndex(0, 0, 1) == -1,
            "an empty target set cannot produce an index");
    }

    private static void CheckRoundContinuation()
    {
        TeamArenaRoundDecision firstWin = TeamArenaRoundPolicy.Evaluate(
            won: true,
            roundsWon: 0,
            roundsLost: 0,
            roundsToWin: 3);
        Assert(firstWin.RoundsWon == 1
               && firstWin.RoundsLost == 0
               && firstWin.Continuation == TeamArenaRoundContinuation.RebindNextRound,
            "the first best-of-five win advances to another round");

        TeamArenaRoundDecision matchWin = TeamArenaRoundPolicy.Evaluate(true, 2, 1, 3);
        Assert(matchWin.RoundsWon == 3
               && matchWin.Continuation == TeamArenaRoundContinuation.EndMatchWon,
            "the configured third win completes the match");

        TeamArenaRoundDecision matchLoss = TeamArenaRoundPolicy.Evaluate(false, 2, 2, 3);
        Assert(matchLoss.RoundsLost == 3
               && matchLoss.Continuation == TeamArenaRoundContinuation.EndMatchLost,
            "the configured third loss completes the match");

        TeamArenaRoundDecision noContest = TeamArenaRoundPolicy.Evaluate(null, 1, 1, 3);
        Assert(noContest.RoundsWon == 1
               && noContest.RoundsLost == 1
               && noContest.Continuation == TeamArenaRoundContinuation.EndNoContest,
            "a technical no-contest preserves the score and ends the protected match");

        bool rejected = false;
        try
        {
            TeamArenaRoundPolicy.Evaluate(true, 0, 0, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Assert(rejected, "an invalid rounds-to-win setting fails closed");
    }

    private static void CheckPursuitPolicy()
    {
        static TeamArenaApproachDecision Decide(
            float squaredDistance,
            bool visible = false,
            bool shootable = false,
            bool healing = false,
            bool reloading = false,
            bool throwing = false,
            bool door = false,
            bool immediateDanger = false)
            => TeamArenaPursuitPolicy.Decide(
                squaredDistance,
                visible,
                shootable,
                healing,
                reloading,
                throwing,
                door,
                immediateDanger);

        float reachDistance = TeamArenaPursuitPolicy.DestinationReachDistanceMeters;
        Assert(Decide((reachDistance + 1f) * (reachDistance + 1f))
               == TeamArenaApproachDecision.Approach,
            "an idle separated bot is ordered toward its nearest opponent");
        Assert(Decide(reachDistance * reachDistance)
               == TeamArenaApproachDecision.Approach,
            "an unseen target inside one metre may still be separated by Labs geometry");
        Assert(Decide(36f, visible: false, shootable: false)
               == TeamArenaApproachDecision.Approach,
            "an unseen opponent within ordinary combat range still receives an approach route");
        Assert(Decide(400f, visible: true, shootable: true)
               == TeamArenaApproachDecision.Engaged,
            "a bot with a visible shootable enemy keeps combat ownership");
        Assert(Decide(400f, visible: true, shootable: false)
               == TeamArenaApproachDecision.Approach,
            "visibility without a firing solution does not permit a distant standoff");
        Assert(Decide(400f, healing: true) == TeamArenaApproachDecision.Busy
               && Decide(400f, reloading: true) == TeamArenaApproachDecision.Busy
               && Decide(400f, throwing: true) == TeamArenaApproachDecision.Busy
               && Decide(400f, door: true) == TeamArenaApproachDecision.Busy,
            "medicine, reload, grenade and door actions are never interrupted");
        Assert(Decide(400f, immediateDanger: true)
               == TeamArenaApproachDecision.ImmediateDanger,
            "grenade, BTR, artillery and flash danger releases pursuit movement immediately");
        Assert(Decide(float.NaN) == TeamArenaApproachDecision.InvalidDistance
               && Decide(float.PositiveInfinity) == TeamArenaApproachDecision.InvalidDistance,
            "invalid world distances fail closed");

        var liveTargets = new HashSet<string>(new[] { "A", "B", "C", "D", "E" }, StringComparer.Ordinal);
        var rejectedTargets = new HashSet<string>(new[] { "A", "B", "C" }, StringComparer.Ordinal);
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        Assert(rejectedTargets.SetEquals(new[] { "A", "B", "C" }),
            "rejected pursuit targets remain excluded until every live candidate has been tried");

        rejectedTargets.Add("D");
        rejectedTargets.Add("E");
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        Assert(rejectedTargets.Count == 0,
            "a completed pursuit pass resets only after all five candidates were considered");

        rejectedTargets.Add("dead");
        TeamArenaPursuitPolicy.PrepareRejectedTargetPass(rejectedTargets, liveTargets);
        Assert(rejectedTargets.Count == 0,
            "dead pursuit identities are pruned without excluding a live target");
    }

    private static void CheckEnemyIdentityPolicy()
    {
        var player = new IdentityKey("target");
        var owner = new IdentityKey("target");
        var unrelatedEquivalent = new IdentityKey("target");
        var playerGroup = new object();
        var ownerGroup = new object();
        var playerEnemy = new object();
        var ownerEnemy = new object();
        var unrelatedEnemy = new object();

        AssertIdentityResolved(
            new Dictionary<IdentityKey, object> { [player] = playerGroup },
            new Dictionary<IdentityKey, object>(),
            player,
            owner,
            player,
            playerGroup,
            null,
            hasEnemy: false,
            "a Player-only group entry can receive its first matching memory entry");
        AssertIdentityResolved(
            new Dictionary<IdentityKey, object> { [owner] = ownerGroup },
            new Dictionary<IdentityKey, object>(),
            player,
            owner,
            owner,
            ownerGroup,
            null,
            hasEnemy: false,
            "a BotOwner-only group entry resolves the live wrapper used by PitFireTeam");
        AssertIdentityResolved(
            new Dictionary<IdentityKey, object> { [player] = playerGroup },
            new Dictionary<IdentityKey, object> { [player] = playerEnemy },
            player,
            owner,
            player,
            playerGroup,
            playerEnemy,
            hasEnemy: true,
            "a coherent Player-keyed group and memory pair is retained");
        AssertIdentityResolved(
            new Dictionary<IdentityKey, object> { [owner] = ownerGroup },
            new Dictionary<IdentityKey, object> { [owner] = ownerEnemy },
            player,
            owner,
            owner,
            ownerGroup,
            ownerEnemy,
            hasEnemy: true,
            "a coherent BotOwner-keyed group and memory pair is retained");
        AssertIdentityResolved(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object> { [owner] = ownerEnemy },
            player,
            owner,
            owner,
            ownerGroup,
            ownerEnemy,
            hasEnemy: true,
            "existing memory selects its matching BotOwner key when both group wrappers exist");
        AssertIdentityResolved(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object> { [player] = playerEnemy },
            player,
            owner,
            player,
            playerGroup,
            playerEnemy,
            hasEnemy: true,
            "existing memory selects its matching Player key when both group wrappers exist");

        AssertIdentityRejected(
            new Dictionary<IdentityKey, object> { [owner] = ownerGroup },
            new Dictionary<IdentityKey, object> { [player] = playerEnemy },
            player,
            owner,
            "a Player-memory/BotOwner-group split fails closed");
        AssertIdentityRejected(
            new Dictionary<IdentityKey, object> { [player] = playerGroup },
            new Dictionary<IdentityKey, object> { [owner] = ownerEnemy },
            player,
            owner,
            "a BotOwner-memory/Player-group split fails closed");
        AssertIdentityRejected(
            new Dictionary<IdentityKey, object>(),
            new Dictionary<IdentityKey, object>(),
            player,
            owner,
            "missing current-body keys fail closed");
        AssertIdentityRejected(
            new Dictionary<IdentityKey, object> { [player] = null! },
            new Dictionary<IdentityKey, object>(),
            player,
            owner,
            "a null group value fails closed");
        AssertIdentityRejected(
            new Dictionary<IdentityKey, object> { [unrelatedEquivalent] = playerGroup },
            new Dictionary<IdentityKey, object>(),
            player,
            owner,
            "an equal-looking stale wrapper cannot impersonate either current body object");
        AssertIdentityResolved(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            player,
            playerGroup,
            playerEnemy,
            hasEnemy: true,
            "two complete current-body pairs deterministically prefer the canonical Player wrapper");
        AssertPreferredIdentityResolved(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            playerEnemy,
            player,
            playerGroup,
            "an exact Player GoalEnemy disambiguates two complete wrapper pairs");
        AssertPreferredIdentityResolved(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            ownerEnemy,
            owner,
            ownerGroup,
            "an exact BotOwner GoalEnemy disambiguates two complete wrapper pairs");
        AssertPreferredIdentityResolved(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            unrelatedEnemy,
            player,
            playerGroup,
            playerEnemy,
            "a foreign GoalEnemy cannot strand two complete current-body pairs");
        AssertPreferredIdentityResolved(
            new Dictionary<IdentityKey, object> { [player] = playerGroup },
            new Dictionary<IdentityKey, object> { [player] = playerEnemy },
            player,
            owner,
            unrelatedEnemy,
            player,
            playerGroup,
            playerEnemy,
            "one coherent pair remains usable when the current GoalEnemy points elsewhere");
        AssertPreferredIdentityResolved(
            new Dictionary<IdentityKey, object> { [player] = playerGroup },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            ownerEnemy,
            player,
            playerGroup,
            playerEnemy,
            "a coherent Player pair wins even when GoalEnemy points to orphaned BotOwner memory");
        AssertPreferredIdentityResolved(
            new Dictionary<IdentityKey, object> { [owner] = ownerGroup },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            playerEnemy,
            owner,
            ownerGroup,
            ownerEnemy,
            "a coherent BotOwner pair wins even when GoalEnemy points to orphaned Player memory");

        bool validOwnerResolved = TeamArenaEnemyIdentityPolicy.TryResolveValid(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            preferredEnemyInfo: playerEnemy,
            groupIsValid: (_, _) => true,
            pairIsValid: (_, _, candidateEnemy) => ReferenceEquals(candidateEnemy, ownerEnemy),
            out IdentityKey validOwnerKey,
            out object validOwnerGroup,
            out object validOwnerEnemy,
            out bool validOwnerHasEnemy);
        Assert(validOwnerResolved
               && ReferenceEquals(validOwnerKey, owner)
               && ReferenceEquals(validOwnerGroup, ownerGroup)
               && ReferenceEquals(validOwnerEnemy, ownerEnemy)
               && validOwnerHasEnemy,
            "a preferred but stale Player pair cannot hide the semantically valid BotOwner pair");

        bool validPlayerResolved = TeamArenaEnemyIdentityPolicy.TryResolveValid(
            new Dictionary<IdentityKey, object>
            {
                [player] = playerGroup,
                [owner] = ownerGroup,
            },
            new Dictionary<IdentityKey, object>
            {
                [player] = playerEnemy,
                [owner] = ownerEnemy,
            },
            player,
            owner,
            preferredEnemyInfo: ownerEnemy,
            groupIsValid: (_, _) => true,
            pairIsValid: (_, _, candidateEnemy) => ReferenceEquals(candidateEnemy, playerEnemy),
            out IdentityKey validPlayerKey,
            out object validPlayerGroup,
            out object validPlayerEnemy,
            out bool validPlayerHasEnemy);
        Assert(validPlayerResolved
               && ReferenceEquals(validPlayerKey, player)
               && ReferenceEquals(validPlayerGroup, playerGroup)
               && ReferenceEquals(validPlayerEnemy, playerEnemy)
               && validPlayerHasEnemy,
            "a preferred but stale BotOwner pair cannot hide the semantically valid Player pair");

        Assert(TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(
                   new Dictionary<IdentityKey, object> { [player] = playerGroup },
                   player)
               && !TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(
                   new Dictionary<IdentityKey, object> { [unrelatedEquivalent] = playerGroup },
                   player),
            "current wrapper detection ignores an equal-looking third wrapper");

        var valueEqualPlayer = new ValueEqualIdentityKey("target");
        var valueEqualOwner = new ValueEqualIdentityKey("owner");
        var valueEqualStale = new ValueEqualIdentityKey("target");
        bool valueEqualResolved = TeamArenaEnemyIdentityPolicy.TryResolve(
            new Dictionary<ValueEqualIdentityKey, object> { [valueEqualStale] = playerGroup },
            new Dictionary<ValueEqualIdentityKey, object>(),
            valueEqualPlayer,
            valueEqualOwner,
            out _,
            out _,
            out _,
            out _);
        Assert(!valueEqualResolved
               && !TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(
                   new Dictionary<ValueEqualIdentityKey, object> { [valueEqualStale] = playerGroup },
                   valueEqualPlayer),
            "dictionary value equality cannot impersonate an exact current wrapper reference");
    }

    private static void AssertIdentityResolved(
        IReadOnlyDictionary<IdentityKey, object> group,
        IReadOnlyDictionary<IdentityKey, object> memory,
        IdentityKey player,
        IdentityKey owner,
        IdentityKey expectedKey,
        object expectedGroup,
        object? expectedEnemy,
        bool hasEnemy,
        string message)
    {
        bool resolved = TeamArenaEnemyIdentityPolicy.TryResolve(
            group,
            memory,
            player,
            owner,
            out IdentityKey actualKey,
            out object actualGroup,
            out object actualEnemy,
            out bool actualHasEnemy);
        Assert(resolved
               && ReferenceEquals(actualKey, expectedKey)
               && ReferenceEquals(actualGroup, expectedGroup)
               && ReferenceEquals(actualEnemy, expectedEnemy)
               && actualHasEnemy == hasEnemy,
            message);
    }

    private static void AssertIdentityRejected(
        IReadOnlyDictionary<IdentityKey, object> group,
        IReadOnlyDictionary<IdentityKey, object> memory,
        IdentityKey player,
        IdentityKey owner,
        string message)
    {
        bool resolved = TeamArenaEnemyIdentityPolicy.TryResolve(
            group,
            memory,
            player,
            owner,
            out IdentityKey actualKey,
            out object actualGroup,
            out object actualEnemy,
            out bool hasEnemy);
        Assert(!resolved
               && actualKey == null
               && actualGroup == null
               && actualEnemy == null
               && !hasEnemy,
            message);
    }

    private static void AssertPreferredIdentityResolved(
        IReadOnlyDictionary<IdentityKey, object> group,
        IReadOnlyDictionary<IdentityKey, object> memory,
        IdentityKey player,
        IdentityKey owner,
        object preferredEnemy,
        IdentityKey expectedKey,
        object expectedGroup,
        string message)
        => AssertPreferredIdentityResolved(
            group,
            memory,
            player,
            owner,
            preferredEnemy,
            expectedKey,
            expectedGroup,
            preferredEnemy,
            message);

    private static void AssertPreferredIdentityResolved(
        IReadOnlyDictionary<IdentityKey, object> group,
        IReadOnlyDictionary<IdentityKey, object> memory,
        IdentityKey player,
        IdentityKey owner,
        object preferredEnemy,
        IdentityKey expectedKey,
        object expectedGroup,
        object expectedEnemy,
        string message)
    {
        bool resolved = TeamArenaEnemyIdentityPolicy.TryResolve(
            group,
            memory,
            player,
            owner,
            preferredEnemy,
            out IdentityKey actualKey,
            out object actualGroup,
            out object actualEnemy,
            out bool hasEnemy);
        Assert(resolved
               && ReferenceEquals(actualKey, expectedKey)
               && ReferenceEquals(actualGroup, expectedGroup)
               && ReferenceEquals(actualEnemy, expectedEnemy)
               && hasEnemy,
            message);
    }

    private static void AssertPreferredIdentityRejected(
        IReadOnlyDictionary<IdentityKey, object> group,
        IReadOnlyDictionary<IdentityKey, object> memory,
        IdentityKey player,
        IdentityKey owner,
        object preferredEnemy,
        string message)
    {
        bool resolved = TeamArenaEnemyIdentityPolicy.TryResolve(
            group,
            memory,
            player,
            owner,
            preferredEnemy,
            out IdentityKey actualKey,
            out object actualGroup,
            out object actualEnemy,
            out bool hasEnemy);
        Assert(!resolved
               && actualKey == null
               && actualGroup == null
               && actualEnemy == null
               && !hasEnemy,
            message);
    }

    private sealed class IdentityKey
    {
        private readonly string _label;

        internal IdentityKey(string label)
            => _label = label;

        public override string ToString()
            => _label;
    }

    private sealed class ValueEqualIdentityKey
    {
        private readonly string _label;

        internal ValueEqualIdentityKey(string label)
            => _label = label;

        public override bool Equals(object? obj)
            => obj is ValueEqualIdentityKey other
               && string.Equals(_label, other._label, StringComparison.Ordinal);

        public override int GetHashCode()
            => StringComparer.Ordinal.GetHashCode(_label);
    }

    private static void CheckPursuitOwnerPolicy()
    {
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: false,
                   executionStarted: false,
                   ageSeconds: 0f)
               == TeamArenaConsumerDecision.Fallback,
            "a missing movement consumer immediately preserves the guarded fallback");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: true,
                   executionStarted: false,
                   ageSeconds: 0.5f)
               == TeamArenaConsumerDecision.AwaitingAcknowledgement,
            "a newly published generation receives a short bounded execution window");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: true,
                   executionStarted: true,
                   ageSeconds: 30f)
               == TeamArenaConsumerDecision.Active,
            "a concrete execution acknowledgement proves ownership after the startup window");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(
                   publicationAccepted: true,
                   executionStarted: false,
                   ageSeconds: TeamArenaPursuitOwnerPolicy.AcknowledgementTimeoutSeconds)
               == TeamArenaConsumerDecision.DisableAndFallback,
            "an unacknowledged registered layer times out into the fallback");
        Assert(TeamArenaPursuitOwnerPolicy.Evaluate(true, false, float.NaN)
                   == TeamArenaConsumerDecision.DisableAndFallback
               && TeamArenaPursuitOwnerPolicy.Evaluate(true, false, -0.1f)
                   == TeamArenaConsumerDecision.DisableAndFallback,
            "malformed or time-reversed acknowledgement ages fail closed into fallback");

        Assert(TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                consumerMatches: true,
                sourceReady: true,
                activeEpoch: 3,
                leaseEpoch: 3,
                leaseGeneration: 42,
                reportedEpoch: 3,
                reportedGeneration: 42),
            "an exact consumer, source, epoch and generation report is accepted");
        Assert(!TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   true, true, 3, 3, 42, 2, 42)
               && !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   true, true, 3, 3, 42, 3, 41)
               && !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   false, true, 3, 3, 42, 3, 42)
               && !TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(
                   true, false, 3, 3, 42, 3, 42),
            "stale generations, stale epochs, foreign consumers and unbound layers cannot acknowledge execution");

        Assert(TeamArenaPursuitOwnerPolicy.HasRouteProgress(9.4f, 30f, 20f)
               && !TeamArenaPursuitOwnerPolicy.HasRouteProgress(9.4f, 30f, 30.1f)
               && !TeamArenaPursuitOwnerPolicy.HasRouteProgress(0f, 30f, 20f)
               && !TeamArenaPursuitOwnerPolicy.HasRouteProgress(float.NaN, 30f, 20f)
               && !TeamArenaPursuitOwnerPolicy.HasRouteProgress(9.4f, float.PositiveInfinity, 20f),
            "route progress requires both physical movement and a finite shorter remaining NavMesh path");
        Assert(TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(2, 9.4f, 30f, 20f) == 0,
            "a long lateral NavMesh detour which shortens the route clears the stall counter");

        int slowProgress = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(0, 0.1f, 30f, 29.9f);
        slowProgress = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(
            slowProgress,
            0.2f,
            30f,
            29.8f);
        slowProgress = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(
            slowProgress,
            0.6f,
            30f,
            29.4f);
        Assert(slowProgress == 0,
            "sub-threshold movement accumulates against the retained probe baseline");

        int stalled = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(0, 0.8f, 30f, 30f);
        stalled = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(stalled, 1.2f, 30f, 30.2f);
        stalled = TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(stalled, 0.7f, 30f, 30f);
        Assert(stalled == TeamArenaPursuitOwnerPolicy.StalledWindowsBeforeRetarget
               && TeamArenaPursuitOwnerPolicy.ShouldRetarget(stalled)
               && !TeamArenaPursuitOwnerPolicy.ShouldRetarget(stalled - 1),
            "three bounded windows of circling without route progress rotate the target");
        Assert(TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(10f, 8.5f, 1.5f)
               && !TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(10.01f, 8.5f, 1.5f)
               && !TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(9f, 10f, 1.5f)
               && !TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(float.NaN, 8f, 1.5f),
            "only a finite, ordered consumer heartbeat inside the exact age boundary proves live execution");
    }

    private static void CheckSainRebindTransaction()
    {
        var staleBody = new object();
        var currentBody = new object();
        object cached = staleBody;
        bool nativeRelationIsCurrent = false;
        int removals = 0;
        int nativeRebuilds = 0;
        var order = new List<string>();

        TeamArenaSainRebindOutcome rebuilt = TeamArenaSainRebindTransaction.TryEnsureExact(
            resolveOrCreate: () =>
            {
                order.Add("resolve");
                if (cached == null && nativeRelationIsCurrent)
                    cached = currentBody;
                return cached;
            },
            isExact: candidate => ReferenceEquals(candidate, currentBody)
                                  && nativeRelationIsCurrent,
            removeStale: () =>
            {
                order.Add("remove");
                removals++;
                cached = null;
                nativeRelationIsCurrent = false;
            },
            rebuildExactNative: () =>
            {
                order.Add("rebuild-native");
                nativeRebuilds++;
                nativeRelationIsCurrent = true;
                return true;
            },
            out object exactEnemy);
        Assert(rebuilt == TeamArenaSainRebindOutcome.ExactRebuilt
               && ReferenceEquals(exactEnemy, currentBody)
               && removals == 1
               && nativeRebuilds == 1,
            "a stale same-profile SAIN enemy is replaced by the current-round body");
        Assert(order.SequenceEqual(new[] { "resolve", "remove", "rebuild-native", "resolve" }),
            "SAIN stale removal is immediately followed by native rebuild before wrapper recreation");

        int exactRemovals = 0;
        int exactRebuilds = 0;
        TeamArenaSainRebindOutcome existing = TeamArenaSainRebindTransaction.TryEnsureExact(
            resolveOrCreate: () => currentBody,
            isExact: candidate => ReferenceEquals(candidate, currentBody),
            removeStale: () => exactRemovals++,
            rebuildExactNative: () =>
            {
                exactRebuilds++;
                return true;
            },
            out exactEnemy);
        Assert(existing == TeamArenaSainRebindOutcome.ExactExisting
               && ReferenceEquals(exactEnemy, currentBody)
               && exactRemovals == 0
               && exactRebuilds == 0,
            "an exact current-round SAIN wrapper is never removed or rebuilt");

        int failedResolves = 0;
        TeamArenaSainRebindOutcome failed = TeamArenaSainRebindTransaction.TryEnsureExact(
            resolveOrCreate: () =>
            {
                failedResolves++;
                return staleBody;
            },
            isExact: candidate => false,
            removeStale: () => { },
            rebuildExactNative: () => false,
            out exactEnemy);
        Assert(failed == TeamArenaSainRebindOutcome.NativeRebuildFailed
               && exactEnemy == null
               && failedResolves == 1,
            "a failed native rebuild fails closed before SAIN can recreate another wrapper");

        int mismatchResolves = 0;
        int mismatchRemovals = 0;
        int mismatchRebuilds = 0;
        TeamArenaSainRebindOutcome mismatch = TeamArenaSainRebindTransaction.TryEnsureExact(
            resolveOrCreate: () =>
            {
                mismatchResolves++;
                return staleBody;
            },
            isExact: candidate => false,
            removeStale: () => mismatchRemovals++,
            rebuildExactNative: () =>
            {
                mismatchRebuilds++;
                return true;
            },
            out exactEnemy);
        Assert(mismatch == TeamArenaSainRebindOutcome.RebuiltEnemyMismatch
               && exactEnemy == null
               && mismatchResolves == 2
               && mismatchRemovals == 2
               && mismatchRebuilds == 2,
            "a second stale wrapper is removed, native awareness is restored, and success is withheld");

        int missingMutations = 0;
        TeamArenaSainRebindOutcome missing = TeamArenaSainRebindTransaction.TryEnsureExact<object>(
            resolveOrCreate: () => null,
            isExact: candidate => true,
            removeStale: () => missingMutations++,
            rebuildExactNative: () =>
            {
                missingMutations++;
                return true;
            },
            out exactEnemy);
        Assert(missing == TeamArenaSainRebindOutcome.Missing
               && exactEnemy == null
               && missingMutations == 0,
            "a missing optional SAIN enemy performs no destructive repair");
    }

    private static void CheckSainSourcePolicy()
    {
        Assert(TeamArenaSainSourcePolicy.Select(
                   registryFound: true,
                   registryReturnedWrapper: true,
                   registryWrapperExact: true,
                   attachedWrapperPresent: true,
                   attachedWrapperExact: true,
                   registeredAndAttachedSame: true)
               == TeamArenaSainSourceSelection.RegisteredExact,
            "an exact profile-registry SAIN wrapper remains authoritative");
        Assert(TeamArenaSainSourcePolicy.Select(true, true, true, false, false, false)
               == TeamArenaSainSourceSelection.RegisteredExact,
            "an exact registered SAIN wrapper remains usable when no attached wrapper is exposed");
        Assert(TeamArenaSainSourcePolicy.Select(true, true, true, true, true, false)
               == TeamArenaSainSourceSelection.Incompatible,
            "distinct exact-looking registered and attached wrappers fail closed");
        Assert(TeamArenaSainSourcePolicy.Select(true, true, false, true, true, false)
               == TeamArenaSainSourceSelection.Incompatible,
            "an exact attached component cannot conceal a stale registered SAIN wrapper used by patches");
        Assert(TeamArenaSainSourcePolicy.Select(true, false, false, true, true, false)
               == TeamArenaSainSourceSelection.Incompatible,
            "GetSAIN true with a null wrapper fails closed even when a component is attached");
        Assert(TeamArenaSainSourcePolicy.Select(false, false, false, true, true, false)
               == TeamArenaSainSourceSelection.AttachedExact,
            "an exact attached component is usable only after a clean registry miss");
        Assert(TeamArenaSainSourcePolicy.Select(false, false, false, false, false, false)
               == TeamArenaSainSourceSelection.NotPresent,
            "a clean registry miss without an attached component means SAIN is absent for the bot");
        Assert(TeamArenaSainSourcePolicy.Select(false, true, true, false, false, false)
               == TeamArenaSainSourceSelection.Incompatible
               && TeamArenaSainSourcePolicy.Select(false, false, false, true, false, false)
                  == TeamArenaSainSourceSelection.Incompatible,
            "contradictory registry output and a stale attached component both fail closed");
    }

    private static void CheckSainAssemblyPolicy()
    {
        Assert(TeamArenaSainAssemblyPolicy.Select(
                   pluginMetadataPresent: false,
                   assemblyLoaded: false)
               == TeamArenaSainAssemblyState.ConfirmedAbsent,
            "SAIN is optional only when both plugin metadata and its assembly are absent");
        Assert(TeamArenaSainAssemblyPolicy.Select(
                   pluginMetadataPresent: true,
                   assemblyLoaded: false)
               == TeamArenaSainAssemblyState.InstalledPending,
            "installed SAIN without a loaded assembly remains pending instead of allowing movement");
        Assert(TeamArenaSainAssemblyPolicy.Select(
                   pluginMetadataPresent: false,
                   assemblyLoaded: true)
               == TeamArenaSainAssemblyState.Loaded
               && TeamArenaSainAssemblyPolicy.Select(
                   pluginMetadataPresent: true,
                   assemblyLoaded: true)
               == TeamArenaSainAssemblyState.Loaded,
            "a loaded SAIN assembly takes precedence over plugin metadata timing");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string service = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorService.cs");
        string healthEffects = Read(root, "ArenaMode.Client", "Spectator", "SpectatorHealthEffects.cs");
        string cameraPatch = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorCameraPatches.cs");
        string inputPatch = Read(root, "ArenaMode.Client", "Spectator", "ArenaSpectatorInputPatch.cs");
        string killcam = Read(root, "ArenaMode.Client", "Spectator", "KillcamReplayCompat.cs");
        string freecam = Read(root, "ArenaMode.Client", "Spectator", "TerkoizFreecamProbe.cs");
        string plugin = Read(root, "ArenaMode.Client", "Plugin.cs");
        string controller = Read(root, "ArenaMode.Client", "ArenaController.cs");
        string project = Read(root, "ArenaMode.Client", "ArenaMode.Client.csproj");
        string pursuit = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuit.cs");
        string sainRebind = Read(root, "ArenaMode.Client", "Bots", "TeamArenaSainRebindTransaction.cs");
        string pursuitBridge = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuitLeaseBridge.cs");
        string pursuitOwnerPolicy = Read(root, "ArenaMode.Client", "Bots", "TeamArenaPursuitOwnerPolicy.cs");
        string sainSourcePolicy = Read(root, "ArenaMode.Client", "Bots", "TeamArenaSainSourcePolicy.cs");
        string bigBrainProject = Read(root, "ArenaMode.BigBrainCompat", "ArenaMode.BigBrainCompat.csproj");
        string bigBrainPlugin = Read(root, "ArenaMode.BigBrainCompat", "Plugin.cs");
        string bigBrainLayer = Read(root, "ArenaMode.BigBrainCompat", "ArenaPursuitLayer.cs");
        string bigBrainLogic = Read(root, "ArenaMode.BigBrainCompat", "ArenaPursuitLogic.cs");
        string bigBrainRuntime = Read(root, "ArenaMode.BigBrainCompat", "ArenaPursuitRuntime.cs");
        string sainHandoff = Read(root, "ArenaMode.BigBrainCompat", "ArenaSainMovementHandoff.cs");
        string burst = Read(root, "ArenaMode.Client", "Bots", "BurstSpawnService.cs");
        string settings = Read(root, "ArenaMode.Client", "Config", "Settings.cs");
        string menuSelection = Read(root, "ArenaMode.Client", "Session", "ArenaMenuSelection.cs");
        string tactical = Read(root, "ArenaMode.Client", "Modes", "TacticalMode.cs");
        string match = Read(root, "ArenaMode.Client", "Modes", "ArenaMatch.cs");
        string announcer = Read(root, "ArenaMode.Client", "Audio", "AnnouncerService.cs");
        string audioLicenses = Read(root, "ArenaMode.Client", "Assets", "ArenaMode", "AUDIO-LICENSES.md");
        string combat = Read(root, "ArenaMode.Client", "Combat", "CombatPatches.cs");
        string respawn = Read(root, "ArenaMode.Client", "Respawn", "ArenaRespawnDriver.cs");
        string matchService = Read(root, "ArenaMode.Client", "Modes", "ArenaMatchService.cs");

        RequireAll(service,
            "private static void TickUnsafe()",
            "Arena spectator tick failed closed",
            "Application.onBeforeRender -= ApplyRenderPose",
            "Application.onBeforeRender += ApplyRenderPose",
            "ArenaInputBlocker.Request(InputOwner, true)",
            "ArenaInputBlocker.Request(InputOwner, false)",
            "InitiateOperation<LockCameraState>()",
            "InitiateOperation<FirstPersonCameraState>()",
            "PitFireTeamCompat.TeamArenaActive",
            "request.ReadEpoch() != request.Epoch",
            "TryFreezeAllowedProfileIds",
            "ReferenceEquals(controller.Player, _request.DeadPlayer)",
            "Stop(\"the local player became alive again\")",
            "returnOwnedCameraToNative",
            "OptionalOwnersPermitNativeRestore()",
            "HasBoundCameraController(playerToRestore)",
            "&& optionalOwnersPermitRestore",
            "RestoreControllerOperation(playerToRestore, restoreFirstPerson)",
            "FrozenBodiesByProfileId.TryGetValue",
            "!ReferenceEquals(frozenBody, player)",
            "SeenProfiles.SetEquals(AllowedProfileIds)",
            "supplied.Count != SpectatorPolicy.RequiredNonLocalTeamActors");
        Assert(service.IndexOf("_ownsCamera = false", StringComparison.Ordinal)
               < service.IndexOf("Application.onBeforeRender -= ApplyRenderPose", StringComparison.Ordinal),
            "synchronous teardown drops patch ownership before releasing callbacks");
        RequireAll(healthEffects,
            "!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive",
            "expectedDeadPlayer.HealthController?.IsAlive != false",
            "ReferenceEquals(BoundPlayerField.GetValue(controller), expectedDeadPlayer)",
            "List<EffectsController.EffectAccumulator>",
            "accumulator.DeleteEffect(accumulator.ActiveEffects[0])",
            "accumulator.ActiveEffects.Count >= before",
            "wiggle._stimulatorBuffs?.Clear()",
            "motionBlur._stimulatorBuffs?.Clear()",
            "flash._indoorEffects?.Clear()",
            "Shader.SetGlobalFloat(\"_BlindnessCoef\", 0f)",
            "blend.Toggle(false)",
            "frostbite._effect.Radius = 1f",
            "sharpen.HealthDesaturate = 0f",
            "sharpen.MaskDesaturate = 0f",
            "provider.HealthDesaturate = 0f",
            "eyeBurn.enabled = false",
            "drops._freeDrops.Enqueue(drops._dropsToRender.Dequeue())",
            "ClearRenderTexture(BloodTextureField.GetValue(blood) as RenderTexture)",
            "LastBloodHitField.SetValue(blood, float.MinValue)",
            "LastHeavyBleedHitField.SetValue(blood, float.MinValue)",
            "BloodLockedField.SetValue(blood, false)",
            "GL.Clear(clearDepth: true, clearColor: true, Color.black)",
            "DeathEffectsHelper.ClearDeathScreenEffects()");
        RequireAll(healthEffects,
            "_automaticAttempts >= 2",
            "_automaticAttempts++",
            "if (!force)",
            "allocation-free for the rest");
        Assert(!healthEffects.Contains("RemoveNegativeEffects", StringComparison.Ordinal)
               && !healthEffects.Contains("PauseAllEffects", StringComparison.Ordinal)
               && !healthEffects.Contains(".ActiveEffects.Clear(", StringComparison.Ordinal)
               && !healthEffects.Contains("Object.Destroy", StringComparison.Ordinal)
               && !healthEffects.Contains("Destroy(controller", StringComparison.Ordinal),
            "spectator cleanup changes only first-person presentation and neither health state nor persistent camera components");
        string requestStart = Slice(
            service,
            "internal static bool RequestStart(",
            "internal static void Tick()");
        Assert(requestStart.IndexOf("Stop(\"superseded spectator request\")", StringComparison.Ordinal)
               < requestStart.IndexOf("SpectatorHealthEffects.ClearForDeadLocal(request.DeadPlayer)", StringComparison.Ordinal)
               && requestStart.IndexOf("SpectatorHealthEffects.ClearForDeadLocal(request.DeadPlayer)", StringComparison.Ordinal)
               < requestStart.IndexOf("Arena spectator pending for epoch", StringComparison.Ordinal),
            "an accepted exact-body lease clears health visuals before Killcam's delayed playback can begin");
        string spectatorTick = Slice(
            service,
            "private static void TickUnsafe()",
            "internal static void Stop(string reason)");
        RequireAll(spectatorTick,
            "SpectatorHealthEffects.ClearForDeadLocal(_request.DeadPlayer)");
        string startFollowing = Slice(
            service,
            "private static void StartFollowing()",
            "private static bool TryValidateLease(");
        RequireAll(startFollowing,
            "DeathEffectsHelper.ClearDeathScreenEffects()",
            "SpectatorHealthEffects.ClearForDeadLocal(_request.DeadPlayer, force: true)");

        RequireAll(cameraPatch,
            "nameof(CameraManager.ForceSetPosition)",
            "!ArenaSession.IsActive",
            "!PitFireTeamCompat.TeamArenaActive",
            "!ArenaSpectatorService.OwnsCamera");
        RequireAll(inputPatch,
            "AccessTools.DeclaredMethod(",
            "typeof(EftGamePlayerOwner)",
            "nameof(EftGamePlayerOwner.TranslateCommand)",
            "new[] { typeof(ECommand) }",
            "ArenaSpectatorService.TryTranslateEscape(__instance, __0",
            "ref InputNode.ETranslateResult __result");
        Assert(!inputPatch.Contains("CanTranslateCommand", StringComparison.Ordinal)
               && !inputPatch.Contains("Input.GetKeyDown", StringComparison.Ordinal)
               && !inputPatch.Contains("LocalGame.Stop", StringComparison.Ordinal),
            "spectator Escape uses the native menu translator without widening dead-body input or bypassing confirmation");
        int inputGate = inputPatch.IndexOf(
            "if (!ArenaSpectatorService.TryTranslateEscape(__instance, __0", StringComparison.Ordinal);
        int inputResult = inputPatch.IndexOf("__result = translated", StringComparison.Ordinal);
        int skipOriginal = inputPatch.IndexOf("return false", inputResult, StringComparison.Ordinal);
        Assert(inputGate >= 0 && inputResult > inputGate && skipOriginal > inputResult,
            "the exact owner original is skipped only after the spectator service handles Escape");
        string escapeRuntime = Slice(
            service,
            "internal static bool TryTranslateEscape(",
            "internal static string CurrentStatusLine");
        RequireAll(escapeRuntime,
            "command != ECommand.Escape",
            "!_ownsCamera",
            "!ArenaSession.IsActive",
            "!PitFireTeamCompat.TeamArenaActive",
            "request.ReadEpoch() == request.Epoch",
            "request.IsEligible()",
            "CheckCurrentScreen(EEftScreenType.BattleUI)",
            "ReferenceEquals(expectedBody, null)",
            "expectedBody.HealthController?.IsAlive == false",
            "owner.TranslateExitScreenInput(command)",
            "result = InputNode.ETranslateResult.BlockAll");
        RequireAll(killcam,
            "PendingReplayKiller",
            "IsPlaying",
            "ReadyForStop",
            "GetExtraDelay",
            "BindingFlags.Public");
        RequireAll(freecam,
            "com.terkoiz.freecam",
            "IsFreecamActive",
            "FindObjectOfType",
            "MissingControllerProbeIntervalSeconds",
            "Time.unscaledTime < _nextControllerProbeAt",
            "_activeProperty.GetValue(_controller)");
        Assert(CountOf(freecam, "FindObjectOfType") == 1,
            "the optional Freecam controller has one throttled scene-scan site rather than a per-frame search");
        RequireAll(plugin,
            "ArenaSpectatorService.Initialize()",
            "ArenaSpectatorCameraPatches.Enable()",
            "ArenaSpectatorInputPatch.Enable()",
            "AddComponent<Spectator.ArenaSpectatorOverlay>()",
            "ArenaSpectatorService.Shutdown()");
        RequireAll(controller, "ArenaSpectatorService.Tick()");

        Assert(!project.Contains("KillcamReplay", StringComparison.Ordinal)
               && !project.Contains("Terkoiz", StringComparison.Ordinal)
               && !project.Contains("Fika", StringComparison.Ordinal),
            "the client project has no hard camera-mod dependency");

        RequireAll(match,
            "internal event Action<ArenaAnnouncerCue> OnAnnouncerCue",
            "internal void ReportAnnouncerCue(ArenaAnnouncerCue cue)",
            "OnAnnouncerCue?.Invoke(cue)");
        RequireAll(announcer,
            "Array.Sort(files, CompareLoadOrder)",
            "string.Equals(key, \"round_start\"",
            "match.OnAnnouncerCue += OnAnnouncerCue",
            "_match.OnAnnouncerCue -= OnAnnouncerCue",
            "ArenaAnnouncerCue.RoundStart => \"round_start\"",
            "ArenaAnnouncerCue.RoundWon => \"round_won\"",
            "ArenaAnnouncerCue.RoundLost => \"round_lost\"",
            "audio.PlayNonspatial(",
            "BetterAudio.AudioSourceGroupType.Nonspatial",
            "route = \"BetterAudio.NonspatialPool\"",
            "Singleton<GUISounds>.Instance.PlaySound(",
            "single: true",
            "\"GUISounds.UI<-BetterAudioFailure\"",
            "fallbackReason == null ? \"output_unavailable\" : \"play_failed\"");
        Assert(!announcer.Contains("BetterAudio.AudioSourceGroupType.NonspatialBypass", StringComparison.Ordinal),
            "rapid announcer cues cannot share BetterAudio's single NonspatialBypass source");
        Assert(!announcer.Contains("NonSpatialBypassMixer", StringComparison.Ordinal)
               && !announcer.Contains("forcedGroup", StringComparison.Ordinal),
            "Arena cannot leave a foreign mixer on an EFT-owned pooled source");
        Assert(!announcer.Contains("AddComponent<AudioSource>", StringComparison.Ordinal)
               && !announcer.Contains("internal static void Tick()", StringComparison.Ordinal)
               && !announcer.Contains("output.isVirtual", StringComparison.Ordinal)
               && !announcer.Contains("_outputRoute", StringComparison.Ordinal),
            "announcer audio stays on EFT-owned sources and needs no private channel lifecycle");
        int announcerAttach = matchService.IndexOf("Audio.AnnouncerService.Attach(match)", StringComparison.Ordinal);
        int modeStart = matchService.IndexOf("mode.OnMatchStart(match)", StringComparison.Ordinal);
        Assert(announcerAttach >= 0 && modeStart >= 0 && announcerAttach < modeStart,
            "announcer subscribes before a mode can synchronously emit its first cue");
        Assert(!controller.Contains("Audio.AnnouncerService.Tick()", StringComparison.Ordinal),
            "controller has no obsolete per-frame private announcer-source probe");
        RequireAll(plugin, "Audio.AnnouncerService.SetPluginDirectory");
        Assert(!plugin.Contains("AnnouncerService.EnsureLoaded()", StringComparison.Ordinal),
            "an ordinary-only process does not preload Arena audio at plugin startup");
        RequireAll(menuSelection,
            "IsSelected = true",
            "Audio.AnnouncerService.EnsureLoaded()");
        Assert(menuSelection.IndexOf("IsSelected = true", StringComparison.Ordinal)
               < menuSelection.IndexOf("Audio.AnnouncerService.EnsureLoaded()", StringComparison.Ordinal),
            "selecting Arena starts announcer preload before map and raid loading");
        RequireAll(audioLicenses,
            "VoiceBosch WARLORD round cues",
            "Creative Commons Attribution-ShareAlike 4.0",
            "CC-BY-SA-4.0.txt",
            "sounds/round_start.ogg",
            "sounds/round_won.ogg",
            "sounds/round_lost.ogg");
        foreach (string asset in new[]
                 {
                     "round_start.ogg",
                     "round_won.ogg",
                     "round_lost.ogg",
                 })
        {
            Assert(File.Exists(Path.Combine(
                    root, "ArenaMode.Client", "Assets", "ArenaMode", "sounds", asset)),
                $"the licensed Tactical lifecycle asset is present: {asset}");
        }
        Assert(File.Exists(Path.Combine(
                root, "ArenaMode.Client", "Assets", "ArenaMode", "CC-BY-SA-4.0.txt")),
            "the WARLORD assets retain their full CC BY-SA 4.0 license text");

        RequireAll(pursuit,
            "private const float SweepSeconds = 1f",
            "private const float RouteProbeSeconds = 4f",
            "public static void Reset()",
            "public static void BeginRound(int epoch)",
            "TeamArenaPursuitLeaseBridge.Reset()",
            "TeamArenaPursuitLeaseBridge.BeginEpoch(epoch)",
            "TeamArenaPursuitLeaseBridge.Publish(",
            "TeamArenaPursuitLeaseBridge.TryConsumeRejection(",
            "TeamArenaPursuitOwnerPolicy.Evaluate(",
            "TeamArenaPursuitLeaseBridge.HasStarted(",
            "TeamArenaPursuitLeaseBridge.Revoke(sourceProfile, epoch)",
            "\"bigbrain\"",
            "!localPlayerDown",
            "epoch != _epoch",
            "!ArenaSession.IsActive",
            "!PitFireTeamCompat.TeamArenaActive",
            "Time.time < _nextSweepAt",
            "candidate.BotState != EBotState.Active",
            "candidate.HealthController?.IsAlive != true",
            "body.HealthController?.IsAlive != true",
            "string.Equals(body.ProfileId?.Trim(), profileId, StringComparison.Ordinal)",
            "ReferenceEquals(body.AIData?.BotOwner, candidate)",
            "!IsFinite(candidate.Position)",
            "!profiles.Add(profileId)",
            "RefreshSafely(ally, liveOpponents, epoch)",
            "RefreshSafely(opponent, liveAllies, epoch)",
            "RefreshNative(source, nearest, out enemyInfo)",
            "SelectEngagedTarget(source, candidates)",
            "SelectRetainedTarget(sourceProfile, candidates, rejectedTargets)",
            "PrepareRejectedTargetPass(rejectedTargets, liveTargets)",
            "current?.IsVisible == true && current.CanShoot == true",
            "IsCurrentTargetPerson(current.Person, candidate)",
            "TryResolveRegisteredEnemy(source, candidate, out EnemyInfo registered)",
            "ReferenceEquals(current, registered)",
            "TeamArenaEnemyIdentityPolicy.TryResolveValid(",
            "group.Enemies,",
            "source.EnemiesController.EnemyInfos,",
            "targetPlayer,",
            "target,",
            "source.Memory?.GoalEnemy,",
            "groupIsValid: (_, candidateGroup) =>",
            "pairIsValid: (candidateKey, candidateGroup, candidateEnemy) =>",
            "ReferenceEquals(candidateGroup.Player, targetPlayer)",
            "ReferenceEquals(candidateEnemy?.Person, candidateKey)",
            "ReferenceEquals(candidateEnemy.GroupInfo, candidateGroup)",
            "ReferenceEquals(candidateEnemy.Owner, source)",
            "ReferenceEquals(candidateEnemy.GroupOwner, group)",
            "enemy?.IsVisible != true",
            "distance < bestDistance",
            "string.CompareOrdinal(profile, bestProfile) < 0",
            "TeamArenaEnemyIdentityPolicy.ContainsReferenceKey(",
            "bool hasCanonicalPlayerGroupKey",
            "if (!hasCanonicalPlayerGroupKey)",
            "group.AddEnemy(playerKey, EBotEnemyCause.bossKillArena)",
            "TryResolveCanonicalPlayerEnemy(",
            "targetPlayer,\n            targetPlayer,",
            "out IPlayer enemyKey",
            "group.ReportAboutEnemy(playerKey, EEnemyPartVisibleType.Sence, source)",
            "source.Memory.AddEnemy(playerKey, groupInfo, onActivation: false)",
            "ReferenceEquals(enemyInfo.Person, playerKey)",
            "groupInfo.IsLastPositionChecked = false",
            "source.Memory.GoalEnemy = enemyInfo",
            "TeamArenaPursuitPolicy.Decide(",
            "IsMedicalActionActive(source)",
            "source.DoorOpener?.Interacting == true",
            "source.StandBy?.Activate()",
            "HasImmediateDanger(source)",
            "out Vector3 destination",
            "source.Mover.PositionOnWay",
            "target.Mover.PositionOnWay",
            "sourceMoverMatchesBody",
            "MoverAnchorToleranceMeters * MoverAnchorToleranceMeters",
            "NavMesh.SamplePosition(",
            "Mathf.Abs(hit.position.y - requested.y) <= AnchorHeightToleranceMeters",
            "BodyAnchorSampleRadius",
            "NavMesh.CalculatePath(",
            "RoutePath.status != NavMeshPathStatus.PathComplete",
            "Vector3[] corners = RoutePath.corners",
            "routeDistanceMeters += Vector3.Distance",
            "TryCalculateRouteDistance(",
            "probe.SourcePosition,",
            "source.Position,",
            "destination,",
            "out float previousRouteDistanceMeters",
            "out float currentRouteDistanceMeters",
            "TeamArenaPursuitOwnerPolicy.HasRouteProgress(",
            "TeamArenaPursuitOwnerPolicy.NextStalledWindowCount(",
            "TeamArenaPursuitOwnerPolicy.ShouldRetarget(stalledWindows)",
            "probe.SourcePosition",
            "RejectTargetForCurrentPass(source, target, epoch)",
            "TryRefreshSain(",
            "ref enemyInfo",
            "if (!GoalEnemyMatches(source, nearest, enemyInfo))",
            "ReferenceEquals(expectedNativeEnemy.Person, targetPerson)",
            "TeamArenaSainSourceState sourceState = TryGetExactSainBot(",
            "TeamArenaSainSourceState.NotPresent",
            "TeamArenaSainSourceState.Incompatible",
            "if (!_sainBridgeResolved)",
            "Chainloader.PluginInfos.ContainsKey(SainPluginGuid)",
            "TeamArenaSainAssemblyPolicy.Select(",
            "source.GetComponent(_sainBotType)",
            "TeamArenaSainSourcePolicy.Select(",
            "IsExactSainSource(getArgs[1], source)",
            "ReferenceEquals(getArgs[1], attached)",
            "ReferenceEquals(_sainBotOwner.GetValue(sainBot), source)",
            "ReferenceEquals(_sainPlayer.GetValue(sainBot), source.GetPlayer)",
            "TeamArenaSainRebindTransaction.TryEnsureExact(",
            "_sainRemoveEnemy.Invoke(",
            "RefreshNative(source, targetOwner, out EnemyInfo rebuiltNativeEnemy)",
            "ReferenceEquals(_sainEnemyInfo.GetValue(candidate), currentNativeEnemy)",
            "ReferenceEquals(_sainEnemyPlayer.GetValue(candidate), targetOwner.GetPlayer)",
            "UpdateLastSeenPosition",
            "ReferenceEquals(_sainGoalEnemy.GetValue(enemyController), enemy)");

        int previousRouteToCurrentDestination = pursuit.IndexOf(
            "probe.SourcePosition,\n                destination,\n                out float previousRouteDistanceMeters",
            StringComparison.Ordinal);
        int currentRouteToCurrentDestination = pursuit.IndexOf(
            "source.Position,\n                destination,\n                out float currentRouteDistanceMeters",
            StringComparison.Ordinal);
        Assert(previousRouteToCurrentDestination >= 0
               && currentRouteToCurrentDestination > previousRouteToCurrentDestination,
            "stall detection compares old and current source positions against the same current target destination");

        Assert(!pursuit.Contains("TrySyncEnemyState", StringComparison.Ordinal)
               && !pursuit.Contains("TryRefreshPft(", StringComparison.Ordinal)
               && !pursuit.Contains("SetPushEnemy", StringComparison.Ordinal)
               && !pursuit.Contains("SetCombatMoveToPointTactical", StringComparison.Ordinal)
               && !pursuit.Contains("TryMoveSain", StringComparison.Ordinal)
               && !pursuit.Contains("HandleSainMovement", StringComparison.Ordinal)
               && !pursuit.Contains("RunToPoint", StringComparison.Ordinal)
               && !pursuit.Contains("SetInCombat", StringComparison.Ordinal)
               && !pursuit.Contains(".TryMoveToEnemy(", StringComparison.Ordinal)
               && !pursuit.Contains("BigBrainUnavailableSources", StringComparison.Ordinal),
            "the manager publishes one exclusive route owner and never starts competing PFT, SAIN or native movement");

        RequireAll(sainRebind,
            "TeamArenaSainRebindOutcome.NativeRebuildFailed",
            "TeamArenaSainRebindOutcome.RebuiltEnemyMismatch",
            "TEnemy candidate = resolveOrCreate()",
            "removeStale()",
            "if (!rebuildExactNative())",
            "candidate = resolveOrCreate()",
            "if (candidate == null)",
            "if (!isExact(candidate))");
        RequireAll(sainSourcePolicy,
            "if (registryFound)",
            "!registryReturnedWrapper",
            "!registryWrapperExact",
            "attachedWrapperPresent",
            "!attachedWrapperExact || !registeredAndAttachedSame",
            "TeamArenaSainSourceSelection.RegisteredExact",
            "if (registryReturnedWrapper)",
            "TeamArenaSainSourceSelection.AttachedExact",
            "registeredAndAttachedSame",
            "TeamArenaSainSourceSelection.Incompatible");

        Assert(!pursuit.Contains("TryResolvePlayerEntry", StringComparison.Ordinal)
               && !pursuit.Contains(
                   "string.Equals(candidate.ProfileId, currentProfile",
                   StringComparison.Ordinal),
            "pursuit never resolves a current body through a profile-only first match");

        string pursuitMedical = Slice(
            pursuit,
            "private static bool IsMedicalActionActive(BotOwner source)",
            "private static void LogRouteOnce(");
        RequireAll(pursuitMedical,
            "source?.Medecine?.FirstAid?.Using == true",
            "source?.Medecine?.SurgicalKit?.Using == true",
            "source?.Medecine?.Stimulators?.Using == true");
        Assert(!pursuitMedical.Contains(".Have2Do == true", StringComparison.Ordinal)
               && !pursuitMedical.Contains(".HaveWork == true", StringComparison.Ordinal)
               && !pursuitMedical.Contains("Medecine.Using", StringComparison.Ordinal)
               && !pursuitMedical.Contains("LastResult()", StringComparison.Ordinal)
               && !pursuitMedical.Contains("BotLogicDecision.heal", StringComparison.Ordinal),
            "pending, aggregate or decision-only medical state cannot hold a wounded bot in place");

        Assert(!System.Text.RegularExpressions.Regex.IsMatch(
                   pursuit,
                   @"private\s+static(?:\s+readonly)?\s+[^;\r\n]*\bBotOwner\b[^;\r\n]*;")
               && !pursuit.Contains("source.GoToPoint", StringComparison.Ordinal)
               && !pursuit.Contains("mustHaveWay: true", StringComparison.Ordinal)
               && !pursuit.Contains("DoorOpener?.NearDoor", StringComparison.Ordinal)
               && !pursuit.Contains("Teleport", StringComparison.Ordinal)
               && !pursuit.Contains("LookSensor", StringComparison.Ordinal)
               && !pursuit.Contains("SetVisible", StringComparison.Ordinal)
               && !pursuit.Contains("Vector3.Dot", StringComparison.Ordinal),
            "post-death pursuit retains no bot body, fakes no sight, and accepts lateral NavMesh detours");

        Assert(pursuit.IndexOf("TeamArenaPursuitLeaseBridge.TryConsumeRejection(", StringComparison.Ordinal)
                   < pursuit.IndexOf("TeamArenaPursuitLeaseBridge.Publish(", StringComparison.Ordinal),
            "a rejected consumer generation is handled before the source can publish another lease");
        int routeValidation = pursuit.IndexOf(
            "TryBuildRouteDestination(",
            StringComparison.Ordinal);
        int routePublication = pursuit.IndexOf(
            "TeamArenaPursuitLeaseBridge.Publish(",
            routeValidation,
            StringComparison.Ordinal);
        int destinationArgument = pursuit.IndexOf(
            "destination,",
            routePublication,
            StringComparison.Ordinal);
        Assert(routeValidation >= 0
               && routePublication > routeValidation
               && destinationArgument > routePublication,
            "only a complete sampled NavMesh destination is published to the movement owner");

        RequireAll(pursuitOwnerPolicy,
            "AcknowledgementTimeoutSeconds = 2f",
            "ProgressMeters = 0.5f",
            "StalledWindowsBeforeRetarget = 3",
            "TeamArenaConsumerDecision.AwaitingAcknowledgement",
            "TeamArenaConsumerDecision.DisableAndFallback",
            "CanAcceptConsumerReport(",
            "leaseGeneration == reportedGeneration",
            "HasRouteProgress(",
            "NextStalledWindowCount(",
            "ShouldRetarget(int stalledWindows)",
            "IsFreshHeartbeat(");

        RequireAll(pursuitBridge,
            "RegisterConsumer(string consumerId)",
            "UnregisterConsumer(string consumerId)",
            "RegisterSource(string consumerId, string sourceProfileId)",
            "UnregisterSource(string consumerId, string sourceProfileId)",
            "public static bool HasExactTeamArenaContext()",
            "ReadySourceRegistrations.ContainsKey(source)",
            "public static void BeginEpoch(int epoch)",
            "public static bool Publish(",
            "MaximumLeaseLifetimeSeconds",
            "MaximumHeartbeatAgeSeconds = 1.5f",
            "public float IssuedAt { get; }",
            "public static bool ReportStarted(",
            "public static bool ReportRejected(",
            "TeamArenaPursuitOwnerPolicy.CanAcceptConsumerReport(",
            "TeamArenaPursuitOwnerPolicy.IsFreshHeartbeat(",
            "internal static bool HasStarted(",
            "internal static bool TryConsumeRejection(",
            "public static bool TryGet(",
            "public static bool Revoke(",
            "HasExactPostDeathContext()",
            "ArenaSession.IsExactActiveRaid(game, world)",
            "PitFireTeamCompat.TeamArenaActive",
            "world.MainPlayer?.HealthController?.IsAlive == false",
            "current.Generation != generation");
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(
                   pursuitBridge,
                   @"private\s+static(?:\s+readonly)?\s+[^;\r\n]*(?:BotOwner|Player)[^;\r\n]*;")
               && !pursuitBridge.Contains("Dictionary<string, BotOwner>", StringComparison.Ordinal)
               && !pursuitBridge.Contains("Dictionary<string, Player>", StringComparison.Ordinal),
            "the cross-assembly movement lease retains identities and values rather than bot bodies");

        RequireAll(bigBrainProject,
            "<ProjectReference Include=\"..\\ArenaMode.Client\\ArenaMode.Client.csproj\" Private=\"false\" />",
            "<Reference Include=\"DrakiaXYZ-BigBrain\">",
            "<Reference Include=\"UnityEngine.AIModule\" Private=\"false\" />",
            "<Private>false</Private>");
        RequireAll(bigBrainPlugin,
            "[BepInDependency(ArenaMode.Plugin.PluginGuid, ArenaMode.Plugin.PluginVersion)]",
            "[BepInDependency(BigBrainPluginGuid, SupportedBigBrainVersion)]",
            "SupportedBigBrainVersion = \"1.5.0\"",
            "PursuitLayerPriority = 74",
            "BrainManager.AddCustomLayer(",
            "BrainManager.CustomLayersReadOnly.TryGetValue",
            "TeamArenaPursuitLeaseBridge.RegisterConsumer(PluginGuid)",
            "TeamArenaPursuitLeaseBridge.UnregisterConsumer(PluginGuid)");
        RequireAll(bigBrainLayer,
            "public override string GetName() => \"Arena Combat Pursuit\"",
            "ArenaPursuitRuntime.HasExactTeamArenaContext()",
            "ReleaseSourceRegistration()",
            "TeamArenaPursuitLeaseBridge.RegisterSource(",
            "EnsureSourceRegistered()",
            "OnPlayerDeadOrUnspawn += OnPlayerDeadOrUnspawn",
            "TeamArenaPursuitLeaseBridge.UnregisterSource(",
            "ArenaPursuitRuntime.TryGetUsableLease(",
            "lease.Generation");
        int layerConstructor = bigBrainLayer.IndexOf("public ArenaPursuitLayer(", StringComparison.Ordinal);
        int layerGetName = bigBrainLayer.IndexOf("public override string GetName()", StringComparison.Ordinal);
        Assert(layerConstructor >= 0
               && layerGetName > layerConstructor
               && !bigBrainLayer.Substring(layerConstructor, layerGetName - layerConstructor)
                   .Contains("RegisterSource(", StringComparison.Ordinal),
            "a merely constructed BigBrain wrapper cannot advertise a movement-ready source");
        RequireAll(bigBrainLogic,
            "_nativeRunToEnemy = new ArenaRunToEnemy(botOwner)",
            "UnresolvedArrivalGraceSeconds = 1.5f",
            "RouteRefreshSeconds = 1f",
            "ArenaPursuitRuntime.TryBindLeaseTarget(BotOwner, lease, out _)",
            "ArenaSainMovementHandoff.EnsureSuspended(",
            "ArenaSainHandoffState.WaitingForDeactivation",
            "ArenaSainHandoffState.Unavailable",
            "BotOwner.PatrollingData?.Pause()",
            "BotOwner.MovementResume()",
            "BotOwner.Mover?.CurrentState == EBotMoverState.NearDoor",
            "BotOwner.Mover?.Stop()",
            "TryAcquireExactLeaseRoute(lease)",
            "_nativeRunToEnemy.UpdateNodeByBrain(null)",
            "_nativeRunToEnemy.HandlingDoorThisUpdate",
            "ReportStartedOrStop(lease)",
            "TrackUnresolvedArrival(",
            "out bool unresolvedArrivalExpired",
            "if (unresolvedArrivalExpired)",
            "if (waitingAtUnresolvedArrival)",
            "BotOwner.Mover?.HasPathAndNoComplete != true",
            "BotOwner.WeaponManager?.TryReloadWeaponOrUnderbarrelLauncher()",
            "TeamArenaPursuitLeaseBridge.ReportStarted(",
            "TeamArenaPursuitLeaseBridge.ReportRejected(",
            "lease.Generation",
            "BotOwner?.Mover?.Stop()");
        int handoffCheck = bigBrainLogic.IndexOf(
            "ArenaSainMovementHandoff.EnsureSuspended(",
            StringComparison.Ordinal);
        int targetBind = bigBrainLogic.IndexOf(
            "ArenaPursuitRuntime.TryBindLeaseTarget(BotOwner, lease, out _)",
            StringComparison.Ordinal);
        int firstRouteStop = bigBrainLogic.IndexOf(
            "BotOwner.Mover?.Stop()",
            handoffCheck,
            StringComparison.Ordinal);
        int firstRouteAcquisition = bigBrainLogic.IndexOf(
            "TryAcquireExactLeaseRoute(lease)",
            firstRouteStop,
            StringComparison.Ordinal);
        int firstStartedReport = bigBrainLogic.IndexOf(
            "TeamArenaPursuitLeaseBridge.ReportStarted(",
            firstRouteAcquisition,
            StringComparison.Ordinal);
        Assert(targetBind >= 0
               && handoffCheck > targetBind
               && firstRouteStop > handoffCheck
               && firstRouteAcquisition > firstRouteStop
               && firstStartedReport > firstRouteAcquisition,
            "a fresh generation waits for SAIN release, clears inherited movement and acquires its own route before acknowledgement");
        int freshDoorGuard = bigBrainLogic.IndexOf(
            "if (!ownsExactLease\n                && (BotOwner.Mover?.CurrentState == EBotMoverState.NearDoor",
            StringComparison.Ordinal);
        int normalFreshRoute = bigBrainLogic.IndexOf(
            "if (!ownsExactLease)\n            {",
            freshDoorGuard + 1,
            StringComparison.Ordinal);
        Assert(freshDoorGuard >= 0
               && normalFreshRoute > freshDoorGuard
               && !bigBrainLogic.Substring(freshDoorGuard, normalFreshRoute - freshDoorGuard)
                   .Contains("ReportStartedOrStop", StringComparison.Ordinal)
               && !bigBrainLogic.Substring(freshDoorGuard, normalFreshRoute - freshDoorGuard)
                   .Contains("_ownsMovement = true", StringComparison.Ordinal),
            "a fresh generation cannot acknowledge or adopt an inherited door path");
        RequireAll(sainHandoff,
            "SAIN.SAINEnableClass",
            "SAIN.Components.BotComponent",
            "SAIN.SAINComponent.Classes.SAINActivationClass",
            "SAIN.SAINComponent.Classes.Mover.SAINMoverClass",
            "SAIN.Models.Enums.ESAINLayer",
            "SAIN.Layers.IBotAction",
            "bot.GetComponent(_sainBotType)",
            "TeamArenaSainSourcePolicy.Select(",
            "TeamArenaSainSourceSelection.Incompatible",
            "IsExactWrapper(registeredSainBot, bot)",
            "IsExactWrapper(attachedSainBot, bot)",
            "ReferenceEquals(registeredSainBot, attachedSainBot)",
            "Chainloader.PluginInfos.ContainsKey(SainPluginGuid)",
            "TeamArenaSainAssemblyPolicy.Select(",
            "if (!_resolved)",
            "the installed SAIN assembly is still loading",
            "ReferenceEquals(_botOwner.GetValue(sainBot), bot)",
            "ReferenceEquals(_player.GetValue(sainBot), bot.GetPlayer)",
            "_setActiveLayer.Invoke(activation, new[] { _noActiveLayer })",
            "_setCurrentAction.Invoke(activation, new object[] { null })",
            "_stopMover.Invoke(mover, Array.Empty<object>())",
            "_setInCombat.Invoke(activation, new object[] { false })",
            "ArenaSainHandoffState.WaitingForDeactivation",
            "_sainLayersActive.GetValue(sainBot)",
            "Enum.Parse(layerType, \"None\", ignoreCase: false)");
        Assert(!sainHandoff.Contains("SetActive(false", StringComparison.Ordinal),
            "the Arena handoff never disables the SAIN bot or changes standby activity");
        RequireAll(bigBrainLogic,
            "private sealed class ArenaRunToEnemy : RunToEnemy",
            "internal const float ArenaReachDistanceMeters = 1f",
            "public override void UpdateNodeByBrain(CoreActionResultParams data)",
            "DoorInteractionStatus doorStatus = DoorOpen()",
            "HandlingDoorThisUpdate = doorStatus == DoorInteractionStatus.OpeningDoor",
            "bool canRun = doorStatus == DoorInteractionStatus.CanRun",
            "PeriodChangeWeapon()",
            "_owner.Steering.LookToMovingDirection()",
            "_owner.LookData.SetLookPointByHearing()",
            "if (!_owner.Mover.HasPathAndNoComplete",
            "_owner.Mover.IsComeTo(ArenaReachDistanceMeters, onCover: false)",
            "ReachedDestinationThisUpdate = true",
            "InPoint()");
        Assert(!bigBrainLogic.Contains("NotMovingCheck()", StringComparison.Ordinal),
            "the native node cannot replace Arena's sampled lease endpoint with GoalEnemy.CurrPosition");
        RequireAll(bigBrainLogic,
            "private bool TrackUnresolvedArrival(",
            "BotOwner.Mover?.HasPathAndNoComplete == true",
            "_unresolvedArrivalEpoch == lease.Epoch",
            "_unresolvedArrivalGeneration == lease.Generation",
            "(lease.Destination - _unresolvedArrivalDestination).sqrMagnitude",
            "(lease.Destination - BotOwner.Position).sqrMagnitude",
            "_nativeRunToEnemy.ReachedDestinationThisUpdate",
            "Time.time - _unresolvedArrivalSince",
            ">= UnresolvedArrivalGraceSeconds",
            "ResetUnresolvedArrival()");
        int arenaHasPathGuard = bigBrainLogic.IndexOf(
            "if (!_owner.Mover.HasPathAndNoComplete",
            StringComparison.Ordinal);
        int arenaArrivalCheck = bigBrainLogic.IndexOf(
            "_owner.Mover.IsComeTo(ArenaReachDistanceMeters, onCover: false)",
            arenaHasPathGuard,
            StringComparison.Ordinal);
        Assert(arenaHasPathGuard >= 0
               && arenaArrivalCheck > arenaHasPathGuard,
            "zero remaining distance without a path cannot masquerade as Arena arrival");
        Assert(!bigBrainLogic.Contains(
                   "_owner.Settings.FileSettings.Move.REACH_DIST",
                   StringComparison.Ordinal),
            "the Arena action cannot inherit PitFireTeam's wider arrival radius");
        RequireAll(bigBrainLogic,
            "private bool TryAcquireExactLeaseRoute(",
            "BotOwner.GoToPoint(",
            "slowAtTheEnd: false",
            "reachDist: ArenaRunToEnemy.ArenaReachDistanceMeters",
            "mustHaveWay: false",
            "status == NavMeshPathStatus.PathComplete",
            "BotOwner.Mover?.TargetPoint is Vector3 targetPoint",
            "ExactDestinationToleranceMeters * ExactDestinationToleranceMeters",
            "BotOwner.Mover?.Stop()",
            "return IsAtLeaseDestination(lease)");
        Assert(!bigBrainLogic.Contains("Teleport", StringComparison.Ordinal)
               && !bigBrainLogic.Contains("TryMoveToEnemy", StringComparison.Ordinal),
            "the BigBrain owner drives the exact leased EFT route and never teleports or accepts a random fallback endpoint");
        int actionPath = bigBrainLogic.IndexOf(
            "&& !TryAcquireExactLeaseRoute(lease))",
            StringComparison.Ordinal);
        int unresolvedArrivalWait = bigBrainLogic.IndexOf(
            "if (waitingAtUnresolvedArrival)\n                return;",
            StringComparison.Ordinal);
        int actionNativeUpdate = bigBrainLogic.IndexOf(
            "_nativeRunToEnemy.UpdateNodeByBrain(null)",
            StringComparison.Ordinal);
        int actionStarted = bigBrainLogic.IndexOf(
            "TeamArenaPursuitLeaseBridge.ReportStarted(",
            StringComparison.Ordinal);
        Assert(actionNativeUpdate >= 0
               && unresolvedArrivalWait > actionNativeUpdate
               && actionPath > actionNativeUpdate
               && actionPath > unresolvedArrivalWait
               && actionStarted > actionPath,
            "the BigBrain consumer withholds acknowledgement during no-path contact grace, verifies a missing path once, then acknowledges execution");
        RequireAll(bigBrainRuntime,
            "!ArenaSession.IsActive",
            "TeamArenaPursuitLeaseBridge.HasExactTeamArenaContext()",
            "Player body = bot?.GetPlayer",
            "bot.BotState == EBotState.Active",
            "body.HealthController?.IsAlive == true",
            "body.ProfileId?.Trim()",
            "bot.ProfileId?.Trim()",
            "ReferenceEquals(body.AIData?.BotOwner, bot)",
            "HasImmediateDanger(bot)",
            "bot.BewareGrenade?.ShallRunAway() == true",
            "bot.BewareBTR?.ShallRunAway() == true",
            "bot.ArtilleryDangerPlace?.ShallRunAway() == true",
            "bot.FlashGrenade?.IsFlashed == true",
            "IsBusy(bot)",
            "IsMedicalActionActive(bot)",
            "bot?.Medecine?.FirstAid?.Using == true",
            "bot?.Medecine?.SurgicalKit?.Using == true",
            "bot?.Medecine?.Stimulators?.Using == true",
            "TryGetExactEnemy(bot, current.TargetProfileId",
            "TryBindLeaseTarget(",
            "bot.Memory.GoalEnemy = enemy",
            "pair.Key.ProfileId?.Trim()",
            "ReferenceEquals(pair.Key, candidate.Person)",
            "ReferenceEquals(candidate.Owner, bot)",
            "ReferenceEquals(candidate.GroupOwner, group)",
            "ReferenceEquals(candidate.GroupInfo?._botGroup, group)",
            "HasCanonicalGroupPlayer(pair.Key, candidate.GroupInfo?.Player, expectedProfile)",
            "HasExactGroupEntry(group, pair.Key, candidate.GroupInfo)",
            "enemy.Person?.HealthController?.IsAlive != true",
            "ReferenceEquals(targetOwner.GetPlayer, groupPlayer)",
            "enemy.IsVisible && enemy.CanShoot");
        string bigBrainMedical = Slice(
            bigBrainRuntime,
            "private static bool IsMedicalActionActive(BotOwner bot)",
            "private static bool TryGetExactEnemy(");
        Assert(!bigBrainMedical.Contains(".Have2Do == true", StringComparison.Ordinal)
               && !bigBrainMedical.Contains(".HaveWork == true", StringComparison.Ordinal)
               && !bigBrainMedical.Contains("Medecine.Using", StringComparison.Ordinal)
               && !bigBrainMedical.Contains("LastResult()", StringComparison.Ordinal)
               && !bigBrainMedical.Contains("BotLogicDecision.heal", StringComparison.Ordinal),
            "the BigBrain owner also blocks only a live medical hands operation");
        Assert(!bigBrainRuntime.Contains("IsExactGoalEnemy", StringComparison.Ordinal),
            "a transient SAIN GoalEnemy decision cannot invalidate the exact Arena lease target");
        string bigBrainEnemyLookup = Slice(
            bigBrainRuntime,
            "private static bool TryGetExactEnemy(",
            "\n    }\n}");
        Assert(!bigBrainEnemyLookup.Contains(
                   "string.Equals(candidate.ProfileId",
                   StringComparison.Ordinal),
            "the BigBrain owner resolves the current dictionary-key profile instead of a cached EnemyInfo profile");

        RequireAll(burst,
            "Interlocked.Increment(ref _operationGeneration)",
            "TryCaptureSpawnLease(",
            "ArenaRespawnDriver.CaptureRaidEpoch()",
            "ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)",
            "ReferenceEquals(Singleton<AbstractGame>.Instance, game)",
            "ReferenceEquals(Singleton<GameWorld>.Instance, world)",
            "ArenaSession.IsExactActiveRaid(game, world)",
            "ReferenceEquals(Singleton<IBotGame>.Instance?.BotsController, controller)",
            "ReferenceEquals(controller?.BotSpawner, spawner)",
            "data?.StopSpawn()",
            "ReleaseReservedIfCurrent(",
            "RetireStaleSpawn(bot, role)");
        Assert(burst.IndexOf("await BotCreationData.Create", StringComparison.Ordinal)
               < burst.IndexOf("if (!IsSpawnLeaseCurrent(operationGeneration", StringComparison.Ordinal),
            "profile generation revalidates the exact raid lease immediately after its await");

        CheckTacticalIntegration(settings, tactical, combat, respawn, matchService);
    }

    private static void CheckTacticalIntegration(
        string settings,
        string tactical,
        string combat,
        string respawn,
        string matchService)
    {
        RequireAll(settings,
            "TacticalTeamRoundsToWin",
            "\"RoundsToWin\"",
            "new AcceptableValueRange<int>(1, 9)",
            "TacticalTeamSpectator",
            "\"SpectatorAfterDeath\"");

        string matchStart = Slice(
            tactical,
            "public void OnMatchStart(ArenaMatch match)",
            "public void Tick(float deltaTime)");
        RequireAll(matchStart,
            "TeamArenaLoadoutSnapshot.TryCapture(",
            "_teamGame = teamGame",
            "_teamRoundsToWin = Mathf.Clamp(Settings.TacticalTeamRoundsToWin.Value, 1, 9)",
            "_phase = Phase.Staging");
        Assert(matchStart.IndexOf("TeamArenaLoadoutSnapshot.TryCapture(", StringComparison.Ordinal)
               < matchStart.IndexOf("_phase = Phase.Staging", StringComparison.Ordinal),
            "protected 5v5 captures its process-local entry loadout and round target before combat staging");

        string tick = Slice(
            tactical,
            "public void Tick(float deltaTime)",
            "private void TickLive()");
        RequireAll(tick,
            "_teamContinuation == TeamArenaRoundContinuation.RebindNextRound",
            "EnterTeamRebinding()",
            "EndTeamRaid()",
            "case Phase.Rebinding:",
            "TickTeamRebinding()");

        string endRound = Slice(
            tactical,
            "private void EndTeamRound(bool? won, string reason)",
            "private void EnterTeamRebinding()");
        RequireAll(endRound,
            "ArenaSpectatorService.Stop(\"the protected 5v5 round ended\")",
            "TeamArenaPursuit.Reset()",
            "TeamArenaRoundPolicy.Evaluate(",
            "_teamRoundsToWin",
            "TeamArenaRoundContinuation.EndMatchWon => true",
            "TeamArenaRoundContinuation.EndMatchLost => false",
            "TeamArenaRoundContinuation.EndNoContest => null",
            "if (won.HasValue)",
            "won.Value ? ArenaAnnouncerCue.RoundWon : ArenaAnnouncerCue.RoundLost");
        Assert(endRound.IndexOf("ArenaSpectatorService.Stop", StringComparison.Ordinal)
               < endRound.IndexOf("RetireSquad()", StringComparison.Ordinal)
               && endRound.IndexOf("TeamArenaPursuit.Reset()", StringComparison.Ordinal)
               < endRound.IndexOf("RetireSquad()", StringComparison.Ordinal),
            "round completion releases camera and pursuit ownership before retiring frozen bodies");
        Assert(endRound.IndexOf("_phase = Phase.Aftermath", StringComparison.Ordinal)
               < endRound.IndexOf("_match.ReportAnnouncerCue", StringComparison.Ordinal),
            "a protected round fixes its result phase before announcing that result");

        string rebindEnum = Slice(
            tactical,
            "private enum TeamRebindStep",
            "private enum TeamRetirementDecision");
        RequireAll(rebindEnum,
            "Begin",
            "AwaitRetirement",
            "AwaitLocalRespawn",
            "RebuildLeader",
            "PrepareProfiles",
            "AwaitProfiles",
            "StartFollowers",
            "AwaitFollowers",
            "AdoptFollowers");

        string rebindTick = Slice(
            tactical,
            "private void TickTeamRebinding()",
            "private void BeginTeamRebind()");
        RequireAll(rebindTick,
            "PitFireTeamCompat.PersistenceSuppressionActive",
            "ReferenceEquals(",
            "_teamGame",
            "BeginTeamRebind()",
            "TickTeamRetirement()",
            "RebuildTeamLeader()",
            "BeginTeamFollowerProfiles()",
            "TickTeamFollowerProfiles()",
            "StartTeamFollowers()",
            "TickTeamFollowers()",
            "AdoptTeamFollowers()");

        string live = Slice(
            tactical,
            "private void TickTeamLive()",
            "private void TryStartTeamSpectator()");
        RequireAll(live,
            "PitFireTeamCompat.EnsureHostility(_boundTeamEnemies, _boundLocalPlayer)",
            "if (_playerDown)",
            "TeamArenaPursuit.Tick(_teamRoundEpoch, true, followers, _boundTeamEnemies)");
        string staging = Slice(
            tactical,
            "private void TickTeamStaging()",
            "private void RequestTeamEnemies()");
        RequireAll(staging, "_phase = Phase.Live", "ArenaAnnouncerCue.RoundStart");
        Assert(staging.IndexOf("_phase = Phase.Live", StringComparison.Ordinal)
               < staging.IndexOf("ArenaAnnouncerCue.RoundStart", StringComparison.Ordinal),
            "protected Tactical announces round start only after both teams become live");
        RequireAll(tactical,
            "TeamArenaPursuit.BeginRound(_teamRoundEpoch)",
            "TryStartTeamSpectator()",
            "Settings.TacticalTeamSpectator.Value",
            "followerProfiles.Concat(enemyProfiles).ToArray()",
            "_teamRoundEpoch == epoch",
            "ReferenceEquals(_boundLocalPlayer, deadPlayer)",
            "BuildTeamSpectatorTargets(",
            "ArenaSpectatorService.RequestStart(request, out string problem)");

        string damageGuard = Slice(
            tactical,
            "internal bool ShouldBlockStagingDamage(Player victim)",
            "private void EndTeamRound(bool? won, string reason)");
        RequireAll(damageGuard,
            "_phase == Phase.Staging",
            "_phase == Phase.Aftermath",
            "_phase == Phase.Rebinding",
            "FailTeamTransition(reason)");
        RequireAll(combat,
            "ShouldBlockProtectedTeamMutation",
            "FailClosedStagingGuard");

        string retirement = Slice(
            tactical,
            "private void TickTeamRetirement()",
            "private TeamRetirementDecision ObserveTeamEnemyRetirement");
        int arm = retirement.IndexOf("_teamLoadoutSnapshot.TryArm(_teamRoundEpoch", StringComparison.Ordinal);
        int validateDeadLeader = retirement.IndexOf(
            "TryValidateDeadTeamLeaderBody(_teamGame, deadPlayer",
            StringComparison.Ordinal);
        int prepare = retirement.IndexOf("_teamLoadoutSnapshot.TryPrepare(", StringComparison.Ordinal);
        int authorize = retirement.IndexOf("_teamRespawnAuthorizationAvailable = true", StringComparison.Ordinal);
        int release = retirement.IndexOf("_playerDown = false", StringComparison.Ordinal);
        Assert(validateDeadLeader >= 0
               && arm > validateDeadLeader
               && prepare > arm
               && authorize > prepare
               && release > authorize,
            "the exact unregistered corpse is revalidated before its snapshot is armed, prepared and released to respawn");

        string failedRebind = Slice(
            tactical,
            "private void FailTeamTransition(string reason)",
            "private string DescribeTeamRebindStep()");
        Assert(failedRebind.Contains(
                   "_teamContinuation = TeamArenaRoundContinuation.EndNoContest",
                   StringComparison.Ordinal)
               && failedRebind.Contains(
                   "PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(",
                   StringComparison.Ordinal)
               && failedRebind.Contains("_authorizedDeadTeamPlayer = null", StringComparison.Ordinal)
               && failedRebind.Contains("_teamRespawnAuthorizationConsumed = false", StringComparison.Ordinal)
               && !failedRebind.Contains("TeamArenaRoundPolicy.Evaluate(", StringComparison.Ordinal)
               && !failedRebind.Contains("ReportAnnouncerCue", StringComparison.Ordinal)
               && !failedRebind.Contains("RoundsWon =", StringComparison.Ordinal)
               && !failedRebind.Contains("RoundsLost =", StringComparison.Ordinal),
            "a failed rebuild cancels detached follower work and respawn authority before ending no-contest without double scoring");

        string authorization = Slice(
            tactical,
            "internal bool TryTakeTeamRespawnAuthorization(",
            "public void OnMatchEnd()");
        RequireAll(authorization,
            "_phase != Phase.Rebinding",
            "_teamRebindStep != TeamRebindStep.AwaitLocalRespawn",
            "!ReferenceEquals(game, _teamGame)",
            "!ReferenceEquals(deadPlayer, _authorizedDeadTeamPlayer)",
            "_preparedTeamRespawn.RoundEpoch != _teamRoundEpoch",
            "!PitFireTeamCompat.RoundTransitionActive",
            "lifecycle.Stage != PitFireTeamRoundLifecycleStage.ReadyForLeader",
            "TryValidateDeadTeamLeaderBody(game, deadPlayer",
            "_teamRespawnAuthorizationAvailable = false",
            "_teamRespawnAuthorizationConsumed = true");

        string matchEnd = Slice(
            tactical,
            "public void OnMatchEnd()",
            "public void ReadyUp()");
        RequireAll(matchEnd,
            "_squadEpoch++",
            "_phase = Phase.Ended",
            "PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(\"Tactical match ended\")",
            "_squad.Clear()",
            "_teamGame = null");
        Assert(matchEnd.IndexOf("_squadEpoch++", StringComparison.Ordinal)
               < matchEnd.IndexOf("_squad.Clear()", StringComparison.Ordinal),
            "match end invalidates asynchronous opponent observers before releasing frozen bodies");
        Assert(matchEnd.IndexOf("PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(", StringComparison.Ordinal)
               < matchEnd.IndexOf("_squad.Clear()", StringComparison.Ordinal),
            "match end cancels detached PitFireTeam follower work before releasing the exact roster");
        string liveLeaderIdentity = Slice(
            tactical,
            "private bool TryReadBoundLocalPlayer(out bool alive, out string problem)",
            "private bool TryFreezeTeamFollowers(");
        RequireAll(liveLeaderIdentity,
            "IPlayer[] registeredWithProfile = world.RegisteredPlayers",
            "registeredWithProfile.Length == 0",
            "registeredWithProfile.Length == 1",
            "ReferenceEquals(registeredWithProfile[0], _boundLocalPlayer)",
            "GameWorld has {registeredWithProfile.Length} active bodies under the local profile");
        string soloStart = Slice(
            tactical,
            "private void StartRound()",
            "private static void RetireBot(");
        RequireAll(soloStart,
            "epoch != _squadEpoch",
            "_phase != Phase.Live",
            "_squad.Count >= _squadRequested",
            "RetireBot(bot, \"stale solo Tactical squad member\")",
            "ArenaAnnouncerCue.RoundStart");
        string soloEnd = Slice(
            tactical,
            "private void EndRound(bool won)",
            "\n}");
        RequireAll(soloEnd,
            "_squadEpoch++",
            "RetireSquad()",
            "won ? ArenaAnnouncerCue.RoundWon : ArenaAnnouncerCue.RoundLost");
        Assert(soloEnd.IndexOf("_squadEpoch++", StringComparison.Ordinal)
               < soloEnd.IndexOf("RetireSquad()", StringComparison.Ordinal),
            "solo Tactical invalidates detached squad observers before retiring the completed-round roster");
        RequireAll(tactical,
            "TryValidateCurrentTeamLeaderBody(player, out string bodyProblem)",
            "TryValidateCurrentTeamLeaderBody(",
            "the current local PMC drifted before PitFireTeam leader rebind",
            "the current local PMC drifted before PitFireTeam follower adoption",
            "world.RegisteredPlayers.Count(candidate =>",
            "world.RegisteredPlayers",
            "world.GetEverExistedPlayerByID(profileId)",
            "ReferenceEquals(game.Profile, deadPlayer.Profile)",
            "ReferenceEquals(_teamGame.Profile, player.Profile)",
            "ReferenceEquals(world.MainPlayer, deadPlayer)",
            "ReferenceEquals(world.MainPlayer, expectedPlayer)");
        string rebuildLeader = Slice(
            tactical,
            "private void RebuildTeamLeader()",
            "private void BeginTeamFollowerProfiles()");
        int revalidateBeforeRebuild = rebuildLeader.IndexOf(
            "TryValidateCurrentTeamLeaderBody(",
            StringComparison.Ordinal);
        int rebuildBoss = rebuildLeader.IndexOf(
            "PitFireTeamRoundLifecycle.TryRebuildBoss(",
            StringComparison.Ordinal);
        Assert(revalidateBeforeRebuild >= 0
               && rebuildBoss > revalidateBeforeRebuild,
            "the exact living local body is rechecked immediately before PFT creates its leader wrapper");
        string adoptFollowers = Slice(
            tactical,
            "private void AdoptTeamFollowers()",
            "private bool TryValidateDeadTeamLeaderBody(");
        int revalidateBeforeAdoption = adoptFollowers.IndexOf(
            "TryValidateCurrentTeamLeaderBody(",
            StringComparison.Ordinal);
        int adoptRoster = adoptFollowers.IndexOf(
            "PitFireTeamCompat.TryAdoptRoundRoster(",
            StringComparison.Ordinal);
        Assert(revalidateBeforeAdoption >= 0
               && adoptRoster > revalidateBeforeAdoption,
            "the exact living local body is rechecked immediately before PFT publishes the new follower roster");
        RequireAll(matchService,
            "Current?.Mode is not TacticalMode tactical",
            "tactical.TryTakeTeamRespawnAuthorization(");
        RequireAll(respawn,
            "ArenaMatchService.TryTakeTeamRespawnAuthorization(",
            "RespawnProtectedTeamPlayer(",
            "prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result)",
            "AbortProtectedTeamRespawn(");
    }

    private static SpectatorStartGate Gate(
        bool arena = true,
        bool team = true,
        bool eligible = true,
        bool epoch = true,
        bool dead = true,
        int targets = 1,
        OptionalCameraOwnerState killcam = OptionalCameraOwnerState.Absent,
        OptionalCameraOwnerState freecam = OptionalCameraOwnerState.Absent)
        => new(arena, team, eligible, epoch, dead, targets, killcam, freecam);

    private static void CheckInstalledSainContract()
    {
        string repo = FindRepositoryRoot();
        string spt = Directory.GetParent(Directory.GetParent(repo)!.FullName)!.FullName;
        string sainPath = Path.Combine(spt, "BepInEx", "plugins", "SAIN", "SAIN.dll");
        if (!File.Exists(sainPath))
        {
            Console.WriteLine(
                "SAIN_RUNTIME_CONTRACT_SKIPPED (optional SAIN client plugin is not installed)");
            return;
        }

        using var sain = AssemblyDefinition.ReadAssembly(sainPath);
        ModuleDefinition module = sain.MainModule;
        TypeDefinition enable = RequireType(module, "SAIN.SAINEnableClass");
        TypeDefinition bot = RequireType(module, "SAIN.Components.BotComponent");
        TypeDefinition botBase = RequireType(module, "SAIN.Components.BotComponentBase");
        TypeDefinition activation = RequireType(
            module,
            "SAIN.SAINComponent.Classes.SAINActivationClass");
        TypeDefinition mover = RequireType(
            module,
            "SAIN.SAINComponent.Classes.Mover.SAINMoverClass");
        TypeDefinition layer = RequireType(module, "SAIN.Models.Enums.ESAINLayer");
        TypeDefinition botAction = RequireType(module, "SAIN.Layers.IBotAction");
        TypeDefinition controller = RequireType(
            module,
            "SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController");
        TypeDefinition enemy = RequireType(
            module,
            "SAIN.SAINComponent.Classes.EnemyClasses.Enemy");
        MethodDefinition getSain = RequireMethod(
            enable,
            "GetSAIN",
            "System.Boolean",
            "System.String",
            "SAIN.Components.BotComponent&");
        Assert(getSain.IsPublic
               && getSain.IsStatic
               && getSain.Parameters[1].IsOut,
            "installed SAIN exposes public static GetSAIN(string, out BotComponent)");

        Assert(HasPublicGetter(botBase, "BotOwner", "EFT.BotOwner")
               && HasPublicGetter(botBase, "Player", "EFT.Player")
               && HasPublicGetter(bot, "EnemyController", controller.FullName)
               && HasPublicGetter(bot, "BotActivation", activation.FullName)
               && HasPublicGetter(bot, "Mover", mover.FullName)
               && HasPublicGetter(bot, "SAINLayersActive", "System.Boolean"),
            "installed SAIN exposes the exact source identity and awareness properties");

        Assert(HasPublicGetter(activation, "ActiveLayer", layer.FullName)
               && HasPublicGetter(activation, "CurrentAction", botAction.FullName)
               && HasPublicGetter(activation, "BotInCombat", "System.Boolean"),
            "installed SAIN exposes the movement ownership state required for a bounded handoff");
        MethodDefinition stopMover = RequireMethod(mover, "Stop", "System.Void");
        MethodDefinition setCurrentAction = RequireMethod(
            activation,
            "SetCurrentAction",
            "System.Void",
            botAction.FullName);
        MethodDefinition setActiveLayer = RequireMethod(
            activation,
            "SetActiveLayer",
            "System.Void",
            layer.FullName);
        MethodDefinition setInCombat = RequireMethod(
            activation,
            "SetInCombat",
            "System.Void",
            "System.Boolean");
        Assert(stopMover.IsPublic && !stopMover.IsStatic
               && setCurrentAction.IsPublic && !setCurrentAction.IsStatic
               && setActiveLayer.IsPublic && !setActiveLayer.IsStatic
               && setInCombat.IsPublic && !setInCombat.IsStatic,
            "installed SAIN exposes public instance movement release methods");
        FieldDefinition noActiveLayer = layer.Fields.SingleOrDefault(field => field.Name == "None")
            ?? throw new InvalidOperationException("Installed SAIN ESAINLayer.None is missing.");
        Assert(noActiveLayer.HasConstant && Convert.ToInt32(noActiveLayer.Constant) == 0,
            "installed SAIN ESAINLayer.None retains the expected zero value");

        MethodDefinition checkAddEnemy = RequireMethod(
            controller,
            "CheckAddEnemy",
            enemy.FullName,
            "EFT.IPlayer");
        MethodDefinition removeEnemy = RequireMethod(
            controller,
            "RemoveEnemy",
            "System.Void",
            "System.String");
        Assert(checkAddEnemy.IsPublic && !checkAddEnemy.IsStatic
               && removeEnemy.IsPublic && !removeEnemy.IsStatic,
            "installed SAIN exposes public instance enemy create and remove methods");

        PropertyDefinition nativeEnemyInfo = RequireProperty(enemy, "EnemyInfo", "EnemyInfo");
        PropertyDefinition enemyPlayer = RequireProperty(enemy, "EnemyPlayer", "EFT.Player");
        Assert(nativeEnemyInfo.GetMethod?.IsPublic == true
               && nativeEnemyInfo.SetMethod == null
               && enemyPlayer.GetMethod?.IsPublic == true,
            "installed SAIN Enemy exposes its native EnemyInfo and canonical Player through public getters");

        PropertyDefinition goalEnemy = RequireProperty(controller, "GoalEnemy", enemy.FullName);
        Assert(goalEnemy.GetMethod?.IsPublic == true
               && goalEnemy.SetMethod?.IsPrivate == true
               && !goalEnemy.SetMethod.IsStatic,
            "installed SAIN GoalEnemy has the public getter and private instance setter used by Arena");

        MethodDefinition updateLastSeen = RequireMethod(
            enemy,
            "UpdateLastSeenPosition",
            "System.Void",
            "UnityEngine.Vector3",
            "System.Single");
        Assert(updateLastSeen.IsPublic && !updateLastSeen.IsStatic,
            "installed SAIN exposes public instance UpdateLastSeenPosition(Vector3, float)");

        Console.WriteLine("SAIN_RUNTIME_CONTRACT_PASSED");
    }

    private static void CheckInstalledPitFireTeamContract()
    {
        string repo = FindRepositoryRoot();
        string spt = Directory.GetParent(Directory.GetParent(repo)!.FullName)!.FullName;
        string pitPath = Path.Combine(
            spt,
            "BepInEx",
            "plugins",
            "pitFireTeam",
            "pitFireTeam.dll");
        if (!File.Exists(pitPath))
        {
            Console.WriteLine(
                "PITFIRETEAM_BIGBRAIN_FILTER_CONTRACT_SKIPPED (optional PitFireTeam client plugin is not installed)");
            return;
        }

        using var pit = AssemblyDefinition.ReadAssembly(pitPath);
        TypeDefinition filter = RequireType(
            pit.MainModule,
            "pitTeam.Patches.HostilePeacefulLayerInterrupt");
        MethodDefinition isCombatLayer = filter.Methods.SingleOrDefault(method =>
                method.Name == "IsCombatLayer"
                && method.IsStatic
                && method.Parameters.Count == 1)
            ?? throw new InvalidOperationException(
                "Installed PitFireTeam HostilePeacefulLayerInterrupt.IsCombatLayer is missing.");
        bool recognizesCombatName = isCombatLayer.HasBody
            && isCombatLayer.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code == Code.Ldstr
                && string.Equals(instruction.Operand as string, "Combat", StringComparison.OrdinalIgnoreCase));
        Assert(recognizesCombatName,
            "installed PitFireTeam retains combat layers whose reported name contains Combat");
        Console.WriteLine("PITFIRETEAM_BIGBRAIN_FILTER_CONTRACT_PASSED");
    }

    private static TypeDefinition RequireType(ModuleDefinition module, string fullName)
        => module.GetType(fullName)
           ?? throw new InvalidOperationException($"Installed SAIN type '{fullName}' is missing.");

    private static MethodDefinition RequireMethod(
        TypeDefinition type,
        string name,
        string returnType,
        params string[] parameterTypes)
    {
        MethodDefinition[] matches = type.Methods.Where(method =>
                method.Name == name
                && method.ReturnType.FullName == returnType
                && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(parameterTypes, StringComparer.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Installed SAIN method '{type.FullName}.{name}({string.Join(", ", parameterTypes)})' "
                + $"with return type '{returnType}' has {matches.Length} exact matches; expected one.");
    }

    private static PropertyDefinition RequireProperty(
        TypeDefinition type,
        string name,
        string propertyType)
    {
        PropertyDefinition[] matches = type.Properties.Where(property =>
                property.Name == name && property.PropertyType.FullName == propertyType)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Installed SAIN property '{type.FullName}.{name}' with type '{propertyType}' "
                + $"has {matches.Length} exact matches; expected one.");
    }

    private static bool HasPublicGetter(
        TypeDefinition type,
        string name,
        string propertyType)
        => type.Properties.Any(property =>
            property.Name == name
            && property.PropertyType.FullName == propertyType
            && property.GetMethod?.IsPublic == true
            && !property.GetMethod.IsStatic);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ArenaMode.sln above the check output.");
    }

    private static string Read(string root, params string[] relativeParts)
        => File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                $"production source retains required contract fragment: {fragment}");
        }
    }

    private static int CountOf(string source, string fragment)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"production source lost method marker: {startMarker}");

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException($"production source lost following marker: {endMarker}");

        return source.Substring(start, end - start);
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
