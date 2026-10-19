using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Diagnostics;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Remove only immortality/Second Chance from exact Arena local bodies.</summary>
internal static class ArenaDeminvincibilityCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".DeminvincibilityCompat");
    private static readonly ArenaDeminvincibilityScope Scope = new();
    private static MethodInfo[] _foreign;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _attempted, _faulted, _newerWarned;
    private static string _failure, _lastWarning;
    private static int _reported;

    internal static void Initialize() { if (_initialized) return; _initialized = true; Refresh(); }
    internal static string GetRejectionReason()
    { if (!_initialized) Initialize(); else Refresh(); return _failure; }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaDeminvincibilityContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _faulted = false; return; }
        if (_faulted) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Deminvincibility has not finished loading");
            if (_foreign != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaDeminvincibilityContract.Normalize(info.Metadata?.Version))
            {
                ArenaDeminvincibilityContract.VerifyNativeBindings(_foreign);
                if (!OwnsPatches()) throw new InvalidOperationException("Deminvincibility isolation hook removed");
                return;
            }
            ClearBinding();
            var methods = ArenaDeminvincibilityContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out var version);
            _attempted = true;
            Patches.Patch(methods[0], prefix: Hook(nameof(BeforeKill)));
            Patches.Patch(methods[1], prefix: Hook(nameof(BeforeDestroy)));
            Patches.Patch(methods[2], transpiler: Hook(nameof(IsolateDamageClamp)));
            Patches.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer)), postfix: Hook(nameof(AfterRegister)));
            Patches.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)), prefix: Hook(nameof(BeforeWorldStarted)));
            _foreign = methods; _plugin = info.Instance; _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("Deminvincibility isolation hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Deminvincibility: exact local-body immortality and Second Chance guards ready; other health preferences retained.");
            if (version > ArenaDeminvincibilityContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                Plugin.ModLogger.LogWarning("Arena Deminvincibility " + version + " is newer than reviewed 2.2.0; its health APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }

    private static HarmonyMethod Hook(string name) => new(typeof(ArenaDeminvincibilityCompat), name) { priority = Priority.First };
    private static bool Receipt(MethodBase method, string name, int kind = 0)
    {
        var info = Harmony.GetPatchInfo(method);
        var list = kind == 2 ? info?.Transpilers : kind == 1 ? info?.Postfixes : info?.Prefixes;
        return list?.Count(p => p.owner == Patches.Id && p.PatchMethod == AccessTools.Method(typeof(ArenaDeminvincibilityCompat), name)) == 1;
    }
    private static bool OwnsPatches() => _foreign != null
        && Receipt(_foreign[0], nameof(BeforeKill)) && Receipt(_foreign[1], nameof(BeforeDestroy))
        && Receipt(_foreign[2], nameof(IsolateDamageClamp), 2)
        && Receipt(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer)), nameof(AfterRegister), 1)
        && Receipt(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted)), nameof(BeforeWorldStarted));

    // Prefixes of the foreign bool prefixes, not native Kill/DestroyBodyPart.
    private static bool BeforeKill(ActiveHealthController __0, Player __2, ref bool __result)
    { if (!Owns(__0, __2, 0)) return true; __result = true; return false; }
    private static bool BeforeDestroy(ActiveHealthController __0, Player __1, ref bool __result)
    { if (!Owns(__0, __1, 1)) return true; __result = true; return false; }

    private static IEnumerable<CodeInstruction> IsolateDamageClamp(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var plugin = __originalMethod.DeclaringType.Assembly.GetType("Deminvincibility.DeminvicibilityPlugin", true);
        int index = ArenaDeminvincibilityContract.FindKeepRead(code, plugin);
        // Preserve the actual ConfigEntry and its getter for ordinary bodies. Only
        // the clamp reads false in Arena; MedicineBool and damage scaling run.
        code.Insert(index++, new CodeInstruction(OpCodes.Ldarg_0));
        code.Insert(index++, new CodeInstruction(OpCodes.Ldarg_1));
        code[index].opcode = OpCodes.Call;
        code[index].operand = AccessTools.Method(typeof(ArenaDeminvincibilityCompat), nameof(KeepHealthValue));
        return code;
    }
    private static bool KeepHealthValue(ConfigEntry<bool> entry, ActiveHealthController controller, Player body)
        => !Owns(controller, body, 2) && entry.Value;

    private static bool Owns(ActiveHealthController controller, Player body, int kind, GameWorld registering = null)
    {
        if (_foreign == null || ReferenceEquals(controller, null) || body is not LocalPlayer || !body.IsYourPlayer) return false;
        try
        {
            // Retired leases are evaluated before consulting current singletons.
            if (Scope.Owns(controller, body, controller.Player, body.ActiveHealthController,
                body.GameWorld, null, true, false, false)) { Report(kind); return true; }
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
            bool initial = game != null && game.Status == GameStatus.Running && ReferenceEquals(game.LocalPlayer, null)
                && body.Profile != null && ReferenceEquals(body.Profile, game.Profile);
            bool exactWorld = !ReferenceEquals(world, null) && !ReferenceEquals(game, null)
                && ReferenceEquals(game.GameWorld, world) && (initial || (Singleton<IBotGame>.Instantiated
                    && ReferenceEquals(Singleton<IBotGame>.Instance, game)));
            var settings = exactWorld ? LocalGameAccessor.GetLocalRaidSettings(game) : null;
            bool exactRaid = ArenaDeminvincibilityScope.OwnsRaid(exactWorld,
                game != null && (game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started),
                settings?.playerSide == ESideType.Pmc, world?.LocationId, settings?.location,
                exactWorld && ArenaSession.IsExactActiveRaid(game, world), ArenaSession.ArmedForNextRaid,
                ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
            bool current = ReferenceEquals(game?.LocalPlayer, body) || ReferenceEquals(world?.MainPlayer, body)
                || (ReferenceEquals(registering, world) && registering != null && registering.RegisteredPlayers.Contains(body));
            bool owned = Scope.Owns(controller, body, controller.Player, body.ActiveHealthController,
                body.GameWorld, world, true, exactRaid, current);
            if (owned) Report(kind);
            return owned;
        }
        catch (Exception ex) { _faulted = true; Reject(ex); return false; }
    }
    private static void Report(int kind)
    {
        if (kind < 0 || !ArenaLog.DeveloperEnabled || (_reported & (1 << kind)) != 0) return;
        _reported |= 1 << kind;
        ArenaLog.Developer("Deminvincibility: Arena local " + new[] { "death", "limb destruction", "lethal damage" }[kind]
            + " uses native/Arena rules; unrelated health preferences remain active.");
    }
    private static void AfterRegister(GameWorld __instance, IPlayer iPlayer)
    {
        if (iPlayer is Player body && ReferenceEquals(body.GameWorld, __instance))
            Owns(body.ActiveHealthController, body, -1, __instance);
    }
    private static void BeforeWorldStarted(GameWorld __instance)
    {
        if (__instance?.MainPlayer is Player body && ReferenceEquals(body.GameWorld, __instance))
            Owns(body.ActiveHealthController, body, -1, __instance);
    }
    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate Deminvincibility: " + ex.GetBaseException().Message
            + ". Update or remove Deminvincibility, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _foreign = null; _plugin = null; _version = null; Scope.Clear(); _reported = 0;
        if (!_attempted) return;
        _attempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown()
    { ClearBinding(); _initialized = _faulted = false; _failure = _lastWarning = null; }
}
