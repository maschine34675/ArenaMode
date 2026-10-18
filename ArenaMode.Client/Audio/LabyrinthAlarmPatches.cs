using ArenaMode.Config;
using ArenaMode.Session;
using Audio.AudioCulling;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.GameTriggers;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Audio;

/// <summary>
/// Silences only Labyrinth's verified center siren. The four alarm_enter
/// handlers share one SyncLoopSoundPlayer; its InitializeSource is the last
/// audio-only boundary before PlayDelayed. Keep the coroutine, StateChanged,
/// trigger subscriptions and bot events running exactly as vanilla does.
/// No source mute/volume, mixer, clip asset or map bundle is modified.
/// </summary>
internal static class LabyrinthAlarmPatches
{
    internal const string ScenePath = "Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity";
    private static readonly ArenaLabyrinthAlarmAlarmSourcePatch Patch = new();
    private static bool _enabled;
    private static bool _patchAttempted;
    private static bool _reportedFailure;
    private static GameWorld _scannedWorld;

    internal static void Enable()
    {
        if (_enabled)
            return;
        try
        {
            _patchAttempted = true;
            Patch.Enable();
            _enabled = true;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    internal static void Shutdown()
    {
        _enabled = false;
        _scannedWorld = null;
        if (!_patchAttempted)
            return;
        try
        {
            Patch.Disable();
            _patchAttempted = false;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private static bool TryGetWorld(out GameWorld world)
    {
        world = null;
        if (!ArenaSession.IsActive || !_enabled
            || Settings.MuteLabyrinthAlarm?.Value != true
            || !Singleton<GameWorld>.Instantiated
            || !Singleton<AbstractGame>.Instantiated)
            return false;

        world = Singleton<GameWorld>.Instance;
        return world != null
            && string.Equals(world.LocationId, "labyrinth", StringComparison.OrdinalIgnoreCase)
            && ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, world);
    }

    internal static bool IsAlarmPlayer(SyncLoopSoundPlayer player)
        => player != null
           && player.gameObject.scene.IsValid()
           && player.gameObject.scene.isLoaded
           && string.Equals(player.gameObject.scene.path, ScenePath, StringComparison.Ordinal)
           && string.Equals(player.gameObject.name, "alarm_sound_root", StringComparison.Ordinal)
           && player._clip != null
           && string.Equals(player._clip.name, "alarm_oneshot", StringComparison.Ordinal)
           && !player._playOnAwake
           && player._useSoundDelayCalculation;

    private static bool IsAlarmSource(SyncLoopSoundPlayer player, AudioSource source)
        => source != null
           && source.gameObject.scene == player.gameObject.scene
           && source.transform.IsChildOf(player.transform)
           && ReferenceEquals(source.clip, player._clip)
           && player._sources != null
           && Array.IndexOf(player._sources, source) >= 0;

    internal static bool AllowInitializeSource(SyncLoopSoundPlayer player, AudioSource source)
    {
        if (!ArenaSession.IsActive)
            return true;
        try
        {
            if (!TryGetWorld(out _) || !IsAlarmPlayer(player) || !IsAlarmSource(player, source))
                return true;

            // Stop also cancels an already scheduled PlayDelayed. This touches
            // only a verified source belonging to this particular siren.
            source.Stop();
            return false;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return true;
        }
    }

    internal static void Tick()
    {
        try
        {
            if (!TryGetWorld(out var world))
            {
                _scannedWorld = null;
                return;
            }
            if (ReferenceEquals(_scannedWorld, world))
                return;
            _scannedWorld = world;

            // One registry pass per raid or off->on setting edge. A player can
            // touch a trigger while Arena's start handshake is still pending;
            // cancel that existing/scheduled audio as soon as Arena is active.
            // LocationScene does NOT register SyncLoopSoundPlayer by its type.
            var players = new HashSet<SyncLoopSoundPlayer>();
            int stopped = 0;
            foreach (var entity in LocationScene.GetAll<ITriggerEntity>())
            {
                if (!(entity is SyncableLoopSoundPlayer handler) || handler == null
                    || handler.gameObject.name != "INTERACTIVE_Labyrinth_Boss_Alarm"
                    || !IsAlarmTrigger(handler._playTriggerId)
                    || !string.IsNullOrEmpty(handler._stopTriggerId)
                    || !IsAlarmPlayer(handler._loopSoundPlayer)
                    || !players.Add(handler._loopSoundPlayer))
                    continue;

                var player = handler._loopSoundPlayer;
                if (player._sources == null)
                    continue;
                foreach (var source in player._sources)
                {
                    if (!IsAlarmSource(player, source))
                        continue;
                    source.Stop();
                    stopped++;
                }
            }
            Plugin.ModLogger?.LogDebug(
                $"Arena Labyrinth alarm suppression active: {players.Count} verified player(s), {stopped} source(s); other audio and triggers unchanged.");
            if (stopped == 0)
                Plugin.ModLogger?.LogWarning("Arena Labyrinth alarm sources were not found in the scene registry; the guarded playback hook remains active.");
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private static bool IsAlarmTrigger(string id)
        => id == "alarm_enter_3508252692" || id == "alarm_enter_4274982985"
           || id == "alarm_enter_336915922" || id == "alarm_enter_3861355289";

    private static void ReportFailure(Exception ex)
    {
        if (_reportedFailure)
            return;
        _reportedFailure = true;
        Plugin.ModLogger?.LogWarning($"Arena Labyrinth alarm suppression failed open: {ex.Message}");
    }

    private sealed class ArenaLabyrinthAlarmAlarmSourcePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(SyncLoopSoundPlayer),
                nameof(SyncLoopSoundPlayer.InitializeSource), new[] { typeof(AudioSource) });

        [PatchPrefix]
        private static bool Prefix(SyncLoopSoundPlayer __instance, AudioSource source)
            => AllowInitializeSource(__instance, source);
    }
}
