#nullable disable
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Removes two kinds of work that have no result for a confirmed generic
/// infected: delayed hearing of another known-friendly infected's steps, and
/// the search for a peaceful interaction partner. No AI cadence, navigation,
/// vision, combat, animation or BotsConnections registration is changed.
/// </summary>
internal static class ZombiePerformancePatches
{
    private static bool _hearingEnabled, _socialEnabled, _warned;
    private static WeakReference _diagnosticWorld;
    private static float _nextDiagnostic;
    private static long _stepCandidates, _friendlyStepsSkipped, _socialSearchesSkipped;

    internal static void EnablePatch()
    {
        if (!_hearingEnabled)
        {
            try { new HearingPatch().Enable(); _hearingEnabled = true; }
            catch (Exception ex) { Warn(ex); }
        }
        if (!_socialEnabled)
        {
            try { new SocialPatch().Enable(); _socialEnabled = true; }
            catch (Exception ex) { Warn(ex); }
        }
    }

    private static bool TryWorld(out GameWorld world, out LocalGame game)
    {
        world = null; game = null;
        if (!ZombieNativeEventBridge.IsActiveZombieRaid()
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated) return false;
        world = Singleton<GameWorld>.Instance;
        game = Singleton<AbstractGame>.Instance as LocalGame;
        return world != null && game != null && game.Status == GameStatus.Started
            && ArenaSession.IsExactActiveRaid(game, world);
    }

    // Do not use IsInfected(): that also includes infectedTagilla, whose boss
    // behavior is outside Arena's four-role wave contract.
    private static bool GenericInfected(WildSpawnType role)
        => role == WildSpawnType.infectedAssault || role == WildSpawnType.infectedCivil
            || role == WildSpawnType.infectedLaborant || role == WildSpawnType.infectedPmc;

