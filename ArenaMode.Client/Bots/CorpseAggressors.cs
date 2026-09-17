using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps Arena from taking away a body that somebody is still owed a death by.
///
/// `Player.ManageAggressor` remembers who hurt you in `LastAggressor`
/// (`EFT/Player.cs:29526,29558`), and `Player.OnDead` hands that reference to
/// `OnBeenKilledByAggressor` as its FIRST statement (`30099-30101`), which
/// dereferences it (`aggressor.AIData?.KillEnemy(this)` and, when the aggressor
/// is still registered, `aggressor.Position`). Nothing ever clears the field
/// when the aggressor's body goes away.
///
/// While a body was merely hidden that reference stayed perfectly usable. Once
/// Arena destroys the body it is a landmine with a delay on it, and the delay is
/// however long the victim takes to die: the 2026-09-17 release raid logged five
/// of them, every single one from `Bleeding.BleedHealth` — a bot that had been
/// wounded, whose attacker died and was released, and who then bled out.
///
/// The consequence is not a log line. The throw happens in the first statement
/// of `OnDead`, so the REST of that death never runs: no corpse is created, no
/// `OnPlayerDead`, no `BotsController.BotDied` — which is the spawner
/// bookkeeping whose leak starves bot refills, the exact wreckage the original
/// destroy attempt was abandoned for.
///
/// So a body stays hidden while any living player still names it. Bleeding is
/// bounded — it kills or is healed — so the body becomes releasable soon after,
/// and until then it costs what a hidden body has always cost. The list of live
/// players is the world's own and short; nothing is searched and nothing is
/// written. Reading the protected field needs one reflected accessor, cached.
/// </summary>
internal static class CorpseAggressors
{
    private static AccessTools.FieldRef<Player, IPlayer> _lastAggressor;
    private static bool _bound, _reportedFailure;

    internal static void Reset()
    {
        _reportedFailure = false;
    }

    /// <summary>
    /// True while any living player still points at this body, and true as well
    /// when that cannot be established. Fails CLOSED, unlike the trigger-zone
    /// retirement next door, and for the opposite reason: what is at stake here
    /// is a death that never completes, not a line in the log.
    /// </summary>
    internal static bool IsPendingAggressor(Player body, GameWorld world)
    {
        if (body == null || world == null)
            return false;

        try
        {
            if (!Bind())
                return true;

            var alive = world.AllAlivePlayersList;
            if (alive == null)
                return true;

            for (var i = 0; i < alive.Count; i++)
            {
                var victim = alive[i];
                if (victim == null || ReferenceEquals(victim, body))
                    continue;

                // Reference equality on purpose. The field is an IPlayer, so
                // Unity's lifetime operator does not apply to it at all — which
                // is exactly why the native null check upstream does not save
                // us — and a profile id would match every body this participant
                // ever had.
                if (ReferenceEquals(_lastAggressor(victim), body))
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
            return true;
        }
    }

    private static bool Bind()
    {
        if (_bound)
            return _lastAggressor != null;

        _bound = true;
        try
        {
            var field = AccessTools.Field(typeof(Player), "LastAggressor");
            if (field == null || field.FieldType != typeof(IPlayer) || field.IsStatic)
                throw new MissingMemberException("Player.LastAggressor changed");

            _lastAggressor = AccessTools.FieldRefAccess<Player, IPlayer>(field);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
            return false;
        }
    }

    private static void WarnOnce(Exception ex)
    {
        if (_reportedFailure)
            return;

        _reportedFailure = true;
        Plugin.ModLogger.LogWarning(
            "Arena corpse release cannot read pending aggressors ("
            + ex.GetBaseException().Message
            + "); no body will be taken out of the scene while that holds.");
    }
}
