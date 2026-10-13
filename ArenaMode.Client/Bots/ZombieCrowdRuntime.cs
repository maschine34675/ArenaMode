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
using UnityEngine.Rendering;

namespace ArenaMode.Bots;

/// <summary>
/// Keeps a zombie raid's infected in the player's neighbourhood and drops
/// the HighPoly vision cast for melee infected. One recall per frame, and
/// only onto a point <see cref="ArenaAreaRuntime.AllowTeleport"/> already
/// accepts, so a bounded Factory area cannot be left for a disconnected
/// NavMesh island. Shooters, followers and anything outside the four generic
/// infected roles keep native vision and are never moved by this.
/// </summary>
internal static class ZombieCrowdRuntime
{
    private static bool _sightEnabled, _warned;
    private static readonly Dictionary<int, float> RecallAt = new Dictionary<int, float>();
    private static readonly HashSet<int> Cheapened = new HashSet<int>();
    private static readonly NavMeshPath ScratchPath = new NavMeshPath();
    private static int _recalled, _sightSkips, _cheapened;
    private static float _nextReport = float.PositiveInfinity;

    internal static void EnablePatch()
    {
        if (_sightEnabled) return;
        try
        {
            new SightPatch().Enable();
            _sightEnabled = true;
        }
        catch (Exception ex)
        {
            Warn(ex);
        }
    }

    internal static void Reset()
    {
        RecallAt.Clear();
        Cheapened.Clear();
        _recalled = _sightSkips = _cheapened = 0;
        _nextReport = float.PositiveInfinity;
    }

    internal static void Tick()
    {
        try
        {
            if (!Enabled() || !ZombieNativeEventBridge.IsActiveZombieRaid()) return;
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
            bool cheapenedOne = false;
            foreach (var bot in owners)
            {
                if (!TryEligible(bot, world, out var body)) continue;
                int id = body.GetInstanceID();
                if (!cheapenedOne && Cheapened.Add(id))
                {
                    Cheapen(body);
                    _cheapened++;
                    cheapenedOne = true;
                }
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
                TryRecall(farthest, player, owners, now);
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

    private static void TryRecall(BotOwner bot, Player player, IEnumerable<BotOwner> owners, float now)
    {
        var body = bot.GetPlayer;
        if (body == null) return;
        Vector3 look = player.MovementContext.LookDirection;
        int id = body.GetInstanceID();
        RecallAt[id] = now;
        float yaw = ZombieCrowdPolicy.SlotYaw(id);
        float separation = ZombieCrowdPolicy.SlotSeparation * ZombieCrowdPolicy.SlotSeparation;
        Vector3? crowded = null;
        foreach (float radius in ZombieCrowdPolicy.RingRadii)
        {
            ZombieCrowdPolicy.RingOffset(look.x, look.z, yaw, radius, out float x, out float z);
            var candidate = new Vector3(player.Position.x + x, player.Position.y, player.Position.z + z);
            if (!AcceptPoint(bot, body, player, candidate, out var placed)) continue;
            if (crowded == null) crowded = placed;
            if (Occupied(owners, bot, placed, separation)) continue;
            Place(bot, player, placed);
            return;
        }
        // A taken slot must not leave this body across the map.
        if (crowded.HasValue) Place(bot, player, crowded.Value);
    }

    private static void Place(BotOwner bot, Player player, Vector3 placed)
    {
        bot.Mover.Stop();
        bot.Mover.Teleport(placed);
        bot.BotsGroup?.AddPointToSearch(player.Position, 1000f, bot);
        _recalled++;
    }

    /// <summary>
    /// A bounded area keeps the existing teleport gate. The full Factory map has
    /// no area runtime, so a recall there only needs a real NavMesh sample and
    /// a complete path to the player. A pocket the bots cannot enter still fails.
    /// </summary>
    private static bool AcceptPoint(BotOwner bot, Player body, Player player, Vector3 candidate, out Vector3 placed)
    {
        placed = candidate;
        if (ArenaAreaRuntime.TryGetForBot(bot, out _))
            return ArenaAreaRuntime.AllowTeleport(body, candidate);
        if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas)) return false;
        if (Mathf.Abs(hit.position.y - candidate.y) > 2f) return false;
        if (!NavMesh.CalculatePath(hit.position, player.Position, NavMesh.AllAreas, ScratchPath)
            || ScratchPath.status != NavMeshPathStatus.PathComplete
            || ScratchPath.corners == null || ScratchPath.corners.Length < 2) return false;
        placed = hit.position;
        return true;
    }

    private static bool Occupied(IEnumerable<BotOwner> owners, BotOwner self, Vector3 point, float separationSquared)
    {
        foreach (var other in owners)
        {
            if (other == null || ReferenceEquals(other, self) || other.IsDead) continue;
            var offset = other.Position - point;
            offset.y = 0f;
            if (offset.sqrMagnitude < separationSquared) return true;
        }
        return false;
    }

    private static void Cheapen(Player body)
    {
        var renderers = body.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (renderer is SkinnedMeshRenderer skin)
            {
                skin.quality = SkinQuality.Bone2;
                skin.updateWhenOffscreen = false;
            }
        }
    }

    private static bool TryEligible(BotOwner bot, GameWorld world, out Player body)
    {
        body = null;
        if (bot == null || bot.IsDead || bot.BotState != EBotState.Active || bot.Mover == null) return false;
        body = bot.GetPlayer;
        if (body == null || !body.IsAI || ReferenceEquals(body, world.MainPlayer)
            || body.HealthController?.IsAlive != true
            || !ReferenceEquals(body.AIData?.BotOwner, bot)) return false;
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
            Plugin.ModLogger.LogInfo(
                "Arena zombie crowd: recall beyond "
                + ZombieCrowdPolicy.RecallDistance.ToString("0")
                + " m onto a " + ZombieCrowdPolicy.RingDistance.ToString("0")
                + " m ring, melee sight within "
                + ZombieCrowdPolicy.SenseDistance.ToString("0")
                + " m without a linecast, Bone2 skins and no caster shadows.");
            return;
        }
        if (now < _nextReport) return;
        _nextReport = now + 30f;
        Plugin.ModLogger.LogInfo(
            $"Arena zombie crowd (last interval): recalled={_recalled}, sightSkips={_sightSkips}, cheapenedBodies={_cheapened}.");
        _recalled = _sightSkips = _cheapened = 0;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning(
            "Arena zombie crowd: optimization skipped for this call; native behavior retained. "
            + ex.GetBaseException().Message);
    }

    private sealed class SightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(typeof(EnemyPartVision), nameof(EnemyPartVision.CheckLineOfSight));

        [PatchPrefix]
        private static bool Prefix(EnemyPartVision __instance, BotOwner owner, EnemyPart part)
            => ShouldRunSight(__instance, owner, part);
    }
}