    private static bool TryBody(BotOwner owner, GameWorld world, out Player body, out WildSpawnType role)
    {
        body = null; role = default;
        if (owner == null || owner.IsDead || owner.BotState != EBotState.Active) return false;
        body = owner.GetPlayer;
        if (body == null || !body.IsAI || body.IsYourPlayer || ReferenceEquals(world.MainPlayer, body)
            || body.HealthController?.IsAlive != true || !ReferenceEquals(body.AIData?.BotOwner, owner)
            || string.IsNullOrEmpty(body.ProfileId)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)) return false;
        var settings = body.Profile?.Info?.Settings;
        if (settings == null || !GenericInfected(settings.Role)) return false;
        role = settings.Role;
        return true;
    }

    private static bool Friendly(BotOwner listener, Player source, BotOwner sourceOwner,
        WildSpawnType sourceRole, LocalGame game)
    {
        var group = listener.BotsGroup;
        if (group == null || !ReferenceEquals(group.BotGame, game)
            || !GenericInfected(group.InitialBotType) || !group.Contains(listener)
            || group.Enemies == null || group.Neutrals == null || listener.Memory == null) return false;
        // Current hostility always wins over native suitability/old ally data.
        // IsEnemy also checks the numeric native ID through value.Player; a
        // ContainsKey-only test would miss an enemy entered under another wrapper.
        if (group.Enemies.ContainsKey(source) || group.Neutrals.ContainsKey(source)
            || group.IsEnemy(source)) return false;
        var target = listener.Memory.GoalEnemy?.Person;
        if (target != null && string.Equals(target.ProfileId, source.ProfileId, StringComparison.Ordinal)) return false;
        // Absence from Enemies is insufficient: unknown/neutral relationships
        // keep vanilla's delayed re-evaluation. Native suitability is a positive
        // friend rule (BotsGroup.IsPlayerEnemy's first return), as are membership
        // and an explicit ally. Do not initialize or alter role/relationship lists.
        return group.Contains(sourceOwner) || group.IsAlly(source) || group.IsSuitable(sourceRole);
    }

    internal static bool ShouldRunHearing(BotHearingSensor sensor, IPlayer source, AISoundType type)
    {
        try
        {
            if (type != AISoundType.step || !ZombieNativeEventBridge.IsActiveZombieRaid()) return true;
            var owner = sensor?._botOwner;
            // A combat listener already rejects friends inline without allocating
            // a delayed task. Leave that cheap path alone; the extra relationship
            // checks below are justified only where a delayed task would be made.
            var memory = owner == null ? null : owner.Memory;
            if (memory == null || memory.GoalEnemy != null || memory.GoalTarget == null) return true;
            var hearing = owner.Settings?.FileSettings?.Hearing;
            if (hearing == null) return true;
            float delay = memory.GoalTarget.HavePlaceTarget()
                ? hearing.HEAR_DELAY_WHEN_HAVE_SMT : hearing.HEAR_DELAY_WHEN_PEACE;
            if (!(delay > 0f) || float.IsInfinity(delay) || source?.AIData?.IsMute != false) return true;
            if (!TryWorld(out var world, out var game)
                || !TryBody(sensor._botOwner, world, out var listenerBody, out var listenerRole)
                || !ReferenceEquals(sensor._botOwner.HearingSensor, sensor)) return true;
            bool skip = false;
            // MovementContext emits the actual Player. Do not reinterpret a
            // foreign IPlayer adapter/profile alias as that body.
            if (source is Player sourceBody && sourceBody != null
                && TryBody(sourceBody.AIData?.BotOwner, world, out var currentSource, out var sourceRole)
                && ReferenceEquals(sourceBody, currentSource))
            {
                var sourceOwner = sourceBody.AIData.BotOwner;
                skip = Friendly(sensor._botOwner, sourceBody, sourceOwner, sourceRole, game)
                    && Friendly(sourceOwner, listenerBody, sensor._botOwner, listenerRole, game);
            }
            Diagnose(world, hearing: true, skipped: skip);
            return !skip;
        }
        catch (Exception ex) { Warn(ex); return true; }
    }

    internal static bool ShouldRunSocial(BotFriendChecker checker)
    {
        try
        {
            // Match the native strict '<' and cheap early return, including NaN.
            // A skipped search still advances its native one-second retry, so
            // this patch does not replace an O(1) idle return with body validation
            // on every bot frame. No existing social action is cancelled.
            if (checker == null || !(checker._nextTimeCheck < Time.time)
                || !TryWorld(out var world, out _)
                || !TryBody(checker._owner, world, out _, out _)
                || !ReferenceEquals(checker._owner.FriendChecker, checker)) return true;
            Diagnose(world, hearing: false, skipped: true);
            checker._nextTimeCheck = Time.time + 1f;
            return false;
        }
        catch (Exception ex) { Warn(ex); return true; }
    }

    private static void Diagnose(GameWorld world, bool hearing, bool skipped)
    {
        // Weak identity cannot retain a departed world. Replacing the exact world
        // starts fresh counters even after a missed end callback or aborted load.
        if (!ReferenceEquals(_diagnosticWorld?.Target, world))
        {
            _diagnosticWorld = new WeakReference(world);
            _nextDiagnostic = Time.realtimeSinceStartup + 60f;
            _stepCandidates = _friendlyStepsSkipped = _socialSearchesSkipped = 0;
            Plugin.ModLogger.LogDebug("Arena zombie performance: friendly-step hearing filter and social-search skip active; 60 s aggregate counters, native combat retained.");
        }
        if (hearing)
        {
            _stepCandidates++;
            if (skipped) _friendlyStepsSkipped++;
        }
        else if (skipped) _socialSearchesSkipped++;
        if (Time.realtimeSinceStartup < _nextDiagnostic) return;
        _nextDiagnostic = Time.realtimeSinceStartup + 60f;
        Plugin.ModLogger.LogDebug($"Arena zombie performance (last interval): delayedStepCandidates={_stepCandidates}, friendlyStepsSkipped={_friendlyStepsSkipped}, socialSearchesSkipped={_socialSearchesSkipped}; counts are avoided work, not frame-time savings.");
        _stepCandidates = _friendlyStepsSkipped = _socialSearchesSkipped = 0;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena zombie performance: optimization unavailable for this call; native behavior retained. " + ex.GetBaseException().Message);
    }

    private sealed class HearingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(BotHearingSensor), nameof(BotHearingSensor.OnSoundPlayed),
                new[] { typeof(IPlayer), typeof(Vector3), typeof(float), typeof(AISoundType) });
        [PatchPrefix]
        private static bool Prefix(BotHearingSensor __instance, IPlayer player, AISoundType type)
            => ShouldRunHearing(__instance, player, type);
    }

    private sealed class SocialPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(BotFriendChecker), nameof(BotFriendChecker.ManualUpdate), Type.EmptyTypes);
        [PatchPrefix]
        private static bool Prefix(BotFriendChecker __instance) => ShouldRunSocial(__instance);
    }
}
