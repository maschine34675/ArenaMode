using BepInEx.Bootstrap;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Spectator;

/// <summary>Detects another camera owner without taking a dependency on Freecam.</summary>
internal static class TerkoizFreecamProbe
{
    private const float MissingControllerProbeIntervalSeconds = 0.5f;
    private const string PluginGuid = "com.terkoiz.freecam";
    private const string ControllerTypeName = "Terkoiz.Freecam.FreecamController";

    private static bool _resolved;
    private static bool _present;
    private static string _problem;
    private static Type _controllerType;
    private static PropertyInfo _activeProperty;
    private static UnityEngine.Object _controller;
    private static float _nextControllerProbeAt;

    internal static void Reset()
    {
        _resolved = false;
        _present = false;
        _problem = null;
        _controllerType = null;
        _activeProperty = null;
        _controller = null;
        _nextControllerProbeAt = 0f;
    }

    internal static OptionalCameraOwnerState ReadState(out string problem)
    {
        Resolve();
        problem = _problem;
        if (!_present)
            return OptionalCameraOwnerState.Absent;
        if (problem != null)
            return OptionalCameraOwnerState.Incompatible;

        try
        {
            if (ReferenceEquals(_controller, null) || _controller == null)
            {
                _controller = null;
                if (Time.unscaledTime < _nextControllerProbeAt)
                    return OptionalCameraOwnerState.Idle;

                _nextControllerProbeAt =
                    Time.unscaledTime + MissingControllerProbeIntervalSeconds;
                _controller = UnityEngine.Object.FindObjectOfType(_controllerType);
            }

            if (_controller == null)
                return OptionalCameraOwnerState.Idle;

            return (bool)_activeProperty.GetValue(_controller)
                ? OptionalCameraOwnerState.Busy
                : OptionalCameraOwnerState.Idle;
        }
        catch (Exception ex)
        {
            problem = "Terkoiz.Freecam state read failed: " + ex.GetBaseException().Message;
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
            _controllerType = assembly.GetType(ControllerTypeName, throwOnError: false)
                ?? throw new TypeLoadException(ControllerTypeName + " was not found");
            _activeProperty = _controllerType.GetProperty(
                "IsFreecamActive",
                BindingFlags.Public | BindingFlags.Instance);
            if (_activeProperty?.PropertyType != typeof(bool))
                throw new MissingMemberException("Terkoiz.Freecam IsFreecamActive contract changed");
        }
        catch (Exception ex)
        {
            _problem = ex.GetBaseException().Message;
        }
    }
}
