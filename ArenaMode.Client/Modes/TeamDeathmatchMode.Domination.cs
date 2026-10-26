using ArenaMode.Bots;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class TeamDeathmatchMode
{
    public bool IsDomination { get; }
    private DominationMatchPolicy _dominationRules;
    private DominationRuntime _domination;

    public TeamDeathmatchMode(bool domination = false) => IsDomination = domination;

    // Share the physical roster/respawn lifecycle, but keep the kill policy
    // untouched. Objective ownership and terminal decisions have their own policy.
    private bool MatchRulesReady => IsDomination ? _dominationRules != null : _rules != null;
    private TdmMatchPhase? MatchPhase => IsDomination ? _dominationRules?.Phase : _rules?.Phase;
    private TdmMatchResult? MatchResult => IsDomination ? _dominationRules?.Result : _rules?.Result;
    private string MatchReason => IsDomination ? _dominationRules?.Reason : _rules?.Reason;
    private int MatchScoreLimit => IsDomination ? _dominationRules?.ScoreLimit ?? 0 : _rules?.KillLimit ?? 0;
    private int MatchTimeLimit => IsDomination ? _dominationRules?.TimeLimitSeconds ?? 0 : _rules?.TimeLimitSeconds ?? 0;
    private int FinalFriendlyScore => IsDomination ? _dominationRules?.FriendlyScore ?? 0 : _rules?.FriendlyKills ?? 0;
    private int FinalOpponentScore => IsDomination ? _dominationRules?.OpponentScore ?? 0 : _rules?.OpponentKills ?? 0;
    internal int FriendlyTeamScore => IsDomination ? (_population?.LocalUsec == true
        ? _dominationRules?.UsecScore ?? 0 : _dominationRules?.BearScore ?? 0)
        : _match?.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills ?? 0;
    internal int OpponentTeamScore => IsDomination ? (_population?.LocalUsec == true
        ? _dominationRules?.BearScore ?? 0 : _dominationRules?.UsecScore ?? 0)
        : _match?.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills ?? 0;
    private int MatchCountdownRemaining(float now) => IsDomination
        ? _dominationRules?.CountdownRemaining(now) ?? 0 : _rules?.CountdownRemaining(now) ?? 0;
    internal string ObjectiveStatusLine => _domination?.StatusLine ?? "";
    internal int ObjectiveCount => _dominationRules?.Points.Count ?? 0;
    internal bool CanPlacePickup(Vector3 floor) => !IsDomination
        || ObjectiveHudVisible && _domination?.AllowsPickup(floor) == true;
    internal bool ObjectiveHudVisible => IsDomination && !_ended && !_nativeStopObserved
        && _failure == null && IsCurrentRaid() && ReferenceEquals(ArenaMatchService.Current, _match);

    private void InitializeDomination()
    {
        if (!IsDomination) return;
        if (!DominationObjectiveBridge.Available)
            throw new InvalidOperationException("Domination requires the registered Arena BigBrain objective layer.");
        _dominationRules = new DominationMatchPolicy(pointCount: DominationMapLayout.Resolve(_world.LocationId).Points.Count);
        _domination = new DominationRuntime(this, _dominationRules, _world);
        try
        {
            _domination.Prepare();
            DominationObjectiveBridge.Begin(_match, _game, _world);
        }
        catch { ReleaseDomination(); throw; }
    }

    private void AdvanceDomination()
    {
        if (!IsDomination || _domination == null) return;
        if (!DominationObjectiveBridge.Available)
        {
            Fail("Domination objective movement support became unavailable");
            return;
        }
        try { _domination.Tick(_local, _bots, _population.LocalUsec, Time.time); }
        catch (Exception ex) { Fail("Domination runtime failed: " + ex.GetBaseException().Message); }
    }

    private void ReleaseDomination()
    {
        if (!IsDomination) return;
        _domination?.Dispose();
        _domination = null;
        DominationObjectiveBridge.Stop();
    }
}
