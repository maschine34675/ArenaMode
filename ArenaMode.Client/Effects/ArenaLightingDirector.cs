using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Effects;

/// <summary>
/// Owns one additive directional light for short Arena round-state feedback.
/// It never edits map objects, shared materials, lightmaps or EFT's global
/// lighting configuration, so teardown is simply disabling and destroying the
/// one object this service created.
/// </summary>
internal static class ArenaLightingDirector
{
    private const string BlackoutPluginGuid = "com.vultify.blackout";
    private const string GoingDarkPluginGuid = "com.senze.goingdark";
    private const string LightObjectName = "ArenaMode_PhaseLighting";

    private static ArenaMatch _match;
    private static GameWorld _world;
    private static GameObject _lightObject;
    private static Light _light;
    private static float _fightUntil;
    private static ArenaLightingOutcome _outcome;
    private static float _outcomeUntil;
    private static int _observedRoundsNumber;
    private static bool _roundsWasIntermission;
    private static bool _externalOwnerLogged;
    private static bool _externalProbeFailureLogged;
    private static bool _creationFailureLogged;
    private static ArenaLightingEffect _lastEffect = ArenaLightingEffect.Off;

    internal static void Attach(ArenaMatch match, GameWorld world)
    {
        Reset();
        if (match == null || world == null)
            return;

        _match = match;
        _world = world;
        _match.OnAnnouncerCue += OnAnnouncerCue;
        // Tactical owns a real buy/staging phase and raises RoundStart only
        // once combat is actually live. Flashing here would announce a false
        // start while the player shops or a protected 5v5 roster assembles.
        // The other modes begin immediately and need this initial edge because
        // they do not all publish a round lifecycle cue.
        _fightUntil = match.Mode is TacticalMode
            ? 0f
            : Time.time + ArenaLightingPolicy.FightFlashSeconds;

        if (match.Mode is RoundsMode rounds)
        {
            _observedRoundsNumber = rounds.Round.Number;
            _roundsWasIntermission = rounds.InIntermission;
        }

        Plugin.ModLogger?.LogInfo(
            ArenaLightingPolicy.SupportsMap(world.LocationId)
                ? "Arena lighting director armed for Labs."
                : $"Arena lighting director inactive on unsupported map '{world.LocationId ?? "<null>"}'.");
    }

    internal static void Tick()
    {
        if (_match == null)
            return;

        if (!ArenaSession.IsActive
            || !Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, _world))
        {
            Reset();
            return;
        }

        ObserveRoundTransitions();

        bool enabled = Settings.ArenaLightingEnabled?.Value == true;
        bool supportedMap = ArenaLightingPolicy.SupportsMap(_world?.LocationId);
        if (enabled
            && supportedMap
            && Settings.ArenaLightingRespectExternalMods?.Value == true
            && TryGetExternalLightingOwner(out string externalOwner))
        {
            if (!_externalOwnerLogged)
            {
                _externalOwnerLogged = true;
                Plugin.ModLogger?.LogInfo(
                    $"Arena phase lighting stayed off because {externalOwner} owns map lighting. "
                    + "Disable RespectExternalLightingMods in F12 to force the additive layer.");
            }

            Apply(ArenaLightingFrame.Off);
            return;
        }

        ReadModeTiming(out float countdownRemaining, out float liveRemaining);
        float now = Time.time;
        float outcomeDuration = ArenaLightingPolicy.BoundOutcomeDuration(
            Settings.ArenaLightingOutcomeSeconds?.Value
            ?? ArenaLightingPolicy.DefaultOutcomeSeconds);
        var context = new ArenaLightingContext
        {
            Enabled = enabled,
            SupportedMap = supportedMap,
            Now = now,
            Strength = Settings.ArenaLightingStrength?.Value ?? 0f,
            CountdownRemaining = countdownRemaining,
            LiveRemaining = liveRemaining,
            PressureWindow = Math.Max(0f, Settings.ArenaLightingPressureSeconds?.Value ?? 0f),
            FightRemaining = _fightUntil - now,
            Outcome = _outcome,
            OutcomeRemaining = _outcomeUntil - now,
            OutcomeDuration = outcomeDuration,
            FriendlyColor = ToPolicyColor(ArenaTracerPalette.Resolve(
                Settings.FriendlyTracerColor?.Value ?? ArenaTracerColor.Cyan)),
            OpponentColor = ToPolicyColor(ArenaTracerPalette.Resolve(
                Settings.OpponentTracerColor?.Value ?? ArenaTracerColor.Red)),
        };

