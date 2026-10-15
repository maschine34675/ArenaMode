using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Stops a Team Deathmatch raid pre-loading three hundred bot profiles it
/// will never use.
///
/// At raid start the client hands the server every role its location declares
/// and turns the answer into Profile objects, inside the loading screen. It
/// asks for about fifty bots — and gets back three hundred and thirty-five,
/// because SPT rounds every distinct (role, difficulty) PAIR up to bot.json's
/// presetBatch and returns all of them: forty-five per assault pair, thirty per
/// marksman, fifteen per PMC. So the bill is written by the number of pairs
/// named, and the counts the client asks for barely matter.
///
/// In this mode almost every pair is dead on arrival. The server's own clone
/// mutation empties location.Waves, closes ABPS's spawn window and silences
/// every boss, precisely so the mode's scoreboard can account for every body.
/// Yet BotProfileClient's constructor walks BossLocationSpawn without ever
/// asking ShallSpawn, so fourteen silenced boss and follower pairs are ordered
/// anyway; and NonWavesSpawnScenario builds its pre-start list even when the
/// non-wave spawner is switched off, adding the assault and marksman pairs on
/// top — the server cannot silence that half at all. The PMC profiles are the
/// final irony: ArenaBotDifficultyClockPatch deliberately DELETES the matching
/// pool entries in THIS mode so every bot is generated fresh against the
/// current tier, so even the fifteen-per-side that could have been used are
/// thrown away. That deletion is what makes the mode safe to skip and is also
/// why Tactical, which spawns its own squads just the same but keeps its pool,
/// is deliberately not included (see ArenaPopulationPolicy).
///
/// Measured on Customs, one raid per machine: three hundred and thirty-five
/// profiles, 3.2 s of loading screen on the fast machine and 8.4 s on the one
/// that runs at sixty frames. All of it for bots that cannot spawn.
///
/// An empty list is a state BSG supports rather than a hole punched in one:
/// TryLoadBotsProfilesOnStart sets StartProfilesLoaded and sends nothing, and
/// the only readers of that flag merely delay a spawn while it is false. A
/// later miss is not fatal either — CreateProfile asks the server for three of
/// whatever it wanted and carries on.
///
/// One thing an empty pool does NOT do is stay empty by itself.
/// BotProfileBackuploader is built with six standing orders of its own —
/// assault and marksman at eight each, across three difficulties — and a five
/// second timer that tops them up against the pool it finds. Emptied, it reads
/// a deficit of forty-eight and re-orders exactly the profiles this just
/// declined, seconds into the match instead of during the loading screen,
/// which is the worse place to pay. So its target table is cleared too. Found
/// by the review; the first version of this patch simply moved the cost.
/// </summary>
internal sealed class ArenaProfilePreloadPatch : ModulePatch
{
    private static bool _loggedFailure;

    /// <summary>Per raid, so a failure in one does not silence the log in the next.</summary>
    public static void Reset() => _loggedFailure = false;

    /// <summary>
    /// Enabled behind its own guard, and not because this target is doubtful.
    ///
    /// ModulePatch.Enable throws PatchException when the target cannot be
    /// resolved, and Plugin.Load enables some forty patches in a row with no
    /// try/catch: a signature that drifted in a future game build would take
    /// every patch listed after this one down with it — the areas, the
    /// movement, the HUD — over an optimisation whose entire benefit is a few
    /// seconds of loading screen. Skipping it costs exactly those seconds.
    /// </summary>
    internal static void EnablePatch()
    {
        try
        {
            new ArenaProfilePreloadPatch().Enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena raid-start profile preload trim unavailable; the native preload is retained: {ex.Message}");
        }
    }

    /// <summary>
    /// The constructor rather than TryLoadBotsProfilesOnStart: it is a plain
    /// method, so there is no async postfix trap, and it runs once the wave
    /// list is fully built and before anything reads it. There is exactly one
    /// construction site in the game (LocalGame.vmethod_1).
    /// </summary>
    protected override MethodBase GetTargetMethod()
        => AccessTools.Constructor(typeof(BotProfileClient), new[]
        {
            typeof(IEftSession),
            typeof(SpawnWave[]),
            typeof(BossLocationSpawn[]),
            typeof(CountTypeBotWave[]),
            typeof(bool),
        });

    [PatchPostfix]
    private static void Postfix(BotProfileClient __instance)
    {
        try
        {
            var loadingMap = Singleton<GameWorld>.Instantiated
                ? Singleton<GameWorld>.Instance?.LocationId
                : null;

            if (!ArenaPopulationPolicy.SkipsProfilePreload(
                    ArenaSession.ArmedForNextRaid,
                    ArenaSession.ArmedMap,
                    ArenaSession.ArmedMode,
                    loadingMap))
            {
                return;
            }

            var waves = __instance?.listWaves;
            if (waves == null || waves.Count == 0)
                return;

            var pairs = waves.Count;
            waves.Clear();

            // Otherwise the backup loader re-orders assault and marksman within
            // seconds of the countdown; see the note above.
            // Counted as PAIRS: TwoDimensionalDictionary.Count is the number of
            // outer keys (two roles), which the first version printed as "2
            // standing orders" for a table that held six.
            var standingOrders = __instance._backupLoader?._targetCount;
            var cleared = standingOrders == null ? 0 : standingOrders.GetAllVals().Count();
            standingOrders?.Clear();

            Plugin.ModLogger.LogDebug(
                $"Arena: skipped the raid-start bot profile preload ({pairs} role/difficulty pair(s), "
                + $"{cleared} standing backup order(s)) — Team Deathmatch spawns its own squads, the server "
                + "has silenced every scheduled wave and boss, and nothing left in that list can spawn either.");
        }
        catch (Exception ex)
        {
            // Never fail a raid over a preload that only costs loading time.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena profile preload trim skipped: {ex}");
            }
        }
    }
}
