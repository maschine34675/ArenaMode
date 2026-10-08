using System;
using System.Collections.Generic;

namespace ArenaMode.Spectator;

internal enum OptionalCameraOwnerState
{
    Absent,
    Idle,
    Busy,
    Incompatible
}

internal enum SpectatorStartDecision
{
    Wait,
    Start,
    Cancel
}

internal readonly struct SpectatorStartGate
{
    public SpectatorStartGate(
        bool arenaActive,
        bool teamArenaActive,
        bool requestEligible,
        bool epochMatches,
        bool localPlayerDead,
        int livingTargetCount,
        OptionalCameraOwnerState killcam,
        OptionalCameraOwnerState externalFreecam)
    {
        ArenaActive = arenaActive;
        TeamArenaActive = teamArenaActive;
        RequestEligible = requestEligible;
        EpochMatches = epochMatches;
        LocalPlayerDead = localPlayerDead;
        LivingTargetCount = livingTargetCount;
        Killcam = killcam;
        ExternalFreecam = externalFreecam;
    }

    public bool ArenaActive { get; }
    public bool TeamArenaActive { get; }
    public bool RequestEligible { get; }
    public bool EpochMatches { get; }
    public bool LocalPlayerDead { get; }
    public int LivingTargetCount { get; }
    public OptionalCameraOwnerState Killcam { get; }
    public OptionalCameraOwnerState ExternalFreecam { get; }
}

internal readonly struct SpectatorEscapeGate
{
    public SpectatorEscapeGate(
        bool escapeCommand,
        bool arenaActive,
        bool teamArenaActive,
        bool ownsCamera,
        bool battleUiActive,
        bool requestEligible,
        bool epochMatches,
        bool expectedBodyIsLocal,
        bool expectedBodyIsDead,
        object expectedBody,
        object ownerBody)
    {
        EscapeCommand = escapeCommand;
        ArenaActive = arenaActive;
        TeamArenaActive = teamArenaActive;
        OwnsCamera = ownsCamera;
        BattleUiActive = battleUiActive;
        RequestEligible = requestEligible;
        EpochMatches = epochMatches;
        ExpectedBodyIsLocal = expectedBodyIsLocal;
        ExpectedBodyIsDead = expectedBodyIsDead;
        ExpectedBody = expectedBody;
        OwnerBody = ownerBody;
    }

    public bool EscapeCommand { get; }
    public bool ArenaActive { get; }
    public bool TeamArenaActive { get; }
    public bool OwnsCamera { get; }
    public bool BattleUiActive { get; }
    public bool RequestEligible { get; }
    public bool EpochMatches { get; }
    public bool ExpectedBodyIsLocal { get; }
    public bool ExpectedBodyIsDead { get; }
    public object ExpectedBody { get; }
    public object OwnerBody { get; }
}

/// <summary>
/// Pure spectator decisions kept outside Unity so the fail-closed boundaries can
/// be exercised offline. Runtime object and profile validation still belongs to
/// <see cref="ArenaSpectatorService"/>.
/// </summary>
internal static class SpectatorPolicy
{
    internal const int RequiredNonLocalTeamActors = 9;
    internal static int RequiredNonLocalActors(bool cooperative) => cooperative ? 4 : RequiredNonLocalTeamActors;

    public static SpectatorStartDecision DecideStart(SpectatorStartGate gate)
    {
        if (!gate.ArenaActive
            || !gate.TeamArenaActive
            || !gate.RequestEligible
            || !gate.EpochMatches
            || !gate.LocalPlayerDead
            || gate.Killcam == OptionalCameraOwnerState.Incompatible
            || gate.ExternalFreecam == OptionalCameraOwnerState.Incompatible)
        {
            return SpectatorStartDecision.Cancel;
        }

        if (gate.Killcam == OptionalCameraOwnerState.Busy
            || gate.ExternalFreecam == OptionalCameraOwnerState.Busy
            || gate.LivingTargetCount <= 0)
        {
            return SpectatorStartDecision.Wait;
        }

        return SpectatorStartDecision.Start;
    }

    public static bool ShouldTranslateEscape(SpectatorEscapeGate gate)
        => gate.EscapeCommand
           && gate.ArenaActive
           && gate.TeamArenaActive
           && gate.OwnsCamera
           && gate.BattleUiActive
           && gate.RequestEligible
           && gate.EpochMatches
           && gate.ExpectedBodyIsLocal
           && gate.ExpectedBodyIsDead
           && gate.ExpectedBody != null
           && ReferenceEquals(gate.ExpectedBody, gate.OwnerBody);

    public static bool TryFreezeAllowedProfileIds(
        IEnumerable<string> source,
        out string[] profileIds,
        out string problem,
        bool cooperative = false)
    {
        profileIds = Array.Empty<string>();
        problem = null;
        if (source == null)
        {
            problem = "the spectator identity set was missing";
            return false;
        }

        HashSet<string> unique = new(StringComparer.Ordinal);
        int required = RequiredNonLocalActors(cooperative);
        List<string> frozen = new(required);
        foreach (string value in source)
        {
            string profileId = value?.Trim();
            if (string.IsNullOrEmpty(profileId))
            {
                problem = "the spectator identity set contained an empty profile id";
                return false;
            }

            if (!unique.Add(profileId))
            {
                problem = $"the spectator identity set contained duplicate profile {profileId}";
                return false;
            }

            frozen.Add(profileId);
        }

        if (frozen.Count != required)
        {
            problem = $"the spectator identity set contained {frozen.Count}/{required} non-local actors";
            return false;
        }

        profileIds = frozen.ToArray();
        return true;
    }

    public static int CycleIndex(int count, int currentIndex, int direction)
    {
        if (count <= 0)
            return -1;

        if (currentIndex < 0 || currentIndex >= count)
            return direction < 0 ? count - 1 : 0;

        int step = direction < 0 ? -1 : 1;
        int next = (currentIndex + step) % count;
        return next < 0 ? next + count : next;
    }
}