        Apply(ArenaLightingPolicy.Evaluate(context));
    }

    internal static void Reset()
    {
        if (_match != null)
            _match.OnAnnouncerCue -= OnAnnouncerCue;

        if (_light != null)
        {
            _light.enabled = false;
            _light.intensity = 0f;
        }

        if (_lightObject != null)
            UnityEngine.Object.Destroy(_lightObject);

        _match = null;
        _world = null;
        _lightObject = null;
        _light = null;
        _fightUntil = 0f;
        _outcome = ArenaLightingOutcome.None;
        _outcomeUntil = 0f;
        _observedRoundsNumber = 0;
        _roundsWasIntermission = false;
        _externalOwnerLogged = false;
        _externalProbeFailureLogged = false;
        _creationFailureLogged = false;
        _lastEffect = ArenaLightingEffect.Off;
    }

    internal static void Shutdown() => Reset();

    private static void OnAnnouncerCue(ArenaAnnouncerCue cue)
    {
        float now = Time.time;
        switch (cue)
        {
            case ArenaAnnouncerCue.RoundStart:
                _outcome = ArenaLightingOutcome.None;
                _outcomeUntil = 0f;
                _fightUntil = now + ArenaLightingPolicy.FightFlashSeconds;
                break;

            case ArenaAnnouncerCue.RoundWon:
                _outcome = ArenaLightingOutcome.Won;
                _outcomeUntil = now + ArenaLightingPolicy.BoundOutcomeDuration(
                    Settings.ArenaLightingOutcomeSeconds?.Value
                    ?? ArenaLightingPolicy.DefaultOutcomeSeconds);
                break;

            case ArenaAnnouncerCue.RoundLost:
                _outcome = ArenaLightingOutcome.Lost;
                _outcomeUntil = now + ArenaLightingPolicy.BoundOutcomeDuration(
                    Settings.ArenaLightingOutcomeSeconds?.Value
                    ?? ArenaLightingPolicy.DefaultOutcomeSeconds);
                break;
        }
    }

    private static void ObserveRoundTransitions()
    {
        if (!(_match?.Mode is RoundsMode rounds))
            return;

        int number = rounds.Round.Number;
        if (!rounds.InIntermission
            && (_roundsWasIntermission || number != _observedRoundsNumber))
        {
            _fightUntil = Time.time + ArenaLightingPolicy.FightFlashSeconds;
        }

        _observedRoundsNumber = number;
        _roundsWasIntermission = rounds.InIntermission;
    }

    private static void ReadModeTiming(out float countdownRemaining, out float liveRemaining)
    {
        countdownRemaining = -1f;
        liveRemaining = -1f;

        if (_match?.Mode is TacticalMode tactical)
        {
            if (tactical.CurrentPhase == TacticalMode.Phase.Buy)
                countdownRemaining = tactical.PhaseRemaining;
            else if (tactical.CurrentPhase == TacticalMode.Phase.Live)
                liveRemaining = tactical.PhaseRemaining;
            return;
        }

        if (_match?.Mode is RoundsMode rounds)
        {
            if (rounds.InIntermission)
                countdownRemaining = rounds.IntermissionRemaining;
            else
                liveRemaining = rounds.TimeRemaining;
        }
    }

    private static bool TryGetExternalLightingOwner(out string owner)
    {
        owner = null;
        try
        {
            if (Chainloader.PluginInfos.ContainsKey(BlackoutPluginGuid))
            {
                owner = "Blackout";
                return true;
            }

            if (Chainloader.PluginInfos.ContainsKey(GoingDarkPluginGuid))
            {
                owner = "Going Dark";
                return true;
            }
        }
        catch (Exception ex)
        {
            if (!_externalProbeFailureLogged)
            {
                _externalProbeFailureLogged = true;
                Plugin.ModLogger?.LogWarning(
                    $"Arena could not inspect external lighting ownership; continuing with its additive light: {ex.Message}");
            }
        }

        return false;
    }

    private static void Apply(ArenaLightingFrame frame)
    {
        if (!frame.Visible)
        {
            if (_light != null)
            {
                _light.enabled = false;
                _light.intensity = 0f;
            }

            _lastEffect = ArenaLightingEffect.Off;
            return;
        }

        if (!EnsureLight())
            return;

        ArenaLightColor color = frame.Color.Normalized();
        _light.color = new Color(color.Red, color.Green, color.Blue, 1f);
        _light.intensity = frame.Intensity;
        _light.enabled = true;

        if (_lastEffect != frame.Effect)
        {
            Plugin.ModLogger?.LogDebug($"Arena lighting phase: {frame.Effect}.");
            _lastEffect = frame.Effect;
        }
    }

    private static bool EnsureLight()
    {
        if (_light != null)
            return true;
        if (_creationFailureLogged)
            return false;

        GameObject created = null;
        try
        {
            created = new GameObject(LightObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            created.transform.SetParent(_world.transform, false);
            created.transform.localPosition = Vector3.zero;
            created.transform.localRotation = Quaternion.Euler(52f, -34f, 0f);

            var light = created.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            light.cullingMask = ~0;
            light.bounceIntensity = 0f;
            light.intensity = 0f;
            light.enabled = false;

            _lightObject = created;
            _light = light;
            Plugin.ModLogger?.LogInfo(
                "Arena created one runtime directional light for Labs round-state feedback.");
            return true;
        }
        catch (Exception ex)
        {
            if (created != null)
                UnityEngine.Object.Destroy(created);
            _creationFailureLogged = true;
            Plugin.ModLogger?.LogWarning(
                $"Arena phase lighting could not create its owned light and is disabled for this raid: {ex.Message}");
            return false;
        }
    }

    private static ArenaLightColor ToPolicyColor(Color color)
        => new ArenaLightColor(color.r, color.g, color.b).Normalized();
}
