#nullable disable
using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps a zombie raid's infected in the player's neighbourhood and drops
/// the HighPoly vision cast for melee infected. One recall per frame, and
/// only onto the validated, clear NavMesh point. Shooters keep native vision
/// but may be recalled too; followers and non-generic roles are never moved.
/// Rendering and pooled models remain entirely owned by the native game.
/// </summary>
internal static class ZombieCrowdRuntime
{
    private static bool _sightEnabled, _avoidEnabled, _warned;
    private static readonly Dictionary<int, float> RecallAt = new Dictionary<int, float>();
    private static readonly NavMeshPath ScratchPath = new NavMeshPath();
    private static readonly Collider[] Clearance = new Collider[16];
    private static int _recalled, _sightSkips;
    private static bool _running;
    private static float _nextReport = float.PositiveInfinity;

    internal static void EnablePatch()
    {
        if (!_sightEnabled)
        {
            try { new ArenaZombieCrowdRuntimeSightPatch().Enable(); _sightEnabled = true; }
            catch (Exception ex) { Warn(ex); }
        }
        if (!_avoidEnabled)
        {
            try { new ArenaZombieCrowdRuntimeAvoidancePatch().Enable(); _avoidEnabled = true; }
            catch (Exception ex) { Warn(ex); }
        }
    }

    internal static void Reset()
    {
        RecallAt.Clear();
        Array.Clear(Clearance, 0, Clearance.Length);
        _recalled = _sightSkips = 0;
        _running = false;
        _warned = false;
        _nextReport = float.PositiveInfinity;
    }

