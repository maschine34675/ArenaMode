using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaMode.Audio;

/// <summary>
/// Announcer stingers: .ogg files from &lt;plugin dir&gt;/ArenaMode/sounds keyed by
/// filename (callout label lowercased, spaces to underscores — "double_kill.ogg").
/// Loading is async at plugin start (bullettime pattern) and the whole service
/// no-ops silently when the folder is missing — sounds are optional content.
/// </summary>
internal static class AnnouncerService
{
    private static readonly Dictionary<string, AudioClip> Clips =
        new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

    private static ArenaMatch _match;
    private static bool _loadStarted;
    private static string _pluginDirectory;

    public static void SetPluginDirectory(string pluginDirectory)
        => _pluginDirectory = pluginDirectory;

    /// <summary>
    /// Lazy first-use load at match start — the proven in-raid context for
    /// UnityWebRequest awaits (chainloader-time sync-context behavior is murky).
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loadStarted || string.IsNullOrEmpty(_pluginDirectory))
            return;

        _loadStarted = true;
        LoadAsync(_pluginDirectory);
    }

    private static async void LoadAsync(string pluginDirectory)
    {
        try
        {
            var soundsDir = Path.Combine(pluginDirectory, "ArenaMode", "sounds");
            if (!Directory.Exists(soundsDir))
                return;

            foreach (var file in Directory.GetFiles(soundsDir, "*.ogg"))
            {
                var clip = await LoadClip(file);
                if (clip != null)
                    Clips[Path.GetFileNameWithoutExtension(file)] = clip;
            }

            if (Clips.Count > 0)
                Plugin.ModLogger.LogInfo($"Arena announcer: {Clips.Count} clip(s) loaded.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena announcer load failed: {ex.Message}");
        }
    }

    public static void Attach(ArenaMatch match)
    {
        Detach();
        if (match == null || Clips.Count == 0)
            return;

        _match = match;
        match.OnCallout += OnCallout;
    }

    public static void Detach()
    {
        if (_match == null)
            return;

        _match.OnCallout -= OnCallout;
        _match = null;
    }

    private static void OnCallout(string label)
    {
        if (!Settings.AnnouncerEnabled.Value || string.IsNullOrEmpty(label))
            return;

        Play(label.ToLowerInvariant().Replace(' ', '_'));
    }

    private static void Play(string key)
    {
        try
        {
            if (!Clips.TryGetValue(key, out var clip) || clip == null)
                return;

            if (!Singleton<GUISounds>.Instantiated)
                return;

            // Default routing = the persistent UI mixer group (obeys the Interface
            // volume slider); single:true stops the previous stinger so overlapping
            // streak calls never stack. commonUiSound would reroute in-raid to a
            // fixed low volume — deliberately not used.
            Singleton<GUISounds>.Instance.PlaySound(clip, single: true, commonUiSound: false,
                volume: Settings.AnnouncerVolume.Value);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena announcer play failed: {ex.Message}");
        }
    }

    private static async Task<AudioClip> LoadClip(string path)
    {
        using (var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Plugin.ModLogger.LogWarning($"Arena announcer clip failed: {path}: {request.error}");
                return null;
            }

            return DownloadHandlerAudioClip.GetContent(request);
        }
    }
}
