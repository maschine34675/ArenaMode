#nullable disable
using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Bots;

/// <summary>
/// Individual recovery is deliberately slower than normal pursuit. Only owned
/// arrivals can be replaced, after stationary, occluded, non-combat observations
/// and a fresh pursuit request. Native death is read back before returning a
/// wave slot. The coarse wave watchdog remains the bounded final fallback.
/// </summary>
internal static class ZombieRecoveryRuntime
{
    internal const float StationarySeconds = 20f;
    internal const float RetrySeconds = 3f;
    internal const float RenewedIntentSeconds = 15f;
    internal const float ProbeLifetimeSeconds = 30f;
    private const float MovementSquared = .75f * .75f;
    private const float SafeDistanceSquared = 8f * 8f;
    private const float CombatGraceSeconds = 12f;
    private const int SamplesPerTick = 4;

    private sealed class Entry
    {
        internal BotOwner Owner;
        internal Player Body;
        internal GameWorld World;
        internal ZombieMode Mode;
        internal Vector3 Anchor;
        internal float Since;
        internal void Restart(float now) { Anchor = Body.Position; Since = now; }
    }

    private sealed class Probe
    {
        internal Entry Entry;
        internal readonly Player[] Targets = new Player[5];
        internal readonly Vector3[] Positions = new Vector3[5];
        internal int Count, Index, UnreachablePasses, ReachablePasses;
        internal bool AnyReachable;
        internal float StartedAt, NextPassAt, IntentSince = float.NaN;
    }

    private static readonly List<Entry> Entries = new List<Entry>(96);
    private static readonly NavMeshPath Scratch = new NavMeshPath();
    private static Probe _probe;
    private static int _cursor, _wave, _recovered;
    private static bool _warned;
    private static float _nextTick;

    internal static void Reset()
    {
        Entries.Clear();
        _probe = null;
        _cursor = _wave = _recovered = 0;
        _nextTick = 0f;
        _warned = false;
    }

    /// <summary>Called only by the exact pending request's confirmed arrival.</summary>
    internal static void Track(BotOwner owner)
    {
        var mode = ArenaMatchService.Current?.Mode as ZombieMode;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var body = owner == null ? null : owner.GetPlayer;
        if (mode == null || !mode.IsSpawnAdmissionOpen || world == null || body == null) return;
        foreach (var entry in Entries)
            if (ReferenceEquals(entry.Owner, owner) || ReferenceEquals(entry.Body, body)) return;
        Entries.Add(new Entry { Owner = owner, Body = body, World = world, Mode = mode,
            Anchor = body.Position, Since = Time.time });
    }

