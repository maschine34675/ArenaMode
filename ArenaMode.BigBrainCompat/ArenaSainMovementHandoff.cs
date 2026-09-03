using ArenaMode.Bots;
using BepInEx.Bootstrap;
using EFT;
using System;
using System.Linq;
using System.Reflection;

namespace ArenaMode.BigBrainCompat;

internal enum ArenaSainHandoffState
{
    Ready,
    WaitingForDeactivation,
    Unavailable
}

/// <summary>
/// Clears SAIN's independent movement state before Arena starts a native EFT
/// route. BigBrain stops SAIN's outgoing CustomLogic, but SAIN also retains its
/// own ActiveLayer, CurrentAction and path until its next component tick.
/// </summary>
internal static class ArenaSainMovementHandoff
{
    private const string SainPluginGuid = "me.sol.sain";

    private static bool _resolved;
    private static bool _sainDetected;
    private static Type _sainBotType;
    private static MethodInfo _getSain;
    private static PropertyInfo _botOwner;
    private static PropertyInfo _player;
    private static PropertyInfo _botActivation;
    private static PropertyInfo _mover;
    private static PropertyInfo _sainLayersActive;
    private static PropertyInfo _activeLayer;
    private static PropertyInfo _currentAction;
    private static PropertyInfo _botInCombat;
    private static MethodInfo _stopMover;
    private static MethodInfo _setCurrentAction;
    private static MethodInfo _setActiveLayer;
    private static MethodInfo _setInCombat;
    private static object _noActiveLayer;

    internal static ArenaSainHandoffState EnsureSuspended(
        BotOwner bot,
        out string problem)
    {
        problem = null;
        if (bot?.GetPlayer == null)
        {
            problem = "the live EFT bot body is missing";
            return ArenaSainHandoffState.Unavailable;
        }

        try
        {
            Resolve();
            if (!_resolved)
            {
                problem = "the installed SAIN assembly is still loading";
                return ArenaSainHandoffState.WaitingForDeactivation;
            }

            if (!_sainDetected)
                return ArenaSainHandoffState.Ready;

            if (!HasCompleteContract())
            {
                problem = "the installed SAIN movement contract is incomplete";
                return ArenaSainHandoffState.Unavailable;
            }

            object[] args = { bot.ProfileId, null };
            bool found = _getSain.Invoke(null, args) is bool registered && registered;
            object registeredSainBot = args[1];
            object attachedSainBot = bot.GetComponent(_sainBotType);
            // SAIN's Harmony patches also resolve by profile ID. If that
            // registry points at an old body, suspending only an exact attached
            // component would still leave the patch path controlled by stale
            // SAIN state, so a registered mismatch must fail closed.
            TeamArenaSainSourceSelection selection = TeamArenaSainSourcePolicy.Select(
                found,
                registeredSainBot != null,
                IsExactWrapper(registeredSainBot, bot),
                attachedSainBot != null,
                IsExactWrapper(attachedSainBot, bot),
                ReferenceEquals(registeredSainBot, attachedSainBot));
            object sainBot;
            switch (selection)
            {
                case TeamArenaSainSourceSelection.RegisteredExact:
                    sainBot = registeredSainBot;
                    break;
                case TeamArenaSainSourceSelection.AttachedExact:
                    sainBot = attachedSainBot;
                    break;
                case TeamArenaSainSourceSelection.NotPresent:
                    return ArenaSainHandoffState.Ready;
                case TeamArenaSainSourceSelection.Incompatible:
                default:
                    problem = "SAIN returned a missing or stale profile-registry wrapper";
                    return ArenaSainHandoffState.Unavailable;
            }

            object activation = _botActivation.GetValue(sainBot);
            object mover = _mover.GetValue(sainBot);
            if (activation == null || mover == null)
            {
                problem = "SAIN's activation or mover component is missing";
                return ArenaSainHandoffState.Unavailable;
            }

            bool layerIsNone = Equals(_activeLayer.GetValue(activation), _noActiveLayer);
            bool currentActionIsClear = _currentAction.GetValue(activation) == null;
            bool inCombat = _botInCombat.GetValue(activation) is bool combat && combat;
            bool layersActive = _sainLayersActive.GetValue(sainBot) is bool active && active;
            if (layerIsNone && currentActionIsClear && !inCombat)
            {
                return layersActive
                    ? ArenaSainHandoffState.WaitingForDeactivation
                    : ArenaSainHandoffState.Ready;
            }

            // Stop both SAIN's private active path and EFT's native mover before
            // clearing bookkeeping. SAINLayersActive is a derived toggle; its
            // next ManualUpdate performs the final true-to-false transition and
            // may stop EFT's mover once more, so Arena must wait for that tick.
            _setActiveLayer.Invoke(activation, new[] { _noActiveLayer });
            _setCurrentAction.Invoke(activation, new object[] { null });
            _stopMover.Invoke(mover, Array.Empty<object>());
            _setInCombat.Invoke(activation, new object[] { false });

            if (!Equals(_activeLayer.GetValue(activation), _noActiveLayer)
                || _currentAction.GetValue(activation) != null
                || (_botInCombat.GetValue(activation) is bool stillInCombat && stillInCombat))
            {
                problem = "SAIN did not release its active movement state";
                return ArenaSainHandoffState.Unavailable;
            }

            return _sainLayersActive.GetValue(sainBot) is bool stillActive && stillActive
                ? ArenaSainHandoffState.WaitingForDeactivation
                : ArenaSainHandoffState.Ready;
        }
        catch (Exception ex)
        {
            problem = ex.GetBaseException().Message;
            return ArenaSainHandoffState.Unavailable;
        }
    }

