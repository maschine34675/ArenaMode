using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Hud;

/// <summary>
/// Suppresses the game's own input while the arena panel is open, so clicking a
/// toggle does not fire the weapon and dragging the window does not turn the
/// character. The game funnels every bound input through a single
/// InputManager method with the signature (List&lt;ECommand&gt;, float[]);
/// filtering the list and zeroing the axes blocks exactly one delivery. A UI
/// close stays protected until a freshly collected, released-pointer batch has
/// passed through that filter; input sampled around the click cannot escape.
///
/// Lifted from the same author's ModProfiler, which has been running this in
/// raids for a while.
/// </summary>
internal static class ArenaInputBlocker
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".inputblock");
    private static FieldInfo _main, _commands, _axes, _currentPreset, _workingKeys, _nativeIgnore;
    private static readonly object NativeMenuRelease = new();
    private static object _manager, _preset;
    private static IList _commandList;
    private static float[] _axisList;
    private static AbstractGame _game;
    private static GameWorld _world;
    private static bool _ready, _drain, _releaseCollected;
    private static long _generation, _sample;
    private static int _shotCommands, _diagnostics;

    internal static bool Blocked
    {
        get { lock (Owners) return _ready && SyncScope() && (Owners.Count > 0 || _drain || NativeMenuIgnored()); }
    }

    /// <summary>
    /// Who currently wants input blocked. Two panels can be up at once — the
    /// scoreboard over the buy menu — and a plain bool let whichever CLOSED
    /// first hand live game input to the one still open. That is the exact
    /// condition these panels exist to prevent: a click on a kit fires the
    /// weapon and a drag on the window turns the character.
    ///
    /// An explicitly requested UI release also drains one fresh input delivery
    /// after the pointer is released. Non-UI owners keep their old behavior.
    /// </summary>
    private static readonly HashSet<object> Owners = new HashSet<object>();

    /// <summary>Blocked while ANY owner wants it, plus an opted-in UI release.</summary>
    internal static void Request(object owner, bool wanted, bool drainOnRelease = false)
    {
        if (owner == null)
            return;

        lock (Owners)
        {
            if (!SyncScope()) return;
            if (wanted)
            {
                if (Owners.Add(owner) && _drain)
                {
                    _generation++;
                    _releaseCollected = false;
                }
            }
            else if (Owners.Remove(owner) && drainOnRelease && _ready)
            {
                _drain = true;
                _releaseCollected = false; // Older, buffered input is not a release sample.
                _generation++;
                _shotCommands = 0;
                Trace($"close {_generation} pending; sample={_sample}, owners={Owners.Count}");
            }
        }
    }

    private static bool _attempted;

    /// <summary>
    /// Retain the existing release-command whitelist. Swallowing
    /// "EndShooting" while the trigger is down
    /// latches the weapon into permanent fire; the same goes for sprint, lean,
    /// breath-hold and interaction. Escape passes too, so the game's own menu
    /// still works while the panel is open.
    /// </summary>
    private static HashSet<int> _passThrough;

    public static void Apply()
    {
        if (_attempted)
            return;

        _attempted = true;
        try
        {
            var type = AccessTools.TypeByName("EFT.InputSystem.InputManager")
                ?? throw new TypeLoadException("EFT.InputSystem.InputManager not found");

            var target = FindDeliverMethod(type);
            var presetType = AccessTools.TypeByName("EFT.InputSystem.InputPreset")
                ?? throw new TypeLoadException("EFT.InputSystem.InputPreset not found");
            _main = RequireField(type, "_isMain", typeof(bool), false);
            _commands = RequireField(type, "_commandsList", target.GetParameters()[0].ParameterType, false);
            _axes = RequireField(type, "_axesList", typeof(float[]), false);
            _currentPreset = RequireField(type, "_currentPreset", presetType, true);
            _workingKeys = presetType.GetField("_workingKeyCombinations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_workingKeys?.FieldType.IsArray != true || _workingKeys.IsStatic)
                throw new MissingFieldException("InputPreset working keys changed");
            var update = RequireVoid(type, "Update", Type.EmptyTypes);
            var destroy = RequireVoid(type, "OnDestroy", Type.EmptyTypes);
            var collect = RequireVoid(presetType, "UpdateInput", new[] { _commands.FieldType, typeof(float[]), typeof(float) });
            _nativeIgnore = RequireField(typeof(GamePlayerOwner), "bool_0", typeof(bool), true);
            var nativeIgnore = typeof(GamePlayerOwner).GetMethod("SetIgnoreInput", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(bool) }, null);
            if (nativeIgnore == null || nativeIgnore.ReturnType != typeof(void)) throw new MissingMethodException("GamePlayerOwner.SetIgnoreInput");
            BuildPassThrough(target);
            Patches.Patch(update, prefix: Hook(nameof(BeforeUpdate)));
            Patches.Patch(collect, postfix: Hook(nameof(AfterCollect)));
            Patches.Patch(destroy, prefix: Hook(nameof(BeforeDestroy)));
            Patches.Patch(
                target,
                prefix: Hook(nameof(Prefix), Priority.Last),
                postfix: Hook(nameof(AfterDispatch), Priority.Last));
            Patches.Patch(nativeIgnore, prefix: Hook(nameof(BeforeNativeIgnore)));
            _ready = true;

            Plugin.ModLogger.LogDebug("Arena input blocker ready: panel close waits for released-pointer input delivery.");
            Plugin.ModLogger.LogDebug("Arena input blocker: native menu resume release safeguard ready.");
        }
        catch (Exception ex)
        {
            Shutdown();
            Plugin.ModLogger.LogWarning(
                "Arena input blocker unavailable — the panel will free the cursor but not "
                + $"suppress game input: {ex.Message}");
        }
    }

    private static HarmonyMethod Hook(string name, int priority = Priority.Normal)
        => new(AccessTools.Method(typeof(ArenaInputBlocker), name)) { priority = priority };

    private static FieldInfo RequireField(Type type, string name, Type fieldType, bool isStatic)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.FieldType != fieldType || field.IsStatic != isStatic) throw new MissingFieldException(type.Name, name);
        return field;
    }

    private static MethodInfo RequireVoid(Type type, string name, Type[] parameters)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, parameters, null);
        if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(type.Name, name);
        return method;
    }

    private static bool SyncScope()
    {
        var game = Singleton<AbstractGame>.Instance;
        var world = Singleton<GameWorld>.Instance;
        if (!ArenaSession.IsExactActiveRaid(game, world)) { ResetScope(); return false; }
        if (!ReferenceEquals(game, _game) || !ReferenceEquals(world, _world))
        {
            ResetScope();
            _game = game;
            _world = world;
        }
        return true;
    }

    private static void ResetScope()
    {
        Owners.Clear();
        ResetInput();
        _game = null;
        _world = null;
    }

    private static void ResetInput()
    {
        _manager = _preset = null;
        _commandList = null;
        _axisList = null;
        _drain = _releaseCollected = false;
        _generation++;
    }

    internal static void Shutdown()
    {
        lock (Owners)
        {
            _ready = _attempted = false;
            try { Patches.UnpatchSelf(); }
            catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena input blocker cleanup: {ex.Message}"); }
            finally { ResetScope(); }
        }
    }

    private static void BeforeUpdate(object __instance)
    {
        lock (Owners)
        {
            if (!_ready || !SyncScope() || !Equals(_main.GetValue(__instance), true)) return;
            var preset = _currentPreset.GetValue(null);
            var commands = _commands.GetValue(__instance) as IList;
            var axes = _axes.GetValue(__instance) as float[];
            // Preserve active UI owners when input is replaced, but never carry
            // a pending old-manager release into a new input tree/preset.
            if (_manager != null && (!ReferenceEquals(_manager, __instance) || !ReferenceEquals(_preset, preset)
                || !ReferenceEquals(_commandList, commands) || !ReferenceEquals(_axisList, axes))) ResetInput();
            _manager = __instance;
            _preset = preset;
            _commandList = commands;
            _axisList = axes;
        }
    }

    private static bool PointerReleased()
    {
        for (int i = 0; i < 3; i++)
            if (Input.GetMouseButton(i) || Input.GetMouseButtonDown(i)) return false;
        return true;
    }

    private static bool IsCurrentNativeOwner()
    {
        var player = GamePlayerOwner.MyPlayer;
        return player != null && player.IsYourPlayer && !player.IsAI
            && ReferenceEquals(player, _world?.MainPlayer);
    }

    private static bool NativeMenuIgnored()
        => IsCurrentNativeOwner() && Equals(_nativeIgnore.GetValue(null), true);

    private static void BeforeNativeIgnore(bool __0)
    {
        lock (Owners)
        {
            if (!_ready || !SyncScope() || __0 || !NativeMenuIgnored()) return;
            // EFT screen environments and the native orphan-UI recovery both
            // release this gate BEFORE gameplay consumes the next command.
            // Do not intercept menu commands while it is open: native screens
            // still own inventory, Escape, settings and keyboard navigation.
            Request(NativeMenuRelease, true);
            Request(NativeMenuRelease, false, drainOnRelease: true);
            Trace("native menu released gameplay input; pointer drain pending");
        }
    }

    private static void AfterCollect(object __instance, IList __0, float[] __1)
    {
        lock (Owners)
        {
            if (!_ready || !SyncScope() || !ReferenceEquals(__instance, _preset)
                || !ReferenceEquals(__0, _commandList) || !ReferenceEquals(__1, _axisList)) return;
            _sample++;
            // Normal native collection clears/rebuilds commands only when its
            // working key array exists. A returned, uninitialized preset is not
            // proof of a fresh batch. No artificial timer or frame count here.
            if (_drain) _releaseCollected = _workingKeys.GetValue(__instance) != null && PointerReleased();
        }
    }

    private static void BeforeDestroy(object __instance)
    {
        lock (Owners) if (ReferenceEquals(__instance, _manager)) ResetInput();
    }

    private static void Trace(string text)
    {
        if (!ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled) return;
        if (_diagnostics++ < 24) ArenaMode.Diagnostics.ArenaLog.Developer("Arena panel input: " + text);
    }

    /// <summary>Matched by SIGNATURE so an SPT rename cannot break it silently.</summary>
    private static MethodInfo FindDeliverMethod(Type type)
    {
        foreach (var method in type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.ReturnType != typeof(void))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 2
                && parameters[0].ParameterType.IsGenericType
                && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(List<>)
                && parameters[0].ParameterType.GetGenericArguments()[0].Name == "ECommand"
                && parameters[1].ParameterType == typeof(float[]))
            {
                return method;
            }
        }

        throw new MissingMethodException(
            "no delivery method with signature (List<ECommand>, float[]) on EFT.InputSystem.InputManager");
    }

    /// <summary>
    /// Resolved once off the delivery method's own generic argument, so a
    /// renamed enum type cannot break it.
    /// </summary>
    private static void BuildPassThrough(MethodInfo target)
    {
        var commandType = target.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var pass = new HashSet<int>();

        foreach (var name in Enum.GetNames(commandType))
        {
            if (name.StartsWith("End", StringComparison.Ordinal)
                || name.EndsWith("End", StringComparison.Ordinal)
                || name.StartsWith("Stop", StringComparison.Ordinal)
                || name.StartsWith("Release", StringComparison.Ordinal)
                || name == "Escape")
            {
                pass.Add(Convert.ToInt32(Enum.Parse(commandType, name)));
            }
        }

        _passThrough = pass;
    }

    // Positional injection (__0/__1): binding by parameter NAME would break
    // silently if an update renames them while the signature stays.
    private static void Prefix(object __instance, IList __0, float[] __1, out long __state)
    {
        __state = -1;
        lock (Owners)
        {
            if (!_ready || !SyncScope() || (Owners.Count == 0 && !_drain)) return;

            if (__0 != null)
            {
                if (_passThrough == null)
                {
                    __0.Clear();
                }
                else
                {
                    // Preserve the established release-command whitelist.
                    for (var i = __0.Count - 1; i >= 0; i--)
                    {
                        if (!_passThrough.Contains(Convert.ToInt32(__0[i])))
                        {
                            if (_drain && string.Equals(__0[i].ToString(), "ToggleShooting", StringComparison.Ordinal)) _shotCommands++;
                            __0.RemoveAt(i);
                        }
                    }
                }
            }

            if (__1 != null)
                Array.Clear(__1, 0, __1.Length);
            // SuperSmooth dispatches a DIFFERENT empty list first. Only the real
            // freshly collected batch can complete this release, even if it is empty.
            if (_drain && _releaseCollected && Owners.Count == 0 && PointerReleased()
                && ReferenceEquals(__instance, _manager) && ReferenceEquals(__0, _commandList)
                && ReferenceEquals(__1, _axisList)) __state = _generation;
        }
    }

    private static void AfterDispatch(long __state)
    {
        lock (Owners)
        {
            // The original delivery has consumed the filtered batch. Reopening
            // or a new close during delivery cannot release another owner's vote.
            if (__state < 0 || !SyncScope() || !_drain || __state != _generation || Owners.Count > 0) return;
            _drain = _releaseCollected = false;
            Trace($"close {_generation} released; sample={_sample}, blockedShotCommands={_shotCommands}");
        }
    }
}
