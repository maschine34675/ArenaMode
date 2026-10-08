using System;
using System.Collections.Generic;

namespace ArenaMode.Effects;

internal enum ArenaTracerSide
{
    Unknown,
    Friendly,
    Opponent,
}

/// <summary>
/// Match-scoped shooter identities for team-colored tracers. Protected 5v5 uses
/// only TacticalMode's exact frozen profile rosters; faction, role and mutable bot
/// groups are not identities. Historical same-side IDs remain until match end so
/// a projectile still flying during a round rebind cannot change color.
/// Independent faction TDM registers only its admitted bot IDs and retains the
/// same historical-side rule; unowned bodies never get a guessed team color.
/// </summary>
internal static class ArenaTracerTeams
{
    internal const int TeamFollowerCount = 4;
    internal const int TeamOpponentCount = 5;

    private static readonly HashSet<string> FriendlyProfileIds =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> OpponentProfileIds =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> WarnedUnknownProfileIds =
        new(StringComparer.Ordinal);

    private static string _localProfileId;
    private static bool _matchBound;
    private static bool _protectedTeamMatch;
    private static bool _factionTeamMatch;
    private static bool _protectedRosterValid;
    private static int _latestTeamEpoch;

    internal static bool ProtectedTeamMatch => _matchBound && _protectedTeamMatch;

    internal static bool BeginMatch(string localProfileId, bool protectedTeamMatch)
    {
        Reset();
        string normalized = Normalize(localProfileId);
        if (normalized == null)
            return false;

        _localProfileId = normalized;
        _protectedTeamMatch = protectedTeamMatch;
        _matchBound = true;
        FriendlyProfileIds.Add(normalized);
        return true;
    }

    internal static bool TryBindTeamRound(
        int epoch,
        string localProfileId,
        IReadOnlyList<string> followerProfileIds,
        IReadOnlyList<string> opponentProfileIds,
        out string problem)
    {
        problem = null;
        if (!_matchBound || !_protectedTeamMatch)
        {
            problem = "no protected Arena tracer match is bound";
            return false;
        }

        // Any attempted rebind makes the previous current-round classification
        // stale until the complete new roster validates. On failure the whole
        // protected match stays native rather than preserving a possibly wrong
        // old color for a profile that changed sides.
        _protectedRosterValid = false;

        if (epoch <= _latestTeamEpoch)
        {
            problem = $"team round epoch {epoch} is not newer than {_latestTeamEpoch}";
            return false;
        }

        string local = Normalize(localProfileId);
        if (!string.Equals(local, _localProfileId, StringComparison.Ordinal))
        {
            problem = "the Tactical local profile does not match the tracer match";
            return false;
        }

        if (followerProfileIds?.Count != TeamFollowerCount
            || opponentProfileIds?.Count != TeamOpponentCount)
        {
            problem = "the tracer roster is not exactly one plus four versus five";
            return false;
        }

        var nextFriendly = new HashSet<string>(StringComparer.Ordinal) { local };
        var nextOpponents = new HashSet<string>(StringComparer.Ordinal);
        if (!TryCollect(
                followerProfileIds,
                nextFriendly,
                OpponentProfileIds,
                "friendly",
                out problem)
            || !TryCollect(
                opponentProfileIds,
                nextOpponents,
                FriendlyProfileIds,
                "opponent",
                out problem))
        {
            return false;
        }

        foreach (string profileId in nextFriendly)
        {
            if (nextOpponents.Contains(profileId))
            {
                problem = $"profile '{profileId}' appears on both tracer teams";
                return false;
            }
        }

        FriendlyProfileIds.UnionWith(nextFriendly);
        OpponentProfileIds.UnionWith(nextOpponents);
        _latestTeamEpoch = epoch;
        _protectedRosterValid = true;
        return true;
    }

    internal static bool BeginFactionMatch(string localProfileId)
    {
        if (!BeginMatch(localProfileId, protectedTeamMatch: false)) return false;
        _factionTeamMatch = true;
        return true;
    }

    /// <summary>Only the owned faction mode admits a validated physical bot.</summary>
    internal static bool RegisterFactionActor(string profileId, bool friendly)
    {
        string id = Normalize(profileId);
        if (!_matchBound || !_factionTeamMatch || id == null) return false;
        var own = friendly ? FriendlyProfileIds : OpponentProfileIds;
        var other = friendly ? OpponentProfileIds : FriendlyProfileIds;
        if (other.Contains(id)) return false;
        own.Add(id);
        return true;
    }

    /// <summary>
    /// In solo Arena, any other verified player-owned shot is an opponent (the
    /// killfeed names and friendly markers rely on that in every mode; tracers
    /// are only RENDERED for team modes, see <c>Settings.TracerEntry</c>). In
    /// protected 5v5, only exact frozen roster IDs are accepted.
    /// </summary>
    internal static ArenaTracerSide Classify(
        string profileId,
        bool verifiedPlayerIdentity)
    {
        if (!_matchBound)
            return ArenaTracerSide.Unknown;

        if (!verifiedPlayerIdentity
            || (_protectedTeamMatch && !_protectedRosterValid))
        {
            return ArenaTracerSide.Unknown;
        }

        string normalized = Normalize(profileId);
        if (normalized == null)
            return ArenaTracerSide.Unknown;

        if (FriendlyProfileIds.Contains(normalized))
            return ArenaTracerSide.Friendly;

        if (OpponentProfileIds.Contains(normalized))
            return ArenaTracerSide.Opponent;

        return !_protectedTeamMatch && !_factionTeamMatch
            ? ArenaTracerSide.Opponent
            : ArenaTracerSide.Unknown;
    }

    internal static bool ShouldWarnUnknown(string profileId)
    {
        if (!ProtectedTeamMatch)
            return false;

        string normalized = Normalize(profileId);
        return normalized != null && WarnedUnknownProfileIds.Add(normalized);
    }

    internal static void Reset()
    {
        FriendlyProfileIds.Clear();
        OpponentProfileIds.Clear();
        WarnedUnknownProfileIds.Clear();
        _localProfileId = null;
        _matchBound = false;
        _protectedTeamMatch = false;
        _factionTeamMatch = false;
        _protectedRosterValid = false;
        _latestTeamEpoch = 0;
        ArenaTracerPatches.ResetDiagnostics();
    }

    private static bool TryCollect(
        IReadOnlyList<string> source,
        HashSet<string> destination,
        HashSet<string> historicalOtherSide,
        string side,
        out string problem)
    {
        for (int index = 0; index < source.Count; index++)
        {
            string profileId = Normalize(source[index]);
            if (profileId == null)
            {
                problem = $"{side} tracer profile {index + 1} is empty";
                return false;
            }

            if (!destination.Add(profileId))
            {
                problem = $"{side} tracer profile '{profileId}' is duplicated";
                return false;
            }

            if (historicalOtherSide.Contains(profileId))
            {
                problem = $"tracer profile '{profileId}' changed team between rounds";
                return false;
            }
        }

        problem = null;
        return true;
    }

    private static string Normalize(string profileId)
    {
        string normalized = profileId?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
