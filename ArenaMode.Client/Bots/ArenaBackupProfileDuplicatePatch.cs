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
/// (docs/reviews/ZOMBIE-ESCALATION-20261119.md, "The profile pool").
///
/// The game (decompile-verified, ../_decompiled): <c>BotProfileClient</c> builds its
/// <c>BotProfileBackuploader</c> with <c>LoadProfiles</c> as the loader and <c>LoadedByBackup</c> as the
/// callback. <c>LoadProfiles</c> already appends the server's answer to the pool
/// (<c>_simpleProfiles.AddRange(array)</c>), and <c>BotProfileBackuploader.DoLoad</c> then hands the
/// same profiles to the callback, which appends them again. Every profile that arrives through the
/// backup loader is therefore in the pool twice, as the same object.
///
/// What the second copy does: the first use takes one copy out of the pool and spawns. When the
/// second copy is drawn, <c>BotCreationData.Create</c> finds its id in the static
/// <c>_generatedHash</c>, reports "generate profile with same id" through Debug.LogError (no such
/// line is in the BepInEx logs of the measured raids, which hold no managed Unity log line from a
/// raid at all) and leaves the profile out. The creation data comes back empty, which for Arena is
/// <c>Arena burst spawn: no profiles for &lt;role&gt; — request dropped.</c> and the pause after a
/// failed dispatch. No request goes to the server for it, because the pool did answer. Measured on
/// 2026-11-20 in a Zombie raid at the default size: 251 dropped requests in 13 minutes and waves that
/// filled at 0.6 to 0.7 infected per second instead of 2.2 to 2.8. Zombies show it most because their
/// standing backup targets keep the loader busy; a PMC pool reloaded by the loader has the same
/// second copies.
///
/// The prefix replaces the callback's list by the profiles the pool does not hold yet, by object
/// reference (<see cref="BackupProfileDuplicatePolicy.NotYetPooled{T}"/>). It measures instead of
/// assuming: a loader that did not store its answer first (another build, another mod's loader) finds
/// nothing in the pool and everything is added as before. One window stays open on purpose: a profile
/// that a spawn took out of the pool between the two appends is not in the pool here and is added
/// once more, which is today's behaviour for that one profile.
///
/// Scope: the exact active Arena raid and its own profile client, every mode. A backup load that
/// finishes before the raid start is confirmed runs natively. Fails open: any exception leaves the
/// native call untouched. Nothing is removed from the pool, no request is changed and the loader's
/// targets, timers and counters are not touched.
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

    [PatchPrefix]
    internal static void Prefix(BotProfileClient __instance, ref List<Profile> profiles)
    {
        // ArenaSession.IsExactActiveRaid below also requires the active Arena session.
        try
        {
            if (profiles == null || profiles.Count == 0 || !IsExactActiveRaidOf(__instance))
                return;

            var pool = __instance._simpleProfiles;
            if (pool == null || pool.Count == 0)
                return;

            // Decide first, replace second: an exception while reading the pool leaves the call as it was.
            var fresh = BackupProfileDuplicatePolicy.NotYetPooled(profiles, pool, out int duplicates);
            if (duplicates == 0)
                return;

            int reloaded = profiles.Count;
            profiles = fresh;
            Plugin.ModLogger.LogDebug(
                $"Arena profile backup: {duplicates} of {reloaded} reloaded bot profile(s) were already in the pool "
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
