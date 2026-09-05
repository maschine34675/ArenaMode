using ArenaMode.Config;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Session;

/// <summary>
/// Applies the installed Labyrinth entrance switches' OFF states without
/// emitting switch/quest/item-placement events. All changes belong to the
/// confirmed raid's scene and emitter, never templates or global configuration.
/// The option is latched per raid: re-arming a trap around a living player is
/// deliberately not a hot-toggle operation.
/// </summary>
internal static class LabyrinthSpawnTraps
{
    internal const string ScenePath = "Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity";
    private static readonly ModulePatch[] Patches = { new DamageStartPatch(), new ShootPatch(), new EffectPatch(), new LoopStartPatch() };
    private static readonly FieldInfo PlayCoroutineField = AccessTools.Field(typeof(HandlerPlaySoundSequenced), "_playCoroutine");
    private static bool _enabled;
    private static bool _reportedFailure;
    private static GameWorld _world;
    private static AbstractGame _game;
    private static bool _suppress;
    private static bool _applied;
    private static int _attempts;
    private static float _nextAttempt;

    private sealed class DamageRule
    {
        internal readonly string Name, Start, Finish, Config;
        internal DamageRule(string name, string start, string finish, string config)
        { Name = name; Start = start; Finish = finish; Config = config; }
    }

    private static readonly DamageRule[] DamageRules =
    {
        new("Fire_zone_01", "flame_enter_zone_493925125", "flame_exit_zone_493925125", "INTERACTIVE_Flametrap_00001_soft"),
        new("Fire_zone_02", "flame_enter_zone_1349880479", "flame_exit_zone_1349880479", "INTERACTIVE_Flametrap_00001_deadly"),
        new("Luquid_damage_zone_01", "Luquid_enter_zone_3486811782", "Luquid_exit_zone_3486811782", "INTERACTIVE_Lifting_grate_00001_soft"),
        new("Luquid_damage_zone_02", "Luquid_enter_zone_2773347541", "Luquid_exit_zone_2773347541", "INTERACTIVE_Lifting_grate_00001_deadly"),
        new("Steam_damage_zone_01", "steam_enter_zone_115583983", "steam_exit_zone_115583983", "INTERACTIVE_Valve_00001_soft"),
        new("Steam_damage_zone_02", "steam_enter_zone_3704346085", "steam_exit_zone_3704346085", "INTERACTIVE_Valve_00001_deadly"),
    };
    private static readonly string[] ShootIds =
    {
        "guntrap_trigger_1628276106", "guntrap_trigger_2131128863",
        "guntrap_trigger_1446364582", "guntrap_trigger_2505672369",
    };

    internal static void Enable()
    {
        if (_enabled) return;
        try
        {
            foreach (var patch in Patches) patch.Enable();
            _enabled = true;
        }
        catch (Exception ex)
        {
            Shutdown();
            ReportFailure(ex);
        }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        ResetContext();
        foreach (var patch in Patches)
        {
            try { patch.Disable(); }
            catch (Exception ex) { ReportFailure(ex); }
        }
    }

    private static void ResetContext()
    {
        _world = null;
        _game = null;
        _suppress = false;
        _applied = false;
        _attempts = 0;
        _nextAttempt = 0f;
    }

    private static bool ShouldSuppress()
    {
        if (!ArenaSession.IsActive || !_enabled
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated)
        {
            ResetContext();
            return false;
        }
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance;
        if (world == null || !ArenaSession.IsExactActiveRaid(game, world)
            || !string.Equals(world.LocationId, "labyrinth", StringComparison.OrdinalIgnoreCase))
        {
            ResetContext();
            return false;
        }
        if (!ReferenceEquals(_world, world) || !ReferenceEquals(_game, game))
        {
            ResetContext();
            _world = world;
            _game = game;
            _suppress = Settings.DisableLabyrinthSpawnTraps?.Value == true;
        }
        return _suppress;
    }

    private static bool InScene(MonoBehaviour component)
        => component != null && component.gameObject.scene.IsValid()
           && component.gameObject.scene.isLoaded
           && component.gameObject.scene.path == ScenePath;

    private static bool IsDamage(HandlerDamage handler)
        => InScene(handler) && DamageRules.Any(rule => handler.gameObject.name == rule.Name
           && handler._triggerIdStartDamage == rule.Start
           && handler._triggerIdFinishDamage == rule.Finish && handler.ConfigurationId == rule.Config);

    private static bool IsShoot(HandlerShoot handler)
        => InScene(handler) && handler.gameObject.name == "INTERACTIVE_Guntrap_01"
           && ShootIds.Contains(handler._triggerId)
           && handler.ConfigurationId == (handler._triggerId == "guntrap_trigger_1446364582"
               ? "INTERACTIVE_Guntrap_00001_deadly" : "INTERACTIVE_Guntrap_00001_soft");

