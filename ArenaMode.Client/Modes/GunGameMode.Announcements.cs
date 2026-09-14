using ArenaMode.Audio;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class GunGameMode
{
    internal ArenaAnnouncementSnapshot AnnouncementSnapshot()
    {
        if (_match == null || _givenUp || _ladder.Count == 0) return default;
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        bool equipping = Respawn.LiveKitRespawn.IsInitialSwapPending
            || Respawn.ArenaRespawnDriver.IsRespawnInProgress
            || Combat.WeaponSwapService.IsSwapping || _preloading || _loadoutDirty;
        return new ArenaAnnouncementSnapshot
        {
            Active = true, Epoch = 1, Now = Time.time,
            Live = !Respawn.LiveKitRespawn.IsInitialSwapPending,
            Preparing = Respawn.LiveKitRespawn.IsInitialSwapPending,
            WeaponTier = !equipping && player?.HealthController?.IsAlive == true ? _tier : null,
            WeaponCount = _ladder.Count, LadderCycle = _wins,
        };
    }
}
