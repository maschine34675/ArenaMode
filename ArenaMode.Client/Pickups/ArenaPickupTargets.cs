using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Pickups;

internal readonly struct ArenaPickupTarget
{
    internal readonly object Identity, Health;
    internal readonly int Generation, Appearance;
    internal readonly string Id;
    internal readonly Vector3 Position;
    internal readonly float Utility;
    internal ArenaPickupTarget(object identity, object health, int generation, int appearance, string id, Vector3 position, float utility)
    { Identity = identity; Health = health; Generation = generation; Appearance = appearance; Id = id; Position = position; Utility = utility; }
}

// Exposes live appearances, never raw layout points or an enemy-target lease.
internal static partial class ArenaPickupRuntime
{
    internal static void GetBotTargets(Player player, List<ArenaPickupTarget> targets)
    {
        targets.Clear();
        if (player?.IsAI != true || !IsLiveActor(player)) return;
        foreach (var entry in Entries)
        {
            float utility = TargetUtility(player, entry);
            if (utility <= 0) continue;
            targets.Add(new ArenaPickupTarget(entry, player.ActiveHealthController, Generation, entry.State.Appearance,
                entry.Definition.Id, entry.Floor, utility));
        }
    }

    internal static bool IsTargetCurrent(Player player, ArenaPickupTarget target)
        => player?.IsAI == true && target.Generation == Generation && ReferenceEquals(target.Health, player.ActiveHealthController)
        && target.Identity is Entry entry && Entries.Contains(entry) && entry.State.Appearance == target.Appearance
        && TargetUtility(player, entry) > 0;

    private static float TargetUtility(Player player, Entry entry)
    {
        if (entry.State.Phase != ArenaPickupPhase.Available || entry.Preparing || entry.NeedsEntry
            || entry.MustLeave.Contains(player) || !Actors.TryGetValue(player, out var sample) || sample.Fresh
            || !CanCollect(player, entry.Definition.Kind)) return 0;
        var effects = ArenaPowerups.Read(player);
        return entry.Definition.Kind switch
        {
            ArenaPickupKind.Health => ArenaHealthPickup.Missing(player) >= 15f ? Math.Min(ArenaHealthPickup.Amount, ArenaHealthPickup.Missing(player)) * 1.5f : 0,
            ArenaPickupKind.DoubleDamage => effects.DamageSeconds <= 3f ? 90f : 0,
            ArenaPickupKind.Shield => effects.ShieldPoints < 110f || effects.ShieldSeconds <= 3f ? 70f : 0,
            _ => 0
        };
    }
}
