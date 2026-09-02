using BepInEx.Bootstrap;
using System;
using System.Reflection;

namespace ArenaMode.Spectator;

/// <summary>
/// Reflection-only ownership probe for the locally installed KillcamReplay. The
/// three state signals cover its lead-in, playback and final cleanup intervals.
/// </summary>
internal static class KillcamReplayCompat
{
    private const string PluginGuid = "com.maschine.KillcamReplay";
    private const string PluginTypeName = "KillcamReplay.Plugin";
    private const string ControllerTypeName = "KillcamReplay.Replay.ReplayController";

    private static bool _resolved;
    private static bool _present;
    private static string _problem;
    private static FieldInfo _replayControllerField;
    private static FieldInfo _pendingKillerField;
    private static PropertyInfo _isPlayingProperty;
    private static PropertyInfo _readyForStopProperty;
    private static MethodInfo _extraDelayMethod;

    internal static bool ReplayMayRun
    {
        get
        {
            Resolve();
            return !_present || _problem == null;
        }
    }

    internal static void Reset()
    {
        _resolved = false;
        _present = false;
        _problem = null;
        _replayControllerField = null;
        _pendingKillerField = null;
        _isPlayingProperty = null;
        _readyForStopProperty = null;
        _extraDelayMethod = null;
    }

    internal static OptionalCameraOwnerState ReadState(out float maximumRemainingSeconds, out string problem)
    {
        maximumRemainingSeconds = 0f;
        Resolve();
        problem = _problem;
        if (!_present)
            return OptionalCameraOwnerState.Absent;
        if (problem != null)
            return OptionalCameraOwnerState.Incompatible;

        try
        {
            object controller = _replayControllerField.GetValue(null);
            if (controller == null)
            {
                problem = "KillcamReplay.ReplayCtrl was null";
                return OptionalCameraOwnerState.Incompatible;
            }

            bool pending = _pendingKillerField.GetValue(null) != null;
            bool playing = (bool)_isPlayingProperty.GetValue(controller);
            bool readyForStop = (bool)_readyForStopProperty.GetValue(controller);
            if (!pending && !playing && readyForStop)
                return OptionalCameraOwnerState.Idle;

            object delay = _extraDelayMethod.Invoke(null, null);
            maximumRemainingSeconds = delay is float seconds && !float.IsNaN(seconds)
                ? Math.Max(0f, seconds)
                : 0f;
            return OptionalCameraOwnerState.Busy;
        }
        catch (Exception ex)
        {
            problem = "KillcamReplay state read failed: " + ex.GetBaseException().Message;
            return OptionalCameraOwnerState.Incompatible;
        }
    }

    private static void Resolve()
    {
        if (_resolved)
            return;

        _resolved = true;
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo)
            || pluginInfo?.Instance == null)
        {
            return;
        }

        _present = true;
        try
        {
            Assembly assembly = pluginInfo.Instance.GetType().Assembly;
            Type pluginType = assembly.GetType(PluginTypeName, throwOnError: false);
            Type controllerType = assembly.GetType(ControllerTypeName, throwOnError: false);
            if (pluginType == null || controllerType == null)
                throw new TypeLoadException("expected public KillcamReplay types were not found");

            _replayControllerField = pluginType.GetField(
                "ReplayCtrl",
                BindingFlags.Public | BindingFlags.Static);
            _pendingKillerField = controllerType.GetField(
                "PendingReplayKiller",
                BindingFlags.Public | BindingFlags.Static);
            _isPlayingProperty = controllerType.GetProperty(
                "IsPlaying",
                BindingFlags.Public | BindingFlags.Instance);
            _readyForStopProperty = controllerType.GetProperty(
                "ReadyForStop",
                BindingFlags.Public | BindingFlags.Instance);
            _extraDelayMethod = controllerType.GetMethod(
                "GetExtraDelay",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (_replayControllerField == null
                || !controllerType.IsAssignableFrom(_replayControllerField.FieldType)
                || _pendingKillerField == null
                || _isPlayingProperty?.PropertyType != typeof(bool)
                || _readyForStopProperty?.PropertyType != typeof(bool)
                || _extraDelayMethod?.ReturnType != typeof(float))
            {
                throw new MissingMemberException("KillcamReplay camera-ownership contract changed");
            }
        }
        catch (Exception ex)
        {
            _problem = ex.GetBaseException().Message;
        }
    }
}