    private static bool IsLoop(HandlerPlaySoundSequenced handler)
        => InScene(handler) && string.IsNullOrEmpty(handler._playTriggerId)
           && handler._playOnSoundSystemInit && handler._playOnce
           && ((handler._stopTriggerId == "disable_traps_02"
                && (handler.gameObject.name == "Fire_zone_01" || handler.gameObject.name == "Fire_zone_02"))
               || (handler._stopTriggerId == "set_valve_01" && handler.gameObject.name == "Gas_sound_01"));

    private static bool IsEffect(HandlerEffect handler)
        => InScene(handler)
           && ((handler.gameObject.name == "INTERACTIVE_Guntrap_01" && ShootIds.Contains(handler._triggerId))
               || DamageRules.Skip(2).Take(2).Any(rule => handler.gameObject.name == rule.Name
                   && handler._triggerId == rule.Start));

    internal static bool AllowDamageStart(HandlerDamage handler)
        => Allow(() => IsDamage(handler));
    internal static bool AllowShoot(HandlerShoot handler)
        => Allow(() => IsShoot(handler));
    internal static bool AllowLoopStart(HandlerPlaySoundSequenced handler)
        => Allow(() => IsLoop(handler));
    internal static bool AllowEffect(HandlerEffect handler)
        => Allow(() => IsEffect(handler));

    private static bool Allow(Func<bool> matches)
    {
        if (!ArenaSession.IsActive) return true;
        try { return !ShouldSuppress() || !matches(); }
        catch (Exception ex) { ReportFailure(ex); return true; }
    }

    internal static void Tick()
    {
        try
        {
            if (!ShouldSuppress() || _applied || _attempts >= 5 || Time.unscaledTime < _nextAttempt)
                return;
            _attempts++;
            _nextAttempt = Time.unscaledTime + 1f;

            // Resolve and validate ALL scene actions before executing any.
            // Missing/changed registries get five bounded readiness attempts;
            // exact known damage/shot guards already work before the first tick.
            var plan = PreparePlan();
            foreach (var control in plan.Controls) control.OnTrigger();
            foreach (var damage in plan.Damage) damage.OnTriggerFinishDamage(null);
            foreach (var animator in plan.Animators) animator._animatorActions[0].Execute(animator._target);
            foreach (var loop in plan.Loops)
            {
                // Cancel only the pending start/loop coroutine, not native end
                // timers from an earlier manual switch or retry. StopSound
                // alone does not cancel this handle. Read the exact field; do
                // not alter it or manually invoke source-release callbacks:
                // BetterSource.Stop can invoke its fade callback synchronously
                // and recursively release an already-cleared source reference.
                var pendingStart = (Coroutine)PlayCoroutineField.GetValue(loop);
                if (pendingStart != null) loop.StopCoroutine(pendingStart);
                loop.StopSound();
            }
            _applied = true;
            Plugin.ModLogger?.LogInfo("Arena Labyrinth spawn traps disabled: 10 hazards (4 gun, 2 fire, 2 liquid, 2 steam), 3 animations, 4 audio loops; option latched for this raid.");
        }
        catch (Exception ex)
        {
            if (_attempts >= 5)
                Plugin.ModLogger?.LogWarning($"Arena Labyrinth spawn trap setup incomplete after 5 attempts: {ex.Message}. Exact known hazard guards remain active; unknown scene content stays vanilla.");
        }
    }

    private sealed class Plan
    {
        internal HandlerTriggerState[] Controls;
        internal HandlerDamage[] Damage;
        internal HandlerAnimator[] Animators;
        internal HandlerPlaySoundSequenced[] Loops;
    }

