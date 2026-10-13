using EFT;
using System;
using System.Collections.Generic;

namespace ArenaMode.Spectator;

internal enum ArenaSpectatorSide
{
    Ally,
    Opponent
}

internal sealed class ArenaSpectatorTarget
{
    public ArenaSpectatorTarget(Player player, string profileId, string displayName, ArenaSpectatorSide side)
    {
        Player = player;
        ProfileId = profileId?.Trim();
        DisplayName = displayName?.Trim();
        Side = side;
    }

    public Player Player { get; }
    public string ProfileId { get; }
    public string DisplayName { get; }
    public ArenaSpectatorSide Side { get; }
}

/// <summary>
/// Immutable lease supplied by Tactical after the local team player dies. The
/// frozen identity set is the authority; the provider only resolves those exact
/// bodies to their current live state.
/// </summary>
internal sealed class ArenaSpectatorRequest
{
    public ArenaSpectatorRequest(
        int epoch,
        int expectedCompanionCount,
        Func<int> readEpoch,
        Func<bool> isEligible,
        Player deadPlayer,
        IEnumerable<string> allowedProfileIds,
        Func<IReadOnlyList<ArenaSpectatorTarget>> targetProvider,
        string preferredProfileId = null,
        bool cooperative = false)
    {
        Epoch = epoch;
        ExpectedCompanionCount = expectedCompanionCount;
        ReadEpoch = readEpoch;
        IsEligible = isEligible;
        DeadPlayer = deadPlayer;
        AllowedProfileIds = allowedProfileIds;
        TargetProvider = targetProvider;
        PreferredProfileId = preferredProfileId?.Trim();
        Cooperative = cooperative;
    }

    public int Epoch { get; }
    public int ExpectedCompanionCount { get; }
    public bool Cooperative { get; }
    public Func<int> ReadEpoch { get; }
    public Func<bool> IsEligible { get; }
    public Player DeadPlayer { get; }
    public IEnumerable<string> AllowedProfileIds { get; }
    public Func<IReadOnlyList<ArenaSpectatorTarget>> TargetProvider { get; }
    public string PreferredProfileId { get; }
}
