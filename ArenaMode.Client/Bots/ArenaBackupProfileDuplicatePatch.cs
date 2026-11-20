using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps the game's backup loader from storing every bot profile it reloads a second time
/// (docs/reviews/ZOMBIE-ESCALATION-20261119.md, "The profile pool held every reloaded profile twice").
///
/// The game (decompile-verified, ../_decompiled): <c>BotProfileClient</c> builds its
/// <c>BotProfileBackuploader</c> with <c>LoadProfiles</c> as the loader and <c>LoadedByBackup</c> as the
/// callback. <c>LoadProfiles</c> already appends the server's answer to the pool
/// (<c>_simpleProfiles.AddRange(array)</c>), and <c>BotProfileBackuploader.DoLoad</c> then hands the
/// same profiles to the callback, which appends them again. Every profile that arrives through the
/// backup loader is therefore in the pool twice, as the same object. The raid-start preload and the
/// miss path of <c>CreateProfile</c> store their answer once.
///
/// What the second copy does: the first use takes one copy out of the pool and spawns. When the
/// second copy is drawn, <c>BotCreationData.Create</c> finds its id in the static
/// <c>_generatedHash</c>, reports "generate profile with same id" through Debug.LogError (no such
/// line is in the saved logs; whether that call reaches the BepInEx log at all was not checked) and
/// leaves the profile out. The creation data comes back empty, which for Arena is
/// <c>Arena burst spawn: no profiles for &lt;role&gt; — request dropped.</c> and the pause after a
/// failed dispatch. No request goes to the server for it, because the pool did answer. Zombies show it
/// most because their standing backup targets keep the loader busy; a PMC pool reloaded by the loader
/// has the same second copies.
///
/// Measured on SPT-GPU-2 on 2026-11-20, Zombies at the default size without player kills
/// (docs/reports/2026-11/GPU-ZOMBIE-PROFILE-POOL-20261120.md): without this patch 251 dropped requests
/// in 13 minutes and waves 5 to 7 filling at 0.6 to 0.7 infected per second; with it every one of 17
/// reloads (1530 profiles) was already in the pool, no request was dropped and waves 5 to 12 filled at
/// 2.4 to 2.7 per second.
///
/// The prefix replaces the callback's list by the profiles the pool does not hold yet, by object
/// reference (<see cref="BackupProfileDuplicatePolicy.NotYetPooled{T}"/>). It measures instead of
/// assuming: a loader that did not store its answer first (another build, another mod's loader) finds
/// nothing in the pool and everything is added as before. One window stays open on purpose: a profile
/// that a spawn took out of the pool between the two appends is not in the pool here and is added
/// once more, which is the game's behaviour for that one profile (in the measured run no reload had
/// such a profile).
///
/// What changes besides the pool: the loader's own stock count (<c>CacheSimpleProfilesChecker</c>
/// through <c>CurCacheddProfilesCount</c>) counted both copies, so it now sees the real stock and
/// orders a pair again when the real stock is more than 3 below its target, where it used to wait
/// until the doubled count was. Its targets, timers, requests and counters are not touched, nothing is
/// removed from the pool, and a miss still asks the server itself (3 wanted; SPT answers with a whole
/// presetBatch).
///
/// Scope: the exact active Arena raid and its own profile client, every mode. A backup load that
/// finishes before the raid start is confirmed runs as in the game. Fails open: any exception leaves
/// the native call untouched.
/// </summary>
internal sealed class ArenaBackupProfileDuplicatePatch : ModulePatch
{
    private static bool _loggedFailure;

    /// <summary>Per raid, so a failure in one raid is reported again in the next.</summary>
    internal static void Reset() => _loggedFailure = false;

    /// <summary>
    /// Enabled behind its own guard like its siblings: a signature that drifted in a game build must
    /// cost this correction only, not every patch listed after it.
    /// </summary>
    internal static void EnablePatch()
    {
        try
        {
            new ArenaBackupProfileDuplicatePatch().Enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena backup profile correction unavailable; reloaded bot profiles are pooled twice as in the game: {ex.Message}");
        }
    }

    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(BotProfileClient), nameof(BotProfileClient.LoadedByBackup),
            new[] { typeof(List<Profile>) });

    /// <summary>
    /// <c>__0</c> is the callback's only argument, bound by position so that the binding does not rest
    /// on a parameter name; assigning it replaces the list the game's method receives.
    /// </summary>
    [PatchPrefix]
    internal static void Prefix(BotProfileClient __instance, ref List<Profile> __0)
    {
        if (!ArenaSession.IsActive)
            return;

        try
        {
            var reloaded = __0;
            if (reloaded == null || reloaded.Count == 0 || !IsExactActiveRaidOf(__instance))
                return;

            var pool = __instance._simpleProfiles;
            if (pool == null || pool.Count == 0)
                return;

            // Decide first, replace second: an exception while reading the pool leaves the call as it was.
            var fresh = BackupProfileDuplicatePolicy.NotYetPooled(reloaded, pool, out int duplicates);
            if (duplicates == 0)
                return;

            __0 = fresh;
            Plugin.ModLogger.LogDebug(
                $"Arena profile backup: {duplicates} of {reloaded.Count} reloaded bot profile(s) were already in the pool "
                + "and were not added a second time.");
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena backup profile correction skipped; this reload is pooled as in the game: {ex.Message}");
            }
        }
    }

    private static bool IsExactActiveRaidOf(BotProfileClient provider)
    {
        if (provider == null || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated)
            return false;

        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        return ArenaSession.IsExactActiveRaid(game, world)
            && ((IBotGame)game).BotsController?.BotSpawner?._botCreator is BotCreatorClient creator
            && ReferenceEquals(creator._game, game) && ReferenceEquals(creator._creatorProfile, provider);
    }
}
