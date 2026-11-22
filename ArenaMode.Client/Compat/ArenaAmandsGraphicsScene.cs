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

/// <summary>
/// Repairs Amand's startup lookup of the level settings in exact Arena raids, on every Arena map, before Core can
/// capture the first body's camera.
/// <para>
/// Amand's Graphics 1.8.0 finds the object that carries the scene's <c>LevelSettings</c> by NAME: a table keyed by the
/// active scene name, <c>!settings</c> for unknown scenes, then <c>GameObject.Find(..).GetComponent</c> without a null
/// check. A scene the table does not know (Labyrinth, every ArenaMapPort map) names no existing object, so Amand
/// throws, its static <c>levelSettings</c> stays null and Core refuses the kit swap and every respawn. The transpiler
/// below only replaces the stack argument of that one <c>Find</c> and routes it through
/// <see cref="FindLevelObject"/>; Amand's own <c>GetComponent</c>, assignment and defaults stay native.
/// </para>
/// <para>
/// Decisions, in the order of <see cref="FindLevelObject"/>:
/// (1) Amand's own lookup wins whenever it yields a <c>LevelSettings</c>, and nothing of Arena is consulted first. A map
/// Amand resolves by scene name (only Factory was run natively, and its report does not name the day or night variant;
/// Laboratory and Customs are expected, not proven, to resolve that way) therefore behaves exactly as without Arena, and an ordinary raid is untouched. Which way a
/// map goes does not matter for correctness: the substitute only ever fills a lookup that came back empty. The earlier version tested for the literal name <c>!settings</c> and for
/// Labyrinth only; neither was a safety argument (the 2026-11-08 run simply covered Factory and Labyrinth), and a name
/// filter would leave a table entry whose object name drifted as a latent NullReferenceException. An existing object
/// WITHOUT a <c>LevelSettings</c> is treated like a missing one: the alternative is a silent null that Core refuses anyway.
/// (2) The substitute is <c>Singleton&lt;LevelSettings&gt;.Instance</c>, the object EFT itself drives in every started raid
/// (<c>TarkovApplication</c> calls <c>OnPostLoadingScene</c> on it), never a name search and never
/// <c>FindObjectOfType</c> (no order guarantee, could return a hideout leftover). It must be live, enabled, in a loaded
/// scene and the very component <c>GetComponent</c> will return from its object.
/// (3) No write into Amand's scene table (a stand-in entry written into that static, persistent table would outlive
/// the raid and reach every later one) and no stand-in object: when no usable <c>LevelSettings</c> exists the original result is returned, which is what Amand gets
/// today on any map, and the Debug line says why.
/// (4) The Fika, camera, world, map and exact-raid gates are unchanged. The map gate is the Arena catalogue; the exact-raid
/// or loading-arm identity already implies an Arena map, this keeps the statement in one place.
/// (5) Everything after the name lookup, the native branch of (1) included, is wrapped so that this code is never the
/// thrower: a failure returns the original object. The native branch only reads a component and writes a log line, but
/// a destroyed object can throw on either, and a throw there would take Graphics' activation down.
/// </para>
/// <para>
/// Known consequences, deliberately not handled in code. Amand treats an unknown scene like the hideout ("default"
/// branch): with its SkyColor option on it writes dim sky, equator and ground colours into the same
/// <c>LevelSettings</c> fields that ArenaMapPort's lighting repairs write, so ambient light on the ported maps can differ
/// from MapPort's own look while Graphics is on. Core's lease requires the same <c>LevelSettings</c> object at every
/// re-activation; if the native lookup flipped between two activations (a named object appearing later) the lease would
/// fault, which fails safe. The Debug lines name the branch taken ("native" or "Arena") so a log shows such a flip.
/// The seven ported maps are covered by code and offline checks only; they have not been run natively with Graphics.
/// </para>
/// </summary>
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
        try
        {
            // Amand's own lookup wins whenever it yields a LevelSettings: no Arena state decides anything, so maps it
            // resolves by scene name and every ordinary raid behave exactly as without Arena (the log only reads state).
            if (original != null && original.GetComponent<LevelSettings>() != null)
            {
                if (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) NoteNativeInRaid(original);
                return original;
            }
            return FindArenaLevelObject(original, cameraObject);
        }
        catch (Exception ex)
        {
            // Never the thrower: a destroyed object, camera or a vanished singleton must end as Amand's own result.
            Plugin.ModLogger.LogDebug("Amand's Graphics level lookup left to Graphics after " + ex.GetType().Name + ".");
            return original;
        }
    }

    /// <summary>
    /// Names the native branch, but only inside a started local game on an Arena map. Arena being active or armed is
    /// not a raid by itself: a hideout activation while the next raid is armed would otherwise log "native branch" and
    /// mislead the reading of a native/Arena flip. The line is diagnostics only and decides nothing.
    /// </summary>
    private static void NoteNativeInRaid(GameObject original)
    {
        var world = Singleton<GameWorld>.Instance;
        if (world != null && Singleton<AbstractGame>.Instance is LocalGame game && ReferenceEquals(game.GameWorld, world) && ArenaMapCatalog.IsSupportedMap(world.LocationId))
            NoteBranch("native", original);
    }

    private static GameObject FindArenaLevelObject(GameObject original, GameObject cameraObject)
    {
        if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid) return original;
        if (FikaCompatCheck.IsFikaLoaded || cameraObject == null || !ReferenceEquals(cameraObject, CameraManager.Instance?.Camera?.gameObject)) return original;
        var world = Singleton<GameWorld>.Instance;
        if (world == null || !(Singleton<AbstractGame>.Instance is LocalGame game) || !ReferenceEquals(game.GameWorld, world)
            || !ArenaMapCatalog.IsSupportedMap(world.LocationId)) return original;
        // Prism can activate before OnGameStarted publishes IsActive. Only accept the actual native arm on
        // this local game's settings in that window. An ordinary load, stale arm or detached camera falls through.
        if (ArenaSession.IsActive ? !ArenaSession.IsExactActiveRaid(game, world) : !MatchesLoadingArm(game, world)) return original;
        var level = Singleton<LevelSettings>.Instance;
        if (level == null || !level.isActiveAndEnabled || !level.gameObject.scene.isLoaded
            || !ReferenceEquals(level.gameObject.GetComponent<LevelSettings>(), level))
        {
            Plugin.ModLogger.LogDebug("Amand's Graphics found no usable live LevelSettings on " + world.LocationId + "; Graphics keeps its own lookup result.");
            return original;
        }
        NoteBranch("Arena", level.gameObject, world.LocationId);
        return level.gameObject;
    }

    private static void NoteBranch(string branch, GameObject level, string map = null)
        => Plugin.ModLogger.LogDebug(map == null
            ? "Amand's Graphics keeps its own LevelSettings lookup (" + branch + " branch): '" + level.name + "'."
            : "Amand's Graphics uses the live LevelSettings '" + level.name + "' of " + map + " for the Arena FPS camera (" + branch + " branch).");

    private static bool MatchesLoadingArm(LocalGame game, GameWorld world)
    {
        var token = ArenaServerClient.PendingArmToken;
        if (!Protocol.IsLifecycleRequestId(token) || !string.Equals(ArenaSession.ArmedMap, world.LocationId, StringComparison.OrdinalIgnoreCase)) return false;
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        return settings != null && string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)
            && (Protocol.IsNativeStartIdentity(settings.serverId, token) || Protocol.HasNativeEndIdentity(settings.serverId, token));
    }
}
