using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// VisualAssist calculates its arc only for ordinary grenade hands, but its throw prefix also intercepts quick
/// throws. In an exact Arena raid, leave unsupported hands to EFT and refresh the ordinary throw's prediction
/// at release, rather than letting a cache from a different body, item or position choose the grenade's origin.
/// Core owns component lifetime; this adapter owns only Arena's throw semantics. Ordinary raids are untouched.
/// </summary>
internal static class ArenaVisualAssistCompat
{
    internal const string PluginGuid = "com.janky.visualassist";
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".VisualAssist");
    private static object _plugin;
    private static MethodInfo _prefix, _calculate;
    private static PropertyInfo _singleton;
    private static FieldInfo _owner, _prediction, _position, _force, _enabled;
    private static string _failure;
    private static bool _throwWarning;

    internal static string GetRejectionReason()
    {
        // Core admission runs first and checks the optional plugin's identity and body lifecycle contracts.
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        { Shutdown(); return null; }
        if (info.Instance == null) return "VisualAssist has not finished loading.";
        if (ReferenceEquals(_plugin, info.Instance))
        {
            if (_failure != null) return _failure;
            return HasHook() ? null : "VisualAssist Arena throw protection is missing.";
        }
        Shutdown();
        _plugin = info.Instance;
        try
        {
            var assembly = info.Instance.GetType().Assembly;
            var arc = assembly.GetType("VisualAssist.GrenadeArc", true);
            var prediction = assembly.GetType("VisualAssist.GrenadeThrow", true);
            _prefix = Method(assembly.GetType("VisualAssist.GrenadeThrowPrefixPatch", true), "Prefix", true,
                typeof(bool), typeof(Player.BaseGrenadeHandsController), typeof(Player), typeof(Transform), typeof(bool), typeof(float));
            _calculate = Method(arc, "CalculateGrenadeThrow", false, prediction, typeof(bool));
            _owner = Field(arc, "localPlayer", typeof(Player));
            _prediction = Field(arc, "GrenadeThrow", prediction);
            _position = Field(prediction, "ThrowPosition", typeof(Vector3));
            _force = Field(prediction, "ThrowForce", typeof(Vector3));
            _enabled = Field(info.Instance.GetType(), "GrenadeArcEnabled", typeof(ConfigEntry<bool>), true);
            _singleton = typeof(Singleton<>).MakeGenericType(arc).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMemberException("VisualAssist singleton");
            Patches.Patch(_prefix, prefix: new HarmonyMethod(typeof(ArenaVisualAssistCompat), nameof(BeforeForeignThrow))
                { priority = Priority.First });
            if (!HasHook()) throw new MissingMethodException("VisualAssist throw protection");
        }
        catch (Exception ex)
        {
            RemoveHook();
            _failure = "VisualAssist cannot be integrated with Arena: " + ex.GetBaseException().Message + ".";
        }
        return _failure;
    }

    private static MethodInfo Method(Type type, string name, bool isStatic, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, args, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static FieldInfo Field(Type type, string name, Type valueType, bool isStatic = false)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance));
        if (field == null || field.FieldType != valueType || field.IsInitOnly)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static bool HasHook() => _prefix != null && Harmony.GetPatchInfo(_prefix)?.Prefixes.Count(p =>
        p.owner == Patches.Id && p.PatchMethod == AccessTools.DeclaredMethod(typeof(ArenaVisualAssistCompat), nameof(BeforeForeignThrow))) == 1;

    internal static void Shutdown()
    {
        RemoveHook();
        _plugin = null;
        _calculate = null;
        _singleton = null;
        _owner = _prediction = _position = _force = _enabled = null;
        _failure = null;
        _throwWarning = false;
    }

    private static void RemoveHook()
    {
        // Admission is polled by the menu: never walk all process patches when the mod is absent.
        if (_prefix != null) Patches.Unpatch(_prefix, HarmonyPatchType.Prefix, Patches.Id);
        _prefix = null;
    }

    private static bool BeforeForeignThrow(Player.BaseGrenadeHandsController __0, Player __1, bool __3, ref bool __result)
    {
        if (!ArenaSession.IsActive) return true;
        // This is a prefix OF a prefix: true as the foreign result means EFT's original throw still runs.
        __result = true;
        try
        {
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            if (__1 == null || !__1.IsYourPlayer || __1.IsAI || __1.HealthController?.IsAlive != true
                || world == null || game == null || !ArenaSession.IsExactActiveRaid(game, world)
                || !ReferenceEquals(world.MainPlayer, __1) || !ReferenceEquals(game.LocalPlayer, __1)
                || !ReferenceEquals(__1.HandsController, __0)
                || __0 is not Player.GrenadeHandsController hands
                || hands.CurrentOperation is not Player.GrenadeHandsController.HighThrowOperation
                || __3 != (hands.CurrentOperation is Player.GrenadeHandsController.LowThrowOperation)) return false;
            if ((_enabled.GetValue(null) as ConfigEntry<bool>)?.Value != true) return false;
            var arc = _singleton.GetValue(null) as MonoBehaviour;
            if (arc == null || !arc.isActiveAndEnabled || !ReferenceEquals(_owner.GetValue(arc), __1)) return false;
            // Only on actual release, never per frame. Calculate through the mod's own formula so its preview and
            // real throw retain the same behavior, including its existing smoothed velocity, without stale origins.
            var prediction = _calculate.Invoke(arc, new object[] { __3 });
            var position = (Vector3)_position.GetValue(prediction);
            var force = (Vector3)_force.GetValue(prediction);
            if (!Finite(position) || !Finite(force) || force.sqrMagnitude <= 0f) return false;
            _prediction.SetValue(arc, prediction);
            return true;
        }
        catch (Exception ex)
        {
            if (!_throwWarning)
            {
                _throwWarning = true;
                Plugin.ModLogger.LogDebug("VisualAssist prediction unavailable; Arena leaves the throw to EFT: " + ex.GetBaseException().Message);
            }
            return false;
        }
    }

    private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
