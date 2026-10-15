using ArenaMode.Areas;
using ArenaMode.Movement;
using ArenaMode.Pickups;
using ArenaMode.Shared;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.BigBrainCompat;

/// <summary>Short, physically owned pickup trip; never a PFT enemy pursuit lease.</summary>
internal sealed class ArenaPickupObjective
{
    private static readonly List<ArenaPickupObjective> Claims = new();
    private static int _generation = -1;
    internal readonly BotOwner Bot;
    internal readonly ArenaPickupTarget Target;
    private readonly Player _body;
    private readonly BotMover _mover;
    private readonly object _group;
    private readonly string _profileId;
    private readonly float _expires;
    private float _lastProgress;
    private Vector3 _progressPosition;
    private float _remaining = float.MaxValue;
    private bool _handlingDoor;
    internal bool Released { get; private set; }
    internal bool Started { get; set; }

    private ArenaPickupObjective(BotOwner bot, ArenaPickupTarget target)
    {
        Bot = bot; _body = bot.GetPlayer; _mover = bot.Mover; _group = bot.BotsGroup; Target = target;
        _profileId = bot.ProfileId;
        _expires = Time.time + 20f; _lastProgress = Time.time; _progressPosition = bot.Position;
    }

    internal static bool CanSeek(BotOwner bot, bool owningDoor = false)
    {
        try
        {
            var player = bot?.GetPlayer;
            if (!Plugin.RuntimeEnabled || player?.IsAI != true || bot.IsDead || bot.BotState != EBotState.Active
                || !ReferenceEquals(player.AIData?.BotOwner, bot) || !ArenaPickupRuntime.IsLiveActor(player)
                || ArenaPickupSettings.BotsCollectPowerups?.Value != true || Time.timeScale <= 0
                || bot.Mover == null || bot.Memory == null || bot.BotsGroup == null || bot.EnemiesController?.EnemyInfos == null
                || bot.Memory.IsUnderFire || !ArenaBotMovementSain.AllowsBoost(bot, player)
                || bot.Medecine?.FirstAid?.Using == true || bot.Medecine?.SurgicalKit?.Using == true || bot.Medecine?.Stimulators?.Using == true
                || bot.WeaponManager?.Reload?.Reloading == true || bot.WeaponManager?.Grenades?.ThrowindNow == true
                || (!owningDoor && (bot.DoorOpener?.Interacting == true || bot.Mover.CurrentState == EBotMoverState.NearDoor))
                || bot.BewareGrenade?.ShallRunAway() == true || bot.BewareBTR?.ShallRunAway() == true
                || bot.ArtilleryDangerPlace?.ShallRunAway() == true || bot.FlashGrenade?.IsFlashed == true) return false;
            foreach (var enemy in bot.EnemiesController.EnemyInfos.Values)
            {
                if (enemy?.Person?.HealthController?.IsAlive != true) continue;
                if (enemy.IsVisible || (enemy.PersonalLastSeenTime > 0 && Time.time - enemy.PersonalLastSeenTime < 3f)
                    || (enemy.PersonalLastShootTime > 0 && Time.time - enemy.PersonalLastShootTime < 3f)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    internal bool Current(bool checkCombat = true)
        => !Released && Time.time < _expires && Time.time - _lastProgress < 4f
        && ReferenceEquals(Bot?.GetPlayer, _body) && ReferenceEquals(Bot.Mover, _mover) && ReferenceEquals(Bot.BotsGroup, _group)
        && ArenaPickupRuntime.IsTargetCurrent(_body, Target) && (!checkCombat || CanSeek(Bot, Started));

    internal void Progress(bool handlingDoor)
    {
        if (handlingDoor || _handlingDoor)
        {
            _handlingDoor = handlingDoor; _remaining = float.MaxValue;
            _progressPosition = Bot.Position; _lastProgress = Time.time;
            return; // Door interaction distances are not pickup-route progress.
        }
        float remaining = Bot.Mover.DistDestination;
        if (Bot.Mover.HasPathAndNoComplete && ArenaPickupLayouts.Finite(remaining) && _remaining - remaining >= .5f
            && (Bot.Position - _progressPosition).sqrMagnitude >= .04f)
        { _progressPosition = Bot.Position; _remaining = remaining; _lastProgress = Time.time; }
    }

    internal void Release(string reason)
    {
        if (Released) return;
        Released = true;
        Claims.Remove(this);
        if (Diagnostics.ArenaLog.DeveloperEnabled)
            Diagnostics.ArenaLog.Developer($"Arena pickup objective released: {Target.Id}; bot {_profileId}; {reason}.");
    }

    internal static bool TryAcquire(BotOwner bot, List<ArenaPickupTarget> candidates, out ArenaPickupObjective lease)
    {
        lease = null;
        Prune();
        if (!CanSeek(bot)) return false;
        ArenaPickupRuntime.GetBotTargets(bot.GetPlayer, candidates);
        candidates.Sort((a, b) => Score(b, bot.Position).CompareTo(Score(a, bot.Position)));
        int attempts = 0;
        foreach (var candidate in candidates)
        {
            if ((candidate.Position - bot.Position).sqrMagnitude > 1600f) continue;
            int others = 0; bool sameGroup = false;
            foreach (var claim in Claims)
            {
                if (!ReferenceEquals(claim.Target.Identity, candidate.Identity)) continue;
                others++; sameGroup |= ReferenceEquals(claim._group, bot.BotsGroup);
            }
            if (others >= 2 || sameGroup) continue;
            if (++attempts > 2) break;
            if (!RouteAllowed(bot.Position, candidate.Position)) continue;
            lease = new ArenaPickupObjective(bot, candidate); Claims.Add(lease);
            if (Diagnostics.ArenaLog.DeveloperEnabled)
                Diagnostics.ArenaLog.Developer($"Arena pickup objective selected: {candidate.Id}; bot {bot.ProfileId}; max 20s.");
            return true;
        }
        return false;
    }

    internal static void ResetClaims()
    {
        foreach (var old in Claims) old.Released = true;
        Claims.Clear();
    }

    internal static void Prune()
    {
        if (_generation != ArenaPickupRuntime.Generation)
        {
            ResetClaims(); _generation = ArenaPickupRuntime.Generation;
        }
        for (int i = Claims.Count - 1; i >= 0; i--)
            if (!Claims[i].Current(false)) { Claims[i].Released = true; Claims.RemoveAt(i); }
    }

    private static float Score(ArenaPickupTarget target, Vector3 position)
        => target.Utility / (1f + (target.Position - position).magnitude * .06f);

    internal static bool RouteAllowed(Vector3 from, Vector3 destination)
    {
        if (!NavMesh.SamplePosition(from, out var start, .75f, NavMesh.AllAreas) || Math.Abs(start.position.y - from.y) > .5f
            || !NavMesh.SamplePosition(destination, out var end, .35f, NavMesh.AllAreas) || Math.Abs(end.position.y - destination.y) > .3f) return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
            || path.status != NavMeshPathStatus.PathComplete) return false;
        var corners = path.corners;
        if (corners.Length == 0 || (corners[corners.Length - 1] - destination).sqrMagnitude > .25f) return false;
        float length = 0;
        var previous = from;
        foreach (var corner in corners) { length += (corner - previous).magnitude; previous = corner; }
        return length <= 60f && CornersAllowed(from, corners);
    }

    internal static bool CornersAllowed(Vector3 from, IReadOnlyList<Vector3> corners)
    {
        if (corners == null || corners.Count == 0) return false;
        var area = ArenaAreaRuntime.CurrentArea;
        return area == null || area.Bounds.ContainsPath(new ArenaAreaPoint(from.x, from.y, from.z),
            corners, true, p => new ArenaAreaPoint(p.x, p.y, p.z), .1f);
    }
}
