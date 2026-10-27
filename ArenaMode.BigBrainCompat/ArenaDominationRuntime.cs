using ArenaMode.Areas;
using ArenaMode.Bots;
using ArenaMode.Movement;
using ArenaMode.Shared;
using EFT;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.BigBrainCompat;

internal static class ArenaDominationRuntime
{
    internal static bool CanAct(BotOwner bot, DominationObjectiveLease lease, bool owningDoor = false)
    {
        try
        {
            var body = bot?.GetPlayer;
            if (!Library.RuntimeEnabled || lease?.Current != true || !ReferenceEquals(lease.Bot, bot)
                || Time.timeScale <= 0 || bot.Memory == null || bot.EnemiesController?.EnemyInfos == null
                || !ArenaBotMovementSain.AllowsBoost(bot, body) || bot.Memory.IsUnderFire
                || bot.Medecine?.FirstAid?.Using == true || bot.Medecine?.SurgicalKit?.Using == true
                // SAIN starts self-actions only while its layer is active. Yield
                // their native read-only demand before Using/Reloading becomes true.
                || bot.Medecine?.FirstAid?.Have2Do == true || bot.Medecine?.SurgicalKit?.HaveWork == true
                || bot.Medecine?.Stimulators?.Using == true || bot.WeaponManager?.Reload?.Reloading == true
                || bot.WeaponManager?.Reload?.BulletCount == 0
                || bot.WeaponManager?.Grenades?.ThrowindNow == true || bot.FlashGrenade?.IsFlashed == true
                || bot.BewareGrenade?.ShallRunAway() == true || bot.BewareBTR?.ShallRunAway() == true
                || bot.ArtilleryDangerPlace?.ShallRunAway() == true
                || (!owningDoor && (bot.DoorOpener?.Interacting == true || bot.Mover.CurrentState == EBotMoverState.NearDoor))) return false;
            foreach (var enemy in bot.EnemiesController.EnemyInfos.Values)
            {
                if (enemy?.Person?.HealthController?.IsAlive != true) continue;
                if ((enemy.IsVisible && enemy.CanShoot)
                    || (enemy.PersonalLastShootTime > 0 && Time.time - enemy.PersonalLastShootTime < 2f)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    internal static bool TryApproach(DominationObjectiveLease lease, Vector3 candidate, out Vector3 destination)
    {
        destination = default;
        if (lease?.Current != true
            || !NavMesh.SamplePosition(candidate, out var hit, .75f, NavMesh.AllAreas)
            || Math.Abs(hit.position.y - candidate.y) > .5f
            || (hit.position - candidate).sqrMagnitude > .75f * .75f
            || !lease.Inside(hit.position)) return false;
        destination = hit.position;
        return true;
    }

    internal static bool RouteAllowed(Vector3 from, Vector3 destination)
    {
        if (!NavMesh.SamplePosition(from, out var start, .75f, NavMesh.AllAreas)
            || Math.Abs(start.position.y - from.y) > .5f
            || !NavMesh.SamplePosition(destination, out var end, .35f, NavMesh.AllAreas)
            || Math.Abs(end.position.y - destination.y) > .3f) return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
            || path.status != NavMeshPathStatus.PathComplete || path.corners.Length == 0) return false;
        var corners = path.corners;
        if ((corners[corners.Length - 1] - destination).sqrMagnitude > .25f) return false;
        float length = 0; var previous = from;
        foreach (var corner in corners) { length += (corner - previous).magnitude; previous = corner; }
        return !float.IsNaN(length) && !float.IsInfinity(length) && length <= 160f && CornersAllowed(from, corners);
    }

    internal static bool CornersAllowed(Vector3 from, System.Collections.Generic.IReadOnlyList<Vector3> corners)
    {
        if (corners == null || corners.Count == 0) return false;
        var area = ArenaAreaRuntime.CurrentArea;
        return area == null || area.Bounds.ContainsPath(new ArenaAreaPoint(from.x, from.y, from.z),
            corners, true, p => new ArenaAreaPoint(p.x, p.y, p.z), .1f);
    }
}
