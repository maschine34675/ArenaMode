using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Redeploy.Core.Compat;
using Redeploy.Core.GameAccess;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>Repairs Amand's startup lookup in Arena Labyrinth before Core can capture the first body's camera.</summary>
internal static class ArenaAmandsGraphicsScene
{
    private const string Guid = "com.Amanda.Graphics";
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".AmandsGraphicsScene");
    private static object _instance;
    private static MethodInfo _target;
    private static string _rejection;

    internal static string GetRejectionReason()
    {
        // Core admission already verified this GUID, component, assembly and activation contract.
        if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info)) return null;
        if (info.Instance == null) return "Amand's Graphics has not finished loading.";
        if (ReferenceEquals(_instance, info.Instance)) return _rejection;
        Shutdown();
        _instance = info.Instance;
        try
        {
            var type = info.Instance.GetType().Assembly.GetType("AmandsGraphics.AmandsGraphicsClass", true);
            _target = AccessTools.DeclaredMethod(type, "ActivateAmandsGraphics", new[] { typeof(GameObject), typeof(PrismEffects) })
                ?? throw new MissingMethodException(type.FullName, "ActivateAmandsGraphics");
            Patches.Patch(_target, transpiler: new HarmonyMethod(typeof(ArenaAmandsGraphicsScene), nameof(Rewrite)));
        }
        catch (Exception ex)
        {
            RemovePatch();
            _rejection = "Amand's Graphics Arena scene integration is unavailable: " + ex.GetBaseException().Message + ".";
        }
        return _rejection;
    }

    internal static void Shutdown()
    {
        RemovePatch();
        _instance = null;
        _rejection = null;
    }

    private static void RemovePatch()
    {
        if (_target != null) Patches.Unpatch(_target, HarmonyPatchType.Transpiler, Patches.Id);
        _target = null;
    }

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var find = AccessTools.Method(typeof(GameObject), nameof(GameObject.Find), new[] { typeof(string) });
        int match = -1;
        for (int i = 0; i + 2 < code.Count; i++)
        {
            if (!code[i].Calls(find) || code[i + 1].opcode != OpCodes.Callvirt
                || !(code[i + 1].operand is MethodInfo getter) || getter.DeclaringType != typeof(GameObject)
                || getter.Name != nameof(GameObject.GetComponent) || !getter.IsGenericMethod
                || getter.ReturnType != typeof(LevelSettings) || getter.GetParameters().Length != 0
                || code[i + 2].opcode != OpCodes.Stsfld || !(code[i + 2].operand is FieldInfo field)
                || field.Name != "levelSettings" || field.FieldType != typeof(LevelSettings)) continue;
            if (match >= 0) throw new InvalidOperationException("Ambiguous LevelSettings lookup");
            match = i;
        }
        if (match < 0) throw new MissingMethodException("Expected LevelSettings lookup was not found");
        // Keep the original find site's labels and block markers. The native GetComponent and every
        // subsequent assignment, including Amand's choice of the default graphics profile, stay intact.
        code[match].opcode = OpCodes.Ldarg_1;
        code[match].operand = null;
        code.Insert(match + 1, new CodeInstruction(OpCodes.Call,
            AccessTools.DeclaredMethod(typeof(ArenaAmandsGraphicsScene), nameof(FindLevelObject))));
        return code;
    }

    private static GameObject FindLevelObject(string name, GameObject cameraObject)
    {
        var original = GameObject.Find(name);
        if (original != null || name != "!settings" || (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid)) return original;
        if (FikaCompatCheck.IsFikaLoaded || cameraObject == null || !ReferenceEquals(cameraObject, CameraManager.Instance?.Camera?.gameObject)) return original;
        var world = Singleton<GameWorld>.Instance;
        if (world == null || !(Singleton<AbstractGame>.Instance is LocalGame game) || !ReferenceEquals(game.GameWorld, world)
            || !string.Equals(world.LocationId, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase)) return original;
        // Prism can activate before OnGameStarted publishes IsActive. Only accept the actual native arm on
        // this local game's settings in that window. An ordinary load, stale arm or detached camera falls through.
        if (ArenaSession.IsActive ? !ArenaSession.IsExactActiveRaid(game, world) : !MatchesLoadingArm(game, world)) return original;
        var level = Singleton<LevelSettings>.Instance;
        if (level == null || !level.isActiveAndEnabled || !level.gameObject.scene.isLoaded) return original;
        Plugin.ModLogger.LogDebug("Amand's Graphics uses Labyrinth's live LevelSettings for the Arena FPS camera.");
        return level.gameObject;
    }

    private static bool MatchesLoadingArm(LocalGame game, GameWorld world)
    {
        var token = ArenaServerClient.PendingArmToken;
        if (!Protocol.IsLifecycleRequestId(token) || !string.Equals(ArenaSession.ArmedMap, world.LocationId, StringComparison.OrdinalIgnoreCase)) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        return settings != null && string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)
            && (Protocol.IsNativeStartIdentity(settings.serverId, token) || Protocol.HasNativeEndIdentity(settings.serverId, token));
    }
}
