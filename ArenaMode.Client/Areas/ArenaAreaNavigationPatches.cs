using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Areas;

/// <summary>
/// Execution guards for the opt-in area's exact TDM bodies. Geometry/readiness
/// belongs to ArenaAreaRuntime; no global navigation, SAIN or PFT state is edited.
/// Goal preflight avoids native recovery for a forbidden target. The controller
/// guards also inspect the actual accepted path: baked cover/core paths and
/// callers supplying corners directly can bypass BotMover.GoToPoint entirely.
/// </summary>
internal static class ArenaAreaNavigationPatches
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".AreaNavigation");
    internal static bool IsReady { get; private set; }

    private readonly struct GoalRequest
    {
        internal readonly BotOwner Owner;
        internal readonly long Version;
        internal GoalRequest(BotOwner owner)
        {
            Owner = owner;
            Version = ArenaAreaRuntime.GetRejectionVersion(owner);
        }
    }

    private sealed class Binding
    {
        internal MethodInfo Target, Prefix, Postfix;
    }

    internal static void Enable()
    {
        if (IsReady) return;
        try
        {
            // Resolve the COMPLETE contract before installing the first patch.
            // A drifted optional area must not retain a partial barrier system.
            Type[] moverPoint = { typeof(Vector3), typeof(bool), typeof(float),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool) };
            Type[] finderPoint = { typeof(Vector3), typeof(bool), typeof(float),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) };
            var bindings = new List<Binding>
            {
                Bind(typeof(BotMover), "GoToPoint", typeof(NavMeshPathStatus), moverPoint,
                    nameof(BeforePoint), nameof(AfterGoal)),
                Bind(typeof(BotMover), "CurrentStateGoToPoint", typeof(NavMeshPathStatus), moverPoint,
                    nameof(BeforePoint), nameof(AfterGoal)),
                Bind(typeof(BotMover), "GoToPoint", typeof(NavMeshPathStatus),
                    new[] { typeof(CustomNavigationPoint), typeof(bool), typeof(bool) },
                    nameof(BeforeLinkedPoint), nameof(AfterGoal)),
                Bind(typeof(BotPathFinderCorePoints), "GoToPosition", typeof(NavMeshPathStatus), finderPoint,
                    nameof(BeforePoint), nameof(AfterGoal)),
                Bind(typeof(BotPathFinderCorePoints), "GoToPosition", typeof(NavMeshPathStatus),
                    new[] { typeof(IAICorePointLink), typeof(bool), typeof(float), typeof(bool) },
                    nameof(BeforeLinkedPoint), nameof(AfterGoal)),
                Bind(typeof(BotMover), "GoToPointNoWay", typeof(void), new[] { typeof(Vector3?) },
                    nameof(BeforeDirectPoint)),
                Bind(typeof(BotPathController), "GoToPointNoWay", typeof(void), new[] { typeof(Vector3?) },
                    nameof(BeforeDirectPoint)),
                Bind(typeof(BotMover), "GoToByWay", typeof(void), new[] { typeof(Vector3[]), typeof(float) },
                    nameof(BeforeCorners)),
                Bind(typeof(BotPathController), "GoToByWay", typeof(void), new[] { typeof(Vector3[]), typeof(float) },
                    nameof(BeforeCorners)),
                Bind(typeof(BotPathController), "SetPath", typeof(void), new[] { typeof(Vector3[]) },
                    nameof(BeforeCorners)),
                Bind(typeof(BotPathController), "SetCurPath", typeof(void), new[] { typeof(VectorTargetBotPath) },
                    nameof(BeforeCurrentPath)),
                Bind(typeof(BotPathController), "set_CurPath", typeof(void), new[] { typeof(AbstractBotPath) },
                    nameof(BeforeCurrentPath)),
                Bind(typeof(BotMover), "Teleport", typeof(void), new[] { typeof(Vector3) },
                    nameof(BeforeMoverTeleport), nameof(AfterMoverTeleport)),
                Bind(typeof(Player), "Teleport", typeof(void), new[] { typeof(Vector3), typeof(bool) },
                    nameof(BeforePlayerTeleport))
            };
            if (!typeof(IAICorePointLink).IsAssignableFrom(typeof(CustomNavigationPoint)))
                throw new InvalidOperationException("CustomNavigationPoint no longer implements IAICorePointLink");
            foreach (Binding binding in bindings)
                Patches.Patch(binding.Target,
                    prefix: new HarmonyMethod(binding.Prefix) { priority = Priority.Last },
                    postfix: binding.Postfix == null ? null
                        : new HarmonyMethod(binding.Postfix) { priority = Priority.Last });
            IsReady = true;
            Plugin.ModLogger.LogInfo("Arena area: native goal, path and teleport guards ready.");
        }
        catch (Exception exception)
        {
            Shutdown();
            Plugin.ModLogger.LogError("Arena area navigation contract unavailable; bounded play must stay disabled: "
                + exception.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        IsReady = false;
        Patches.UnpatchSelf();
    }

    private static Binding Bind(Type type, string name, Type result, Type[] parameters,
        string prefix, string postfix = null)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
        var target = type.GetMethod(name, flags, null, parameters, null);
        if (target == null || target.ReturnType != result || target.IsStatic
            || !typeof(BotData).IsAssignableFrom(type) && type != typeof(Player))
            throw new MissingMethodException(type.FullName, name);
        return new Binding
        {
            Target = target,
            Prefix = Hook(prefix),
            Postfix = postfix == null ? null : Hook(postfix)
        };
    }

    private static MethodInfo Hook(string name)
        => typeof(ArenaAreaNavigationPatches).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new MissingMethodException(nameof(ArenaAreaNavigationPatches), name);

    private static bool BeforePoint(BotData __instance, Vector3 __0,
        ref NavMeshPathStatus __result, out GoalRequest __state)
        => BeginGoal(__instance, __0, true, ref __result, out __state);

    private static bool BeforeLinkedPoint(BotData __instance, IAICorePointLink __0,
        ref NavMeshPathStatus __result, out GoalRequest __state)
    {
        __state = default;
        // Do not inspect foreign goals (including native null/error behavior).
        if (!Scoped(__instance?._owner)) return true;
        return BeginGoal(__instance, __0 == null ? default : __0.Position, __0 != null,
            ref __result, out __state);
    }

    private static bool BeginGoal(BotData source, Vector3 target, bool hasTarget,
        ref NavMeshPathStatus result, out GoalRequest state)
    {
        state = default;
        BotOwner owner = source?._owner;
        if (!Scoped(owner)) return true;
        state = new GoalRequest(owner);
        if (hasTarget && ArenaAreaRuntime.AllowDestination(owner, target)) return true;
        Reject(owner, source, "goal destination rejected at " + target.ToString("F2"));
        result = NavMeshPathStatus.PathInvalid;
        return false;
    }

    private static void AfterGoal(GoalRequest __state, ref NavMeshPathStatus __result)
    {
        // SetCurPath has no return value; the native finder otherwise reports
        // PathComplete even if its computed path was refused internally. A
        // per-owner version also propagates nested/direct-teleport rejections
        // without a ThreadStatic context leaking into another bot's callback.
        if (Scoped(__state.Owner)
            && ArenaAreaRuntime.GetRejectionVersion(__state.Owner) != __state.Version)
            __result = NavMeshPathStatus.PathInvalid;
    }

    private static bool BeforeDirectPoint(BotData __instance, Vector3? __0)
    {
        // Null is the native no-op. It must remain available during teardown.
        BotOwner owner = __instance?._owner;
        if (!__0.HasValue || !Scoped(owner)) return true;
        if (ArenaAreaRuntime.AllowDestination(owner, __0.Value)) return true;
        Reject(owner, __instance, "direct movement destination rejected at " + __0.Value.ToString("F2"));
        return false;
    }

    private static bool BeforeCorners(BotData __instance, Vector3[] __0)
    {
        BotOwner owner = __instance?._owner;
        if (!Scoped(owner)) return true;
        if (ArenaAreaRuntime.AllowPath(owner, __0)) return true;
        Reject(owner, __instance, "supplied path rejected");
        return false;
    }

    private static bool BeforeCurrentPath(BotPathController __instance, AbstractBotPath __0)
    {
        BotOwner owner = __instance?._owner;
        // Native Stop -> SetCurPath(null) -> CurPath=null is always allowed.
        if (__0 == null || !Scoped(owner)) return true;
        if (ArenaAreaRuntime.AllowPath(owner, __0._path)) return true;
        Reject(owner, __instance, "accepted path rejected");
        return false;
    }

    private static bool BeforeMoverTeleport(BotMover __instance, Vector3 __0, out GoalRequest __state)
    {
        __state = default;
        BotOwner owner = __instance?._owner;
        if (!Scoped(owner)) return true;
        __state = new GoalRequest(owner);
        Player player = owner.GetPlayer;
        if (ArenaAreaRuntime.AllowTeleport(player, __0)) return true;
        ArenaAreaRuntime.NoteRejectedTeleport(player, "mover teleport rejected at " + __0.ToString("F2"));
        StopUnsafePaths(owner, __instance);
        // Reject BEFORE BotMover updates PositionOnWayInner, not merely at the
        // inner Player.Teleport call, which would leave body/mover desynchronized.
        return false;
    }

    private static void AfterMoverTeleport(BotMover __instance, GoalRequest __state)
    {
        if (!Scoped(__state.Owner) || !ReferenceEquals(__instance._owner, __state.Owner)
            || ArenaAreaRuntime.GetRejectionVersion(__state.Owner) == __state.Version) return;
        // SetPlayerToNavMesh can write its recovery candidate before Teleport;
        // Teleport itself writes it after Player.Teleport. Restore both forms of
        // rejected recovery to the live body's actual position, including a
        // rejection raised only by the nested Player.Teleport guard. Never move
        // the player or restore a captured position from a different body/raid.
        __instance.PositionOnWayInner = __state.Owner.GetPlayer.Position;
    }

    private static bool BeforePlayerTeleport(Player __instance, Vector3 __0)
    {
        if (!IsReady || !ArenaAreaRuntime.TryGetForPlayer(__instance, out _)) return true;
        if (ArenaAreaRuntime.AllowTeleport(__instance, __0)) return true;
        ArenaAreaRuntime.NoteRejectedTeleport(__instance, "direct player teleport rejected at " + __0.ToString("F2"));
        BotOwner owner = __instance.AIData?.BotOwner;
        if (Scoped(owner)) StopUnsafePaths(owner, owner.Mover);
        return false;
    }

    private static bool Scoped(BotOwner owner)
        => IsReady && !ReferenceEquals(owner, null) && ArenaAreaRuntime.TryGetForBot(owner, out _);

    private static void Reject(BotOwner owner, BotData source, string reason)
    {
        ArenaAreaRuntime.NoteRejected(owner, reason);
        StopUnsafePaths(owner, source);
    }

    private static void StopUnsafePaths(BotOwner owner, BotData source)
    {
        // Refusing one bad cover/retreat request must not erase a legitimate
        // already-running route. Only an unsafe current route is cleared, using
        // the native null-path flow (timestamps and OnWayChanged included).
        BotPathController direct = source as BotPathController;
        if (source is BotPathFinderCorePoints finder) direct = finder._pathController;
        BotMover mover = owner.Mover;
        BotMoverStateMachine machine = mover?._moverStateMachine;
        BotPathController actual = machine?.CurrentMoverState?.PathController;
        BotPathController standard = null;
        if (machine?._states != null && machine._states.TryGetValue(EBotMoverState.Default, out var standardState))
            standard = standardState?.PathController;
        ClearIfUnsafe(owner, direct);
        if (!ReferenceEquals(actual, direct)) ClearIfUnsafe(owner, actual);
        if (!ReferenceEquals(standard, direct) && !ReferenceEquals(standard, actual))
            ClearIfUnsafe(owner, standard);
    }

    private static void ClearIfUnsafe(BotOwner owner, BotPathController controller)
    {
        AbstractBotPath current = controller?.CurPath;
        if (current == null || ArenaAreaRuntime.AllowPath(owner, current._path)) return;
        controller.SetCurPath(null);
        // Mirror BotMover.Stop for the active controller without invoking
        // CheckIsFinished on a route whose target is outside the allowed area.
        // OnWayChanged may synchronously install a new, valid route. Do not
        // overwrite that callback's movement state after the old path is gone.
        if (controller.CurPath == null
            && ReferenceEquals(owner.Mover?._moverStateMachine?.CurrentMoverState?.PathController, controller))
            owner.Mover.IsMoving = false;
    }
}
