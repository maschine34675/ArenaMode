using System;
using System.Runtime.CompilerServices;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using Redeploy.Core.Compat;

namespace ArenaMode.Compat;

/// <summary>Arena alone owns bot medical fallback. Core owns replacement-player safety.</summary>
internal static class ArenaMedicalBodyScope
{
    private sealed class Lease
    {
        internal readonly GameWorld World;
        internal Lease(GameWorld world) => World = world;
    }

    private static readonly ConditionalWeakTable<Player, Lease> Bodies = new();
    private static IDisposable _registration;

    internal static void Initialize()
        => _registration ??= BodyReplacementCompatibility.RegisterHealingScope(Owns);

    internal static void Shutdown()
    {
        _registration?.Dispose();
        _registration = null;
    }

    private static bool Owns(Player body)
    {
        if (ReferenceEquals(body, null)) return false;
        var bodyWorld = body.GameWorld;
        if (ReferenceEquals(bodyWorld, null)) return false;
        if (Bodies.TryGetValue(body, out var lease))
        {
            if (ReferenceEquals(lease.World, bodyWorld)) return true;
            Bodies.Remove(body);
        }
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        if (ReferenceEquals(game, null) || ReferenceEquals(world, null)
            || !ReferenceEquals(game.GameWorld, world) || !ReferenceEquals(bodyWorld, world)
            || !Singleton<IBotGame>.Instantiated || !ReferenceEquals(Singleton<IBotGame>.Instance, game)
            || !ArenaSession.IsExactActiveRaid(game, world)) return false;
        Bodies.Add(body, new Lease(bodyWorld));
        return true;
    }
}