    internal static void Tick()
    {
        try
        {
            if (!Enabled() || !ZombieNativeEventBridge.IsActiveZombieRaid())
            {
                if (_running)
                {
                    Plugin.ModLogger.LogDebug("Arena zombie crowd: disabled; native movement, sight and avoidance retained; no crowd teleports.");
                    Reset();
                }
                return;
            }
            if (ArenaRespawnDriver.IsRespawnInProgress) return;
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var player = world?.MainPlayer;
            if (player?.HealthController?.IsAlive != true || player.MovementContext == null) return;

            var owners = Singleton<IBotGame>.Instantiated
                ? Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners
                : null;
            if (owners == null) return;

            float now = Time.time;
            BotOwner farthest = null;
            float farthestDistance = ZombieCrowdPolicy.RecallDistance * ZombieCrowdPolicy.RecallDistance;
            _running = true;
            foreach (var bot in owners)
            {
                if (!TryEligible(bot, world, out var body)) continue;
                int id = body.GetInstanceID();
                float distance = (bot.Position - player.Position).sqrMagnitude;
                float since = RecallAt.TryGetValue(id, out float at) ? now - at : float.PositiveInfinity;
                if (!ZombieCrowdPolicy.ShouldRecall(true, distance, since)) continue;
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = bot;
                }
            }

            if (farthest != null)
                TryRecall(farthest, player, world, now);
            Report(now);
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    internal static bool ShouldRunSight(EnemyPartVision vision, BotOwner owner, EnemyPart part)
    {
        try
        {
            if (!Enabled() || !ZombieNativeEventBridge.IsActiveZombieRaid()) return true;
            if (vision == null || owner == null || part == null || owner.IsDead || owner.BotState != EBotState.Active)
                return true;
            var settings = owner.Profile?.Info?.Settings;
            if (settings == null) return true;
            bool generic = settings.Role == WildSpawnType.infectedAssault
                || settings.Role == WildSpawnType.infectedCivil
                || settings.Role == WildSpawnType.infectedLaborant
                || settings.Role == WildSpawnType.infectedPmc;
            bool melee = settings.BotDifficulty == BotDifficulty.easy
                || settings.BotDifficulty == BotDifficulty.normal;
            bool follower = owner.IsFollower() || owner.BotFollower?.HaveBoss == true;
            var offset = part.Position - owner.Position;
            float distance = offset.magnitude;
            if (!ZombieCrowdPolicy.ShouldSkipSight(
                    ZombieCrowdPolicy.IsEligible(true, true, generic, true, true, follower), melee, distance))
                return true;
            vision.HasLineOfSight = true;
            vision.BotToTargetHit = default;
            vision.BotToTargetObstacleType = ELookObstacleType.None;
            _sightSkips++;
            return false;
        }
        catch (Exception ex)
        {
            Warn(ex);
            return true;
        }
    }

    private static void TryRecall(BotOwner bot, Player player, GameWorld world, float now)
    {
        var body = bot.GetPlayer;
        if (body == null) return;
        Vector3 look = player.MovementContext.LookDirection;
        int id = body.GetInstanceID();
        RecallAt[id] = now;
        float yaw = ZombieCrowdPolicy.SlotYaw(id);
        float separation = ZombieCrowdPolicy.SlotSeparation * ZombieCrowdPolicy.SlotSeparation;
        foreach (float radius in ZombieCrowdPolicy.RingRadii)
        {
            ZombieCrowdPolicy.RingOffset(look.x, look.z, yaw, radius, out float x, out float z);
            var candidate = new Vector3(player.Position.x + x, player.Position.y, player.Position.z + z);
            if (!AcceptPoint(bot, player, candidate, out var placed)) continue;
            if (Occupied(world, body, placed, separation)) continue;
            Place(bot, player, placed);
            return;
        }
        // An occupied slot is not a fallback: wait for a later safe candidate.
    }

    private static void Place(BotOwner bot, Player player, Vector3 placed)
    {
        bot.Mover.Stop();
        bot.Mover.Teleport(placed);
        bot.BotsGroup?.AddPointToSearch(player.Position, 1000f, bot);
        _recalled++;
    }

    /// <summary>
    /// Both branches return the actual sample, not the unsnapped candidate.
    /// A configured area must never fall back to full-map navigation.
    /// </summary>
    private static bool AcceptPoint(BotOwner bot, Player player, Vector3 candidate, out Vector3 placed)
    {
        placed = default;
        if (ArenaAreaRuntime.CurrentArea != null)
            return ArenaAreaRuntime.TryResolveCrowdRecall(bot, candidate, out placed)
                && HasRoom(placed);
        if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas)) return false;
        if (Mathf.Abs(hit.position.y - candidate.y) > 2f) return false;
        if (!NavMesh.CalculatePath(hit.position, player.Position, NavMesh.AllAreas, ScratchPath)
            || ScratchPath.status != NavMeshPathStatus.PathComplete
            || ScratchPath.corners == null || ScratchPath.corners.Length < 2) return false;
        placed = hit.position;
        return HasRoom(placed);
    }

    private static bool HasRoom(Vector3 point)
    {
        int mask = LayersMaskController.PlayerStaticCollisionsMask;
        if (!Physics.Raycast(point + Vector3.up * .25f, Vector3.down, out var floor,
                .5f, mask, QueryTriggerInteraction.Ignore)
            || Mathf.Abs(floor.point.y - point.y) > .15f || floor.normal.y < .7f)
            return false;
        int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * .45f,
            point + Vector3.up * 1.45f, .35f, Clearance, mask, QueryTriggerInteraction.Ignore);
        // Saturation cannot prove clearance. Character occupancy is checked separately.
        bool blocked = count >= Clearance.Length;
        for (int i = 0; i < Math.Min(count, Clearance.Length); i++)
        {
            var collider = Clearance[i];
            Clearance[i] = null;
            if (collider != null && collider.GetComponentInParent<Player>() == null) blocked = true;
        }
        return !blocked;
    }

    private static bool Occupied(GameWorld world, Player self, Vector3 point, float separationSquared)
    {
        if (world?.AllAlivePlayersList == null) return true;
        foreach (var actor in world.AllAlivePlayersList)
        {
            if (actor is not Player other || other == null || ReferenceEquals(other, self)
                || other.HealthController?.IsAlive != true) continue;
            var offset = other.Position - point;
            if (Mathf.Abs(offset.y) >= 2f) continue;
            offset.y = 0f;
            if (offset.sqrMagnitude < separationSquared) return true;
        }
        return false;
    }

    internal static bool ShouldRunAvoidance(BotLocalAvoidance avoidance)
    {
        try
        {
            if (!Enabled() || !ZombieNativeEventBridge.IsActiveZombieRaid()) return true;
            var owner = avoidance?._owner;
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active) return true;
            var settings = owner.Profile?.Info?.Settings;
            if (settings == null) return true;
            bool generic = settings.Role == WildSpawnType.infectedAssault
                || settings.Role == WildSpawnType.infectedCivil
                || settings.Role == WildSpawnType.infectedLaborant
                || settings.Role == WildSpawnType.infectedPmc;
            bool follower = owner.IsFollower() || owner.BotFollower?.HaveBoss == true;
            if (!ZombieCrowdPolicy.ShouldSkipAvoidance(
                    ZombieCrowdPolicy.IsEligible(true, true, generic, true, true, follower))) return true;
            // Other bots (including followers) can still call AddPower on us.
            // Skipping ManualUpdate must not preserve that offset forever.
            avoidance.DropOffset();
            return false;
        }
        catch (Exception ex)
        {
            Warn(ex);
            return true;
        }
    }

    private static bool TryEligible(BotOwner bot, GameWorld world, out Player body)
    {
        body = null;
        if (bot == null || bot.IsDead || bot.BotState != EBotState.Active || bot.Mover == null) return false;
        body = bot.GetPlayer;
        if (body == null || !body.IsAI || ReferenceEquals(body, world.MainPlayer)
            || body.HealthController?.IsAlive != true
            || !ReferenceEquals(body.AIData?.BotOwner, bot)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)) return false;
        var settings = body.Profile?.Info?.Settings;
        if (settings == null) return false;
        bool generic = settings.Role == WildSpawnType.infectedAssault
            || settings.Role == WildSpawnType.infectedCivil
            || settings.Role == WildSpawnType.infectedLaborant
            || settings.Role == WildSpawnType.infectedPmc;
        bool follower = bot.IsFollower() || bot.BotFollower?.HaveBoss == true;
        return ZombieCrowdPolicy.IsEligible(true, true, generic, true, true, follower);
    }

    private static bool Enabled()
        => Settings.ZombieCrowdEnabled != null && Settings.ZombieCrowdEnabled.Value;

    private static void Report(float now)
    {
        if (float.IsPositiveInfinity(_nextReport))
        {
            _nextReport = now + 30f;
            Plugin.ModLogger.LogDebug(
                "Arena zombie crowd: recall beyond "
                + ZombieCrowdPolicy.RecallDistance.ToString("0")
                + " m onto clear 12/16/22 m rings around the player, melee sight within "
                + ZombieCrowdPolicy.SenseDistance.ToString("0")
                + " m without a linecast. Infected skip local avoidance and clear its offsets. Native rendering retained.");
            return;
        }
        if (now < _nextReport) return;
        _nextReport = now + 30f;
        Plugin.ModLogger.LogDebug(
            $"Arena zombie crowd (last interval): recalled={_recalled}, sightSkips={_sightSkips}.");
        _recalled = _sightSkips = 0;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning(
            "Arena zombie crowd: optimization skipped for this call; native behavior retained. "
            + ex.GetBaseException().Message);
    }

    private sealed class ArenaZombieCrowdRuntimeSightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(EnemyPartVision), nameof(EnemyPartVision.CheckLineOfSight));

        [PatchPrefix]
        private static bool Prefix(EnemyPartVision __instance, BotOwner owner, EnemyPart part)
            => ShouldRunSight(__instance, owner, part);
    }

    private sealed class ArenaZombieCrowdRuntimeAvoidancePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(BotLocalAvoidance), nameof(BotLocalAvoidance.ManualUpdate));

        [PatchPrefix]
        private static bool Prefix(BotLocalAvoidance __instance)
            => ShouldRunAvoidance(__instance);
    }
}
