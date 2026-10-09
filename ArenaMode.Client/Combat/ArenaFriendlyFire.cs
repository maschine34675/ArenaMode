using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat;

/// <summary>The raid-frozen mutator is independent of AI hostility and team cosmetics.</summary>
internal static class ArenaFriendlyFire
{
    private sealed class Report { internal bool Emitted; }
    private static readonly ConditionalWeakTable<object, Report> Reports = new();

    internal static bool ShouldBlockFriendlyDamage(Player victim, IPlayer attacker, bool reportDamage = true)
    {
        if (!ArenaSession.IsActive || ArenaSession.FriendlyFireEnabledSelection
            || victim == null || ReferenceEquals(attacker, null)
            || ReferenceEquals(victim, attacker)
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated)
            return false;
        try
        {
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance;
            if (game == null || game.Status != GameStatus.Started
                || !ArenaSession.IsExactActiveRaid(game, world)
                || victim.HealthController?.IsAlive != true
                || !ReferenceEquals(world.GetAlivePlayerByProfileID(victim.ProfileId), victim)
                || !ReferenceEquals(world.GetEverExistedPlayerByID(victim.ProfileId), victim))
                return false;
            var match = ArenaMatchService.Current;
            var mode = match?.Mode;
            bool blocked = false;
            if (mode is TeamDeathmatchMode)
                blocked = TeamDeathmatchRelations.ShouldBlockFriendlyDamage(victim, attacker);
            else if (mode is TacticalMode tactical)
                blocked = tactical.ShouldBlockFriendlyDamage(victim, attacker);
            else if (mode is ZombieMode zombie)
                blocked = zombie.ShouldBlockFriendlyDamage(victim, attacker);
            if (blocked && reportDamage && ReferenceEquals(attacker, world.MainPlayer))
            {
                // Weak match ownership: no raid/body retention and no tick spam.
                var report = Reports.GetValue(match, _ => new Report());
                if (!report.Emitted)
                {
                    report.Emitted = true;
                    try { Plugin.ModLogger.LogInfo($"Arena {mode.GetType().Name}: friendly fire blocked (local damage hook, source={attacker.ProfileId}, victim={victim.ProfileId}; frozen mutator OFF)."); }
                    catch (Exception) { } // Diagnostics must never change the damage decision.
                }
            }
            return blocked;
        }
        catch (Exception)
        {
            // An unreadable/foreign actor does not gain damage immunity. Staging
            // safety remains owned by the separate fail-closed staging guards.
        }
        return false;
    }
}
