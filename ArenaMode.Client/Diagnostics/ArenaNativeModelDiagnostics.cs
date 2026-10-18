using ArenaMode.Session;
using Comfort.Common;
using Diz.Skinning;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace ArenaMode.Diagnostics;

/// <summary>Read-only, fault-only context for native model alarms. Never repairs,
/// hides an original log, suppresses an exception, or scans the scene.</summary>
internal static class ArenaNativeModelDiagnostics
{
    private static Harmony _harmony;
    private static FieldInfo _bodyActive, _bodyProfile, _muzzleManager, _weaponData;
    private static readonly ReportBudget Budget = new();
    private static bool _probeFailed;

    internal static void Enable()
    {
        if (_harmony != null) return;
        var harmony = new Harmony(Plugin.PluginGuid + ".nativemodeldiagnostics");
        try
        {
            _bodyActive = RequiredField(typeof(PlayerBody), "_active");
            _bodyProfile = RequiredField(typeof(PlayerBody), "_playerProfileID");
            _muzzleManager = RequiredField(typeof(FirearmsEffects), "_muzzleManager");
            _weaponData = RequiredField(typeof(WeaponPrefab), "_weaponData");
            Bind(harmony, typeof(PlayerBody), "OnDestroy", Type.EmptyTypes, nameof(BodyDestroyPrefix));
            Bind(harmony, typeof(FirearmsEffects), "Init", new[] { typeof(Transform) }, nameof(EffectsInitPostfix), postfix: true);
            Bind(harmony, typeof(WeaponPrefab), "Parent", new[] { typeof(IPlayer) }, nameof(WeaponParentFinalizer), finalizer: true);
            Bind(harmony, typeof(Player.FirearmController.UtilityOperation), "Update", new[] { typeof(float) }, nameof(UtilityTranspiler), transpiler: true);
            Bind(harmony, typeof(Player.FirearmController.LauncherUtilityOperation), "Update", new[] { typeof(float) }, nameof(UtilityTranspiler), transpiler: true);
            Bind(harmony, typeof(CustomItem), "Init", new[] { typeof(PlayerBody) }, nameof(CustomItemPrefix));
            Bind(harmony, typeof(CustomItemSkin), "Init", new[] { typeof(Skeleton), typeof(PlayerBody) }, nameof(CustomSkinPrefix));
            _harmony = harmony;
            _probeFailed = false;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchSelf(); } catch { }
            Plugin.ModLogger?.LogWarning("Arena native model diagnostics unavailable; native behavior unchanged: " + ex.GetBaseException().Message);
        }
    }

    internal static void Shutdown()
    {
        var harmony = _harmony;
        _harmony = null;
        Budget.Clear();
        try { harmony?.UnpatchSelf(); }
        catch (Exception ex) { Plugin.ModLogger?.LogWarning("Arena native model diagnostics shutdown: " + ex.GetBaseException().Message); }
    }

    private static FieldInfo RequiredField(Type type, string name)
        => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);

    private static void Bind(Harmony harmony, Type type, string method, Type[] arguments, string callback,
        bool postfix = false, bool finalizer = false, bool transpiler = false)
    {
        var target = AccessTools.DeclaredMethod(type, method, arguments)
            ?? throw new MissingMethodException(type.FullName, method);
        var hook = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ArenaNativeModelDiagnostics), callback))
            { priority = Priority.Last };
        harmony.Patch(target, prefix: !postfix && !finalizer && !transpiler ? hook : null,
            postfix: postfix ? hook : null, finalizer: finalizer ? hook : null,
            transpiler: transpiler ? hook : null, ilmanipulator: null);
    }

    private static void BodyDestroyPrefix(PlayerBody __instance)
    {
        if (!TryContext(out _, out _, out _)) return;
        try
        {
            // Inspect BEFORE native OnDestroy, whose own Error is still emitted.
            if (!ReferenceEquals(__instance, null) && (bool)_bodyActive.GetValue(__instance))
                Report("body-undisposed", __instance, __instance, null, null, () => "active=true; destroyStack=" + Stack());
        }
        catch (Exception ex) { ProbeFailure(ex); }
    }

    private static void EffectsInitPostfix(FirearmsEffects __instance)
    {
        if (!TryContext(out _, out _, out _)) return;
        try
        {
            var muzzle = __instance != null ? _muzzleManager.GetValue(__instance) as MuzzleManager : null;
            if (muzzle != null && muzzle.JetMaterial == null)
                Report("jet-material-missing", __instance, null, null, null, () => "muzzle=" + ObjectInfo(muzzle));
        }
        catch (Exception ex) { ProbeFailure(ex); }
    }

    private static Exception WeaponParentFinalizer(WeaponPrefab __instance, IPlayer player, Exception __exception)
    {
        if (__exception == null || !TryContext(out _, out _, out _)) return __exception;
        try
        {
            Report("weapon-parent-exception", __instance, null, player as Player,
                __instance != null ? _weaponData.GetValue(__instance) as Weapon : null,
                () => "requestedPlayer=" + (player == null ? "none-menu-or-unowned" : player.GetType().Name)
                + "; exception=" + __exception);
        }
        catch (Exception ex) { ProbeFailure(ex); }
        return __exception; // The exact exception is neither replaced nor swallowed.
    }

    private static IEnumerable<CodeInstruction> UtilityTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        bool Marker(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldstr
            && (Equals(instruction.operand, "UtilityOperationEvent not found on ")
                || Equals(instruction.operand, "UtilityOperationEvent not found. No animator!"));
        // Only these two verified error branches may be observed. Do not patch
        // a changed/previously rewritten operation by guessing another anchor.
        if (code.Count(Marker) != 2 || code.Any(i => i.blocks.Count != 0))
            throw new InvalidOperationException("Native utility timeout diagnostic anchors changed.");
        var report = AccessTools.DeclaredMethod(typeof(ArenaNativeModelDiagnostics), nameof(UtilityTimeout));
        foreach (var instruction in code)
        {
            if (Marker(instruction))
            {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                yield return first;
                yield return new CodeInstruction(OpCodes.Call, report);
            }
            yield return instruction; // Original string, Error and fallback intact.
        }
    }

    private static void UtilityTimeout(Player.FirearmController.FirearmOperation operation)
    {
        if (!TryContext(out _, out _, out _)) return;
        try
        {
            if (operation is Player.FirearmController.UtilityOperation regular)
                Report("utility-event-timeout", operation.Player, null, operation.Player, operation.Weapon,
                    () => "operation=UtilityOperation/" + regular._utilityOperationType + "; elapsed=" + regular._utilityTime);
            else if (operation is Player.FirearmController.LauncherUtilityOperation launcher)
                Report("launcher-utility-event-timeout", operation.Player, null, operation.Player, operation.Weapon,
                    () => "operation=LauncherUtilityOperation/" + launcher._utilityOperationType + "; elapsed=" + launcher._utilityTime);
        }
        catch (Exception ex) { ProbeFailure(ex); }
    }

    private static void CustomItemPrefix(CustomItem __instance, PlayerBody playerBody)
    {
        if (!TryContext(out _, out _, out _)) return;
        try
        {
            if (__instance != null && !CustomItem.InterestSlots.ContainsKey(__instance._itemType))
                Report("clipping-custom-type", __instance, playerBody, null, null, () => "customType=" + (int)__instance._itemType);
        }
        catch (Exception ex) { ProbeFailure(ex); }
    }

    private static void CustomSkinPrefix(CustomItemSkin __instance, PlayerBody playerBody)
    {
        if (!TryContext(out _, out _, out _)) return;
        try
        {
            if (__instance != null && !CustomItem.InterestSlots.ContainsKey(__instance._itemType))
                Report("clipping-skin-type", __instance, playerBody, null, null, () => "customType=" + (int)__instance._itemType);
        }
        catch (Exception ex) { ProbeFailure(ex); }
    }

    private static bool TryContext(out AbstractGame game, out GameWorld world, out string phase)
    {
        game = null; world = null; phase = null;
        if (!ArenaLog.DeveloperEnabled || _harmony == null || _probeFailed) return false;
        game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (!(game is LocalGame local) || world == null) return false;
        if (ArenaSession.IsExactActiveRaid(game, world))
        { phase = "active/" + local.Status; return true; }
        // Read-only loading exception: confirmed server arm + exact currently
        // loading map. Blank map, last Arena, menu and ordinary PMC are refused.
        if (!ArenaSession.IsActive && ArenaSession.ArmedForNextRaid && ArenaServerClient.HasPendingArm
            && !string.IsNullOrEmpty(ArenaServerClient.PendingArmToken)
            && !string.IsNullOrEmpty(ArenaSession.ArmedMap)
            && string.Equals(ArenaSession.ArmedMap, world.LocationId, StringComparison.OrdinalIgnoreCase)
            && local.Status != GameStatus.Stopped && local.Status != GameStatus.Stopping && local.Status != GameStatus.SoftStopping)
        { phase = "armed-loading/" + local.Status; return true; }
        return false;
    }

    private static void Report(string family, Component subject, PlayerBody explicitBody, Player explicitPlayer, Weapon weapon, Func<string> detail)
    {
        if (!TryContext(out var game, out var world, out var phase)) return;
        int id = !ReferenceEquals(subject, null) ? subject.GetInstanceID() : 0;
        if (!Budget.TryTake(game, world, ArenaSession.ArmEpoch, family + "/" + id, out bool reachedCap)) return;
        var owner = explicitPlayer != null ? explicitPlayer : subject != null ? subject.GetComponentInParent<Player>() : null;
        var body = !ReferenceEquals(explicitBody, null) ? explicitBody : owner != null ? owner.PlayerBody : subject != null ? subject.GetComponentInParent<PlayerBody>() : null;
        var prefab = subject != null ? subject.GetComponentInParent<WeaponPrefab>() : null;
        if (weapon == null && prefab != null) weapon = _weaponData.GetValue(prefab) as Weapon;
        string bodyProfile = !ReferenceEquals(body, null) ? _bodyProfile.GetValue(body) as string : null;
        var registered = !string.IsNullOrEmpty(bodyProfile) ? world.GetEverExistedPlayerByID(bodyProfile) : null;
        var line = new StringBuilder("Arena native model fault: family=").Append(family)
            .Append("; map=").Append(Clean(world.LocationId)).Append("; phase=").Append(phase)
            .Append("; mode=").Append(Clean(ArenaSession.IsActive ? ArenaSession.ActiveMode : ArenaSession.ArmedMode))
            .Append("; object=").Append(ObjectInfo(subject)).Append("; hierarchy=").Append(Path(subject != null ? subject.transform : null))
            .Append("; body=").Append(ObjectInfo(body))
            .Append("; bodyProfile=").Append(Clean(bodyProfile))
            .Append("; currentWorldBody=").Append(registered == null ? "unregistered" : ReferenceEquals(registered.PlayerBody, body) ? "same" : "different")
            .Append("; bodyCustomization=").Append(!ReferenceEquals(body, null) ? Clean(body.BodyCustomizationId?.Value) : "none")
            .Append("; owner=").Append(ObjectInfo(owner))
            .Append("; role=").Append(owner == null ? "unowned-preview-or-clone-possible" : ReferenceEquals(owner, world.MainPlayer) ? "main-player" : owner.IsAI ? "ai" : "other-player")
            .Append("; weaponTemplate=").Append(Clean(weapon?.TemplateId)).Append("; ").Append(Clean(detail(), 2200));
        var pool = subject != null ? subject.GetComponentInParent<AssetPoolObject>() : null;
        if (pool != null) line.Append("; pool=").Append(ObjectInfo(pool)).Append("/inPool=").Append(pool.IsInPool).Append("/stub=").Append(pool.IsStub);
        ArenaLog.Developer(line.ToString());
        if (reachedCap) ArenaLog.Developer("Arena native model fault context capped at 24 reports for this raid; native warnings/errors remain unchanged.");
    }

    private static string ObjectInfo(Component component)
    {
        if (ReferenceEquals(component, null)) return "none";
        string identity = component.GetType().Name + "#" + component.GetInstanceID();
        if (component == null) return identity + "/destroyed";
        return identity + "/" + Clean(component.name)
            + "/activeSelf=" + component.gameObject.activeSelf + "/activeHierarchy=" + component.gameObject.activeInHierarchy
            + "/scene=" + Clean(component.gameObject.scene.name);
    }

    private static string Path(Transform transform)
    {
        var parts = new List<string>();
        for (int i = 0; transform != null && i < 7; ++i, transform = transform.parent)
            parts.Add(Clean(transform.name, 64) + "#" + transform.GetInstanceID() + "/active=" + transform.gameObject.activeSelf);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string Clean(string value, int maximum = 160)
    {
        if (string.IsNullOrEmpty(value)) return "none";
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= maximum ? value : value.Substring(0, maximum) + "...";
    }

    private static string Stack() => new System.Diagnostics.StackTrace(2, false).ToString();

    private static void ProbeFailure(Exception exception)
    {
        if (!TryContext(out _, out _, out _)) return;
        _probeFailed = true;
        ArenaLog.Developer("Arena native model diagnostic stopped after its own read failure; native behavior unchanged: " + Clean(exception.GetBaseException().Message));
    }

    // Weak identity prevents diagnostic state retaining ended worlds/bodies.
    // Changing local body during respawn does NOT renew the raid's allowance.
    internal sealed class ReportBudget
    {
        private WeakReference _game, _world;
        private int _epoch, _total;
        private readonly Dictionary<string, int> _signatures = new();
        internal bool TryTake(object game, object world, int epoch, string signature, out bool reachedCap)
        {
            reachedCap = false;
            if (game == null || world == null) return false;
            if (_game == null || !ReferenceEquals(_game.Target, game) || !ReferenceEquals(_world.Target, world) || _epoch != epoch)
            {
                Clear(); _game = new WeakReference(game); _world = new WeakReference(world); _epoch = epoch;
            }
            if (_total >= 24) return false;
            _signatures.TryGetValue(signature, out int count);
            if (count >= 2) return false;
            _signatures[signature] = count + 1;
            reachedCap = ++_total == 24;
            return true;
        }
        internal void Clear() { _game = null; _world = null; _epoch = 0; _total = 0; _signatures.Clear(); }
    }
}
