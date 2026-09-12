using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>
/// Mode factory keyed by stable id.
/// </summary>
internal static class ModeRegistry
{
    private static readonly Dictionary<string, Func<IArenaMode>> Factories =
        new Dictionary<string, Func<IArenaMode>>(StringComparer.OrdinalIgnoreCase)
        {
            [ArenaModeCatalog.DeathmatchId] = () => new RoundsMode(Session.ArenaSession.DeathmatchSelection
                ?? throw new InvalidOperationException("Deathmatch rules were not frozen before arming.")),
            [ArenaModeCatalog.GunGameId] = () => new GunGameMode(),
            [ArenaModeCatalog.TacticalId] = () => new TacticalMode(),
            [ArenaModeCatalog.TeamDeathmatchId] = () => new TeamDeathmatchMode(),
        };

    public const string DefaultId = ArenaModeCatalog.DeathmatchId;

    public static IArenaMode Create(string id)
    {
        if (!string.IsNullOrEmpty(id) && Factories.TryGetValue(ArenaModeCatalog.NormalizeId(id), out var factory))
            return factory();

        if (!string.IsNullOrEmpty(id))
            Plugin.ModLogger.LogWarning($"Unknown arena mode '{id}' — falling back to {DefaultId}.");

        return Factories[DefaultId]();
    }
}
