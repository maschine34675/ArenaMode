using Comfort.Common;
using EFT.UI;
using System;
using UnityEngine;
using UnityEngine.Audio;

namespace ArenaMode.Audio;

/// <summary>
/// One Arena-owned 2D source on EFT's persistent application host. Routing to
/// the exact Master root bypasses UIVolume while retaining master/listener mute.
/// Never changes a shared source, mixer parameter or listener setting.
/// </summary>
internal static class ArenaAnnouncerOutput
{
    private static readonly ArenaMasterAudioOutput Output = new();
    internal static bool TryPlay(AudioClip clip, float volume, out string problem)
        => Output.TryPlay(clip, volume, out problem);
    internal static void Tick(bool enabled, float volume) => Output.Tick(enabled, volume);
    internal static void Reset() => Output.Reset();
}

/// <summary>Each instance owns its source; short impacts cannot stop announcer speech.</summary>
internal sealed class ArenaMasterAudioOutput
{
    private GUISounds _host;
    private AudioMixer _mixer;
    private AudioMixerGroup _group;
    private AudioSource _source;

    internal bool TryPlay(AudioClip clip, float volume, out string problem)
    {
        problem = null;
        try
        {
            var host = Singleton<GUISounds>.Instantiated ? Singleton<GUISounds>.Instance : null;
            if (host == null || !host.isActiveAndEnabled || !host.gameObject.activeInHierarchy
                || host.MasterMixer == null)
            {
                Reset();
                problem = "Arena Master output: native application host/mixer unavailable";
                return false;
            }

            if (_source == null || !ReferenceEquals(_host, host)
                || !ReferenceEquals(_mixer, host.MasterMixer) || !ValidBinding())
            {
                Reset();
                AudioMixerGroup master = null;
                foreach (var candidate in host.MasterMixer.FindMatchingGroups("Master"))
                {
                    if (candidate == null || !string.Equals(candidate.name, "Master", StringComparison.Ordinal))
                        continue;
                    if (!ReferenceEquals(candidate.audioMixer, host.MasterMixer) || master != null)
                    {
                        problem = "Arena Master output: ambiguous or foreign Master group";
                        return false;
                    }
                    master = candidate;
                }
                if (master == null)
                {
                    problem = "Arena Master output: exact Master group missing";
                    return false;
                }

                _host = host;
                _mixer = host.MasterMixer;
                _group = master;
                _source = host.gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.loop = false;
                _source.spatialBlend = 0f;
                _source.spatialize = false;
                _source.dopplerLevel = 0f;
                _source.bypassReverbZones = true;
                _source.priority = 0;
                _source.ignoreListenerPause = false;
                _source.ignoreListenerVolume = false;
                _source.outputAudioMixerGroup = master;
            }

            // The service serialized admission before reaching this owned output.
            _source.Stop();
            _source.clip = clip;
            _source.volume = Volume(volume);
            _source.Play();
            return true; // request submitted; never claim this proves audibility
        }
        catch (Exception ex)
        {
            Reset();
            problem = "Arena Master output: " + ex.Message;
            return false;
        }
    }

    internal void Tick(bool enabled, float volume)
    {
        try
        {
            if (_source == null) return;
            var host = Singleton<GUISounds>.Instantiated ? Singleton<GUISounds>.Instance : null;
            if (!enabled || host == null || !ReferenceEquals(host, _host)
                || !ReferenceEquals(host.MasterMixer, _mixer) || !ValidBinding())
            {
                Reset();
                return;
            }
            _source.volume = Volume(volume); // F12 changes also affect the current phrase
        }
        catch (Exception ex)
        {
            Reset();
            Plugin.ModLogger.LogWarning($"Arena Master output update failed: {ex.Message}");
        }
    }

    private bool ValidBinding()
        => _host != null && _host.isActiveAndEnabled && _host.gameObject.activeInHierarchy
            && _source != null && _source.enabled && _group != null && _mixer != null
            && string.Equals(_group.name, "Master", StringComparison.Ordinal)
            && ReferenceEquals(_group.audioMixer, _mixer)
            && ReferenceEquals(_source.gameObject, _host.gameObject)
            && ReferenceEquals(_source.outputAudioMixerGroup, _group);

    private static float Volume(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(1f, value));

    internal void Reset()
    {
        var source = _source;
        _source = null;
        _host = null;
        _group = null;
        _mixer = null;
        if (source == null) return;
        try { source.Stop(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena owned audio stop failed: {ex.Message}"); }
        try { UnityEngine.Object.Destroy(source); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena owned audio cleanup failed: {ex.Message}"); }
    }
}
