using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>Read-only adapter over each mode's real combat phase, never HUD text.</summary>
internal sealed class ArenaRewardModeState
{
    internal int Number = 1;
    internal string Rule = "KillsOnly";
    internal long Limit;
    internal int Target;
    internal bool Active, Failed, Finished;
    internal float Elapsed = -1f;
    internal long MinimumElapsedMilliseconds;
    internal List<(Player Player, string Side)> FixedRoster;

    internal static ArenaRewardModeState Read(IArenaMode mode)
    {
        if (mode is RoundsMode dm) return dm.ReadRewardState();
        if (mode is TeamDeathmatchMode tdm) return tdm.ReadRewardState();
        if (mode is TacticalMode tactical) return tactical.ReadRewardState();
        if (mode is ZombieMode zombies) return zombies.ReadRewardState();
        if (mode is GunGameMode guns) return new ArenaRewardModeState { Active = guns.AssistScoringOpen
            && !Respawn.LiveKitRespawn.IsInitialSwapPending, Failed = !guns.AssistScoringOpen
                && !Respawn.LiveKitRespawn.IsInitialSwapPending && guns.RewardFailed };
        return new ArenaRewardModeState { Failed = true };
    }
}

public sealed partial class RoundsMode
{
    internal ArenaRewardModeState ReadRewardState() => new ArenaRewardModeState
    {
        Number = Round.Number,
        Rule = Continuous ? "KillsOnly" : FreeForAll ? "FreeForAllScore" : "LocalTarget",
        Limit = _options.Seconds * 1000L, Target = _options.Kills,
        Active = _rules?.Phase == DeathmatchPhase.Live,
        Finished = _rules?.Phase is DeathmatchPhase.Intermission or DeathmatchPhase.Finished,
        Failed = _rules?.Outcome == DeathmatchOutcome.NoContest,
        Elapsed = _rules == null || _rules.Phase == DeathmatchPhase.Waiting ? 0f
            : _options.Seconds > 0 ? _options.Seconds - TimeRemaining : Mathf.Max(0f, Time.time - Round.StartedAt)
    };
}

public sealed partial class TeamDeathmatchMode
{
    internal ArenaRewardModeState ReadRewardState() => new ArenaRewardModeState
    {
        Rule = (_rules?.KillLimit ?? Settings.TeamDeathmatchKillLimit.Value) == 0
            && (_rules?.TimeLimitSeconds ?? Settings.TeamDeathmatchTimeLimitSeconds.Value) == 0 ? "KillsOnly" : "TeamScore",
        Limit = (_rules?.TimeLimitSeconds ?? Settings.TeamDeathmatchTimeLimitSeconds.Value) * 1000L,
        Target = _rules?.KillLimit ?? Settings.TeamDeathmatchKillLimit.Value,
        Active = _rules?.Phase == TdmMatchPhase.Live,
        Finished = HasResult, Failed = _failure != null, Elapsed = PlayingElapsed
    };

    internal bool TryRewardSide(Player player, out string side)
    {
        side = null;
        if (ReferenceEquals(player, _local) && player?.ProfileId == _localId) { side = "Friendly"; return true; }
        if (player == null || _population == null || !_bots.Exists(bot => ReferenceEquals(bot?.GetPlayer, player))
            || !_usecById.TryGetValue(player.ProfileId, out var usec)) return false;
        side = usec == _population.LocalUsec ? "Friendly" : "Opponent";
        return true;
    }
}

public sealed partial class TacticalMode
{
    private float _rewardRoundStartedAt, _rewardRoundLimit;
    private bool _rewardTimerExpired;

    private void BeginRewardCombat()
    {
        _rewardRoundStartedAt = Time.time;
        _rewardTimerExpired = false;
        // Match the configured duration used by the native mode timer. Subtracting
        // two absolute float timestamps can shift fractional limits by a
        // millisecond as game uptime grows and falsely change the frozen rule.
        _rewardRoundLimit = Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);
        ArenaRewardCapture.Observe();
    }

    internal ArenaRewardModeState ReadRewardState()
    {
        var result = new ArenaRewardModeState {
            Number = RoundNumber,
            Rule = _teamArena ? "TeamElimination" : "SoloElimination",
            Limit = (long)Math.Round((_rewardRoundLimit > 0 ? _rewardRoundLimit : Mathf.Max(30f, Settings.TacticalRoundSeconds.Value)) * 1000d),
            Active = _phase == Phase.Live && (_teamArena || _soloCombatReady),
            Finished = _phase is Phase.Aftermath or Phase.Rebinding or Phase.Buy or Phase.Ended,
            Failed = _teamArena && _phase == Phase.Aftermath && _teamRoundResult == TeamArenaRoundResult.NoContest,
            Elapsed = Mathf.Max(0f, Time.time - _rewardRoundStartedAt),
            // The native float timer can expire just below the canonical
            // millisecond limit. Only its actual expiry branch supplies this
            // lower bound; early elimination never asserts a timeout.
            MinimumElapsedMilliseconds = _rewardTimerExpired ? (long)Math.Round(_rewardRoundLimit * 1000d) : 0,
            FixedRoster = new List<(Player, string)>()
        };
        if (!result.Active) return result;
        var world = Comfort.Common.Singleton<GameWorld>.Instance;
        var local = _teamArena ? _boundLocalPlayer : world?.MainPlayer;
        result.FixedRoster.Add((local, "Friendly"));
        if (_teamArena) foreach (var bot in _boundTeamFollowers) result.FixedRoster.Add((bot?.GetPlayer, "Friendly"));
        foreach (var bot in _teamArena ? (IReadOnlyList<BotOwner>)_boundTeamEnemies : _squad)
            result.FixedRoster.Add((bot?.GetPlayer, "Opponent"));
        return result;
    }
}

public sealed partial class ZombieMode
{
    internal bool IsRewardFriendly(Player player) => _team?.IsRewardFriendly(player) == true;
    internal ArenaRewardModeState ReadRewardState() => new ArenaRewardModeState {
        // One kill-only ledger spans waves. Intermission/rebuild clocks pause it;
        // wave/ladder wins require a richer server proof and earn no bonus here.
        Active = !IsComplete && _phase == ZombieWavePolicy.Phase.Wave && (_team == null || _team.IsLive),
        Finished = IsComplete,
        Failed = _soloNoContestReason != null || _team?.RewardNoContest == true
    };
}

internal sealed partial class ZombieTeamRuntime
{
    internal bool RewardNoContest => _noContest;
    internal bool IsRewardFriendly(Player player) => ReferenceEquals(player, _boundLocalPlayer)
        || Array.Exists(_boundTeamFollowers, bot => ReferenceEquals(bot?.GetPlayer, player));
}
public sealed partial class GunGameMode { internal bool RewardFailed => _givenUp; }
