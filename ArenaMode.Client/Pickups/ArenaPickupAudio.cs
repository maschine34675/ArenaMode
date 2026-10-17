using ArenaMode.Audio;
using EFT;
using System;
using System.IO;
using UnityEngine;

namespace ArenaMode.Pickups;

/// <summary>One owned output, separate from hitmarkers/announcer and native UI volume.</summary>
internal static class ArenaPickupAudio
{
    private static readonly ArenaMasterAudioOutput Output = new();
    private static HitmarkerPcmClip _clip;
    private static bool _loadAttempted, _reported;
    internal static int PlayRequests { get; private set; }

    private static float Volume => ArenaPickupSettings.SoundVolume?.Value ?? 0f;

    // Called only after the appearance's reservation is successfully completed.
    // Native inventory completion may be asynchronous; recheck the physical actor.
    internal static void Collected(Player player)
    {
        if (player == null || player.IsAI || !player.IsYourPlayer
            || !ArenaPickupRuntime.IsLiveActor(player) || AudioListener.pause || Time.timeScale <= 0f
            || !(Volume > 0f)) return;
        try
        {
            if (!_loadAttempted)
            {
                _loadAttempted = true;
                _clip = HitmarkerPcmClip.Load(Path.Combine(ArenaAssetPaths.FromPluginFile(Plugin.Instance.Info.Location),
                    "pickups", "collect.wav"));
            }
            if (_clip == null) return;
            if (!Output.TryPlay(_clip.GetRuntime(), Volume, out string problem)) { Report(problem); return; }
            PlayRequests++;
            if (PlayRequests <= 3) Plugin.ModLogger.LogDebug($"Arena pickup audio play request {PlayRequests}: volume={Volume:0.##}; Arena.Master.");
        }
        catch (Exception ex) { Report(ex.GetBaseException().Message); }
    }

    internal static void Tick() => Output.Tick(!AudioListener.pause && Time.timeScale > 0f && Volume > 0f, Volume);

    private static void Report(string problem)
    {
        if (_reported) return;
        _reported = true;
        Plugin.ModLogger.LogWarning("Arena pickup audio unavailable: " + problem);
    }

    internal static void Reset()
    {
        Output.Reset();
        var clip = _clip;
        _clip = null;
        _loadAttempted = _reported = false;
        PlayRequests = 0;
        try { clip?.ResetRuntime(); }
        catch (Exception ex) { Report(ex.GetBaseException().Message); }
    }
}
