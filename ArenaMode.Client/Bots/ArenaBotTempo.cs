#nullable disable
using ArenaMode.Movement;
using ArenaMode.Respawn;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.Bots;

/// <summary>Optional, exact-version SAIN adapter. Changes call inputs, never shared presets.</summary>
internal static class ArenaBotTempo
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".BotTempo");
    private static bool _ready, _warned;
    private static Type _botType, _actionType, _searchAction, _pathType;
    private static FieldInfo _actionBot, _waitTimer, _unpauseTimer, _pathBot, _strafeField;
    private static Lens _sourceBot, _owner, _player, _layers, _action, _search, _mover, _dogFight,
        _searchActive, _searchTarget, _searchState, _selfDecision, _danger,
        _visible, _lineOfSight, _enemyBot, _enemyPlayer, _enemyVisible, _enemyLos,
        _heardFromPeace, _enemyPath, _activePath, _pathFinder, _paused, _cancelled, _recalc, _started, _pathId;
    private static object _waitState, _selfNone;
    private static ConditionalWeakTable<object, WaitObservation> _waits = new();
    private static WeakReference _logWorld;
    private static float _nextLog;
    private static int _sprints, _strafes, _released;

    // Bind once; these are cached/forwarding getters verified against installed SAIN.
    // In particular, never probe BotAction.Bot (lazy initialization) or Enemy.CheckValid.
    private sealed class Lens
    {
        private readonly PropertyInfo[] _properties;
        internal Type ResultType { get; }
        internal Lens(Type type, string path, Type expected = null)
        {
            var properties = new List<PropertyInfo>();
            foreach (string name in path.Split('.'))
            {
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p?.GetMethod == null || p.GetIndexParameters().Length != 0)
                    throw new InvalidOperationException("unsupported SAIN property " + type.Name + "." + name);
                properties.Add(p); type = p.PropertyType;
            }
            if (expected != null && type != expected) throw new InvalidOperationException("unsupported SAIN type " + path);
            _properties = properties.ToArray(); ResultType = type;
        }
        internal object Get(object value)
        {
            foreach (var p in _properties) { if (value == null) return null; value = p.GetValue(value); }
            return value;
        }
        internal bool Yes(object value) => Get(value) is bool flag && flag;
    }

    internal static void Enable()
    {
        if (_ready) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SAIN");
        if (assembly == null) return;
        try
        {
            Bind(assembly);
            _ready = true;
            Plugin.ModLogger.LogDebug("Arena bot tempo ready (SAIN 4.5.1): completed-reload wait tail 0.5s, safe search sprint, combat strafe floor 0.7; native actions and collision retained.");
        }
        catch (Exception ex) { Patches.UnpatchSelf(); Warn(ex); }
    }

    private static FieldInfo Field(Type type, string name, Type expected)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field?.FieldType != expected) throw new InvalidOperationException("unsupported SAIN field " + type.Name + "." + name);
        return field;
    }

    private static void Bind(Assembly assembly)
    {
        if (assembly.GetName().Version != new System.Version(4, 5, 1, 0)) throw new InvalidOperationException("expected SAIN 4.5.1.0");
        Type Type(string name) => assembly.GetType(name, true);
        _botType = Type("SAIN.Components.BotComponent");
        var search = Type("SAIN.Classes.Bot.Search.SearchClass");
        var dog = Type("SAIN.SAINComponent.Classes.Mover.DogFight");
        var enemy = Type("SAIN.SAINComponent.Classes.EnemyClasses.Enemy");
        _pathType = Type("SAIN.SAINComponent.Classes.Mover.BotPathDataManual");
        _actionType = Type("SAIN.Layers.BotAction");
        _searchAction = Type("SAIN.Layers.Combat.Solo.SearchAction");
        _actionBot = Field(_actionType, "_bot", _botType);
        _sourceBot = new Lens(Type("SAIN.SAINComponent.BotBase"), "Bot", _botType);
        _owner = new Lens(_botType, "BotOwner", typeof(BotOwner));
        _player = new Lens(_botType, "Player", typeof(Player));
        _layers = new Lens(_botType, "SAINLayersActive", typeof(bool));
        _action = new Lens(_botType, "CurrentAction");
        _search = new Lens(_botType, "Search", search);
        _mover = new Lens(_botType, "Mover");
        _dogFight = new Lens(_botType, "Mover.DogFight", dog);
        _selfDecision = new Lens(_botType, "Decision.CurrentSelfDecision");
        _selfNone = Enum.Parse(_selfDecision.ResultType, "None");
        _danger = new Lens(_botType, "Grenade.GrenadeReactionClass.DangerGrenade");
        _visible = new Lens(_botType, "EnemyController.VisibleEnemies");
        _lineOfSight = new Lens(_botType, "EnemyController.EnemiesInLineOfSight");
        _searchActive = new Lens(search, "SearchActive", typeof(bool));
        _searchTarget = new Lens(search, "SearchTarget", enemy);
        _searchState = new Lens(search, "CurrentState");
        _waitState = Enum.Parse(_searchState.ResultType, "Wait");
        _enemyBot = new Lens(enemy, "Bot", _botType);
        _enemyPlayer = new Lens(enemy, "EnemyPlayer", typeof(Player));
        _enemyVisible = new Lens(enemy, "IsVisible", typeof(bool));
        _enemyLos = new Lens(enemy, "InLineOfSight", typeof(bool));
        _heardFromPeace = new Lens(enemy, "Hearing.EnemyHeardFromPeace", typeof(bool));
        _enemyPath = new Lens(enemy, "Path.PathToEnemy", typeof(NavMeshPath));
        _activePath = new Lens(_botType, "Mover.ActivePath");
        _pathFinder = new Lens(_pathType, "PathFinder");
        _paused = new Lens(_pathType, "PausedRequested", typeof(bool));
        _cancelled = new Lens(_pathType, "CancelRequested", typeof(bool));
        _recalc = new Lens(_pathType, "PathRecalcRequested", typeof(bool));
        _started = new Lens(_pathType, "TimeStarted", typeof(float));
        _pathId = new Lens(_pathType, "Id", typeof(int));
        _waitTimer = Field(search, "_waitAtPointTimer", typeof(float));
        _unpauseTimer = Field(_pathType, "_unpauseTime", typeof(float));
        _pathBot = Field(_pathType, "Bot", _botType);
        var moveSettings = new Lens(_botType, "Info.FileSettings").ResultType.GetField("Move", BindingFlags.Public | BindingFlags.Instance)?.FieldType
            ?? throw new InvalidOperationException("unsupported SAIN Move settings field");
        _strafeField = Field(moveSettings, "STRAFE_SPEED", typeof(float));
        MethodInfo Method(Type type, string name, Type result, params Type[] args)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, args, null);
            if (method?.ReturnType != result) throw new InvalidOperationException("unsupported SAIN method " + name);
            return method;
        }
        var searchMethod = Method(search, "Search", typeof(void), typeof(bool), enemy);
        var waitMethod = Method(search, "WaitAtPoint", typeof(bool));
        var dogMethod = Method(dog, "DogFightMove", typeof(void), typeof(bool), enemy);
        Patches.Patch(searchMethod, prefix: new HarmonyMethod(typeof(ArenaBotTempo), nameof(SearchPrefix)));
        Patches.Patch(waitMethod, prefix: new HarmonyMethod(typeof(ArenaBotTempo), nameof(WaitPrefix)),
            finalizer: new HarmonyMethod(typeof(ArenaBotTempo), nameof(WaitFinalizer)));
        Patches.Patch(dogMethod, transpiler: new HarmonyMethod(typeof(ArenaBotTempo), nameof(StrafeTranspiler)));
    }

    private static bool CanAct(object source, object enemy, bool travel, out object component, out BotOwner owner)
    {
        component = null; owner = null;
        if (!_ready || ArenaRespawnDriver.StandDown || Time.timeScale <= 0f || !Finite(Time.time)) return false;
        component = _sourceBot.Get(source);
        owner = _owner.Get(component) as BotOwner;
        if (!ArenaBotMovement.IsLiveBot(owner)) return false;
        var player = owner.GetPlayer;
        if (!ReferenceEquals(_player.Get(component), player) || !ReferenceEquals(owner.GetComponent(_botType), component)
            || !_layers.Yes(component)) return false;
        var action = _action.Get(component);
        if (action == null || action.GetType().Assembly != _botType.Assembly || !_actionType.IsInstanceOfType(action)
            || !ReferenceEquals(_actionBot.GetValue(action), component)
            || !_selfNone.Equals(_selfDecision.Get(component))) return false;
        var context = player.MovementContext;
        var state = context.CurrentState?.Name ?? EPlayerState.None;
        if ((state != EPlayerState.Idle && state != EPlayerState.Run && state != EPlayerState.Sprint)
            || !context.IsGrounded || context.IsInPronePose || context.IsInMountedState || context.StationaryWeapon != null
            || context.PlatformMotion != Vector3.zero || owner.DoorOpener?.Interacting == true || owner.DoorOpener?.NearDoor == true
            || owner.Mover?.CurrentState == EBotMoverState.NearDoor || !ArenaBotMovementSain.AllowsBoost(owner, player)
            || owner.WeaponManager?.Reload?.Reloading == true || owner.WeaponManager?.Grenades?.ThrowindNow == true
            || owner.Medecine?.FirstAid?.Using == true || owner.Medecine?.SurgicalKit?.Using == true || owner.Medecine?.Stimulators?.Using == true
            || owner.FlashGrenade?._isFlashed == true || owner.BewareGrenade?.GrenadeDangerPoint != null
            || owner.ArtilleryDangerPlace?.IsActive == true || owner.BewareBTR?.ShallRunAway() == true || _danger.Get(component) != null
            || (travel && owner.Memory?.IsUnderFire == true)) return false;
        if (enemy == null || !ReferenceEquals(_enemyBot.Get(enemy), component)) return false;
        var target = _enemyPlayer.Get(enemy) as Player;
        var world = Singleton<GameWorld>.Instance;
        if (target == null || ReferenceEquals(target, player) || target.HealthController?.IsAlive != true
            || string.IsNullOrEmpty(target.ProfileId) || !ReferenceEquals(world.GetAlivePlayerByProfileID(target.ProfileId), target)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(target.ProfileId), target)) return false;
        if (!travel) return true;
        return action.GetType() == _searchAction && !_enemyVisible.Yes(enemy) && !_enemyLos.Yes(enemy)
            && !_heardFromPeace.Yes(enemy) && _visible.Get(component) is IList visible && visible.Count == 0
            && _lineOfSight.Get(component) is IList los && los.Count == 0;
    }

    private static bool OwnSearch(object search, object component, object enemy)
        => ReferenceEquals(_search.Get(component), search) && _searchActive.Yes(search)
            && ReferenceEquals(_searchTarget.Get(search), enemy);

    private static void SearchPrefix(object __instance, ref bool __0, object __1)
    {
        if (__0) return; // Never cancel a native sprint decision.
        try
        {
            if (!CanAct(__instance, __1, true, out var bot, out var owner) || !OwnSearch(__instance, bot, __1)
                || _waitState.Equals(_searchState.Get(__instance))) return;
            var active = _activePath.Get(bot);
            if (active != null && (active.GetType() != _pathType || !ReferenceEquals(_pathBot.GetValue(active), bot)
                || !ReferenceEquals(_pathFinder.Get(active), _mover.Get(bot)) || _paused.Yes(active)
                || _cancelled.Yes(active) || _recalc.Yes(active))) return;
            var path = _enemyPath.Get(__1) as NavMeshPath;
            if (path == null || path.status != NavMeshPathStatus.PathComplete) return;
            var corners = path.corners;
            if (!LongClearSegment(corners, owner.GetPlayer.Position)) return;
            __0 = true;
            Record(0);
        }
        catch (Exception ex) { __0 = false; Warn(ex); }
    }

    // Enemy paths refresh intermittently. Project onto their polyline rather than
    // toggling sprint whenever the bot has moved away from an old path origin.
    // This only recognizes an existing route; no path/target/geometry is created.
    private static bool LongClearSegment(Vector3[] corners, Vector3 position)
    {
        if (corners == null || corners.Length < 2 || !Point(position) || !Point(corners[0])) return false;
        int segment = -1;
        float nearest = 1.5f, straight = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            if (!Point(corners[i])) return false;
            var delta = corners[i] - corners[i - 1];
            float squared = delta.sqrMagnitude;
            if (!Finite(squared) || squared < 0.0001f) continue;
            float t = Vector3.Dot(position - corners[i - 1], delta) / squared;
            if (t < 0f || t > 1f) continue;
            float distance = Vector3.Distance(position, corners[i - 1] + delta * t);
            if (distance >= nearest) continue;
            nearest = distance; segment = i; straight = Vector3.Distance(position, corners[i]);
        }
        if (segment < 0 || straight < 4f) return false;
        float remaining = straight;
        for (int i = segment + 1; i < corners.Length; i++)
            remaining += Vector3.Distance(corners[i - 1], corners[i]);
        return Finite(remaining) && remaining >= 8f;
    }

    private sealed class WaitObservation
    {
        internal object Path, Match;
        internal int Id;
        internal float Started, Deadline, SafeSince, LastCall;
    }
    internal sealed class WaitLease
    {
        internal object Search, Path;
        internal float Original, Written;
    }

    private static void WaitPrefix(object __instance, out WaitLease __state)
    {
        __state = null;
        try
        {
            var enemy = _ready ? _searchTarget.Get(__instance) : null;
            if (!CanAct(__instance, enemy, true, out var bot, out _) || !OwnSearch(__instance, bot, enemy)
                || !_waitState.Equals(_searchState.Get(__instance))) { _waits.Remove(__instance); return; }
            var path = _activePath.Get(bot);
            float now = Time.time, deadline = (float)_waitTimer.GetValue(__instance);
            if (now < 1f || !Finite(deadline) || deadline <= now || path == null || path.GetType() != _pathType
                || !ReferenceEquals(_pathBot.GetValue(path), bot) || !ReferenceEquals(_pathFinder.Get(path), _mover.Get(bot))
                || !_paused.Yes(path) || _cancelled.Yes(path) || _recalc.Yes(path)
                || (float)_unpauseTimer.GetValue(path) != deadline) { _waits.Remove(__instance); return; }
            float started = (float)_started.Get(path);
            if (!Finite(started) || started < 0f || started > now) { _waits.Remove(__instance); return; }
            int id = (int)_pathId.Get(path);
            var observation = _waits.GetValue(__instance, _ => new WaitObservation());
            var match = Modes.ArenaMatchService.Current;
            if (!ReferenceEquals(observation.Path, path) || !ReferenceEquals(observation.Match, match) || observation.Id != id
                || observation.Started != started || observation.Deadline != deadline || now < observation.LastCall || now - observation.LastCall > 0.5f)
            {
                observation.Path = path; observation.Match = match; observation.Id = id;
                observation.Started = started; observation.Deadline = deadline; observation.SafeSince = now;
            }
            observation.LastCall = now;
            if (now - observation.SafeSince < 0.5f) return;
            __state = new WaitLease { Search = __instance, Path = path, Original = deadline, Written = now - 0.25f };
            _waitTimer.SetValue(__instance, __state.Written);
            // Original WaitAtPoint owns UnPause and timer reset; we never release a path ourselves.
            _waits.Remove(__instance);
        }
        catch (Exception ex) { Restore(__state); __state = null; _waits.Remove(__instance); Warn(ex); }
    }

    private static void Restore(WaitLease lease)
    {
        try
        {
            if (lease != null && ReferenceEquals(_activePath.Get(_sourceBot.Get(lease.Search)), lease.Path)
                && (float)_waitTimer.GetValue(lease.Search) == lease.Written)
                _waitTimer.SetValue(lease.Search, lease.Original);
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static Exception WaitFinalizer(Exception __exception, WaitLease __state)
    {
        if (__state == null) return __exception;
        try
        {
            if (__exception != null) Restore(__state);
            else if ((float)_waitTimer.GetValue(__state.Search) < 0f) Record(2);
        }
        catch (Exception ex) { Warn(ex); }
        return __exception;
    }

    private static IEnumerable<CodeInstruction> StrafeTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var sites = code.Select((i, n) => (i, n)).Where(x => x.i.opcode == OpCodes.Ldfld && Equals(x.i.operand, _strafeField)).ToArray();
        if (sites.Length != 1) throw new InvalidOperationException("SAIN DogFight strafe field call site changed");
        int index = sites[0].n;
        if (index + 1 >= code.Count || (code[index + 1].opcode != OpCodes.Callvirt && code[index + 1].opcode != OpCodes.Call)
            || code[index + 1].operand is not MethodInfo method || method.Name != "SetTargetMoveSpeed"
            || method.DeclaringType != _mover.ResultType || method.ReturnType != typeof(void)
            || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(float))
            throw new InvalidOperationException("SAIN DogFight strafe receiver changed");
        code.InsertRange(index + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Call, typeof(ArenaBotTempo).GetMethod(nameof(StrafeSpeed), BindingFlags.NonPublic | BindingFlags.Static)) });
        return code;
    }
    private static float StrafeSpeed(float native, object dogFight, object enemy)
    {
        if (!Finite(native) || native <= 0f || native >= 0.7f) return native;
        try
        {
            if (!CanAct(dogFight, enemy, false, out var bot, out _) || !ReferenceEquals(_dogFight.Get(bot), dogFight)) return native;
            Record(1);
            return 0.7f;
        }
        catch (Exception ex) { Warn(ex); return native; }
    }

    private static void Record(int kind)
    {
        var world = Singleton<GameWorld>.Instance;
        if (!ReferenceEquals(_logWorld?.Target, world))
        { _logWorld = new WeakReference(world); _nextLog = Time.time + 60f; _sprints = _strafes = _released = 0; }
        if (kind == 0) _sprints++; else if (kind == 1) _strafes++; else _released++;
        if (Time.time < _nextLog) return;
        _nextLog = Time.time + 60f;
        Plugin.ModLogger.LogDebug($"Arena bot tempo (last interval): searchSprintInputs={_sprints}, strafeInputs={_strafes}, completedReloadWaits={_released}; inputs are not measured motion.");
        _sprints = _strafes = _released = 0;
    }
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    private static bool Point(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bot tempo unavailable for this operation; native behavior retained: " + ex.GetBaseException().Message);
    }
    internal static void Shutdown()
    {
        _ready = false; Patches.UnpatchSelf(); _waits = new(); _warned = false; _logWorld = null;
    }
}
