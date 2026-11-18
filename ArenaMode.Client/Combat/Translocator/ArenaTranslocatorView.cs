using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT.UI;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

namespace ArenaMode.Combat.Translocator;

/// <summary>Small original meshes and tones. No imported UT assets or shared EFT materials.</summary>
internal sealed class ArenaTranslocatorView : IDisposable
{
    private GameObject _root, _body, _pulse;
    private Light _light;
    private readonly List<Material> _materials = new();
    private AudioSource _audio;
    private AudioClip _launch, _teleport;
    private GUISounds _audioHost;
    private AudioMixerGroup _audioGroup;
    private float _pulseStarted = float.NegativeInfinity;

    internal ArenaTranslocatorView()
    {
        try
        {
            _root = new GameObject("Arena Translocator effects"); _root.layer = 2;
            _body = new GameObject("Arena Translocator disc"); _body.layer = 2;
            _body.transform.SetParent(_root.transform, false);
            var solid = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var unlit = Shader.Find("Sprites/Default");
            if (solid == null || unlit == null) throw new InvalidOperationException("Translocator shaders unavailable");
            var metal = Material(solid, new Color(.10f, .14f, .18f));
            if (metal.HasProperty("_Metallic")) metal.SetFloat("_Metallic", .7f);
            var glow = Material(unlit, new Color(.12f, .85f, 1f));
            Part(_body.transform, "Disc shell", PrimitiveType.Cylinder,
                Vector3.zero, new Vector3(.16f, .016f, .16f), metal);
            Part(_body.transform, "Disc core", PrimitiveType.Cylinder,
                Vector3.up * .019f, new Vector3(.105f, .006f, .105f), glow);
            Ring(_body.transform, "Disc rim", .082f, .005f, .012f, glow);
            var lamp = new GameObject("Disc glow"); lamp.layer = 2; lamp.transform.SetParent(_body.transform, false);
            lamp.transform.localPosition = Vector3.up * .07f;
            _light = lamp.AddComponent<Light>(); _light.color = new Color(.12f, .8f, 1f);
            _light.range = 1.8f; _light.intensity = .55f; _light.shadows = LightShadows.None;
            _pulse = new GameObject("Translocation ring"); _pulse.layer = 2;
            _pulse.transform.SetParent(_root.transform, false);
            Ring(_pulse.transform, "Arrival", .35f, .03f, .08f, glow);
            _body.SetActive(false); _pulse.SetActive(false);
            try { TryAudio(); }
            catch (Exception ex) { Plugin.ModLogger.LogDebug("Translocator audio unavailable: " + ex.Message); }
        }
        catch { Dispose(); throw; }
    }

    private Material Material(Shader shader, Color color)
    {
        var result = new Material(shader) { name = "Arena original Translocator material", color = color };
        _materials.Add(result); return result;
    }

    private static void Part(Transform parent, string name, PrimitiveType kind, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(kind); go.name = name; go.layer = 2;
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        var collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
        var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }

    private static void Ring(Transform parent, string name, float radius, float width, float height, Material material)
    {
        var go = new GameObject(name); go.layer = 2; go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true;
        line.positionCount = 32; line.startWidth = line.endWidth = width; line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        for (int i = 0; i < 32; i++)
        {
            float angle = i * Mathf.PI * 2f / 32;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius));
        }
    }

    internal void Tick(ArenaTranslocatorDisc disc)
    {
        if (_root == null) return;
        bool visible = disc != null && disc.State != ArenaTranslocatorDisc.Flight.Lost;
        _body.SetActive(visible);
        if (visible)
        {
            _body.transform.position = ArenaTranslocatorGeometry.Unity(disc.Position);
            _body.transform.rotation = Quaternion.FromToRotation(Vector3.up,
                ArenaTranslocatorGeometry.Unity(disc.SurfaceNormal)) * Quaternion.Euler(0, Time.time * 100f, 0);
            _light.intensity = .5f + .12f * Mathf.Sin(Time.time * 5f);
        }
        float age = Time.time - _pulseStarted;
        _pulse.SetActive(age >= 0f && age < .32f);
        if (_pulse.activeSelf) _pulse.transform.localScale = Vector3.one * (1f + age * 5f);
    }

    internal void Pulse(Vector3 position)
    {
        if (_pulse == null) return;
        _pulse.transform.position = position; _pulseStarted = Time.time;
        _pulse.transform.localScale = Vector3.one; _pulse.SetActive(true);
    }

    private void TryAudio()
    {
        _audioHost = Singleton<GUISounds>.Instantiated ? Singleton<GUISounds>.Instance : null;
        if (_audioHost == null || !_audioHost.isActiveAndEnabled || _audioHost.MasterMixer == null) return;
        foreach (var group in _audioHost.MasterMixer.FindMatchingGroups("Master"))
        {
            if (group == null || group.name != "Master") continue;
            if (_audioGroup != null || !ReferenceEquals(group.audioMixer, _audioHost.MasterMixer)) { _audioGroup = null; return; }
            _audioGroup = group;
        }
        if (_audioGroup == null) return;
        _audio = _root.AddComponent<AudioSource>(); _audio.playOnAwake = false; _audio.loop = false;
        _audio.outputAudioMixerGroup = _audioGroup; _audio.spatialBlend = 0f; _audio.volume = .22f;
        _audio.ignoreListenerPause = false; _audio.ignoreListenerVolume = false;
        _launch = Tone("Arena original translocator launch", .09f, 520f, 1250f);
        _teleport = Tone("Arena original translocation", .19f, 1450f, 360f);
    }

    private static AudioClip Tone(string name, float seconds, float from, float to)
    {
        const int rate = 22050;
        var samples = new float[(int)(rate * seconds)]; double phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)samples.Length;
            phase += 2d * Math.PI * (from + (to - from) * t) / rate;
            samples[i] = (float)Math.Sin(phase) * Mathf.Sin(Mathf.PI * t) * .4f;
        }
        var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
        try { clip.SetData(samples, 0); return clip; }
        catch { UnityEngine.Object.Destroy(clip); throw; }
    }

    internal void Cue(bool teleport)
    {
        if (_audio == null || _audioGroup == null || _audioHost == null
            || !Singleton<GUISounds>.Instantiated || !ReferenceEquals(Singleton<GUISounds>.Instance, _audioHost)
            || !_audioHost.isActiveAndEnabled || !_audioHost.gameObject.activeInHierarchy
            || !ReferenceEquals(_audioGroup.audioMixer, _audioHost.MasterMixer)) return;
        var clip = teleport ? _teleport : _launch;
        if (clip != null) _audio.PlayOneShot(clip);
    }

    public void Dispose()
    {
        try { if (_audio != null) _audio.Stop(); }
        catch { /* Continue releasing visuals and owned clips after native teardown. */ }
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_launch != null) UnityEngine.Object.Destroy(_launch);
        if (_teleport != null) UnityEngine.Object.Destroy(_teleport);
        foreach (var material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        _materials.Clear(); _root = _body = _pulse = null; _audio = null;
        _launch = _teleport = null; _audioHost = null; _audioGroup = null;
    }
}
