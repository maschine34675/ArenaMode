using EFT;
using UnityEngine;
using ArenaMode.Shared;

namespace ArenaMode.Areas;

internal static partial class ArenaAreaRuntime
{
    // Stronger than the native teleport prefix's bool gate: the caller must
    // actually place the body at the sampled, connected point we verified.
    internal static bool TryResolveCrowdRecall(BotOwner bot, Vector3 candidate, out Vector3 placed)
    {
        placed = default;
        if (!TryGetForBot(bot, out _) || !IsReady
            || !Contains(candidate, ArenaAreaCatalog.NavigationInset)
            || !TrySample(candidate, out var sample) || !CompleteConnectedPath(sample)
            || !IsUnoccupied(sample, bot.GetPlayer, null)) return false;
        placed = sample;
        return true;
    }
}
