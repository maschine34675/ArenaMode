using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Gun Game: a pooled PMC profile keeps the gear of the moment it was generated, so the ramp of the
/// server's level band (docs/reviews/DIFFICULTY-MODEL-20261118.md) only reaches bots whose profile is
/// generated late. The client serves a spawn from its pool of already generated profiles first
/// (BotProfileClient._simpleProfiles), and SPT rounds every requested (role, difficulty) pair up to
/// presetBatch (15 for a PMC), so one request fills the pool with profiles of one single moment.
/// Measured natively: PMCs of the first nine minutes were still level 2..7 where the ramp stood at 41.
///
/// Before a PMC demand, this removes the UNUSED pool entries of exactly the demanded (role, difficulty)
/// whose level is stale by <see cref="ArenaDifficultyPolicy.IsGunGameGearStale"/>. The native miss path
/// then asks the server, which answers with a fresh batch at the current band. Nothing else is touched:
/// no other role or difficulty, no assigned profile, no backup loader, no request. Scav roles are left
/// alone (their gear barely follows their level, and their batch is 45).
///
/// Per (role, difficulty) at most one purge every <see cref="PurgeIntervalSeconds"/> of unscaled time,
/// so a server that answers below what the client expects costs one fresh batch per interval instead of
/// one per spawn. The state is per world and is also reset by <see cref="Reset"/>.
///
/// It does not cover profiles that are already assigned to a bot, and at the end of the ramp the
/// pool can still hold profiles up to centre - 5 (the rule's slack, the server's band spread).
/// The same hook shape, with the same safety conditions, was the v0.6.0 Team Deathmatch clock patch.
/// </summary>
internal sealed class ArenaGunGameFreshGearPatch : ModulePatch
{
    /// <summary>Minimum unscaled seconds between two purges of the same (role, difficulty).</summary>
    internal const float PurgeIntervalSeconds = 30f;

    private static readonly Dictionary<(WildSpawnType, BotDifficulty), float> _lastPurge = new();
    private static GameWorld _world;
    private static bool _loggedFailure;

    /// <summary>Per raid: the next demand starts with no purge interval running, and a failure is logged again.</summary>
    internal static void Reset()
    {
        _world = null;
        _loggedFailure = false;
    }

    /// <summary>
    /// Enabled behind its own guard like ArenaProfilePreloadPatch: a signature that drifted in a game
    /// build must cost this refinement only, not every patch listed after it.
    /// </summary>
    internal static void EnablePatch()
    {
        try
        {
            new ArenaGunGameFreshGearPatch().Enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena Gun Game fresh gear unavailable; pooled bot profiles are served as before: {ex.Message}");
        }
    }

    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(BotProfileClient), nameof(BotProfileClient.CreateProfile),
            new[] { typeof(BotCreationData), typeof(CancellationToken), typeof(bool) });

    [PatchPrefix]
    internal static void Prefix(BotProfileClient __instance, BotCreationData data,
        CancellationToken cancellationToken, bool withDelete)
    {
        if (!withDelete)
            return;

        // ArenaSession.IsExactActiveRaid below also requires the active Arena session.
        try
        {
            if (cancellationToken.IsCancellationRequested || data == null || data.SpawnStopped
                || data._profileData?.TryGetRole(out var role, out var difficulty) != true
                || (role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR)
                || !IsExactActiveRaidOf(__instance, data, out GameWorld world)
                || ArenaDifficulty.CurrentGunGameGearLevel is not int current)
                return;

            if (!ReferenceEquals(world, _world))
            {
                _lastPurge.Clear();
                _world = world;
            }

            float now = Time.unscaledTime;
            var key = (role, difficulty);
            if (_lastPurge.TryGetValue(key, out float last) && now - last < PurgeIntervalSeconds)
                return;

            // Decide first, remove second: an exception while reading the pool leaves it untouched.
            var stale = new HashSet<Profile>();
            int lowest = int.MaxValue;
            foreach (var profile in __instance._simpleProfiles)
            {
                var info = profile?.Info;
                if (info?.Settings == null || info.Settings.Role != role || info.Settings.BotDifficulty != difficulty
                    || !ArenaDifficultyPolicy.IsGunGameGearStale(info.Level, current))
                    continue;
                lowest = Math.Min(lowest, info.Level);
                stale.Add(profile);
            }

            if (stale.Count > 0)
            {
                __instance._simpleProfiles.RemoveAll(stale.Contains);
                _lastPurge[key] = now;
                Plugin.ModLogger.LogDebug(
                    $"Arena Gun Game: dropped {stale.Count} pooled {role}/{difficulty} profile(s), lowest level {lowest}, "
                    + $"gear level now {current}; the next one is generated fresh.");
            }
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena Gun Game fresh gear skipped; native generation retained: {ex.Message}");
            }
        }
    }

    private static bool IsExactActiveRaidOf(BotProfileClient provider, BotCreationData data, out GameWorld world)
    {
        world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        return ArenaSession.IsExactActiveRaid(game, world) && game?.Status == GameStatus.Started
            && ((IBotGame)game).BotsController?.BotSpawner?._botCreator is BotCreatorClient creator
            && ReferenceEquals(creator._game, game) && ReferenceEquals(creator._creatorProfile, provider)
            && ReferenceEquals(data._botCreator, creator);
    }
}
