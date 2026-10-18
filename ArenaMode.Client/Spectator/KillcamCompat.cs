using BepInEx.Bootstrap;
using System;
using System.Reflection;

namespace ArenaMode.Spectator;

/// <summary>
/// Reflection-only ownership probe for the locally installed Killcam. The
/// three state signals cover its lead-in, playback and final cleanup intervals.
/// </summary>
internal static class KillcamCompat
{
    private const string PluginGuid = "com.maschine.Killcam";
    private const string PluginTypeName = "Killcam.Plugin";
    private const string ControllerTypeName = "Killcam.Replay.ReplayController";

    private static bool _resolved;
    private static bool _present;
    private static string _problem;
    private static FieldInfo _replayControllerField;
    private static FieldInfo _pendingKillerField;
    private static PropertyInfo _isPlayingProperty;
    private static PropertyInfo _readyForStopProperty;
    private static MethodInfo _extraDelayMethod;
    private static PropertyInfo _deathReplayActiveProperty;

    internal static bool CanPlay(out string reason)
    {
        Resolve();
        reason = null;
        if (!_present) { reason = "Requires the Killcam mod."; return false; }
        if (_problem != null) { reason = "Killcam compatibility could not be verified."; return false; }
        try
        {
            if (_deathReplayActiveProperty?.PropertyType != typeof(bool))
            { reason = "Update Killcam: its enabled-state API is unavailable."; return false; }
            if (!(bool)_deathReplayActiveProperty.GetValue(null))
            { reason = "Enable DeathReplay in Killcam; KillAndDamageInfo also disables it."; return false; }
            if (_replayControllerField.GetValue(null) == null)
            { reason = "Killcam is not ready."; return false; }
            return true;
        }
        catch (Exception)
        { reason = "Killcam availability could not be read."; return false; }
    }

    internal static bool ReplayMayRun
    {
        get
        {
            Resolve();
            return !_present || _problem == null;
        }
    }

    // Rebind must not start its replacement deadline while a slow-motion replay
    // still owns the dead body. Unknown ownership is never a release either.
    internal static bool CameraReleased
        => ReadState(out _, out _) is OptionalCameraOwnerState.Absent or OptionalCameraOwnerState.Idle;

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
        _deathReplayActiveProperty = null;
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
                problem = "Killcam.ReplayCtrl was null";
                return OptionalCameraOwnerState.Incompatible;
            }

            bool pending = _pendingKillerField.GetValue(null) != null;
            bool playing = (bool)_isPlayingProperty.GetValue(controller);
            bool readyForStop = (bool)_readyForStopProperty.GetValue(controller);
            if (!pending && !playing && readyForStop)
                return OptionalCameraOwnerState.Idle;

            object delay = _extraDelayMethod.Invoke(null, null);
            if (delay is not float seconds || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
            {
                problem = "Killcam returned an invalid replay bound";
                return OptionalCameraOwnerState.Incompatible;
            }
            maximumRemainingSeconds = seconds;
            return OptionalCameraOwnerState.Busy;
        }
        catch (Exception ex)
        {
            problem = "Killcam state read failed: " + ex.GetBaseException().Message;
            return OptionalCameraOwnerState.Incompatible;
        }
    }

    private static void Resolve()
    {
        if (_resolved)
            return;

        _resolved = true;
        // A leftover Replay plugin can still own the camera even when the renamed
        // plugin refused to load. Never mistake that installation for no killcam.
        if (Chainloader.PluginInfos.TryGetValue("com.maschine.KillcamReplay", out var legacy)
            && legacy?.Instance != null)
        {
            _present = true;
            _problem = "Remove the old KillcamReplay DLL and install Killcam.";
            return;
        }
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
                throw new TypeLoadException("expected public Killcam types were not found");

            _replayControllerField = pluginType.GetField(
                "ReplayCtrl",
                BindingFlags.Public | BindingFlags.Static);
            _deathReplayActiveProperty = pluginType.GetProperty(
                "DeathReplayActive", BindingFlags.Public | BindingFlags.Static);
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
                throw new MissingMemberException("Killcam camera-ownership contract changed");
            }
        }
        catch (Exception ex)
        {
            _problem = ex.GetBaseException().Message;
        }
    }
}