    private static void Resolve()
    {
        if (_resolved)
            return;

        Assembly sainAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                "SAIN",
                StringComparison.Ordinal));
        TeamArenaSainAssemblyState assemblyState = TeamArenaSainAssemblyPolicy.Select(
            Chainloader.PluginInfos.ContainsKey(SainPluginGuid),
            sainAssembly != null);
        if (assemblyState != TeamArenaSainAssemblyState.Loaded)
        {
            // The compatibility assembly can initialize before optional SAIN.
            // Retry while its plugin metadata exists; cache only a confirmed
            // absence so non-SAIN installations do not scan every brain tick.
            _resolved = assemblyState == TeamArenaSainAssemblyState.ConfirmedAbsent;
            return;
        }

        _resolved = true;

        _sainDetected = true;
        Type enableType = sainAssembly.GetType(
            "SAIN.SAINEnableClass",
            throwOnError: false,
            ignoreCase: false);
        Type botType = sainAssembly.GetType(
            "SAIN.Components.BotComponent",
            throwOnError: false,
            ignoreCase: false);
        Type activationType = sainAssembly.GetType(
            "SAIN.SAINComponent.Classes.SAINActivationClass",
            throwOnError: false,
            ignoreCase: false);
        Type moverType = sainAssembly.GetType(
            "SAIN.SAINComponent.Classes.Mover.SAINMoverClass",
            throwOnError: false,
            ignoreCase: false);
        Type layerType = sainAssembly.GetType(
            "SAIN.Models.Enums.ESAINLayer",
            throwOnError: false,
            ignoreCase: false);
        Type actionType = sainAssembly.GetType(
            "SAIN.Layers.IBotAction",
            throwOnError: false,
            ignoreCase: false);
        if (enableType == null
            || botType == null
            || activationType == null
            || moverType == null
            || layerType?.IsEnum != true
            || actionType?.IsInterface != true)
        {
            return;
        }

        _sainBotType = botType;

        _getSain = enableType.GetMethod(
            "GetSAIN",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), botType.MakeByRefType() },
            null);
        _botOwner = botType.GetProperty("BotOwner", BindingFlags.Public | BindingFlags.Instance);
        _player = botType.GetProperty("Player", BindingFlags.Public | BindingFlags.Instance);
        _botActivation = botType.GetProperty("BotActivation", BindingFlags.Public | BindingFlags.Instance);
        _mover = botType.GetProperty("Mover", BindingFlags.Public | BindingFlags.Instance);
        _sainLayersActive = botType.GetProperty(
            "SAINLayersActive",
            BindingFlags.Public | BindingFlags.Instance);
        _activeLayer = activationType.GetProperty("ActiveLayer", BindingFlags.Public | BindingFlags.Instance);
        _currentAction = activationType.GetProperty("CurrentAction", BindingFlags.Public | BindingFlags.Instance);
        _botInCombat = activationType.GetProperty("BotInCombat", BindingFlags.Public | BindingFlags.Instance);
        _stopMover = moverType.GetMethod(
            "Stop",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
        _setCurrentAction = activationType.GetMethod(
            "SetCurrentAction",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { actionType },
            null);
        _setActiveLayer = activationType.GetMethod(
            "SetActiveLayer",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { layerType },
            null);
        _setInCombat = activationType.GetMethod(
            "SetInCombat",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(bool) },
            null);
        _noActiveLayer = Enum.Parse(layerType, "None", ignoreCase: false);

        if (_botOwner?.PropertyType != typeof(BotOwner)
            || _player?.PropertyType != typeof(Player)
            || _botActivation?.PropertyType != activationType
            || _mover?.PropertyType != moverType
            || _sainLayersActive?.PropertyType != typeof(bool)
            || _activeLayer?.PropertyType != layerType
            || _currentAction?.PropertyType != actionType
            || _botInCombat?.PropertyType != typeof(bool)
            || _stopMover?.ReturnType != typeof(void)
            || _setCurrentAction?.ReturnType != typeof(void)
            || _setActiveLayer?.ReturnType != typeof(void)
            || _setInCombat?.ReturnType != typeof(void))
        {
            _getSain = null;
        }
    }

    private static bool HasCompleteContract()
        => _getSain != null
           && _sainBotType != null
           && _botOwner != null
           && _player != null
           && _botActivation != null
           && _mover != null
           && _sainLayersActive != null
           && _activeLayer != null
           && _currentAction != null
           && _botInCombat != null
           && _stopMover != null
           && _setCurrentAction != null
           && _setActiveLayer != null
           && _setInCombat != null
           && _noActiveLayer != null;

    private static bool IsExactWrapper(object sainBot, BotOwner bot)
        => sainBot != null
           && bot != null
           && ReferenceEquals(_botOwner.GetValue(sainBot), bot)
           && ReferenceEquals(_player.GetValue(sainBot), bot.GetPlayer);
}
