using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>The inventory bar must display Arena's per-body limits, not rebuild vanilla globals.</summary>
internal static class ArenaEncumbranceBarCompat
{
    private const string Guid = "com.mpstark.PlayerEncumbranceBar";
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".EncumbranceBar");
    private static object _instance;
    private static MethodInfo _target;
    private static FieldInfo _baseLimits, _walkLimits, _health, _bar;
    private static string _failure;

    internal static string GetRejectionReason()
    {
        // Core has already checked the optional plugin identity and shared body binding.
        if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info)) return null;
        if (info?.Instance == null) return "Player Encumbrance Bar has not finished loading.";
        if (ReferenceEquals(_instance, info.Instance)) return _failure;
        Shutdown();
        _instance = info.Instance;
        try
        {
            var type = info.Instance.GetType().Assembly.GetType("PlayerEncumbranceBar.PlayerEncumbranceBarComponent", true);
            _target = AccessTools.DeclaredMethod(type, "UpdateWeightLimits", new[] { typeof(bool) });
            _baseLimits = Field(type, "_baseOverweightLimits", typeof(Vector2), false);
            _walkLimits = Field(type, "_walkOverweightLimits", typeof(Vector2), false);
            _health = Field(type, "_healthController", typeof(IHealthController), true);
            _bar = Field(info.Instance.GetType(), "PlayerEncumbranceBar", type, false);
            if (_target == null || _target.IsStatic || _target.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "UpdateWeightLimits");
            Patches.Patch(_target, transpiler: new HarmonyMethod(typeof(ArenaEncumbranceBarCompat), nameof(Rewrite)));
        }
        catch (Exception ex)
        {
            RemovePatch();
            _failure = "Player Encumbrance Bar Arena weight display is unavailable: " + ex.GetBaseException().Message;
        }
        return _failure;
    }

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        int baseIndex = -1, walkIndex = -1;
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stfld) continue;
            if (Equals(code[i].operand, _baseLimits))
            { if (baseIndex >= 0) throw new InvalidOperationException("Ambiguous base weight limits"); baseIndex = i; }
            if (Equals(code[i].operand, _walkLimits))
            { if (walkIndex >= 0) throw new InvalidOperationException("Ambiguous walk weight limits"); walkIndex = i; }
        }
        if (baseIndex < 0 || walkIndex <= baseIndex) throw new MissingMethodException("Expected ordered weight-limit assignments were not found");
        // Replace only the computed display values, before the foreign code positions
        // its labels/ticks and chooses colors. Its normal rendering and events remain.
        code.InsertRange(walkIndex + 1, new[] {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ArenaEncumbranceBarCompat), nameof(ApplyLimits))) });
        return code;
    }

    private static void ApplyLimits(object bar)
    {
        if (!ArenaSession.IsActive || !ReferenceEquals(_bar.GetValue(_instance), bar)) return;
        var world = Singleton<GameWorld>.Instance;
        if (world == null || Singleton<AbstractGame>.Instance is not LocalGame game
            || !ArenaSession.IsExactActiveRaid(game, world)) return;
        var body = world.MainPlayer;
        if (body == null || !body.IsYourPlayer || body.IsAI || !ReferenceEquals(game.LocalPlayer, body)
            || !ReferenceEquals(body.GameWorld, world) || !ReferenceEquals(body.HealthController, _health.GetValue(null))
            || body.Physical == null) return;
        _baseLimits.SetValue(bar, body.Physical.BaseOverweightLimits);
        _walkLimits.SetValue(bar, body.Physical.WalkOverweightLimits);
    }

    internal static void Shutdown()
    {
        RemovePatch();
        _instance = null; _baseLimits = null; _walkLimits = null; _health = null; _bar = null; _failure = null;
    }
    private static void RemovePatch()
    {
        if (_target != null) Patches.Unpatch(_target, HarmonyPatchType.Transpiler, Patches.Id);
        _target = null;
    }
    private static FieldInfo Field(Type type, string name, Type expected, bool isStatic)
    {
        var field = AccessTools.DeclaredField(type, name);
        if (field == null || field.FieldType != expected || field.IsStatic != isStatic)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
}
