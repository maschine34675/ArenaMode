using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>
/// Mode factory keyed by stable id. v1 ships Endless only; Rounds (M6) and
/// Gun Game (M7) register here.
/// </summary>
internal static class ModeRegistry
{
    private static readonly Dictionary<string, Func<IArenaMode>> Factories =
        new Dictionary<string, Func<IArenaMode>>(StringComparer.OrdinalIgnoreCase)
        {
            ["endless"] = () => new EndlessDeathmatchMode(),
            ["rounds"] = () => new RoundsMode(),
        };

    public const string DefaultId = "endless";

    public static IArenaMode Create(string id)
    {
        if (!string.IsNullOrEmpty(id) && Factories.TryGetValue(id.Trim(), out var factory))
            return factory();

        if (!string.IsNullOrEmpty(id))
            Plugin.ModLogger.LogWarning($"Unknown arena mode '{id}' — falling back to {DefaultId}.");

        return Factories[DefaultId]();
    }
}
