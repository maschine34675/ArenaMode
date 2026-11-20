using ArenaMode.Compat;
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

namespace ArenaMode.Bots;

/// <summary>
/// Solo Tactical opening rounds (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md): a pooled PMC profile
/// keeps the gear of the moment it was generated, and the client serves a spawn from its pool of
/// already generated profiles first (BotProfileClient._simpleProfiles). SPT rounds every requested
/// (role, difficulty) pair up to presetBatch (15 for a PMC), so the pool made at the raid start holds
/// about 30 PMC profiles of one single moment, which is the round 1 band. Without this patch a
/// five-man squad per round would feed about four rounds from it: round 2 and 3 would meet round 1's
/// gear, and round 3 (the full level) would meet the opening gear. It is the Gun Game staleness
/// (<see cref="ArenaGunGameFreshGearPatch"/>) in a second mode, and a SIBLING of that patch on purpose:
/// Gun Game's file and its 53 mutants stay byte-for-byte as they are, at the price of about forty
/// duplicated lines of pool scan.
///
/// Before a PMC demand, this removes the UNUSED pool entries of exactly the demanded (role,
/// difficulty) whose level lies OUTSIDE the band the server writes for the acknowledged round
/// (<see cref="ArenaDifficultyPolicy.IsTacticalGearOutOfBand"/>: below max(1, centre - 5) or above
/// centre + 5). Both sides, because Tactical's band moves up (round 1 to 3) and a restarted match
/// would move it down; Gun Game's ramp only ever needs the lower side. The native miss path then asks
/// the server, which answers with a fresh batch at the band it now holds. Nothing else is touched: no
/// other role or difficulty, no assigned profile, no backup loader, no request, scav roles (their gear
/// barely follows their level and their batch is 45).
///
/// The centre is <see cref="ArenaTacticalOpening.CurrentGearLevel"/>, the level of the ACKNOWLEDGED
/// round (not of RoundNumber). Why: the server writes the band of the last round it accepted; when a
/// report is lost both sides stay on the previous stage and nothing churns. When the ACK is lost
/// instead the server is one stage ahead of the client's belief; the client then purges toward a band
/// the server does not write, the budget below ends it, and a late ack that raises the acknowledged
/// round moves the centre and so starts the budget over. It is null, and nothing is purged, when the
/// feature does not apply (a refusal, <c>applies:false</c>, no acknowledgement yet, a map without tier
/// scaling, a scav squad, an opening equal to the full level, or a gate of another world).
///
/// Budget: per (role, difficulty) at most <see cref="PurgesPerCentre"/> purges for one centre, and a
/// new centre starts over. This is NOT Gun Game's 30 second interval: a short round 2 followed by
/// round 3 would find the round 3 purge skipped when round 2's purge was less than 30 seconds ago. Two
/// instead of one absorbs the one benign race, a backup-loader refill requested just before the stage
/// change that lands just after the first purge. The budget exists for the picture where the server
/// does NOT write the stage (the band adapter of the bot system is off, so every fresh batch is again
/// outside the band): without it every spawn would cost a 15-profile server round trip.
/// The state is per world (cleared when the world object changes) and is also reset by
/// <see cref="Reset"/>, so a second raid with the same centre starts with a full budget.
///
/// It does not cover profiles that are already assigned to a bot. A pooled profile is read per squad
/// member: a miss yields a batch of 15 that serves the rest of the squad and the next round of the
/// same role while the stage holds. The same hook shape, with the same safety conditions, is the Gun
/// Game patch and was the v0.6.0 Team Deathmatch clock patch.
/// </summary>
internal sealed class ArenaTacticalFreshGearPatch : ModulePatch
{
    /// <summary>Purges allowed per (role, difficulty) for one gear centre.</summary>
    internal const int PurgesPerCentre = 2;

    private static readonly Dictionary<(WildSpawnType, BotDifficulty), (int Centre, int Count, bool Reported)> _budget = new();
    private static object _world;
    private static bool _loggedFailure;
    private static bool _warnedMismatch;

    /// <summary>Per raid: every budget starts over, and a failure or a mismatch is reported again.</summary>
    internal static void Reset()
    {
        _budget.Clear();
        _world = null;
        _loggedFailure = false;
        _warnedMismatch = false;
    }

    /// <summary>
    /// Enabled behind its own guard like ArenaGunGameFreshGearPatch: a signature that drifted in a game
    /// build must cost this refinement only, not every patch listed after it.
    /// </summary>
    internal static void EnablePatch()
    {
        try
        {
            new ArenaTacticalFreshGearPatch().Enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena Tactical fresh gear unavailable; pooled bot profiles are served as before: {ex.Message}");
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
                || !ArenaDifficultyPolicy.IsTacticalMode(ArenaSession.ActiveMode)
                || PitFireTeamCompat.TeamArenaActive
                || ArenaTacticalOpening.CurrentGearLevel is not int centre)
                return;

            if (!ReferenceEquals(world, _world))
            {
                _budget.Clear();
                _warnedMismatch = false;
                _world = world;
            }

            // Decide first, remove second: an exception while reading the pool leaves it untouched.
            var outside = new HashSet<Profile>();
            int lowest = int.MaxValue, highest = int.MinValue;
            foreach (var profile in __instance._simpleProfiles)
            {
                var info = profile?.Info;
                if (info?.Settings == null || info.Settings.Role != role || info.Settings.BotDifficulty != difficulty
                    || !ArenaDifficultyPolicy.IsTacticalGearOutOfBand(info.Level, centre))
                    continue;
                lowest = Math.Min(lowest, info.Level);
                highest = Math.Max(highest, info.Level);
                outside.Add(profile);
            }

            if (outside.Count == 0)
                return;

            var key = (role, difficulty);
            var spent = _budget.TryGetValue(key, out var held) && held.Centre == centre
                ? held
                : (Centre: centre, Count: 0, Reported: false);
            if (spent.Count >= PurgesPerCentre)
            {
                // The server does not write the band the client expects (or the round report is behind):
                // a purge would only fetch another batch outside the band. Say so once per pair and centre.
                if (!spent.Reported)
                {
                    _budget[key] = (spent.Centre, spent.Count, true);
                    Plugin.ModLogger.LogDebug(
                        $"Arena Tactical: {outside.Count} pooled {role}/{difficulty} profile(s) are outside gear level {centre} "
                        + $"but {PurgesPerCentre} purges for this level are spent; they are served as they are.");
                }

                if (!_warnedMismatch)
                {
                    _warnedMismatch = true;
                    Plugin.ModLogger.LogWarning(
                        "Arena Tactical: the server's gear band does not match the round; bot profiles of the pool are served as they are.");
                }

                return;
            }

            __instance._simpleProfiles.RemoveAll(outside.Contains);
            _budget[key] = (spent.Centre, spent.Count + 1, false);
            Plugin.ModLogger.LogDebug(
                $"Arena Tactical: dropped {outside.Count} pooled {role}/{difficulty} profile(s), levels {lowest} to {highest}, "
                + $"gear level now {centre}; the next one is generated fresh.");
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena Tactical fresh gear skipped; native generation retained: {ex.Message}");
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
