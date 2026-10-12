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
    private readonly List<Material> _materials = new();
    private readonly List<Texture2D> _textures = new();
    private readonly List<AudioClip> _clips = new();
    private AudioSource _loop, _transient;
    private AudioClip _end;
    private GUISounds _audioHost;
    private AudioMixerGroup _audioGroup;
    private enum Layer { Base, Flame, Ember }
    private ParticleSystem _base, _embers;
    private readonly ParticleSystem[] _flames = new ParticleSystem[3];
    private Light _light;
    private readonly IReadOnlyList<Vector3> _cells;
    private readonly System.Random _random = new();
    private float _nextBase, _nextFlame, _nextEmber, _lightPhase;
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
            var baseMaterial = MakeMaterial(shader, -1);
            _base = MakeSystem("Fire base", 384, baseMaterial, Layer.Base);
            // Separate original shapes avoid texture-atlas bleeding and animated frame jumps.
            // 384 + 3*96 + 32 = 704 particles/fire, below the original prototype's 768.
            for (int i = 0; i < _flames.Length; i++)
                _flames[i] = MakeSystem("Flame tongue " + i, 96, MakeMaterial(shader, i), Layer.Flame);
            _embers = MakeSystem("Embers", 32, baseMaterial, Layer.Ember);
            _lightPhase = Between(0f, 100f);
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
    private Material MakeMaterial(Shader shader, int variant)
    {
        bool softBase = variant < 0;
        int size = softBase ? 64 : 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = softBase ? "Arena soft fire base" : "Arena original flame tongue " + variant,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        _textures.Add(texture); // constructor cleanup owns every partial allocation
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float h = y / (float)(size - 1), u = x / (float)(size - 1) * 2 - 1;
            float alpha;
            if (softBase)
            {
                float v = h * 2 - 1;
                float core = Mathf.Clamp01(1 - u * u - v * v);
                alpha = core * core;
            }
            else
            {
                float center = (.12f + variant * .055f) * Mathf.Sin(h * (8f + variant * 2f) + variant) * h;
                float width = (.72f - variant * .055f) * Mathf.Pow(1 - h, .6f + variant * .12f)
                    + .025f + .07f * Mathf.Sin(h * 17f + variant * 2f) * h * (1 - h);
                float side = Mathf.Clamp01(1 - Mathf.Abs(u - center) / width);
                alpha = side * side * Mathf.Clamp01(h * 14) * Mathf.Clamp01((1 - h) * 9);
            }
            pixels[y * size + x] = new Color(1, 1, 1, alpha);
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        var material = new Material(shader);
        _materials.Add(material);
        material.name = texture.name; material.mainTexture = texture;
        return material;
    }
    private ParticleSystem MakeSystem(string name, int cap, Material material, Layer layer)
    {
        bool softBase = layer == Layer.Base, ember = layer == Layer.Ember;
        var go = new GameObject(name); go.transform.SetParent(_root.transform, false);
        var particles = go.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = cap; main.startLifetime = .45f; main.startSpeed = 0; main.startSize3D = true;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var colors = particles.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, softBase ? .88f : .8f, .2f), 0),
            new GradientColorKey(new Color(1f, .28f, .03f), .45f), new GradientColorKey(new Color(.45f, .1f, .015f), 1) },
            new[] { new GradientAlphaKey(softBase ? .72f : .15f, 0),
                new GradientAlphaKey(ember ? .85f : .9f, .12f),
                new GradientAlphaKey(softBase ? .68f : .6f, .6f), new GradientAlphaKey(0, 1) });
        colors.color = gradient;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
            new Keyframe(0, .85f), new Keyframe(.2f, 1), new Keyframe(1, ember ? .2f : .65f)));
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = layer == Layer.Flame ? ParticleSystemRenderMode.VerticalBillboard : ParticleSystemRenderMode.Billboard;
        if (layer == Layer.Flame) renderer.pivot = new Vector3(0, -.5f, 0); // tongue grows above its sampled floor
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        particles.Play(); return particles;
    }
    private float Between(float a, float b) => a + (b - a) * (float)_random.NextDouble();
    internal void Tick(float remaining)
    {
        if (_root == null) return;
        float fade = Mathf.Clamp01(remaining / .55f);
        _light.intensity = Mathf.Lerp(1f, 1.45f, Mathf.PerlinNoise(_lightPhase, Time.time * 5.3f)) * fade;
        TickAudio(fade, remaining);
        if (Time.time >= _nextBase)
        {
            _nextBase = Time.time + .1f; // no accumulated catch-up burst after a hitch
            if (remaining > .1f) foreach (var cell in _cells)
            {
                _base.Emit(new ParticleSystem.EmitParams {
                    position = cell + new Vector3(Between(-.13f, .13f), Between(.15f, .22f), Between(-.13f, .13f)),
                    velocity = new Vector3(Between(-.14f, .14f), Between(.3f, .6f), Between(-.14f, .14f)),
                    startSize3D = new Vector3(Between(.72f, 1f), Between(.42f, .65f), 1) * (.65f + .35f * fade),
                    startLifetime = Mathf.Min(remaining, Between(.55f, .7f)),
                    rotation = Between(-16f, 16f), startColor = new Color(1, 1, 1, fade)
                }, 1);
            }
        }
        if (Time.time >= _nextFlame)
        {
            _nextFlame = Time.time + Between(.16f, .2f);
            if (remaining > .12f) foreach (var cell in _cells)
            {
                _flames[_random.Next(_flames.Length)].Emit(new ParticleSystem.EmitParams {
                    position = cell + new Vector3(Between(-.18f, .18f), .13f, Between(-.18f, .18f)),
                    velocity = new Vector3(Between(-.22f, .22f), Between(.65f, 1.15f), Between(-.22f, .22f)),
                    startSize3D = new Vector3(Between(.38f, .65f), Between(.7f, 1.2f), 1) * (.45f + .55f * fade),
                    startLifetime = Mathf.Min(remaining, Between(.48f, .65f)), rotation = Between(-12f, 12f),
                    startColor = new Color(1, Between(.86f, 1f), Between(.85f, 1f), fade)
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
        foreach (var material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        foreach (var texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        _materials.Clear(); _textures.Clear();
        _root = null; _base = null; _embers = null;
        Array.Clear(_flames, 0, _flames.Length);
    }
}
