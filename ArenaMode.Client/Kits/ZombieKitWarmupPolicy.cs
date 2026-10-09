using ArenaMode.Shared;
using System;

namespace ArenaMode.Kits;

/// <summary>
/// Optional work only: one-life Zombies never draws a later random kit.
/// Required per-kit loading, reservation and refill ownership are unchanged.
/// Callers supply the session's armed/active mode, never mutable menu settings.
/// </summary>
internal static class ZombieKitWarmupPolicy
{
    internal static bool AllowsOptionalCatalogue(string mode)
        => !string.Equals(mode, ArenaPopulationPolicy.Zombie, StringComparison.Ordinal);

    internal static KitPreloadScope LoadingScope(KitPreloadScope requested, string mode)
        => !AllowsOptionalCatalogue(mode) && requested == KitPreloadScope.WholeCatalogue
            ? KitPreloadScope.InitialKit
            : requested;
}
