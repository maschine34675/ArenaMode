#nullable disable
using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Combat;

/// <summary>
/// Observes actual HP changes once, before native lethal scoring. Player subscribes
/// OnHealthChanged to its controller's HealthChangedEvent; ChangeHealth raises it
/// with the clamped HP delta before ApplyDamage destroys a body part and calls Kill.
/// No prediction from weapon damage, armor damage or retained LastAggressor.
/// </summary>
internal static class ArenaAssistCapture
{
    private static bool _enabled, _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            new HealthChangedPatch().Enable();
            new DeathCleanupPatch().Enable();
            _enabled = true;
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena assist observation unavailable: " + ex.GetBaseException().Message);
    }

    private static bool ScoringOpen(IArenaMode mode)
        => mode != null && !mode.IsComplete && (mode switch
        {
            RoundsMode rounds => rounds.AssistScoringOpen,
            TacticalMode tactical => tactical.AssistScoringOpen,
            TeamDeathmatchMode teams => teams.AssistScoringOpen,
            GunGameMode gunGame => gunGame.AssistScoringOpen,
            ZombieMode zombie => zombie.AssistScoringOpen,
            _ => false
        });

    private static bool IsRegistered(GameWorld world, Player body, bool requireAlive)
        => body != null && !string.IsNullOrEmpty(body.ProfileId)
            && (!requireAlive || body.HealthController?.IsAlive == true)
            && ReferenceEquals(world.GetEverExistedPlayerByID(body.ProfileId), body)
            && (!requireAlive || ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body));

    private static bool TryContext(ArenaMatch match, Player victim, bool requireAlive,
        out GameWorld world, out Player local)
    {
        world = null;
        local = null;
        if (match == null || !ReferenceEquals(ArenaMatchService.Current, match)
            || !ScoringOpen(match.Mode) || ArenaMatchService.TeamStartAbortPending
            || Areas.ArenaAreaRuntime.IsPending || !Singleton<GameWorld>.Instantiated
            || !Singleton<AbstractGame>.Instantiated) return false;
        world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        local = world?.MainPlayer;
        return game != null && game.Status == GameStatus.Started && local != null
            && !local.IsAI && local.IsYourPlayer && ReferenceEquals(game.LocalPlayer, local)
            && ArenaSession.IsExactActiveRaid(game, world)
            && IsRegistered(world, victim, requireAlive)
            && !ArenaAdministrativeDeathGate.IsActive(victim.ProfileId);
    }

    internal static void ObserveHealthChange(Player victim, float diff, DamageInfo info)
    {
        if (!_enabled || !ArenaSession.IsActive || !(diff < 0f) || float.IsInfinity(diff)) return;
        try
        {
            var match = ArenaMatchService.Current;
            if (!TryContext(match, victim, requireAlive: true, out var world, out var local)) return;

            // Every real loss enters the denominator, even an unattributable
            // bleed/fall/friendly hit. Optional identity/classification failures
            // must not inflate another contributor's eventual damage share.
            var damage = new ArenaAssistDamage
            {
                VictimBodyId = ArenaHitHistory.BodyId(victim), Damage = -(double)diff
            };
            try { damage = Attribute(damage, match.Mode, world, local, victim, info); }
            catch (Exception ex) { Warn(ex); }
            match.RecordAssistDamage(damage);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static ArenaAssistDamage Attribute(ArenaAssistDamage damage, IArenaMode mode,
        GameWorld world, Player local, Player victim, DamageInfo info)
    {
        if (!TryHostileSource(mode, world, local, victim, info.Player?.iPlayer,
                info.DamageType, out var attacker, out var friendly)) return damage;

        // Construct atomically: a failed native identity/classification read
        // cannot leave a partially attributed record. Display metadata remains
        // the scoreboard's already established participant entry.
        return new ArenaAssistDamage
        {
            VictimBodyId = damage.VictimBodyId, Damage = damage.Damage,
            AttackerBodyId = ArenaHitHistory.BodyId(attacker),
            AttackerProfileId = attacker.ProfileId,
            AttackerIsLocal = ReferenceEquals(attacker, local) || Fire.ArenaFireRuntime.OwnsDamage(victim, attacker, info.DamageType),
            AttackerIsFriendly = friendly
        };
    }

    private static bool TryHostileSource(IArenaMode mode, GameWorld world, Player local,
        Player victim, IPlayer source, EDamageType damageType, out Player attacker, out bool? friendly)
    {
        attacker = source as Player;
        friendly = null;
        if ((!ArenaCombatAwards.IsCombatDamage(damageType.ToString())
                && !Fire.ArenaFireRuntime.OwnsDamage(victim, source, damageType))
            || ReferenceEquals(attacker, null) || ReferenceEquals(attacker, victim)
            || string.IsNullOrEmpty(attacker.ProfileId)
            || string.Equals(attacker.ProfileId, victim.ProfileId, StringComparison.Ordinal)) return false;

        if (mode is TeamDeathmatchMode)
        {
            // Established weak affiliations deliberately survive a dead thrower.
            // An old/unregistered grenade source with no proven affiliation still
            // contributes damage, but cannot acquire a successor's team by ID.
            if (!TeamDeathmatchRelations.AreOpponents(attacker, victim)) return false;
            var side = TeamDeathmatchRelations.Faction(attacker);
            var localSide = TeamDeathmatchRelations.Faction(local);
            if (side.HasValue && localSide.HasValue) friendly = side == localSide;
        }
        else if (mode is TacticalMode tactical)
        {
            if (!tactical.IsOpposingAwardKill(victim, attacker)) return false;
            if (ReferenceEquals(attacker, local) || tactical.IsOpposingAwardKill(victim, local)) friendly = true;
            else if (tactical.IsOpposingAwardKill(local, attacker)) friendly = false;
        }
        else if (mode is ZombieMode zombieTeam && zombieTeam.IsTeamArena)
        {
            if (!zombieTeam.IsOpposingAwardKill(victim, attacker)) return false;
            // Affiliation survives the thrower's death; live-marker eligibility
            // deliberately does not, so it must never classify assist sources.
            friendly = ReferenceEquals(attacker, local) || zombieTeam.IsOpposingAwardKill(victim, local);
        }
        else
        {
            // Non-team modes have no retained affiliation registry. A dead body
            // may remain the exact current registered identity; a replacement
            // under the same profile ID must never inherit its contributions.
            if (!IsRegistered(world, attacker, requireAlive: false)
                && !Fire.ArenaFireRuntime.OwnsDamage(victim, source, damageType)) return false;
            if (mode is RoundsMode rounds)
            {
                if (!rounds.FreeForAll && !ReferenceEquals(attacker, local) && !Fire.ArenaFireRuntime.OwnsDamage(victim, source, damageType)
                    && !ReferenceEquals(victim, local)) return false;
            }
            else if (mode is GunGameMode)
            {
                // Gun Game has no frozen DeathmatchOptions. Use actual exact-body
                // group hostility, never a live F12 setting from another mode.
                if (!GunGameOpponents(local, attacker, victim)
                    && !(Fire.ArenaFireRuntime.OwnsDamage(victim, source, damageType) && ExplicitEnemy(victim, local))) return false;
            }
            else if (mode is ZombieMode)
            {
                // Infected are friendly to each other and hostile only to the
                // player (native Mind settings, verified live against
                // SPT_Data/database/bots/types/infected*.json — see Stage A's
                // handoff). There is no bot-vs-bot combat to attribute here and
                // no PitFireTeam companion in v0, so only the local player's own
                // damage (direct or self-thrown fire) is ever a contribution.
                if (!ReferenceEquals(attacker, local) && !Fire.ArenaFireRuntime.OwnsDamage(victim, source, damageType)) return false;
            }
            else return false;
        }
        return true;
    }

    /// <summary>
    /// Existing killer scores are untouched. Assists require the final kill to
    /// be hostile as well: native friendly bot fire must not cash out earlier
    /// legitimate damage, even where the legacy kill path still shows a kill.
    /// Called immediately before ReportKill, while the exact dead body remains
    /// registered; OnDead's finalizer performs unconditional later cleanup.
    /// </summary>
    internal static void PrepareKill(ArenaMatch match, Player victim, IPlayer aggressor, EDamageType damageType)
    {
        if (match == null || ReferenceEquals(victim, null)) return;
        try
        {
            if (_enabled && ArenaSession.IsActive
                && TryContext(match, victim, requireAlive: false, out var world, out var local)
                && victim.HealthController?.IsAlive == false
                && TryHostileSource(match.Mode, world, local, victim, aggressor, damageType, out _, out _)) return;
        }
        catch (Exception ex) { Warn(ex); }
        // The caller still owns its captured match when Current changes. Clear
        // that object's pending private assist data, never the new Current match.
        ForgetVictim(victim, match);
    }

    private static bool GunGameOpponents(Player local, Player attacker, Player victim)
    {
        if (ReferenceEquals(attacker, local)) return ExplicitEnemy(victim, attacker);
        if (ReferenceEquals(victim, local)) return ExplicitEnemy(attacker, victim);
        var first = attacker.AIData?.BotOwner?.BotsGroup;
        var second = victim.AIData?.BotOwner?.BotsGroup;
        return first != null && second != null && !ReferenceEquals(first, second)
            && ExplicitEnemy(attacker, victim) && ExplicitEnemy(victim, attacker);
    }

    private static bool ExplicitEnemy(Player bot, IPlayer other)
    {
        var owner = bot?.AIData?.BotOwner;
        var group = owner?.BotsGroup;
        return bot?.IsAI == true && owner != null && ReferenceEquals(owner.GetPlayer, bot)
            && group?.Enemies != null && !ArenaHostility.CountsAsAlly(group, other)
            && group.Enemies.ContainsKey(other);
    }

    internal static void FinishDeath(Player victim, ArenaMatch match)
    {
        if (!_enabled || !ArenaSession.IsActive || victim == null || match == null
            || !ReferenceEquals(ArenaMatchService.Current, match)) return;
        ForgetVictim(victim, match);
    }

    private static void ForgetVictim(Player victim, ArenaMatch match)
    {
        try { match.ForgetAssistVictim(ArenaHitHistory.BodyId(victim)); }
        catch (Exception ex) { Warn(ex); }
    }

    private sealed class HealthChangedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnHealthChanged),
                new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) });
        [PatchPrefix]
        private static void Prefix(Player __instance, float diff, DamageInfo damageInfo)
            => ObserveHealthChange(__instance, diff, damageInfo);
    }

    private sealed class DeathCleanupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(Player), nameof(Player.OnDead), new[] { typeof(EDamageType) });
        [PatchPrefix]
        private static void Prefix(out ArenaMatch __state)
            => __state = _enabled && ArenaSession.IsActive ? ArenaMatchService.Current : null;
        [PatchFinalizer]
        private static Exception Finalizer(Player __instance, Exception __exception, ArenaMatch __state)
        {
            FinishDeath(__instance, __state);
            return __exception;
        }
    }
}
