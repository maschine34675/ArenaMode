using System.Runtime.CompilerServices;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ArenaMode.Server.Services;

/// <summary>
/// Wave generation returns deferred PLINQ and SPT clones its details per bot.
/// Keep wave ownership weak and bridge ONLY the synchronous TryGenerateSingleBot
/// -> PrepareAndGenerateBot call. No thread/async scope spans the wave itself.
/// The APBS hook reads an exact cloned-object permit, never an ambient session.
/// </summary>
internal static class ArenaBotTierOwnership
{
    internal sealed record Permit(ArenaSessionService Sessions, ArenaSessionService.BotTierIdentity Identity,
        string? Role, string? Side, bool IsPmc, int? Level, int Min, int Max)
    {
        internal bool Matches(BotGenerationDetails details) => !details.IsPlayerScav
            && details.IsPmc == IsPmc && details.Role == Role && details.Side == Side
            && details.PlayerLevel == Level
            && details.LocationSpecificPmcLevelOverride?.Min == Min
            && details.LocationSpecificPmcLevelOverride?.Max == Max
            && string.Equals(details.Location, Identity.Map, StringComparison.OrdinalIgnoreCase);

        internal bool IsLive() => ReferenceEquals(ArenaContext.Sessions, Sessions)
            && Sessions.IsCurrentBotTierIdentity(Identity)
            && ArenaContext.Config.Maps.TryGetValue(Identity.Map, out var map) && map.ScaleBotTier;
    }

    internal sealed class Scope(Scope? previous, Permit? permit)
    {
        internal readonly Scope? Previous = previous;
        internal Permit? Pending = permit;
    }

    private static readonly ConditionalWeakTable<BotGenerationDetails, Permit> Waves = new();
    private static readonly ConditionalWeakTable<BotGenerationDetails, Permit> Clones = new();
    [ThreadStatic] private static Scope? _scope;

    internal static void ForgetWave(BotGenerationDetails details) => Waves.Remove(details);

    internal static void MarkWave(BotGenerationDetails details, ArenaSessionService sessions,
        ArenaSessionService.BotTierIdentity identity)
    {
        if (details.IsPlayerScav || details.LocationSpecificPmcLevelOverride is not { } range) return;
        Waves.AddOrUpdate(details, new(sessions, identity, details.Role, details.Side, details.IsPmc,
            details.PlayerLevel, range.Min, range.Max));
    }

    internal static Scope Begin(MongoId sessionId, BotGenerationDetails? details)
    {
        // Shadow first, even if an invalid caller supplied null or a guard
        // throws. Such a failure must not expose an outer pending permit.
        var scope = new Scope(_scope, null);
        _scope = scope;
        try
        {
            if (details != null && Waves.TryGetValue(details, out var permit)
                && permit.Identity.Session == sessionId.ToString()
                && permit.Matches(details) && permit.IsLive()) scope.Pending = permit;
        }
        catch
        {
            // Return the empty shadow so Harmony's finalizer can restore the
            // prior scope even when an optional ownership guard failed.
            scope.Pending = null;
        }
        return scope;
    }

    internal static void End(Scope? scope)
    {
        if (scope != null) _scope = ReferenceEquals(_scope, scope) ? scope.Previous : null;
    }

    internal static void BindClone(MongoId sessionId, BotGenerationDetails? details)
    {
        var permit = _scope?.Pending;
        if (_scope != null) _scope.Pending = null; // Single use, including rejected/nested calls.
        if (details == null) return;
        Clones.Remove(details);
        if (permit != null && permit.Identity.Session == sessionId.ToString()
            && permit.Matches(details) && permit.IsLive()) Clones.AddOrUpdate(details, permit);
    }

    internal static void ReleaseClone(BotGenerationDetails? details)
    {
        if (details != null) Clones.Remove(details);
    }

    internal static bool TryGetRange(BotGenerationDetails details, MinMax<int> templateRange,
        int maxAvailableLevel, out MinMax<int>? range)
    {
        range = null;
        if (maxAvailableLevel < 1 || !Clones.TryGetValue(details, out var permit)
            || !permit.Matches(details) || !permit.IsLive()) return false;

        // Preserve the native template minimum and experience-table ceiling.
        // As with SPT's location override, its template maximum is superseded.
        int ceiling = Math.Min(79, maxAvailableLevel);
        int floor = Math.Clamp(templateRange.Min, 1, ceiling);
        range = new(Math.Clamp(permit.Min, floor, ceiling), Math.Clamp(permit.Max, floor, ceiling));
        return true;
    }
}
