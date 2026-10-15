#nullable disable
using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Tune only values consumed by native locomotion. Never edit Physical, inventory,
/// skills, global curves or stored animator settings, and never rerun reweighting.
/// </summary>
internal static class ArenaBotAgility
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".BotAgility");
    private static bool _ready, _warned;
    [ThreadStatic] private static Scope _scope;
    private static WeakReference _loggedWorld;

    internal enum ValueKind { Walk, Side, Diagonal, Inertia, Acceleration, PreSprint, Sprint }
    internal struct Scope
    {
        internal bool Active;
        internal object Receiver, State;
        internal MovementContext Context;
        internal Player Body;
        internal BotOwner Owner;
        internal PhysicalBase Physical;
    }
    internal struct RestoreScope
    {
        internal bool Entered;
        internal Scope Previous;
    }
    private sealed class Site
    {
        internal MemberInfo Member;
        internal ValueKind Kind;
        internal int Count;
        internal Site(MemberInfo member, ValueKind kind, int count = 1)
        { Member = member ?? throw new MissingMemberException("bot agility operand"); Kind = kind; Count = count; }
    }
    private static readonly Dictionary<MethodBase, Site[]> Sites = new();

    internal static void Enable()
    {
        if (_ready) return;
        try
        {
            Sites.Clear();
            MemberInfo Get(string name) => typeof(MovementContext).GetProperty(name)?.GetMethod;
            MemberInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            void Add(Type type, string name, Type[] args, params Site[] sites)
            {
                var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, args, null);
                if (method?.ReturnType != typeof(void)) throw new MissingMethodException("bot agility " + name);
                foreach (var site in sites)
                    if ((site.Member is FieldInfo f && f.FieldType != typeof(float))
                        || (site.Member is MethodInfo m && (m.ReturnType != typeof(float) || m.GetParameters().Length != 0)))
                        throw new InvalidOperationException("unsupported bot agility scalar " + site.Member.Name);
                Sites.Add(method, sites);
            }
            Add(typeof(MovePlayerState), nameof(MovePlayerState.UpdateMovementDirection), new[] { typeof(float) },
                new Site(Get(nameof(MovementContext.WalkInertia)), ValueKind.Walk, 2),
                new Site(Get(nameof(MovementContext.MoveSideInertia)), ValueKind.Side),
                new Site(Get(nameof(MovementContext.MoveDiagonalInertia)), ValueKind.Diagonal),
                new Site(Get(nameof(MovementContext.Inertia)), ValueKind.Inertia),
                new Site(Field(typeof(GlobalConfiguration.InertiaSettings), "SuddenChangesSmoothness"), ValueKind.Acceleration));
            Add(typeof(MovePlayerState), nameof(MovePlayerState.ProcessDirection), new[] { typeof(float), typeof(float) },
                new Site(Field(typeof(MovePlayerState), "_directionSpeedAcceleration"), ValueKind.Acceleration));
            Add(typeof(MovementContext), nameof(MovementContext.PreSprintAcceleration), new[] { typeof(float) },
                new Site(Field(typeof(PhysicalBase), nameof(PhysicalBase.PreSprintAcceleration)), ValueKind.PreSprint));
            Add(typeof(MovementContext), nameof(MovementContext.SprintAcceleration), new[] { typeof(float) },
                new Site(Field(typeof(PhysicalBase), nameof(PhysicalBase.SprintAcceleration)), ValueKind.Sprint),
                new Site(Field(typeof(PhysicalBase), nameof(PhysicalBase.Inertia)), ValueKind.Inertia));
            foreach (var method in Sites.Keys)
                Patches.Patch(method, prefix: new HarmonyMethod(typeof(ArenaBotAgility), nameof(Prefix)) { priority = Priority.Last },
                    transpiler: new HarmonyMethod(typeof(ArenaBotAgility), nameof(Transpiler)),
                    finalizer: new HarmonyMethod(typeof(ArenaBotAgility), nameof(Finalizer)) { priority = Priority.Last });
            _ready = true;
            Plugin.ModLogger.LogDebug("Arena bot agility ready: acceleration x1.5, walk blend time x0.6, remaining weight inertia penalties halved; native clocks, collision and inventory retained.");
        }
        catch (Exception ex) { _ready = false; Patches.UnpatchSelf(); Sites.Clear(); Warn(ex); }
    }

    private static bool Eligible(MovementContext context, out Player body, out BotOwner owner)
    {
        body = null; owner = null;
        if (!_ready || ArenaRespawnDriver.StandDown || Time.timeScale <= 0f
            || !ArenaBotMovement.TryGetLiveBot(context, out body, out owner)) return false;
        var state = context.CurrentState?.Name ?? EPlayerState.None;
        return (state == EPlayerState.Idle || state == EPlayerState.Run || state == EPlayerState.Sprint)
            && context.IsGrounded && !context.IsInPronePose && !context.IsInMountedState
            && context.PlatformMotion == Vector3.zero && context.StationaryWeapon == null
            && owner.DoorOpener?.Interacting != true && owner.DoorOpener?.NearDoor != true
            && owner.Mover?.CurrentState != EBotMoverState.NearDoor && ArenaBotMovementSain.AllowsBoost(owner, body);
    }

    private static void Prefix(object __instance, float __0, out RestoreScope __state)
    {
        __state = new RestoreScope { Entered = true, Previous = _scope };
        // Every invocation is a barrier, even an ineligible nested/foreign call.
        _scope = default;
        try
        {
            var move = __instance as MovePlayerState;
            var context = move?.MovementContext ?? __instance as MovementContext;
            if (!Finite(__0) || __0 <= 0f || !Eligible(context, out var body, out var owner)
                || body.Physical == null || (move != null && !ReferenceEquals(context.CurrentState, move))) return;
            _scope = new Scope { Active = true, Receiver = __instance, Context = context, State = context.CurrentState,
                Body = body, Owner = owner, Physical = body.Physical };
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static Exception Finalizer(Exception __exception, RestoreScope __state)
    {
        // A preceding foreign prefix can skip ours or throw before admission.
        if (__state.Entered) _scope = __state.Previous;
        return __exception;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        if (!Sites.TryGetValue(__originalMethod, out var sites)) throw new InvalidOperationException("unknown bot agility method");
        var code = instructions.ToList();
        bool Matches(CodeInstruction i, Site s) => Equals(i.operand, s.Member)
            && (s.Member is FieldInfo ? i.opcode == OpCodes.Ldfld : i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt);
        foreach (var site in sites)
            if (code.Count(i => Matches(i, site)) != site.Count)
                throw new InvalidOperationException("bot agility IL changed: " + __originalMethod.Name + "/" + site.Member.Name);
        var helper = typeof(ArenaBotAgility).GetMethod(nameof(Tune), BindingFlags.Static | BindingFlags.NonPublic);
        // Retain original instruction objects, labels and exception boundaries.
        for (int index = code.Count - 1; index >= 0; index--)
        {
            var site = sites.FirstOrDefault(s => Matches(code[index], s));
            if (site == null) continue;
            code.InsertRange(index + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldc_I4, (int)site.Kind), new CodeInstruction(OpCodes.Call, helper) });
        }
        return code;
    }

    private static float Tune(float native, object receiver, ValueKind kind)
    {
        if (!_scope.Active || !_ready || !Finite(native) || native <= 0f || !ReferenceEquals(receiver, _scope.Receiver)) return native;
        try
        {
            // Admission can close inside another prefix/callback. Never lend a
            // cached scope to a different body, physical object or movement state.
            if (ArenaRespawnDriver.StandDown || Time.timeScale <= 0f || !ArenaBotMovement.IsLiveBot(_scope.Owner)
                || !ReferenceEquals(_scope.Owner.GetPlayer, _scope.Body)
                || !ReferenceEquals(_scope.Body.MovementContext, _scope.Context)
                || !ReferenceEquals(_scope.Context.CurrentState, _scope.State)
                || !ReferenceEquals(_scope.Body.Physical, _scope.Physical)
                || !Singleton<GlobalConfiguration>.Instantiated) return native;
            var inertia = Singleton<GlobalConfiguration>.Instance.Inertia;
            if (inertia == null) return native;
            float adjusted = native;
            switch (kind)
            {
                case ValueKind.Inertia: adjusted = native * 0.5f; break;
                case ValueKind.Walk: adjusted = Lighter(native, inertia.WalkInertia.x, false) * 0.6f; break;
                case ValueKind.Side: adjusted = Lighter(native, inertia.SideTime.x, false); break;
                case ValueKind.Diagonal: adjusted = Lighter(native, inertia.DiagonalTime.x, false); break;
                case ValueKind.Acceleration: adjusted = native * 1.5f; break;
                case ValueKind.PreSprint: adjusted = Lighter(native, inertia.PreSprintAccelerationLimits.x, true) * 1.5f; break;
                case ValueKind.Sprint:
                    adjusted = Lighter(native, Mathf.InverseLerp(inertia.SprintAccelerationLimits.x, inertia.SprintAccelerationLimits.y, 0f), true) * 1.5f;
                    break;
            }
            if (!Finite(adjusted) || adjusted <= 0f) return native;
            if (adjusted != native && !ReferenceEquals(_loggedWorld?.Target, Singleton<GameWorld>.Instance))
            {
                _loggedWorld = new WeakReference(Singleton<GameWorld>.Instance);
                Plugin.ModLogger.LogDebug("Arena bot agility active: lighter locomotion inertia and faster acceleration/turn blending; no stored weight changes.");
            }
            return adjusted;
        }
        catch (Exception ex) { Warn(ex); return native; }
    }

    private static float Lighter(float native, float baseline, bool higherIsBetter)
    {
        if (!Finite(baseline) || baseline < 0f) return native;
        // Some native side/diagonal curves become faster with weight. Never make
        // them worse, or overwrite a value already improved by another mod.
        if (higherIsBetter ? baseline <= native : baseline >= native) return native;
        return native * 0.5f + baseline * 0.5f;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bot agility unavailable for this operation; native values retained: " + ex.GetBaseException().Message);
    }
    internal static void Shutdown()
    { _ready = false; Patches.UnpatchSelf(); Sites.Clear(); _scope = default; _loggedWorld = null; _warned = false; }
}