    /// <summary>Readiness/rebind pauses must not age a stationary proof.</summary>
    internal static void Suspend()
    {
        _probe = null;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (entry.Body == null || entry.Body.HealthController?.IsAlive != true
                || entry.Owner == null || !ReferenceEquals(entry.Owner.GetPlayer, entry.Body)
                || !ReferenceEquals(entry.Mode, ArenaMatchService.Current?.Mode)
                || !ReferenceEquals(entry.World, world)) Entries.RemoveAt(i);
            else entry.Restart(Time.time);
        }
    }

    /// <summary>Read-only identity proof for scoped diagnostics; never admits a new body.</summary>
    internal static bool OwnsCurrentBody(BotOwner owner)
    {
        var mode = ArenaMatchService.Current?.Mode as ZombieMode;
        if (!Admitted(mode)) return false;
        foreach (var entry in Entries)
            if (ReferenceEquals(entry.Owner, owner)) return Current(entry, mode);
        return false;
    }

    /// <returns>Exactly one only after one owned body is proven dead.</returns>
    internal static int Tick(ZombieMode mode, int wave, int waveSize, float timeout)
    {
        try
        {
            if (!(timeout > 0f) || !Admitted(mode)) { Suspend(); return 0; }
            float now = Time.time;
            if (now < _nextTick) return 0;
            _nextTick = now + .25f;
            if (_wave != wave) { _wave = wave; _recovered = 0; Suspend(); }
            // A pathological spawn region cannot create an endless recycle loop.
            bool budgetAvailable = _recovered < Math.Min(8, Math.Max(1, waveSize / 5));
            if (!budgetAvailable) _probe = null;
            for (int scanned = 0; scanned < SamplesPerTick && Entries.Count > 0; scanned++)
            {
                if (_cursor >= Entries.Count) _cursor = 0;
                var entry = Entries[_cursor];
                // EFT can confirm an alive PreActive arrival before activation.
                // It is not eligible yet, but must not lose its ownership record.
                if (entry.Owner != null && entry.Owner.BotState == EBotState.PreActive
                    && entry.Body?.HealthController?.IsAlive == true)
                { entry.Restart(now); _cursor++; continue; }
                if (!Current(entry, mode)) { Entries.RemoveAt(_cursor); continue; }
                _cursor++;
                if (!Stationary(entry, now)) continue;
                if (budgetAvailable && _probe == null && now - entry.Since >= StationarySeconds)
                    _probe = BeginProbe(entry, now);
            }
            if (_probe == null) return 0;
            var probe = _probe;
            var candidate = probe.Entry;
            if (now - probe.StartedAt >= ProbeLifetimeSeconds
                || !Current(candidate, mode) || !Stationary(candidate, now)
                || !SafeTargets(probe, capture: false)) { CancelProbe(now); return 0; }

            // A valid mesh route alone is not physical progress (e.g. a door or
            // collision mismatch). Require renewed actual movement input toward
            // this survivor continuously after the fresh pursuit request.
            if (HasPursuitIntent(probe))
            {
                if (float.IsNaN(probe.IntentSince)) probe.IntentSince = now;
            }
            else probe.IntentSince = float.NaN;
            if (now < probe.NextPassAt) return 0;

            // One CalculatePath at most per tick, even with four companions.
            var target = probe.Targets[probe.Index];
            if (!NavMesh.SamplePosition(target.Position, out var destination, 1.2f, NavMesh.AllAreas)
                || Math.Abs(destination.position.y - target.Position.y) > .6f)
            { CancelProbe(now); return 0; } // airborne/unknown target is not unreachable proof
            bool reachable = NavMesh.CalculatePath(candidate.Body.Position, destination.position,
                NavMesh.AllAreas, Scratch) && Scratch.status == NavMeshPathStatus.PathComplete;
            probe.AnyReachable |= reachable;
            if (++probe.Index < probe.Count) return 0;
            if (probe.AnyReachable) { probe.ReachablePasses++; probe.UnreachablePasses = 0; }
            else { probe.UnreachablePasses++; probe.ReachablePasses = 0; }
            bool proven = probe.UnreachablePasses >= 2
                || (probe.ReachablePasses >= 2 && !float.IsNaN(probe.IntentSince)
                    && now - probe.IntentSince >= RenewedIntentSeconds);
            probe.Index = 0;
            probe.AnyReachable = false;
            probe.NextPassAt = now + RetrySeconds;
            if (!proven) return 0;

            // Re-read exact identity, combat, geometry and mode immediately before
            // Kill. The administrative gate suppresses the synchronous kill,
            // assist and reward observers; a throwing subscriber may still have
            // killed the body, so health readback also happens after an exception.
            if (!Admitted(mode) || !Current(candidate, mode) || !Stationary(candidate, now)
                || !SafeTargets(probe, capture: false)) { CancelProbe(now); return 0; }
            var health = candidate.Body.HealthController;
            var killer = candidate.Body.ActiveHealthController;
            try
            {
                using (ArenaAdministrativeDeathGate.Enter(candidate.Body.ProfileId))
                    killer.Kill(EDamageType.Undefined);
            }
            catch (Exception ex) { Warn(ex); }
            // Native death callbacks may immediately detach/dispose the owner.
            // The captured controller is the one we just proved and killed.
            bool dead = health.IsAlive == false;
            CancelProbe(now);
            if (!dead || !Admitted(mode)) return 0;
            Entries.Remove(candidate);
            _recovered++;
            Plugin.ModLogger.LogDebug($"Arena zombie recovery: replaced one confirmed stuck infected in wave {wave} ({_recovered} recovery); no combat credit.");
            return 1;
        }
        catch (Exception ex) { CancelProbe(Time.time); Warn(ex); return 0; }
    }

    private static bool Admitted(ZombieMode mode)
        => mode != null && ReferenceEquals(ArenaMatchService.Current?.Mode, mode)
            && mode.IsRecoveryAdmissionOpen && Time.timeScale > 0f
            && !ArenaRespawnDriver.IsRespawnInProgress && !ArenaRespawnDriver.StandDown;

    private static bool Current(Entry entry, ZombieMode mode)
    {
        var owner = entry.Owner;
        var body = entry.Body;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        if (!ReferenceEquals(entry.Mode, mode) || world == null || !ReferenceEquals(entry.World, world)
            || game == null || game.Status != GameStatus.Started || !ArenaSession.IsExactActiveRaid(game, world)
            || owner == null || owner.IsDead || owner.BotState != EBotState.Active || body == null
            || !body.IsAI || body.IsYourPlayer || ReferenceEquals(world.MainPlayer, body)
            || !ReferenceEquals(owner.GetPlayer, body) || !ReferenceEquals(body.AIData?.BotOwner, owner)
            || body.HealthController?.IsAlive != true || body.ActiveHealthController == null
            || string.IsNullOrEmpty(body.ProfileId)
            || !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)
            || owner.IsFollower() || owner.BotFollower?.HaveBoss == true) return false;
        var settings = body.Profile?.Info?.Settings;
        return settings != null && (settings.Role == WildSpawnType.infectedAssault
            || settings.Role == WildSpawnType.infectedPmc || settings.Role == WildSpawnType.infectedCivil
            || settings.Role == WildSpawnType.infectedLaborant);
    }

    private static bool Stationary(Entry entry, float now)
    {
        var owner = entry.Owner;
        var memory = owner.Memory;
        var enemy = memory?.GoalEnemy;
        var standby = owner.StandBy;
        bool combat = standby == null || (standby.StandByType != BotStandByType.none
                && standby.StandByType != BotStandByType.active)
            || owner.Mover == null || owner.Mover.Pause || owner.Mover.RemainPause > 0f
            || owner.Mover.CurrentState == EBotMoverState.NearDoor || owner.DoorOpener == null
            || owner.DoorOpener.Interacting || owner.DoorOpener._enteringDoorSequence || owner.DoorOpener._shallStartInteract
            || memory == null || memory.IsDamaged || memory.LastDamageDataActive
            || now - memory.LastTimeHit < CombatGraceSeconds
            || owner.ShootData?.Shooting == true || now - (owner.ShootData?.LastTriggerPressd ?? now) < CombatGraceSeconds
            || now - (owner.WeaponManager?.Melee?.LastTimeEnemyHit ?? now) < CombatGraceSeconds
            || (enemy != null && (enemy.IsVisible || enemy.CanShoot
                || now - enemy.PersonalLastSeenTime < CombatGraceSeconds
                || now - enemy.PersonalLastShootTime < CombatGraceSeconds));
        if (combat || (entry.Body.Position - entry.Anchor).sqrMagnitude >= MovementSquared)
        { entry.Restart(now); return false; }
        return true;
    }

    private static Probe BeginProbe(Entry entry, float now)
    {
        var probe = new Probe { Entry = entry, StartedAt = now, NextPassAt = now + RetrySeconds };
        if (!SafeTargets(probe, capture: true)) { entry.Restart(now); return null; }
        // Same pursuit refresh as the ordinary idle nudge; never teleport or
        // override movement/attack nodes. Give native pursuit time to respond.
        ArenaHostility.ReportPlayerTo(entry.Owner.BotsGroup, probe.Targets[0]);
        entry.Owner.BotsGroup?.AddPointToSearch(probe.Targets[0].Position, 1000f, entry.Owner);
        return probe;
    }

    private static bool SafeTargets(Probe probe, bool capture)
    {
        var entry = probe.Entry;
        var members = entry.Mode.LiveTeamMembers;
        int count = entry.Mode.IsTeamArena ? members.Count : 1;
        if (count < 1 || count > probe.Targets.Length || (!capture && count != probe.Count)) return false;
        for (int i = 0; i < count; i++)
        {
            var player = entry.Mode.IsTeamArena ? members[i] : entry.World.MainPlayer;
            if (player == null || player.HealthController?.IsAlive != true
                || player.MovementContext == null || !player.MovementContext.IsGrounded
                || !ReferenceEquals(entry.World.GetAlivePlayerByProfileID(player.ProfileId), player)
                || (player.Position - entry.Body.Position).sqrMagnitude <= SafeDistanceSquared) return false;
            if (!capture && (!ReferenceEquals(probe.Targets[i], player)
                || (probe.Positions[i] - player.Position).sqrMagnitude > 4f)) return false;
            // Physical occlusion from both head and torso; native vision alone
            // is insufficient (Crowd can intentionally bypass its LOS checks).
            int mask = LayersMaskController.PlayerStaticCollisionsMask;
            if (!Physics.Linecast(entry.Body.Position + Vector3.up * 1.5f, player.Position + Vector3.up * 1.5f,
                    mask, QueryTriggerInteraction.Ignore)
                || !Physics.Linecast(entry.Body.Position + Vector3.up * .8f, player.Position + Vector3.up * .8f,
                    mask, QueryTriggerInteraction.Ignore)) return false;
            if (capture) { probe.Targets[i] = player; probe.Positions[i] = player.Position; }
        }
        probe.Count = count;
        return true;
    }

    private static bool HasPursuitIntent(Probe probe)
    {
        var bot = probe.Entry.Owner;
        var mover = bot.Mover;
        if (mover == null || !mover.IsMoving || !mover.HasPathAndNoComplete || mover.Pause
            || mover.RemainPause > 0f || mover.CurrentState == EBotMoverState.NearDoor
            || bot.DoorOpener == null || bot.DoorOpener.Interacting || bot.DoorOpener._enteringDoorSequence
            || bot.DoorOpener._shallStartInteract || !mover.TargetPoint.HasValue
            || probe.Entry.Body.MovementContext == null
            || probe.Entry.Body.MovementContext.MovementDirection.sqrMagnitude < .01f) return false;
        var target = bot.Memory?.GoalEnemy?.Person;
        for (int i = 0; i < probe.Count; i++)
            if (ReferenceEquals(target, probe.Targets[i])
                && (mover.TargetPoint.Value - probe.Targets[i].Position).sqrMagnitude <= 16f) return true;
        return false;
    }

    private static void CancelProbe(float now)
    {
        if (_probe?.Entry.Body != null) _probe.Entry.Restart(now);
        _probe = null;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena zombie individual recovery deferred: " + ex.GetBaseException().Message);
    }
}