    private static Plan PreparePlan()
    {
        if (PlayCoroutineField == null || PlayCoroutineField.DeclaringType != typeof(HandlerPlaySoundSequenced)
            || PlayCoroutineField.FieldType != typeof(Coroutine))
            throw new InvalidOperationException("native sound start-coroutine contract changed");
        if (!(_world.TriggersEmitter is LocalTriggersEmitter))
            throw new InvalidOperationException("expected the local raid trigger emitter");
        var all = LocationScene.GetAll<ITriggerEntity>().OfType<MonoBehaviour>().Where(InScene).ToArray();
        var damage = all.OfType<HandlerDamage>().Where(IsDamage).ToArray();
        var shoot = all.OfType<HandlerShoot>().Where(IsShoot).ToArray();
        var effects = all.OfType<HandlerEffect>().Where(IsEffect).ToArray();
        if (damage.Length != 6 || damage.Select(x => x._triggerIdStartDamage).Distinct().Count() != 6
            || shoot.Length != 4 || shoot.Select(x => x._triggerId).Distinct().Count() != 4
            || effects.Length != 6 || effects.Select(x => x._triggerId).Distinct().Count() != 6)
            throw new InvalidOperationException("ten unique spawn hazards and six associated effect handlers were not found");

        var controls = new[]
        {
            FindControl(all, "disable_traps_01", "INTERACTIVE_Toggle_Trap_Switch", ShootIds),
            FindControl(all, "disable_traps_02", "Interactive_button_12", DamageRules.Take(2).Select(x => x.Start).ToArray()),
            FindControl(all, "disable_traps_04", "INTERACTIVE_Toggle_Trap_Switch", DamageRules.Skip(2).Take(2).Select(x => x.Start).ToArray()),
            FindControl(all, "set_valve_01", "Valve_PlaceHolder", DamageRules.Skip(4).Select(x => x.Start).ToArray()),
        };
        var animators = new[]
        {
            FindAnimator(all, "Fire_zone_Animaton", "Fire_zone_switch_01", "isGoing", false),
            FindAnimator(all, "Valve_Animation_Sync", "set_valve_01", "HasValve", true),
            FindAnimator(all, "INTERACTIVE_Lifting_grate_01", "LiftGate_843566315", "IsClosed", true),
        };
        var loops = all.OfType<HandlerPlaySoundSequenced>().Where(IsLoop).ToArray();
        if (loops.Length != 4 || loops.Count(x => x.gameObject.name == "Fire_zone_01") != 1
            || loops.Count(x => x.gameObject.name == "Fire_zone_02") != 1
            || loops.Count(x => x.gameObject.name == "Gas_sound_01") != 2)
            throw new InvalidOperationException("four known continuous trap sounds were not found");
        return new Plan { Controls = controls, Damage = damage, Animators = animators, Loops = loops };
    }

    private static HandlerTriggerState FindControl(MonoBehaviour[] all, string id, string name, string[] targets)
    {
        var matches = all.OfType<HandlerTriggerState>().Where(x => x._triggerId == id).ToArray();
        if (matches.Length != 1 || matches[0].gameObject.name != name || matches[0]._targetState
            || matches[0]._controlledTriggerId == null
            || !matches[0]._controlledTriggerId.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(targets.OrderBy(x => x, StringComparer.Ordinal)))
            throw new InvalidOperationException($"disable-state contract changed for {id}");
        return matches[0];
    }

    private static HandlerAnimator FindAnimator(MonoBehaviour[] all, string name, string id, string parameter, bool value)
    {
        var matches = all.OfType<HandlerAnimator>().Where(x => x.gameObject.name == name).ToArray();
        if (matches.Length != 1 || matches[0]._target == null
            || matches[0]._target.gameObject != matches[0].gameObject
            || matches[0]._animatorActions == null || matches[0]._animatorActions.Length != 1)
            throw new InvalidOperationException($"animator ownership changed for {name}");
        var action = matches[0]._animatorActions[0];
        if (action.TriggerId != id || action.BoolAction.Name != parameter || action.BoolAction.Value != value
            || !string.IsNullOrEmpty(action.IntAction.Name) || !string.IsNullOrEmpty(action.FloatAction.Name)
            || !string.IsNullOrEmpty(action.AnimationTriggerAction.TriggerName))
            throw new InvalidOperationException($"animator action changed for {name}");
        return matches[0];
    }

    private static void ReportFailure(Exception ex)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        Plugin.ModLogger?.LogWarning($"Arena Labyrinth spawn trap guard failed open: {ex.Message}");
    }

    private sealed class DamageStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(HandlerDamage), nameof(HandlerDamage.OnTriggerStartDamage), new[] { typeof(TriggerEvent) });
        [PatchPrefix] private static bool Prefix(HandlerDamage __instance) => AllowDamageStart(__instance);
    }
    private sealed class ShootPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(HandlerShoot), nameof(HandlerShoot.OnShootEvent), new[] { typeof(TriggerEvent) });
        [PatchPrefix] private static bool Prefix(HandlerShoot __instance) => AllowShoot(__instance);
    }
    private sealed class LoopStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(HandlerPlaySoundSequenced), nameof(HandlerPlaySoundSequenced.PlaySound), Type.EmptyTypes);
        [PatchPrefix] private static bool Prefix(HandlerPlaySoundSequenced __instance) => AllowLoopStart(__instance);
    }
    private sealed class EffectPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(HandlerEffect), nameof(HandlerEffect.HandleEvent), new[] { typeof(TriggerEvent) });
        [PatchPrefix] private static bool Prefix(HandlerEffect __instance) => AllowEffect(__instance);
    }
}
