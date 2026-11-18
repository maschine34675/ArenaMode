using System;
using System.Reflection;
using ArenaMode.Diagnostics;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Two complete native approach/melee planning jobs per frame, shared by exact
/// owned arrivals only. The separate individual-recovery route probe remains
/// bounded to one per 0.25s. This is not a cap on nested NavMesh calls or CPU time.
/// </summary>
internal static class ZombiePathBudgetRuntime
{
    private static readonly ZombiePathBudgetPolicy Budget = new ZombiePathBudgetPolicy();
    private static readonly Func<object, object, bool> CurrentTicket = IsCurrentTicket;
    private static object _world, _mode;
    private static float _nextLog;
    private static bool _warned;
    internal static int LastBudgetFrame => Budget.Frame;
    internal static int FrameJobs => Budget.Frame == Time.frameCount ? Budget.FrameJobs : 0;
    internal static int PeakJobs => Budget.PeakJobs;
    internal static int Pending => Budget.Pending;
    internal static int PeakPending => Budget.PeakPending;
    internal static int OldestWaitFrames => Budget.OldestWaitFrames;
    internal static long ApproachJobs { get; private set; }
    internal static long MeleeJobs { get; private set; }
    internal static long Deferrals { get; private set; }

    internal static void EnablePatch()
    {
        try { new ArenaZombiePathBudgetApproachPatch().Enable(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning("Arena zombie approach planning budget unavailable: " + ex.GetBaseException().Message); }
    }

    internal static void Reset()
    {
        Budget.Reset(); _world = _mode = null; _nextLog = 0f; _warned = false;
        ApproachJobs = MeleeJobs = Deferrals = 0;
    }

    // Called after native TryHit, steering, sprint and pose, but before any path
    // clock/write. Deferral means continue that movement/attack tick unchanged.
    internal static bool ShouldDeferMelee(BotMeleeWeaponData melee)
    {
        if (!ArenaSession.IsActive) return false;
        try
        {
            var owner = melee?._owner;
            if (owner?.Mover == null || owner.Memory?.GoalEnemy == null) return false;
            if (owner.Mover.HasPathAndNoComplete && !(melee._runPathCheck < Time.time)) return false;
            return !TryTake(owner, melee: true);
        }
        catch (Exception ex) { Warn(ex); return false; }
    }

    internal static bool AllowApproach(MoveToEnemyZigZag node)
    {
        // Leave even native's odd cheap target/path check untouched. An admitted
        // due check counts conservatively as a job even if it ends up cheap.
        if (!ArenaSession.IsActive) return true;
        try
        {
            if (node == null || node._nextCheck > Time.time) return true;
            return TryTake(node._owner, melee: false);
        }
        catch (Exception ex) { Warn(ex); return true; }
    }

    private static bool TryTake(BotOwner owner, bool melee)
    {
        if (owner == null || !ZombieRecoveryRuntime.OwnsCurrentBody(owner)) return true;
        var target = owner.Memory?.GoalEnemy?.Person;
        if (target == null || target.HealthController?.IsAlive != true) return true;
        var world = Singleton<GameWorld>.Instance;
        var mode = ArenaMatchService.Current?.Mode;
        if (!ReferenceEquals(_world, world) || !ReferenceEquals(_mode, mode))
        {
            Reset(); _world = world; _mode = mode;
        }
        bool granted = Budget.TryAcquire(owner.GetPlayer, target, Time.frameCount, CurrentTicket);
        if (granted) { if (melee) MeleeJobs++; else ApproachJobs++; }
        else Deferrals++;
        if (ArenaLog.DeveloperEnabled && Time.time >= _nextLog)
        {
            _nextLog = Time.time + 5f;
            ArenaLog.Developer($"Zombie planning jobs: approach={ApproachJobs}, melee={MeleeJobs}, deferred={Deferrals}, frame={Time.frameCount}, used={FrameJobs}/2, peak={PeakJobs}, pending={Pending}, oldest={OldestWaitFrames} frames; native nested searches remain intact.");
        }
        return granted;
    }

    private static bool IsCurrentTicket(object bodyObject, object target)
    {
        var body = bodyObject as Player;
        var owner = body == null ? null : body.AIData?.BotOwner;
        return body != null && owner != null && ReferenceEquals(owner.GetPlayer, body)
            && ReferenceEquals(owner.Memory?.GoalEnemy?.Person, target)
            && owner.Memory.GoalEnemy.Person.HealthController?.IsAlive == true
            && ZombieRecoveryRuntime.OwnsCurrentBody(owner);
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena zombie planning budget deferred; native movement retained: " + ex.GetBaseException().Message);
    }

    private sealed class ArenaZombiePathBudgetApproachPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MoveToEnemyZigZag), nameof(MoveToEnemyZigZag.NotMovingCheck));
        [PatchPrefix]
        private static bool Prefix(MoveToEnemyZigZag __instance) => AllowApproach(__instance);
    }
}
