using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Modes;
using ArenaMode.Session;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArenaMode.Audio;

/// <summary>Independent, bounded hit feedback. No gameplay or announcer queue ownership.</summary>
internal static class HitmarkerAudioService
{
    private enum Cue { None, Hit, Headshot, Kill }
    private static readonly string[] Names = { "", "hit", "headshot", "kill" };
    private static readonly HitmarkerPcmClip[] Clips = new HitmarkerPcmClip[4];
    private static readonly ArenaMasterAudioOutput Output = new();
    private static readonly HashSet<string> Reported = new();
    private static ArenaMatch _match;
    private static string _pluginDirectory;
    private static Cue _pending;
    private static float _pendingAt;
    private static int _received, _submitted, _skipped;
    private const float CollectSeconds = 0.04f;
    private const float ExpireSeconds = 0.15f;

    internal static void SetPluginDirectory(string directory) => _pluginDirectory = directory;

    internal static void Attach(ArenaMatch match)
    {
        Detach();
        if (match == null || !ArenaSession.IsActive) return;
        _match = match;
        _received = _submitted = _skipped = 0;
        Reported.Clear();
        // Three tiny bounded reads once per match, never per shot. Reloading here
        // also makes repaired/replaced files available at the next match.
        for (int i = 1; i < Clips.Length; i++)
        {
            try
            {
                if (string.IsNullOrEmpty(_pluginDirectory)) throw new InvalidOperationException("Plugin directory unavailable.");
                Clips[i] = HitmarkerPcmClip.Load(Path.Combine(_pluginDirectory, "ArenaMode", "hitmarkers", Names[i] + ".wav"));
            }
            catch (Exception ex) { Report("load_" + Names[i], ex.Message); }
        }
        ArenaEvents.OnLocalDamageDealt += OnDamage;
        Plugin.ModLogger.LogInfo($"Arena hitmarker audio attached: enabled={Settings.HitmarkerSounds.Value}, "
            + $"volume={Settings.HitmarkerVolume.Value:0.##}, clips={ReadyCount()}/3, route=Arena.Master (independent source).");
    }

    private static bool Current => _match != null && ArenaSession.IsActive
        && ReferenceEquals(_match, ArenaMatchService.Current);
    private static float Volume
    {
        get
        {
            float value = Settings.HitmarkerVolume.Value;
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
        }
    }

    private static void OnDamage(ArenaDamage damage)
    {
        if (!Current || !Settings.HitmarkerSounds.Value || Volume <= 0f
            || damage == null || !damage.AudioEligible) return;
        float now = Time.realtimeSinceStartup;
        // Prefix captured eligibility while the target was alive and the round
        // live. Do not re-test IsComplete here: the lethal hit can end the match
        // synchronously before the damage postfix publishes its kill marker.
        Cue cue = damage.Kill ? Cue.Kill : damage.Headshot ? Cue.Headshot : Cue.Hit;
        if (_pending != Cue.None && now - _pendingAt > ExpireSeconds) _pending = Cue.None;
        if (_pending == Cue.None) _pendingAt = now;
        if (cue > _pending) _pending = cue;
        _received++;
    }

    internal static void Tick()
    {
        if (_match == null) return;
        if (!Current) { Detach(); return; }
        bool enabled = Settings.HitmarkerSounds.Value && Volume > 0f;
        // A transient hit must not resume its old tail after leaving pause.
        // Reset only our source; announcer and native game sources stay alone.
        Output.Tick(enabled && !AudioListener.pause, Volume);
        if (!enabled || AudioListener.pause)
        {
            _pending = Cue.None;
            return;
        }
        if (_pending == Cue.None) return;
        float age = Time.realtimeSinceStartup - _pendingAt;
        if (age < CollectSeconds) return;
        Cue cue = _pending;
        _pending = Cue.None; // Consume before attempting output; never retry an old hit.
        if (age > ExpireSeconds) { _skipped++; return; }
        try
        {
            var cached = Clips[(int)cue];
            if (cached == null) { Report("missing_" + Names[(int)cue], "clip unavailable; retry next match"); return; }
            if (!Output.TryPlay(cached.GetRuntime(), Volume, out string problem))
            { Report("output", problem); return; }
            _submitted++;
            if (_submitted <= 3)
                Plugin.ModLogger.LogInfo($"Arena hitmarker audio play request: {Names[(int)cue]}, volume={Volume:0.##}.");
        }
        catch (Exception ex) { Report("play_" + Names[(int)cue], ex.Message); }
    }

    private static void Report(string key, string problem)
    {
        _skipped++;
        if (Reported.Add(key)) Plugin.ModLogger.LogWarning($"Arena hitmarker audio skipped: {key}, {problem}");
    }

    private static int ReadyCount()
    {
        int ready = 0;
        foreach (var clip in Clips) if (clip != null) ready++;
        return ready;
    }

    internal static void Detach()
    {
        ArenaEvents.OnLocalDamageDealt -= OnDamage;
        bool attached = _match != null;
        _match = null;
        _pending = Cue.None;
        Output.Reset();
        for (int i = 1; i < Clips.Length; i++)
        {
            var clip = Clips[i];
            Clips[i] = null;
            try { clip?.ResetRuntime(); }
            catch (Exception ex) { Report("cleanup", ex.Message); }
        }
        if (attached) Plugin.ModLogger.LogInfo($"Arena hitmarker audio detached: events={_received}, "
            + $"playRequests={_submitted}, skipped={_skipped}.");
    }
}
