using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT.UI;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

namespace ArenaMode.Combat.Fire;

/// <summary>Original procedural art/audio; owns no pooled EFT assets.</summary>
internal sealed class ArenaFireView : IDisposable
{
    private GameObject _root;
    private Material _material;
    private Texture2D _texture;
    private readonly List<AudioClip> _clips = new();
    private AudioSource _loop, _transient;
    private AudioClip _end;
    private GUISounds _audioHost;
    private AudioMixerGroup _audioGroup;
    private ParticleSystem _flames, _embers;
    private Light _light;
    private readonly IReadOnlyList<Vector3> _cells;
    private readonly System.Random _random = new();
    private float _nextFlame, _nextEmber;
    private bool _ending;

    internal ArenaFireView(IReadOnlyList<Vector3> cells)
    {
        if (cells == null || cells.Count == 0) throw new ArgumentException("Fire needs ground cells", nameof(cells));
        _cells = cells;
        try
        {
            _root = new GameObject("Arena ground fire"); _root.transform.position = cells[0];
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Fire sprite shader unavailable");
            _texture = FlameTexture();
            _material = new Material(shader) { name = "Arena original flame", mainTexture = _texture };
            _flames = MakeSystem("Flames", 384, false); _embers = MakeSystem("Embers", 32, true);
            var lamp = new GameObject("Fire light"); lamp.transform.SetParent(_root.transform, false);
            lamp.transform.localPosition = Vector3.up * .45f;
            _light = lamp.AddComponent<Light>(); _light.color = new Color(1f, .35f, .07f);
            _light.range = 5f; _light.shadows = LightShadows.None;
            // A missing audio device/mixer cannot suppress the independently owned area.
            try { StartAudio(); }
            catch (Exception ex) { StopAudio(); Plugin.ModLogger.LogWarning($"Arena fire audio unavailable: {ex.Message}"); }
            Tick(ArenaFirePolicy.Lifetime);
        }
        catch { Dispose(); throw; }
    }
    private Texture2D FlameTexture()
    {
        const int size = 128;
        _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "Arena original flame tongue", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var texture = _texture; // constructor cleanup owns it even if upload fails
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float h = y / (float)(size - 1), u = x / (float)(size - 1) * 2 - 1;
            float center = .13f * Mathf.Sin(h * 9f) * h;
            float width = .72f * Mathf.Pow(1 - h, .65f) + .025f;
            float side = Mathf.Clamp01(1 - Mathf.Abs(u - center) / width);
            float alpha = side * side * Mathf.Clamp01(h * 14) * Mathf.Clamp01((1 - h) * 9);
            pixels[y * size + x] = new Color(1, 1, 1, alpha);
        }
        texture.SetPixels(pixels); texture.Apply(false, true); return texture;
    }
    private ParticleSystem MakeSystem(string name, int cap, bool ember)
    {
        var go = new GameObject(name); go.transform.SetParent(_root.transform, false);
        var particles = go.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = cap; main.startLifetime = .45f; main.startSpeed = 0; main.startSize3D = true;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var colors = particles.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, .76f, .2f), 0),
            new GradientColorKey(new Color(1f, .28f, .03f), .45f), new GradientColorKey(new Color(.45f, .1f, .015f), 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(ember ? .85f : .78f, .12f),
                new GradientAlphaKey(.45f, .55f), new GradientAlphaKey(0, 1) });
        colors.color = gradient;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, .2f));
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = _material;
        renderer.renderMode = ember ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.VerticalBillboard;
        if (!ember) renderer.pivot = new Vector3(0, -.5f, 0); // flame grows above its sampled floor
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        particles.Play(); return particles;
    }
    private float Between(float a, float b) => a + (b - a) * (float)_random.NextDouble();
    internal void Tick(float remaining)
    {
        if (_root == null) return;
        float fade = Mathf.Clamp01(remaining / .55f);
        _light.intensity = (1.2f + .12f * Mathf.Sin(Time.time * 21f) + .08f * Mathf.Sin(Time.time * 13f)) * fade;
        TickAudio(fade, remaining);
        if (Time.time >= _nextFlame)
        {
            _nextFlame = Time.time + .12f;
            if (remaining > .12f) foreach (var cell in _cells)
            {
                _flames.Emit(new ParticleSystem.EmitParams {
                    position = cell + new Vector3(Between(-.23f, .23f), .16f, Between(-.23f, .23f)),
                    velocity = new Vector3(Between(-.08f, .08f), Between(.35f, .7f), Between(-.08f, .08f)),
                    startSize3D = new Vector3(Between(.3f, .5f), Between(.65f, 1.05f), 1) * (.45f + .55f * fade),
                    startLifetime = Mathf.Min(remaining, Between(.32f, .5f)), startColor = new Color(1, 1, 1, fade)
                }, 1);
            }
        }
        if (Time.time >= _nextEmber && remaining > .5f)
        {
            _nextEmber = Time.time + .16f;
            _embers.Emit(new ParticleSystem.EmitParams {
                position = _cells[_random.Next(_cells.Count)] + Vector3.up * .2f,
                velocity = new Vector3(Between(-.25f, .25f), Between(.8f, 1.4f), Between(-.25f, .25f)),
                startSize3D = new Vector3(.035f, .075f, 1), startLifetime = Mathf.Min(remaining, .7f), startColor = Color.white
            }, 1);
        }
    }
    private AudioClip Clip(string name, float[] samples)
    {
        var clip = AudioClip.Create(name, samples.Length, 1, ArenaFireSound.Rate, false);
        if (clip == null) throw new InvalidOperationException("Fire clip creation failed");
        _clips.Add(clip);
        if (!clip.SetData(samples, 0)) throw new InvalidOperationException("Fire PCM upload failed"); return clip;
    }
    private void StartAudio()
    {
        _audioHost = Singleton<GUISounds>.Instantiated ? Singleton<GUISounds>.Instance : null;
        if (_audioHost == null || !_audioHost.isActiveAndEnabled || !_audioHost.gameObject.activeInHierarchy
            || _audioHost.MasterMixer == null) throw new InvalidOperationException("Native Master mixer unavailable");
        foreach (var candidate in _audioHost.MasterMixer.FindMatchingGroups("Master"))
        {
            if (candidate == null || !string.Equals(candidate.name, "Master", StringComparison.Ordinal)) continue;
            if (_audioGroup != null || !ReferenceEquals(candidate.audioMixer, _audioHost.MasterMixer))
                throw new InvalidOperationException("Native Master mixer ambiguous");
            _audioGroup = candidate;
        }
        if (_audioGroup == null) throw new InvalidOperationException("Native Master group missing");
        _loop = Source(true); _transient = Source(false);
        _loop.clip = Clip("Arena original burn", ArenaFireSound.Burn());
        _end = Clip("Arena original extinguish", ArenaFireSound.Extinguish());
        var start = Clip("Arena original glass and ignition", ArenaFireSound.BreakAndIgnite());
        _loop.volume = .7f; _transient.volume = .8f; _loop.Play(); _transient.PlayOneShot(start);
    }
    private AudioSource Source(bool loop)
    {
        var source = _root.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = loop;
        source.spatialBlend = 1; source.spatialize = false; source.dopplerLevel = 0;
        source.minDistance = 1; source.maxDistance = 18; source.rolloffMode = AudioRolloffMode.Linear;
        source.outputAudioMixerGroup = _audioGroup; source.ignoreListenerPause = false; source.ignoreListenerVolume = false;
        return source;
    }
    private void TickAudio(float fade, float remaining)
    {
        if (_loop == null) return;
        try
        {
            var current = Singleton<GUISounds>.Instantiated ? Singleton<GUISounds>.Instance : null;
            if (current == null || !ReferenceEquals(current, _audioHost) || !current.isActiveAndEnabled
                || !current.gameObject.activeInHierarchy || _audioGroup == null
                || !ReferenceEquals(_audioGroup.audioMixer, current.MasterMixer)
                || !ReferenceEquals(_loop.outputAudioMixerGroup, _audioGroup)
                || _transient == null || !ReferenceEquals(_transient.outputAudioMixerGroup, _audioGroup))
            { StopAudio(); return; }
            _loop.volume = .7f * fade;
            if (!_ending && remaining <= .5f) { _ending = true; _transient.PlayOneShot(_end, .65f); }
        }
        catch { StopAudio(); }
    }
    private void StopAudio()
    {
        StopSource(_loop); StopSource(_transient);
        _loop = null; _transient = null;
        foreach (var clip in _clips) if (clip != null) UnityEngine.Object.Destroy(clip);
        _clips.Clear(); _end = null; _audioHost = null; _audioGroup = null;
    }
    private static void StopSource(AudioSource source)
    {
        if (source == null) return;
        try { source.Stop(); }
        catch (Exception) { } // still release other owned sources/resources
        finally { UnityEngine.Object.Destroy(source); }
    }
    public void Dispose()
    {
        StopAudio();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_texture != null) UnityEngine.Object.Destroy(_texture);
        _root = null; _material = null; _texture = null;
    }
}
