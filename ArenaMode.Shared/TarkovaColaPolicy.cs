#nullable disable
using System;

namespace ArenaMode.Shared;

/// <summary>Rules shared by the optional integration and its lifetime tests.</summary>
internal static class TarkovaColaPolicy
{
    internal const string PluginGuid = "com.tarkovacola.server";
    internal const string ReviewedVersion = "2.2.1";
    internal const int MaxStateBytes = 524288;
    internal static bool SupportedVersion(Version version)
        => version != null && version >= new Version(2, 2, 1);
    internal static bool RewardsAllowed(string mode)
        => string.Equals(mode, "zombie", StringComparison.Ordinal);
}

/// <summary>
/// An accepted arm owns writes until explicit cleanup, including temporary missing
/// bodies and the interval after capture. Gameplay needs the exact confirmed world.
/// References are deliberately physical identities rather than reusable profile IDs.
/// </summary>
internal sealed class TarkovaColaClientLease
{
    internal TarkovaColaClientLease(string profileId, string map, string mode, int epoch)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(map)
            || string.IsNullOrWhiteSpace(mode)) throw new ArgumentException("Missing Cola arm identity.");
        ProfileId = profileId; Map = map; Mode = mode; Epoch = epoch;
    }
    internal string ProfileId { get; }
    internal string Map { get; }
    internal string Mode { get; }
    internal int Epoch { get; }
    internal object World { get; private set; }
    internal object Body { get; private set; }
    internal bool Confirmed { get; private set; }
    internal bool Finished { get; private set; }
    internal bool Failed { get; private set; }
    internal bool Released { get; private set; }
    internal bool FinalRequested { get; private set; }
    internal bool StatisticsCompleted { get; private set; }
    private int _completedExperience, _bodyExperience;
    private object _pendingStatisticsBody;
    internal bool ReplacementStatisticsPending => _pendingStatisticsBody != null;
    internal int EarnedExperience => checked(_completedExperience + _bodyExperience);
    internal bool BlocksWrites => !Released;

    internal bool BindWorld(object world, object body, string profileId, string map, int epoch)
    {
        if (Released || Failed || Finished || world == null || body == null
            || epoch != Epoch || !string.Equals(profileId, ProfileId, StringComparison.Ordinal)
            || !string.Equals(map, Map, StringComparison.OrdinalIgnoreCase)) return false;
        if (World != null) return ReferenceEquals(World, world) && ReferenceEquals(Body, body);
        World = world; Body = body; return true;
    }

    internal bool Confirm(object world, string profileId, string mode, int epoch)
    {
        if (Released || Failed || Finished || World == null || !ReferenceEquals(World, world)
            || epoch != Epoch || !string.Equals(ProfileId, profileId, StringComparison.Ordinal)
            || !string.Equals(Mode, mode, StringComparison.Ordinal)) return false;
        Confirmed = true; return true;
    }

    internal bool Rebind(object world, object oldBody, object newBody)
    {
        if (FinalRequested || !CanObserve(world) || !ReferenceEquals(Body, oldBody) || newBody == null
            || ReferenceEquals(oldBody, newBody) || !ReferenceEquals(_pendingStatisticsBody, newBody)) return false;
        _completedExperience = EarnedExperience;
        _bodyExperience = 0;
        _pendingStatisticsBody = null;
        Body = newBody; return true;
    }

    internal bool ObserveExperience(object world, object body, long rawExperience)
    {
        if (!CanApply(world, body) || FinalRequested || ReplacementStatisticsPending) return false;
        if (rawExperience < 0 || rawExperience > int.MaxValue) throw new InvalidOperationException("Invalid native Cola experience.");
        // A replacement's native BeginStatisticsSession resets the SAME shared
        // profile before Replaced. Retain the last monotonic observation until
        // the exact physical replacement commits it once.
        _bodyExperience = Math.Max(_bodyExperience, (int)rawExperience);
        _ = EarnedExperience; // checked before publishing or retiring this body
        return true;
    }

    internal bool BeforeReplacementStatistics(object world, object oldBody, object newBody, long rawExperience)
    {
        if (newBody == null || ReferenceEquals(oldBody, newBody) || ReplacementStatisticsPending
            || !ObserveExperience(world, oldBody, rawExperience)) return false;
        _pendingStatisticsBody = newBody;
        return true;
    }

    internal bool CanObserve(object world)
        => !Released && !Failed && !Finished && Confirmed && World != null && ReferenceEquals(World, world);
    internal bool CanApply(object world, object body)
        => CanObserve(world) && body != null && ReferenceEquals(Body, body);
    internal bool BeginFinal(object world)
    {
        if (!CanObserve(world) || ReplacementStatisticsPending) return false;
        FinalRequested = true; return true;
    }
    internal bool ObserveStatisticsEnd(object world, object body)
    {
        if (!FinalRequested || !CanApply(world, body)) return false;
        StatisticsCompleted = true; return true;
    }
    internal bool Finish()
    {
        if (Released || Failed || Finished || !Confirmed || !StatisticsCompleted) return false;
        Finished = true; return true;
    }
    internal void Fail() => Failed = true;
    internal void Release() => Released = true;
}
