using ArenaMode.Modes;
using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Seam between ZombieMode's pure wave policy and the real native spawn
/// path.
///
/// STAGE B (this file): every member here honestly reports "nothing is wired
/// yet" — TryRequest always refuses, the counters always read zero,
/// NudgeIdleZombies and RegisterBackupTargets are no-ops — and the one
/// warning below is logged once per raid so that is visible instead of
/// silently assumed. No method here may claim behavior it does not have.
///
/// STAGE C fills these bodies with: the real BurstSpawnService entry point
/// for an arbitrary Savage role/difficulty (reusing SpawnPair's internals,
/// one point per profile, the existing 25-70 m ring); the native infection-
/// event bridge (Harmony patches on BotsEventsController.Activate/Dispose
/// and the static BotHalloweenWithZombies.GetProfilesOnStart/
/// AddBackupProfiles — see the orchestrator's decision 3, which this stage
/// deliberately does not implement); and the AddPointToSearch nudge for
/// idle zombies. When Stage C wires a body, it must also correct this doc
/// comment and IsReady — a comment that disagrees with the code is a bug
/// (see AGENTS.md).
/// </summary>
internal static class ZombieSpawnService
{
    private static bool _warned;

    /// <summary>True once Stage C's native wiring is live. Always false in this stage.</summary>
    internal static bool IsReady => false;

    /// <summary>Spawn requests accepted but not yet confirmed alive or dead. Always 0 in this stage.</summary>
    internal static int Pending => 0;

    /// <summary>Called by ZombieMode at both OnMatchStart and OnMatchEnd (idempotent).</summary>
    internal static void Reset() => _warned = false;

    /// <summary>Always refuses: nothing can be spawned until Stage C wires the native path.</summary>
    internal static bool TryRequest(WildSpawnType role, BotDifficulty difficulty)
    {
        WarnOnce();
        return false;
    }

    /// <summary>Always 0: nothing is spawned yet in this stage, so nothing can be alive.</summary>
    internal static int CountAliveInfected()
    {
        WarnOnce();
        return 0;
    }

    /// <summary>No-op in this stage: there are no bots to nudge toward the player yet.</summary>
    internal static void NudgeIdleZombies(Vector3 playerPosition) => WarnOnce();

    /// <summary>No-op in this stage: nothing registers with the native backup loader yet.</summary>
    internal static void RegisterBackupTargets(IReadOnlyList<ZombieProfilePlan.Entry> targets) => WarnOnce();

    private static void WarnOnce()
    {
        if (_warned)
            return;

        _warned = true;
        Plugin.ModLogger.LogWarning(
            "Zombie mode: the native spawn path is not wired yet (Stage C) — waves will not produce any bots this raid.");
    }
}
