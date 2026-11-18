using ArenaMode.Combat.Translocator;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Areas;

/// <summary>
/// One exact synchronous human teleport may use physical disc provenance rather
/// than the bot navigation network. All other teleport callers retain their
/// existing area guard. No ambient bypass survives the native call.
/// </summary>
internal static class ArenaAreaTranslocatorGate
{
    internal sealed class Ticket : IDisposable
    {
        internal readonly Player Player;
        internal readonly MovementContext Context;
        internal readonly object Controller, Area;
        internal readonly GameWorld World;
        internal readonly AbstractGame Game;
        internal readonly Vector3 Target;
        internal bool Consumed;
        internal Ticket(Player player, Vector3 target)
        {
            Player = player; Context = player.MovementContext; Controller = player.CharacterController;
            World = Singleton<GameWorld>.Instance; Game = Singleton<AbstractGame>.Instance;
            Area = ArenaAreaRuntime.CurrentArea; Target = target;
        }
        public void Dispose() { if (ReferenceEquals(_current, this)) _current = null; }
    }
    private static Ticket _current;

    internal static Ticket Begin(Player player, Vector3 target)
    {
        if (_current != null || !ArenaAreaNavigationPatches.IsReady
            || !ArenaTranslocatorTeleport.TryValidateDestination(player, target, out _)) return null;
        return _current = new Ticket(player, target);
    }

    internal static bool TryConsume(Player player, Vector3 target)
    {
        var ticket = _current;
        if (!Matches(ticket, player, target) || !ArenaTranslocatorTeleport.TryValidateDestination(player, target, out _)
            || !Matches(ticket, player, target)) return false;
        ticket.Consumed = true;
        return true;
    }

    // Distinguish no owned call from an owned call that failed revalidation.
    // Falling through to the ordinary whole-map allowance on failure would move
    // a stale body even though its disc authorization was refused.
    internal static bool TryResolve(Player player, Vector3 target, out bool allowed)
    {
        allowed = false;
        if (_current == null || !ReferenceEquals(_current.Player, player)) return false;
        allowed = TryConsume(player, target);
        return true;
    }

    private static bool Matches(Ticket ticket, Player player, Vector3 target)
        => ticket != null && !ticket.Consumed && ReferenceEquals(_current, ticket) && ReferenceEquals(ticket.Player, player)
            && ArenaTranslocatorTeleportPolicy.Exact(ticket.Target, target)
            && Singleton<GameWorld>.Instantiated && Singleton<AbstractGame>.Instantiated
            && ReferenceEquals(ticket.World, Singleton<GameWorld>.Instance)
            && ReferenceEquals(ticket.Game, Singleton<AbstractGame>.Instance)
            && ReferenceEquals(ticket.Area, ArenaAreaRuntime.CurrentArea)
            && ReferenceEquals(ticket.Context, player.MovementContext)
            && ReferenceEquals(ticket.Controller, player.CharacterController)
            && ArenaAreaNavigationPatches.IsReady;

    internal static bool ContainsCapsule(Vector3 bottom, Vector3 top, float radius)
    {
        var area = ArenaAreaRuntime.CurrentArea;
        if (area == null) return true;
        if (!ArenaAreaRuntime.IsReady) return false;
        float inset = radius + ArenaAreaCatalog.NavigationInset;
        return area.Bounds.Contains(bottom.x, bottom.y - radius, bottom.z, inset)
            && area.Bounds.Contains(bottom.x, bottom.y + radius, bottom.z, inset)
            && area.Bounds.Contains(top.x, top.y - radius, top.z, inset)
            && area.Bounds.Contains(top.x, top.y + radius, top.z, inset)
            && area.Bounds.ContainsPath(new ArenaAreaPoint(bottom.x, bottom.y, bottom.z),
                new[] { new ArenaAreaPoint(top.x, top.y, top.z) }, true, inset);
    }
}
